# 명세 → 테스트 메서드

최종 규칙 W1–W7을 적용했습니다. **56개 테스트 메서드**이며 `[TestCase]` 행을 확장하면 97개 실행 케이스입니다. 최신 명세 ID에 테스트가 없는 항목은 없습니다. S11f는 W1에서 명시적으로 대체되었으며 폐기된 동작을 별도로 강제하지 않습니다. Contract의 SubmitResult 팩터리 테스트는 V11에 따라 생성자 테스트로 대체했습니다.

메서드는 `tests/ReliableWebRequest.Tests/`의 ComponentTests.cs, PurchaseSubmitterTests.cs, FlushOutboxTests.cs에 있습니다. 아래 표에서 괄호는 TestCase 인자입니다.

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
| S11f | W1에 의해 S15a–e로 대체. 특히 Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind |
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

최종 규칙의 추가 경계도 테스트합니다.

| 규칙 | 테스트 메서드 / 검증 내용 |
|---|---|
| W1 | S15a–e; Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(false, 200)/(true, 400); Submit_FinalUpdateFails_RetainsKnownPersistenceAndWarns |
| W2 | S3, S16; Submit_GetFails_SkipsInitialSaveAndRecoversWithFinalUpsert |
| W3 | S5, S7, S7b; Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit; Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems |
| W4 | Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(1/2); S6a–e, S11a/g/h, S9, S13 |
| W5 | Flush_LoadFailure_PropagatesBeforeAnySend; Flush_ItemStoreFailure_CountsOutcomeAndContinues |
| W6 | TimeoutFactory_LinksCallerAndSchedulesTimeout(true: initially uncanceled with 10-minute timeout, then caller cancellation / false: real 50ms timeout, wait up to 5s); S7b, S8; Submit_RetriesOn503_ThenSucceedsWithBackoff; flush disposal checks |
| W7 | K1 UTF-8 vectors; S1 shared call journal; Submit_PendingWriteAheadSave_DoesNotSendUntilSaveCompletes; S4b |
