using System.Text.Json;
using NUnit.Framework;

namespace ReliableWebRequest.Tests
{
    [TestFixture]
    public sealed class PurchaseSubmitterTests
    {
        [Test] // S1, D2
        public async Task Submit_FirstSuccess_CompletesWriteAheadBeforeSend()
        {
            var f = new Fixture();
            f.Transport.Respond(200);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That(result.StatusCode, Is.EqualTo(200));
            Assert.That(result.IsPersisted, Is.False);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
            Assert.That(f.Journal.Entries.Take(5), Is.EqualTo(new[] { "Get:start", "Get:complete", "Save:start", "Save:complete", "Send:start" }));
            Assert.That(f.Outbox.Saves[0].AttemptCount, Is.Zero);
            Assert.That(f.Outbox.Saves[0].CreatedAt, Is.EqualTo(f.Clock.UtcNow));
            Assert.That(f.Outbox.Saves[0].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow));
            Assert.That(f.Outbox.Saves[0].Receipt.ProductId, Is.EqualTo(f.Receipt.ProductId));
            Assert.That(f.Outbox.Saves[0].Receipt.TransactionId, Is.EqualTo(f.Receipt.TransactionId));
            Assert.That(f.Outbox.Saves[0].Receipt.Receipt, Is.EqualTo(f.Receipt.Receipt));
            Assert.That(f.Outbox.Saves[0].Receipt.Signature, Is.EqualTo(f.Receipt.Signature));
            Assert.That(f.Outbox.Items, Is.Empty);
            Assert.That(f.Outbox.Removes, Is.EqualTo(new[] { Fixture.AbcKey }));
            f.AssertDisposed(1);
        }

        [Test] // S1, D2
        public async Task Submit_PendingWriteAheadSave_DoesNotSendUntilSaveCompletes()
        {
            var f = new Fixture();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Outbox.SaveGate = gate;
            f.Transport.Respond(200);
            var operation = f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            try
            {
                await Fixture.AwaitStarted(operation, f.Outbox.SaveStarted.Task);
                Assert.That(gate.Task.IsCompleted, Is.False);
                Assert.That(f.Journal.Entries, Does.Not.Contain("Save:complete"));
                Assert.That(f.Transport.Requests, Is.Empty, "Send must wait for SaveAsync to complete");
                Assert.That(operation.IsCompleted, Is.False);
                gate.SetResult();
                var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
                Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
                Assert.That(f.Journal.Entries.Take(5), Is.EqualTo(new[] { "Get:start", "Get:complete", "Save:start", "Save:complete", "Send:start" }));
            }
            finally { gate.TrySetResult(); }
        }

