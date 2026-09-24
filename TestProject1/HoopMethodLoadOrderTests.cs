using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// せん断補強筋の工法・呼び名・規格・算定式は、保存データの中の並び順によらず読めること。
    ///
    /// setter は「いまの工法で選べない値」を拒否する。保存データで算定式や呼び名が工法より先に来ると、
    /// その時点ではまだ工法が「標準」なので、正しい値も選べない値に見える。読み込みの最中は setter の検査を止め、
    /// 読み終わりに工法と一組で整える (<c>PileSection._isDeserializing</c> / <c>SyncHoopMethodConsistency</c>)。
    /// その仕組みが System.Text.Json と Newtonsoft の両方で効いていることを、並びを入れ替えた実際の JSON で確かめる。
    /// </summary>
    [TestClass]
    public class HoopMethodLoadOrderTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>エムケーパイルリング785 + (3.3) 式 + MD13 の断面を保存し、工法に関わる項目を先頭へ、工法を末尾へ並べ替えた JSON。</summary>
        private static string ReorderedJson()
        {
            var section = new PileSection
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
                ConcreteOutDia = 1200,
                PileDiameter = 1200,
                ConcreteFc = 30,
                HoopMethod = ShearReinforcementMethods.MkPileRing785,
                HoopSize = "MD13",
                HoopDamageFormula = ShearReinforcementMethods.DamageFormulaSafetyShortTerm,
            };
            var original = JsonNode.Parse(JsonSerializer.Serialize(section, Options))!.AsObject();

            string[] first = [nameof(PileSection.HoopDamageFormula), nameof(PileSection.HoopUltimateFormula),
                              nameof(PileSection.HoopSize), nameof(PileSection.HoopSpec)];
            var reordered = new JsonObject();
            // 参照の目印 ($id など) は先頭に置く決まりなので動かさない
            foreach (var (key, value) in original.Where(p => p.Key.StartsWith('$')))
                reordered[key] = value?.DeepClone();
            foreach (string key in first.Where(original.ContainsKey))
                reordered[key] = original[key]!.DeepClone();
            foreach (var (key, value) in original.Where(p => !p.Key.StartsWith('$') && !first.Contains(p.Key) && p.Key != nameof(PileSection.HoopMethod)))
                reordered[key] = value?.DeepClone();
            reordered[nameof(PileSection.HoopMethod)] = original[nameof(PileSection.HoopMethod)]!.DeepClone();

            string json = reordered.ToJsonString();
            int methodAt = json.IndexOf("\"HoopMethod\"", System.StringComparison.Ordinal);
            Assert.IsTrue(json.IndexOf("\"HoopDamageFormula\"", System.StringComparison.Ordinal) < methodAt
                          && json.IndexOf("\"HoopSize\"", System.StringComparison.Ordinal) < methodAt,
                "前提: 算定式と呼び名が工法より前に並んでいること");
            return json;
        }

        private static void AssertKept(PileSection loaded)
        {
            Assert.AreEqual(ShearReinforcementMethods.MkPileRing785, loaded.HoopMethod, "工法が読めていません");
            Assert.AreEqual("MD13", loaded.HoopSize, "工法より前にあった呼び名が捨てられました");
            Assert.AreEqual("MK785", loaded.HoopSpec, "規格が工法に合っていません");
            Assert.AreEqual(ShearReinforcementMethods.DamageFormulaSafetyShortTerm, loaded.HoopDamageFormula,
                "工法より前にあった算定式が捨てられました (既定の式に戻っています)");
        }

        [TestMethod]
        public void SystemTextJson_ReadsFormulaAndSizeBeforeMethod()
        {
            var loaded = JsonSerializer.Deserialize<PileSection>(ReorderedJson(), Options);
            Assert.IsNotNull(loaded);
            AssertKept(loaded);
        }

        [TestMethod]
        public void Newtonsoft_ReadsFormulaAndSizeBeforeMethod()
        {
            var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<PileSection>(ReorderedJson());
            Assert.IsNotNull(loaded);
            AssertKept(loaded);
        }
    }
}
