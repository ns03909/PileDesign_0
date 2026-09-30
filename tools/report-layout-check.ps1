<#
.SYNOPSIS
  代表の計算書を Word で描画して PDF にし、レイアウトを検査する。

.DESCRIPTION
  計算書の試験 (docx の生成) は中身を見るが、Word で開いたときの見え方は見ない。次のものは
  描画して初めて分かるので、Word に描画させて調べる。
    - 見出しがページの最後に残り、本文が次のページから始まる (見出しだけのページ)
    - 図と図の題、表題と表が別のページに分かれる
    - 図・表がページの幅からはみ出す (文字切れ)
    - 作成できずに省いた図・表の注記がある
    - 複数ページにまたがる表で見出し行を繰り返していない (注意として出す)
  PDF も書き出すので、目でも確かめられる。

  代表の計算書は全体テスト (ReportDeterminismTests) が TestProject1\TestResults\report-sample\ に作る。
  Word が要るので、普段の全体テストではなくリリースの確認 (tools/release-check.ps1) で使う。

.PARAMETER Docx
  検査する docx。省略時は代表の計算書。

.PARAMETER WarnOnly
  問題があっても失敗にしない (一覧だけ出す)。
#>
param(
    [string]$Docx = "",
    [switch]$WarnOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Docx) { $Docx = Join-Path $root "TestProject1\TestResults\report-sample\sample-report.docx" }
if (-not (Test-Path $Docx)) {
    Write-Host "代表の計算書がありません: $Docx" -ForegroundColor Red
    Write-Host "先に tools/run-tests.ps1 を実行してください (ReportDeterminismTests が作ります)。" -ForegroundColor Red
    exit 2
}
$Docx = (Resolve-Path $Docx).Path
$outDir = Split-Path -Parent $Docx
$baseName = [IO.Path]::GetFileNameWithoutExtension($Docx)
$pdf = Join-Path $outDir ($baseName + ".pdf")
$reportPath = Join-Path $outDir ($baseName + "-layout.txt")

$problems = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]
$wdActiveEndPageNumber = 3
$wdWithInTable = 12
$wdStatisticPages = 2
$wdExportFormatPDF = 17
$wdOutlineLevelBodyText = 10
# はみ出しの許容 (pt、約 1 mm)。Word は表をセルの余白の分だけ左へ出して描くので、1 pt 前後の超過は見た目に出ない
$widthTolerance = 3

function Short([string]$s) { if ($s.Length -gt 40) { $s.Substring(0, 40) + "…" } else { $s } }

