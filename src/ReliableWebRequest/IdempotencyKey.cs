using System;
using System.Security.Cryptography;
using System.Text;

namespace ReliableWebRequest
{
    /// <summary>거래 ID에서 재시작 후에도 동일한 멱등 키를 만든다.</summary>
    public static class IdempotencyKey
    {
        /// <summary>거래 ID의 UTF-8 SHA-256을 소문자 64자리 hex로 표현하고 pur_를 붙인다.
        /// null, 빈 문자열, 공백 입력은 ArgumentException을 던진다.</summary>
        public static string FromTransactionId(string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
                throw new ArgumentException("A transaction ID is required.", nameof(transactionId));

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(transactionId));
            var key = new StringBuilder("pur_", 68);
            foreach (var value in hash)
                key.Append(value.ToString("x2"));
            return key.ToString();
        }
    }
}
