<#
.SYNOPSIS
    リリース前の確認を 1 つの手順で行う。

.DESCRIPTION
    リリースのたびに手で確かめていたことをまとめる。どれかが通らなければ NG で止める。

      1. 全体テスト (tools/run-tests.ps1)。ビルド・実行件数の下限に加え、代表の例題の結果比較
         (収束の回帰のスナップショット・検定の文の golden・並列の決定性) もここに含まれる
      2. 版番号と更新履歴の整合 (ReleaseConsistencyTests が 1 の中で見る) と、
         [Unreleased] に未リリースの変更が残っていないか (版の節へ移したか)
      3. 単一ファイル発行 (PublishSingleFile + SelfContained)。IL3000 系など発行のときだけ出るエラーがある
         (CLAUDE.md の「ビルドとテストで守れない領域」)
      4. 発行した exe の版が csproj の版と一致するか
      5. 計算書のレイアウト (tools/report-layout-check.ps1)。1 で作った代表の計算書を Word で描画して PDF にし、
         見出しだけのページ・図と図の題 / 表題と表の泣き別れ・はみ出し・省いた図を調べる。Word が要る
      6. 発行した exe の起動の確認 (PileDesign.exe --self-check)。例題を開く・水平解析する・検定する・計算書を出すまでを
         画面と同じ入口で通し、終了コードで合否を見る。単一ファイル・自己完結の形で初めて出る問題 (同梱の例題・フォント・
         ネイティブのライブラリが見つからないなど) は、ビルドと全体テストでは見えない
      7. 代表モデルの性能 (tools/perf-check.ps1)。水平解析・計算書の出力・保存の所要時間と最大メモリを基準
         (tools/perf-baseline.json) と比べる。基準を取った PC と違う PC では測って記録するだけ

    版を上げる手順そのもの (CHANGELOG の [Unreleased] を版の節へ改名する・csproj の版・add-changelog.py の目印)
    は行わない。上げたあとに、取り残しが無いかをこれで確かめる。

.PARAMETER AllowUnreleased
    [Unreleased] に変更が残っていても止めない (リリースの前に、発行が通るかだけ見たいとき)。

.PARAMETER SkipReportLayout
    計算書のレイアウトの検査を行わない (Word が無い PC)。行わなかったことを最後に書く。

.PARAMETER SkipPerformance
    代表モデルの性能の確認を行わない (数分かかる)。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release-check.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release-check.ps1 -AllowUnreleased

.NOTES
    このファイルは BOM 付き UTF-8 で保存すること (Windows PowerShell 5.1 は BOM が無いと ANSI として読む)。
#>
[CmdletBinding()]
param(
    [switch]$AllowUnreleased,
    [switch]$SkipReportLayout,
    [switch]$SkipPerformance
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "Graphics_r1\PileDesign.csproj"

function Fail($message) {
    Write-Host ""
    Write-Host "NG: $message" -ForegroundColor Red
    exit 1
}
function Ok($message) { Write-Host "OK: $message" -ForegroundColor Green }

# ── 版番号 ──
[xml]$proj = Get-Content $app -Encoding UTF8
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { Fail "csproj から版番号 (Version) を読めません。" }
Write-Host "リリースの確認: 版 $version" -ForegroundColor Cyan

# ── 1. 全体テスト (代表の例題の結果比較を含む) ──
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "run-tests.ps1")
if ($LASTEXITCODE -ne 0) { Fail "全体テストが通りません。上の出力を見てください。" }
Ok "全体テスト (版番号と更新履歴の整合・代表の例題の結果比較を含む)"

# ── 5. 計算書のレイアウト (1 で作った代表の計算書を Word で描画して調べる) ──
if ($SkipReportLayout) {
    Write-Host "注意: 計算書のレイアウトの検査を行いません (-SkipReportLayout)" -ForegroundColor Yellow
} else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "report-layout-check.ps1")
    if ($LASTEXITCODE -ne 0) { Fail "計算書のレイアウトに問題があります (Word が無い PC では -SkipReportLayout)。上の一覧を見てください。" }
    Ok "計算書のレイアウト (見出し・図・表の配置とはみ出し)"
}

