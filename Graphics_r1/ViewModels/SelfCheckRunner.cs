using PileDesign.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 起動の確認 (<c>PileDesign.exe --self-check &lt;出力先フォルダ&gt;</c>)。
    ///
    /// <para>配布する単一ファイル版を実際に起動し、例題を開く・水平解析する・検定する・計算書を出すところまでを
    /// 画面と同じ入口で通す。ビルドと全体テストでは、発行した形 (単一ファイル・自己完結) で初めて出る問題
    /// (同梱した例題・フォント・ネイティブのライブラリが見つからない、起動時の読み込みの失敗など) が見えない。
    /// リリースの確認 (tools/release-check.ps1) が発行した exe をこれで起動し、終了コードで合否を見る。</para>
    ///
    /// <para>確認のあいだはダイアログを出さない (<c>MessageService.IsUnattended</c>)。
    /// 結果は出力先に <see cref="ResultFileName"/> として書き、計算書を <see cref="ReportFileName"/> として残す。</para>
    /// </summary>
    internal static class SelfCheckRunner
    {
        /// <summary>起動の確認を求めるコマンドラインの指定。</summary>
        internal const string Switch = "--self-check";

        /// <summary>結果を書くファイルの名前 (出力先フォルダに置く)。</summary>
        internal const string ResultFileName = "self-check-result.txt";

        /// <summary>出力した計算書の名前 (出力先フォルダに置く)。</summary>
        internal const string ReportFileName = "self-check-report.docx";

        /// <summary>水平解析を待つ上限。</summary>
        private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(10);

        /// <summary>コマンドラインから出力先を読む (無ければ null)。</summary>
        internal static string? ParseDirectory(string[]? args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], Switch, StringComparison.OrdinalIgnoreCase)) continue;
                return i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                    ? Path.GetFullPath(args[i + 1])
                    : Path.Combine(Path.GetTempPath(), "PileDesign-self-check");
            }
            return null;
        }

        /// <summary>確認を通し、終了コード (成功 0 / 失敗 1) を返す。結果は出力先のファイルに書く。</summary>
        internal static async Task<int> RunAsync(MainWindowViewModel vm, string outDir)
        {
            var lines = new List<string>();
            var sw = Stopwatch.StartNew();
            void Step(string message)
            {
                lines.Add($"{sw.Elapsed.TotalSeconds,7:F1} 秒  {message}");
                Serilog.Log.Information("[起動の確認] {Message}", message);
            }

            int exitCode = 1;
            try
            {
                Directory.CreateDirectory(outDir);
                Step($"版 {AppInfo.Version}・実行ファイル {Environment.ProcessPath}");

                // 1. 例題を開く (画面の「計算例」と同じ入口)
                await vm.Example3_1Command.ExecuteAsync(null);
                int piles = vm.CurrentInputModel?.PileLayoutItems?.Count ?? 0;
                if (piles == 0) throw new InvalidOperationException("例題「設計例集3.1」を読み込めませんでした (杭が 0 本)。");
                Step($"例題「設計例集3.1」を開きました (杭 {piles} 本)");

                // 2. 水平解析 (解析の画面と同じ ViewModel。確認のダイアログは出さない)
                var horizontal = new HorizontalCalculationViewModel(vm) { BypassUiPromptsForTesting = true };
                await horizontal.ExecuteAnalysisCommand.ExecuteAsync(null);
                var deadline = DateTime.UtcNow + AnalysisTimeout;
                while (horizontal.IsAnalysisRunning && DateTime.UtcNow < deadline) await Task.Delay(200);
                if (horizontal.IsAnalysisRunning) throw new TimeoutException($"水平解析が {AnalysisTimeout.TotalMinutes:N0} 分で終わりませんでした。");
                if (!horizontal.IsAnalysisExecuted || horizontal.CurrentModel == null)
                    throw new InvalidOperationException("水平解析の結果がありません。");
                horizontal.OkCommand.Execute(null);   // 画面の「OK」と同じく結果を登録する
                if (!vm.IsHorizontalAnalysisDone || vm.CurrentResultSet == null)
                    throw new InvalidOperationException("水平解析の結果を登録できませんでした。");
                int steps = vm.CurrentModel?.AnalysisStepResults?.Count ?? 0;
                Step($"水平解析を終えました (ステップ {steps})");

                // 3. 検定
                var evaluation = EvaluationService.BuildEvaluationResult(vm, factored: true);
                if (evaluation.Items.Count == 0) throw new InvalidOperationException("検定の項目がありません。");
                Step($"検定しました ({evaluation.Items.Count} 項目・NG {evaluation.NgCount} 項目)");

                // 4. 計算書 (解析したときの入力で作る。画面の出力と同じ)
                string report = Path.Combine(outDir, ReportFileName);
                var input = vm.ResultInputModel;
                var doc = new Output.WordDocument(input, vm.CurrentModel!, vm);
                doc.CreateWordDocument(input, report);
                long size = new FileInfo(report).Length;
                if (size < 10_000) throw new InvalidOperationException($"計算書が小さすぎます ({size} バイト)。");
                if (doc.OmittedItems.Count > 0)
                    throw new InvalidOperationException("計算書で作成できずに省いた図・表があります: " + string.Join(", ", doc.OmittedItems));
                Step($"計算書を出力しました ({size / 1024:N0} KB): {report}");

                exitCode = 0;
            }
            catch (Exception ex)
            {
                Step("失敗: " + ex);
                Serilog.Log.Error(ex, "[起動の確認] 失敗しました");
            }
            finally
            {
                lines.Add(exitCode == 0 ? "結果: 成功" : "結果: 失敗");
                try
                {
                    // 書き切ってから差し替える (途中で止まっても、読む側が半端な結果を読まない)
                    byte[] text = System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
                    PileDesign.Services.FileOperationService.WriteAtomically(Path.Combine(outDir, ResultFileName), s => s.Write(text));
                }
                catch (Exception ex) { Serilog.Log.Error(ex, "[起動の確認] 結果を書けませんでした"); }
            }
            return exitCode;
        }
    }
}
