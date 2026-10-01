using System;
using System.Collections.Generic;

namespace ReliableWebRequest
{
    /// <summary>로그 수준.</summary>
    public enum LogLevel { Info, Warning, Error }
    /// <summary>정규화된 시도 종류. 호출자 취소는 별도 처리한다.</summary>
    public enum AttemptOutcomeKind { Response, TransportError, AttemptTimedOut, Unexpected }
    /// <summary>분류 결과.</summary>
    public enum RetryDecision { Succeed, Retry, RejectPermanently, StopKeep }
    /// <summary>최종 결과 종류.</summary>
    public enum SubmitResultKind { Succeeded, RejectedPermanently, DeferredToOutbox, Canceled, FailedNotPersisted }

    /// <summary>전송할 HTTP 요청 데이터.</summary>
    public sealed class TransportRequest
    {
        /// <summary>HTTP 메서드.</summary>
        public string Method { get; }
        /// <summary>주입된 엔드포인트.</summary>
        public Uri Url { get; }
        /// <summary>Idempotency-Key 및 Content-Type.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>영수증 JSON.</summary>
        public string Body { get; }
        /// <summary>값만 보관한다.</summary>
        public TransportRequest(string method, Uri url, IReadOnlyDictionary<string, string> headers, string body)
        {
            Method = method;
            Url = url;
            Headers = headers;
            Body = body;
        }
    }

    /// <summary>HTTP 응답 데이터.</summary>
    public sealed class TransportResponse
    {
        /// <summary>HTTP 상태 코드.</summary>
        public int StatusCode { get; }
        /// <summary>응답 헤더.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }
        /// <summary>민감 값이 포함될 수 있는 응답 본문.</summary>
        public string Body { get; }
        /// <summary>값만 보관한다.</summary>
        public TransportResponse(int statusCode, IReadOnlyDictionary<string, string> headers, string body)
        {
            StatusCode = statusCode;
            Headers = headers;
            Body = body;
        }
    }

    /// <summary>구매 영수증 데이터.</summary>
    public sealed class PurchaseReceipt
    {
        /// <summary>상품 식별자.</summary>
        public string ProductId { get; }
        /// <summary>UTF-8 해시의 입력인 스토어 거래 식별자.</summary>
        public string TransactionId { get; }
        /// <summary>로그에 노출하면 안 되는 영수증.</summary>
        public string Receipt { get; }
        /// <summary>로그에 노출하면 안 되는 서명.</summary>
        public string Signature { get; }
        /// <summary>값만 보관한다.</summary>
        public PurchaseReceipt(string productId, string transactionId, string receipt, string signature)
        {
            ProductId = productId;
            TransactionId = transactionId;
            Receipt = receipt;
            Signature = signature;
        }
    }

    /// <summary>저장되는 대기 항목. 시도 횟수는 실제 시작한 전송의 누계이다.</summary>
    public sealed class PendingSubmission
    {
        /// <summary>최초 키를 유지한다.</summary>
        public string IdempotencyKey { get; }
        /// <summary>최초 영수증 데이터.</summary>
        public PurchaseReceipt Receipt { get; }
        /// <summary>flush의 처리 순서 기준.</summary>
        public DateTimeOffset CreatedAt { get; }
        /// <summary>시작한 SendAsync 횟수, 취소 및 타임아웃 시도도 포함.</summary>
        public int AttemptCount { get; }
        /// <summary>현재 시각 + 예약 지연. 전송 전/중 호출자 취소는 현재 시각이며 대기 중 취소는 예약을 보존한다.</summary>
        public DateTimeOffset NextAttemptAt { get; }
        /// <summary>마지막 보류 사유.</summary>
        public string? LastReason { get; }
        /// <summary>값만 보관한다.</summary>
        public PendingSubmission(string idempotencyKey, PurchaseReceipt receipt, DateTimeOffset createdAt,
            int attemptCount, DateTimeOffset nextAttemptAt, string? lastReason)
        {
            IdempotencyKey = idempotencyKey;
            Receipt = receipt;
            CreatedAt = createdAt;
            AttemptCount = attemptCount;
            NextAttemptAt = nextAttemptAt;
            LastReason = lastReason;
        }
    }

