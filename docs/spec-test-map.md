# 명세 → 테스트 메서드

[동작 명세](spec.md)의 설계 규칙 D1–D10과 테스트 계약을 연결합니다. **62개 테스트 메서드**이며 `[TestCase]` 행을 확장하면 115개 실행 케이스입니다. S11f의 초기 저장 실패 계약은 S15a–e로 검증합니다. 데이터 생성자 계약은 D10에 정의합니다.

메서드는 `tests/ReliableWebRequest.Tests/`의 ComponentTests.cs, PurchaseSubmitterTests.cs, FlushOutboxTests.cs, ReviewRegressionTests.cs에 있습니다. 아래 표에서 괄호는 TestCase 인자입니다.

| 명세 ID | 테스트 메서드 |
|---|---|
| B1 | Backoff_GrowsExponentially_AndCapsBeforeJitter |
| B2 | Backoff_CappedAttempt_HasSpecifiedJitterEndpoints |
| B3 | Backoff_StaysInsideNonnegativeJitteredCap |
| B4 | Backoff_InvalidAttempt_ThrowsArgumentOutOfRange |
| C1 | Classifier_ConfirmedResponse_Succeeds |
| C2 | Classifier_TransientOutcome_Retries |
| C3 | Classifier_PermanentResponse_Rejects |
| C4 | Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps |
| R1 | RetryAfter_DeltaSeconds_ParsesExactly |
| R2 | RetryAfter_FutureHttpDate_UsesInjectedClock |
| R3 | RetryAfter_MissingOrInvalid_ReturnsFalse |
| R4 | RetryAfter_PastDate_ReturnsZero |
| R5 | RetryAfter_HeaderLookup_IsCaseInsensitive |
| L1 | Redactor_Json_MasksEachSensitiveValue |
| L2 | Redactor_FormAndQuery_MasksValuesAndKeepsKeys |
| L3 | Redactor_PreservesUnrelatedValues_AndIsIdempotent |
| L4 | Redactor_EscapedJsonAndWhitespace_MasksWholeValues |
| K1 | Key_IsDeterministicDistinctAndUtf8Sha256; Key_InvalidTransactionId_ThrowsArgumentException |
| S1 | Submit_FirstSuccess_CompletesWriteAheadBeforeSend (receipt fields preserved by value); Submit_PendingWriteAheadSave_DoesNotSendUntilSaveCompletes (TCS-gated save completion before send) |
| S2 | Submit_RetriesOn503_ThenSucceedsWithBackoff |
| S3 | Submit_RetriesAndNewInstance_ReuseTransactionKey |
| S4 | Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(400) |
| S4b | Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(409) |
| S5 | Submit_ExhaustsFiveAttempts_PersistsOneItemAndFinalSchedule (receipt fields preserved by value); Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(1/2) |
| S6 | 아래 S6a–e의 전체 테스트 |
| S6a | Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429, "3", 3) |
| S6b | Submit_LongRetryAfter_DefersWithoutInlineWait |
| S6c | Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429, "garbage", 1) |
| S6d | Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(500, "3", 1) |
| S6e | Submit_FinalAttemptRetryAfter_SchedulesWithoutSixthSend |
| S7 | Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(false/true) |
| S7b | Submit_CallerCancellationDuringSend_CountsAttemptAndDisposesSource |
| S8 | Submit_AttemptTimeout_RetriesWithoutCancelingCallerAndDisposesAllSources; TimeoutFactory_LinksCallerAndSchedulesTimeout |
| S9 | Submit_UnexpectedTransportException_KeepsReasonWithoutRetry |
| S10 | Submit_ResponseAndExceptionSecrets_NeverAppearInRetryLogs |
| S11 | S11a–i와 Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems |
| S11a | Flush_OrdersByCreation_RemovesFinalOutcomesAndUpdatesOthers |
| S11b | Flush_NotYetDue_SkipsWithoutSendingOrChangingItem |
| S11c | Flush_CumulativeLimit_StallsAndWarnsWithoutDeleting |
| S11d | Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit |
| S11e | Flush_NewInstance_ResumesWithOriginalStoredKey |
| S11f | D1의 초기 저장 실패 계약: S15a–e. 특히 Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind |
| S11g | Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(503, "120", 120) |
| S11h | Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(429, "3", 3) |
| S11i | Flush_ItemStoreFailure_CountsOutcomeAndContinues(200); 추가 400/503 행으로 제거/저장 실패 검사 |
| S12 | Submit_Request_ContainsReceiptJsonAndContentType (HTTP method POST) |
| S13 | Submit_AcceptedButUnconfirmed_KeepsItemForLater |
| S14 | Submit_LogsOneInfoPerAttemptAndOneFinalResult (exactly N distinct Info entries with their own number/kind/key, plus exactly one separate final-result entry) |
| S15 | 아래 S15a–e의 전체 테스트 |
| S15a | Submit_InitialSaveFailsBut400_StillRejectsAndRemoves |
| S15b | Submit_AllSavesFailAndCallerCancels_ReturnsCanceledNotPersisted |
| S15c | Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(true) |
| S15d | Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(false) |
| S15e | Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(true, 200) |
| S16 | Submit_AlreadyPending_DoesNotSendOrOverwriteAnyField |
| Contract.RetryPolicy | RetryPolicy_Default_HasSpecifiedConstants (values only; no instance identity requirement) |
| Contract.SubmitResult | SubmitResult_Constructor_PreservesAllValues |

