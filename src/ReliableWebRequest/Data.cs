using System;
using System.Collections.Generic;

namespace ReliableWebRequest
{
    /// <summary>S10, S14, S4b: 로그 수준.</summary>
    public enum LogLevel { Info, Warning, Error }
    /// <summary>C1–C4, V3: 정규화된 시도 종류. 호출자 취소는 별도 처리한다.</summary>
    public enum AttemptOutcomeKind { Response, TransportError, AttemptTimedOut, Unexpected }
    /// <summary>C1–C4: 분류 결과.</summary>
    public enum RetryDecision { Succeed, Retry, RejectPermanently, StopKeep }
    /// <summary>W1, S1–S16: 최종 결과 종류.</summary>
    public enum SubmitResultKind { Succeeded, RejectedPermanently, DeferredToOutbox, Canceled, FailedNotPersisted }

    /// <summary>S3, S12: 전송할 HTTP 요청 데이터.</summary>
    public sealed class TransportRequest
    {
        /// <summary>S12: HTTP 메서드.</summary>
        public string Method { get; }
        /// <summary>S12: 주입된 엔드포인트.</summary>
        public Uri Url { get; }
        /// <summary>S3, S12: Idempotency-Key 및 Content-Type.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>S12: 영수증 JSON.</summary>
        public string Body { get; }
        /// <summary>S12: 값만 보관한다.</summary>
        public TransportRequest(string method, Uri url, IReadOnlyDictionary<string, string> headers, string body)
        { Method = method; Url = url; Headers = headers; Body = body; }
    }

    /// <summary>C1–C4, R1–R5: HTTP 응답 데이터.</summary>
    public sealed class TransportResponse
    {
        /// <summary>C1–C4: HTTP 상태 코드.</summary>
        public int StatusCode { get; }
        /// <summary>R1–R5: 응답 헤더.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>S10: 민감 값이 포함될 수 있는 응답 본문.</summary>
        public string Body { get; }
        /// <summary>C1–C4: 값만 보관한다.</summary>
        public TransportResponse(int statusCode, IReadOnlyDictionary<string, string> headers, string body)
        { StatusCode = statusCode; Headers = headers; Body = body; }
    }

    /// <summary>S12, K1: 구매 영수증 데이터.</summary>
    public sealed class PurchaseReceipt
    {
        /// <summary>S12: 상품 식별자.</summary>
        public string ProductId { get; }
        /// <summary>K1: UTF-8 해시의 입력인 스토어 거래 식별자.</summary>
        public string TransactionId { get; }
        /// <summary>L1–L4, S10: 로그에 노출하면 안 되는 영수증.</summary>
        public string Receipt { get; }
        /// <summary>L1–L4, S10: 로그에 노출하면 안 되는 서명.</summary>
        public string Signature { get; }
        /// <summary>S12: 값만 보관한다.</summary>
        public PurchaseReceipt(string productId, string transactionId, string receipt, string signature)
        { ProductId = productId; TransactionId = transactionId; Receipt = receipt; Signature = signature; }
    }

    /// <summary>S5, S11, S16: 저장되는 대기 항목. 시도 횟수는 실제 시작한 전송의 누계이다.</summary>
    public sealed class PendingSubmission
    {
        /// <summary>S3, S11: 최초 키를 유지한다.</summary>
        public string IdempotencyKey { get; }
        /// <summary>S12: 최초 영수증 데이터.</summary>
        public PurchaseReceipt Receipt { get; }
        /// <summary>S11: flush의 처리 순서 기준.</summary>
        public DateTimeOffset CreatedAt { get; }
        /// <summary>W3: 시작한 SendAsync 횟수, 취소 및 타임아웃 시도도 포함.</summary>
        public int AttemptCount { get; }
        /// <summary>W3, W4: 현재 시각 + 예약 지연. 전송 전/중 호출자 취소는 현재 시각.</summary>
        public DateTimeOffset NextAttemptAt { get; }
        /// <summary>S9, S11: 마지막 보류 사유.</summary>
        public string? LastReason { get; }
        /// <summary>S5, S11: 값만 보관한다.</summary>
        public PendingSubmission(string idempotencyKey, PurchaseReceipt receipt, DateTimeOffset createdAt,
            int attemptCount, DateTimeOffset nextAttemptAt, string? lastReason)
        { IdempotencyKey = idempotencyKey; Receipt = receipt; CreatedAt = createdAt; AttemptCount = attemptCount; NextAttemptAt = nextAttemptAt; LastReason = lastReason; }
    }

