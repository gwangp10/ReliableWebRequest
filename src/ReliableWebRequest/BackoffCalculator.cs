using System;

namespace ReliableWebRequest
{
    /// <summary>상한과 지터를 적용한 지수 백오프를 계산한다.</summary>
    public static class BackoffCalculator
    {
        /// <summary>1부터 시작하는 n에 base * 2^(n-1)을 MaxDelay로 제한한 뒤
        /// capped * JitterRatio * (2r-1)을 더한다. 음수는 0이며 n &lt; 1이면 ArgumentOutOfRangeException.</summary>
        public static TimeSpan GetDelay(int attemptNumber, RetryPolicy policy, IRandom random)
        {
            if (attemptNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(attemptNumber));

            var capped = Math.Min(policy.BaseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1),
                policy.MaxDelay.TotalMilliseconds);
            var jitter = capped * policy.JitterRatio * (2 * random.NextDouble() - 1);
            return TimeSpan.FromMilliseconds(Math.Max(0, capped + jitter));
        }
    }
}
