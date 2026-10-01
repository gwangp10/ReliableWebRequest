# 구매 영수증 전송 동작 명세

이 문서는 현재 라이브러리의 동작과 테스트 계약을 정의합니다. 이전 회사 프로젝트의 결제 전송 모듈에서 경험한 문제를 일반화한 개인 코드 샘플이며, 실제 전송 어댑터와 영속 저장소는 사용 환경에서 제공합니다. 테스트 메서드와의 연결은 [명세별 테스트](spec-test-map.md), 서버 측 요구 사항은 [서버 계약](server-contract.md)을 참조합니다.

## 실행 경계와 데이터

`PurchaseSubmitter`는 전송(`ITransport`), 정책(`RetryPolicy`), 대기(`IDelay`), 시계(`IClock`), 난수(`IRandom`), 로그(`ILogSink`), 저장소(`IOutboxStore`), 타임아웃 팩터리(`IAttemptTimeoutFactory`), 목적지(`Uri`)를 주입받습니다. 진입점은 `SubmitAsync(receipt, ct)`와 `FlushOutboxAsync(ct)`입니다. 같은 저장소를 사용하는 호출은 호출자가 직렬화해야 합니다. 동시 실행 잠금이나 분산 작업 소유권은 제공하지 않습니다.

라이브러리는 netstandard2.1과 C# 9를 대상으로 하며 JSON 직렬화에는 System.Text.Json을 사용합니다. Unity 이식은 미검증입니다.

### D10 데이터 계약

데이터 클래스는 생성자로 받은 값을 읽기 전용 속성에 보관합니다. 생성자에서 검증·키 생성·결과 분류를 수행하지 않습니다. `RetryPolicy.Default`는 아래 기본값을 담은 정책을 반환합니다.

| 데이터 | 필드와 의미 |
|---|---|
| `PurchaseReceipt` | `ProductId`, `TransactionId`, `Receipt`, `Signature` |
| `TransportRequest` | `Method`, `Url`, `Headers`, `Body` |
| `TransportResponse` | `StatusCode`, `Headers`, `Body` |
| `PendingSubmission` | 원래 `IdempotencyKey`와 `Receipt`, 생성 시각 `CreatedAt`, 누적 `AttemptCount`, 예약 시각 `NextAttemptAt`, nullable `LastReason` |
| `AttemptOutcome` | `Kind`, nullable `Response`, nullable `Exception` |
| `SubmitResult` | `Kind`, nullable `StatusCode`, nullable `Reason`, `IsPersisted` |
| `FlushResult` | `Succeeded`, `Rejected`, `Remaining`, `SkippedNotDue`, `Stalled`, `StoreErrors`, `Canceled` |

| 정책 | 기본값 |
|---|---|
| `MaxAttempts` | 한 Submit 호출에서 최대 5회 |
| `BaseDelay` | 1초 |
| `MaxDelay` | 지터 적용 전 백오프 상한 10초 |
| `JitterRatio` | 0.2 |
| `PerAttemptTimeout` | 시도별 5초 |
| `MaxOutboxAttempts` | 누적 20회 |
| `MaxInlineRetryAfter` | 인라인 대기 한도 30초 |

`Contract.RetryPolicy`는 이 기본값을, `Contract.SubmitResult`는 모든 결과 종류와 nullable 값을 생성자가 그대로 보존함을 검사합니다.

## 제출과 저장

### D7 멱등 키

`IdempotencyKey.FromTransactionId`는 거래 ID의 UTF-8 바이트를 SHA-256으로 해시하고 소문자 64자리 16진수 앞에 `pur_`를 붙입니다. 입력을 잘라내거나 정규화하지 않습니다. null·빈 문자열·공백뿐인 문자열은 `ArgumentException`입니다. 같은 거래는 재시도, 별도 호출, 새 submitter 인스턴스에서도 같은 키를 사용합니다. Flush는 저장된 원래 키를 그대로 사용합니다.

K1의 정확한 벡터는 다음과 같습니다.

```text
abc → pur_ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
```

요청은 주입된 목적지에 대한 POST이며 `Idempotency-Key`와 `Content-Type: application/json` 헤더를 가집니다. JSON에는 `productId`, `transactionId`, `receipt`, `signature`를 원래 값으로 넣습니다. 서버는 같은 키·같은 payload의 재전송을 중복 처리하지 않아야 합니다.

### D2 대기 중 거래 재제출

