using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 2026-10-08 の網羅的な見直しで、利用者の了解を得て直した「解析の数値が変わる」不具合の再発防止。
    /// </summary>
    [TestClass]
    public class AuditNumericFixes20261008Tests
    {
        /// <summary>
        /// 1. 慣性力の作用点は、荷重ケースごとの慣性力中心に置く。
        /// 計算モデルは作用点を 1 つ (レベル1 の 1 番目のケースの位置) で作るので、ケースのモデルで動かし、剛体連結の腕も作り直す。
        /// </summary>
        [TestMethod]
        public void TheActionPoint_MovesToEachCasesInertiaCenter()
        {
            var built = AnalysisInputScopeTests.BuildAnalyzed();
            if (built == null) { Assert.Inconclusive("例題を組めません"); return; }
            var model = built.Value.vm.CurrentModel!.DeepCopy();
            var action = model.Nodes.First(n => n.Name == "ActionPoint");
            var rb = model.RigidBodies[0];
            var slave = rb.SlaveNodes.First();
            double x = action.Coord.X + 1.5, y = action.Coord.Y - 0.5, z = action.Coord.Z + 2.0;

            Assert.IsFalse(model.RelocateActionPoint(action.Coord.X, action.Coord.Y, action.Coord.Z), "同じ位置なのに作り直しています");
            Assert.IsTrue(model.RelocateActionPoint(x, y, z));
            Assert.AreEqual(x, action.Coord.X, 1e-12);
            Assert.AreEqual(z, action.Coord.Z, 1e-12);
            Assert.AreEqual(slave.Coord.X - x, slave.SlaveArm.X, 1e-9, "剛体連結の腕が、動かした作用点に合わせて作り直されていません");
            Assert.AreEqual(slave.Coord.Z - z, slave.SlaveArm.Z, 1e-9);

            string run = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.Run.cs");
            StringAssert.Contains(run, "caseModel.RelocateActionPoint(loadCase.ForceActionPointX, loadCase.ForceActionPointY, loadCase.ForceActionPointAltitude)",
                "ケースのモデルで作用点を慣性力中心に動かしていません");
        }

        /// <summary>
        /// 2. 鋼管杭の「鋼管部」(コンクリートを充填しない区間) は、残った充填コンクリートの径を断面の量に入れない。
        /// 3. 「コンクリート充填鋼管部」の解析の M-φ は、杭断面ウィンドウと同じ杭頭 M-φ (SteelPipeSection) を使う。
        /// </summary>
        [TestMethod]
        public void SteelPipeSections_UseOnlyWhatTheyHave()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example3_5", "PileExample3_5");
            if (input == null) { Assert.Inconclusive(error); return; }
            var sections = input.PileBodies.SelectMany(b => b.PileBodySegments).Select(s => s.PileSection).Where(s => s != null).ToList();

            var pipeOnly = sections.FirstOrDefault(s => s!.PileBodyType == PileTypeNames.SteelPipe && s.PileSectionType != PileTypeNames.CftSection);
            Assert.IsNotNull(pipeOnly, "前提: 例題 3.5 に鋼管部の区間がある");
            Assert.IsFalse(pipeOnly.HasConcreteCore);
            Assert.AreEqual(0.0, pipeOnly.Ac, "鋼管部に存在しないコンクリートの断面積が入っています");
            Assert.AreEqual(pipeOnly.PipeEs * pipeOnly.PipeAsCorroded * 0.001, pipeOnly.EA, 1e-6, "鋼管部の EA に鋼管以外が入っています");
            double pipeW = pipeOnly.PipeAs * PileDesign.Constants.UnitConversion.STEEL_UNIT_WEIGHT * 1e-6;
            Assert.AreEqual(pipeW, pipeOnly.W, 1e-9, "鋼管部の自重に鋼管以外が入っています");

            var cft = sections.FirstOrDefault(s => s!.PileBodyType == PileTypeNames.SteelPipe && s.PileSectionType == PileTypeNames.CftSection);
            Assert.IsNotNull(cft, "前提: 例題 3.5 にコンクリート充填鋼管部の区間がある");
            Assert.IsTrue(cft.HasConcreteCore);
            bool fiber = ConcreteModelOptions.UseFiberMPhi;
            try
            {
                ConcreteModelOptions.UseFiberMPhi = false;
                double nKn = 1000;
                var moments = cft.GetMPhiRelationship(nKn).Moments;
                var head = cft.TryCreateSteelPipeSection()!.GetMPhiRelationshipHead(nKn * 1000);
                Assert.AreEqual(head.moments.Max() * PileDesign.Constants.UnitConversion.NMM_TO_KNM, moments.Max(), 1e-6 * Math.Abs(moments.Max()),
                    "解析の M-φ が、杭断面ウィンドウの杭頭 M-φ と別の式です");
            }
            finally { ConcreteModelOptions.UseFiberMPhi = fiber; }
        }

        /// <summary>4. 追加実行で「破棄して新規実行」に切り替えたら、計算モデルも作り直す (境界条件はモデルを作るときにしか読まれない)。</summary>
        [TestMethod]
        public void SwitchingToAFreshRun_RebuildsTheModel()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "HorizontalCalculationViewModel.cs");
            int sw = src.IndexOf("additive = false;");
            Assert.IsTrue(sw > 0);
            int rebuild = src.IndexOf("TryCreateAnalysisModel()", sw);
            int next = src.IndexOf("// プリフライト", sw);
            Assert.IsTrue(rebuild > sw && rebuild < next, "新規実行に切り替えたあと、計算モデルを作り直していません");
        }

        /// <summary>5. 粘性土で細粒分含有率が 0 の土質点があれば、地盤の OK で知らせる (液状化層として扱われるため)。</summary>
        [TestMethod]
        public void ClayWithZeroFc_IsReportedOnOk()
        {
            var ground = new GroundInput();
            ground.GroundMassesData.Clear();
            ground.GroundMassesData.Add(new GroundMassDataInput { GLDepth = -2.0, GranularityClass = "粘性土", Fc = 0 });
            ground.GroundMassesData.Add(new GroundMassDataInput { GLDepth = -3.0, GranularityClass = "粘性土", Fc = 80 });
            ground.GroundMassesData.Add(new GroundMassDataInput { GLDepth = -4.0, GranularityClass = "砂質土", Fc = 0 });
            string? message = GroundLayerViewModel.DescribeClayWithZeroFc([ground]);
            Assert.IsNotNull(message, "粘性土で Fc が 0 の土質点を知らせていません");
            StringAssert.Contains(message, "地盤番号1: 1 点");
            StringAssert.Contains(message, "GL-2.00 m");
            Assert.IsFalse(message.Contains("GL-4.00"), "砂質土まで挙げています");

            ground.GroundMassesData[0].Fc = 90;
            Assert.IsNull(GroundLayerViewModel.DescribeClayWithZeroFc([ground]));
            StringAssert.Contains(TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs"), "if (DescribeClayWithZeroFc(GroundsInput) is { } zeroFc)");
        }

        /// <summary>6. 「C=10+2Z」ボタンは、ラベルどおり Cu = 10 + 2Z (Z は層中央の深さ) を入れる (以前は 10 + H/2 だけ大きかった)。</summary>
        [TestMethod]
        public void TheCohesionButton_FollowsItsLabel()
        {
            Assert.AreEqual(14.0, GroundLayerViewModel.CohesionAlluvialClay(-4.0, 4.0), 1e-12, "GL0〜−4 m の層 (中央 2 m) で 10 + 2×2 = 14 になりません");
            Assert.AreEqual(30.0, GroundLayerViewModel.CohesionAlluvialClay(-12.0, 4.0), 1e-12, "GL−8〜−12 m の層 (中央 10 m) で 30 になりません");
        }
    }
}
