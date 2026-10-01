# 스캐폴드 검증 결과

최종 소스 기준으로 실행했습니다. 동작 구현은 의도적으로 비어 있으며 실패 자체가 이 단계의 예상 결과입니다.

## 빌드

명령: `dotnet build ReliableWebRequest.sln --nologo`. 종료 코드 0. **오류 0, 경고 0**. [전체 빌드 출력](build-output.txt).

초기 제한 환경에서는 NuGet TLS 인증서 오류가 있었으며, 권한 확장 실행으로 nuget.org에서 복원한 뒤 위 최종 빌드가 성공했습니다. 패키지 캐시는 저장소 내부 `.nuget/packages`에 있으며 Git에서 제외합니다.

## 테스트

명령: `dotnet test ReliableWebRequest.sln --no-restore --logger "trx;LogFileName=scaffold.trx" --results-directory TestResults --nologo`. 종료 코드 1(의도된 실패).

메서드 54개 / 실행 케이스 **전체 94, 실패 92, 통과 2, 건너뜀 0**. 원본 증거: 로컬의 TestResults/scaffold.trx 및 TestResults/final-output.txt. 재실행 생성물은 Git에서 제외합니다.

실패 메시지를 전부 검사했습니다. NotImplementedException이 없는 실패: **0개**. 테스트가 NotImplementedException을 기대하는 경우는 없습니다. 잘못된 인자 테스트는 원하는 ArgumentException/ArgumentOutOfRangeException 대신 발생한 스텁 예외를 NUnit이 보고합니다.

Submit/Flush 테스트는 현재 PurchaseSubmitter 생성자 스텁에서 먼저 실패합니다. 따라서 이 결과는 동작 구현의 정확성을 증명하지 않습니다. 생성자 구현 이후 각 테스트 본문이 실제 동작 계약을 검증합니다.

통과한 테스트:

- `SubmitResult_Constructor_PreservesAllValues`
- `RetryPolicy_Default_HasSpecifiedConstants`

## 모든 실패 케이스 → 실제 실패 사유

아래 각 행은 TRX의 ErrorInfo.Message에서 읽은 실제 스텁 ID입니다. 매개변수 행도 생략하지 않았습니다.

