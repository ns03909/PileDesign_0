using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    // HorizontalCalculationViewModel partial: ステップサマリーの整形・クリップボード/CSV/レポート出力
    public partial class HorizontalCalculationViewModel
    {
        /// <summary>
        /// MS Gothic 等幅フォント上での視覚幅を計算する (col 数)。
        /// ASCII → 1 col、CJK (Hiragana / Katakana / 漢字 / 全角) → 2 col。
        /// East Asian Ambiguous (Greek α β δ ω, ✓ ✗, Box Drawing, 矢印 等) は MS Gothic では
        /// **全角 2 col** で描画されるため、VisualWidth でも 2 col として扱う。
        /// 例外: '|' (ASCII vertical bar) と '-' (ASCII dash) は確実に半角なので ASCII 同様 1 col。
        /// </summary>
        internal static int VisualWidth(string s)
        {
            int w = 0;
            foreach (char c in s)
            {
                if (c < 0x0080) { w += 1; continue; }                       // ASCII (含 '|' '-' '+')
                if (c >= 0x0370 && c <= 0x03FF) { w += 2; continue; }       // Greek → MS Gothic で全角
                if (c == '‖' || c == '·') { w += 2; continue; }             // ‖ はサマリー外で使用
                if (c == '✓' || c == '✗' || c == '▶') { w += 2; continue; } // チェック / 矢印 → 全角
                if (c == '⛔' || c == '⏱' || c == '♻' || c == '⚠') { w += 2; continue; }
                if (c >= 0x2500 && c <= 0x257F) { w += 2; continue; }       // Box Drawing → 全角
                if ((c >= 0x3000 && c <= 0x9FFF) || (c >= 0xFF00 && c <= 0xFFEF)) { w += 2; continue; } // CJK / 全角
                w += 1;
            }
            return w;
        }

        /// <summary>視覚幅ベースで指定列幅にパディングする (左寄せ既定、rightAlign で右寄せ)。</summary>
        internal static string VisualPad(string s, int targetVisualWidth, bool rightAlign = false)
        {
            int cur = VisualWidth(s);
            int pad = Math.Max(0, targetVisualWidth - cur);
            string padStr = new(' ', pad);
            return rightAlign ? padStr + s : s + padStr;
        }

        /// <summary>
        /// v29: ステップサマリーを TSV / CSV 形式に整形して返す。
        /// 集計サマリー (header) + 全ステップの 1 行 1 レコード形式。
        /// </summary>
        private string BuildStepSummaryText(string sep)
        {
            var snapshot = _stepSummaries.ToArray();
            if (snapshot.Length == 0) return string.Empty;
            var sorted = snapshot
                .OrderBy(s => s.Level).ThenBy(s => s.LoadCaseNo).ThenBy(s => s.ComboNo)
                .ThenBy(s => s.IsLiquefaction ? 1 : 0).ThenBy(s => s.Step).ThenBy(s => s.BisectionAttempt)
                .ToList();

            int convergedCount = sorted.Count(s => s.Status == StepStatus.Converged);
            int unconvergedCount = sorted.Count(s => s.Status == StepStatus.Unconverged);
            int physicallyUnconvergedCount = sorted.Count(s => s.Status == StepStatus.PhysicallyUnconverged);
            int retryCount = sorted.Count(s => s.BisectionAttempt > 0);
            double totalElapsed = sorted.Sum(s => s.ElapsedSec);

            var sb = new StringBuilder();
            sb.AppendLine($"# 解析サマリーレポート (生成: {DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            sb.AppendLine($"# ステップ総数 {sorted.Count} (再試行含む)、合計時間 {totalElapsed:F1}s");
            sb.AppendLine($"# 収束 {convergedCount} 件 / 未収束 {unconvergedCount} 件 / 物理的未収束 {physicallyUnconvergedCount} 件 / 再試行発生 {retryCount} 件");
            sb.AppendLine();
            // ヘッダ行
            sb.AppendLine(string.Join(sep, new[] {
                "ケース", "Level", "荷重ケース", "組合せ", "液状化",
                "Step", "NStep", "試行", "反復", "残差", "α許容", "max|d|", "状態", "時間(s)"
            }));
            foreach (var s in sorted)
            {
                string statusStr = s.Status switch
                {
                    StepStatus.Converged => "Converged",
                    StepStatus.Unconverged => "Unconverged",
                    StepStatus.PhysicallyUnconverged => "PhysUnconverged",
                    _ => "?"
                };
                sb.AppendLine(string.Join(sep, new[] {
                    s.CaseTag.Replace(",", " "),  // CSV 用にカンマ除去
                    s.Level.ToString(),
                    s.LoadCaseNo.ToString(),
                    s.ComboNo.ToString(),
                    s.IsLiquefaction ? "Liq" : "NoLq",
                    s.Step.ToString(),
                    s.NStep.ToString(),
                    s.BisectionAttempt.ToString(),
                    s.Iterations.ToString(),
                    s.FinalResidual.ToString("E3"),
                    s.EffectiveAlpha.ToString("E2"),
                    s.MaxDisp.ToString("E3"),
                    statusStr,
                    s.ElapsedSec.ToString("F1")
                }));
            }
            return sb.ToString();
        }

        [RelayCommand]
        private void CopySummaryToClipboard()
        {
            var text = BuildStepSummaryText("\t");
            if (string.IsNullOrEmpty(text))
            {
                MessageService.Show("サマリーデータがありません。先に解析を実行してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { Common.ClipboardHelper.TrySetText(text); }
            catch (Exception ex)
            {
                MessageService.Show($"クリップボードへのコピーに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            StatusMessage = $"サマリー {_stepSummaries.Count} 行をクリップボードにコピーしました";
        }

        [RelayCommand]
        private void ExportSummaryToCsv()
        {
            var text = BuildStepSummaryText(",");
            if (string.IsNullOrEmpty(text))
            {
                MessageService.Show("サマリーデータがありません。先に解析を実行してください。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"AnalysisSummary_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                // Excel での文字化け回避のため UTF-8 BOM 付きで保存
                System.IO.File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(true));
                StatusMessage = $"サマリーを {System.IO.Path.GetFileName(dialog.FileName)} に保存しました";
            }
            catch (Exception ex)
            {
                MessageService.Show($"CSV 保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// v29: 解析終了時にステップ単位の収束サマリーをレポート出力する。
        /// 全ステップ (再試行含む) を表形式で表示し、未収束件数を集計表示。
        /// </summary>
        private async Task OutputStepSummaryReport()
        {
            var snapshot = _stepSummaries.ToArray();
            if (snapshot.Length == 0) return;

            // テキスト全体を 1 度ビルド → ログとキャッシュに出す (docx 出力用)
            string summaryText = BuildStepSummaryReportText(snapshot);
            _mainWindowViewModel.DocxOutput.LastAnalysisSummaryText = summaryText;
            foreach (var line in summaryText.Split('\n'))
            {
                // BuildStepSummaryReportText は \r\n を出さない前提
                await AddLogAsync(line.TrimEnd('\r'));
            }
        }

        // docx 出力でも同内容を再利用するため、テキスト全体を 1 つの string として返す
        internal static string BuildStepSummaryReportText(StepSummary[] snapshot)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string line) => sb.AppendLine(line);
            Add_LegacyOutputStepSummary(snapshot, Add);
            return sb.ToString();
        }

        // 旧 OutputStepSummaryReport の本体ロジックを Action<string> 経由で出力するように汎化
        internal static void Add_LegacyOutputStepSummary(StepSummary[] snapshot, Action<string> emit)
        {

            // ケースタグ → 荷重ステップ番号 → 試行番号 の順でソート
            var sorted = snapshot
                .OrderBy(s => s.Level)
                .ThenBy(s => s.LoadCaseNo)
                .ThenBy(s => s.ComboNo)
                .ThenBy(s => s.IsLiquefaction ? 1 : 0)
                .ThenBy(s => s.Step)
                .ThenBy(s => s.BisectionAttempt)
                .ToList();

            int totalCount = sorted.Count;
            int convergedCount = sorted.Count(s => s.Status == StepStatus.Converged);
            int unconvergedCount = sorted.Count(s => s.Status == StepStatus.Unconverged);
            int physicallyUnconvergedCount = sorted.Count(s => s.Status == StepStatus.PhysicallyUnconverged);
            int retryCount = sorted.Count(s => s.BisectionAttempt > 0);
            double totalElapsed = sorted.Sum(s => s.ElapsedSec);

            // 罫線文字 (Box-Drawing) で表組み。LogWindow は MS Gothic 等幅フォント。
            // MS Gothic では ━ (U+2501) と CJK は全角 2 col、ASCII は半角 1 col。
            // 表本体は 103 visual cols (2 leading + 各列 + " | " 区切り) — 罫線も同幅に揃える:
            //   topRule    = 20 ━ + "  解析サマリーレポート  " + 20 ━ + " ━" = 40 + 22 + 40 + 2 = 103 cols
            //   (実際は ━ 単位で偶数幅しか作れないため bottomRule = 52 × 2 = 104 col で許容)
            const string topRule    = "━━━━━━━━━━━━━━━━━━━━  解析サマリーレポート  ━━━━━━━━━━━━━━━━━━━━";
            const string bottomRule = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

            emit("");
            emit(topRule);
            emit($"ステップ総数 {totalCount} (再試行含む)  ┃  合計時間 {totalElapsed:F1}s");
            emit($"  ✓ 収束 {convergedCount} 件" +
                (unconvergedCount > 0 ? $"  /  ✗ 未収束 (反復上限到達) {unconvergedCount} 件" : "") +
                (physicallyUnconvergedCount > 0 ? $"  /  ⛔ 物理的未収束 (耐力超過の可能性) {physicallyUnconvergedCount} 件" : "") +
                (retryCount > 0 ? $"  /  ♻ 再試行発生 {retryCount} 件" : ""));
            emit("");

            // 列の視覚幅 (MS Gothic 上での col 数)。データの最大幅以上に確保すること:
            //   wRes/wMaxD: "X.XXE-NNN" = 9 col 必要、wAlpha: "X.XE-NNN" = 8 col 必要
            //   wTime: " XX.Xs" = 6 col、wIter: 3 桁反復まで = 4 col
            //   wNRMode: "FNR=NN/MNR=NN" 等を表示するため "NR/MNR" = 8 col 確保
            const int wCase = 18, wStep = 5, wRetry = 4, wIter = 4, wNRMode = 9, wRes = 9, wAlpha = 8, wMaxD = 9, wStat = 14, wTime = 6;

            // 表ヘッダ + 区切り行 (視覚幅でパディング)
            // ASCII-only ヘッダ — Consolas/MS Gothic 混在環境で CJK 幅が ASCII×2 と
            // 厳密に一致しないため、列構造はすべて ASCII で統一する
            emit(
                "  " + VisualPad("Case", wCase) + " | " + VisualPad("Step", wStep) +
                " | " + VisualPad("Try", wRetry) + " | " + VisualPad("Iter", wIter) +
                " | " + VisualPad("NR/MNR", wNRMode) +
                " | " + VisualPad("Resid", wRes) + " | " + VisualPad("a_tol", wAlpha) +
                " | " + VisualPad("max|du|", wMaxD) + " | " + VisualPad("Status", wStat) +
                " | " + VisualPad("Time", wTime));
            emit(
                "  " + new string('-', wCase + 1) + "+" + new string('-', wStep + 2) +
                "+" + new string('-', wRetry + 2) + "+" + new string('-', wIter + 2) +
                "+" + new string('-', wNRMode + 2) +
                "+" + new string('-', wRes + 2) + "+" + new string('-', wAlpha + 2) +
                "+" + new string('-', wMaxD + 2) + "+" + new string('-', wStat + 2) +
                "+" + new string('-', wTime + 1));
            foreach (var s in sorted)
            {
                string statusStr = s.Status switch
                {
                    StepStatus.Converged => "OK Converged",
                    StepStatus.Unconverged => "NG Unconverged",
                    StepStatus.PhysicallyUnconverged => "!! Phys.Unconv",
                    _ => "?"
                };
                string retryTag = s.BisectionAttempt > 0 ? $"#{s.BisectionAttempt}" : "-";
                string stepStr = $"{s.Step,2}/{s.NStep,-2}";
                // NR/MNR 列: "K 行列を再計算した反復数 / 再利用した反復数" を表示
                string nrModeStr = $"{s.KRebuildCount}/{s.KReuseCount}";
                emit(
                    "  " + VisualPad(s.CaseTag, wCase) + " | " + VisualPad(stepStr, wStep) +
                    " | " + VisualPad(retryTag, wRetry, rightAlign: true) +
                    " | " + VisualPad(s.Iterations.ToString(), wIter, rightAlign: true) +
                    " | " + VisualPad(nrModeStr, wNRMode, rightAlign: true) +
                    " | " + VisualPad(s.FinalResidual.ToString("E2"), wRes, rightAlign: true) +
                    " | " + VisualPad(s.EffectiveAlpha.ToString("E1"), wAlpha, rightAlign: true) +
                    " | " + VisualPad(s.MaxDisp.ToString("E2"), wMaxD, rightAlign: true) +
                    " | " + VisualPad(statusStr, wStat) +
                    " | " + VisualPad($"{s.ElapsedSec:F1}s", wTime, rightAlign: true));
            }

            // 未収束ステップのみの再掲 (見落とし防止)
            var failures = sorted.Where(s => s.Status != StepStatus.Converged).ToList();
            if (failures.Count > 0)
            {
                emit("");
                emit("  ─ 未収束 / 物理的未収束のステップ ─");
                foreach (var s in failures)
                {
                    string statusStr = s.Status switch
                    {
                        StepStatus.Unconverged => "✗ 未収束",
                        StepStatus.PhysicallyUnconverged => "⛔ 物理的未収束",
                        _ => "?"
                    };
                    emit($"    {s.CaseTag} step {s.Step}/{s.NStep} (試行#{s.BisectionAttempt})  {statusStr}  残差={s.FinalResidual:E2}  max|δu|={s.MaxDisp:E2}m");
                }
            }
            emit(bottomRule);
            emit("");
        }

    }
}
