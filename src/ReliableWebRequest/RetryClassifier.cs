namespace ReliableWebRequest
{
    /// <summary>정규화한 전송 결과를 서버 계약에 따라 분류한다.</summary>
    public static class RetryClassifier
    {
        /// <summary>200/201/204는 성공, 408/429/500/502/503/504 및 전송 오류/시도 타임아웃은 재시도,
        /// 400/401/403/404/409/422는 영구 거절이다. 나머지(202 포함)는 보관 후 중단한다.
        /// 호출자 취소는 이 분류에 전달하지 않는다.</summary>
        public static RetryDecision Classify(AttemptOutcome outcome)
        {
            if (outcome.Kind == AttemptOutcomeKind.TransportError || outcome.Kind == AttemptOutcomeKind.AttemptTimedOut)
                return RetryDecision.Retry;
            if (outcome.Kind != AttemptOutcomeKind.Response)
                return RetryDecision.StopKeep;

            return outcome.Response?.StatusCode switch
            {
                200 or 201 or 204 => RetryDecision.Succeed,
                408 or 429 or 500 or 502 or 503 or 504 => RetryDecision.Retry,
                400 or 401 or 403 or 404 or 409 or 422 => RetryDecision.RejectPermanently,
                _ => RetryDecision.StopKeep
            };
        }
    }
}