Submit은 먼저 키로 `GetAsync`를 완료합니다. 기존 항목이 있으면 전송·저장·제거 없이 `DeferredToOutbox`, `IsPersisted=true`, `Reason="already pending"`을 반환합니다. 호출자 토큰이 취소되어 있어도 이 조회 결과를 즉시 반환합니다. 기존 영수증, 생성 시각, 누적 횟수, 예약 시각, 사유를 보존하며 이후 처리는 Flush에 맡깁니다.

항목이 없으면 `AttemptCount=0`, `CreatedAt=now`, `NextAttemptAt=now`, `LastReason=null`로 초기 저장을 완료한 뒤 전송합니다. 저장은 키 기준 upsert이며 같은 키의 중복 항목을 만들지 않습니다. `GetAsync` 실패 시에는 Warning을 기록하고 초기 저장을 생략한 채 전송합니다. 이후 최종 저장은 가능하지만 이 경로에서는 기존 누적 횟수가 초기화될 수 있습니다.

### D1 결과 우선순위와 저장 상태

기존 대기 항목의 즉시 반환 외에는 아래 순서로 결과를 결정합니다.

| 우선순위 | 조건 | 최종 처리 |
|---|---|---|
| 1 | 확정된 `Succeed` | 항상 제거를 시도하고 `Succeeded` |
| 2 | 확정된 `RejectPermanently` | 항상 제거를 시도하고 `RejectedPermanently` |
| 3 | 호출자 취소 | 최종 upsert 후 `Canceled` |
| 4 | 그 외 | 최종 upsert 후 저장이 확인되면 `DeferredToOutbox`, 아니면 `FailedNotPersisted` |

`IsPersisted`는 이번 호출에서 한 번이라도 저장에 성공했고 이후 제거에 성공하지 않았음을 뜻합니다. 기존 항목을 조회해 반환하는 경우도 true입니다. 최종 갱신 실패로 저장된 횟수가 오래된 상태여도 이전 저장 성공 사실은 유지합니다. 제거 성공은 false로 바꾸며 제거 실패만으로 true가 되지는 않습니다. 저장·제거 오류나 나중에 관찰한 호출자 취소가 확정된 성공·영구 거절을 뒤집지 않습니다.

결과의 `StatusCode`는 확보한 응답이 있으면 그 상태 코드입니다. 보통 `Reason`과 저장 항목의 `LastReason`에는 `HTTP <상태 코드>` 또는 예외 타입 이름을 사용하며, 호출자 취소 사유는 `caller canceled`입니다.

대기·시계 등 처리 의존성에서 예외가 나도 최종 처리를 수행합니다. 이미 확정된 성공·영구 거절은 제거 후 유지합니다. 그 외에는 예외 타입 이름과 마지막 확보 시각을 `LastReason`, `NextAttemptAt`으로 최종 저장하고 위 순서로 결과를 반환합니다. 시계 첫 조회 실패에 대비해 먼저 시스템 UTC 시각을 확보합니다. 호출자 취소 시 시계가 실패해도 마지막 확보 시각을 사용합니다.

### D5 저장소 오류

Submit의 조회·저장·제거 예외는 Warning으로 기록하고 외부로 전파하지 않습니다. 저장소 오류는 전송 결과로 분류하지 않습니다. 초기 저장 실패만으로 전송을 막지 않으며 최종 저장 성공 여부까지 반영합니다. 저장소 메서드에는 취소 토큰이 없으므로 호출자 취소 후에도 필요한 저장을 시도합니다.

Flush의 `LoadAllAsync` 실패는 아무 항목도 처리하기 전에 호출자에게 전파합니다. 항목별 저장·제거 실패는 Warning과 `StoreErrors` 1건으로 집계하고 다음 항목을 계속 처리합니다. 전송 결과 집계는 저장소 오류와 독립적입니다. 예를 들어 성공 응답 뒤 제거가 실패하면 `Succeeded=1`, `StoreErrors=1`이며 항목은 저장소에 남을 수 있습니다.

## 전송 결과와 재시도

### D8 결과 정규화와 서버 응답

분류기는 원시 예외 대신 `AttemptOutcome`을 받습니다. 정상 반환은 `Response`, `HttpRequestException`은 `TransportError`, 호출자 취소 없이 시도 토큰이 취소된 `OperationCanceledException`은 `AttemptTimedOut`입니다. 나머지 예외는 `Unexpected`입니다. 따라서 직접 발생한 `TimeoutException`이나 어느 토큰도 취소되지 않은 `OperationCanceledException`도 `Unexpected`입니다. 호출자 취소는 분류기에 전달하지 않습니다.