$sw = [Diagnostics.Stopwatch]::StartNew()
$word = $null
$doc = $null
# 自分が起動した Word を控える (終わりに Quit しても残ることがあるので、残っていれば止める)
$wordBefore = @(Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
$myWord = $null
try {
    $word = New-Object -ComObject Word.Application
    $myWord = Get-Process WINWORD -ErrorAction SilentlyContinue | Where-Object { $wordBefore -notcontains $_.Id } | Select-Object -First 1
    $word.Visible = $false
    $word.DisplayAlerts = 0
    # ConfirmConversions=false, ReadOnly=true, AddToRecentFiles=false
    $doc = $word.Documents.Open([string]$Docx, $false, $true, $false)
    [void]$doc.Fields.Update()
    foreach ($toc in $doc.TablesOfContents) { [void]$toc.Update() }
    $doc.Repaginate()

    $ps = $doc.PageSetup
    $usable = $ps.PageWidth - $ps.LeftMargin - $ps.RightMargin
    $pages = $doc.ComputeStatistics($wdStatisticPages)

    # 段落を 1 つずつ読むと表のセルの段落まで数千回 Word に問い合わせることになり、10 分を超えた。
    # 見出し (書式で探す)・図・表から直接たどる。

    # 次の中身のある段落 (空の段落は飛ばす)。無ければ null
    function NextContent($para) {
        $q = $para.Next(1)
        for ($k = 0; $k -lt 6 -and $null -ne $q; $k++) {
            $r = $q.Range
            if ($r.Information($wdWithInTable) -or $r.InlineShapes.Count -gt 0 -or ($r.Text -replace "[\r\a\f\v\n\s]", "").Length -gt 0) { return $q }
            $q = $q.Next(1)
        }
        return $null
    }
    function ParaText($para) { ($para.Range.Text -replace "[\r\a\f\v\n]", "").Trim() }

    # PDF は調べる前 (描画したまま) に書き出す。
    # 引数は素の型にして渡す。Join-Path の返す文字列 (PowerShell の包んだ型) のまま渡すと、Word が応答を返さなくなった
    $doc.SaveAs2([string]$pdf, [int]$wdExportFormatPDF)
    Write-Host "開いて描画し、PDF に書き出しました ($pages ページ, $([math]::Round($sw.Elapsed.TotalSeconds)) 秒)。見出しを調べています..."
    # ── 見出しがページの最後に残る (見出し 1〜3 を書式で探す) ──
    $headingCount = 0
    foreach ($styleId in @(-2, -3, -4)) {   # wdStyleHeading1..3 (言語に依らない組み込みの見出し)
        $rng = $doc.Content
        $find = $rng.Find
        $find.ClearFormatting()
        $find.Text = ""
        $find.Format = $true
        $find.Style = $doc.Styles.Item($styleId)
        $find.Forward = $true
        $find.Wrap = 0
        $guard = 0
        while ($find.Execute() -and $guard -lt 2000) {
            $guard++
            $para = $rng.Paragraphs.Item(1)
            $text = ParaText $para
            if ($text.Length -gt 0 -and -not $rng.Information($wdWithInTable)) {
                $headingCount++
                $page = $rng.Information($wdActiveEndPageNumber)
                $next = NextContent $para
                if ($null -ne $next -and $next.Range.Information($wdActiveEndPageNumber) -gt $page) {
                    $problems.Add("見出しがページの最後に残り、続きが次のページから始まっています: 「$(Short $text)」(p.$page)")
                }
            }
            $rng.Collapse(0)   # wdCollapseEnd: 見つけた段落の後ろから探し続ける
        }
    }
    if ($headingCount -eq 0) { $problems.Add("見出しが 1 つも見つかりません (見出しの書式が組み込みの見出しに対応していない)") }

    Write-Host "見出し $headingCount 個 ($([math]::Round($sw.Elapsed.TotalSeconds)) 秒)。図を調べています..."
    # ── 図: 幅と、図の題 (題は図の直後) ──
    $shapeCount = 0
    foreach ($s in $doc.InlineShapes) {
        $shapeCount++
        $para = $s.Range.Paragraphs.Item(1)
        $page = $s.Range.Information($wdActiveEndPageNumber)
        if ($s.Width -gt $usable + $widthTolerance) {
            $problems.Add("図がページの幅からはみ出しています: 幅 $([math]::Round($s.Width)) pt / 本文の幅 $([math]::Round($usable)) pt (p.$page)")
        }
        $next = NextContent $para
        if ($null -ne $next) {
            $caption = ParaText $next
            $captionPage = $next.Range.Information($wdActiveEndPageNumber)
            if ($caption.StartsWith("図") -and $captionPage -ne $page) {
                $problems.Add("図と図の題が別のページに分かれています: 「$(Short $caption)」(図 p.$page / 題 p.$captionPage)")
            }
        }
    }
    Write-Host "図 $shapeCount 個 ($([math]::Round($sw.Elapsed.TotalSeconds)) 秒)。表を調べています..."
    # ── 表: 幅・表題 (題は表の直前)・ページをまたぐ表の見出し行 ──
    $tableCount = 0
    foreach ($t in $doc.Tables) {
        $tableCount++
        $first = $t.Range.Characters.First.Information($wdActiveEndPageNumber)
        $last = $t.Range.Characters.Last.Information($wdActiveEndPageNumber)

        # 縦に結合したセルがある表は行を取り出せないので、幅と見出し行は見ない
        try {
            $row1 = $t.Rows.Item(1)
            $width = 0.0
            foreach ($c in $row1.Cells) { $width += $c.Width }
            if ($width -gt $usable + $widthTolerance) {
                $problems.Add("表がページの幅からはみ出しています: 幅 $([math]::Round($width)) pt / 本文の幅 $([math]::Round($usable)) pt (p.$first)")
            }
            if ($last -gt $first -and $t.Rows.Count -gt 1 -and -not $row1.HeadingFormat) {
                $notes.Add("複数ページにまたがる表で見出し行を繰り返していません (p.$first-$last)")
            }
        }
        catch { }

        $prev = $t.Range.Paragraphs.Item(1).Previous(1)
        for ($k = 0; $k -lt 3 -and $null -ne $prev -and (ParaText $prev).Length -eq 0; $k++) { $prev = $prev.Previous(1) }
        if ($null -ne $prev) {
            $title = ParaText $prev
            $titlePage = $prev.Range.Information($wdActiveEndPageNumber)
            if ($title -match "^表\s*\d" -and $titlePage -ne $first) {
                $problems.Add("表題と表が別のページに分かれています: 「$(Short $title)」(題 p.$titlePage / 表 p.$first)")
            }
        }
    }

    Write-Host "表 $tableCount 個 ($([math]::Round($sw.Elapsed.TotalSeconds)) 秒)。"
    # ── 作成できずに省いた図・表 ──
    $omitted = ([regex]::Matches($doc.Content.Text, "を作成できませんでした")).Count
    if ($omitted -gt 0) { $problems.Add("作成できずに省いた図・表の注記が $omitted か所あります") }

}
finally {
    if ($doc) { $doc.Close(0); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($doc) }
    if ($word) { $word.Quit(); [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    if ($myWord -and -not $myWord.WaitForExit(10000)) { Stop-Process -Id $myWord.Id -Force -ErrorAction SilentlyContinue }
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("計算書のレイアウトの検査: $Docx")
$lines.Add("ページ $pages / 図 $shapeCount / 表 $tableCount / 所要 $([math]::Round($sw.Elapsed.TotalSeconds)) 秒")
$lines.Add("PDF: $pdf")
$lines.Add("")
$lines.Add("■ 問題 ($($problems.Count) 件)")
foreach ($p in $problems) { $lines.Add("・" + $p) }
$lines.Add("")
$lines.Add("■ 注意 ($($notes.Count) 件)")
foreach ($n in $notes) { $lines.Add("・" + $n) }
$lines | Set-Content -Path $reportPath -Encoding UTF8
$lines | ForEach-Object { Write-Host $_ }

if ($problems.Count -gt 0 -and -not $WarnOnly) {
    Write-Host "レイアウトの問題が $($problems.Count) 件あります (一覧: $reportPath)" -ForegroundColor Red
    exit 1
}
Write-Host "レイアウトの検査を終えました (一覧: $reportPath)" -ForegroundColor Green
exit 0
