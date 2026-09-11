using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;

namespace TestProject1
{
    /// <summary>
    /// 基準水平地盤反力係数 kh0 = α ξ E0 (B/B0)^(-3/4) の α が文献値 80 [1/m] であること
    /// (基礎指針'19 (6.6.12))。
    ///
    /// <para>解析は初版から α = 60 (<c>SetParameters</c> の既定引数) で、呼び出し側はどちらも
    /// 値を渡していなかった。ヘルプの記号表と計算書は 80 と書いていたので、
    /// <b>計算書が解析と違う α を印字していた</b>。2026-09-11 に利用者が文献で 80 を確認し、
    /// 解析を直した (水平解析の地盤ばねがすべて 4/3 倍になる)。</para>
    ///
    /// <para>この値は実装ではなく文献が正なので、期待値はここに書く。</para>
    /// </summary>
    [TestClass]
    public class Kh0AlphaTests
    {
        private const double Literature = 80.0;

        [TestMethod]
        public void TheAnalysisUsesTheLiteratureAlpha()
        {
            Assert.AreEqual(Literature, HorizontalSoilReactionItem.Kh0Alpha, 0.0);

            // 呼び出し側は α を渡さないので、既定引数がそのまま効く
            var item = new HorizontalSoilReactionItem();
            item.SetParameters("層", "砂質土", gamma: 18, b: 1.0, e0: 2800, zTop: 0, zBtm: -1,
                xi: 1.0, rOnB: 10_000, nValue: 4, phi: 30, cu: 0, sigmaZPrimeTop: 10, sigmaZPrimeBtm: 20);
            double expected = Literature * 1.0 * 2800 * Math.Pow(1.0 / 0.01, -0.75);
            Assert.AreEqual(expected, item.Kh0, expected * 1e-12, "kh0 が α = 80 で計算されていません");
        }

        [TestMethod]
        public void HelpAndReportPrintTheSameAlpha()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            StringAssert.Contains(help, "<li><span class=\"symbol-label\">$\\alpha$</span>" + Literature + "</li>",
                "ヘルプの kh0 の α が文献値と違います");

            string report = TestSource.Read("Graphics_r1", "Output", "WordDocument.Sections.cs");
            StringAssert.Contains(report, "\": " + Literature + "m<^-1>\"", "計算書の kh0 の α が文献値と違います");
        }
    }
}
