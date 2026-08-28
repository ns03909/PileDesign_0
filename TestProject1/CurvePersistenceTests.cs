using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1
{
    /// <summary>
    /// M-θ 曲線が保存・復元できること。
    ///
    /// 曲線の点は <c>List&lt;(double Theta, double Moment)&gt;</c> で持っていた。
    /// ValueTuple の中身はプロパティではなく<b>フィールド</b>で、
    /// System.Text.Json は既定でフィールドを直列化しない。
    /// そのため保存すると <c>[{}, {}]</c> という空オブジェクトの羅列になり、
    /// 読み戻すと<b>点が 1 つも復元されない</b>。例外も警告も出ないので、
    /// 開き直した解析結果の杭頭回転ばねが黙って空になっていた。
    /// </summary>
    [TestClass]
    public class CurvePersistenceTests
    {
        /// <summary>ファイル保存と同じ設定 (フィールドは直列化しない)。</summary>
        private static JsonSerializerOptions FileOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        [TestMethod]
        public void Curve_SurvivesASaveLoadRoundTrip()
        {
            var curve = new MomentRotationCurve([(0.001, 100.0), (0.002, 180.0)]);
            var options = FileOptions();

            string json = JsonSerializer.Serialize(curve, options);
            var restored = JsonSerializer.Deserialize<MomentRotationCurve>(json, options);

            Assert.IsNotNull(restored);
            Assert.AreEqual(2, restored!.Points.Count, "曲線の点が復元されていない");
            Assert.AreEqual(0.001, restored.Points[0].Theta, 1e-12);
            Assert.AreEqual(100.0, restored.Points[0].Moment, 1e-12);
            Assert.AreEqual(0.002, restored.Points[1].Theta, 1e-12);
            Assert.AreEqual(180.0, restored.Points[1].Moment, 1e-12);
        }

        /// <summary>
        /// 保存した JSON に<b>実際の値が入っている</b>こと。
        /// 空オブジェクトの羅列でも往復自体は「成功」してしまうので、中身を見る。
        /// </summary>
        [TestMethod]
        public void SavedJson_ContainsTheActualValues()
        {
            var curve = new MomentRotationCurve([(0.001, 100.0)]);

            string json = JsonSerializer.Serialize(curve, FileOptions());

            StringAssert.Contains(json, "0.001", "θ が保存されていない");
            StringAssert.Contains(json, "100", "M が保存されていない");
        }

        /// <summary>
        /// 値の失われた旧ファイルでも、開けること。
        /// 旧形式では点が <c>{}</c> として保存されている。中身は取り戻せないが、
        /// <b>ファイルが開けなくなってはいけない</b>。
        /// </summary>
        [TestMethod]
        public void OldFileWithEmptyPoints_StillOpens()
        {
            const string oldFormat = """
                {
                  "$id": "1",
                  "Points": { "$id": "2", "$values": [ {}, {} ] }
                }
                """;

            var restored = JsonSerializer.Deserialize<MomentRotationCurve>(oldFormat, FileOptions());

            Assert.IsNotNull(restored, "旧形式のファイルが開けない");
            Assert.AreEqual(0, restored!.Points.Count,
                "旧形式には値が無いので、0 点として読むのが正しい");
        }
    }
}
