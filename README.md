# ReliableWebRequest

“지금이라면 이렇게 짠다”라는 관점에서 구매 영수증 전송을 다시 설계하는 개인 코드 샘플입니다. 이전 회사 프로젝트에서 경험한 무제한 재시도, 응답 지연에 따른 중복 요청, HTTP 상태 확인 누락, 예외 발생 시 데이터 유실, 자원 정리 누락, 민감 정보 로그 노출, 취소 처리 문제를 일반화했습니다. 회사 코드나 자산은 포함하지 않습니다.

English summary: A reliable purchase submission library with deterministic idempotency keys, a write-ahead outbox, bounded retries, explicit cancellation and timeout handling, and redacted logging. All 115 specification tests pass. Targets netstandard2.1; Unity integration is not verified.

## 기능

- 거래별 동일 키로 전송하고, 이미 outbox에 있는 거래는 기존 예약과 누적 횟수를 보존합니다.
- HTTP 응답과 전송 오류를 분류해 제한된 횟수만 재시도하고, 지수 백오프·지터·Retry-After를 적용합니다.
- `SubmitAsync`는 전송 결과와 저장 여부를 반환합니다. `FlushOutboxAsync`는 기한이 된 항목을 한 번씩 재전송하고 부분 결과와 저장소 오류를 집계합니다.
- 시도별 타임아웃 자원을 정리하고 호출자 취소를 별도로 처리합니다. 시도 번호·결과 종류·키를 기록하며 민감 값은 보호합니다.

## 구조

```text
src/ReliableWebRequest/
  Abstractions.cs                  전송·저장·대기·시계·난수·로그·타임아웃 경계
  Data.cs                          요청, 응답, 영수증, 결과, 정책 데이터
  PurchaseSubmitter.cs             저장 → 전송 → 예약 → 최종 결과 흐름
  IdempotencyKey.cs                거래 ID의 결정적 SHA-256 키
  BackoffCalculator.cs             지수 백오프와 지터
  RetryAfterParser.cs              초 단위 값과 HTTP-date 해석
  RetryClassifier.cs              서버 계약에 따른 결과 분류
  LogRedactor.cs                   JSON 및 form/query 민감 값 마스킹
  DefaultAttemptTimeoutFactory.cs 연결 CTS와 시도별 제한 시간
  ReliableWebRequest.csproj        netstandard2.1 / C# 9
tests/ReliableWebRequest.Tests/
  ComponentTests.cs                개별 구성 요소와 데이터 계약
  PurchaseSubmitterTests.cs        인라인 전송과 저장 실패 시나리오
  FlushOutboxTests.cs              재전송, 누적 한도, 부분 결과
  Fakes.cs                         수동 시간·취소·전송·저장소 테스트 대역
docs/
  spec.md                         현재 동작 명세와 설계 규칙
  design.md                       전송 순서와 상태 처리
  server-contract.md              서버 멱등 처리 및 HTTP 응답 계약
  spec-test-map.md                명세와 115개 실행 케이스 매핑
```

[동작 명세](docs/spec.md), [설계](docs/design.md), [서버 계약](docs/server-contract.md), [명세별 테스트](docs/spec-test-map.md)에서 상세 동작을 확인할 수 있습니다.

## 빌드와 테스트

.NET 10 SDK가 필요합니다. 라이브러리는 C# 9와 System.Text.Json을 사용하며 테스트는 .NET 10과 NUnit 4로 실행합니다. 패키지 버전은 프로젝트 파일에 고정했습니다.

```sh
dotnet restore ReliableWebRequest.sln --source https://api.nuget.org/v3/index.json
dotnet build
dotnet test
```

검증 결과: 빌드 오류 0개, 경고 0개. 테스트 115개 통과, 실패 0개, 건너뜀 0개입니다.

## 주요 설계 결정

- **결정적 키:** 거래 ID의 UTF-8 SHA-256 앞에 `pur_`를 붙입니다. 재시도와 새 인스턴스에서도 같은 키를 보내며, 서버가 같은 거래의 중복 처리를 막아야 합니다.
- **전송 전 저장:** 키 조회 후 outbox에 먼저 저장합니다. 기존 대기 항목은 덮어쓰지 않고 flush에 맡깁니다. 조회 실패 시 초기 저장을 생략하고 전송하며, 이후 저장으로 복구할 수 있지만 누적 횟수는 초기화될 수 있습니다.
- **결과 우선순위:** 성공·영구 거절이 우선하고, 다음은 호출자 취소, 나머지는 저장 여부에 따라 보류 또는 저장 실패입니다. 제거 실패가 전송 성공을 뒤집지 않으며 `IsPersisted`로 저장 상태를 별도 전달합니다.
- **취소와 타임아웃:** 호출자 취소는 전송을 중단하고 항목을 보관합니다. 시도 타임아웃은 재시도 대상입니다. 시도 CTS는 매번 해제하고, 재시도 대기는 호출자 토큰만 사용합니다.
- **Retry-After:** 429/503의 유효한 값은 지터나 상한 없이 그대로 예약합니다. 그 외는 상한 적용 후 지터를 더한 백오프를 씁니다. 인라인 한도를 넘으면 outbox로 넘기며 flush는 기다리지 않습니다.
- **로그 보호:** 본문과 예외 메시지를 기록하지 않고 상태 코드·예외 타입 등 필요한 정보만 사용합니다. 로그 경계에서 JSON 및 form/query의 receipt·signature·token·email 값을 마스킹합니다.

## 한계

**netstandard2.1 대상, Unity 이식은 미검증**. UnityWebRequest 어댑터와 실제 영속 저장소는 포함하지 않습니다. `ITransport`와 `IOutboxStore`는 사용하는 환경에서 구현해야 합니다. 전송 어댑터는 취소 토큰을 관찰하고 요청 자원을 정리해야 합니다.

같은 저장소에 대한 제출과 flush 호출은 호출자가 직렬화해야 합니다. 이 샘플은 동시 실행 잠금이나 분산 작업 소유권을 제공하지 않습니다. 저장 실패 시 데이터 보존을 보장할 수 없으며, 누적 시도 한도에 도달한 항목은 삭제하지 않고 `Stalled`로 남깁니다. 서버의 멱등 처리와 운영 측 후속 처리가 필요합니다.

## 작성 방식

주제(이전 결제 전송 모듈의 약점 개선)와 범위는 직접 정했고, 명세·테스트·구현은 AI 코딩 도구(Claude Code, Codex)로 작성한 뒤 테스트와 코드 리뷰로 검증했습니다.