| 입력 | 분류 |
|---|---|
| HTTP 200, 201, 204 | `Succeed` |
| HTTP 408, 429, 500, 502, 503, 504 | `Retry` |
| `TransportError`, `AttemptTimedOut` | `Retry` |
| HTTP 400, 401, 403, 404, 409, 422 | `RejectPermanently` |
| 그 외 상태, 응답 없는 `Response`, `Unexpected` | `StopKeep` |

200/201은 서버에 기록되었거나 같은 키의 처리 결과를 재전달했음을 뜻합니다. 202는 아직 확정되지 않아 보관합니다. 409는 동일 키에 다른 payload를 보낸 충돌로 영구 거절하고 키가 포함된 Error 로그 한 줄을 추가합니다. 422는 유효하지 않은 영수증입니다. 501을 포함해 표에 없는 상태를 임의로 재시도하지 않습니다.

### D3 시도 횟수 계산

`AttemptCount`는 실제 시작한 `SendAsync` 호출 횟수입니다. 전송 직전에 증가하며 전송 중 취소나 시도 타임아웃도 포함합니다. 전송 전 취소는 증가시키지 않습니다. Submit과 모든 Flush를 합산하며, 기본 설정에서는 인라인 5회와 이후 Flush 15회로 총 20회가 한도입니다. Submit도 `MaxAttempts`와 `MaxOutboxAttempts`를 모두 지킵니다.

전송 전 또는 전송 중 호출자 취소 시 다음 기한은 현재 시각입니다. **인라인 대기 중 취소는 이미 계산한 `NextAttemptAt`을 그대로 보존**합니다. 이때 새 토큰으로 Flush를 호출해도 예약 기한 전에는 전송하지 않습니다. 호출자 취소는 추가 전송 없이 `Canceled`로 반환하며 `OperationCanceledException`을 전파하지 않습니다.

Flush에서 전송 직전에 취소되면 해당 미전송 항목은 재저장하지 않고 횟수·예약·사유를 모두 보존합니다. `Remaining`이나 `StoreErrors`에 추가하지 않고 앞서 처리한 부분 집계와 `Canceled=true`를 반환합니다. 이미 생성한 시도 CTS는 해제합니다. 전송 중 취소된 항목은 시도 횟수를 증가시킨 상태로 보관하고 `Remaining`에 포함합니다.

### D4 예약 지연 규칙

시도 n 뒤의 백오프는 `min(BaseDelay × 2^(n−1), MaxDelay)`에 `capped × JitterRatio × (2r−1)`을 더한 값이며 최종 음수는 0으로 만듭니다. `IRandom.NextDouble()`의 범위는 [0, 1)입니다. n은 1부터 시작하며 1 미만은 `ArgumentOutOfRangeException`입니다.

`Retry` 뒤에는 HTTP 429/503의 유효한 Retry-After를 우선 사용합니다. 헤더 이름은 대소문자를 구분하지 않고, 값은 비음수 정수 초 또는 HTTP-date입니다. 앞뒤 공백을 제거하며 과거 날짜는 0초입니다. 누락·잘못된 값·TimeSpan 범위를 넘는 초 값은 파싱 실패입니다. HTTP-date는 주입된 시계를 기준으로 계산합니다.

Retry-After 지연에는 지터나 MaxDelay 상한을 적용하지 않습니다. 다만 `now + delay`가 DateTimeOffset 범위를 넘으면 예약에 사용할 수 없는 값으로 보고 백오프로 대체합니다. 이 경우 파서 자체는 TimeSpan을 반환할 수 있습니다. 다른 상태 코드의 Retry-After는 무시합니다. `StopKeep`과 `Unexpected`도 백오프로 예약합니다.

예약은 증가한 누적 횟수 n으로 계산하여 `NextAttemptAt=now+delay`로 정합니다. Submit에서는 `Retry`이며 두 횟수 한도에 여유가 있고 지연이 `MaxInlineRetryAfter` 이하일 때만 기다린 후 재전송합니다. 한도를 넘는 지연이나 마지막 시도에서는 추가 대기·전송 없이 저장합니다. 이 한도는 일반 백오프에도 적용됩니다. 새 Submit의 인라인 지연은 첫 전송 뒤 backoff(1), 둘째 뒤 backoff(2) 순서입니다.

