using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 保存ファイルの<b>キーの並び順</b>が変わっても、読み込んだ結果が変わらないこと。
    ///
    /// System.Text.Json は<b>ファイルに書かれた順</b>にプロパティを設定します。順序は
    /// 保証されないので、<c>A</c> のセッターが <c>B</c> を読んで何かを計算していると、
    /// <b><c>B</c> がまだ既定値のまま <c>A</c> が計算される</b>ことがあります。
    /// 例外にはならず、値だけが静かに間違います。
    ///
    /// 実際に <c>PileSection</c> がそうでした。<c>ConcreteOutDia</c> を設定すると
    /// <c>RecalculatePileDia()</c> が走り、杭体タイプがまだ既定 (場所打ちRC) だと
    /// <b>既製杭の肉厚を場所打ちRC の式で塗り潰していました</b>
    /// (φ1100 で 140mm → 550mm)。しかも塗り潰された値は、あとで杭体タイプが
    /// 正しくなっても戻りません。
    ///
    /// 対処は <c>IJsonOnDeserializing</c> / <c>IJsonOnDeserialized</c> で、
    /// 読み込み中は連鎖を止め、すべて揃ってから一度だけ計算し直す形です
    /// (<c>PileBodyInput</c> / <c>SoilPile</c> / <c>ZDataItem</c> と同じ仕組み)。
    ///
    /// <c>$id</c> / <c>$ref</c> / <c>$values</c> は <c>ReferenceHandler.Preserve</c> の
    /// 仕組み上、順序に意味がある (定義が参照より前に必要) ので動かしません。
    /// 入れ子も動かさず、<b>同じオブジェクトの値どうし</b>の並びだけ逆にします。
    /// 順序依存が起きるのはまさにそこなので、狙いは外しません。
    /// </summary>
    [TestClass]
    public class SaveLoadKeyOrderTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve
        };

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PileDesignKeyOrder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
            }
        }

        [DataTestMethod]
        [DataRow("Example3_1", "PileExample3_1")]               // 既製コンクリート杭
        [DataRow("Example3_5", "PileExample3_5")]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example10", "PileExample10")]
        [DataRow("ExampleCPT2018", "PileExampleCPT2018")]       // キャプテンパイル工法
        [DataRow("ExampleCAP3_7", "PileExampleCAP3_7")]         // キャプリングパイル工法
        public void ReadingTheSameFile_InADifferentKeyOrder_GivesTheSameModel(
            string groundName, string pileName)
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            var svc = new FileOperationService(MakeOptions());

            var straight = Path.Combine(_tempDir, "straight.json");
            svc.SaveProjectData(straight, inputModel, new AnaModel(), null!);

            // 同じ中身で、値の並びだけ逆にしたファイルを作る
            var reversed = Path.Combine(_tempDir, "reversed.json");
            var root = JsonNode.Parse(File.ReadAllText(straight))!;
            File.WriteAllText(reversed,
                ReverseScalarKeys(root).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // どちらも読んで、同じ形で書き戻す
            var a = svc.LoadProjectData(straight);
            var b = svc.LoadProjectData(reversed);

            var outA = Path.Combine(_tempDir, "outA.json");
            var outB = Path.Combine(_tempDir, "outB.json");
            svc.SaveProjectData(outA, a.InputModel, a.AnaModel, a.VerticalBeamCaseResults!);
            svc.SaveProjectData(outB, b.InputModel, b.AnaModel, b.VerticalBeamCaseResults!);

            string na = Normalize(File.ReadAllText(outA));
            string nb = Normalize(File.ReadAllText(outB));

            if (na != nb)
            {
                Assert.Fail(
                    $"{groundName}+{pileName}: 同じ内容でも値の並び順が違うと読み込み結果が変わります。"
                    + "セッターが別のプロパティを読んで計算していて、相手がまだ既定値のまま"
                    + "計算されている箇所があります。IJsonOnDeserializing / IJsonOnDeserialized で"
                    + "読み込み中は連鎖を止め、揃ってから一度だけ計算し直してください。"
                    + Environment.NewLine + FirstDiff(na, nb));
            }
        }

        /// <summary>
        /// <b>値だけのプロパティ</b>の並びを逆にする。オブジェクトと配列は元の位置に
        /// 置いたままにする (Preserve の <c>$id</c> が <c>$ref</c> より前に必要なため)。
        /// </summary>
        private static JsonNode ReverseScalarKeys(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                var entries = obj.ToList();
                foreach (var kv in entries) obj.Remove(kv.Key);

                // 値だけのプロパティの「並び」を逆にする。枠 (どの位置が値か) は保つ。
                var scalarSlots = new List<int>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var v = entries[i].Value;
                    if (!entries[i].Key.StartsWith('$') && v is not JsonObject && v is not JsonArray)
                        scalarSlots.Add(i);
                }
                var reordered = new List<KeyValuePair<string, JsonNode?>>(entries);
                for (int k = 0; k < scalarSlots.Count; k++)
                    reordered[scalarSlots[k]] = entries[scalarSlots[scalarSlots.Count - 1 - k]];

                var result = new JsonObject();
                foreach (var kv in reordered)
                    result[kv.Key] = kv.Value == null ? null : ReverseScalarKeys(kv.Value);
                return result;
            }
            if (node is JsonArray arr)
            {
                var items = arr.ToList();
                foreach (var it in items) arr.Remove(it);
                var result = new JsonArray();
                foreach (var it in items) result.Add(it == null ? null : ReverseScalarKeys(it));
                return result;
            }
            return node;
        }

        // ロード時に再採番される runtime-only な数値 Id。永続識別子は UniqueId。
        private static readonly Regex IdLine = new("\"Id\": \\d+", RegexOptions.Compiled);

        private static string Normalize(string json) => IdLine.Replace(json, "\"Id\": *");

        private static string FirstDiff(string a, string b)
        {
            var la = a.Split('\n');
            var lb = b.Split('\n');
            int max = Math.Min(la.Length, lb.Length);
            for (int i = 0; i < max; i++)
            {
                if (la[i] == lb[i]) continue;
                var ctx = "";
                for (int k = Math.Max(0, i - 3); k < Math.Min(max, i + 4); k++)
                {
                    var marker = k == i ? ">>" : "  ";
                    ctx += $"\n{marker} {k}: 通常順={la[k].TrimEnd('\r')}\n   {k}: 逆順  ={lb[k].TrimEnd('\r')}";
                }
                return $"最初の差異 行 {i} (通常 {la.Length} 行 / 逆順 {lb.Length} 行):{ctx}";
            }
            if (la.Length != lb.Length) return $"行数差: 通常 {la.Length} / 逆順 {lb.Length}";
            return "差なし";
        }
    }
}
