using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 杭頭 M-θ が現ステップの軸力に追随することの検証。
    ///
    /// 杭軸力は解析中に動く（SetVectorDF が設定した増分を UpdateF が毎ステップ加算する、
    /// VL → 入力地震時軸力の荷重ステップ比例のランプ）。杭体の M-φ は元からこれに追随していたが、
    /// 杭頭 M-θ はケース開始時の 1 回きりで満載時の地震時軸力に固定されており、
    /// 序盤のステップで杭体と杭頭が別の軸力の断面として振る舞っていた (2026-08-21 修正)。
    /// </summary>
    [TestClass]
    public class MThetaAxialForceTests
    {
        private static (HorizontalCalculationViewModel vm, AnaModel model, InputModel input, LoadCase lc)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            foreach (var c in inputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases)
                c.IsPileNonLinear = true;

            var mainVm = new MainWindowViewModel { CurrentInputModel = inputModel };
            var vm = new HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };

            var modelling = new AnalysisModelling(inputModel);
            var model = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates();

            var lc = inputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(c => c.Level == 2);
            return (vm, model, inputModel, lc);
        }

        /// <summary>ばね名 "RθXY-{pileNo}" から対応する杭を引く（本体と同じ規約）。</summary>
        private static PileLayoutDataItem? PileOf(InputModel input, RotationalSpring spring)
        {
            if (spring.Name == null || !spring.Name.Contains('-')) return null;
            var parts = spring.Name.Split('-');
            if (parts.Length < 2 || !int.TryParse(parts[^1], out int pileNo)) return null;
            return input.PileLayoutItems?.FirstOrDefault(p => p.No == pileNo);
        }

        [TestMethod]
        public void MTheta_FollowsCurrentStepAxialForce_AndKeepsCrackHistory()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, model, input, lc) = built.Value;

            // ケース開始時のセットアップ: 入力地震時軸力で構築される
            vm.SetupNonlinearMThetaForLoadCase(model, lc);

            var spring = model.RotationalSprings.FirstOrDefault(
                s => s.McrXY.HasValue && PileOf(input, s) != null);
            if (spring == null) { Assert.Inconclusive("Mcr を持つ杭頭ばねが無い例題"); return; }

            var pile = PileOf(input, spring)!;
            var pileBody = input.PileBodies[(spring.PileBodyNo is int v && v > 0 ? v : 1) - 1];

            double seismic = pile.GetSeismicAxialForce(lc.No, lc.Level);
            double vl = pile.AxialForceVL0 + pile.AxialForceVLAdditional;
            Assert.AreNotEqual(vl, seismic, 1e-6,
                "この例題では地震時軸力と常時軸力が同値のため検証が成立しない");

            double mcrAtSeismic = spring.McrXY!.Value;
            Assert.AreEqual(pileBody.GetMThetaRelationship(Math.Round(seismic)).McrXY ?? double.NaN,
                mcrAtSeismic, 1e-6, "ケース開始時は入力地震時軸力で構築されるはず");

            // 序盤ステップを模擬: 軸力はまだ常時側に近い
            spring.MarkCracked(0.0, 100.0);          // クラック履歴を付ける
            Assert.IsTrue(spring.HasCrackedXY);
            model.SetAxialForce(pile, vl);

            vm.UpdateMThetaByCurrentAxialForLoadCase(model, lc);

            // (1) 現ステップの軸力で作り直されている
            double expectedMcrAtVl = pileBody.GetMThetaRelationship(Math.Round(vl)).McrXY ?? double.NaN;
            Assert.AreEqual(expectedMcrAtVl, spring.McrXY!.Value, 1e-6,
                "M-θ が現ステップの軸力で作り直されていない");
            Assert.AreNotEqual(mcrAtSeismic, spring.McrXY!.Value, 1e-6,
                "軸力を変えたのに M-θ が変わっていない");

            // (2) クラック履歴は保持される (ここでリセットするとヒステリシスが壊れる)
            Assert.IsTrue(spring.HasCrackedXY, "ステップ毎の再解決でクラック履歴が消えている");
            Assert.IsNotNull(spring.CrackNx, "クラック方向が消えている");
            Assert.IsNotNull(spring.CrackNy, "クラック方向が消えている");
        }

        [TestMethod]
        public void MTheta_CaseSetup_ResetsCrackHistory()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, model, input, lc) = built.Value;

            vm.SetupNonlinearMThetaForLoadCase(model, lc);
            var spring = model.RotationalSprings.FirstOrDefault(s => s.McrXY.HasValue);
            if (spring == null) { Assert.Inconclusive("Mcr を持つ杭頭ばねが無い例題"); return; }

            spring.MarkCracked(0.0, 100.0);
            Assert.IsTrue(spring.HasCrackedXY);

            // ケース開始時のセットアップはケース間独立のため履歴をリセットする
            vm.SetupNonlinearMThetaForLoadCase(model, lc);
            Assert.IsFalse(spring.HasCrackedXY, "ケース開始時にクラック履歴がリセットされていない");
        }
    }
}