| 테스트 이름 | 실패 사유 |
|---|---|
| `Backoff_CappedAttempt_HasSpecifiedJitterEndpoints` | NotImplementedException — TODO(user): B1-B4 |
| `Backoff_GrowsExponentially_AndCapsBeforeJitter` | NotImplementedException — TODO(user): B1-B4 |
| `Backoff_InvalidAttempt_ThrowsArgumentOutOfRange(0)` | NotImplementedException — TODO(user): B1-B4 |
| `Backoff_InvalidAttempt_ThrowsArgumentOutOfRange(-1)` | NotImplementedException — TODO(user): B1-B4 |
| `Backoff_StaysInsideNonnegativeJitteredCap` | NotImplementedException — TODO(user): B1-B4 |
| `Classifier_ConfirmedResponse_Succeeds(200)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_ConfirmedResponse_Succeeds(201)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_ConfirmedResponse_Succeeds(204)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(400)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(401)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(403)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(404)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(409)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(422)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(-1)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(-2)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(408)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(429)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(500)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(502)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(503)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(504)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(-1)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(202)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(203)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(205)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(206)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(299)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(301)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(418)` | NotImplementedException — TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(501)` | NotImplementedException — TODO(user): C1-C4 |
| `Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_CumulativeLimit_StallsAndWarnsWithoutDeleting` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(200)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(400)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(503)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_LoadFailure_PropagatesBeforeAnySend` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_NewInstance_ResumesWithOriginalStoredKey` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_NotYetDue_SkipsWithoutSendingOrChangingItem` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_OrdersByCreation_RemovesFinalOutcomesAndUpdatesOthers` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(429,"3",3)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(503,"120",120)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Key_InvalidTransactionId_ThrowsArgumentException(" \t\r\n")` | NotImplementedException — TODO(user): K1 |
| `Key_InvalidTransactionId_ThrowsArgumentException("")` | NotImplementedException — TODO(user): K1 |
| `Key_InvalidTransactionId_ThrowsArgumentException(null)` | NotImplementedException — TODO(user): K1 |
| `Key_IsDeterministicDistinctAndUtf8Sha256` | NotImplementedException — TODO(user): K1 |
| `Redactor_EscapedJsonAndWhitespace_MasksWholeValues` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_FormAndQuery_MasksValuesAndKeepsKeys` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("email")` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("receipt")` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("signature")` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("token")` | NotImplementedException — TODO(user): L1-L4 |
| `Redactor_PreservesUnrelatedValues_AndIsIdempotent` | NotImplementedException — TODO(user): L1-L4 |
| `RetryAfter_DeltaSeconds_ParsesExactly` | NotImplementedException — TODO(user): R1-R5 |
| `RetryAfter_FutureHttpDate_UsesInjectedClock` | NotImplementedException — TODO(user): R1-R5 |
| `RetryAfter_HeaderLookup_IsCaseInsensitive` | NotImplementedException — TODO(user): R1-R5 |
| `RetryAfter_MissingOrInvalid_ReturnsFalse("garbage")` | NotImplementedException — TODO(user): R1-R5 |
| `RetryAfter_MissingOrInvalid_ReturnsFalse(null)` | NotImplementedException — TODO(user): R1-R5 |
| `RetryAfter_PastDate_ReturnsZero` | NotImplementedException — TODO(user): R1-R5 |
| `Submit_AcceptedButUnconfirmed_KeepsItemForLater` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AllSavesFailAndCallerCancels_ReturnsCanceledNotPersisted` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AlreadyPending_DoesNotSendOrOverwriteAnyField` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AttemptTimeout_RetriesWithoutCancelingCallerAndDisposesAllSources` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(False)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(True)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationDuringSend_CountsAttemptAndDisposesSource` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ExhaustsFiveAttempts_PersistsOneItemAndFinalSchedule` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FinalAttemptRetryAfter_SchedulesWithoutSixthSend` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FinalUpdateFails_RetainsKnownPersistenceAndWarns` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FirstSuccess_CompletesWriteAheadBeforeSend` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_GetFails_SkipsInitialSaveAndRecoversWithFinalUpsert` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(False)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(True)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFailsBut400_StillRejectsAndRemoves` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_LogsOneInfoPerAttemptAndOneFinalResult` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_LongRetryAfter_DefersWithoutInlineWait` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(400)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(409)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(False,200)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(True,200)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(True,400)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_Request_ContainsReceiptJsonAndContentType` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ResponseAndExceptionSecrets_NeverAppearInRetryLogs` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetriesAndNewInstance_ReuseTransactionKey` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetriesOn503_ThenSucceedsWithBackoff` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429,"3",3)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429,"garbage",1)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(500,"3",1)` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_UnexpectedTransportException_KeepsReasonWithoutRetry` | NotImplementedException — TODO(user): S1-S16 W1-W7 (constructor) |
| `TimeoutFactory_LinksCallerAndSchedulesTimeout(False)` | NotImplementedException — TODO(user): S8 W6 |
| `TimeoutFactory_LinksCallerAndSchedulesTimeout(True)` | NotImplementedException — TODO(user): S8 W6 |

## 소스 경계 및 명세 매핑

모든 비-스텁 멤버와 9개 스텁은 [수동 소스 검토](source-review.md)에 열거했습니다. 본문이 있는 비-스텁은 데이터 생성자 8개와 상수 RetryPolicy.Default getter뿐입니다. 나머지는 인터페이스/enum/자동 속성 선언입니다.

[명세별 테스트 매핑](spec-test-map.md)에 B1–B4, C1–C4, R1–R5, L1–L4, K1, S1–S16 및 하위 ID, S11a–i, 데이터 계약, W1–W7을 기록했습니다. 매핑 문서에 이름이 누락된 테스트 메서드: 0개. 유효 명세 ID 누락 없음. S11f는 W1에서 S15a–e로 대체되었습니다.

## Git 검증

이 보고서까지 포함하여 지정된 메시지로 최초 커밋 하나를 만듭니다. 커밋 후 `git log --oneline`, `git rev-list --count HEAD`, `git remote -v`, `git status --short`의 실제 출력은 최종 작업 보고에서 제공합니다. 커밋 해시는 자기 참조를 피하기 위해 이 파일에 넣지 않습니다.
