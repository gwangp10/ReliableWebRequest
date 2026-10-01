# ReliableWebRequest

“지금이라면 이렇게 짠다”라는 관점에서 구매 영수증 전송을 다시 설계하는 개인 코드 샘플입니다. 이전 회사 프로젝트에서 경험한 무제한 재시도, 응답 지연에 따른 중복 요청, HTTP 상태 확인 누락, 예외 발생 시 데이터 유실, 자원 정리 누락, 민감 정보 로그 노출, 취소 처리 문제를 일반화했습니다. 회사 코드나 자산은 포함하지 않습니다.

명세(테스트)·데이터 선언·인터페이스·스텁은 AI 도움을 받아 작성했고, 동작 구현(src/의 TODO)은 아직 없습니다. 구현은 직접 작성합니다.

English summary: A test-first scaffold for reliable purchase submission with deterministic idempotency keys, bounded retries, a persistent outbox, cancellation, and redacted logging. Behavior is intentionally unimplemented; the author will implement the TODO stubs.

## 현재 단계

**netstandard2.1 대상, Unity 이식은 미검증**. UnityWebRequest 어댑터와 실제 영속 저장소는 포함하지 않습니다. 라이브러리는 C# 9, 테스트는 .NET 10 및 NUnit 4를 사용합니다. 데이터는 일반 클래스이며 `SubmitResult`는 팩터리 대신 공개 생성자를 제공합니다.

`src/`의 동작 메서드와 `PurchaseSubmitter` 생성자는 `NotImplementedException("TODO(user): ...")`을 던집니다. 테스트는 원하는 동작을 검증하므로 현재는 데이터 계약 테스트를 제외하고 실패해야 합니다. 실패 테스트가 스텁 예외를 기대하도록 작성하지 않았습니다. 생성자 스텁을 구현한 뒤에는 각 테스트가 이후 동작을 검증합니다. 테스트용 로직은 `tests/`에만 있습니다.

## 빌드와 테스트

```text
dotnet restore ReliableWebRequest.sln --source https://api.nuget.org/v3/index.json
dotnet build ReliableWebRequest.sln
dotnet test ReliableWebRequest.sln --logger "trx;LogFileName=scaffold.trx"
```

NuGet 버전은 프로젝트 파일에 고정했습니다. 의도적인 실패 때문에 `dotnet test`의 종료 코드는 1입니다. 실패·통과·건너뜀 수와 각 실패의 원인은 [검증 보고서](docs/verification.md), 전체 ID별 테스트 이름은 [명세 매핑](docs/spec-test-map.md)을 확인하세요. 실제 구현 후에는 모든 동작 테스트가 통과해야 합니다.

## 구현 TODO

각 ID의 계약은 `src/` XML 주석과 테스트에 있습니다. 기본 명세보다 V1–V11, 그보다 W1–W7 최종 규칙이 우선합니다.

- [ ] B1: 지수 증가와 지터 전 상한
- [ ] B2: 상한 적용 시 지터 양 끝
- [ ] B3: 지연 범위
- [ ] B4: 잘못된 시도 번호
- [ ] C1: 확인된 성공 상태
- [ ] C2: 일시 장애 및 정규화된 타임아웃
- [ ] C3: 영구 거절 상태
- [ ] C4: 미확인 상태 및 예상 밖 오류 보관
- [ ] R1: 초 단위 Retry-After
- [ ] R2: 미래 HTTP-date
- [ ] R3: 누락·잘못된 헤더
- [ ] R4: 과거 날짜 0초
- [ ] R5: 헤더 대소문자 무관
- [ ] L1: JSON 민감 값 마스킹
- [ ] L2: form/query 마스킹
- [ ] L3: 무관 값 보존·멱등성
- [ ] L4: 이스케이프·공백 포함 JSON
- [ ] K1: UTF-8 SHA-256 거래 키·유효하지 않은 입력
- [ ] S1: Get/Save 완료 후 최초 전송
- [ ] S2: 제한된 재시도·지연
- [ ] S3: 재시도·새 인스턴스에서 키 유지
- [ ] S4, S4b: 영구 거절 제거·409 Error 로그
- [ ] S5: 인라인 최대 5회·단일 outbox 항목
- [ ] S6a–e: Retry-After 적용·무시·마지막 시도 예약
- [ ] S7, S7b: 전송 전·중·지연 중 호출자 취소
- [ ] S8: 시도 타임아웃·CTS 연결 및 Dispose
- [ ] S9: 예상 밖 예외 보관·타입 기록
- [ ] S10: 응답·예외·요청 민감 정보 보호
- [ ] S11a: 생성 순서·결과별 제거/갱신·부분 집계
- [ ] S11b: 기한 미도래 건너뛰기
- [ ] S11c: 누적 20회 정지·Warning
- [ ] S11d: 연속 flush 중복 없음·누적 한도
- [ ] S11e: 새 인스턴스에서 원래 키 재개
- [ ] S11f: W1에 의해 S15a–e로 대체됨
- [ ] S11g, S11h: flush Retry-After 예약·인라인 대기 없음
- [ ] S11i: 저장 오류 집계·다음 항목 계속
- [ ] S12: JSON 본문·Content-Type
- [ ] S13: 202 미확인 응답 보관
- [ ] S14: 시도별 Info·최종 결과 로그
- [ ] S15a–e: 저장·제거 실패 시 결과 우선순위와 IsPersisted
- [ ] S16: 기존 대기 항목 변경·전송 방지
- [ ] W1–W7 추가 계약: 조회 실패, 갱신 실패, flush 취소/Load 실패, 자원 소유권
- [x] 데이터 계약: RetryPolicy.Default, SubmitResult 생성자

[설계](docs/design.md)와 [서버 계약](docs/server-contract.md)을 함께 읽으세요. 구현을 마친 뒤 실제 작성 범위에 맞춰 위 작성자 설명과 체크리스트를 갱신합니다.
