using NUnit.Framework;

namespace ReliableWebRequest.Tests
{
    internal sealed class CallJournal
    {
        public List<string> Entries { get; } = new();
        public void Add(string entry) => Entries.Add(entry);
    }

    internal sealed class FakeTransport : ITransport
    {
        private readonly Queue<Func<CancellationToken, Task<TransportResponse>>> script = new();
        public CallJournal Journal { get; }
        public List<TransportRequest> Requests { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();
        public FakeTransport(CallJournal journal) => Journal = journal;
        public void Respond(int status, string? retryAfter = null, string body = "") =>
            script.Enqueue(_ => Task.FromResult(Fixture.Response(status, retryAfter, body)));
        public void Throw(Exception error) => script.Enqueue(_ => Task.FromException<TransportResponse>(error));
        public void Enqueue(Func<CancellationToken, Task<TransportResponse>> step) => script.Enqueue(step);
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            Tokens.Add(ct);
            Journal.Add("Send:start");
            if (script.Count == 0) throw new AssertionException("Unexpected extra SendAsync call");
            return script.Dequeue()(ct);
        }
    }

    internal sealed class RecordingDelay : IDelay
    {
        public List<TimeSpan> Delays { get; } = new();
        public List<CancellationToken> Tokens { get; } = new();
        public Action? OnDelay { get; set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            Tokens.Add(ct);
            OnDelay?.Invoke();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    internal sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    internal sealed class FixedRandom : IRandom
    {
        private readonly double value;
        public FixedRandom(double value = 0.5) => this.value = value;
        public double NextDouble() => value;
    }

    internal sealed class MemoryLog : ILogSink
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public void Write(LogLevel level, string message) => Entries.Add((level, message));
    }

    internal sealed class MemoryOutbox : IOutboxStore
    {
        public Dictionary<string, PendingSubmission> Items { get; } = new();
        public List<PendingSubmission> Saves { get; } = new();
        public List<string> Removes { get; } = new();
        public CallJournal Journal { get; }
        public Func<int, bool>? FailSave { get; set; }
        public Func<string, bool>? FailRemove { get; set; }
        public bool FailGet { get; set; }
        public bool FailLoad { get; set; }
        public int SaveCalls { get; private set; }
        public TaskCompletionSource? SaveGate { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MemoryOutbox(CallJournal journal) => Journal = journal;
        public Task<PendingSubmission?> GetAsync(string key)
        {
            Journal.Add("Get:start");
            if (FailGet) throw new IOException("get failure");
            Items.TryGetValue(key, out var item);
            Journal.Add("Get:complete");
            return Task.FromResult(item);
        }
        public async Task SaveAsync(PendingSubmission item)
        {
            Journal.Add("Save:start");
            SaveCalls++;
            if (FailSave?.Invoke(SaveCalls) == true) throw new IOException("save failure");
            SaveStarted.TrySetResult();
            if (SaveGate != null) await SaveGate.Task;
            Saves.Add(item);
            Items[item.IdempotencyKey] = item;
            Journal.Add("Save:complete");
        }
        public Task<IReadOnlyList<PendingSubmission>> LoadAllAsync()
        {
            Journal.Add("Load:start");
            if (FailLoad) throw new IOException("load failure");
            return Task.FromResult<IReadOnlyList<PendingSubmission>>(Items.Values.ToArray());
        }
        public Task RemoveAsync(string key)
        {
            Removes.Add(key);
            Journal.Add("Remove:start");
            if (FailRemove?.Invoke(key) == true) throw new IOException("remove failure");
            Items.Remove(key);
            Journal.Add("Remove:complete");
            return Task.CompletedTask;
        }
    }

    internal sealed class TrackingCts : CancellationTokenSource
    {
        private readonly CancellationTokenRegistration link;
        public int DisposeCount { get; private set; }
        public TrackingCts(CancellationToken caller) => link = caller.Register(() => Cancel());
        protected override void Dispose(bool disposing)
        {
            if (disposing) { DisposeCount++; link.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ManualTimeoutFactory : IAttemptTimeoutFactory
    {
        public List<TrackingCts> Sources { get; } = new();
        public List<TimeSpan> Timeouts { get; } = new();
        public CancellationTokenSource Create(TimeSpan timeout, CancellationToken callerToken)
        {
            var source = new TrackingCts(callerToken);
            Sources.Add(source);
            Timeouts.Add(timeout);
            return source;
        }
        public void Trigger(int attemptIndex) => Sources[attemptIndex].Cancel();
    }

    internal sealed class PendingUntilCanceledTransport : ITransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<CancellationToken> Tokens { get; } = new();
        public int SendCount { get; private set; }
        public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct)
        {
            SendCount++;
            Tokens.Add(ct);
            if (SendCount > 1) return Fixture.Response(200);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new AssertionException("Pending transport completed without cancellation");
        }
    }

    internal sealed class Fixture
    {
        public const string AbcKey = "pur_ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        public CallJournal Journal { get; } = new();
        public FakeTransport Transport { get; }
        public MemoryOutbox Outbox { get; }
        public RecordingDelay Delay { get; } = new();
        public FixedClock Clock { get; } = new();
        public MemoryLog Log { get; } = new();
        public ManualTimeoutFactory Timeouts { get; } = new();
        public RetryPolicy Policy { get; set; } = PolicyWith();
        public PurchaseReceipt Receipt { get; } = new("coins", "abc", "SECRET_RECEIPT_923", "SECRET_SIGNATURE_456");
        public Uri Endpoint { get; } = new("https://example.invalid/purchase");
        public Fixture() { Transport = new FakeTransport(Journal); Outbox = new MemoryOutbox(Journal); }
        public PurchaseSubmitter Create(ITransport? transport = null) =>
            new(transport ?? Transport, Policy, Delay, Clock, new FixedRandom(), Log, Outbox, Timeouts, Endpoint);
        public static RetryPolicy PolicyWith(double jitter = 0, int maxAttempts = 5) =>
            new(maxAttempts, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), jitter, TimeSpan.FromSeconds(5), 20, TimeSpan.FromSeconds(30));
        public static TransportResponse Response(int status, string? retryAfter = null, string body = "") =>
            new(status, retryAfter == null ? new Dictionary<string, string>() : new() { ["Retry-After"] = retryAfter }, body);
        public PendingSubmission Seed(string key, int count = 0, int dueSeconds = 0, int createdSeconds = 0)
        {
            var item = new PendingSubmission(key, Receipt, Clock.UtcNow.AddSeconds(createdSeconds), count,
                Clock.UtcNow.AddSeconds(dueSeconds), "original reason");
            Outbox.Items.Add(key, item);
            return item;
        }
        public void AssertDisposed(int count)
        {
            Assert.That(Timeouts.Sources, Has.Count.EqualTo(count));
            Assert.That(Timeouts.Sources.Select(s => s.DisposeCount), Is.All.EqualTo(1));
            Assert.That(Timeouts.Timeouts, Is.All.EqualTo(Policy.PerAttemptTimeout));
        }
        // A faulted operation wins over the start signal, preserving its original stub failure.
        public static async Task AwaitStarted(Task operation, Task started)
        {
            var winner = await Task.WhenAny(operation, started).WaitAsync(TimeSpan.FromSeconds(5));
            if (winner == operation) await operation;
            await started.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
