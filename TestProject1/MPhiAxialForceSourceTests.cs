using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// M-φ を構築する軸力まわりの検証。
    ///
    /// 仕様: ステップ毎の M-φ 再解決 (SetupMPhiByCurrentAxialForMiddleBeam) は
    /// <c>model.GetAxialForce(pile)</c> を使う。これは「常時軸力 VL 固定」ではなく、
    /// SetVectorDF が設定した <c>AxialForceIncrement = (N_seis - VL)/nStep</c> を
    /// UpdateF が毎ステップ加算した**荷重ステップ比例のランプ**で、
    /// 最終ステップでちょうど入力の地震時軸力に一致する。
    ///
    /// 2026-08-21 にこれを「VL 固定」と読み違えて常に N_seis を使う変更を入れ、revert した。
    /// 同じ読み違いを繰り返さないよう、ランプの意味をここで固定しておく。
    /// </summary>
    [TestClass]
    public class MPhiAxialForceSourceTests
    {
        private static (AnaModel model, InputModel input)? Build()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) return null;

            var modelling = new AnalysisModelling(inputModel);
            var model = new AnaModel(
                inputModel, modelling.Nodes, modelling.Beams, modelling.DummyBeams,
                modelling.RigidBodies, modelling.HorizontalSoilSprings, modelling.RotationalSprings);
            model.InitializeStates(); // AxialForce = AxialForceVL0 + AxialForceVLAdditional (常時 VL)

            return (model, inputModel);
        }

        /// <summary>
        /// 例題ビルダーが軸力を写していること。写し忘れると全杭 N=0 になり、
        /// 収束回帰テストが M-φ の軸力依存性を一切踏まなくなる
        /// (Example10 の 1200φ では Mcr が常時軸力時の 40% にしかならない別断面になる)。
        /// </summary>
        [TestMethod]
        public void ExampleBuilder_CarriesAxialForces()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (model, input) = built.Value;

            var pile = input.PileLayoutItems[0];
            Assert.AreNotEqual(0.0, pile.AxialForceVL0, "AxialForceVL0 が写されていない");
            Assert.AreNotEqual(0.0, model.GetAxialForce(pile), "常時軸力 (VL) が 0 のまま");
            Assert.IsTrue(pile.AxialForceLevel2s.Any(v => v != 0.0),
                "AxialForceLevel2s が写されていない");
        }

        /// <summary>
        /// ケース開始時点の <c>GetAxialForce</c> は常時軸力 VL であること。
        /// </summary>
        [TestMethod]
        public void AxialForce_StartsAtGravityBaseline()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (model, input) = built.Value;

            var pile = input.PileLayoutItems[0];
            Assert.AreEqual(pile.AxialForceVL0 + pile.AxialForceVLAdditional,
                model.GetAxialForce(pile), 1e-9,
                "ケース開始時の軸力が常時軸力 VL になっていない");
        }

        /// <summary>
        /// 軸力は荷重ステップに比例して VL から地震時軸力までランプし、
        /// 最終ステップでちょうど入力の地震時軸力に一致すること。
        /// (SetVectorDF が設定する増分を UpdateF が毎ステップ加算する仕組みの意味を固定する)
        /// </summary>
        [TestMethod]
        public void AxialForce_RampsFromGravityToSeismicOverLoadSteps()
        {
            var built = Build();
            if (built == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var (model, input) = built.Value;

            var loadCase = input.LoadCasesInput.AnalysisTargetSeismicLoadCases.First(lc => lc.Level == 2);
            var pile = input.PileLayoutItems[0];

            double vl = pile.AxialForceVL0 + pile.AxialForceVLAdditional;
            double seismic = pile.GetSeismicAxialForce(loadCase.No, loadCase.Level);
            Assert.AreNotEqual(vl, seismic, 1e-6,
                "この例題では地震時軸力と常時軸力が同値のため、ランプの検証が成立しない");

            const int nStep = 16;
            model.SetAxialForceIncrement(pile, (seismic - vl) / nStep);

            // UpdateF が毎ステップ行う加算と同じ操作
            for (int step = 0; step < nStep; step++)
            {
                model.SetAxialForce(pile, model.GetAxialForce(pile) + model.GetAxialForceIncrement(pile));

                double expected = vl + (step + 1) * (seismic - vl) / nStep;
                Assert.AreEqual(expected, model.GetAxialForce(pile), 1e-6,
                    $"step {step}: 軸力が荷重ステップに比例していない");
            }

            Assert.AreEqual(seismic, model.GetAxialForce(pile), 1e-6,
                "最終ステップで入力の地震時軸力に一致していない");
        }
    }
}
