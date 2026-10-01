using System;
using System.Threading;

namespace ReliableWebRequest
{
    /// <summary>호출자 취소와 시도 제한 시간을 연결한다.</summary>
    public sealed class DefaultAttemptTimeoutFactory : IAttemptTimeoutFactory
    {
        /// <summary>연결 CTS에 CancelAfter를 설정한다. 반환한 CTS는 호출자가 소유하고 정리한다.</summary>
        public CancellationTokenSource Create(TimeSpan timeout, CancellationToken callerToken)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            try
            {
                source.CancelAfter(timeout);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
    }
}
