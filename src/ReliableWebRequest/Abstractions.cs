using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReliableWebRequest
{
    /// <summary>S1–S16: 유일한 전송 경계. 요청의 자원은 어댑터가 정리한다.</summary>
    public interface ITransport
    {
        /// <summary>S7b, S8, W6: 토큰을 관찰하고 취소 시 OperationCanceledException을 던져야 한다.</summary>
        Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct);
    }

    /// <summary>S2, S6, S7: 재시도 사이 대기 경계.</summary>
    public interface IDelay
    {
        /// <summary>W6: 시도 토큰이 아닌 호출자 토큰으로 대기한다.</summary>
        Task DelayAsync(TimeSpan delay, CancellationToken ct);
    }

    /// <summary>R2, R4, S11: 주입 가능한 UTC 시계.</summary>
    public interface IClock
    {
        /// <summary>현재 UTC 시각.</summary>
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>B2, B3: 지터 난수 경계.</summary>
    public interface IRandom
    {
        /// <summary>V9: [0, 1) 범위의 값을 반환한다.</summary>
        double NextDouble();
    }

    /// <summary>S10, S14: 민감 값은 호출 전에 가려야 한다.</summary>
    public interface ILogSink
    {
        /// <summary>S4b, S10, S14: 수준과 정제된 메시지를 기록한다.</summary>
        void Write(LogLevel level, string message);
    }

    /// <summary>S1, S5, S11, S15, S16: 재시작을 넘는 저장 경계. 메서드에는 취소 토큰이 없다.</summary>
    public interface IOutboxStore
    {
        /// <summary>W2: 키로 조회한다. 기존 항목은 SubmitAsync에서 덮어쓰지 않는다.</summary>
        Task<PendingSubmission?> GetAsync(string idempotencyKey);
        /// <summary>V2: 키 기준 upsert. 같은 키의 항목은 교체하며 중복을 만들지 않는다.</summary>
        Task SaveAsync(PendingSubmission item);
        /// <summary>S11, W5: 저장 항목 전체를 읽는다. 반환 순서는 보장하지 않는다.</summary>
        Task<IReadOnlyList<PendingSubmission>> LoadAllAsync();
        /// <summary>W1, W5: 키에 해당하는 항목을 제거한다.</summary>
        Task RemoveAsync(string idempotencyKey);
    }

    /// <summary>S7b, S8, W6: 시도별 취소 자원을 제공한다.</summary>
    public interface IAttemptTimeoutFactory
    {
        /// <summary>W6: 호출자 토큰에 연결되고 timeout 뒤 취소되는 CTS를 반환한다.
        /// 소유자인 submitter는 시도 종료 시 Dispose하며, 토큰은 SendAsync에만 전달한다.</summary>
        CancellationTokenSource Create(TimeSpan timeout, CancellationToken callerToken);
    }
}
