using System.Text.RegularExpressions;

namespace ReliableWebRequest
{
    /// <summary>JSON 및 form/query 문자열의 민감 값을 가린다.</summary>
    public static class LogRedactor
    {
        private static readonly Regex JsonValue = new Regex(
            @"(""(?:receipt|signature|token|email)""\s*:\s*)""(?:\\.|[^""\\])*""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FormValue = new Regex(
            @"(^|[?&\s])((?:receipt|signature|token|email)=)[^&\s#]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>receipt/signature/token/email 값을 ***로 바꾼다. JSON 이스케이프와 공백을 처리하며
        /// 키와 무관 값은 보존한다. 반복 적용 결과는 동일하다.</summary>
        public static string Redact(string text)
        {
            var jsonRedacted = JsonValue.Replace(text, "$1\"***\"");
            return FormValue.Replace(jsonRedacted, "$1$2***");
        }
    }
}
