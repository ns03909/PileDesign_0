using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 液状化のケースで、土圧合力ばね (根入部) に液状化による低減率 βLq が掛かること (2026-11-23)。
    ///
    /// <para>基礎指針'19 4.3 節は「液状化地盤中では、3.2 節 2. の杭の水平地盤反力係数の低減を準用し、
    /// 基礎根入れ部の土圧合力 PEt に塑性地盤反力の低減値 αL (図3.2.13 の βL と等しい) を乗じる」とする。
    /// 以前は液状化のケースでも地盤変位を液状化時の値に替えるだけで、土圧は低減していなかった。
    /// 掛け忘れても解析は通るので、ここで見張る。</para>
    /// </summary>
    [TestClass]
    public class EarthPressureLiquefactionReductionTests
    {
        /// <summary>
        /// 土質点 3 件 (層厚 2 m ずつ) の地盤を根入部の地盤にしたモデル。2 番目 (GL−2〜−4 m) だけ液状化層で
        /// βLq = L1 0.6 / L2 0.2 (<see cref="LiquefactionReductionTests"/> と同じ置き方)。
        /// </summary>
        private static InputModel Model()
        {
            var input = new MainWindowViewModel().CurrentInputModel!;
            var g = new GroundInput { GroundTopAltitude = 0.0 };
            g.GroundMassesData = [];
            for (int i = 1; i <= 3; i++)
            {
                bool isLiq = i == 2;
                g.GroundMassesData.Add(new GroundMassDataInput
                {
                    No = i, GLDepth = -2.0 * i, H = 2.0, NValue = 5,
                    IsLiquefactionLayer = isLiq,
                    BetaL = isLiq ? [0.6, 0.2] : new ObservableCollection<double?> { 0.0, 0.0 },
                });
            }
            input.GroundsInput[0] = g;
            input.ElementDivision.SoilEmbedment = new SoilEmbedment(1, 0.0, []);
            return input;
        }

        private static DoatsuGoryokuBaneItem Item(double zTop, double zBtm) => new() { ZTop = zTop, ZBtm = zBtm };

        [DataTestMethod]
        [DataRow(-2.2, -3.8, 1, 0.6)]   // 液状化層・レベル1
        [DataRow(-2.2, -3.8, 2, 0.2)]   // 液状化層・レベル2
        [DataRow(-0.5, -1.5, 2, 1.0)]   // 液状化層でない
        [DataRow(-2.2, -3.8, 0, 1.0)]   // VL
        public void TheReductionComesFromTheEmbedmentGround(double zTop, double zBtm, int level, double expected)
        {
            var input = Model();
            Assert.AreEqual(expected, input.EarthPressureLiquefactionReduction(Item(zTop, zBtm), isLiquefaction: true, level), 1e-12);
        }

        [TestMethod]
        public void CasesWithoutLiquefactionAreNotReduced()
        {
            var input = Model();
            Assert.AreEqual(1.0, input.EarthPressureLiquefactionReduction(Item(-2.2, -3.8), isLiquefaction: false, 2), 0.0);

            input.ElementDivision.SoilEmbedment = null;
            Assert.AreEqual(1.0, input.EarthPressureLiquefactionReduction(Item(-2.2, -3.8), isLiquefaction: true, 2), 0.0,
                "根入部が無ければ低減しない");
        }

        /// <summary>ばねの組立 (PrepareKmat) と、土圧合力ばねのグラフの理論曲線の両方が βLq を通すこと。</summary>
        [TestMethod]
        public void TheSpringAssemblyAndTheGraphApplyTheReduction()
        {
            string solver = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Solver.cs");
            string body = TestSource.MethodBody(solver, "void PrepareKmat(int iLC, bool isTan, AnaModel model");
            StringAssert.Contains(body, "EarthPressureLiquefactionReduction(item, model.CaseIsLiquefaction, model.CaseLevel)",
                "ばねの組立が土圧合力ばねに βLq を掛けていません");

            string graph = TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.CurveGraphs.cs");
            StringAssert.Contains(graph, "EarthPressureLiquefactionReduction(item, isLiquefaction, loadCase.Level)",
                "土圧合力ばねのグラフの理論曲線が βLq を掛けていません (解析と食い違います)");
        }

        /// <summary>
        /// 計算例9 の地盤で、根入部 (0〜−6.5 m) の区間に土質点の βLq が届くこと。
        ///
        /// <para>例題のままでは根入部の砂層 (GL−3.5〜−6.5 m) は N = 19〜29 で液状化せず、βLq はすべて 1
        /// (液状化するのは根入部より下の N = 1 の層)。同梱の例題で根入部を持つのは計算例9 だけなので、
        /// βLq を掛けても例題の結果は変わらない。そのままでは何も確かめずに合格するので、根入部の砂層の
        /// N 値を 3 に下げて液状化させてから見る。</para>
        /// </summary>
        [TestMethod]
        public void TheEmbedmentOfExample9IsReducedOnceItLiquefies()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (input?.ElementDivision?.SoilEmbedment == null) { Assert.Inconclusive(error); return; }
            var ground = input.GroundsInput[input.ElementDivision.SoilEmbedment.GroundNo - 1];
            DoatsuGoryokuBaneItem[] items = [Item(0.0, -0.8), Item(-2.5, -3.5), Item(-3.5, -4.5), Item(-4.5, -5.5), Item(-5.5, -6.5)];
            double[] Betas() => items.Select(it => input.EarthPressureLiquefactionReduction(it, isLiquefaction: true, level: 2)).ToArray();

            new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground }.Update();
            Assert.IsTrue(Betas().All(b => b == 1.0), "(前提) 例題のままでは根入部は液状化しない: " + string.Join(", ", Betas()));

            foreach (var m in ground.GroundMassesData.Where(m => m.GLDepth <= -3.5 && m.GLDepth >= -6.5))
                m.NValue = 3.0;
            new GroundLayerViewModel(new MainWindowViewModel()) { GroundInput = ground }.Update();

            var betas = Betas();
            Assert.AreEqual(1.0, betas[0], 0.0, "地下水位より上 (0〜−0.8 m) は低減しない");
            Assert.IsTrue(betas.Skip(2).All(b => b < 1.0 - 1e-9),
                "N 値を下げて液状化させた根入部の区間に βLq が届いていません: " + string.Join(", ", betas.Select(b => b.ToString("0.00"))));
            Assert.AreEqual(ground.LiquefactionReductionAt(-3.5, -4.5, 1), betas[2], 0.0, "杭の p-y と同じ βLq を使う");
        }
    }
}
