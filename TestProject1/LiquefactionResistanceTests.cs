using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 液状化抵抗比 τL/σz′ が、図3.2.1 の 5% の線を表す閉じた形の式であること。
    ///
    /// <para>基礎指針'19 は図から読み取るとしており式は無い。実装には 2 つの式があり、
    /// 使われていたのは近似式 0.0410·{√Na + 0.00903·(Na/10)^7}、使われていなかったのが
    /// 閉じた形 a·Cr·{16√Na/100 + (16√Na/Cs)^n} (a = 0.45, Cr = 0.57, Cs = 80, n = 14)。
    /// 近似式は Cs ≈ 80.7 に相当し、立ち上がり付近で小さめ (Na = 28 で 8.6%)。
    /// 2026-09-12 に利用者と図を見比べ、5% の線 (Na = 25 で立ち上がる) に近い閉じた形に切り替えた。</para>
    ///
    /// <para>定数は文献 (図の線) が正なので、期待値はここに書く。</para>
    /// </summary>
    [TestClass]
    public class LiquefactionResistanceTests
    {
        private static double ClosedForm(double na)
            => 0.45 * 0.57 * (16 * Math.Sqrt(na) / 100 + Math.Pow(16 * Math.Sqrt(na) / 80, 14));

        [DataTestMethod]
        [DataRow(5.0)]
        [DataRow(10.0)]
        [DataRow(20.0)]
        [DataRow(25.0)]
        [DataRow(28.0)]
        public void UsesTheClosedForm(double na)
        {
            Assert.AreEqual(ClosedForm(na), GroundLayerViewModel.LiquefactionResistance(na), 1e-12);
        }

        [TestMethod]
        public void RisesAtNaEqualTo25()
        {
            // 16√25/80 = 1 → 0.45·0.57·(0.8 + 1) = 0.4617
            Assert.AreEqual(0.45 * 0.57 * 1.8, GroundLayerViewModel.LiquefactionResistance(25.0), 1e-12);
        }

        [TestMethod]
        public void OnlyOneFormulaRemains()
        {
            string src = Regex.Replace(TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs"), "//.*", "");
            Assert.IsFalse(src.Contains("0.00903", StringComparison.Ordinal), "旧近似式がコードに残っています");
            Assert.IsFalse(src.Contains("RecalculateTauLonSigmaZPrime2", StringComparison.Ordinal), "2 つ目の τL の計算が残っています");
        }

        [TestMethod]
        public void HelpShowsBothFormulasAndWhichIsAdopted()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            StringAssert.Contains(help, @"C_s = 80,\ n = 14$$", "ヘルプに採用式の定数がありません");
            StringAssert.Contains(help, "<div class=\"formula-right\">採用</div>", "ヘルプに採用式の印がありません");
            StringAssert.Contains(help, "<div class=\"formula-right\">旧（不採用）</div>", "ヘルプに旧式がありません");
        }
    }
}