    /// <summary>W1: 성공/영구거절, 취소, 보류 순서로 결정한 결과.</summary>
    public sealed class SubmitResult
    {
        /// <summary>W1: 최종 결과 종류.</summary>
        public SubmitResultKind Kind { get; }
        /// <summary>S1, S4: 응답이 있으면 HTTP 상태 코드.</summary>
        public int? StatusCode { get; }
        /// <summary>S9, S16: 예외 타입 또는 already pending 등의 사유.</summary>
        public string? Reason { get; }
        /// <summary>W1: 이번 호출에서 저장 성공 이력이 있고 이후 제거 성공이 없으면 true.
        /// W2의 기존 대기 항목 반환도 true. 제거 실패만으로 true가 되지 않는다.</summary>
        public bool IsPersisted { get; }
        /// <summary>Contract: 검증이나 팩터리 동작 없이 값만 보관한다.</summary>
        public SubmitResult(SubmitResultKind kind, int? statusCode, string? reason, bool isPersisted)
        { Kind = kind; StatusCode = statusCode; Reason = reason; IsPersisted = isPersisted; }
    }

    /// <summary>S11, W5: flush의 부분 결과. StoreErrors와 전송 결과는 독립 집계한다.</summary>
    public sealed class FlushResult
    {
        /// <summary>S11a: 성공 응답 수.</summary>
        public int Succeeded { get; }
        /// <summary>S11a: 영구 거절 응답 수.</summary>
        public int Rejected { get; }
        /// <summary>S11a: 재시도/StopKeep 결과 수.</summary>
        public int Remaining { get; }
        /// <summary>S11b: 아직 기한이 안 된 항목 수.</summary>
        public int SkippedNotDue { get; }
        /// <summary>S11c: 누적 시도 한도에 도달한 항목 수.</summary>
        public int Stalled { get; }
        /// <summary>W5, S11i: 항목 저장/제거 오류 수.</summary>
        public int StoreErrors { get; }
        /// <summary>S7, S11: 호출자 취소로 중단했는지 여부.</summary>
        public bool Canceled { get; }
        /// <summary>S11: 값만 보관한다.</summary>
        public FlushResult(int succeeded, int rejected, int remaining, int skippedNotDue, int stalled, int storeErrors, bool canceled)
        { Succeeded = succeeded; Rejected = rejected; Remaining = remaining; SkippedNotDue = skippedNotDue; Stalled = stalled; StoreErrors = storeErrors; Canceled = canceled; }
    }

    /// <summary>C1–C4, V3: 원시 예외를 분류 전에 정규화한 데이터.</summary>
    public sealed class AttemptOutcome
    {
        /// <summary>V3: 정규화한 종류.</summary>
        public AttemptOutcomeKind Kind { get; }
        /// <summary>C1–C4: 응답 또는 null.</summary>
        public TransportResponse? Response { get; }
        /// <summary>S9: 원래 예외 또는 null.</summary>
        public Exception? Exception { get; }
        /// <summary>C1–C4: 값만 보관한다.</summary>
        public AttemptOutcome(AttemptOutcomeKind kind, TransportResponse? response, Exception? exception)
        { Kind = kind; Response = response; Exception = exception; }
    }

    /// <summary>B1–B4, S5, S6, S8, S11: 재시도 설정 데이터.</summary>
    public sealed class RetryPolicy
    {
        /// <summary>S5: 한 SubmitAsync의 최대 전송 수.</summary>
        public int MaxAttempts { get; }
        /// <summary>B1: 지수 지연의 시작 값.</summary>
        public TimeSpan BaseDelay { get; }
        /// <summary>B1, V9: 지터 적용 전 계산 지연 상한.</summary>
        public TimeSpan MaxDelay { get; }
        /// <summary>B2, B3: 0–1 범위 지터 비율.</summary>
        public double JitterRatio { get; }
        /// <summary>S8: 개별 시도의 제한 시간.</summary>
        public TimeSpan PerAttemptTimeout { get; }
        /// <summary>S11c, W3: 누적 전송 한도.</summary>
        public int MaxOutboxAttempts { get; }
        /// <summary>S6, W4: 인라인 대기 한도; Retry-After 자체는 자르지 않는다.</summary>
        public TimeSpan MaxInlineRetryAfter { get; }
        /// <summary>Contract: 기본값 5회, 1초, 10초, 0.2, 5초, 누적 20회, 인라인 30초.</summary>
        public static RetryPolicy Default => new RetryPolicy(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10),
            0.2, TimeSpan.FromSeconds(5), 20, TimeSpan.FromSeconds(30));
        /// <summary>Contract: 검증 없이 값만 보관한다.</summary>
        public RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay, double jitterRatio,
            TimeSpan perAttemptTimeout, int maxOutboxAttempts, TimeSpan maxInlineRetryAfter)
        { MaxAttempts = maxAttempts; BaseDelay = baseDelay; MaxDelay = maxDelay; JitterRatio = jitterRatio; PerAttemptTimeout = perAttemptTimeout; MaxOutboxAttempts = maxOutboxAttempts; MaxInlineRetryAfter = maxInlineRetryAfter; }
    }
}
