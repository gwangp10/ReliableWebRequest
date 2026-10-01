using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReliableWebRequest
{
    /// <summary>Retry-After의 초 단위 값과 HTTP-date를 해석한다.</summary>
    public static class RetryAfterParser
    {
        /// <summary>헤더 이름은 대소문자를 구분하지 않는다. 과거 날짜는 0초/true,
        /// 누락하거나 잘못된 값은 false. 날짜의 기준은 주입된 UTC 시계이다.</summary>
        public static bool TryParse(IReadOnlyDictionary<string, string> headers, IClock clock, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            foreach (var header in headers)
            {
                if (!string.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = header.Value.Trim();
                if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                {
                    if (seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
                        return false;
                    delay = TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond);
                    return true;
                }

                if (!DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var date))
                    return false;

                var remaining = date - clock.UtcNow;
                delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
                return true;
            }
            return false;
        }
    }
}
