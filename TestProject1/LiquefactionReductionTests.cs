using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 液状化による水平地盤反力の低減率 βL (基礎指針'19 表4.5.1) が、
    /// <b>kh0 と py の両方に掛かって解析に届くこと</b>。
    ///
    /// <para>2026-09-12 まで βL は地盤ウィンドウと計算書に出るだけで、水平解析には
    /// 一切効いていなかった (使っていたのは鋼管杭の座屈長を数える
    /// <c>SteelPipeBuckling</c> だけ)。液状化した層は杭を横から支えられなくなるので、
    /// 効かせないと液状化ケースの応力を過小評価する。</para>
    ///
    /// <para>βL は<b>深さ (土質点) ごと・地震動レベルごと</b>に違い、液状化を考慮する
    /// ケースだけ 1 未満になる。そのため群杭係数 ξ・杭間隔比 R/B と同じく
    /// <see cref="GroupPileEffect"/> に載せて<b>要素単位で評価時に渡す</b>。</para>
    /// </summary>
    [TestClass]
    public class LiquefactionReductionTests
    {
        // ── 要素 → 土質点 の引き当て (GroundInput.LiquefactionReductionAt) ──

        /// <summary>
        /// 土質点 3 件 (層厚 2 m ずつ) の地盤。2 番目だけ液状化層で βL = L1 0.6 / L2 0.2。
        /// </summary>
        private static GroundInput Ground(bool judged = true)
        {
            var g = new GroundInput { GroundTopAltitude = 0.0 };
            g.GroundMassesData = [];
            for (int i = 1; i <= 3; i++)
            {
                bool isLiq = judged && i == 2;
                g.GroundMassesData.Add(new GroundMassDataInput
                {
                    No = i,
                    GLDepth = -2.0 * i,
                    H = 2.0,
                    NValue = 5,
                    IsLiquefactionLayer = isLiq,
                    // 判定していない地盤では [0, 0] のまま残る (「未判定」で「完全液状化」ではない)
                    BetaL = isLiq ? [0.6, 0.2] : new ObservableCollection<double?> { 0.0, 0.0 },
                });
            }
            return g;
        }

        /// <summary>土質点の範囲は層の上端から層厚ぶん。2 番目は GL−2〜−4 m。</summary>
        [DataTestMethod]
        [DataRow(-0.5, -1.5, 1.0)]   // 1 番目 (液状化層でない)
        [DataRow(-2.2, -3.8, 0.2)]   // 2 番目 (液状化層) → レベル2 の βL
        [DataRow(-4.5, -5.5, 1.0)]   // 3 番目 (液状化層でない)
        [DataRow(-7.0, -8.0, 1.0)]   // 最下の土質点より深い
        public void TheReductionComesFromTheMassThatContainsTheElement(
            double zTop, double zBtm, double expected)
        {
            Assert.AreEqual(expected, Ground().LiquefactionReductionAt(zTop, zBtm, levelIndex: 1), 1e-12,
                $"要素 ({zTop}〜{zBtm} m) の βL が土質点の範囲から引けていません");
        }

        /// <summary>βL はレベルごとに違う (表 4.5.1 は深さと Na の関数で、レベルで Na が変わる)。</summary>
        [TestMethod]
        public void TheReductionIsPerLevel()
        {
            var g = Ground();
            Assert.AreEqual(0.6, g.LiquefactionReductionAt(-2.2, -3.8, levelIndex: 0), 1e-12, "レベル1 の βL");
            Assert.AreEqual(0.2, g.LiquefactionReductionAt(-2.2, -3.8, levelIndex: 1), 1e-12, "レベル2 の βL");
        }

        /// <summary>
        /// <b>βL の初期値 [0, 0] は「未判定」で「完全液状化」ではない。</b>
        /// 液状化の判定を一度も行っていない地盤で 0 を低減率として使うと、
        /// 水平地盤ばねが全部消えて解析が解けなくなる。
        /// 判定済みかどうか (<c>IsLiquefactionLayer</c>) で切り分ける。
        /// </summary>
        [TestMethod]
        public void AnUnjudgedGroundIsNotTreatedAsFullyLiquefied()
        {
            var g = Ground(judged: false);
            foreach (int level in new[] { 0, 1 })
            {
                Assert.AreEqual(1.0, g.LiquefactionReductionAt(-2.2, -3.8, level), 0.0,
                    "液状化の判定をしていない地盤で βL = 0 (完全液状化) として扱われています");
            }
        }

        /// <summary>VL (レベルを持たないケース) は低減しない。</summary>
        [TestMethod]
        public void TheVerticalLoadCaseIsNotReduced()
        {
            Assert.AreEqual(1.0, Ground().LiquefactionReductionAt(-2.2, -3.8, levelIndex: -1), 0.0);
        }

        /// <summary>液状化層でも βL が null (対象外) なら低減しない。</summary>
        [TestMethod]
        public void ANullReductionMeansNoReduction()
        {
            var g = Ground();
            g.GroundMassesData[1].BetaL = [null, null];
            Assert.AreEqual(1.0, g.LiquefactionReductionAt(-2.2, -3.8, levelIndex: 1), 0.0);
        }

        // ── kh0 と py の両方に掛かること ──────────────────────────

        private static HorizontalSoilReactionItem Sand()
        {
            var item = new HorizontalSoilReactionItem();
            item.SetParameters(
                name: "砂", soilType: "砂質土", gamma: 18, b: 1.0, e0: 2800,
                zTop: -5.0, zBtm: -6.0, xi: 1.0, rOnB: 0.0, nValue: 10, phi: 30, cu: 0,
                sigmaZPrimeTop: 100, sigmaZPrimeBtm: 100);
            return item;
        }

        [TestMethod]
        public void TheReductionAppliesToBothTheCoefficientAndThePlasticReaction()
        {
            var item = Sand();
            var full = GroupPileEffect.None;
            var reduced = GroupPileEffect.None.WithLiquefaction(0.2);

            Assert.AreEqual(0.2, item.GetKh0For(reduced) / item.GetKh0For(full), 1e-12,
                "βL が kh0 に掛かっていません");
            Assert.AreEqual(0.2,
                item.GetPyFor(isTop: true, isFront: true, reduced)
                    / item.GetPyFor(isTop: true, isFront: true, full), 1e-12,
                "βL が py に掛かっていません");
        }

        /// <summary>群杭係数 ξ と液状化低減 βL は kh0 に対して両方掛かる (片方で上書きしない)。</summary>
        [TestMethod]
        public void TheReductionMultipliesWithTheGroupPileFactor()
        {
            var item = Sand();
            var effect = new GroupPileEffect { Xi = 0.8, ROnB = 0.0, BetaL = 0.5 };
            Assert.AreEqual(item.Kh0 * 0.8 * 0.5, item.GetKh0For(effect), item.Kh0 * 1e-12,
                "ξ と βL が両方 kh0 に掛かっていません");
        }

        /// <summary>
        /// βL は kh0 と py の両方に同じだけ掛かるので、p-y 曲線は<b>全体が βL 倍</b>になり、
        /// 降伏変位 y<sub>y</sub> = (p<sub>y</sub>/k<sub>h0</sub>)²/y<sub>0</sub> は変わらない。
        /// 片方だけに掛けると曲線の形が変わり (降伏が早まる/遅れる)、この関係が崩れる。
        /// </summary>
        [DataTestMethod]
        [DataRow(0.0005)]   // 弾性域
        [DataRow(0.005)]    // sqrt 域
        [DataRow(0.05)]     // 降伏後
        [DataRow(0.3)]
        public void TheWholeCurveScalesWithTheReduction(double y)
        {
            var item = Sand();
            var full = GroupPileEffect.None;
            var reduced = GroupPileEffect.None.WithLiquefaction(0.25);

            double secFull = item.GetSoilSecantReactionCoefficient(y, isTop: true, isFront: true, full);
            double secRed = item.GetSoilSecantReactionCoefficient(y, isTop: true, isFront: true, reduced);
            double tanFull = item.GetSoilTangentReactionCoefficient(y, isTop: true, isFront: true, full);
            double tanRed = item.GetSoilTangentReactionCoefficient(y, isTop: true, isFront: true, reduced);

            Assert.AreEqual(0.25, secRed / secFull, 1e-9, $"y = {y} m で割線剛性が βL 倍になっていません");
            Assert.AreEqual(0.25, tanRed / tanFull, 1e-9, $"y = {y} m で接線剛性が βL 倍になっていません");

            // 降伏の判定は βL に依らない (kh0 と py が同じ割合で下がるため)
            Assert.AreEqual(item.IsYieldedAtY(y, isTop: true, isFront: true, full),
                item.IsYieldedAtY(y, isTop: true, isFront: true, reduced),
                $"y = {y} m で降伏の判定が βL で変わっています");
        }

        /// <summary>低減なしの補正 (None / For) は βL = 1。既定値 0 のまま使うとばねが消える。</summary>
        [TestMethod]
        public void TheDefaultFactoriesMeanNoReduction()
        {
            Assert.AreEqual(1.0, GroupPileEffect.None.BetaL, 0.0);
            Assert.AreEqual(1.0, GroupPileEffect.For(new PileLayoutDataItem()).BetaL, 0.0);
            Assert.AreEqual(1.0, GroupPileEffect.For(null).BetaL, 0.0);
        }

        // ── 解析へ届いていること ─────────────────────────────────

        /// <summary>
        /// ばねの組立 (PrepareKmat) が、ケースの液状化・レベルを見て要素ごとの βL を掛けていること。
        /// 掛け忘れても解析は通り、結果が過小評価になるだけなので落ちない。ここで見張る。
        /// </summary>
        [TestMethod]
        public void TheSpringAssemblyAppliesTheReduction()
        {
            string source = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            string body = TestSource.MethodBody(source, "void PrepareKmat(int iLC, bool isTan, AnaModel model");

            StringAssert.Contains(body, "CaseIsLiquefaction",
                "ばねの組立がケースの液状化を見ていません (βL が液状化ケースに効きません)");
            StringAssert.Contains(body, "LiquefactionReductionAt",
                "ばねの組立が要素ごとの βL を引いていません");
            StringAssert.Contains(body, "WithLiquefaction",
                "ばねの組立が βL を GroupPileEffect に載せていません");
        }

        /// <summary>
        /// 同梱例題に βL が 1 未満の土質点があること。全部 1 なら、この経路は
        /// 回帰網を通っても一度も使われない (網が空振りする)。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9")]
        [DataRow("Example10")]
        public void TheBundledExamplesActuallyExerciseTheReduction(string groundName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, "PileExample" + groundName.Replace("Example", ""));
            if (model == null) { Assert.Inconclusive(error); return; }

            var ground = model.GroundsInput[0];
            var reduced = ground.GroundMassesData
                .Where(m => m.IsLiquefactionLayer && m.BetaL != null && m.BetaL.Count > 1
                            && m.BetaL[1].HasValue && m.BetaL[1]!.Value < 1.0 - 1e-9)
                .ToList();

            Assert.IsTrue(reduced.Count > 0,
                $"{groundName}: βL が 1 未満の土質点がありません。液状化の低減が一度も使われません");

            // 杭が通る深さで実際に低減が効くこと
            var soilPile = model.ElementDivision.SoilPiles[0];
            var betas = soilPile.HorizontalSoilReactions
                .Select(r => ground.LiquefactionReductionAt(r.ZTop, r.ZBtm, levelIndex: 1))
                .ToList();
            Assert.IsTrue(betas.Any(b => b < 1.0 - 1e-9),
                $"{groundName}: 杭の要素で βL が 1 未満になりません (杭が液状化層を通っていない)");
            Assert.IsTrue(betas.All(b => b > 0.0),
                $"{groundName}: βL が 0 の要素があります (ばねが完全に消えます): "
                + string.Join(", ", betas.Select(b => b.ToString("0.00"))));
        }
    }
}
