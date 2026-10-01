# 설계 메모

이 문서는 PurchaseSubmitter의 전송 흐름과 결과 계약을 설명합니다. 결정적 거래 키와 서버의 멱등 처리가 중복 결제를 막고, 전송 전 outbox 저장이 응답 유실과 재시작에 대비합니다. 키는 거래 ID의 UTF-8 SHA-256에 `pur_` 접두사를 붙입니다. 저장소와 네트워크는 주입되는 경계입니다.

```mermaid
sequenceDiagram
    participant Caller as 호출자
    participant Submitter as PurchaseSubmitter
    participant Outbox as IOutboxStore
    participant Transport as ITransport
    Caller->>Submitter: SubmitAsync(영수증, 호출자 토큰)
    Submitter->>Outbox: GetAsync(결정적 키)
    alt 이미 대기 중
        Submitter-->>Caller: DeferredToOutbox / already pending
    else 새 항목 또는 조회 실패
        opt 조회 성공
            Submitter->>Outbox: SaveAsync(시도 0, 기한 now)
        end
        loop 인라인 최대 5회, 호출자 취소 시 중단
            Note over Submitter: 시도 CTS 생성, 실제 전송 직전 횟수 증가
            Submitter->>Transport: SendAsync(동일 키, 시도 토큰)
            Transport-->>Submitter: 응답 또는 예외
            Note over Submitter: CTS Dispose, 정규화 및 분류, 로그 정제
            opt 재시도 가능하고 예약 지연 ≤ 인라인 한도
                Note over Submitter: 호출자 토큰으로 대기
            end
        end
        alt 성공 또는 영구 거절
            Submitter->>Outbox: RemoveAsync(항상 호출)
        else 취소 또는 보류
            Submitter->>Outbox: 최종 upsert(횟수, 다음 기한, 사유)
        end
        Submitter-->>Caller: 최종 결과와 IsPersisted
    end
```

성공/영구 거절이 최우선이고, 다음은 호출자 취소, 그 외는 저장 여부에 따른 보류/저장 실패입니다. 이번 호출에서 한 번이라도 저장에 성공했고 이후 제거 성공이 없으면 `IsPersisted`는 true입니다. 제거 실패만으로 true가 되지 않습니다. 기존 대기 항목 반환은 true입니다. 저장소 오류는 SubmitAsync 안에서 Warning으로 기록하며 전송 오류로 분류하지 않습니다. 조회 실패 시 초기 저장을 생략하고 전송하므로 누적 카운터가 초기화될 수 있습니다. 갱신 실패 시 저장된 카운터가 오래된 값이어도 보관 사실은 유지합니다.

예약 지연은 429/503의 유효한 Retry-After가 우선이며 정확한 값을 쓰고 지터·MaxDelay 상한을 적용하지 않습니다. 그 외에는 누적 시도 횟수의 백오프를 씁니다. 백오프만 지터 전 상한이 있습니다. 인라인 한도(기본 30초)를 초과하거나 마지막 시도이면 대기 없이 예약합니다. StopKeep/Unexpected도 백오프로 예약합니다. 실제 시작된 전송만 누적하며 전송 전/중 호출자 취소는 다음 기한을 now로 둡니다.

Flush는 CreatedAt 순서로 처리하고 기한 미도래, 누적 20회 도달 항목을 건너뜁니다. 정지 항목은 Warning과 함께 보관합니다. 대상마다 원래 키로 한 번 전송하고 인라인 대기 없이 갱신하거나 제거합니다. 호출자 취소는 부분 결과를 반환하고 진행 중 항목을 보관합니다. Load 오류는 전파하지만 항목별 저장 오류는 Warning/StoreErrors로 기록하고 다음 항목을 계속 처리합니다. StoreErrors는 전송 결과 집계와 독립적입니다.

시도마다 생성한 연결 CTS는 submitter 소유이며 종료 시 Dispose합니다. 호출자 토큰과 시도 토큰을 구분해 사용자 취소를 재시도하지 않습니다. 테스트는 실제 시간 경과 대신 수동 CTS 취소를 사용합니다. 영수증·서명·토큰·이메일은 로그에서 가리고, 시도별 번호·종류·키 및 최종 결과를 기록합니다. Unity 어댑터의 요청 자원 정리는 추후 구현 범위입니다.
