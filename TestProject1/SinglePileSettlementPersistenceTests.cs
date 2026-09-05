using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;

namespace TestProject1
{
    /// <summary>
    /// 単杭沈下の荷重-沈下曲線は、保存ファイルに<b>1 回だけ</b>入ること。
    ///
    /// 曲線の置き場所 (<c>SoilPile.LoadDisplacements</c>) は変えていない。
    /// 基礎梁考慮沈下の杭頭ばねと水平解析の杭先端 P-S ばねが<b>次の解析の入力として</b>
    /// これを読むためで、結果側へ動かすと数値の出る経路に手が入る。
    ///
    /// 変えたのは保存の仕方だけ。以前は入力の一部として書き出していたので、
    /// 現在の入力と解析時のスナップショットの両方に同じ曲線が入っていた。
    /// </summary>
    [TestClass]
    public class SinglePileSettlementPersistenceTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        private static SoilPile MakeSoilPile(int groundNo, int pileBodyNo, double z)
        {
            var sp = new SoilPile();
            sp.Initialize(1, groundNo, new GroundInput(), pileBodyNo, new PileBodyInput(), z, []);
            return sp;
        }

        private static InputModel MakeInputWithCurve(double load = 1234.0, double disp = 5.5)
        {
            var sp = MakeSoilPile(1, 1, -2.5);
            sp.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = load, DD0s = disp });
            sp.LoadDisplacementsLimit.Add(new VerticalLoadTransferMethod.LoadDisplacement
            { PileTopLoad = load * 2, DD0s = disp * 4 });

            var input = new InputModel { ElementDivision = new ElementDivision() };
            input.ElementDivision.SoilPiles.Add(sp);
            return input;
        }

        [TestMethod]
        public void TheCurveIsWrittenOnlyOnce()
        {
            var live = MakeInputWithCurve();

            // 解析時のスナップショットも曲線を持っている状態 (本番と同じ形)
            var snapshot = MakeInputWithCurve();

            var data = new ProjectData
            {
                FormatVersion = 2,
                InputModel = live,
                ResultInputSnapshot = snapshot,
                SinglePileSettlementResult = SinglePileSettlementResult.Capture(live),
            };

            string json = JsonSerializer.Serialize(data, Options);

            int occurrences = CountOccurrences(json, "PileTopLoad");
            Assert.AreEqual(2, occurrences,
                $"曲線が保存ファイルに {occurrences} 回入っています "
                + "(常時と極限の 2 点ぶんだけが結果の節に入るのが正)");
        }

        [TestMethod]
        public void TheCurveComesBackOnLoad()
        {
            var live = MakeInputWithCurve();
            var data = new ProjectData
            {
                FormatVersion = 2,
                InputModel = live,
                SinglePileSettlementResult = SinglePileSettlementResult.Capture(live),
            };

            string json = JsonSerializer.Serialize(data, Options);
            var loaded = JsonSerializer.Deserialize<ProjectData>(json, Options)!;

            var sp = loaded.InputModel.ElementDivision.SoilPiles[0];
            Assert.AreEqual(0, sp.LoadDisplacements.Count, "前提: 入力の節には曲線が入っていない");

            loaded.SinglePileSettlementResult!.ApplyTo(loaded.InputModel);

            Assert.AreEqual(1, sp.LoadDisplacements.Count, "曲線が復元されていない");
            Assert.AreEqual(1234.0, sp.LoadDisplacements[0].PileTopLoad, 1e-9);
            Assert.AreEqual(5.5, sp.LoadDisplacements[0].DD0s, 1e-9);
            Assert.AreEqual(1, sp.LoadDisplacementsLimit.Count, "極限側が復元されていない");
        }

        /// <summary>
        /// 曲線が入力の中に入っている旧ファイルが、いまも開けること。
        /// 開いたあと保存し直しても二重にならないこと。
        /// </summary>
        [TestMethod]
        public void AnOldFileWithTheCurveInsideTheInputStillLoads()
        {
            string oldJson =
                "{\n" +
                "  \"$id\": \"1\",\n" +
                "  \"GroundNo\": 1,\n" +
                "  \"PileBodyNo\": 1,\n" +
                "  \"LoadDisplacements\": {\n" +
                "    \"$id\": \"2\",\n" +
                "    \"$values\": [ { \"$id\": \"3\", \"PileTopLoad\": 800.0, \"DD0s\": 3.25 } ]\n" +
                "  }\n" +
                "}";

            var raw = JsonSerializer.Deserialize<SoilPile>(oldJson, Options)!;
            Assert.AreEqual(1, raw.LegacyLoadDisplacements.Count, "旧ファイルの曲線が読めていない");

            // 移行と保存し直しは、初期化済みの土層-杭セットで見る
            // (計算プロパティが地盤を参照するので、素の SoilPile は直列化できない)
            var sp = MakeSoilPile(1, 1, -2.5);
            sp.LegacyLoadDisplacements = [.. raw.LegacyLoadDisplacements];

            sp.MigrateLegacyLoadDisplacements();

            Assert.AreEqual(1, sp.LoadDisplacements.Count, "曲線が本体へ移っていない");
            Assert.AreEqual(3.25, sp.LoadDisplacements[0].DD0s, 1e-9);
            Assert.AreEqual(0, sp.LegacyLoadDisplacements.Count,
                "受け取り口が空になっていない (保存し直すと二重に入る)");

            string resaved = JsonSerializer.Serialize(sp, Options);
            Assert.AreEqual(0, CountOccurrences(resaved, "PileTopLoad"),
                "保存し直すと入力の中に曲線が復活しています");
        }

        /// <summary>
        /// 曲線は (地盤番号, 杭体番号, Z) で対応付けること。順番ではない。
        /// </summary>
        [TestMethod]
        public void CurvesAreMatchedByTheSoilPileKey()
        {
            var a = MakeSoilPile(1, 1, -2.0);
            var b = MakeSoilPile(2, 1, -2.0);
            a.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 10, DD0s = 1 });
            b.LoadDisplacements.Add(new VerticalLoadTransferMethod.LoadDisplacement { PileTopLoad = 20, DD0s = 2 });

            var source = new InputModel { ElementDivision = new ElementDivision() };
            source.ElementDivision.SoilPiles.Add(a);
            source.ElementDivision.SoilPiles.Add(b);

            // 受け側は順番が逆
            var target = new InputModel { ElementDivision = new ElementDivision() };
            target.ElementDivision.SoilPiles.Add(MakeSoilPile(2, 1, -2.0));
            target.ElementDivision.SoilPiles.Add(MakeSoilPile(1, 1, -2.0));

            SinglePileSettlementResult.CopyCurves(source, target);

            Assert.AreEqual(20.0, target.ElementDivision.SoilPiles[0].LoadDisplacements[0].PileTopLoad, 1e-9,
                "順番で対応付けています (地盤番号が違う杭に曲線が付く)");
            Assert.AreEqual(10.0, target.ElementDivision.SoilPiles[1].LoadDisplacements[0].PileTopLoad, 1e-9);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int n = 0;
            for (int i = haystack.IndexOf(needle, System.StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, System.StringComparison.Ordinal))
                n++;
            return n;
        }
    }
}
