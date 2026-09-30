using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Types.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Output;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// DXF・3dm の書き出しの中身: 杭・基礎梁・通り心が、正しい位置・大きさ・レイヤー・単位で出ていること。
    ///
    /// <para>これまでの試験は「ファイルができた (前のファイルを差し替えた)」ことしか見ていなかった。座標はメートルで書くので、
    /// 杭径 (入力は mm) の換算や、ファイルに記録する単位を取り違えると、CAD で開いたときに 1000 倍ずれる。</para>
    /// </summary>
    [TestClass]
    public class CadExportContentTests
    {
        private static InputModel Example()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (input == null) Assert.Inconclusive($"例題ロード失敗: {error}");
            return input!;
        }

        private static string TempFile(string ext) => Path.Combine(Path.GetTempPath(), $"pd_cad_{Guid.NewGuid():N}{ext}");

        /// <summary>杭の径 [m] (杭体の先頭の区間の杭径。入力は mm)。</summary>
        private static double DiameterM(InputModel input, PileLayoutDataItem pile)
            => input.PileBodyAt(pile.PileBodyNo)!.PileBodySegments[0].PileSection.PileDiameter / 1000.0;

        /// <summary>平面図の DXF: 杭は杭の位置を中心とする円 (半径 = 杭径/2 [m])、単位はメートルと記録する。</summary>
        [TestMethod]
        public void PlanDxf_DrawsEveryPileAtItsPlaceAndSize_InMetres()
        {
            var input = Example();
            string path = TempFile(".dxf");
            try
            {
                new DxfPlanExporter(input).Export(path);
                var doc = DxfReader.Read(path);

                Assert.AreEqual(UnitsType.Meters, doc.Header.InsUnits, "DXF に単位 (メートル) が記録されていません (CAD が単位を決められない)");

                var circles = doc.Entities.OfType<Circle>().Where(c => c.Layer.Name == "杭").ToList();
                Assert.IsTrue(circles.Count >= input.PileLayoutItems.Count, $"杭の円が足りません ({circles.Count} / 杭 {input.PileLayoutItems.Count} 本)");
                foreach (var pile in input.PileLayoutItems)
                {
                    double r = DiameterM(input, pile) / 2;
                    bool found = circles.Any(c => Math.Abs(c.Center.X - pile.X) < 1e-6 && Math.Abs(c.Center.Y - pile.Y) < 1e-6
                                                  && Math.Abs(c.Radius - r) < 1e-6);
                    Assert.IsTrue(found, $"杭 No.{pile.PileNo} の円 (中心 {pile.X}, {pile.Y}、半径 {r} m) がありません");
                }

                var layers = doc.Layers.Select(l => l.Name).ToHashSet();
                foreach (var name in new[] { "杭", "杭番号", "通り心", "通り心シンボル", "寸法", "基礎梁" })
                    Assert.IsTrue(layers.Contains(name), $"レイヤー「{name}」がありません");
                int gridLines = doc.Entities.OfType<Line>().Count(l => l.Layer.Name == "通り心");
                Assert.AreEqual((input.GridXItems?.Count ?? 0) + (input.GridYItems?.Count ?? 0), gridLines, "通り心の線の数");
            }
            finally { File.Delete(path); }
        }

        /// <summary>立体の DXF: 単位はメートルと記録し、杭の面は杭の位置から杭径/2 の距離にある。</summary>
        [TestMethod]
        public void SolidDxf_RecordsMetres_AndPilesAreAtTheirPlace()
        {
            var input = Example();
            string path = TempFile(".dxf");
            try
            {
                new DxfExporter(input).Export(path);
                var doc = DxfReader.Read(path);
                Assert.AreEqual(UnitsType.Meters, doc.Header.InsUnits, "DXF に単位 (メートル) が記録されていません");

                var faces = doc.Entities.OfType<Face3D>().Where(f => f.Layer.Name == "杭").ToList();
                Assert.IsTrue(faces.Count > 0, "杭の面がありません");
                // 杭ごとに、その杭の位置から杭径/2 の距離にある頂点が見つかること (杭の円筒の側面)
                foreach (var pile in input.PileLayoutItems)
                {
                    double r = DiameterM(input, pile) / 2;
                    bool found = faces.Any(f => Math.Abs(Math.Sqrt(Math.Pow(f.FirstCorner.X - pile.Point3D.X, 2) + Math.Pow(f.FirstCorner.Y - pile.Point3D.Y, 2)) - r) < 1e-6);
                    Assert.IsTrue(found, $"杭 No.{pile.PileNo} の側面 (半径 {r} m) がありません");
                }
            }
            finally { File.Delete(path); }
        }

        /// <summary>3dm: モデルの単位はメートル (座標をメートルで書いている)。単位を書かないと Rhino はミリとして開き、1000 倍小さくなる。</summary>
        [TestMethod]
        public void Rhino3dm_RecordsMetres_AndHasThePileLayer()
        {
            var input = Example();
            string path = TempFile(".3dm");
            try
            {
                try { new Rhino3dmExporter(input).Export(path); }
                catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or BadImageFormatException)
                {
                    Assert.Inconclusive("rhino3dm のネイティブライブラリがありません: " + ex.Message);
                    return;
                }
                var file = Rhino.FileIO.File3dm.Read(path);
                Assert.IsNotNull(file, "3dm を読み戻せません");
                Assert.AreEqual(Rhino.UnitSystem.Meters, file.Settings.ModelUnitSystem, "3dm のモデルの単位がメートルではありません");
                var layers = file.AllLayers.Select(l => l.Name).ToList();
                foreach (var name in new[] { "杭", "根入れ部", "基礎梁", "通り心", "寸法" })
                    CollectionAssert.Contains(layers, name, $"レイヤー「{name}」がありません");
                int pileLayer = file.AllLayers.First(l => l.Name == "杭").Index;
                Assert.IsTrue(file.Objects.Count(o => o.Attributes.LayerIndex == pileLayer) >= input.PileLayoutItems.Count, "杭の立体が足りません");
            }
            finally { File.Delete(path); }
        }
    }
}