Flush는 `CreatedAt` 오름차순으로 처리합니다. 먼저 `NextAttemptAt > now`인 항목을 `SkippedNotDue`로 건너뜁니다. 그다음 누적 한도 도달 항목은 `Stalled`와 Warning으로 남기고 삭제하지 않습니다. 나머지는 원래 키로 한 번만 전송하며 인라인 대기가 없습니다. 성공·영구 거절은 제거하고, 그 외에는 누적 횟수·예약·사유를 갱신합니다. `Remaining`은 전송 후 보관한 결과 수이며 전체 저장소 잔량이 아닙니다. 아직 처리하지 않은 항목은 취소 시 집계하지 않습니다.

### D6 시도별 타임아웃

팩터리는 호출자 토큰과 연결되고 지정 시간이 지나면 취소되는 CTS를 반환합니다. 기본 구현은 연결 CTS와 `CancelAfter`를 사용합니다. Submitter가 CTS를 소유하고 시도 종료 시 해제합니다. CTS의 토큰은 `SendAsync`에만 전달하고 인라인 대기는 호출자 토큰을 사용합니다.

전송 어댑터는 토큰을 관찰하여 취소 시 `OperationCanceledException`을 던지고 요청 자원을 정리해야 합니다. 라이브러리가 토큰을 무시하는 전송을 강제 종료하지는 않습니다. 취소 예외에서는 호출자 토큰을 먼저 확인합니다. 호출자 취소가 아니면서 시도 토큰이 취소된 경우에만 타임아웃 재시도이며 호출자 토큰은 취소시키지 않습니다.

### D9 로그 보호와 격리

정상적인 로깅 경로에서는 각 전송 시도마다 시도 번호·결과 종류·멱등 키가 포함된 Info 한 줄, Submit 최종 결과에 별도 한 줄을 기록합니다. 요청·응답 본문과 예외 메시지는 기록하지 않고 상태 코드·예외 타입 등 메타데이터를 사용합니다.

로그 경계에서 `receipt`, `signature`, `token`, `email` 값을 `***`로 마스킹합니다. JSON 객체·배열은 중첩 구조를 순회하며 대소문자를 구분하지 않는 속성명과 이스케이프된 속성명을 인식합니다. 민감 값이 문자열·숫자·null·객체·배열이어도 전체를 마스킹합니다. 다른 문자열 안의 form/query 값도 마스킹합니다. form/query에서는 키와 구분자를 보존합니다. 관련 없는 값의 의미는 유지하지만 JSON 공백·표현 형식 보존은 보장하지 않습니다. 반복 적용 결과는 같습니다.

로그 출력 예외는 로그 경계에서 격리합니다. 로그 실패가 저장·제거·결과 반환·다음 Flush 항목 처리를 중단하지 않습니다. 대기·시계 실패로 정상 로그 경로를 벗어나거나 로그 저장소가 실패한 경우 로그 전달까지 보장하지는 않습니다.

## 테스트 계약 ID

아래 ID는 위 규칙의 구체적인 검증 시나리오입니다. 별도 표기가 없는 백오프 예시는 지터 0을 사용합니다.

