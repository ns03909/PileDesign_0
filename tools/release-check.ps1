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

    版を上げる手順そのもの (CHANGELOG の [Unreleased] を版の節へ改名する・csproj の版・add-changelog.py の目印)
    は行わない。上げたあとに、取り残しが無いかをこれで確かめる。

.PARAMETER AllowUnreleased
    [Unreleased] に変更が残っていても止めない (リリースの前に、発行が通るかだけ見たいとき)。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release-check.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release-check.ps1 -AllowUnreleased

.NOTES
    このファイルは BOM 付き UTF-8 で保存すること (Windows PowerShell 5.1 は BOM が無いと ANSI として読む)。
#>
[CmdletBinding()]
param(
    [switch]$AllowUnreleased
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

Write-Host ""
Write-Host "リリースの確認がすべて通りました (版 $version)。発行先: $publishDir" -ForegroundColor Green
