using System.Text.Json;
using NUnit.Framework;

namespace ReliableWebRequest.Tests
{
    [TestFixture]
    public sealed class ReviewRegressionTests
    {
        [Test]
        public async Task ThrowingLogger_DoesNotInterruptPersistenceResultsOrFlushProgress()
        {
            foreach (var status in new[] { 200, 400, 202 })
            {
                var f = new Fixture();
                f.Outbox.FailSave = n => n == 1;
                f.Transport.Respond(status);
                var result = await Create(f, log: new ThrowingLog()).SubmitAsync(f.Receipt, CancellationToken.None);
                Assert.That(result.Kind, Is.EqualTo(status == 200 ? SubmitResultKind.Succeeded :
                    status == 400 ? SubmitResultKind.RejectedPermanently : SubmitResultKind.DeferredToOutbox));
                Assert.That(result.IsPersisted, Is.EqualTo(status == 202));
                Assert.That(f.Outbox.SaveCalls, Is.EqualTo(status == 202 ? 2 : 1));
                Assert.That(f.Outbox.Removes.Count, Is.EqualTo(status == 202 ? 0 : 1));
            }

            var flush = new Fixture();
            flush.Seed("first", createdSeconds: -2);
            flush.Seed("second", createdSeconds: -1);
            flush.Seed("third");
            flush.Outbox.FailRemove = key => key == "first";
            flush.Transport.Respond(200); flush.Transport.Respond(400); flush.Transport.Respond(503);
            var counts = await Create(flush, log: new ThrowingLog()).FlushOutboxAsync(CancellationToken.None);
            Assert.That(counts.Succeeded, Is.EqualTo(1));
            Assert.That(counts.Rejected, Is.EqualTo(1));
            Assert.That(counts.Remaining, Is.EqualTo(1));
            Assert.That(counts.StoreErrors, Is.EqualTo(1));
            Assert.That(flush.Transport.Requests, Has.Count.EqualTo(3));
            Assert.That(flush.Outbox.Items.Keys, Is.EquivalentTo(new[] { "first", "third" }));
            Assert.That(flush.Outbox.Items["third"].AttemptCount, Is.EqualTo(1));
        }

        [TestCase("delay", 503, true, false)]
        [TestCase("delay", 503, false, false)]
        [TestCase("clock", 200, true, false)]
        [TestCase("clock", 400, true, false)]
        [TestCase("clock", 503, true, false)]
        [TestCase("clock", 503, false, false)]
        [TestCase("clock", 200, true, true)]
        [TestCase("clock", 400, true, true)]
        [TestCase("clock", 503, true, true)]
        [TestCase("initial clock", 503, true, false)]
        public async Task DependencyFailure_PreservesConfirmedOutcomeOrPerformsFinalUpsert(
            string failure, int status, bool saveSucceeds, bool flush)
        {
            var f = new Fixture();
            var now = f.Clock.UtcNow;
            var clockFails = failure == "initial clock";
            var clock = new CallbackClock(() => clockFails ? throw new InvalidOperationException() : now);
            f.Outbox.FailSave = n => !saveSucceeds || (!flush && n == 1 && failure != "initial clock");
            f.Delay.OnDelay = () => throw new InvalidOperationException();
            f.Transport.Enqueue(_ =>
            {
                clockFails = failure == "clock";
                return Task.FromResult(Fixture.Response(status));
            });
            var submitter = Create(f, clock: clock);
            var before = DateTimeOffset.UtcNow;
            if (flush)
            {
                f.Seed(Fixture.AbcKey);
                var result = await submitter.FlushOutboxAsync(CancellationToken.None);
                Assert.That(result.Succeeded, Is.EqualTo(status == 200 ? 1 : 0));
                Assert.That(result.Rejected, Is.EqualTo(status == 400 ? 1 : 0));
                Assert.That(result.Remaining, Is.EqualTo(status == 503 ? 1 : 0));
                Assert.That(result.StoreErrors, Is.Zero);
            }
            else
            {
                var result = await submitter.SubmitAsync(f.Receipt, CancellationToken.None);
                Assert.That(result.Kind, Is.EqualTo(status == 200 ? SubmitResultKind.Succeeded :
                    status == 400 ? SubmitResultKind.RejectedPermanently :
                    saveSucceeds ? SubmitResultKind.DeferredToOutbox : SubmitResultKind.FailedNotPersisted));
                Assert.That(result.IsPersisted, Is.EqualTo(status == 503 && saveSucceeds));
                if (status == 503) Assert.That(result.Reason, Is.EqualTo(nameof(InvalidOperationException)));
            }
            Assert.That(f.Transport.Requests.Count, Is.EqualTo(failure == "initial clock" ? 0 : 1));
            if (status == 200 || status == 400)
            {
                Assert.That(f.Outbox.Removes, Is.EqualTo(new[] { Fixture.AbcKey }));
                Assert.That(f.Outbox.Items, Is.Empty);
            }
            else
            {
                Assert.That(f.Outbox.SaveCalls, Is.EqualTo(flush || failure == "initial clock" ? 1 : 2));
                if (saveSucceeds)
                {
                    var item = f.Outbox.Items[Fixture.AbcKey];
                    Assert.That(item.LastReason, Is.EqualTo(nameof(InvalidOperationException)));
                    Assert.That(item.AttemptCount, Is.EqualTo(failure == "initial clock" ? 0 : 1));
                    if (failure == "initial clock")
                        Assert.That(item.NextAttemptAt, Is.InRange(before, DateTimeOffset.UtcNow));
                    else Assert.That(item.NextAttemptAt, Is.EqualTo(now));
                }
                else Assert.That(f.Outbox.Items, Is.Empty);
            }
        }

