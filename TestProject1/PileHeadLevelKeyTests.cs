using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭頭Z を同じ高さとみなす規則 (2026-10-24 の指摘 3 件)。
    /// </summary>
    [TestClass]
    public class PileHeadLevelKeyTests
    {
        /// <summary>
        /// 許容差より近い杭頭Z は、丸めの境目をまたいでも同じ土層-杭セットになる。以前は Math.Round(z / 許容差) をキーにしていたので、
        /// -2.0000004 と -2.0000006 (差 0.2 µm) が別のキー (-2000000 と -2000001) になり、中身の同じセットが 2 つできた。
        /// </summary>
        [TestMethod]
        public void HeadsWithinTheTolerance_ShareOneSoilPileSet_EvenAcrossTheRoundingBoundary()
        {
            Assert.IsTrue(InputModel.SameLevel(-2.0000004, -2.0000006));
            Assert.IsFalse(InputModel.SameLevel(-2.0, -2.00001));
            CollectionAssert.AreEqual(new[] { 4.96, -3.3 }, InputModel.DistinctLevels([-3.3, 4.96, -3.2999999999, 4.96 + 1e-9]).Select(v => System.Math.Round(v, 6)).ToArray());

            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            var piles = input.PileLayoutItems.ToList();
            double dz = piles[0].PileHeadZ - piles[0].Z;
            piles[0].Z = -2.0000004 - dz;
            piles[1].Z = -2.0000006 - dz;
            for (int i = 2; i < piles.Count; i++) piles[i].Z = -2.0 - dz;
            input.GenerateSoilPiles();

            Assert.AreEqual(1, input.ElementDivision.SoilPiles.Count, "許容差より近い杭頭Z で、土層-杭セットが分かれています");
            Assert.IsTrue(piles.All(p => p.SoilPileAltNo == 1), "杭が同じセットを指していません");
            Assert.AreSame(input.ElementDivision.SoilPiles[0], input.LookupSoilPile(piles[1].GroundNo, piles[1].PileBodyNo, piles[1].PileHeadZ));

            foreach (var (folder, file) in new[]
            {
                ("Models/InputData", "InputModel.cs"), ("Models/Results", "SinglePileSettlementResult.cs"),
                ("ViewModels", "PileBodyViewModel.cs"), ("Output", "WordDocument.cs"),
            })
            {
                string code = TestSource.Read(new[] { "Graphics_r1" }.Concat(folder.Split('/')).Append(file).ToArray());
                Assert.IsFalse(code.Contains("COORDINATE_TOLERANCE)") && System.Text.RegularExpressions.Regex.IsMatch(code, @"Math\.Round\([^;]*COORDINATE_TOLERANCE\)"),
                    $"{file} が杭頭Z を丸めてキーにしています (InputModel.SameLevel を使う)");
            }
        }

        /// <summary>沈下ウィンドウの Masing 履歴の常時荷重の初期値は、どの杭の値かを示す (同じセットでも杭ごとに軸力が違う)。</summary>
        [TestMethod]
        public void TheMasingWindow_SaysWhichPileTheInitialLoadCameFrom()
        {
            string code = TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs");
            Assert.IsFalse(code.Contains(".FirstOrDefault(p => p.SoilPileAltNo == SoilPileNo)"), "最初の杭の軸力を黙って使っています");
            StringAssert.Contains(code, "初期値: 杭No.");
            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "MasingHysteresisWindow.xaml"), "{Binding ConstantLoadNote}");
        }

        /// <summary>DXF・3dm の通り心と寸法は、杭頭レベルごとに描く (以前は最も高いレベルだけで、低い段の杭から浮いた)。</summary>
        [TestMethod]
        public void TheCadGrid_IsDrawnAtEachPileHeadLevel()
        {
            foreach (var file in new[] { "DxfExporter.cs", "Rhino3dmExporter.cs" })
            {
                string code = TestSource.Read("Graphics_r1", "Output", file);
                StringAssert.Contains(code, "foreach (double z in PileHeadLevels())", $"{file} が杭頭レベルごとに通り心を描いていません");
                Assert.IsFalse(code.Contains("GetMaxPileTopZ"), $"{file} に杭頭の最高レベルだけを使う処理が残っています");
            }
        }
    }
}
