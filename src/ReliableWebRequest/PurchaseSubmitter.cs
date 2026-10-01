using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ReliableWebRequest
{
    /// <summary>전송 전 outbox 저장, 제한된 재시도, 지연된 재전송을 조정한다.
    /// 같은 저장소에 대한 SubmitAsync/FlushOutboxAsync 호출은 호출자가 직렬화해야 한다.</summary>
    public sealed class PurchaseSubmitter
    {
        private readonly ITransport transport;
        private readonly RetryPolicy policy;
        private readonly IDelay delay;
        private readonly IClock clock;
        private readonly IRandom random;
        private readonly ILogSink log;
        private readonly IOutboxStore outbox;
        private readonly IAttemptTimeoutFactory timeouts;
        private readonly Uri endpoint;

        /// <summary>전송, 저장, 시간, 로그 경계와 목적지를 주입받는다.</summary>
        public PurchaseSubmitter(ITransport transport, RetryPolicy policy, IDelay delay, IClock clock,
            IRandom random, ILogSink log, IOutboxStore outbox, IAttemptTimeoutFactory timeouts, Uri endpoint)
        {
            this.transport = transport;
            this.policy = policy;
            this.delay = delay;
            this.clock = clock;
            this.random = random;
            this.log = log;
            this.outbox = outbox;
            this.timeouts = timeouts;
            this.endpoint = endpoint;
        }

        /// <summary>키 조회와 초기 저장을 완료한 뒤 POST JSON 요청을 보낸다. 기존 항목은 수정/전송 없이
        /// DeferredToOutbox, IsPersisted=true, already pending으로 반환하고 flush에 맡긴다.
        /// 조회 실패는 초기 저장을 생략하며 이 경우 누적 횟수가 초기화될 수 있다.
        /// 성공/영구 거절, 호출자 취소, 저장 여부에 따른 보류/저장 실패 순서로 결과를 결정한다.
        /// 호출자 취소는 OCE를 전파하지 않고 최종 upsert 후 Canceled를 반환한다.
        /// 저장소 오류는 Warning으로 기록한다. 저장 성공 후 제거 성공이 없으면 IsPersisted=true이다.</summary>
        /// <remarks>실제 SendAsync 호출만 누적한다. HttpRequestException과 시도 토큰 취소는 재시도하고,
        /// 나머지 예외는 타입 이름을 사유로 보관한다. 매 시도 CTS는 Dispose한다.
        /// 429/503의 유효 Retry-After를 그대로 사용하며 그 외에는 누적 횟수의 백오프로 예약한다.
        /// 마지막 시도 또는 인라인 한도 초과 지연은 기다리지 않는다. StopKeep도 백오프로 예약한다.
        /// 전송 전/중 호출자 취소의 다음 기한은 현재 시각이며, 인라인 대기 중 취소는 예약 기한을 보존한다.
        /// 각 시도와 최종 결과를 로그에 남기며,
        /// 409는 키를 포함한 Error 로그 한 줄을 추가한다. 본문과 예외 메시지는 로그에 넣지 않는다.</remarks>
        public async Task<SubmitResult> SubmitAsync(PurchaseReceipt receipt, CancellationToken ct)
        {
            var key = IdempotencyKey.FromTransactionId(receipt.TransactionId);
            var lookup = await TryLoadExistingAsync(key).ConfigureAwait(false);
            if (lookup.Item != null)
                return LogResult(key, new SubmitResult(SubmitResultKind.DeferredToOutbox, null, "already pending", true));

            // 시계의 첫 조회부터 실패해도 최종 저장에 사용할 시각을 확보한다.
            var now = DateTimeOffset.UtcNow;
            var progress = new SubmissionProgress(new PendingSubmission(key, receipt, now, 0, now, null));
            try
            {
                now = ReadNow(progress);
                progress.Item = new PendingSubmission(key, receipt, now, 0, now, null);
                if (lookup.Succeeded)
                    progress.IsPersisted = await PersistAsync(progress.Item).ConfigureAwait(false);

                await SendInlineAsync(progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                MarkCanceled(progress);
            }
            catch (Exception error)
            {
                MarkDependencyFailure(progress, error);
            }
            return await ResolveResultAsync(progress, ct).ConfigureAwait(false);
        }

        /// <summary>CreatedAt 순으로 기한이 된 항목을 원래 키로 한 번씩 전송한다. 인라인 대기는 없다.
        /// 미도래 항목은 SkippedNotDue, 누적 한도 도달 항목은 Stalled/Warning으로 보관한다.
        /// 성공/거절은 제거하고 나머지는 횟수, 예약 기한, 사유를 갱신한다. 취소 시 부분 결과를 반환한다.
        /// LoadAll 실패는 전파하며 항목별 저장/제거 실패는 StoreErrors에 세고 다음 항목을 계속 처리한다.
        /// Succeeded/Rejected/Remaining은 저장소 오류와 독립적인 전송 결과 집계이다.</summary>
        public async Task<FlushResult> FlushOutboxAsync(CancellationToken ct)
        {
            var items = await outbox.LoadAllAsync().ConfigureAwait(false);
            var succeeded = 0;
            var rejected = 0;
            var remaining = 0;
            var skipped = 0;
            var stalled = 0;
            var storeErrors = 0;

            foreach (var item in items.OrderBy(item => item.CreatedAt))
            {
                if (ct.IsCancellationRequested)
                    break;
                var progress = new SubmissionProgress(item);
                try
                {
                    if (item.NextAttemptAt > ReadNow(progress))
                    {
                        skipped++;
                        continue;
                    }
                    if (item.AttemptCount >= policy.MaxOutboxAttempts)
                    {
                        stalled++;
                        WriteLog(LogLevel.Warning, $"Outbox stalled: key={item.IdempotencyKey}");
                        continue;
                    }
                    await SendOnceAsync(progress, ct).ConfigureAwait(false);
                    if (!IsFinal(progress.Decision))
                        ScheduleNextAttempt(progress);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    if (progress.Item.AttemptCount == item.AttemptCount)
                        break;
                    MarkCanceled(progress);
                }
                catch (Exception error)
                {
                    MarkDependencyFailure(progress, error);
                }

                bool stored;
                if (IsFinal(progress.Decision))
                {
                    if (progress.Decision == RetryDecision.Succeed) succeeded++;
                    else rejected++;
                    stored = await RemoveAsync(item.IdempotencyKey).ConfigureAwait(false);
                }
                else
                {
                    if (progress.Item.AttemptCount > item.AttemptCount) remaining++;
                    stored = await PersistAsync(progress.Item).ConfigureAwait(false);
                }
                if (!stored) storeErrors++;
            }

            return new FlushResult(succeeded, rejected, remaining, skipped, stalled, storeErrors, ct.IsCancellationRequested);
        }

        private async Task SendInlineAsync(SubmissionProgress progress, CancellationToken ct)
        {
            var scheduledDelay = TimeSpan.Zero;
            while (ShouldContinueInline(progress, scheduledDelay))
            {
                if (progress.Item.AttemptCount > 0)
                {
                    // 시도 타임아웃은 다음 대기를 취소하지 않는다. 대기 중 취소는 예약을 보존한다.
                    progress.IsWaiting = true;
                    await delay.DelayAsync(scheduledDelay, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    progress.IsWaiting = false;
                }

                await SendOnceAsync(progress, ct).ConfigureAwait(false);
                if (IsFinal(progress.Decision))
                    return;
                scheduledDelay = ScheduleNextAttempt(progress);
            }
        }

        private bool ShouldContinueInline(SubmissionProgress progress, TimeSpan scheduledDelay) =>
            progress.Item.AttemptCount < policy.MaxAttempts &&
            progress.Item.AttemptCount < policy.MaxOutboxAttempts &&
            (progress.Item.AttemptCount == 0 || progress.Decision == RetryDecision.Retry) &&
            scheduledDelay <= policy.MaxInlineRetryAfter;

        private async Task SendOnceAsync(SubmissionProgress progress, CancellationToken ct)
        {
            var request = CreateRequest(progress.Item);
            using var source = timeouts.Create(policy.PerAttemptTimeout, ct);
            var now = ReadNow(progress);
            ct.ThrowIfCancellationRequested();
            progress.Item = CopyItem(progress.Item, progress.Item.AttemptCount + 1, now, null);

            AttemptOutcome outcome;
            try
            {
                var response = await transport.SendAsync(request, source.Token).ConfigureAwait(false);
                outcome = new AttemptOutcome(AttemptOutcomeKind.Response, response, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogAttempt(progress.Item, "Canceled");
                throw;
            }
            catch (OperationCanceledException error) when (source.IsCancellationRequested)
            {
                outcome = new AttemptOutcome(AttemptOutcomeKind.AttemptTimedOut, null, error);
            }
            catch (HttpRequestException error)
            {
                outcome = new AttemptOutcome(AttemptOutcomeKind.TransportError, null, error);
            }
            catch (Exception error)
            {
                outcome = new AttemptOutcome(AttemptOutcomeKind.Unexpected, null, error);
            }

            progress.Outcome = outcome;
            progress.Decision = RetryClassifier.Classify(outcome);
            var reason = outcome.Response != null
                ? $"HTTP {outcome.Response.StatusCode}"
                : outcome.Exception?.GetType().Name ?? outcome.Kind.ToString();
            progress.Item = CopyItem(progress.Item, progress.Item.AttemptCount, ReadNow(progress), reason);
            LogAttempt(progress.Item, outcome.Kind.ToString());
            if (outcome.Response?.StatusCode == 409)
                WriteLog(LogLevel.Error, $"Idempotency conflict: key={progress.Item.IdempotencyKey}");
        }

        // W4: 인라인 전송과 flush에 동일한 예약 규칙을 적용한다.
        private TimeSpan ScheduleNextAttempt(SubmissionProgress progress)
        {
            var response = progress.Outcome?.Response;
            var now = ReadNow(progress);
            TimeSpan scheduledDelay;
            if (progress.Decision == RetryDecision.Retry && response != null &&
                (response.StatusCode == 429 || response.StatusCode == 503) &&
                RetryAfterParser.TryParse(response.Headers, clock, out var retryAfter) &&
                // 날짜 범위를 넘는 Retry-After는 무효로 보고 백오프로 대체한다.
                retryAfter <= DateTimeOffset.MaxValue - now)
                scheduledDelay = retryAfter;
            else
                scheduledDelay = BackoffCalculator.GetDelay(progress.Item.AttemptCount, policy, random);

            progress.Item = CopyItem(progress.Item, progress.Item.AttemptCount,
                now + scheduledDelay, progress.Item.LastReason);
            return scheduledDelay;
        }

        private async Task<SubmitResult> ResolveResultAsync(SubmissionProgress progress, CancellationToken ct)
        {
            if (IsFinal(progress.Decision))
            {
                // 제거 실패는 저장 여부를 새로 확정하거나 기존 저장 사실을 지우지 않는다.
                if (await RemoveAsync(progress.Item.IdempotencyKey).ConfigureAwait(false))
                    progress.IsPersisted = false;
            }
            else
            {
                if (ct.IsCancellationRequested)
                    MarkCanceled(progress);
                if (await PersistAsync(progress.Item).ConfigureAwait(false))
                    progress.IsPersisted = true;
            }

            // W1: 확정된 결과, 호출자 취소, 저장 실패 순서로 우선한다.
            var kind = progress.Decision == RetryDecision.Succeed ? SubmitResultKind.Succeeded
                : progress.Decision == RetryDecision.RejectPermanently ? SubmitResultKind.RejectedPermanently
                : ct.IsCancellationRequested ? SubmitResultKind.Canceled
                : progress.IsPersisted ? SubmitResultKind.DeferredToOutbox
                : SubmitResultKind.FailedNotPersisted;
            return LogResult(progress.Item.IdempotencyKey, new SubmitResult(kind,
                progress.Outcome?.Response?.StatusCode, progress.Item.LastReason, progress.IsPersisted));
        }

        private async Task<(bool Succeeded, PendingSubmission? Item)> TryLoadExistingAsync(string key)
        {
            try
            {
                return (true, await outbox.GetAsync(key).ConfigureAwait(false));
            }
            catch (Exception error)
            {
                LogStoreError("Get", key, error);
                return (false, null);
            }
        }

        private async Task<bool> PersistAsync(PendingSubmission item)
        {
            try
            {
                await outbox.SaveAsync(item).ConfigureAwait(false);
                return true;
            }
            catch (Exception error)
            {
                LogStoreError("Save", item.IdempotencyKey, error);
                return false;
            }
        }

        private async Task<bool> RemoveAsync(string key)
        {
            try
            {
                await outbox.RemoveAsync(key).ConfigureAwait(false);
                return true;
            }
            catch (Exception error)
            {
                LogStoreError("Remove", key, error);
                return false;
            }
        }

        private TransportRequest CreateRequest(PendingSubmission item)
        {
            var receipt = item.Receipt;
            var body = JsonSerializer.Serialize(new
            {
                productId = receipt.ProductId,
                transactionId = receipt.TransactionId,
                receipt = receipt.Receipt,
                signature = receipt.Signature
            });
            return new TransportRequest("POST", endpoint, new Dictionary<string, string>
            {
                ["Idempotency-Key"] = item.IdempotencyKey,
                ["Content-Type"] = "application/json"
            }, body);
        }

        private void MarkCanceled(SubmissionProgress progress)
        {
            var nextAttemptAt = progress.Item.NextAttemptAt;
            if (!progress.IsWaiting)
            {
                try { ReadNow(progress); }
                catch (Exception) { /* 시계 실패가 호출자 취소 처리를 막지 않도록 이전 시각을 쓴다. */ }
                nextAttemptAt = progress.LastObservedAt;
            }
            progress.Item = CopyItem(progress.Item, progress.Item.AttemptCount, nextAttemptAt, "caller canceled");
        }

        private DateTimeOffset ReadNow(SubmissionProgress progress) => progress.LastObservedAt = clock.UtcNow;

        private static void MarkDependencyFailure(SubmissionProgress progress, Exception error)
        {
            if (!IsFinal(progress.Decision))
                progress.Item = CopyItem(progress.Item, progress.Item.AttemptCount,
                    progress.LastObservedAt, error.GetType().Name);
        }

        private static PendingSubmission CopyItem(PendingSubmission item, int attempts, DateTimeOffset nextAttemptAt, string? reason) =>
            new PendingSubmission(item.IdempotencyKey, item.Receipt, item.CreatedAt, attempts, nextAttemptAt, reason);

        private static bool IsFinal(RetryDecision decision) =>
            decision == RetryDecision.Succeed || decision == RetryDecision.RejectPermanently;

        private void LogAttempt(PendingSubmission item, string kind) =>
            WriteLog(LogLevel.Info, $"Attempt {item.AttemptCount}: {kind}, key={item.IdempotencyKey}");

        private SubmitResult LogResult(string key, SubmitResult result)
        {
            WriteLog(LogLevel.Info, $"Result {result.Kind}: key={key}, persisted={result.IsPersisted}");
            return result;
        }

        // 본문과 예외 메시지에는 민감 정보가 있을 수 있으므로 메타데이터만 기록한다.
        private void LogStoreError(string operation, string key, Exception error) =>
            WriteLog(LogLevel.Warning, $"Outbox {operation} failed: {error.GetType().Name}, key={key}");

        private void WriteLog(LogLevel level, string message)
        {
            try { log.Write(level, LogRedactor.Redact(message)); }
            catch (Exception) { /* 로그 실패가 저장, 제거 및 결과 반환을 중단해서는 안 된다. */ }
        }

        private sealed class SubmissionProgress
        {
            public PendingSubmission Item { get; set; }
            public AttemptOutcome? Outcome { get; set; }
            public RetryDecision Decision { get; set; } = RetryDecision.StopKeep;
            public bool IsPersisted { get; set; }
            public bool IsWaiting { get; set; }
            public DateTimeOffset LastObservedAt { get; set; }

            public SubmissionProgress(PendingSubmission item)
            {
                Item = item;
                LastObservedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
