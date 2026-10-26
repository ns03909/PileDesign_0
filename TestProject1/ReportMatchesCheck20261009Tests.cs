using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;

namespace TestProject1
{
    /// <summary>
    /// 計算書・画面の図が、検定と同じ前提で描かれること (2026-10-09)。
    /// 図と検定の前提が違うと、図では線の内側なのに検定は NG (またはその逆) になり、読み手が迷う。
    /// </summary>
    [TestClass]
    public class ReportMatchesCheck20261009Tests
    {
        /// <summary>N-M・N-Q 図の点の軸力は検定の設計軸力。地震時軸力が未入力 (0) なら常時軸力 VL (以前は図だけ N = 0)。</summary>
        [TestMethod]
        public void PlotAxialForce_IsTheCheckDesignAxialForce()
        {
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, AxialForceVL0 = 1200.0 };
            pile.AxialForceLevel1s = [0.0, 0.0, 0.0, 0.0];
            var lc = new LoadCase { No = 1, Level = 1 };
            Assert.AreEqual(pile.GetDesignAxialForce(1, 1), WordDocument.CheckAxialForce(null, pile, lc, null), 1e-9);
            Assert.AreEqual(pile.AxialForceVL, WordDocument.CheckAxialForce(null, pile, lc, null), 1e-9, "未入力の地震時軸力で常時軸力を使っていません");

            pile.AxialForceLevel1s[0] = 1500.0;
            Assert.AreEqual(1500.0, WordDocument.CheckAxialForce(null, pile, lc, null), 1e-9);

            // 欄が無いケースでも出力を止めない (常時軸力に落とす)
            Assert.AreEqual(pile.AxialForceVL, WordDocument.CheckAxialForce(null, pile, new LoadCase { No = 9, Level = 1 }, null), 1e-9);

            string graph = TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.cs");
            Assert.IsFalse(graph.Contains("var axialForce = pileLayoutDataItem.GetSeismicAxialForce("),
                "画面のグラフの点がまだ地震時軸力をそのまま使っています");
        }

        /// <summary>
        /// 検定の軸力は解析と同じ規則。入力値モードは設計軸力、入力値＋応力解析結果モードは杭頭の要素の軸力 (−Fxi) を足し、
        /// P-S ばねのときは解析が外力として与えた地震時軸力を引く (Fxi に含まれているため)。
        /// </summary>
        [TestMethod]
        public void CheckAxialForce_FollowsTheAnalysisAxialMode()
        {
            var input = new InputModel();
            var pile = new PileLayoutDataItem { No = 1, PileNo = 1, AxialForceVL0 = 1000.0 };
            pile.AxialForceLevel1s = [1500.0, 0.0, 0.0, 0.0];
            double fxi = -300.0;   // 圧縮 300 kN

            input.UseAnalysisAxialForce = false;
            Assert.AreEqual(1500.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 1, 1, null, fxi).N, 1e-9,
                "入力値モードで解析の軸力を足しています");

            input.UseAnalysisAxialForce = true;
            Assert.AreEqual(1800.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 1, 1, null, fxi).N, 1e-9,
                "入力値＋応力解析結果モードで解析の軸力を足していません");
            Assert.AreEqual(1300.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 2, 1, null, fxi).N, 1e-9,
                "未入力の地震時軸力は常時軸力 VL に解析の軸力を足します");

            input.UsePsSpringAtPileTip = true;
            Assert.AreEqual(300.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 1, 1, null, fxi).N, 1e-9,
                "P-S ばねでは地震時軸力が Fxi に含まれているので二重に数えています");
            Assert.AreEqual(1300.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 2, 1, null, fxi).N, 1e-9,
                "P-S ばねで地震時軸力が未入力なら、外力は与えていないので VL に解析の軸力を足します");

            Assert.AreEqual(1500.0, PileDesign.ViewModels.EvaluationService.ResolveCheckAxialForce(input, pile, 1, 1, null, null).N, 1e-9,
                "杭頭の要素の結果が無ければ入力値の軸力");
        }

        /// <summary>応力ダイアグラムのせん断の限界線は、検定と同じ M/(Q·d) と設計用せん断力の割増で描く。</summary>
        [TestMethod]
        public void StressDiagramShearLimit_UsesTheCheckMonQdAndMagnification()
        {
            string charts = TestSource.Read("Graphics_r1", "Output", "WordDocument.Charts.cs");
            StringAssert.Contains(charts, "EvaluationService.ResolveMonQd(");
            StringAssert.Contains(charts, "ShearDesignMagnification");
            Assert.IsFalse(charts.Contains("GetQNCurvesForLevel(lc.Level == 1 ? 1 : 2);"), "既定の M/(Q·d) で描く古い呼び出しが残っています");
        }

        /// <summary>
        /// 諸元表の「腐食考慮」の重量は、公称の重量と鋼管の断面積だけが違う (以前は主筋を MainBarAg で数え、
        /// 鋼管部のようにコンクリートの無い区間でも残った主筋の重量を足していた)。
        /// </summary>
        [TestMethod]
        public void CorrodedWeight_DiffersFromNominalOnlyByThePipe()
        {
            var steel = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.SteelPipe,
                PileSectionType = PileDesign.Constants.PileTypeNames.SteelPipeSection,
                PipeDia = 800, PipeTs = 12, CorrosionDepth = 1,
            };
            steel.MainBarNum = 16;   // 鋼管部では使わない主筋 (以前は腐食考慮の重量にだけ入った)
            double expected = (steel.PipeAs - steel.PipeAsCorroded) * PileDesign.Constants.UnitConversion.STEEL_UNIT_WEIGHT * 1e-6;
            Assert.AreEqual(expected, steel.W - steel.WCorroded, 1e-9);
        }

        /// <summary>計算書は、作っている間だけ解析したときの入力の材料モデルのオプションを使い、終わったら今の入力のものへ戻す。</summary>
        [TestMethod]
        public void Report_UsesTheAnalysedInputOptions_AndRestoresAfterwards()
        {
            string fileIo = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs");
            int apply = fileIo.IndexOf("SyncStaticOptionsFor(inputForReport ?? CurrentInputModel);", System.StringComparison.Ordinal);
            int run = fileIo.IndexOf("RunCancellableReport(run => doc.CreateWordDocument(", System.StringComparison.Ordinal);
            int restore = fileIo.IndexOf("SyncStaticOptionsFor(CurrentInputModel);", run, System.StringComparison.Ordinal);
            Assert.IsTrue(apply > 0 && run > apply && restore > run, "計算書の作成を解析時のオプションで挟んでいません");
        }
    }
}
