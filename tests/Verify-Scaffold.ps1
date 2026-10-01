param(
    [string]$ResultsPath = (Join-Path $PSScriptRoot '../TestResults/scaffold.trx'),
    [string]$ReportPath = (Join-Path $PSScriptRoot '../docs/verification.md')
)

$ErrorActionPreference = 'Stop'
[xml]$trx = Get-Content -LiteralPath $ResultsPath -Encoding UTF8
$results = @($trx.TestRun.Results.UnitTestResult)
$failed = @($results | Where-Object outcome -eq 'Failed' | Sort-Object testName)
$other = @($failed | Where-Object { $_.Output.ErrorInfo.Message -notmatch 'NotImplementedException' })
$passed = @($results | Where-Object outcome -eq 'Passed')
$skipped = @($results | Where-Object { $_.outcome -notin @('Passed', 'Failed') })
$methodMatches = @(Select-String -Path (Join-Path $PSScriptRoot 'ReliableWebRequest.Tests/*Tests.cs') -Pattern 'public (?:async Task|void) (\w+)\(')
$methodNames = @($methodMatches | ForEach-Object { $_.Matches[0].Groups[1].Value })
$mapPath = Join-Path $PSScriptRoot '../docs/spec-test-map.md'
$map = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8
$unmapped = @($methodNames | Where-Object { -not $map.Contains($_) })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# 스캐폴드 검증 결과')
$lines.Add('')
$lines.Add('최종 소스 기준으로 실행했습니다. 동작 구현은 의도적으로 비어 있으며 실패 자체가 이 단계의 예상 결과입니다.')
$lines.Add('')
$lines.Add('## 빌드')
$lines.Add('')
$lines.Add('명령: `dotnet build ReliableWebRequest.sln --nologo`. 종료 코드 0. **오류 0, 경고 0**. [전체 빌드 출력](build-output.txt).')
$lines.Add('')
$lines.Add('초기 제한 환경에서는 NuGet TLS 인증서 오류가 있었으며, 권한 확장 실행으로 nuget.org에서 복원한 뒤 위 최종 빌드가 성공했습니다. 패키지 캐시는 저장소 내부 `.nuget/packages`에 있으며 Git에서 제외합니다.')
$lines.Add('')
$lines.Add('## 테스트')
$lines.Add('')
$lines.Add('명령: `dotnet test ReliableWebRequest.sln --no-restore --logger "trx;LogFileName=scaffold.trx" --results-directory TestResults --nologo`. 종료 코드 1(의도된 실패).')
$lines.Add('')
$lines.Add("메서드 $($methodNames.Count)개 / 실행 케이스 **전체 $($results.Count), 실패 $($failed.Count), 통과 $($passed.Count), 건너뜀 $($skipped.Count)**. 원본 증거: 로컬의 TestResults/scaffold.trx 및 TestResults/final-output.txt. 재실행 생성물은 Git에서 제외합니다.")
$lines.Add('')
$lines.Add("실패 메시지를 전부 검사했습니다. NotImplementedException이 없는 실패: **$($other.Count)개**. 테스트가 NotImplementedException을 기대하는 경우는 없습니다. 잘못된 인자 테스트는 원하는 ArgumentException/ArgumentOutOfRangeException 대신 발생한 스텁 예외를 NUnit이 보고합니다.")
$lines.Add('')
$lines.Add('Submit/Flush 테스트는 현재 PurchaseSubmitter 생성자 스텁에서 먼저 실패합니다. 따라서 이 결과는 동작 구현의 정확성을 증명하지 않습니다. 생성자 구현 이후 각 테스트 본문이 실제 동작 계약을 검증합니다.')
$lines.Add('')
$lines.Add('통과한 테스트:')
$lines.Add('')
foreach ($result in $passed) { $lines.Add('- `' + $result.testName + '`') }
$lines.Add('')
$lines.Add('## 모든 실패 케이스 → 실제 실패 사유')
$lines.Add('')
$lines.Add('아래 각 행은 TRX의 ErrorInfo.Message에서 읽은 실제 스텁 ID입니다. 매개변수 행도 생략하지 않았습니다.')
$lines.Add('')
$lines.Add('| 테스트 이름 | 실패 사유 |')
$lines.Add('|---|---|')
foreach ($result in $failed)
{
    $message = [string]$result.Output.ErrorInfo.Message
    $stub = [regex]::Match($message, 'TODO\(user\):[^\r\n]+').Value.Trim().TrimEnd('>')
    $reason = if ($message -match 'NotImplementedException') { 'NotImplementedException — ' + $stub } else { '비-스텁 실패: ' + ($message -replace '[\r\n]+', ' ') }
    $name = ([string]$result.testName).Replace('|', '\|').Replace("`r", '\r').Replace("`n", '\n')
    $lines.Add('| `' + $name + '` | ' + $reason.Replace('|', '\|') + ' |')
}
$lines.Add('')
$lines.Add('## 소스 경계 및 명세 매핑')
$lines.Add('')
$lines.Add('모든 비-스텁 멤버와 9개 스텁은 [수동 소스 검토](source-review.md)에 열거했습니다. 본문이 있는 비-스텁은 데이터 생성자 8개와 상수 RetryPolicy.Default getter뿐입니다. 나머지는 인터페이스/enum/자동 속성 선언입니다.')
$lines.Add('')
$lines.Add("[명세별 테스트 매핑](spec-test-map.md)에 B1–B4, C1–C4, R1–R5, L1–L4, K1, S1–S16 및 하위 ID, S11a–i, 데이터 계약, W1–W7을 기록했습니다. 매핑 문서에 이름이 누락된 테스트 메서드: $($unmapped.Count)개. 유효 명세 ID 누락 없음. S11f는 W1에서 S15a–e로 대체되었습니다.")
$lines.Add('')
$lines.Add('## Git 검증')
$lines.Add('')
$lines.Add('이 보고서까지 포함하여 지정된 메시지로 최초 커밋 하나를 만듭니다. 커밋 후 `git log --oneline`, `git rev-list --count HEAD`, `git remote -v`, `git status --short`의 실제 출력은 최종 작업 보고에서 제공합니다. 커밋 해시는 자기 참조를 피하기 위해 이 파일에 넣지 않습니다.')
$lines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output "Total=$($results.Count) Failed=$($failed.Count) Passed=$($passed.Count) Skipped=$($skipped.Count) NonStubFailures=$($other.Count) Methods=$($methodNames.Count) UnmappedMethods=$($unmapped.Count)"
if ($other.Count -gt 0 -or $skipped.Count -gt 0 -or $unmapped.Count -gt 0) { exit 1 }
