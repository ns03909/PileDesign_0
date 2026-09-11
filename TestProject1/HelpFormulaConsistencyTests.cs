using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプと計算書に書いた式の係数が、実装と合っていること (既定値・収束以外)。
    ///
    /// <para>2026-09-11 にヘルプの式・記号表を実装と突き合わせたところ、文章だけが違うものが
    /// まとまって見つかった。PHC 杭の使用限界の β1 (ヘルプ 1.0 → 実装 0.9)、PRC 杭の同 (1.0 → 0.8)、
    /// PHC 杭の安全限界の β2 (「1.0 固定」→ 実装・ヘルプ自身の表とも 0.75 / 0.65)、M-φ の
    /// 「β1β2 = 0.65」(→ 0.8 × 0.75 / 0.65)。計算書の後方杭の κ は
    /// min(0.55 − 0.007φ, R/B − 1.0, 0.4) という別の式を印字していた。</para>
    ///
    /// <para>期待値はコードから読む。係数を変えればここが落ち、文章の書き直しを促す。</para>
    /// </summary>
    [TestClass]
    public class HelpFormulaConsistencyTests
    {
        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        // 冒頭のコメントに 3 クラスの宣言が並んでいるので、コメントを落としてから切る
        private static string Precast() => Strip(TestSource.Read("Graphics_r1", "Models", "InputData", "PrecastPileSection.cs"));

        /// <summary>杭種ごとの使用限界の β1 (GetServiceLimitMoment の第 1 引数)。</summary>
        [DataTestMethod]
        [DataRow("internal class PHCSection :", "internal class PRCSection :", "PHC 杭の使用限界曲げモーメント")]
        [DataRow("internal class PRCSection :", "internal class SCSection :", "PRC 杭の使用限界曲げモーメント")]
        public void ServiceLimitBeta1(string classMarker, string nextClassMarker, string helpHeading)
        {
            // 使用限界の曲線は低減前 (β = 1.0) と低減後の 2 本ある。ヘルプの M_s = β1·min(...) は低減後の方
            string factored = MethodBody(
                Between(Precast(), classMarker, nextClassMarker), ") GetFactoredServiceLimitMNInteraction(");  // 定義 (呼び出しではなく)
            var values = Regex.Matches(factored, @"GetServiceLimitMoment\((\d\.\d+)\s*,")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.AreEqual(1, values.Count, $"{classMarker} の使用限界の β1 が 1 つに決まりません: {string.Join(", ", values)}");

            StringAssert.Contains(HelpSection(helpHeading), "$\\beta_1 = " + values[0] + "$",
                $"ヘルプの「{helpHeading}」の β1 が、実装の {values[0]} と違います");
        }

        [TestMethod]
        public void PhcBeta2()
        {
            string phc = Between(Precast(), "internal class PHCSection :", "internal class PRCSection :");
            var m = Regex.Match(phc, @"<\s*10\s*\?\s*(\d\.\d+)\s*:\s*(\d\.\d+)");
            Assert.IsTrue(m.Success, "PHC 杭の β2 (GetBeta2) が見つかりません (書き方が変わった?)");
            string low = m.Groups[1].Value, high = m.Groups[2].Value;

            string ultimate = HelpSection("PHC 杭の安全限界曲げモーメント");
            StringAssert.Contains(ultimate, $"{low} / {high}", "ヘルプの PHC 杭の安全限界の β2 が実装と違います");
            Assert.IsFalse(ultimate.Contains("1.0 固定", StringComparison.Ordinal),
                "ヘルプに「PHC 杭の β2 は 1.0 固定」が残っています");

            StringAssert.Contains(HelpSection("PHC 杭の損傷限界曲げモーメント"), $"{low} / {high}",
                "ヘルプの PHC 杭の損傷限界 (L2) の β2 が実装と違います");
            StringAssert.Contains(HelpSection("PHC 杭の M-φ 関係"), $"{low}$ または {high}",
                "ヘルプの PHC 杭の M-φ の β2 が実装と違います");
        }

        [TestMethod]
        public void ReportPrintsTheRearPileKappaTheCodeUses()
        {
            string code = Strip(TestSource.Read("Graphics_r1", "Models", "InputData", "HorizontalSoilReaction.cs"));
            var cap = Regex.Match(code, @"Math\.Min\(\(0\.55 - 0\.007 \* phi\) \* \(rOnB - 1\.0\) \+ 0\.4,\s*(\d+(?:\.\d+)?)\)");
            Assert.IsTrue(cap.Success, "後方杭の κ の実装が見つかりません (書き方が変わった?)");

            string report = Strip(TestSource.Read("Graphics_r1", "Output", "WordDocument.Sections.cs"));
            StringAssert.Contains(report,
                @"\kappa = \min\left((0.55 - 0.007\phi)\left(\frac{R}{B} - 1.0\right) + 0.4, " + cap.Groups[1].Value + @"\right)",
                "計算書の後方杭の κ の式が実装と違います");
        }

        private static string Strip(string src) => Regex.Replace(src, "//.*", "");

        private static string Between(string src, string start, string end)
        {
            int a = src.IndexOf(start, StringComparison.Ordinal);
            int b = src.IndexOf(end, a + 1, StringComparison.Ordinal);
            Assert.IsTrue(a >= 0 && b > a, $"{start} 〜 {end} が見つかりません");
            return src[a..b];
        }

        /// <summary><paramref name="signature"/> で始まるメソッドの本体 (波括弧の対応で切り出す)。</summary>
        private static string MethodBody(string src, string signature)
        {
            int at = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(at >= 0, $"{signature} が見つかりません (名前が変わった?)");
            int open = src.IndexOf('{', at);
            int depth = 0;
            for (int i = open; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}' && --depth == 0) return src[open..(i + 1)];
            }
            Assert.Fail($"{signature} の本体の終わりが見つかりません");
            return "";
        }

        /// <summary>見出し (h4) から次の h4 まで。</summary>
        private static string HelpSection(string heading)
        {
            string help = Help();
            int a = help.IndexOf("<h4>" + heading, StringComparison.Ordinal);
            Assert.IsTrue(a >= 0, $"ヘルプに見出し「{heading}」がありません");
            int b = help.IndexOf("<h4", a + 1, StringComparison.Ordinal);
            return help[a..(b < 0 ? help.Length : b)];
        }
    }
}