        [TestCase("30", 30), TestCase(null, 1)]
        public async Task CancelDuringInlineWait_PreservesScheduleAndFlushSkipsUntilDue(string? header, int seconds)
        {
            var f = new Fixture();
            var scheduled = f.Clock.UtcNow.AddSeconds(seconds);
            using var caller = new CancellationTokenSource();
            f.Transport.Respond(429, header);
            f.Delay.OnDelay = () => { f.Clock.UtcNow = f.Clock.UtcNow.AddMilliseconds(500); caller.Cancel(); };
            var result = await f.Create().SubmitAsync(f.Receipt, caller.Token);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Canceled));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(scheduled));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(1));
            var flush = await f.Create().FlushOutboxAsync(CancellationToken.None);
            Assert.That(flush.SkippedNotDue, Is.EqualTo(1));
            Assert.That(flush.Succeeded + flush.Rejected + flush.Remaining, Is.Zero);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
        }

        [TestCase(false), TestCase(true)]
        public async Task UnrepresentableRetryAfter_FallsBackToBackoffWithoutStoppingProgress(bool flush)
        {
            var f = new Fixture();
            const string header = "315537897600";
            Assert.That(RetryAfterParser.TryParse(Fixture.Response(503, header).Headers, f.Clock, out _), Is.True);
            f.Transport.Respond(503, header); f.Transport.Respond(200);
            if (flush)
            {
                f.Seed("first", createdSeconds: -1); f.Seed("second");
                var result = await f.Create().FlushOutboxAsync(CancellationToken.None);
                Assert.That(result.Remaining, Is.EqualTo(1));
                Assert.That(result.Succeeded, Is.EqualTo(1));
                Assert.That(f.Outbox.Items["first"].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(1)));
                Assert.That(f.Delay.Delays, Is.Empty);
            }
            else
            {
                var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
                Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
                Assert.That(f.Delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1) }));
                Assert.That(f.Outbox.Items, Is.Empty);
            }
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(2));
        }

        [Test]
        public void Redactor_DecodesNestedSensitiveNamesAndPreservesJsonAndFormBoundaries()
        {
            const string input = "[{\"\\u0072eceipt\":{\"secret\":1},\"nested\":[{\"ToKeN\":123,\"EMAIL\":null,\"signature\":[1,2]}],\"note\":\"see token=SECRET\",\"productId\":\"coins\",\"count\":2}]";
            var redacted = LogRedactor.Redact(input);
            using var json = JsonDocument.Parse(redacted);
            var item = json.RootElement[0];
            Assert.That(item.GetProperty("receipt").GetString(), Is.EqualTo("***"));
            foreach (var key in new[] { "ToKeN", "EMAIL", "signature" })
                Assert.That(item.GetProperty("nested")[0].GetProperty(key).GetString(), Is.EqualTo("***"));
            Assert.That(item.GetProperty("note").GetString(), Is.EqualTo("see token=***"));
            Assert.That(item.GetProperty("productId").GetString(), Is.EqualTo("coins"));
            Assert.That(item.GetProperty("count").GetInt32(), Is.EqualTo(2));
            Assert.That(LogRedactor.Redact(redacted), Is.EqualTo(redacted));
            foreach (var boundary in new[] { "&", " ", "\t", "\"", "'", ",", ";", "}" })
            {
                var form = LogRedactor.Redact("token=SECRET" + boundary + "productId=coins");
                Assert.That(form, Is.EqualTo("token=***" + boundary + "productId=coins"));
                Assert.That(LogRedactor.Redact(form), Is.EqualTo(form));
            }
            const string objectInput = "{\"note\":\"see token=SECRET\",\"productId\":\"coins\"}";
            using var obj = JsonDocument.Parse(LogRedactor.Redact(objectInput));
            Assert.That(obj.RootElement.GetProperty("productId").GetString(), Is.EqualTo("coins"));
            Assert.That(obj.RootElement.GetProperty("note").GetString(), Is.EqualTo("see token=***"));
        }

        [TestCase(0), TestCase(1)]
        public async Task Flush_CancellationBeforeSend_DoesNotCountOrResaveUnsentItem(int completed)
        {
            var f = new Fixture();
            using var caller = new CancellationTokenSource();
            if (completed > 0) { f.Seed("done", createdSeconds: -2); f.Transport.Respond(200); }
            var original = f.Seed("unsent", 3, createdSeconds: -1);
            var later = f.Seed("later");
            var factory = new CallbackTimeoutFactory(() =>
            {
                if (f.Transport.Requests.Count == completed) caller.Cancel();
            });
            var result = await Create(f, timeouts: factory).FlushOutboxAsync(caller.Token);
            Assert.That(result.Canceled, Is.True);
            Assert.That(result.Succeeded, Is.EqualTo(completed));
            Assert.That(result.Rejected + result.Remaining + result.StoreErrors + result.SkippedNotDue + result.Stalled, Is.Zero);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(completed));
            Assert.That(f.Outbox.SaveCalls, Is.Zero);
            Assert.That(f.Outbox.Items["unsent"], Is.SameAs(original));
            Assert.That(f.Outbox.Items["later"], Is.SameAs(later));
            Assert.That(factory.Sources, Has.Count.EqualTo(completed + 1));
            Assert.That(factory.Sources.Select(s => s.DisposeCount), Is.All.EqualTo(1));
        }

        private static PurchaseSubmitter Create(Fixture f, IClock? clock = null, ILogSink? log = null,
            IAttemptTimeoutFactory? timeouts = null) =>
            new(f.Transport, f.Policy, f.Delay, clock ?? f.Clock, new FixedRandom(), log ?? f.Log,
                f.Outbox, timeouts ?? f.Timeouts, f.Endpoint);

        private sealed class ThrowingLog : ILogSink
        {
            public void Write(LogLevel level, string message) => throw new InvalidOperationException();
        }

        private sealed class CallbackClock : IClock
        {
            private readonly Func<DateTimeOffset> read;
            public CallbackClock(Func<DateTimeOffset> read) => this.read = read;
            public DateTimeOffset UtcNow => read();
        }

        private sealed class CallbackTimeoutFactory : IAttemptTimeoutFactory
        {
            private readonly Action onCreate;
            public List<TrackingCts> Sources { get; } = new();
            public CallbackTimeoutFactory(Action onCreate) => this.onCreate = onCreate;
            public CancellationTokenSource Create(TimeSpan timeout, CancellationToken callerToken)
            {
                onCreate();
                var source = new TrackingCts(callerToken);
                Sources.Add(source);
                return source;
            }
        }
    }
}
