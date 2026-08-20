using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 2026-08-21 の軸力監査で見つかった 3 件の回帰検査。
    /// いずれも「計算に渡している軸力が本来の量と違う」という同じ型の欠陥。
    /// </summary>
    [TestClass]
    public class AxialForceSourceAuditTests
    {
        private static PileLayoutDataItem MakePile() => new()
        {
            No = 1,
            PileNo = 1,
            AxialForceVL0 = 3528.0,
            AxialForceLevel1s = [2996.5, 2935.8, 4059.5, 4120.2],
            AxialForceLevel2s = [2465.0, 2343.5, 4591.0, 4712.5],
        };

        // ---- ★3 GetSeismicAxialForce の境界チェック ----

        [TestMethod]
        public void SeismicAxialForce_Level2_ValidatesAgainstLevel2Collection()
        {
            // 実害が出るのはこちら: Level1s の方が短いと、有効な Level2 の索引まで弾かれ、
            // 呼び出し側 (M-φ / M-θ セットアップ) は例外を握りつぶして重力ベースへ黙って落ちる。
            var pile = MakePile();
            pile.AxialForceLevel1s = [2996.5, 2935.8];

            Assert.AreEqual(4591.0, pile.GetSeismicAxialForce(3, 2), 1e-9,
                "Level2s に有効な値があるのに Level1s の長さで弾かれている");

            // 逆に Level2s が短ければきちんと弾く。判定が Level2s 側で行われている証拠として
            // ParamName まで見る (インデクサ由来の例外と区別するため)
            pile.AxialForceLevel2s = [2465.0, 2343.5];
            var ex = Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => pile.GetSeismicAxialForce(3, 2));
            Assert.AreEqual("loadCaseNo", ex.ParamName,
                "Level2s の長さでガードしていない (インデクサ側の例外になっている)");
        }

        [TestMethod]
        public void SeismicAxialForce_RejectsZeroLoadCaseNo()
        {
            var pile = MakePile();
            // loadCaseNo は 1 始まり。0 は索引 -1 になるので下限で弾く必要がある
            var ex1 = Assert.ThrowsException<ArgumentOutOfRangeException>(() => pile.GetSeismicAxialForce(0, 1));
            Assert.AreEqual("loadCaseNo", ex1.ParamName, "L1: 下限のガードが効いていない");
            var ex2 = Assert.ThrowsException<ArgumentOutOfRangeException>(() => pile.GetSeismicAxialForce(0, 2));
            Assert.AreEqual("loadCaseNo", ex2.ParamName, "L2: 下限のガードが効いていない");
        }

        // ---- ★2 限界線・照査に使う設計軸力 ----

        [TestMethod]
        public void DesignAxialForce_PrefersSeismicThenFallsBackToGravity()
        {
            var pile = MakePile();

            Assert.AreEqual(2996.5, pile.GetDesignAxialForce(1, 1), 1e-9, "L1 の地震時軸力が使われていない");
            Assert.AreEqual(2465.0, pile.GetDesignAxialForce(1, 2), 1e-9, "L2 の地震時軸力が使われていない");

            // 範囲外 → 常時軸力
            Assert.AreEqual(pile.AxialForceVL, pile.GetDesignAxialForce(99, 2), 1e-9);

            // 未入力 (0) → 常時軸力。N=0 の耐力で限界線を描かないための保険
            pile.AxialForceLevel2s = [0.0, 0.0, 0.0, 0.0];
            Assert.AreEqual(pile.AxialForceVL, pile.GetDesignAxialForce(1, 2), 1e-9);
        }

        // ---- ★1 解析軸力を毎ステップ累積しないこと ----

        [TestMethod]
        public void AnalysisAxialForce_IsNotAccumulatedAcrossSteps()
        {
            var (inputModel, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) { Assert.Inconclusive($"例題ファイルなし: {err}"); return; }

            inputModel.UseAnalysisAxialForce = true;
            inputModel.UsePsSpringAtPileTip = false;   // 通常モード (案 Z ではない側) を検証

            var mainVm = new MainWindowViewModel { CurrentInputModel = inputModel };
            var vm = new HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };

            var modelling = new AnalysisModelling(inputModel);
            var model = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates();

            var pile = inputModel.PileLayoutItems[0];
            var headBeam = model.Beams.FirstOrDefault(b =>
                b.IsPileHeadElement && b.NodeI != null
                && Math.Abs(b.NodeI.Coord.X - pile.Point3D.X) < 0.01
                && Math.Abs(b.NodeI.Coord.Y - pile.Point3D.Y) < 0.01);
            if (headBeam == null) { Assert.Inconclusive("杭頭要素が見つからない"); return; }

            double baseAxial = model.GetAxialForce(pile);

            // ステップ 1: 解析軸力 Fxi = -10 kN (圧縮) → 入力軸力 + 10
            headBeam.CumulativeForce.Fxi = -10.0;
            vm.UpdateAxialForceFromAnalysis(model);
            Assert.AreEqual(baseAxial + 10.0, model.GetAxialForce(pile), 1e-6,
                "1 回目の解析軸力の反映が正しくない");

            // ステップ 2: Fxi は増分ではなく「そこまでの累積」。-25 になったら結果は入力軸力 + 25。
            // 以前は前ステップの -10 を打ち消さずに引き続けており、+35 になっていた
            // (過大係数はおよそ (nStep+1)/2 倍)。
            headBeam.CumulativeForce.Fxi = -25.0;
            vm.UpdateAxialForceFromAnalysis(model);
            Assert.AreEqual(baseAxial + 25.0, model.GetAxialForce(pile), 1e-6,
                "解析軸力が累積している (前ステップ分が打ち消されていない)");

            // ステップ 3: 解析軸力が戻れば軸力も戻る (単調に積み上がらない)
            headBeam.CumulativeForce.Fxi = -5.0;
            vm.UpdateAxialForceFromAnalysis(model);
            Assert.AreEqual(baseAxial + 5.0, model.GetAxialForce(pile), 1e-6,
                "解析軸力の減少が反映されていない");
        }
    }
}
