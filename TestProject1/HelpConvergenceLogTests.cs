using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの「解析ログの読み方」と「収束対策」に載せたログ例・閾値が、実装と合っていること。
    ///
    /// <para>2026-09-11 にヘルプの突き合わせを既定値以外の数値まで広げたところ、
    /// 水平解析の収束まわりだけで食い違いが 10 か所以上あった。ログ例は
    /// 許容値 1.0E-5・最大反復 50 回・停滞 7 回・緩やかな発散 5 回で書かれていたが、
    /// 実装は 1.0E-6・100 回・15 回・10 回。物理的未収束の閾値はヘルプが 0.1、実装は 10。
    /// 「Converged」と書いた例の残差が許容値を上回っているものまであった。</para>
    ///
    /// <para>期待値は<b>コードから読む</b>。閾値を変えればここが落ち、ヘルプの書き直しを促す。
    /// ログ例は書式だけでなく「その値で本当にその行が出るか」(収束と書いた例の残差が
    /// 許容値の内側か、比較記号が値と合っているか) まで見る。</para>
    /// </summary>
    [TestClass]
    public class HelpConvergenceLogTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>ログ例の数値 (1.23E-004 / 5.92E-08 のどちらも)。</summary>
        private const string Num = @"[0-9]\.[0-9]+E[+\-][0-9]+";

        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        private static string Run() =>
            TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");

        private static string Summary() =>
            TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Summary.cs");

        private static double Parse(string s) => double.Parse(s, NumberStyles.Float, Inv);

        /// <summary>実装の <c>const</c> の値。</summary>
        private static double Const(string src, string name)
        {
            var m = Regex.Match(src, @"\bconst\s+(?:double|int)\s+" + name + @"\s*=\s*([0-9.eE+\-]+)\s*;");
            Assert.IsTrue(m.Success, $"{name} が実装に見つかりません (書き方が変わった?)");
            return Parse(m.Groups[1].Value);
        }

        private static int Count(string src, string name) => (int)Const(src, name);

        /// <summary>
        /// ケースのやり直しの上限。const ではなく、逆向きの荷重組合せで非線形のケースだけ
        /// 途中で小さい値に書き換えられる。代入をすべて集めて (最大, 最小) を返す。
        /// </summary>
        private static (int Max, int Min) RetryLimits(string src)
        {
            var ms = Regex.Matches(src, @"\bMAX_STEP_BISECTIONS\s*=\s*(\d+)\s*;");
            TestSource.AssertScanned(ms.Count, 2, "やり直しの上限の代入");
            int max = int.MinValue, min = int.MaxValue;
            foreach (Match m in ms)
            {
                int v = int.Parse(m.Groups[1].Value, Inv);
                max = Math.Max(max, v);
                min = Math.Min(min, v);
            }
            return (max, min);
        }

        /// <summary>収束の許容値 (残差ノルム比)。</summary>
        private static double Tolerance() => Const(Run(), "alpha");

        [TestMethod]
        public void TheIterationLinesCompareAgainstTheTolerance()
        {
            // 反復行は本来の許容値を E1 で出す ($"‖R‖²={..:E2}{compareSym}{alpha:E1}")
            double tol = Tolerance();
            string tolText = tol.ToString("E1", Inv);
            string help = Help();

            var lines = Regex.Matches(help, @"‖R‖²=(" + Num + @")(>|&gt;|≦)(" + Num + ")");
            TestSource.AssertScanned(lines.Count, 3, "ヘルプの反復行の例");
            foreach (Match m in lines)
            {
                Assert.AreEqual(tolText, m.Groups[3].Value,
                    $"反復行の例「{m.Value}」の許容値が実装 ({tolText}) と違います");
                bool within = Parse(m.Groups[1].Value) <= tol;
                Assert.AreEqual(within, m.Groups[2].Value == "≦",
                    $"反復行の例「{m.Value}」の比較記号が値と合いません");
            }

            StringAssert.Contains(help, "<code>&gt;" + tolText + "</code>",
                $"反復行の記号の説明にある許容値が、実装の {tolText} と違います");
        }

        [TestMethod]
        public void ConvergedExamplesAreWithinTheirTolerance()
        {
            string help = Help();
            double tol = Tolerance();
            int n = 0;

            // ステップ収束ログ: 緩和の併記がなければ本来の許容値、あればその値の内側
            foreach (Match m in Regex.Matches(help,
                @"Converged in \d+ iterations\. Residual norm=(" + Num + @")(?: \(緩和基準α=(" + Num + @")\))?"))
            {
                double limit = m.Groups[2].Success ? Parse(m.Groups[2].Value) : tol;
                Assert.IsTrue(Parse(m.Groups[1].Value) <= limit,
                    $"「{m.Value}」は許容値 {limit:E1} を上回る残差で収束と書いています");
                n++;
            }

            // サマリー表: 残差 | a_tol | max|du| | OK Converged
            foreach (Match m in Regex.Matches(help,
                @"\|\s*(" + Num + @")\s*\|\s*(" + Num + @")\s*\|\s*" + Num + @"\s*\|\s*OK Converged"))
            {
                double resid = Parse(m.Groups[1].Value);
                double aTol = Parse(m.Groups[2].Value);
                Assert.IsTrue(aTol >= tol,
                    $"サマリー表の例「{m.Value}」の a_tol が、実装の許容値 {tol:E1} より小さくなっています");
                Assert.IsTrue(resid <= aTol,
                    $"サマリー表の例「{m.Value}」は a_tol を上回る残差で収束と書いています");
                n++;
            }

            TestSource.AssertScanned(n, 4, "ヘルプの収束例");
        }

        [TestMethod]
        public void TheRelaxationExamplesStartFromTheTolerance()
        {
            string from = Tolerance().ToString("E2", Inv);
            double floor = Const(Run(), "RELAXED_ALPHA");

            var examples = Regex.Matches(Help(), @"収束基準を緩和します \((" + Num + ")→(" + Num + @")\)");
            TestSource.AssertScanned(examples.Count, 2, "ヘルプの緩和ログの例");
            foreach (Match m in examples)
            {
                Assert.AreEqual(from, m.Groups[1].Value,
                    $"緩和ログの例「{m.Value}」の緩和前が、実装の許容値 {from} と違います");
                Assert.IsTrue(Parse(m.Groups[2].Value) >= floor,
                    $"緩和ログの例「{m.Value}」の緩和後が、下限 {floor:E1} を下回っています");
            }
        }

        [TestMethod]
        public void TheCountsInTheLogExamples()
        {
            string src = Run();
            string help = Help();

            var max = Regex.Match(src, @"maxIterations\s*=\s*SkipIteration\s*\?\s*1\s*:\s*(\d+)\s*;");
            Assert.IsTrue(max.Success, "最大反復回数が実装に見つかりません (書き方が変わった?)");

            foreach (var (expected, what) in new[]
            {
                ($"最大反復回数 {max.Groups[1].Value} に到達", "最大反復回数"),
                ($"(許容値={Tolerance().ToString("E3", Inv)})", "未収束ログの許容値"),
                ($"停滞検出: {Count(src, "STAGNATION_LIMIT")}回連続", "停滞の回数"),
                ($"緩やかな発散検出: {Count(src, "SLOW_DIVERGENCE_LIMIT")}回連続", "緩やかな発散の回数"),
                ($"長期未改善検出: {Count(src, "NO_IMPROVEMENT_LIMIT")}反復", "長期未改善の反復数"),
                ($"ケース再試行 (1/{RetryLimits(src).Max})", "再試行の最大回数"),
            })
            {
                StringAssert.Contains(help, expected, $"ヘルプのログ例の{what}が実装と違います (「{expected}」がありません)");
            }
        }

        [TestMethod]
        public void TheRetryAndAbortThresholds()
        {
            string src = Run();
            string help = Help();
            double accept = Const(src, "RELAX_ACCEPT_THRESHOLD");
            double abort = Const(src, "PHYSICALLY_UNCONVERGEABLE_THRESHOLD");

            // ログ例 (E2)
            StringAssert.Contains(help, $"物理的未収束閾値 {abort.ToString("E2", Inv)}",
                $"物理的未収束のログ例の閾値が、実装の {abort:E2} と違います");

            // 本文。緩和基準の節には受け入れの上限・やり直しの回数・打ち切りの閾値の 3 つ、
            // 物理的未収束の節には打ち切りの閾値
            string relaxed = Section(help, "convergence-relaxed-criterion");
            string physical = Section(help, "convergence-physical");
            string abortText = abort.ToString("0.##", Inv) + " を超え";

            StringAssert.Contains(relaxed, accept.ToString("0.0E+0", Inv) + " (残差 1%)",
                $"緩和収束を受け入れる上限が、実装の {accept:E1} と違います");
            var (retryMax, retryMin) = RetryLimits(src);
            StringAssert.Contains(relaxed, $"(最大 {retryMax} 回",
                $"やり直しの最大回数が、実装の {retryMax} 回と違います");
            StringAssert.Contains(relaxed, $"最大 {retryMin} 回)",
                $"逆向きの荷重組合せで非線形のケースのやり直しの最大回数が、実装の {retryMin} 回と違います");
            StringAssert.Contains(relaxed, abortText, $"物理的未収束の閾値が、実装の {abort} と違います");
            StringAssert.Contains(physical, abortText, $"物理的未収束の閾値が、実装の {abort} と違います");
        }

        [TestMethod]
        public void TheStatusLabels()
        {
            // 状態の書き分け (statusStr) は 3 か所ある。クリップボードへ渡す表 (Converged / PhysUnconverged)、
            // ログに出す文字の表、その下の未収束一覧。ヘルプが載せているのは 2 番目なので、
            // その表の見出し (a_tol) より後ろで最初の書き分けを読む
            string summary = Summary();
            int header = summary.IndexOf("\"a_tol\"", StringComparison.Ordinal);
            Assert.IsTrue(header >= 0, "サマリー表の見出し (a_tol) が実装に見つかりません");
            int at = summary.IndexOf("string statusStr", header, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "サマリー表の状態の書き分け (statusStr) が実装に見つかりません");
            string table = summary[at..];

            string help = Help();
            string physical = Section(help, "convergence-physical");

            foreach (string status in new[] { "Converged", "Unconverged", "PhysicallyUnconverged" })
            {
                var m = Regex.Match(table, @"StepStatus\." + status + @"\s*=>\s*""([^""]+)""");
                Assert.IsTrue(m.Success, $"サマリー表の状態ラベル ({status}) が実装に見つかりません");
                string label = "<code>" + m.Groups[1].Value + "</code>";
                StringAssert.Contains(help, label, $"ヘルプにサマリー表の状態ラベル「{m.Groups[1].Value}」がありません");

                // 物理的未収束の節も、同じ文字で書いていること
                if (status == "PhysicallyUnconverged")
                    StringAssert.Contains(physical, label, "物理的未収束の節にある状態ラベルが実装と違います");
            }
        }

        /// <summary><c>id</c> の見出しから次の見出しまで。</summary>
        private static string Section(string help, string id)
        {
            int at = help.IndexOf($"id=\"{id}\"", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"ヘルプに節 {id} がありません");
            int end = help.IndexOf("<h5", at + 1, StringComparison.Ordinal);
            return help[at..(end < 0 ? help.Length : end)];
        }
    }
}
