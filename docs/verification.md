# Scaffold verification after reference validation

Verification was run in the real scaffold repository after the final test and XML-doc changes. No reference implementation was added.

## Build

Command: `dotnet build ReliableWebRequest.sln --nologo`.
Exit code: **0**. **0 warnings, 0 errors**. See [build output](build-output.txt).

## Tests

Command: `dotnet test ReliableWebRequest.sln --no-restore --logger "trx;LogFileName=scaffold.trx" --results-directory TestResults --nologo`.
Exit code: **1**, expected for this unimplemented scaffold.

**56 test methods; 97 cases: 95 failed, 2 passed, 0 skipped.**
Local evidence: `TestResults/scaffold.trx` and `TestResults/final-output.txt` (ignored generated artifacts).

All 95 failed cases were checked individually against TRX ErrorInfo.Message: each contains NotImplementedException. Failures without that exception: **0**. No test expects NotImplementedException. Invalid-argument assertions report the stub exception instead of the required argument exception.

Submit/Flush cases currently fail at the PurchaseSubmitter constructor stub, so this run does not establish implementation correctness. The gated-save test preserves an early operation failure through Fixture.AwaitStarted instead of replacing it with a wait timeout.

Passed cases:

- `RetryPolicy_Default_HasSpecifiedConstants`
- `SubmitResult_Constructor_PreservesAllValues`

## Scope and mapping

The only src change is the SubmitAsync XML-doc line `S12: HTTP method POST`. All implementation stubs remain unchanged. The existing [source review](source-review.md) still describes the source members.

The [spec-test map](spec-test-map.md) covers all 56 method names. Changes remove receipt/default-policy identity constraints, gate write-ahead completion, check caller and real timeout cancellation separately, cover MaxAttempts 1 and 2, require distinct per-attempt Info entries and one final result, and require HTTP POST.

## Every failed case and its stub reason