    /// <summary>성공/영구거절, 취소, 보류 순서로 결정한 결과.</summary>
    public sealed class SubmitResult
    {
        /// <summary>최종 결과 종류.</summary>
        public SubmitResultKind Kind { get; }
        /// <summary>응답이 있으면 HTTP 상태 코드.</summary>
        public int? StatusCode { get; }
        /// <summary>예외 타입 또는 already pending 등의 사유.</summary>
        public string? Reason { get; }
        /// <summary>이번 호출에서 저장 성공 이력이 있고 이후 제거 성공이 없으면 true.
        /// 기존 대기 항목 반환도 true. 제거 실패만으로 true가 되지 않는다.</summary>
        public bool IsPersisted { get; }
        /// <summary>검증이나 팩터리 동작 없이 값만 보관한다.</summary>
        public SubmitResult(SubmitResultKind kind, int? statusCode, string? reason, bool isPersisted)
        {
            Kind = kind;
            StatusCode = statusCode;
            Reason = reason;
            IsPersisted = isPersisted;
        }
    }

    /// <summary>flush의 부분 결과. StoreErrors와 전송 결과는 독립 집계한다.</summary>
    public sealed class FlushResult
    {
        /// <summary>성공 응답 수.</summary>
        public int Succeeded { get; }
        /// <summary>영구 거절 응답 수.</summary>
        public int Rejected { get; }
        /// <summary>재시도/StopKeep 결과 수.</summary>
        public int Remaining { get; }
        /// <summary>아직 기한이 안 된 항목 수.</summary>
        public int SkippedNotDue { get; }
        /// <summary>누적 시도 한도에 도달한 항목 수.</summary>
        public int Stalled { get; }
        /// <summary>항목 저장/제거 오류 수.</summary>
        public int StoreErrors { get; }
        /// <summary>호출자 취소로 중단했는지 여부.</summary>
        public bool Canceled { get; }
        /// <summary>값만 보관한다.</summary>
        public FlushResult(int succeeded, int rejected, int remaining, int skippedNotDue, int stalled, int storeErrors, bool canceled)
        {
            Succeeded = succeeded;
            Rejected = rejected;
            Remaining = remaining;
            SkippedNotDue = skippedNotDue;
            Stalled = stalled;
            StoreErrors = storeErrors;
            Canceled = canceled;
        }
    }

    /// <summary>원시 예외를 분류 전에 정규화한 데이터.</summary>
    public sealed class AttemptOutcome
    {
        /// <summary>정규화한 종류.</summary>
        public AttemptOutcomeKind Kind { get; }
        /// <summary>응답 또는 null.</summary>
        public TransportResponse? Response { get; }
        /// <summary>원래 예외 또는 null.</summary>
        public Exception? Exception { get; }
        /// <summary>값만 보관한다.</summary>
        public AttemptOutcome(AttemptOutcomeKind kind, TransportResponse? response, Exception? exception)
        {
            Kind = kind;
            Response = response;
            Exception = exception;
        }
    }

    /// <summary>재시도 설정 데이터.</summary>
    public sealed class RetryPolicy
    {
        /// <summary>한 SubmitAsync의 최대 전송 수.</summary>
        public int MaxAttempts { get; }
        /// <summary>지수 지연의 시작 값.</summary>
        public TimeSpan BaseDelay { get; }
        /// <summary>지터 적용 전 계산 지연 상한.</summary>
        public TimeSpan MaxDelay { get; }
        /// <summary>0–1 범위 지터 비율.</summary>
        public double JitterRatio { get; }
        /// <summary>개별 시도의 제한 시간.</summary>
        public TimeSpan PerAttemptTimeout { get; }
        /// <summary>누적 전송 한도.</summary>
        public int MaxOutboxAttempts { get; }
        /// <summary>인라인 대기 한도; Retry-After 자체는 자르지 않는다.</summary>
        public TimeSpan MaxInlineRetryAfter { get; }
        /// <summary>기본값 5회, 1초, 10초, 0.2, 5초, 누적 20회, 인라인 30초.</summary>
        public static RetryPolicy Default => new RetryPolicy(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10),
            0.2, TimeSpan.FromSeconds(5), 20, TimeSpan.FromSeconds(30));
        /// <summary>검증 없이 값만 보관한다.</summary>
        public RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay, double jitterRatio,
            TimeSpan perAttemptTimeout, int maxOutboxAttempts, TimeSpan maxInlineRetryAfter)
        {
            MaxAttempts = maxAttempts;
            BaseDelay = baseDelay;
            MaxDelay = maxDelay;
            JitterRatio = jitterRatio;
            PerAttemptTimeout = perAttemptTimeout;
            MaxOutboxAttempts = maxOutboxAttempts;
            MaxInlineRetryAfter = maxInlineRetryAfter;
        }
    }
}
