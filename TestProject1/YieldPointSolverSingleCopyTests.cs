using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 降伏点まわりの解法が 1 か所にしかないこと。
    ///
    /// <para>2026-09-19 まで、場所打ち RC・場所打ち鋼管コンクリート杭頭・既製杭杭頭の 3 つに
    /// 同じ本体が写されていた (2 変数ニュートン法 113 行 / 降伏モーメント 57 行 /
    /// 降伏 N-M 相関 34 行 / 終局側 N-M 9 行)。差は主筋を <c>MainBars</c> から取るか
    /// <c>MainBars1</c> から取るかだけ。写しは直すと取り残される。</para>
    ///
    /// <para>実際に取り残されていた: 終局側の圧縮縁ひずみ εcu を、場所打ち RC だけが定数
    /// <c>ULTIMATE_COMPRESSIVE_STRAIN</c> で参照し、残り 2 つは <c>0.003</c> を直書きしていた。
    /// 値は同じだが、定数を変えても片方しか動かない形だった。今は 3 つとも基底の
    /// <c>UltimateCompressiveStrain</c> (断面ごとに上書きできる) を通る。</para>
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
        /// 派生側に残ってよいのは「基底へ値を渡すだけ」の式。ここでは<b>転送の式</b>を数える
        /// (メソッド名で探すと呼び出し側に当たるので、式の形で照合する)。
        /// 解法の本体が戻っていないことは <see cref="TheSolverBodiesLiveOnlyInTheBaseClass"/> が見る。
        /// </summary>
        private static readonly (string Member, string Forwarding)[] Forwarders =
        {
            ("SolveYieldPoint2DNewton", "=> SolveYieldPoint2DNewton(Ntarget,"),
            ("GetSteelYieldNMax", "=> SteelYieldNMax("),
            ("GetSteelYieldMoment", "=> SolveSteelYieldMoment(Ntarget,"),
            ("GetSteelYieldMNInteraction", "=> SolveSteelYieldMNInteraction("),
        };

        /// <summary>解法の中心。派生側に現れたら写しが戻っている。</summary>
        private static readonly (string Member, string Marker)[] BodyMarkers =
        {
            ("SolveYieldPoint2DNewton", "double df1_deps = (N_deps - Nval) / dEps;"),
            ("SteelYieldNMax", "double phi = (epsilonC + epsY) / Math.Max(lever, 1e-9);"),
            ("SolveSteelYieldMoment", "internal (double M, double curvature) SolveSteelYieldMoment("),
            ("SolveSteelYieldMNInteraction", "SolveSteelYieldMNInteraction(double epsY, double lever)"),
        };

        private static string Section(string file) => TestSource.Read("Graphics_r1", "Models", "InputData", file);

        private static string Base() => TestSource.Read("Graphics_r1", "Models", "InputData", "AbstractPileSection.cs");

        /// <summary>
        /// 3 つの断面に残っているのは、基底へ値を渡すだけの式であること。
        /// 反復や連立の解きが本体に戻っていたら落ちる。
        /// </summary>
        [TestMethod]
        public void TheThreeSectionsOnlyForwardToTheSharedSolvers()
        {
            int scanned = 0;
            foreach (var file in Sections)
            {
                string src = Section(file);
                foreach (var (member, forwarding) in Forwarders)
                {
                    Assert.AreEqual(1, Regex.Matches(src, Regex.Escape(forwarding)).Count,
                        $"{file}: {member} が基底へ渡すだけの形ではありません (転送の式が 1 つではない)");
                    scanned++;
                }
            }
            TestSource.AssertScanned(scanned, Sections.Length * Forwarders.Length, "基底へ渡すだけのメソッド");
        }

        /// <summary>解法の本体は基底に 1 つだけで、派生には無いこと。</summary>
        [TestMethod]
        public void TheSolverBodiesLiveOnlyInTheBaseClass()
        {
            string baseSrc = Base();
            foreach (var (member, marker) in BodyMarkers)
            {
                Assert.AreEqual(1, Regex.Matches(baseSrc, Regex.Escape(marker)).Count,
                    $"基底の {member} が 1 つではありません");
                foreach (var file in Sections)
                    Assert.IsFalse(Section(file).Contains(marker), $"{file}: {member} の写しが戻っています");
            }
        }

        /// <summary>
        /// 終局側の圧縮縁ひずみは、断面ごとに上書きできる <c>UltimateCompressiveStrain</c> から取ること。
        /// 定数の直接参照や 0.003 の直書きに戻ったら落ちる (3 つで割れていたのが元の姿)。
        /// </summary>
        [TestMethod]
        public void TheUltimateStrainComesFromTheOverridableProperty()
        {
            StringAssert.Contains(Base(), "double epsilonC = UltimateCompressiveStrain;",
                "基底の SteelYieldNMax が上書きできるひずみを使っていません");

            foreach (var file in Sections)
            {
                string src = Section(file);
                Assert.IsFalse(Regex.IsMatch(src, @"epsilonC\s*=\s*0\.003"),
                    $"{file}: 終局側の圧縮縁ひずみが直書きに戻っています");
                Assert.IsFalse(src.Contains("ULTIMATE_COMPRESSIVE_STRAIN"),
                    $"{file}: 定数を直接参照しています (断面ごとの上書きが効かなくなります)");
            }
        }
    }
}