| Failed case | Actual reason from TRX |
|---|---|
| `Backoff_CappedAttempt_HasSpecifiedJitterEndpoints` | NotImplementedException - TODO(user): B1-B4 |
| `Backoff_GrowsExponentially_AndCapsBeforeJitter` | NotImplementedException - TODO(user): B1-B4 |
| `Backoff_InvalidAttempt_ThrowsArgumentOutOfRange(-1)` | NotImplementedException - TODO(user): B1-B4 |
| `Backoff_InvalidAttempt_ThrowsArgumentOutOfRange(0)` | NotImplementedException - TODO(user): B1-B4 |
| `Backoff_StaysInsideNonnegativeJitteredCap` | NotImplementedException - TODO(user): B1-B4 |
| `Classifier_ConfirmedResponse_Succeeds(200)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_ConfirmedResponse_Succeeds(201)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_ConfirmedResponse_Succeeds(204)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(400)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(401)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(403)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(404)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(409)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_PermanentResponse_Rejects(422)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(-1)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(-2)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(408)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(429)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(500)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(502)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(503)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_TransientOutcome_Retries(504)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(-1)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(202)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(203)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(205)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(206)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(299)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(301)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(418)` | NotImplementedException - TODO(user): C1-C4 |
| `Classifier_UnconfirmedOrUnexpectedOutcome_StopsAndKeeps(501)` | NotImplementedException - TODO(user): C1-C4 |
| `Flush_CallerCancelsInFlight_ReturnsPartialCountsAndKeepsUnsentItems` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_CumulativeLimit_StallsAndWarnsWithoutDeleting` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(200)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(400)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_ItemStoreFailure_CountsOutcomeAndContinues(503)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_LoadFailure_PropagatesBeforeAnySend` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_NewInstance_ResumesWithOriginalStoredKey` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_NotYetDue_SkipsWithoutSendingOrChangingItem` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_OrdersByCreation_RemovesFinalOutcomesAndUpdatesOthers` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RepeatedUpserts_NeverDuplicateAndStopAtCumulativeLimit` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(429,"3",3)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Flush_RetryAfter_SchedulesExactlyWithoutInlineDelay(503,"120",120)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Key_InvalidTransactionId_ThrowsArgumentException(" \t\r\n")` | NotImplementedException - TODO(user): K1 |
| `Key_InvalidTransactionId_ThrowsArgumentException("")` | NotImplementedException - TODO(user): K1 |
| `Key_InvalidTransactionId_ThrowsArgumentException(null)` | NotImplementedException - TODO(user): K1 |
| `Key_IsDeterministicDistinctAndUtf8Sha256` | NotImplementedException - TODO(user): K1 |
| `Redactor_EscapedJsonAndWhitespace_MasksWholeValues` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_FormAndQuery_MasksValuesAndKeepsKeys` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("email")` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("receipt")` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("signature")` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_Json_MasksEachSensitiveValue("token")` | NotImplementedException - TODO(user): L1-L4 |
| `Redactor_PreservesUnrelatedValues_AndIsIdempotent` | NotImplementedException - TODO(user): L1-L4 |
| `RetryAfter_DeltaSeconds_ParsesExactly` | NotImplementedException - TODO(user): R1-R5 |
| `RetryAfter_FutureHttpDate_UsesInjectedClock` | NotImplementedException - TODO(user): R1-R5 |
| `RetryAfter_HeaderLookup_IsCaseInsensitive` | NotImplementedException - TODO(user): R1-R5 |
| `RetryAfter_MissingOrInvalid_ReturnsFalse("garbage")` | NotImplementedException - TODO(user): R1-R5 |
| `RetryAfter_MissingOrInvalid_ReturnsFalse(null)` | NotImplementedException - TODO(user): R1-R5 |
| `RetryAfter_PastDate_ReturnsZero` | NotImplementedException - TODO(user): R1-R5 |
| `Submit_AcceptedButUnconfirmed_KeepsItemForLater` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AllSavesFailAndCallerCancels_ReturnsCanceledNotPersisted` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AlreadyPending_DoesNotSendOrOverwriteAnyField` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_AttemptTimeout_RetriesWithoutCancelingCallerAndDisposesAllSources` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(False)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationBeforeSendOrInDelay_ReturnsCanceledAndKeepsItem(True)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_CallerCancellationDuringSend_CountsAttemptAndDisposesSource` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(1)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ConfiguredMaxAttempts_LimitsSendsDelaysAndStoredCount(2)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ExhaustsFiveAttempts_PersistsOneItemAndFinalSchedule` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FinalAttemptRetryAfter_SchedulesWithoutSixthSend` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FinalUpdateFails_RetainsKnownPersistenceAndWarns` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_FirstSuccess_CompletesWriteAheadBeforeSend` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_GetFails_SkipsInitialSaveAndRecoversWithFinalUpsert` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFailsBut400_StillRejectsAndRemoves` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(False)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_InitialSaveFails_FinalPersistenceDeterminesDeferredKind(True)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_LogsOneInfoPerAttemptAndOneFinalResult` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_LongRetryAfter_DefersWithoutInlineWait` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_PendingWriteAheadSave_DoesNotSendUntilSaveCompletes` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(400)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_PermanentReject_RemovesWriteAheadAndLogsConflict(409)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(False,200)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(True,200)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RemoveFails_PreservesOutcomeAndOnlyKnownPersistence(True,400)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_Request_ContainsReceiptJsonAndContentType` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_ResponseAndExceptionSecrets_NeverAppearInRetryLogs` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetriesAndNewInstance_ReuseTransactionKey` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetriesOn503_ThenSucceedsWithBackoff` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429,"3",3)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(429,"garbage",1)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_RetryAfter_UsesEligibleValidHeaderOtherwiseBackoff(500,"3",1)` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `Submit_UnexpectedTransportException_KeepsReasonWithoutRetry` | NotImplementedException - TODO(user): S1-S16 W1-W7 (constructor) |
| `TimeoutFactory_LinksCallerAndSchedulesTimeout(False)` | NotImplementedException - TODO(user): S8 W6 |
| `TimeoutFactory_LinksCallerAndSchedulesTimeout(True)` | NotImplementedException - TODO(user): S8 W6 |

## Commit

This update is recorded as one new commit with subject `Strengthen spec tests after reference validation` and the requested co-author trailer. No amend, remote change, or push is performed. The commit hash and `git show --stat HEAD` are reported after committing to avoid a self-reference in this file.
