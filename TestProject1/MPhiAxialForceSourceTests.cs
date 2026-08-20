using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// M-φ を構築する軸力の出所を検証する。
    ///
    /// 2026-08-21 まで、ステップ毎再解決 (SetupMPhiByCurrentAxialForMiddleBeam) は
    /// 常に model.GetAxialForce (= 常時 VL) を使っていた。これはステップループの先頭で
    /// 初期セットアップの曲線を上書きするため、「入力値」モードでは
    /// 入力された地震時軸力 (AxialForceLevel{1,2}s) が M-φ に一切反映されていなかった。
    /// 1200φ・Fc27・16-D29 の断面では、変動下限の杭で Mcr を 22% 過大、
    /// 上限の杭で 17% 過小に評価していた。
    ///
    /// 仕様（M-θ と同じ優先順位）:
    ///   「入力値」モード         … 地震時軸力を優先、未入力 (0) なら重力ベースへフォールバック
    ///   「入力値＋応力解析結果」 … model.GetAxialForce（解析 Fxi 加算後の現在軸力）をそのまま使う
    /// </summary>
    [TestClass]
    public class MPhiAxialForceSourceTests
    {
        private static (HorizontalCalculationViewModel vm, AnaModel model, InputModel input)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var mainVm = new MainWindowViewModel { CurrentInputModel = inputModel };
            var vm = new HorizontalCalculationViewModel(mainVm) { BypassUiPromptsForTesting = true };

            var modelling = new AnalysisModelling(inputModel);
            var model = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates(); // AxialForce = AxialForceVL0 + AxialForceVLAdditional (常時 VL)

            return (vm, model, inputModel);
        }

        /// <summary>
        /// 例題ビルダーが軸力を写していること。写し忘れると全杭 N=0 になり、
        /// 収束回帰テストが M-φ の軸力依存性を一切踏まなくなる。
        /// </summary>
        [TestMethod]
        public void ExampleBuilder_CarriesAxialForces()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (_, model, input) = built.Value;

            var pile = input.PileLayoutItems[0];
            Assert.AreNotEqual(0.0, pile.AxialForceVL0, "AxialForceVL0 が写されていない");
            Assert.AreNotEqual(0.0, model.GetAxialForce(pile), "常時軸力 (VL) が 0 のまま");
            Assert.IsTrue(pile.AxialForceLevel2s.Any(v => v != 0.0),
                "AxialForceLevel2s が写されていない");
        }

        [TestMethod]
        public void InputMode_UsesSeismicAxialForce()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, model, input) = built.Value;

            var loadCase = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(lc => lc.Level == 2);
            var pile = input.PileLayoutItems[0];

            double seismic = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
            double gravity = model.GetAxialForce(pile);

            // 前提: この例題では地震時軸力と常時軸力が違う (同じなら検証にならない)
            Assert.AreNotEqual(gravity, seismic, 1e-6,
                "例題の地震時軸力が常時軸力と同値のため、この検証は成立しない");

            vm.UseAnalysisAxialForce = false;
            Assert.AreEqual(seismic, vm.ResolveMPhiAxialForce(model, pile, loadCase), 1e-9,
                "「入力値」モードで地震時軸力が使われていない");
        }

        [TestMethod]
        public void AnalysisMode_UsesCurrentAnalysisAxialForce()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, model, input) = built.Value;

            var loadCase = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(lc => lc.Level == 2);
            var pile = input.PileLayoutItems[0];

            // 解析モードの GetAxialForce は UpdateAxialForceFromAnalysis が Fxi を加算した現在軸力。
            // 入力の地震時軸力を重ねると二重計上になるので、こちらを使うのが正。
            model.SetAxialForce(pile, 1234.5);

            vm.UseAnalysisAxialForce = true;
            Assert.AreEqual(1234.5, vm.ResolveMPhiAxialForce(model, pile, loadCase), 1e-9,
                "「入力値＋応力解析結果」モードで解析軸力が使われていない");
        }

        [TestMethod]
        public void InputMode_FallsBackToGravityWhenSeismicNotEntered()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (vm, model, input) = built.Value;

            var loadCase = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(lc => lc.Level == 2);
            var pile = input.PileLayoutItems[0];

            for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
                pile.AxialForceLevel2s[i] = 0.0;

            vm.UseAnalysisAxialForce = false;
            Assert.AreEqual(model.GetAxialForce(pile), vm.ResolveMPhiAxialForce(model, pile, loadCase), 1e-9,
                "地震時軸力が未入力のとき重力ベースへフォールバックしていない");
        }
    }
}
