<#
.SYNOPSIS
    テストを走らせ、実行件数が下限を下回っていたら失敗にする。

.DESCRIPTION
    dotnet test は、テストホストが途中で切れても残りを集計して「成功!」と表示する。
    実際に「1,768 件」が「1,308 件」になったまま緑で通ったことがある。
    460 件が実行されていないのに commit できてしまう状態で、
    見えている失敗よりこちらのほうが危険。

    このスクリプトは

      1. 本体を先にビルドする（テスト側から先に組むと、WPF の一時プロジェクトで
         .g.cs が見つからない CS2001 が間欠的に出るため）
      2. テストをビルドする
      3. テストを走らせ、表示された「合計」を読む
      4. 合計が下限を下回っていたら、成功と出ていても失敗にする

    アセンブリに含まれるテストの数（宣言の数）のほうは
    TestSuiteIntegrityTests が見張る。片方だけでは足りないので両方いる。

.PARAMETER Minimum
    実行件数の下限。既定 1900。テストを増やしたら上げること。

.PARAMETER Filter
    dotnet test の --filter に渡す文字列。指定すると件数の検査は行わない
    （絞り込み実行では合計が下限を下回るのが当たり前のため）。

.PARAMETER Clean
    obj と bin を消してから組み直す。件数が前回と違うときはこれ。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Filter "FullyQualifiedName~PrecastShearQNTests"
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/run-tests.ps1 -Clean

.NOTES
    このファイルは BOM 付き UTF-8 で保存すること。Windows PowerShell 5.1 は
    BOM が無いと ANSI として読むため、日本語が化けて構文エラーになる。
#>
[CmdletBinding()]
param(
    [int]$Minimum = 1900,
    [string]$Filter = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "Graphics_r1\PileDesign.csproj"
$tests = Join-Path $root "TestProject1\TestProject1.csproj"

function Fail($message) {
    Write-Host ""
    Write-Host "NG: $message" -ForegroundColor Red
    exit 1
}

# アプリ起動中は出力先が掴まれていてビルドできない (MSB3021)。
# 先に見て、分かる形で止める。エラーの山を読ませない。
$running = Get-Process -Name "PileDesign" -ErrorAction SilentlyContinue
if ($running) {
    Fail "PileDesign が起動しています (PID $($running.Id -join ', '))。閉じてからやり直してください。"
}

if ($Clean) {
    Write-Host "obj と bin を消しています..." -ForegroundColor Cyan
    foreach ($d in @("Graphics_r1\obj", "Graphics_r1\bin", "TestProject1\obj", "TestProject1\bin")) {
        $p = Join-Path $root $d
        if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    }
}

# 本体を先に。テスト側から先に組むと _wpftmp の CS2001 が間欠的に出る。
# 出力はそのまま流す。native の stderr を 2>&1 すると、Windows PowerShell では
# NativeCommandError に包まれて、ビルドエラーの代わりに PowerShell の例外が出る。
Write-Host "本体をビルドしています..." -ForegroundColor Cyan
& dotnet build $app
if ($LASTEXITCODE -ne 0) { Fail "本体のビルドに失敗しました。上の error を見てください。" }

Write-Host "テストをビルドしています..." -ForegroundColor Cyan
& dotnet build $tests
if ($LASTEXITCODE -ne 0) { Fail "テストのビルドに失敗しました。上の error を見てください。" }

Write-Host "テストを実行しています..." -ForegroundColor Cyan

# 件数は TRX (XML) から読む。画面の「合計: 1769」を正規表現で拾うと、
# コンソールの文字コード次第で「合訁E」のように化けて読めなくなる。
# 表示に頼らないこと。
$resultsDir = Join-Path $root "TestProject1\TestResults"
$trxName = "run-tests.trx"
$trxPath = Join-Path $resultsDir $trxName
if (Test-Path $trxPath) { Remove-Item -Force $trxPath }

# 共有 STA スレッドに後から届いた処理が落ちた記録。前回の分を消してから走らせる。
$strayLog = Join-Path $resultsDir "dispatcher-exceptions.log"
if (Test-Path $strayLog) { Remove-Item -Force $strayLog }

# 出力はそのまま流す。Windows PowerShell で native の stderr を 2>&1 すると
# NativeCommandError に包まれて、本当のエラーが読めなくなる。
$loggerArg = "trx;LogFileName=$trxName"
if ($Filter) {
    & dotnet test $tests --no-build --filter $Filter --logger $loggerArg --results-directory $resultsDir
} else {
    & dotnet test $tests --no-build --logger $loggerArg --results-directory $resultsDir
}
$testExit = $LASTEXITCODE

if (-not (Test-Path $trxPath)) {
    Fail ("テストの結果ファイルが作られませんでした: $trxPath" +
          "`n     テストホストが起動していないか、途中でクラッシュした可能性があります。" +
          "`n     上の出力を見てください。再現しないこともあります（もう一度走らせてみてください）。")
}

[xml]$trx = Get-Content $trxPath -Encoding UTF8
$counters = $trx.TestRun.ResultSummary.Counters
if (-not $counters) { Fail "テストの結果ファイルを読み取れませんでした: $trxPath" }

$total = [int]$counters.total
$failed = [int]$counters.failed
$passed = [int]$counters.passed

if ($failed -gt 0) {
    Write-Host ""
    $trx.TestRun.Results.UnitTestResult |
        Where-Object { $_.outcome -eq "Failed" } |
        Select-Object -First 15 |
        ForEach-Object {
            Write-Host ("  失敗 " + $_.testName) -ForegroundColor Red
            $msg = $_.Output.ErrorInfo.Message
            if ($msg) { Write-Host ("       " + ($msg -split "`n")[0]) }
        }
    Fail "テストが $failed 件失敗しました (合計 $total 件)。"
}

if ($testExit -ne 0) {
    Fail "テストの実行が異常終了しました (合計 $total 件、合格 $passed 件)。"
}

# 本体が投げ放しにした処理が、テストの外で落ちていないか。
# 拾わずにいるとテストホストごと落ちて、実行が途中で終わる。
if (Test-Path $strayLog) {
    Write-Host ""
    Write-Host "画面スレッドで拾われなかった例外があります:" -ForegroundColor Yellow
    Get-Content $strayLog | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    Fail ("テストの外で例外が起きています: $strayLog" +
          "`n     本体が BeginInvoke で投げた処理が、テストが次へ進んだあとに落ちています。" +
          "`n     拾わずにいると実行が途中で止まります。")
}

if ($Filter) {
    Write-Host ""
    Write-Host "OK: $total 件合格（絞り込み実行のため件数の検査は行いません）" -ForegroundColor Green
    exit 0
}

if ($total -lt $Minimum) {
    Fail ("実行されたのが $total 件だけです（最低 $Minimum 件のはず）。" +
          "`n     dotnet test は途中で切れても残りを集計して成功と表示します。" +
          "`n     -Clean を付けて組み直してください。" +
          "`n     意図して減らしたのなら、このスクリプトの Minimum を下げてください。")
}

Write-Host ""
Write-Host "OK: $total 件すべて合格（下限 $Minimum 件）" -ForegroundColor Green
exit 0