        [Test] // S2
        public async Task Submit_RetriesOn503_ThenSucceedsWithBackoff()
        {
            var f = new Fixture();
            f.Transport.Respond(503); f.Transport.Respond(503); f.Transport.Respond(200);
            using var caller = new CancellationTokenSource();
            var result = await f.Create().SubmitAsync(f.Receipt, caller.Token);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(3));
            Assert.That(f.Delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }));
            Assert.That(f.Delay.Tokens, Is.All.EqualTo(caller.Token));
            Assert.That(f.Transport.Tokens, Is.All.Not.EqualTo(caller.Token));
            f.AssertDisposed(3);
        }

        [Test] // S3
        public async Task Submit_RetriesAndNewInstance_ReuseTransactionKey()
        {
            var f = new Fixture();
            f.Transport.Respond(503); f.Transport.Respond(200); f.Transport.Respond(200); f.Transport.Respond(200);
            Assert.That((await f.Create().SubmitAsync(f.Receipt, CancellationToken.None)).Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That((await f.Create().SubmitAsync(f.Receipt, CancellationToken.None)).Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            var other = new PurchaseReceipt("coins", "other-transaction", f.Receipt.Receipt, f.Receipt.Signature);
            Assert.That((await f.Create().SubmitAsync(other, CancellationToken.None)).Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(4));
            Assert.That(f.Transport.Requests.Take(3).Select(r => r.Headers["Idempotency-Key"]), Is.All.EqualTo(Fixture.AbcKey));
            Assert.That(f.Transport.Requests[3].Headers["Idempotency-Key"], Is.Not.EqualTo(Fixture.AbcKey));
        }

        [TestCase(400), TestCase(409)] // S4, S4b
        public async Task Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(int status)
        {
            var f = new Fixture(); f.Transport.Respond(status);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.RejectedPermanently));
            Assert.That(result.StatusCode, Is.EqualTo(status));
            Assert.That(result.IsPersisted, Is.False);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
            Assert.That(f.Outbox.Saves, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(f.Outbox.Removes, Is.EqualTo(new[] { Fixture.AbcKey }));
            Assert.That(f.Outbox.Items, Is.Empty);
            if (status == 409)
            {
                var errors = f.Log.Entries.Where(e => e.Level == LogLevel.Error).ToArray();
                Assert.That(errors, Has.Length.EqualTo(1));
                Assert.That(errors[0].Message, Does.Contain(Fixture.AbcKey));
            }
        }

        [Test] // S5, D3
        public async Task Submit_ExhaustsFiveAttempts_PersistsOneItemAndFinalSchedule()
        {
            var f = new Fixture();
            for (var i = 0; i < 5; i++) f.Transport.Respond(503);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(5));
            Assert.That(f.Delay.Delays, Has.Count.EqualTo(4));
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
            var item = f.Outbox.Items[Fixture.AbcKey];
            Assert.That(item.AttemptCount, Is.EqualTo(5));
            Assert.That(item.Receipt.ProductId, Is.EqualTo(f.Receipt.ProductId));
            Assert.That(item.Receipt.TransactionId, Is.EqualTo(f.Receipt.TransactionId));
            Assert.That(item.Receipt.Receipt, Is.EqualTo(f.Receipt.Receipt));
            Assert.That(item.Receipt.Signature, Is.EqualTo(f.Receipt.Signature));
            Assert.That(item.IdempotencyKey, Is.EqualTo(Fixture.AbcKey));
            Assert.That(item.NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(10)));
            Assert.That(item.LastReason, Is.Not.Null.And.Not.Empty);
        }

        [TestCase(1), TestCase(2)] // S5, D4
        public async Task Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(int maxAttempts)
        {
            var f = new Fixture { Policy = Fixture.PolicyWith(maxAttempts: maxAttempts) };
            for (var i = 0; i < maxAttempts; i++) f.Transport.Respond(503);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(maxAttempts));
            Assert.That(f.Delay.Delays, Has.Count.EqualTo(maxAttempts - 1));
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(maxAttempts));
        }

        [TestCase(429, "3", 3), TestCase(429, "garbage", 1), TestCase(500, "3", 1)] // S6a, S6c, S6d
        public async Task Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(int status, string header, int seconds)
        {
            var f = new Fixture();
            f.Transport.Respond(status, header); f.Transport.Respond(200);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(2));
            Assert.That(f.Delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(seconds) }));
        }

        [Test] // S6b
        public async Task Submit_LongRetryAfter_DefersWithoutInlineWait()
        {
            var f = new Fixture(); f.Transport.Respond(503, "120");
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
            Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(120)));
        }

        [Test] // S6e
        public async Task Submit_FinalAttemptRetryAfter_SchedulesWithoutSixthSend()
        {
            var f = new Fixture();
            for (var i = 0; i < 4; i++) f.Transport.Respond(503);
            f.Transport.Respond(503, "3");
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(5));
            Assert.That(f.Delay.Delays, Is.EqualTo(new[] { 1, 2, 4, 8 }.Select(n => TimeSpan.FromSeconds(n))));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(3)));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(5));
        }

        [TestCase(false), TestCase(true)] // S7: 전송 전 / 대기 중 취소
        public async Task Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(bool duringDelay)
        {
            var f = new Fixture();
            using var caller = new CancellationTokenSource();
            if (duringDelay) { f.Transport.Respond(503); f.Delay.OnDelay = caller.Cancel; }
            else caller.Cancel();
            var result = await f.Create().SubmitAsync(f.Receipt, caller.Token);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Canceled));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(duringDelay ? 1 : 0));
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(duringDelay ? 1 : 0));
            Assert.That(f.Outbox.Saves, Has.Count.GreaterThanOrEqualTo(2), "Cancellation still performs a final upsert");
            if (!duringDelay) Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow));
            else Assert.That(f.Delay.Tokens, Is.EqualTo(new[] { caller.Token }));
        }

        [Test] // S7b, D3, D6
        public async Task Submit_CallerCancellationDuringSend_CountsAttemptAndDisposesSource()
        {
            var f = new Fixture(); var transport = new PendingUntilCanceledTransport();
            using var caller = new CancellationTokenSource();
            var submitter = f.Create(transport);
            var operation = submitter.SubmitAsync(f.Receipt, caller.Token);
            try
            {
                await Fixture.AwaitStarted(operation, transport.Started.Task);
                caller.Cancel();
                var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Canceled));
                Assert.That(result.IsPersisted, Is.True);
                Assert.That(transport.SendCount, Is.EqualTo(1));
                Assert.That(f.Delay.Delays, Is.Empty);
                Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(1));
                Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow));
                f.AssertDisposed(1);
            }
            finally { caller.Cancel(); }
        }

        [Test] // S8, D6
        public async Task Submit_AttemptTimeout_RetriesWithoutCancelingCallerAndDisposesAllSources()
        {
            var f = new Fixture(); var transport = new PendingUntilCanceledTransport();
            using var caller = new CancellationTokenSource();
            var operation = f.Create(transport).SubmitAsync(f.Receipt, caller.Token);
            try
            {
                await Fixture.AwaitStarted(operation, transport.Started.Task);
                f.Timeouts.Trigger(0);
                var result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
                Assert.That(transport.SendCount, Is.EqualTo(2));
                Assert.That(transport.Tokens[0].IsCancellationRequested, Is.True);
                Assert.That(caller.IsCancellationRequested, Is.False);
                Assert.That(f.Delay.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(1) }));
                Assert.That(f.Delay.Tokens, Is.EqualTo(new[] { caller.Token }));
                f.AssertDisposed(2);
            }
            finally { caller.Cancel(); }
        }

        [Test] // S9
        public async Task Submit_UnexpectedTransportException_KeepsReasonWithoutRetry()
        {
            var f = new Fixture(); f.Transport.Throw(new InvalidOperationException("unexpected"));
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.Reason, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
            var item = f.Outbox.Items[Fixture.AbcKey];
            Assert.That(item.LastReason, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(item.AttemptCount, Is.EqualTo(1));
            Assert.That(item.NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(1)));
        }

        [Test] // S10, D9 및 D8의 전송 오류 정규화
        public async Task Submit_ResponseAndExceptionSecrets_NeverAppearInRetryLogs()
        {
            var f = new Fixture();
            var sensitive = $"{{\"receipt\":\"{f.Receipt.Receipt}\",\"signature\":\"{f.Receipt.Signature}\"}}";
            f.Transport.Respond(503, body: sensitive);
            f.Transport.Throw(new HttpRequestException(sensitive));
            f.Transport.Respond(200);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Succeeded));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(3));
            Assert.That(f.Log.Entries, Is.Not.Empty);
            var logs = string.Join("\n", f.Log.Entries.Select(e => e.Message));
            Assert.That(logs, Does.Not.Contain(f.Receipt.Receipt).And.Not.Contain(f.Receipt.Signature));
        }

        [Test] // S12
        public async Task Submit_Request_ContainsReceiptJsonAndContentType()
        {
            var f = new Fixture(); f.Transport.Respond(200);
            await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            var request = f.Transport.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Url, Is.EqualTo(f.Endpoint));
            Assert.That(request.Headers["Content-Type"], Is.EqualTo("application/json"));
            using var json = JsonDocument.Parse(request.Body);
            Assert.That(json.RootElement.GetProperty("productId").GetString(), Is.EqualTo(f.Receipt.ProductId));
            Assert.That(json.RootElement.GetProperty("transactionId").GetString(), Is.EqualTo(f.Receipt.TransactionId));
            Assert.That(json.RootElement.GetProperty("receipt").GetString(), Is.EqualTo(f.Receipt.Receipt));
            Assert.That(json.RootElement.GetProperty("signature").GetString(), Is.EqualTo(f.Receipt.Signature));
        }

        [Test] // S13
        public async Task Submit_AcceptedButUnconfirmed_KeepsItemForLater()
        {
            var f = new Fixture(); f.Transport.Respond(202);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].NextAttemptAt, Is.EqualTo(f.Clock.UtcNow.AddSeconds(1)));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Delay.Delays, Is.Empty);
        }

        [Test] // S14
        public async Task Submit_LogsOneInfoPerAttemptAndOneFinalResult()
        {
            var f = new Fixture();
            f.Transport.Respond(503); f.Transport.Throw(new HttpRequestException("offline")); f.Transport.Respond(200);
            await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            var finalLines = f.Log.Entries.Where(e => Enum.GetNames<SubmitResultKind>().Any(kind => e.Message.Contains(kind))).ToArray();
            Assert.That(finalLines, Has.Length.EqualTo(1));
            Assert.That(finalLines[0].Message, Does.Contain(nameof(SubmitResultKind.Succeeded)));
            var infos = f.Log.Entries.Where(e => e.Level == LogLevel.Info && e != finalLines[0]).Select(e => e.Message).ToArray();
            var kinds = new[] { "Response", "TransportError", "Response" };
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(kinds.Length));
            Assert.That(infos, Has.Length.EqualTo(kinds.Length));
            var matchedLines = new HashSet<int>();
            for (var i = 0; i < kinds.Length; i++)
            {
                var number = i + 1;
                var matches = Enumerable.Range(0, infos.Length).Where(index =>
                    infos[index].Contains(Fixture.AbcKey) && infos[index].Contains(kinds[i]) &&
                    System.Text.RegularExpressions.Regex.IsMatch(infos[index].Replace(Fixture.AbcKey, ""), $@"(?<!\d){number}(?!\d)")).ToArray();
                Assert.That(matches, Has.Length.EqualTo(1));
                Assert.That(matchedLines.Add(matches[0]), Is.True, "Each attempt must have its own Info line");
            }
        }

        [Test] // S15a
        public async Task Submit_InitialSaveFailsBut400_StillRejectsAndRemoves()
        {
            var f = new Fixture(); f.Outbox.FailSave = _ => true; f.Transport.Respond(400);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.RejectedPermanently));
            Assert.That(result.IsPersisted, Is.False);
            Assert.That(f.Outbox.Removes, Is.EqualTo(new[] { Fixture.AbcKey }));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [Test] // S15b
        public async Task Submit_AllSavesFailAndCallerCancels_ReturnsCanceledNotPersisted()
        {
            var f = new Fixture(); f.Outbox.FailSave = _ => true; f.Transport.Respond(503);
            using var caller = new CancellationTokenSource(); f.Delay.OnDelay = caller.Cancel;
            var result = await f.Create().SubmitAsync(f.Receipt, caller.Token);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.Canceled));
            Assert.That(result.IsPersisted, Is.False);
            Assert.That(f.Outbox.SaveCalls, Is.GreaterThanOrEqualTo(2));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(1));
            Assert.That(f.Outbox.Items, Is.Empty);
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [TestCase(true), TestCase(false)] // S15c, S15d, S11f: 최종 저장 상태를 검증한다.
        public async Task Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(bool laterSaveSucceeds)
        {
            var f = new Fixture(); f.Outbox.FailSave = n => n == 1 || !laterSaveSucceeds;
            for (var i = 0; i < 5; i++) f.Transport.Respond(503);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(laterSaveSucceeds ? SubmitResultKind.DeferredToOutbox : SubmitResultKind.FailedNotPersisted));
            Assert.That(result.IsPersisted, Is.EqualTo(laterSaveSucceeds));
            Assert.That(f.Transport.Requests, Has.Count.EqualTo(5));
            Assert.That(f.Outbox.SaveCalls, Is.GreaterThanOrEqualTo(2));
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(laterSaveSucceeds ? 1 : 0));
            Assert.That(result.Reason, Is.Not.Null.And.Not.Empty);
            if (laterSaveSucceeds) Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(5));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [TestCase(true, 200), TestCase(false, 200), TestCase(true, 400)] // S15e, D1의 제거 규칙
        public async Task Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(bool initialSaveSucceeds, int status)
        {
            var f = new Fixture();
            f.Outbox.FailSave = _ => !initialSaveSucceeds; f.Outbox.FailRemove = _ => true;
            f.Transport.Respond(status);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(status == 200 ? SubmitResultKind.Succeeded : SubmitResultKind.RejectedPermanently));
            Assert.That(result.IsPersisted, Is.EqualTo(initialSaveSucceeds));
            Assert.That(f.Outbox.Removes, Is.EqualTo(new[] { Fixture.AbcKey }));
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(initialSaveSucceeds ? 1 : 0));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
            if (initialSaveSucceeds && status == 200)
            {
                f.Outbox.FailRemove = null; f.Transport.Respond(200);
                var flush = await f.Create().FlushOutboxAsync(CancellationToken.None);
                Assert.That(flush.Succeeded, Is.EqualTo(1));
                Assert.That(f.Transport.Requests.Select(r => r.Headers["Idempotency-Key"]), Is.All.EqualTo(Fixture.AbcKey));
                Assert.That(f.Outbox.Items, Is.Empty);
            }
        }

        [Test] // S16
        public async Task Submit_AlreadyPending_DoesNotSendOrOverwriteAnyField()
        {
            var f = new Fixture(); var original = f.Seed(Fixture.AbcKey, 7, 120, -60);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(result.Reason, Is.EqualTo("already pending"));
            Assert.That(f.Transport.Requests, Is.Empty);
            Assert.That(f.Outbox.SaveCalls, Is.Zero);
            Assert.That(f.Outbox.Removes, Is.Empty);
            Assert.That(f.Outbox.Items, Has.Count.EqualTo(1));
            Assert.That(f.Outbox.Items[Fixture.AbcKey], Is.SameAs(original), "Immutable data: same instance preserves every field");
        }

        [Test] // D2의 조회 실패, D1의 최종 저장 성공
        public async Task Submit_GetFails_SkipsInitialSaveAndRecoversWithFinalUpsert()
        {
            var f = new Fixture(); f.Outbox.FailGet = true; f.Transport.Respond(202);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Journal.Entries.Take(2), Is.EqualTo(new[] { "Get:start", "Send:start" }));
            Assert.That(f.Outbox.SaveCalls, Is.EqualTo(1));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.EqualTo(1));
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }

        [Test] // D1: 최종 갱신 실패 시 저장된 이전 횟수를 허용한다.
        public async Task Submit_FinalUpdateFails_RetainsKnownPersistenceAndWarns()
        {
            var f = new Fixture(); f.Outbox.FailSave = n => n > 1; f.Transport.Respond(202);
            var result = await f.Create().SubmitAsync(f.Receipt, CancellationToken.None);
            Assert.That(result.Kind, Is.EqualTo(SubmitResultKind.DeferredToOutbox));
            Assert.That(result.IsPersisted, Is.True);
            Assert.That(f.Outbox.SaveCalls, Is.GreaterThanOrEqualTo(2));
            Assert.That(f.Outbox.Items[Fixture.AbcKey].AttemptCount, Is.Zero);
            Assert.That(f.Log.Entries.Any(e => e.Level == LogLevel.Warning), Is.True);
        }
    }
}
