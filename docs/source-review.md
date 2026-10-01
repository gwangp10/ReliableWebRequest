# src 수동 검토

검토 대상은 직접 작성한 `Abstractions.cs`, `Data.cs`, `Stubs.cs`입니다. `obj/`의 SDK 생성 코드는 제외했습니다. **동작 구현은 없습니다.** 생성자도 단순 데이터 저장 이외의 동작은 넣지 않았습니다.

## throw 이외의 본문이 있는 모든 멤버

| 멤버 | 본문 내용 | 판정 |
|---|---|---|
| TransportRequest 생성자 | Method, Url, Headers, Body에 인자 대입 | 데이터 초기화만 |
| TransportResponse 생성자 | StatusCode, Headers, Body에 인자 대입 | 데이터 초기화만 |
| PurchaseReceipt 생성자 | ProductId, TransactionId, Receipt, Signature에 인자 대입 | 데이터 초기화만 |
| PendingSubmission 생성자 | IdempotencyKey, Receipt, CreatedAt, AttemptCount, NextAttemptAt, LastReason에 인자 대입 | 데이터 초기화만 |
| SubmitResult 생성자 | Kind, StatusCode, Reason, IsPersisted에 인자 대입 | 데이터 초기화만 |
| FlushResult 생성자 | Succeeded, Rejected, Remaining, SkippedNotDue, Stalled, StoreErrors, Canceled에 인자 대입 | 데이터 초기화만 |
| AttemptOutcome 생성자 | Kind, Response, Exception에 인자 대입 | 데이터 초기화만 |
| RetryPolicy 생성자 | MaxAttempts, BaseDelay, MaxDelay, JitterRatio, PerAttemptTimeout, MaxOutboxAttempts, MaxInlineRetryAfter에 인자 대입 | 데이터 초기화만 |
| RetryPolicy.Default getter | new RetryPolicy(5, 1초, 10초, 0.2, 5초, 20, 30초) | 명시 허용된 상수 기본값만 |

검증, 분기, 반복, 키 생성, 분류, 전송, JSON 생성, 팩터리 로직은 위 본문에 없습니다. Default의 TimeSpan.FromSeconds 호출은 상수 기간을 표현하는 용도로만 쓰였습니다.

## 본문 없는 모든 선언 멤버

| 형식 | 선언 멤버 |
|---|---|
| ITransport | SendAsync |
| IDelay | DelayAsync |
| IClock | UtcNow getter |
| IRandom | NextDouble |
| ILogSink | Write |
| IOutboxStore | GetAsync, SaveAsync, LoadAllAsync, RemoveAsync |
| IAttemptTimeoutFactory | Create |
| LogLevel | Info, Warning, Error |
| AttemptOutcomeKind | Response, TransportError, AttemptTimedOut, Unexpected |
| RetryDecision | Succeed, Retry, RejectPermanently, StopKeep |
| SubmitResultKind | Succeeded, RejectedPermanently, DeferredToOutbox, Canceled, FailedNotPersisted |
| TransportRequest | Method, Url, Headers, Body의 get-only 자동 속성 |
| TransportResponse | StatusCode, Headers, Body의 get-only 자동 속성 |
| PurchaseReceipt | ProductId, TransactionId, Receipt, Signature의 get-only 자동 속성 |
| PendingSubmission | IdempotencyKey, Receipt, CreatedAt, AttemptCount, NextAttemptAt, LastReason의 get-only 자동 속성 |
| SubmitResult | Kind, StatusCode, Reason, IsPersisted의 get-only 자동 속성 |
| FlushResult | Succeeded, Rejected, Remaining, SkippedNotDue, Stalled, StoreErrors, Canceled의 get-only 자동 속성 |
| AttemptOutcome | Kind, Response, Exception의 get-only 자동 속성 |
| RetryPolicy | MaxAttempts, BaseDelay, MaxDelay, JitterRatio, PerAttemptTimeout, MaxOutboxAttempts, MaxInlineRetryAfter의 get-only 자동 속성 |

## 스텁 본문

다음 9개 멤버는 `throw new NotImplementedException("TODO(user): <ids>")` 한 문장만 포함합니다.

| 멤버 | TODO ID |
|---|---|
| BackoffCalculator.GetDelay | B1-B4 |
| RetryClassifier.Classify | C1-C4 |
| RetryAfterParser.TryParse | R1-R5 |
| LogRedactor.Redact | L1-L4 |
| IdempotencyKey.FromTransactionId | K1 |
| DefaultAttemptTimeoutFactory.Create | S8 W6 |
| PurchaseSubmitter 생성자 | S1-S16 W1-W7 (constructor) |
| PurchaseSubmitter.SubmitAsync | S1-S16 W1-W7 |
| PurchaseSubmitter.FlushOutboxAsync | S11a-S11i W3-W6 |

`rg -n '\b(return|if|for|while)\b|=>' src/ReliableWebRequest -g '*.cs' -g '!**/obj/**'` 결과는 `RetryPolicy.Default`의 상수 getter 한 건뿐입니다. XML 주석으로 계약과 명세 ID를 제공했습니다. 레코드, init setter, netstandard2.1 이후 API를 사용하지 않았습니다.
