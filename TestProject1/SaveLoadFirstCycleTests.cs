using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// <b>1 周目</b>の保存→読込で値が落ちないこと。
    ///
    /// <c>SaveLoadRoundTripTests</c> は 2 周目と 3 周目の JSON を比べています。
    /// 2 周目以降は落ちたあとの値どうしなので一致し、<b>最初の 1 回で失われた値は
    /// 構造的に見えません</b>。実際 FT-Pile の杭径がこれで見逃されていました
    /// (φ1000 で保存 → 開くと φ600、以後ずっと φ600 なので冪等)。
    ///
    /// 典型的な原因は <c>public double X { get; private set; }</c> です。
    /// System.Text.Json は非 public なセッターを呼ばないので、書き出されても
    /// 読み戻されず、宣言の既定値に戻ります。ファイルには正しい値が
    /// 書いてあるので、ファイルを見ても気づけません。
    /// </summary>
    [TestClass]
    public class SaveLoadFirstCycleTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PileDesignFirstCycle_" + Guid.NewGuid().ToString("N"));
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
        [DataRow("Example3_1", "PileExample3_1")]
        [DataRow("Example3_2", "PileExample3_2")]
        [DataRow("Example3_3", "PileExample3_3")]
        [DataRow("Example3_4", "PileExample3_4")]
        [DataRow("Example3_5", "PileExample3_5")]
        [DataRow("Example3_8", "PileExample3_8")]
        [DataRow("Example5", "PileExample5")]
        [DataRow("Example7", "PileExample7")]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example10", "PileExample10")]
        [DataRow("ExampleK8", "PileExampleK8")]
        [DataRow("ExampleCPT2018", "PileExampleCPT2018")]      // キャプテンパイル工法
        [DataRow("ExampleCAP3_7", "PileExampleCAP3_7")]        // キャプリングパイル工法
        [DataRow("ExampleCAP3_7_3", "PileExampleCAP3_7_3")]    // キャプリングパイル工法
        public void TheFirstSaveAndLoad_LosesNothing(string groundName, string pileName)
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            var svc = new FileOperationService(MakeOptions());

            var f1 = Path.Combine(_tempDir, "cycle1.json");
            svc.SaveProjectData(f1, inputModel, new AnaModel(), null!);

            var loaded = svc.LoadProjectData(f1);
            var f2 = Path.Combine(_tempDir, "cycle2.json");
            svc.SaveProjectData(f2, loaded.InputModel, loaded.AnaModel, loaded.VerticalBeamCaseResults!);

            string n1 = Normalize(File.ReadAllText(f1));
            string n2 = Normalize(File.ReadAllText(f2));

            if (n1 != n2)
            {
                Assert.Fail(
                    $"{groundName}+{pileName}: 保存して開き直すと値が変わります。"
                    + "書き出されるのに読み戻されないプロパティ "
                    + "(public な get と非 public な set) がないか確かめてください。"
                    + Environment.NewLine + FirstDiff(n1, n2));
            }
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
                for (int k = Math.Max(0, i - 2); k < Math.Min(max, i + 3); k++)
                {
                    var marker = k == i ? ">>" : "  ";
                    ctx += $"\n{marker} {k}: 保存={la[k].TrimEnd('\r')}\n   {k}: 開き直し={lb[k].TrimEnd('\r')}";
                }
                return $"最初の差異 行 {i} (保存 {la.Length} 行 / 開き直し {lb.Length} 行):{ctx}";
            }
            if (la.Length != lb.Length) return $"行数差: 保存 {la.Length} / 開き直し {lb.Length}";
            return "差なし";
        }
    }
}
