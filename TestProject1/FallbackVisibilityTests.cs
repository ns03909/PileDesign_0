using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 断面計算が既定値 (0 等) で代替されたことが、<b>どこかで利用者に伝わる</b>こと。
    ///
    /// <para>諸元・N-M 曲線・M-φ の算定は例外を飲んで既定値を返す箇所がある
    /// (<see cref="CalcFallbackTracker"/> が 37 か所から呼ばれる)。値が 0 のまま表に載ると、
    /// 読み手には「そういう断面」に見える。水平解析は解析ログに件数と内訳を出していたが、
    /// <b>計算書の生成は断面を作り直すのに何も出していなかった</b> (2026-09-19 に発見)。</para>
    /// </summary>
    [TestClass]
    public class FallbackVisibilityTests
    {
        /// <summary>数えるための入口と読み出しが揃っていること。</summary>
        [TestMethod]
        public void TheTrackerCountsAndSummarises()
        {
            CalcFallbackTracker.Reset();
            Assert.AreEqual(0L, CalcFallbackTracker.TotalCount, "Reset で 0 に戻らない");

            CalcFallbackTracker.Report("テスト用の代替（→0）", detail: "軸力 1000 kN");
            Assert.AreEqual(1L, CalcFallbackTracker.TotalCount, "件数が数えられていない");

            string summary = CalcFallbackTracker.BuildSummary();
            StringAssert.Contains(summary, "テスト用の代替",
                "内訳に発生源が出ない (何が既定値になったのか読み取れない)");

            CalcFallbackTracker.Reset();
        }

        /// <summary>
        /// 水平解析と計算書の<b>両方</b>が、件数を数えて利用者に伝えること。
        ///
        /// <para>解析だけが伝えていると、解析せずに計算書だけ出した場合に 0 が黙って載る。
        /// 走査で「数え始め (Reset)」と「読み出し (TotalCount)」の両方があることを見る。</para>
        /// </summary>
        [TestMethod]
        public void BothTheAnalysisAndTheReportSurfaceFallbacks()
        {
            var expected = new (string Label, string[] Files)[]
            {
                ("水平解析", ["HorizontalCalculationViewModel.cs"]),
                ("計算書", ["WordDocument.cs", "WordDocument.References.cs"]),
            };

            foreach (var (label, files) in expected)
            {
                string text = string.Concat(files.Select(f =>
                    File.Exists(Path.Combine(TestSource.Dir("Graphics_r1", "ViewModels"), f))
                        ? File.ReadAllText(Path.Combine(TestSource.Dir("Graphics_r1", "ViewModels"), f))
                        : File.ReadAllText(Path.Combine(TestSource.Dir("Graphics_r1", "Output"), f))));

                Assert.IsTrue(text.Contains("CalcFallbackTracker.Reset()"),
                    $"{label}: 断面計算の代替を数え始めていません (前の実行の件数が混ざります)");
                Assert.IsTrue(text.Contains("CalcFallbackTracker.TotalCount"),
                    $"{label}: 数えた件数を読み出していません (代替が起きても誰にも伝わりません)");
                Assert.IsTrue(text.Contains("CalcFallbackTracker.BuildSummary()"),
                    $"{label}: 内訳を出していません (どの算定が既定値になったのか分かりません)");
            }
        }

        /// <summary>
        /// 既定値に落ちる箇所は必ず記録すること。
        ///
        /// <para>例外を飲んで 0 を返す <c>catch</c> が、記録なしで増えていないかを見る。
        /// 断面・材料・入力モデルの算定に限って走査する (描画や保存の catch は対象外)。</para>
        /// </summary>
        [TestMethod]
        public void SilentCatchesDoNotCreepIntoTheSectionCalculations()
        {
            string[] files =
            [
                Path.Combine(TestSource.Dir("Graphics_r1", "Models", "InputData"), "MaterialModels.cs"),
                Path.Combine(TestSource.Dir("Graphics_r1", "Models", "InputData"), "AbstractPileSection.cs"),
                Path.Combine(TestSource.Dir("Graphics_r1", "Models", "InputData"), "SectionIntegration.cs"),
            ];

            int scanned = 0;
            var offenders = new System.Collections.Generic.List<string>();

            foreach (string file in files)
            {
                if (!File.Exists(file)) continue;
                scanned++;
                string text = File.ReadAllText(file);

                // catch ブロックの中身を切り出し、記録もログも無いものを拾う
                foreach (Match m in Regex.Matches(text, @"catch\s*(\([^)]*\))?\s*\{(?<body>[^{}]*)\}"))
                {
                    string body = m.Groups["body"].Value;
                    if (string.IsNullOrWhiteSpace(body)) continue;              // 空 catch は別の網の領分
                    if (body.Contains("CalcFallbackTracker")) continue;
                    if (body.Contains("Log.") || body.Contains("Serilog")) continue;
                    if (body.Contains("throw")) continue;
                    int line = text[..m.Index].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line}");
                }
            }

            TestSource.AssertScanned(scanned, 3, "断面・材料の算定のファイル");
            Assert.AreEqual(0, offenders.Count,
                "既定値に落ちるのに記録もログも無い catch があります (黙って 0 になります): "
                + string.Join(" / ", offenders));
        }
    }
}
