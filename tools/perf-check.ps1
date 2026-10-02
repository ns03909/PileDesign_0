<#
.SYNOPSIS
  代表モデルの性能 (水平解析・計算書の出力・保存の所要時間と最大メモリ) を測り、基準 (tools/perf-baseline.json) と比べる。

.DESCRIPTION
  機能を足して大きく遅くなった・メモリを食うようになったことを検知する。
  全体テストの中では走らせない (他の試験と同じプロセスではメモリの最大値が測れず、時間もかかる)。
  このスクリプトが PERF_CHECK=1 を付けて、性能の試験 (PerformanceBaselineTests) だけを別のプロセスで走らせる。

  時間は PC に依存するので、基準を取った PC と同じ PC のときだけ比べる。違う PC では測って記録するだけ
  (TestProject1\TestResults\perf\perf-result.json)。
  許容は、時間が基準の 1.5 倍 + 2 秒、最大メモリが 1.3 倍 + 150 MB。

  大きなモデルは、計算例9 の杭を並べ増やした合成モデル (杭 72 本・144 本) で測る。
  本数を 2 倍にしたときの時間の伸び (本数の何乗か) は PC に依らないので、どの PC でも確かめる (上限 2.3 乗)。

.PARAMETER Update
  基準を取り直す (意図して性能が変わったとき・基準を取る PC を変えたとき)。

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/perf-check.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/perf-check.ps1 -Update
#>
param([switch]$Update)

$ErrorActionPreference = "Stop"
$env:PERF_CHECK = "1"
if ($Update) { $env:PERF_UPDATE = "1" } else { Remove-Item Env:PERF_UPDATE -ErrorAction SilentlyContinue }
try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "run-tests.ps1") -Filter "FullyQualifiedName~PerformanceBaselineTests"
    $code = $LASTEXITCODE
}
finally {
    Remove-Item Env:PERF_CHECK -ErrorAction SilentlyContinue
    Remove-Item Env:PERF_UPDATE -ErrorAction SilentlyContinue
}

$result = Join-Path (Split-Path -Parent $PSScriptRoot) "TestProject1\TestResults\perf\perf-result.json"
if (Test-Path $result) {
    Write-Host ""
    Write-Host "測った値 ($result):" -ForegroundColor Cyan
    Get-Content $result -Encoding UTF8 | ForEach-Object { Write-Host "  $_" }
}
if ($code -ne 0) {
    Write-Host "性能の確認が通りません (上の出力を見てください)。" -ForegroundColor Red
    exit $code
}
Write-Host "性能の確認を終えました。" -ForegroundColor Green
exit 0
