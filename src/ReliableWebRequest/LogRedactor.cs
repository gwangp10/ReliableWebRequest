using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReliableWebRequest
{
    /// <summary>JSON 및 form/query 문자열의 민감 값을 가린다.</summary>
    public static class LogRedactor
    {
        private static readonly Regex FormValue = new Regex(
            @"(^|[?&\s])((?:receipt|signature|token|email)=)[^&\s#""',;}]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>receipt/signature/token/email 값을 ***로 바꾼다. JSON 이스케이프와 공백을 처리하며
        /// 키와 무관 값은 보존한다. 반복 적용 결과는 동일하다.</summary>
        public static string Redact(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Object ||
                    document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    using var stream = new MemoryStream();
                    using (var writer = new Utf8JsonWriter(stream))
                        WriteRedacted(writer, document.RootElement);
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch (JsonException) { /* JSON이 아니면 form/query 규칙을 적용한다. */ }
            return FormValue.Replace(text, "$1$2***");
        }

        private static void WriteRedacted(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (IsSensitive(property.Name)) writer.WriteStringValue("***");
                        else WriteRedacted(writer, property.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray()) WriteRedacted(writer, item);
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(FormValue.Replace(element.GetString()!, "$1$2***"));
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static bool IsSensitive(string name) =>
            string.Equals(name, "receipt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "signature", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "token", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "email", StringComparison.OrdinalIgnoreCase);
    }
}