| ID | 검증 동작 |
|---|---|
| B1 | 시도 1–5의 백오프는 1, 2, 4, 8, 10초 |
| B2 | 지터 0.2, 상한 시도에서 r=0이면 8초, r=0.999999이면 12초에 1ms 이내 |
| B3 | 지연은 0 이상, MaxDelay × (1+JitterRatio) 이하 |
| B4 | 시도 번호 1 미만은 ArgumentOutOfRangeException |
| C1 | 200/201/204 성공 |
| C2 | D8의 일시적 응답·전송 오류·시도 타임아웃 재시도 |
| C3 | D8의 영구 거절 응답 |
| C4 | 미확정·미지정 상태 및 Unexpected는 StopKeep |
| R1 | Retry-After 120은 정확히 120초 |
| R2 | 현재보다 30초 뒤 HTTP-date는 30초 |
| R3 | 누락·잘못된 헤더는 false |
| R4 | 과거 날짜는 true와 0초 |
| R5 | 헤더 이름의 대소문자 무관 |
| L1 | JSON의 각 민감 값 마스킹 |
| L2 | form/query 값 마스킹 및 키 보존 |
| L3 | 무관 값 보존 및 반복 마스킹의 멱등성 |
| L4 | 이스케이프·공백이 있는 JSON 값 전체 마스킹 |
| K1 | D7의 결정성, 서로 다른 입력의 키, 형식, UTF-8 벡터, 잘못된 입력 예외 |
| S1 | Get 완료 → 초기 Save 완료 → Send 순서; 첫 200은 1회 전송·대기 없음 |
| S2 | 503, 503, 200은 3회 전송과 backoff(1), backoff(2) 대기 후 성공 |
| S3 | 재시도 및 성공 후 새 인스턴스의 같은 거래 제출은 같은 키 |
| S4 | 400은 1회 전송 후 영구 거절, 초기 저장 항목 제거 |
| S4b | 409는 영구 거절 및 키가 포함된 Error 로그 정확히 한 줄 |
| S5 | 최대 시도까지 503이면 한 항목 보관, 정확한 누적 횟수와 최종 예약; 설정한 MaxAttempts 준수 |
| S6 | S6a–e의 Retry-After 처리 |
| S6a | 429의 3초 헤더는 정확히 3초 대기 후 재전송 |
| S6b | 503의 120초 헤더는 1회 전송 후 대기 없이 120초 뒤 예약 |
| S6c | 429의 잘못된 헤더는 backoff(1) |
| S6d | 500의 헤더는 무시하고 backoff(1) |
| S6e | 다섯 번째 503의 3초 헤더는 여섯 번째 전송 없이 3초 뒤 예약 |
| S7 | 전송 전·인라인 대기 중 호출자 취소는 Canceled와 항목 보관; 대기 중 예약은 D3에 따라 보존 |
| S7b | 전송 중 호출자 취소는 1회로 세고 재시도 없이 보관하며 CTS 해제 |
| S8 | 첫 시도 타임아웃 후 두 번째 성공; 호출자 토큰 유지와 모든 시도 CTS 해제; 실제 팩터리의 연결·시간 제한 |
| S9 | 예상 밖 전송 예외는 인라인 재시도 없이 보관, 사유에 예외 타입 이름 |
| S10 | 실패 응답 본문·예외 메시지의 영수증·서명 원문이 로그에 없음 |
| S11 | 아래 S11a–i 및 취소 시 부분 집계 |
| S11a | 생성 순서대로 성공·영구 거절 제거, 나머지 갱신과 집계 |
| S11b | 기한 전 항목은 전송·변경 없이 건너뜀 |
| S11c | 누적 한도 항목은 전송·삭제 없이 Stalled와 Warning |
| S11d | 반복 upsert는 중복을 만들지 않고 누적 20회 한도 준수 |
| S11e | 새 인스턴스도 저장된 원래 키로 재개 |
| S11f | 초기 저장 실패 이후 결과·최종 저장 상태를 D1로 판정; S15a–e에서 검증 |
| S11g | Flush의 503/120초 헤더는 120초 뒤 예약 |
| S11h | Flush의 429/3초 헤더는 인라인 대기 없이 3초 뒤 예약 |
| S11i | 항목별 제거·저장 실패에도 전송 결과와 StoreErrors를 집계하고 다음 항목 진행 |
| S12 | POST, Content-Type 및 네 영수증 필드의 JSON 값 보존 |
| S13 | 202는 DeferredToOutbox와 항목 보관 |
| S14 | 각 시도의 번호·종류·키 Info 로그와 별도 최종 결과 로그 |
| S15 | S15a–e의 결과 우선순위·저장 상태 |
| S15a | 초기 저장 실패와 400은 RejectedPermanently, IsPersisted=false |
| S15b | 모든 저장 실패와 대기 중 취소는 Canceled, IsPersisted=false |
| S15c | 초기 저장 실패, 5회 503, 최종 저장 성공은 DeferredToOutbox, IsPersisted=true |
| S15d | 모든 저장 실패와 5회 503은 FailedNotPersisted |
| S15e | 초기 저장 성공, 200, 제거 실패는 Succeeded, IsPersisted=true와 Warning |
| S16 | 횟수 7·미래 예약인 기존 항목 재제출은 0회 전송, already pending, 모든 저장 필드 보존 |

코드 리뷰 회귀 테스트는 D1의 대기·시계 실패 처리, D3의 대기 중 취소 및 Flush 전송 직전 취소, D4의 표현 불가능한 Retry-After, D9의 로그 실패 격리와 중첩 마스킹을 검증합니다. 구체적인 메서드와 실행 케이스는 [명세별 테스트](spec-test-map.md)에 연결되어 있습니다.
