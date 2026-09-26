using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析前の入力検査 (<see cref="CheckInputData"/>) が、壊れた入力で落ちずに名指しで止め、
    /// 注意のつもりの項目では止めないこと。
    /// </summary>
    [TestClass]
    public class InputCheckRobustnessTests
    {
        private static PileBodyInput Body(PileSection section, double length = 10.0)
            // 杭体の種別を先に入れる (区間を入れると杭体の種別が断面へ写される)
            => new() { PileBodyType = section.PileBodyType, PileBodySegments = [new PileBodySegment { SegmentLength = length, SegmentDepth = length, PileSection = section }] };

        private static GroundInput Ground()
            => new() { GroundLayers = [new GroundLayerInput { BottomAltitude = -30, LayerThickness = 30 }] };

        /// <summary>
        /// 杭が範囲の外の杭体番号・地盤番号を指していても、例外にせず、どの杭かを示して止めること。
        /// 以前は番号でそのまま配列を引いていたので、入力の問題を知らせる前に落ちた。
        /// </summary>
        [TestMethod]
        public void OutOfRangeReferencesAreNamedInsteadOfThrowing()
        {
            var input = new InputModel
            {
                PileBodies = [Body(new PileSection())],
                GroundsInput = [Ground()],
                PileLayoutItems =
                [
                    new PileLayoutDataItem { No = 7, PileBodyNo = 3, GroundNo = 1 },
                    new PileLayoutDataItem { No = 8, PileBodyNo = 1, GroundNo = 0 },
                ],
            };

            string message = CheckInputData.CheckSoilPile(input, "");
            StringAssert.Contains(message, "杭 No.7: 杭体番号 3 の杭体がありません");
            StringAssert.Contains(message, "杭 No.8: 地盤番号 0 の地盤がありません");
        }

        [TestMethod]
        public void EmbedmentNamesTheActualGroundNumber()
        {
            var input = new InputModel { GroundsInput = [Ground()] };
            input.EmbedmentInput = new EmbedmentInput();
            input.EmbedmentInput.GroundNo = 5;
            input.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem());
            input.EmbedmentInput.EmbedmentLayersCount = 1;

            string message = CheckInputData.CheckSoilEmbedment(input, "");
            StringAssert.Contains(message, "地盤番号5の地盤がありません", "範囲の外の地盤番号で落ちるか、名指ししていません");

            // 層数の欄と層の一覧が食い違っていても落ちず、食い違いを入力の誤りとして止めること (層数は別に持たれている)。
            // 以前は一覧が空なら黙って検査を終えていた
            input.EmbedmentInput.GroundNo = 1;
            input.EmbedmentInput.EmbedmentLayers.Clear();
            StringAssert.Contains(CheckInputData.CheckSoilEmbedment(input, ""), "根入部の層数 (1) と層の入力 (0 行) が合いません");

            string src = TestSource.Read("Graphics_r1", "Services", "CheckInputData.cs");
            Assert.IsFalse(src.Contains("地盤番号{\" + groundNo", StringComparison.Ordinal),
                "根入れのメッセージが地盤番号を「{2}」のように括弧付きで出します (文字列補間になっていません)");
        }

        /// <summary>寸法・強度の NaN・無限大も解析前に拒むこと (「0 以下」の比較は NaN を素通りさせる)。</summary>
        [TestMethod]
        public void NonFiniteGeometryIsRejected()
        {
            // 値は杭体に入れてから与える (区間を入れると杭体の種別が断面へ写され、寸法が既定値に戻る)
            var body = Body(new PileSection { PileBodyType = PileTypeNames.InsituRc });
            var section = body.PileBodySegments[0].PileSection;
            section.ConcreteOutDia = double.NaN;
            section.ConcreteFc = double.PositiveInfinity;
            var input = new InputModel
            {
                PileBodies = [body],
                GroundsInput = [new GroundInput { GroundLayers = [new GroundLayerInput { BottomAltitude = double.NaN, LayerThickness = double.NaN }] }],
            };

            string message = CheckInputData.CheckPileBodyGeometry(input, "");
            // 区間長は入れる側 (PileBodySegment.SegmentLength) が NaN・無限大を受け付けないので、ここでは見ない
            StringAssert.Contains(message, "コンクリート外径が 0 以下か数値ではありません");
            StringAssert.Contains(message, "Fc が 0 以下か数値ではありません");

            string ground = CheckInputData.CheckGroundLayerGeometry(input, "");
            StringAssert.Contains(ground, "層厚が 0 以下か数値ではありません");
            StringAssert.Contains(ground, "下端の標高が数値ではありません");
        }

        /// <summary>
        /// 節杭が最上段にあるのは「注意」。解析を止めるエラーには入れず、警告一覧に出すこと。
        /// 以前はエラーの一覧に入れていたので、注意のつもりで解析できなかった。
        /// </summary>
        [TestMethod]
        public void ANodularPileOnTopIsAWarningNotAnError()
        {
            var body = Body(new PileSection { PileBodyType = PileTypeNames.PrecastConcrete });
            var section = body.PileBodySegments[0].PileSection;
            section.PileSectionType = PileTypeNames.PhcNodular;
            Assert.IsTrue(section.IsNodularPile, "(前提) 節杭の断面であること");
            var input = new InputModel { PileBodies = [body] };

            string errors = CheckInputData.CheckPileBodyGeometry(input, "");
            Assert.IsFalse(errors.Contains("最上段", StringComparison.Ordinal), "節杭が最上段にあることで解析を止めています");

            var warnings = CheckInputData.CollectInputWarnings(input);
            Assert.IsTrue(warnings.Any(w => w.Contains("最上段の区間にあります", StringComparison.Ordinal)),
                "節杭が最上段にあることを警告に出していません");
        }

        // ── 根入部の各層の形 ─────────────────────────────

        private static InputModel WithEmbedment(params (double Top, double Bottom, double Thickness)[] layers)
        {
            var input = new InputModel { GroundsInput = [Ground()] };
            input.EmbedmentInput = new EmbedmentInput { GroundNo = 1 };
            foreach (var (top, bottom, thickness) in layers)
                input.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem
                {
                    TopAltitude = top, BottomAltitude = bottom, LayerThickness = thickness,
                });
            input.EmbedmentInput.EmbedmentLayersCount = layers.Length;
            return input;
        }

        [TestMethod]
        public void ConsistentEmbedmentLayersPass()
        {
            Assert.AreEqual("", CheckInputData.CheckSoilEmbedment(WithEmbedment((0, -2, 2), (-2, -6.5, 4.5)), ""));
        }

        /// <summary>
        /// 根入部の各層の厚さ・上下・隣の層とのつながりを解析前に見ること。以前は根入部全体の上端・下端と
        /// 地盤の範囲しか比べず、壊れた形のまま計算に進んだ。
        /// </summary>
        [TestMethod]
        public void BrokenEmbedmentLayersAreNamed()
        {
            string Check(params (double, double, double)[] layers) => CheckInputData.CheckSoilEmbedment(WithEmbedment(layers), "");

            StringAssert.Contains(Check((0, -2, 0), (-2, -6, 4)), "根入部 第1層: 層厚が 0 以下か数値ではありません");
            StringAssert.Contains(Check((0, -2, double.NaN)), "根入部 第1層: 層厚が 0 以下か数値ではありません");
            StringAssert.Contains(Check((double.NaN, -2, 2)), "根入部 第1層: 上端・下端の標高が数値ではありません");
            StringAssert.Contains(Check((-2, 0, 2)), "根入部 第1層: 上端 (-2.000 m) が下端 (0.000 m) より高くありません");
            StringAssert.Contains(Check((0, -2, 3)), "根入部 第1層: 上端と下端の差 (2.000 m) が層厚 (3.000 m) と合いません");
            StringAssert.Contains(Check((0, -2, 2), (-2.5, -6, 3.5)), "根入部 第1層と第2層の間に 0.500 m の隙間があります");
            StringAssert.Contains(Check((0, -2, 2), (-1.5, -6, 4.5)), "根入部 第1層と第2層が 0.500 m 重なっています");
            // 1 つ目の誤りで止めず、すべての層を見る
            string both = Check((0, -2, 0), (-2, -6, -1));
            StringAssert.Contains(both, "第1層: 層厚");
            StringAssert.Contains(both, "第2層: 層厚");
        }

        /// <summary>同梱の例題は、根入部の新しい検査を通ること (正しい入力を止めない)。</summary>
        [TestMethod]
        public void BundledExamplesPassTheEmbedmentCheck()
        {
            int checkedFiles = 0, withEmbedment = 0;
            foreach (var file in TestSource.ExampleFiles("PileExample*.json", 10))
            {
                string pileName = System.IO.Path.GetFileNameWithoutExtension(file);
                string groundName = "Example" + pileName["PileExample".Length..];
                if (TestSource.ExamplePath(groundName + ".json") == null) continue;
                var (input, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
                Assert.IsNotNull(input, $"{pileName}: {error}");
                checkedFiles++;
                if ((input.EmbedmentInput?.EmbedmentLayersCount ?? 0) > 0) withEmbedment++;
                Assert.AreEqual("", CheckInputData.CheckSoilEmbedment(input, ""), $"{pileName}: 根入部の検査が正しい例題を止めています");
            }
            TestSource.AssertScanned(checkedFiles, 10, "杭の例題");
            Assert.IsTrue(withEmbedment >= 1, "(前提) 根入部のある例題がありません");
        }
    }
}