설계 규칙의 추가 경계도 테스트합니다.

| 규칙 | 테스트 메서드 / 검증 내용 |
|---|---|
| D1 | S15a–e; Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(false, 200)/(true, 400); Submit_FinalUpdateFails_RetainsKnownPersistenceAndWarns |
| D2 | S1 shared call journal; Submit_PendingWriteAheadSave_DoesNotSendUntilSaveCompletes; S3, S16; Submit_GetFails_SkipsInitialSaveAndRecoversWithFinalUpsert |
| D3 | S5, S7, S7b; Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit; Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems |
| D4 | Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(1/2); S6a–e, S11a/g/h, S9, S13 |
| D5 | Flush_LoadFailure_PropagatesBeforeAnySend; Flush_ItemStoreFailure_CountsOutcomeAndContinues |
| D6 | TimeoutFactory_LinksCallerAndSchedulesTimeout(true: initially uncanceled with 10-minute timeout, then caller cancellation / false: real 50ms timeout, wait up to 5s); S7b, S8; Submit_RetriesOn503_ThenSucceedsWithBackoff; flush disposal checks |
| D7 | K1 UTF-8 vectors; S3, S11e |
| D8 | C1–C4; S9, S13, S4b |
| D9 | L1–L4; S10, S14; 코드 리뷰 회귀 테스트의 로그 격리·마스킹 |
| D10 | Contract.RetryPolicy; Contract.SubmitResult |

## 코드 리뷰 회귀 테스트

`ReviewRegressionTests.cs`의 검증입니다.

| 설계 규칙 | 테스트 메서드 | 실행 케이스 / 검증 내용 |
|---|---|---|
| D9, D1, D5 | ThrowingLogger_DoesNotInterruptPersistenceResultsOrFlushProgress | 1: 로그가 항상 던져도 초기 저장 실패 이후 최종 저장·제거·결과 반환 및 저장소 오류 이후 flush 진행 |
| D1 | DependencyFailure_PreservesConfirmedOutcomeOrPerformsFinalUpsert | 10: 대기 실패, 첫 시계 조회 실패, 전송 후 시계 실패; 성공/거절 유지, 최종 upsert 성공/실패, 이전 시각 사용, flush 최종 처리 |
| D3, D4 | CancelDuringInlineWait_PreservesScheduleAndFlushSkipsUntilDue | 2: Retry-After와 백오프 대기 중 취소 시 NextAttemptAt 보존 및 기한 전 flush 건너뛰기 |
| D4 | UnrepresentableRetryAfter_FallsBackToBackoffWithoutStoppingProgress | 2: 파서가 허용하되 날짜 범위를 넘는 지연을 Submit/Flush 예약에서 백오프로 대체 |
| D9, L1–L4 | Redactor_DecodesNestedSensitiveNamesAndPreservesJsonAndFormBoundaries | 1: 이스케이프·대소문자·중첩 속성명, 비문자열 민감 값, JSON 안의 form 문자열, 구분자 보존과 멱등성 |
| D3, D5 | Flush_CancellationBeforeSend_DoesNotCountOrResaveUnsentItem | 2: 앞선 성공 0/1건 이후 전송 직전 취소, 기존 집계만 반환, 미전송 항목 무변경 및 CTS 해제 |

D3에 따라 전송 전/중 취소는 `NextAttemptAt = now`이며, **인라인 대기 중 취소는 기존 예약 시각을 보존**합니다.
