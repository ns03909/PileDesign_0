using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプに書かれた既定値が、実装の既定値と一致していること。
    ///
    /// <para>2026-09-11 にヘルプの「既定」「デフォルト」「初期値」を含む記述 53 件
    /// (更新履歴を除く) を実装と 1 件ずつ突き合わせたところ、49 件は一致し、
    /// <b>4 件が食い違っていた</b>。自動保存の間隔 (5 分 → 3 分)、SC 杭の β2
    /// (1.0 → 0.75。ヘルプの中でも別の節とは矛盾していた)、ペナルティばね
    /// (10^8 kN/m → 並進 10^7 kN/m・回転 10^8 kN·m/rad)、同時実行ケース数 (2 → 16)。
    /// いずれも実装を変えたときにヘルプが取り残された形。</para>
    ///
    /// <para>期待値は<b>コードから読む</b>。実装の既定値を変えればここが落ち、
    /// ヘルプの書き直しを促す。</para>
    /// </summary>
    [TestClass]
    public class HelpStatedDefaultsTests
    {
        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        [TestMethod]
        public void TheAutoSaveInterval()
        {
            string src = TestSource.Read("Graphics_r1", "Services", "AutoSaveService.cs");
            var m = Regex.Match(src, @"AutoSaveIntervalMinutes\s*\{\s*get;\s*set;\s*\}\s*=\s*(\d+);");
            Assert.IsTrue(m.Success, "自動保存の間隔の既定値が見つかりません (書き方が変わった?)");

            StringAssert.Contains(Help(), $"既定: {m.Groups[1].Value}分",
                $"ヘルプの自動保存の間隔が、実装の既定 {m.Groups[1].Value} 分と違います");
        }

        [TestMethod]
        public void TheScShearReductionFactors()
        {
            string help = Help();

            foreach (var (symbol, value) in new[]
            {
                (@"$\beta_1$", ConcreteModelOptions.DefaultScUltimateShearBeta1),
                (@"$\beta_2$", ConcreteModelOptions.DefaultScUltimateShearBeta2),
            })
            {
                // 記号表は別の節 (β1 を 1.0 に固定する式) にもあるので、
                // 基本設定から入力する SC 杭の行に絞る
                string line = LineContaining(help, "symbol-label\">" + symbol + "</span>", "基本設定で入力");
                string expected = "既定 $" + value.ToString("0.0#", CultureInfo.InvariantCulture) + "$";
                StringAssert.Contains(line, expected,
                    $"ヘルプの記号表の {symbol} の既定が、実装の {value} と違います");
            }

            // 基本設定の節の書き方。<strong> で囲まれているので「既定は」とは続いておらず、
            // 数値の組を含む行が「既定」も含むことで見る
            string sentence = "β1 = "
                + ConcreteModelOptions.DefaultScUltimateShearBeta1.ToString("0.00", CultureInfo.InvariantCulture)
                + "、β2 = "
                + ConcreteModelOptions.DefaultScUltimateShearBeta2.ToString("0.00", CultureInfo.InvariantCulture);
            LineContaining(help, sentence, "既定");
        }

        [TestMethod]
        public void ThePenaltySpringConstants()
        {
            string help = Help();
            int at = help.IndexOf("ペナルティ定数", StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, "ヘルプにペナルティ定数の説明がありません");
            string paragraph = help[at..help.IndexOf("</p>", at, StringComparison.Ordinal)];

            string translation = "$10^" + Exponent(FemConstants.KbigTranslation) + "$ kN/m";
            string rotation = "$10^" + Exponent(FemConstants.KbigRotation) + "$ kN·m/rad";

            StringAssert.Contains(paragraph, translation,
                $"ヘルプの並進ペナルティばねの既定が、実装の {FemConstants.KbigTranslation:E0} kN/m と違います");
            StringAssert.Contains(paragraph, rotation,
                $"ヘルプの回転ペナルティばねの既定が、実装の {FemConstants.KbigRotation:E0} kN·m/rad と違います");
        }

        [TestMethod]
        public void TheCaseParallelism()
        {
            StringAssert.Contains(Help(), $"既定値は {HorizontalCalculationViewModel.DefaultCaseParallelism}",
                $"ヘルプの同時実行ケース数の既定が、実装の {HorizontalCalculationViewModel.DefaultCaseParallelism} と違います");
        }

        /// <summary><paramref name="marker"/> と <paramref name="alsoContains"/> を両方含む行。</summary>
        private static string LineContaining(string text, string marker, string alsoContains)
        {
            for (int at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
            {
                int start = text.LastIndexOf('\n', at) + 1;
                int end = text.IndexOf('\n', at);
                string line = text[start..(end < 0 ? text.Length : end)];
                if (line.Contains(alsoContains, StringComparison.Ordinal)) return line;
            }

            Assert.Fail($"ヘルプに「{marker}」と「{alsoContains}」を両方含む行がありません");
            return "";
        }

        private static int Exponent(double value)
        {
            double e = Math.Log10(value);
            Assert.AreEqual(Math.Round(e), e, 1e-9, $"{value} が 10 のべき乗ではありません (ヘルプの書き方を見直す)");
            return (int)Math.Round(e);
        }
    }
}