# ── 2. [Unreleased] が空か ──
$changelog = Get-Content (Join-Path $root "CHANGELOG.md") -Encoding UTF8
$start = [Array]::IndexOf($changelog, "## [Unreleased]")
if ($start -lt 0) { Fail "CHANGELOG に [Unreleased] の節がありません。" }
$pending = @()
for ($i = $start + 1; $i -lt $changelog.Count -and -not $changelog[$i].StartsWith("## ["); $i++) {
    if ($changelog[$i].TrimStart().StartsWith("- ")) { $pending += $changelog[$i] }
}
if ($pending.Count -gt 0) {
    $msg = "CHANGELOG の [Unreleased] に未リリースの変更が $($pending.Count) 件残っています。版 $version の節へ移してください。"
    if ($AllowUnreleased) { Write-Host "注意: $msg (-AllowUnreleased のため続けます)" -ForegroundColor Yellow }
    else { Fail $msg }
} else {
    Ok "[Unreleased] は空です"
}

# ── 3. 単一ファイル発行 ──
Write-Host "単一ファイル発行を行っています..." -ForegroundColor Cyan
& dotnet publish $app -p:PublishProfile=FolderProfile
if ($LASTEXITCODE -ne 0) { Fail "発行に失敗しました。発行のときだけ出るエラー (IL3000 など) を上の出力で見てください。" }
Ok "単一ファイル発行"

# ── 4. 発行した exe の版 ──
$pubxml = [xml](Get-Content (Join-Path $root "Graphics_r1\Properties\PublishProfiles\FolderProfile.pubxml") -Encoding UTF8)
$publishDir = $pubxml.Project.PropertyGroup.PublishDir
# 発行先はプロジェクトからの相対で書いてある (MSBuild と同じく、プロジェクトの場所から数える)
if (-not [System.IO.Path]::IsPathRooted($publishDir)) { $publishDir = Join-Path (Join-Path $root "Graphics_r1") $publishDir }
$exe = Join-Path $publishDir "PileDesign.exe"
if (-not (Test-Path $exe)) { Fail "発行した exe が見つかりません: $exe" }
$product = (Get-Item $exe).VersionInfo.ProductVersion
# ProductVersion には "+コミットの識別" が付くことがあるので、その前で比べる
if (($product -split '\+')[0] -ne $version) { Fail "発行した exe の版 ($product) が csproj の版 ($version) と違います。" }
Ok "発行した exe の版 ($product)"

# ── 6. 発行した exe の起動の確認 ──
$selfCheckDir = Join-Path $root "TestProject1\TestResults\self-check"
if (Test-Path $selfCheckDir) { Remove-Item -Recurse -Force $selfCheckDir }
Write-Host "発行した exe を起動して確かめています (例題を開く・解析する・計算書を出す)..." -ForegroundColor Cyan
$proc = Start-Process -FilePath $exe -ArgumentList "--self-check", "`"$selfCheckDir`"" -PassThru
if (-not $proc.WaitForExit(15 * 60 * 1000)) {
    try { $proc.Kill() } catch { }
    Fail "発行した exe の起動の確認が 15 分で終わりません。"
}
$selfCheckResult = Join-Path $selfCheckDir "self-check-result.txt"
if (Test-Path $selfCheckResult) { Get-Content $selfCheckResult -Encoding UTF8 | ForEach-Object { Write-Host "    $_" } }
if ($proc.ExitCode -ne 0) { Fail "発行した exe の起動の確認が通りません (終了コード $($proc.ExitCode))。上の結果とログを見てください。" }
Ok "発行した exe の起動の確認 (例題を開く・解析する・検定する・計算書を出す)"

# ── 7. 代表モデルの性能 ──
if ($SkipPerformance) {
    Write-Host "注意: 代表モデルの性能の確認を行いません (-SkipPerformance)" -ForegroundColor Yellow
} else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "perf-check.ps1")
    if ($LASTEXITCODE -ne 0) { Fail "代表モデルの性能が基準を超えました。上の一覧を見てください (意図した変化なら tools/perf-check.ps1 -Update)。" }
    Ok "代表モデルの性能 (水平解析・計算書の出力・保存の時間と最大メモリ)"
}

Write-Host ""
Write-Host "リリースの確認がすべて通りました (版 $version)。発行先: $publishDir" -ForegroundColor Green
