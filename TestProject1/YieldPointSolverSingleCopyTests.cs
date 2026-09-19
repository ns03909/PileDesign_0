using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 降伏点の 2 変数ニュートン法が 1 か所にしかないこと。
    ///
    /// <para>2026-09-19 まで、場所打ち RC・場所打ち鋼管コンクリート杭頭・既製杭杭頭の 3 つに
    /// 同じ本体が 113 行ずつ写されていた。差は先頭 2 行 (引張降伏ひずみと圧縮縁〜主筋重心を
    /// どの主筋から取るか) だけで、解法は完全に同じだった。写しは直すと取り残される。</para>
    ///
    /// <para>解法を <c>AbstractPileSection.SolveYieldPoint2DNewton(Ntarget, epsY, cSteel)</c> に
    /// 寄せ、3 つの断面は epsY と cSteel を渡すだけにした。ここでは「反復の本体が派生側へ
    /// 戻っていないこと」を走査で見張る。</para>
    /// </summary>
    [TestClass]
    public class YieldPointSolverSingleCopyTests
    {
        private static readonly string[] Sections =
        {
            "InsituReinforcedConcreteSection.cs",
            "InsituSteelPipeReinforcedConcreteTopSection.cs",
            "PrecastPileTopSection.cs",
        };

        /// <summary>
        /// 3 つの断面の <c>SolveYieldPoint2DNewton</c> は、基底へ値を渡すだけの 1 式であること。
        /// 反復や連立の解きが本体に戻っていたら落ちる。
        /// </summary>
        [TestMethod]
        public void TheThreeSectionsOnlyForwardToTheSharedSolver()
        {
            int scanned = 0;
            foreach (var file in Sections)
            {
                string src = TestSource.Read("Graphics_r1", "Models", "InputData", file);
                var m = Regex.Match(src,
                    @"private \(bool hasYield, double My, double phiY\) SolveYieldPoint2DNewton\(double Ntarget\)(.{0,400}?);",
                    RegexOptions.Singleline);
                Assert.IsTrue(m.Success, $"{file}: SolveYieldPoint2DNewton が見つかりません (書き方が変わった?)");

                string body = m.Groups[1].Value;
                StringAssert.Contains(body, "=> SolveYieldPoint2DNewton(Ntarget,",
                    $"{file}: 基底へ渡すだけの形でなくなっています");
                Assert.IsFalse(body.Contains("for (") || body.Contains("while ("),
                    $"{file}: 反復が派生側へ戻っています (解法は基底に 1 つだけ)");
                scanned++;
            }
            TestSource.AssertScanned(scanned, Sections.Length, "降伏点ソルバを呼ぶ断面");
        }

        /// <summary>
        /// 解法の本体 (ヤコビアンの数値差分) は基底に 1 つだけ。
        /// </summary>
        [TestMethod]
        public void TheSolverBodyLivesOnlyInTheBaseClass()
        {
            string baseSrc = TestSource.Read("Graphics_r1", "Models", "InputData", "AbstractPileSection.cs");
            StringAssert.Contains(baseSrc,
                "internal (bool hasYield, double My, double phiY) SolveYieldPoint2DNewton(",
                "基底に共通の解法がありません");

            // ヤコビアンの数値差分は解法の中心。派生に現れたら写しが戻っている
            const string marker = "double df1_deps = (N_deps - Nval) / dEps;";
            Assert.AreEqual(1, Regex.Matches(baseSrc, Regex.Escape(marker)).Count,
                "基底の解法が 1 つではありません");
            foreach (var file in Sections)
            {
                string src = TestSource.Read("Graphics_r1", "Models", "InputData", file);
                Assert.IsFalse(src.Contains(marker), $"{file}: 解法の写しが戻っています");
            }
        }
    }
}
