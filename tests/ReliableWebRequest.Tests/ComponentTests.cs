using System.Globalization;
using System.Text.Json;
using NUnit.Framework;

namespace ReliableWebRequest.Tests
{
    [TestFixture]
    public sealed class BackoffCalculatorTests
    {
        [Test] // B1
        public void Backoff_GrowsExponentially_AndCapsBeforeJitter()
        {
            Assert.That(Enumerable.Range(1, 5).Select(n => BackoffCalculator.GetDelay(n, Fixture.PolicyWith(), new FixedRandom()).TotalSeconds),
                Is.EqualTo(new[] { 1d, 2, 4, 8, 10 }));
        }
        [Test] // B2
        public void Backoff_CappedAttempt_HasSpecifiedJitterEndpoints()
        {
            Assert.That(BackoffCalculator.GetDelay(5, Fixture.PolicyWith(.2), new FixedRandom(0)).TotalSeconds, Is.EqualTo(8));
            Assert.That(BackoffCalculator.GetDelay(5, Fixture.PolicyWith(.2), new FixedRandom(.999999)).TotalSeconds, Is.EqualTo(12).Within(.001));
        }
        [Test] // B3
        public void Backoff_StaysInsideNonnegativeJitteredCap()
        {
            foreach (var jitter in new[] { 0d, .2, 1 })
            foreach (var random in new[] { 0d, .5, .999999 })
            foreach (var attempt in new[] { 1, 5, 20, 100 })
                Assert.That(BackoffCalculator.GetDelay(attempt, Fixture.PolicyWith(jitter), new FixedRandom(random)).TotalSeconds,
                    Is.InRange(0, 10 * (1 + jitter)));
        }
        [TestCase(0), TestCase(-1)] // B4
        public void Backoff_InvalidAttempt_ThrowsArgumentOutOfRange(int attempt)
        {
            Assert.That(() => BackoffCalculator.GetDelay(attempt, Fixture.PolicyWith(), new FixedRandom()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    [TestFixture]
    public sealed class RetryClassifierTests
    {
        [TestCase(200), TestCase(201), TestCase(204)] // C1
        public void Classifier_ConfirmedResponse_Succeeds(int status) => Assert.That(
            RetryClassifier.Classify(new AttemptOutcome(AttemptOutcomeKind.Response, Fixture.Response(status), null)), Is.EqualTo(RetryDecision.Succeed));

        [TestCase(408), TestCase(429), TestCase(500), TestCase(502), TestCase(503), TestCase(504)]
        [TestCase(-1), TestCase(-2)] // C2: 음수는 응답이 아닌 정규화된 결과를 나타낸다.
        public void Classifier_TransientOutcome_Retries(int status)
        {
            var outcome = status == -1 ? new AttemptOutcome(AttemptOutcomeKind.TransportError, null, new HttpRequestException()) :
                status == -2 ? new AttemptOutcome(AttemptOutcomeKind.AttemptTimedOut, null, new OperationCanceledException()) :
                new AttemptOutcome(AttemptOutcomeKind.Response, Fixture.Response(status), null);
            Assert.That(RetryClassifier.Classify(outcome), Is.EqualTo(RetryDecision.Retry));
        }
        [TestCase(400), TestCase(401), TestCase(403), TestCase(404), TestCase(409), TestCase(422)] // C3
        public void Classifier_PermanentResponse_Rejects(int status) => Assert.That(
            RetryClassifier.Classify(new AttemptOutcome(AttemptOutcomeKind.Response, Fixture.Response(status), null)), Is.EqualTo(RetryDecision.RejectPermanently));

        [TestCase(202), TestCase(203), TestCase(205), TestCase(206), TestCase(299), TestCase(301), TestCase(418), TestCase(501), TestCase(-1)] // C4
        public void Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(int status)
        {
            var outcome = status == -1 ? new AttemptOutcome(AttemptOutcomeKind.Unexpected, null, new InvalidOperationException()) :
                new AttemptOutcome(AttemptOutcomeKind.Response, Fixture.Response(status), null);
            Assert.That(RetryClassifier.Classify(outcome), Is.EqualTo(RetryDecision.StopKeep));
        }
    }

    [TestFixture]
    public sealed class RetryAfterParserTests
    {
        [Test] // R1
        public void RetryAfter_DeltaSeconds_ParsesExactly()
        {
            Assert.That(RetryAfterParser.TryParse(new Dictionary<string, string> { ["Retry-After"] = "120" }, new FixedClock(), out var delay), Is.True);
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(120)));
        }
        [Test] // R2
        public void RetryAfter_FutureHttpDate_UsesInjectedClock()
        {
            var clock = new FixedClock();
            Assert.That(RetryAfterParser.TryParse(new Dictionary<string, string> { ["Retry-After"] = clock.UtcNow.AddSeconds(30).ToString("r", CultureInfo.InvariantCulture) }, clock, out var delay), Is.True);
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
        [TestCase(null), TestCase("garbage")] // R3
        public void RetryAfter_MissingOrInvalid_ReturnsFalse(string? value)
        {
            var headers = new Dictionary<string, string>();
            if (value != null) headers.Add("Retry-After", value);
            Assert.That(RetryAfterParser.TryParse(headers, new FixedClock(), out _), Is.False);
        }
        [Test] // R4
        public void RetryAfter_PastDate_ReturnsZero()
        {
            var clock = new FixedClock();
            Assert.That(RetryAfterParser.TryParse(new Dictionary<string, string> { ["Retry-After"] = clock.UtcNow.AddSeconds(-30).ToString("r", CultureInfo.InvariantCulture) }, clock, out var delay), Is.True);
            Assert.That(delay, Is.EqualTo(TimeSpan.Zero));
        }
        [Test] // R5
        public void RetryAfter_HeaderLookup_IsCaseInsensitive()
        {
            Assert.That(RetryAfterParser.TryParse(new Dictionary<string, string>(StringComparer.Ordinal) { ["rEtRy-AfTeR"] = "3" }, new FixedClock(), out var delay), Is.True);
            Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
    }

    [TestFixture]
    public sealed class LogRedactorTests
    {
        [TestCase("receipt"), TestCase("signature"), TestCase("token"), TestCase("email")] // L1
        public void Redactor_Json_MasksEachSensitiveValue(string key)
        {
            using var json = JsonDocument.Parse(LogRedactor.Redact("{\"" + key + "\":\"abc\"}"));
            Assert.That(json.RootElement.GetProperty(key).GetString(), Is.EqualTo("***"));
        }
        [Test] // L2
        public void Redactor_FormAndQuery_MasksValuesAndKeepsKeys()
        {
            const string input = "receipt=abc&productId=x&signature=def&token=ghi&email=jkl";
            const string expected = "receipt=***&productId=x&signature=***&token=***&email=***";
            Assert.That(LogRedactor.Redact(input), Is.EqualTo(expected));
            Assert.That(LogRedactor.Redact("https://example.invalid/?" + input), Is.EqualTo("https://example.invalid/?" + expected));
        }
        [Test] // L3
        public void Redactor_PreservesUnrelatedValues_AndIsIdempotent()
        {
            const string input = "{\"productId\":\"coins\",\"receipt\":\"abc\"}";
            var redacted = LogRedactor.Redact(input);
            using var json = JsonDocument.Parse(redacted);
            Assert.That(json.RootElement.GetProperty("productId").GetString(), Is.EqualTo("coins"));
            Assert.That(LogRedactor.Redact(redacted), Is.EqualTo(redacted));
            Assert.That(LogRedactor.Redact("productId=x&count=2"), Is.EqualTo("productId=x&count=2"));
        }
        [Test] // L4
        public void Redactor_EscapedJsonAndWhitespace_MasksWholeValues()
        {
            const string input = "{ \"receipt\" : \"a\\\"b\\\\c\", \"signature\":\"x y\" }";
            var redacted = LogRedactor.Redact(input);
            using var json = JsonDocument.Parse(redacted);
            Assert.That(json.RootElement.GetProperty("receipt").GetString(), Is.EqualTo("***"));
            Assert.That(json.RootElement.GetProperty("signature").GetString(), Is.EqualTo("***"));
            Assert.That(redacted, Does.Not.Contain("a\\\"b").And.Not.Contain("x y").And.Not.Contain("\\\\c"));
        }
    }

    [TestFixture]
    public sealed class IdempotencyKeyTests
    {
        [Test] // K1
        public void Key_IsDeterministicDistinctAndUtf8Sha256()
        {
            Assert.That(IdempotencyKey.FromTransactionId("abc"), Is.EqualTo(Fixture.AbcKey));
            Assert.That(IdempotencyKey.FromTransactionId("abc"), Is.EqualTo(IdempotencyKey.FromTransactionId("abc")));
            Assert.That(IdempotencyKey.FromTransactionId("abd"), Is.Not.EqualTo(Fixture.AbcKey).And.Match("^pur_[0-9a-f]{64}$"));
            var expected = "pur_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("거래-1"))).ToLowerInvariant();
            Assert.That(IdempotencyKey.FromTransactionId("거래-1"), Is.EqualTo(expected));
        }
        [TestCase(null), TestCase(""), TestCase(" \t\r\n")] // K1
        public void Key_InvalidTransactionId_ThrowsArgumentException(string? value) =>
            Assert.That(() => IdempotencyKey.FromTransactionId(value!), Throws.TypeOf<ArgumentException>());
    }

    [TestFixture]
    public sealed class AttemptTimeoutFactoryTests
    {
        [TestCase(true), TestCase(false)] // S8, W6: 실제 팩터리 계약
        public async Task TimeoutFactory_LinksCallerAndSchedulesTimeout(bool cancelCaller)
        {
            using var caller = new CancellationTokenSource();
            using var source = new DefaultAttemptTimeoutFactory().Create(
                cancelCaller ? TimeSpan.FromMinutes(10) : TimeSpan.FromMilliseconds(50), caller.Token);
            if (cancelCaller) Assert.That(source.IsCancellationRequested, Is.False, "A long timeout must not cancel immediately");
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = source.Token.Register(() => canceled.TrySetResult());
            if (cancelCaller) caller.Cancel();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(source.IsCancellationRequested, Is.True);
            Assert.That(caller.IsCancellationRequested, Is.EqualTo(cancelCaller));
        }
    }

    [TestFixture]
    public sealed class ContractTests
    {
        [Test] // Contract.RetryPolicy
        public void RetryPolicy_Default_HasSpecifiedConstants()
        {
            var p = RetryPolicy.Default;
            Assert.That(p.MaxAttempts, Is.EqualTo(5));
            Assert.That(p.BaseDelay, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(p.MaxDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(p.JitterRatio, Is.EqualTo(.2));
            Assert.That(p.PerAttemptTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(p.MaxOutboxAttempts, Is.EqualTo(20));
            Assert.That(p.MaxInlineRetryAfter, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
        [Test] // Contract.SubmitResult: V11에 따라 팩터리 대신 생성자를 검증한다.
        public void SubmitResult_Constructor_PreservesAllValues()
        {
            foreach (var kind in Enum.GetValues<SubmitResultKind>())
            {
                var result = new SubmitResult(kind, 503, "reason", true);
                Assert.That(result.Kind, Is.EqualTo(kind));
                Assert.That(result.StatusCode, Is.EqualTo(503));
                Assert.That(result.Reason, Is.EqualTo("reason"));
                Assert.That(result.IsPersisted, Is.True);
            }
            var empty = new SubmitResult(SubmitResultKind.Canceled, null, null, false);
            Assert.That(empty.StatusCode, Is.Null);
            Assert.That(empty.Reason, Is.Null);
            Assert.That(empty.IsPersisted, Is.False);
        }
    }
}
