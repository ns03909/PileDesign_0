using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TestProject1
{
    /// <summary>
    /// 利用者の声 (2026-11-07): 単杭の解析モデル図 (拘束・強制変位・設計用の荷重) が既定で欲しい。
    /// 計算書の「解析モデルの境界条件」の表と、水平抵抗解析杭モデルの図の記号。
    /// どちらも解析のモデル化と同じ規則を読む (設定を変えたら表と図も変わる)。
    /// </summary>
    [TestClass]
    public class PileModelBoundaryTests
    {
        private static InputModel LoadExample()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example3_1", "PileExample3_1");
            if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
            input!.FundamentalInput ??= new FundamentalInput();
            return input;
        }

        private static PileModelBoundaryRow Row(List<PileModelBoundaryRow> rows, string position)
            => rows.Single(r => r.Position == position);

        [TestMethod]
        public void TheTableFollowsThePileTipSetting()
        {
            var input = LoadExample();
            var soilPiles = input.ElementDivision.SoilPiles.ToList();
            TestSource.AssertScanned(soilPiles.Count, 1, "土層-杭セット");

            input.UsePsSpringAtPileTip = false;
            var rows = WordDocument.PileModelBoundaryRows(input, soilPiles);
            Assert.AreEqual("鉛直 (Dz) を固定", Row(rows, "杭先端").Restraint);
            StringAssert.StartsWith(Row(rows, "杭頭").Action, "外力なし");
            Assert.IsTrue(WordDocument.PileModelAnnotationFor(input, soilPiles[0]).TipVerticalFixed);

            // P-S ばねを選んでも、沈下解析の履歴が無ければ解析は杭先端を固定する。表も同じに書く
            input.UsePsSpringAtPileTip = true;
            foreach (var sp in soilPiles) { sp.NodeDisplacements = null; sp.NodeReactions = null; }
            rows = WordDocument.PileModelBoundaryRows(input, soilPiles);
            Assert.AreEqual("鉛直 (Dz) を固定", Row(rows, "杭先端").Restraint, "解析は固定するのに、ばねと書いています");
            StringAssert.StartsWith(Row(rows, "杭頭").Action, "杭の軸力 N を外力として");

            foreach (var sp in soilPiles)
            {
                sp.NodeDisplacements = [MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(2)];
                sp.NodeReactions = [MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(2)];
            }
            rows = WordDocument.PileModelBoundaryRows(input, soilPiles);
            StringAssert.Contains(Row(rows, "杭先端").Restraint, "鉛直地盤ばね");
            Assert.IsFalse(WordDocument.PileModelAnnotationFor(input, soilPiles[0]).TipVerticalFixed);
        }

        [TestMethod]
        public void TheFoundationRowFollowsThePileLayout()
        {
            var input = LoadExample();
            var soilPiles = input.ElementDivision.SoilPiles.ToList();
            bool rigidFloor = input.FoundationBeamInput?.ConnectionMode == FoundationBeamConnectionMode.RigidFloor
                              && input.FoundationBeamInput.Beams is { Count: > 0 };
            if (rigidFloor) Assert.Inconclusive("(前提) 例題が剛体連結ではありません");
            TestSource.AssertScanned(input.PileLayoutItems.Count, 2, "杭");

            // 杭を一列に並べると、解析は基礎の回転を固定する
            double x0 = input.PileLayoutItems[0].X;
            foreach (var p in input.PileLayoutItems) p.X = x0;
            var rows = WordDocument.PileModelBoundaryRows(input, soilPiles);
            StringAssert.StartsWith(Row(rows, "基礎（慣性力中心）").Restraint, "回転 (Rx・Ry・Rz) を固定");
            StringAssert.Contains(Row(rows, "基礎（慣性力中心）").Action, "βU·Ps + βL·Pf");

            // 二方向に広げると拘束なし。ねじれを拘束すれば Rz だけ
            for (int i = 0; i < input.PileLayoutItems.Count; i++)
            {
                input.PileLayoutItems[i].X = x0 + i;
                input.PileLayoutItems[i].Y = i % 2;
            }
            input.RestrainFoundationTorsion = false;
            Assert.AreEqual("拘束なし", Row(WordDocument.PileModelBoundaryRows(input, soilPiles), "基礎（慣性力中心）").Restraint);
            input.RestrainFoundationTorsion = true;
            StringAssert.StartsWith(Row(WordDocument.PileModelBoundaryRows(input, soilPiles), "基礎（慣性力中心）").Restraint, "ねじれ (Rz) を固定");
        }

        [TestMethod]
        public void TheTableNamesEveryPositionOfThePileModel()
        {
            var input = LoadExample();
            var rows = WordDocument.PileModelBoundaryRows(input, input.ElementDivision.SoilPiles.ToList());
            foreach (var position in new[] { "基礎（慣性力中心）", "杭頭", "杭の節点", "水平地盤ばねの外側の節点", "杭先端" })
                Assert.IsTrue(rows.Any(r => r.Position == position), $"{position} の行がありません");
            StringAssert.Contains(Row(rows, "水平地盤ばねの外側の節点").Action, "地盤変位 × αL");
            Assert.IsTrue(rows.All(r => r.Restraint.Length > 0 && r.Action.Length > 0), "空の欄があります");
        }

        /// <summary>杭頭の回転の支え方は、解析 (SetupNonlinearMThetaForLoadCase) と同じ分け方。</summary>
        [TestMethod]
        public void PileHeadRotationFollowsTheAnalysisRule()
        {
            PileBodyInput Body(string top, string type) => new() { PileTopType = top, PileBodyType = type };

            var rc = Body("鉄筋定着工法", PileTypeNames.InsituRc);
            Assert.IsTrue(WordDocument.PileHeadRotation(rc, considerHeadRotation: true, anyNonlinearCase: false).Spring,
                "場所打ちRC杭は杭の線形・非線形によらず、基本設定に従って M-θ");
            Assert.IsFalse(WordDocument.PileHeadRotation(rc, considerHeadRotation: false, anyNonlinearCase: true).Spring);

            var steel = Body("鉄筋定着工法", PileTypeNames.SteelPipe);
            StringAssert.Contains(WordDocument.PileHeadRotation(steel, true, false).Text, "Kθ");

            var captain = Body("キャプテンパイル工法", PileTypeNames.PrecastConcrete);
            Assert.IsTrue(WordDocument.PileHeadRotation(captain, false, anyNonlinearCase: true).Spring,
                "半剛接合の工法は、杭を非線形とする荷重ケースで M-θ");
            Assert.IsFalse(WordDocument.PileHeadRotation(captain, true, anyNonlinearCase: false).Spring);

            Assert.IsFalse(WordDocument.PileHeadRotation(Body("埋込み方式", PileTypeNames.PrecastConcrete), true, true).Spring);
            Assert.IsFalse(WordDocument.PileHeadRotation(null, true, true).Spring);
        }

        /// <summary>
        /// 記号を描き足した図: 荷重の矢印 (赤) と強制変位の矢じりが載り、記号なしの図 (沈下・従来の呼び方) は変わらない。
        /// 目で確かめられるよう、図を TestResults/report-sample/ に残す。
        /// </summary>
        [TestMethod]
        [Timeout(180000)]
        public void TheAnnotatedFigureCarriesTheLoadsAndTheForcedDisplacement()
        {
            var style = new PileDiagramStyle("游ゴシック", 8.0, 7.0, 1.2, 0.7, 24, 8.0);
            var soilPile = PileModelDiagramTests.BuildSoilPile();

            var ex = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                byte[] plain = DiagramRenderer.RenderPileForceElevationPngBytes(soilPile, "horizontal", style, 150, 190, 96, 2);
                byte[] annotated = DiagramRenderer.RenderPileForceElevationPngBytes(soilPile, "horizontal", style, 150, 190, 96, 2,
                    new PileModelAnnotation(HeadRotationSpring: false, TipVerticalFixed: true, AxialForceAsLoad: false));
                byte[] vertical = DiagramRenderer.RenderPileForceElevationPngBytes(soilPile, "vertical", style, 150, 190, 96, 2);
                byte[] verticalWithAnnotation = DiagramRenderer.RenderPileForceElevationPngBytes(soilPile, "vertical", style, 150, 190, 96, 2,
                    new PileModelAnnotation(true, true, true));

                Assert.IsTrue(plain.Length > 0 && annotated.Length > 0, "描画に失敗しています");
                Assert.AreEqual(0, CountPixels(plain, IsLoadRed), "記号を頼んでいない図に荷重の矢印があります");
                Assert.IsTrue(CountPixels(annotated, IsLoadRed) > 200, "荷重の矢印 (H・N) がありません");
                Assert.IsTrue(CountPixels(annotated, IsForcedDispOrange) > 50, "強制変位の矢じりがありません");
                CollectionAssert.AreEqual(vertical, verticalWithAnnotation, "沈下の杭モデルには記号を描きません");

                string dir = Path.Combine(TestSource.Dir("TestProject1"), "TestResults", "report-sample");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "pile-model-annotated.png"), annotated);
            }, out bool timedOut, 120);

            if (timedOut) Assert.Inconclusive("STA スレッドがタイムアウトしました");
            if (ex != null) throw ex;
        }

        private static bool IsLoadRed(byte r, byte g, byte b) => r > 150 && g < 60 && b < 60;              // Firebrick (B2,22,22)
        private static bool IsForcedDispOrange(byte r, byte g, byte b) => r > 170 && g is > 60 and < 110 && b < 60; // (C0,50,20)

        private static int CountPixels(byte[] png, Func<byte, byte, byte, bool> match)
        {
            var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var buf = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(buf, stride, 0);
            int n = 0;
            for (int i = 0; i < buf.Length; i += 4)
                if (match(buf[i + 2], buf[i + 1], buf[i])) n++;
            return n;
        }
    }
}
