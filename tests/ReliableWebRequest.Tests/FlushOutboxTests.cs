using NUnit.Framework;

namespace ReliableWebRequest.Tests
{
    [TestFixture]
    public sealed class FlushOutboxTests
    {
        [Test] // S11a, S11, W3, W4
        public async Task Flush_OrdersByCreation_RemovesFinalOutcomesAndUpdatesOthers()
        {
            var f = new Fixture();
            f.Seed("retry", 2, createdSeconds: -20);
            f.Seed("success", 1, createdSeconds: -40);
            f.Seed("keep", 3, createdSeconds: -10);
            f.Seed("reject", 1, createdSeconds: -30);
            f.Transport.Respond(200); f.Transport.Respond(400); f.Transport.Respond(503); f.Transport.Respond(202);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(result.Rejected, Is.EqualTo(1));
            Assert.That(result.Remaining, Is.EqualTo(2));
            Assert.That(result.SkippedNotDue, Is.Zero); Assert.That(result.Stalled, Is.Zero);
            Assert.That(result.StoreErrors, Is.Zero); Assert.That(result.Canceled, Is.False);
            Assert.That(f.Transport.Requests.Select(r => r.Headers["Idempotency-Key"]), Is.EqualTo(new[] { "success", "reject", "retry", "keep" }));
            Assert.That(f.Outbox.Items.Keys, Is.EquivalentTo(new[] { "retry", "keep" }));
            Assert.That(f.Outbox.Items["retry"].AttemptCount, Is.EqualTo(3));
            Assert.That(f.Outbox.Items["retry"].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(4)));
            Assert.That(f.Outbox.Items["keep"].AttemptCount, Is.EqualTo(4));
            Assert.That(f.Outbox.Items["keep"].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(8)));
            Assert.That(f.Outbox.Items.Values.Select(i => i.LastReason), Is.All.Not.Null.And.All.Not.Empty);
            Assert.That(f.Delay.Delays, Is.Empty);
            f.AssertDisposed(4);
        }

        [Test] // S11b
        public async Task Flush_NotYetDue_SkipsWithoutSendingOrChangingItem()
        {
            var f = new Fixture(); var original = f.Seed("future", 7, 120);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.SkippedNotDue, Is.EqualTo(1));
            Assert.That(result.Succeeded + result.Rejected + result.Remaining + result.Stalled, Is.Zero);
            Assert.That(f.Transport.Requests, Is.Empty);
            Assert.That(f.Outbox.Items["future"], Is.SameAs(original));
            Assert.That(f.Outbox.SaveCalls, Is.Zero);
        }

        [Test] // S11c, W3
        public async Task Flush_CumulativeLimit_StallsAndWarnsWithoutDeleting()
        {
            var f = new Fixture(); var original = f.Seed("limit", 20);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.Stalled, Is.EqualTo(1));
            Assert.That(f.Transport.Requests, Is.Empty);
            Assert.That(f.Outbox.Items["limit"], Is.SameAs(original));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [Test] // S11d, W3: 5 inline + 15 flush attempts = 20 total
        public async Task Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit()
        {
            var f = new Fixture();
            for (var i = 0; i < 20; i++) f.Transport.Respond(503);
            var submitter = f.Create();
            await submitter.SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(5));
            for (var count = 6; count <= 20; count++)
            {
                f.Clock.UtcNow = f.Outbox.Items[Fixture.AbcKey].NextAttemptAt;
                var result = await submitter.FlushOutboxAsync(CancellationToken.None);
                Assert.That(result.Remaining, Is.EqualTo(1));
                Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
                Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(count));
            }
            f.Clock.UtcNow = f.Outbox.Items[Fixture.AbcKey].NextAttemptAt;
            Assert.That((await submitter.FlushOutboxAsync(CancellationToken.None)).Stalled, Is.EqualTo(1));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(20));
            Assert.That(f.Delay.Delays, Has.Count.EqualTo(4));
        }

        [Test] // S11e
        public async Task Flush_NewInstance_ResumesWithOriginalStoredKey()
        {
            var f = new Fixture(); f.Transport.Respond(202);
            await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            var stored = f.Outbox.Items.Single().Value;
            f.Clock.UtcNow = stored.NextAttemptAt;
            f.Transport.Respond(200);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(2));
            Assert.That(f.Transport.Requests[1].Headers["Idempotency-Key"], Is.EqualTo(stored.IdempotencyKey));
            Assert.That(f.Outbox.Items, Is.Empty);
        }

        [TestCase(503, "120", 120), TestCase(429, "3", 3)] // S11g, S11h
        public async Task Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(int status, string header, int seconds)
        {
            var f = new Fixture(); f.Seed("saved-key", 5); f.Transport.Respond(status, header);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.Remaining, Is.EqualTo(1));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
            Assert.That(f.Outbox.Items["saved-key"].AttemptCount, Is.EqualTo(6));
            Assert.That(f.Outbox.Items["saved-key"].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(seconds)));
        }

        [TestCase(200), TestCase(400), TestCase(503)] // S11i, W5: remove and save failures
        public async Task Flush_ItemStoreFailure_CountsOutcomeAndContinues(int firstStatus)
        {
            var f = new Fixture(); f.Seed("first", createdSeconds: -1); f.Seed("second");
            f.Outbox.FailRemove = key => key == "first";
            f.Outbox.FailSave = _ => true;
            f.Transport.Respond(firstStatus); f.Transport.Respond(400);
            var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(result.Succeeded, Is.EqualTo(firstStatus == 200 ? 1 : 0));
            Assert.That(result.Rejected, Is.EqualTo(firstStatus == 400 ? 2 : 1));
            Assert.That(result.Remaining, Is.EqualTo(firstStatus == 503 ? 1 : 0));
            Assert.That(result.StoreErrors, Is.EqualTo(1));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(2));
            Assert.That(f.Outbox.Items.Keys, Is.EqualTo(new[] { "first" }));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [Test] // W5
        public void Flush_LoadFailure_PropagatesBeforeAnySend()
        {
            var f = new Fixture(); f.Outbox.FailLoad = true;
            var submitter = f.Create();
            Assert.That(async () => await submitter.FlushOutboxAsync(CancellationToken.None), Throws.TypeOf<IOException>());
            Assert.That(f.Transport.Requests, Is.Empty);
        }

        [Test] // S11 cancellation, S7b, W3, W6
        public async Task Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems()
        {
            var f = new Fixture();
            f.Seed("done", createdSeconds: -3); f.Seed("inflight", 2, createdSeconds: -2); f.Seed("untouched", createdSeconds: -1);
            using var caller = new CancellationTokenSource();
            var pending = new PendingUntilCanceledTransport();
            f.Transport.Respond(200);
            f.Transport.Enqueue(ct => pending.SendAsync(new TransportRequest("POST", f.Endpoint, new Dictionary<string, string>(), ""), ct));
            var operation = f.Create().FlushOutboxAsync(caller.Token);
            try
            {
                await Fixture.AwaitStarted(operation, pending.Started.Task);
                caller.Cancel();
                var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(result.Canceled, Is.True);
                Assert.That(result.Succeeded, Is.EqualTo(1));
                Assert.That(result.Rejected, Is.Zero);
                Assert.That(f.Transport.Requests, Has.Count.EqualTo(2));
                Assert.That(f.Outbox.Items.Keys, Is.EquivalentTo(new[] { "inflight", "untouched" }));
                Assert.That(f.Outbox.Items["inflight"].AttemptCount, Is.EqualTo(3));
                Assert.That(f.Outbox.Items["inflight"].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow));
                Assert.That(f.Outbox.Items["untouched"].AttemptCount, Is.Zero);
                f.AssertDisposed(2);
            }
            finally { caller.Cancel(); }
        }
    }
}
