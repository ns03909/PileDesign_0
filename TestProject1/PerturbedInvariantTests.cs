using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 値を既定値から動かしたうえで、<b>並び順</b>と <b>DeepCopy</b> の不変条件を見る。
    ///
    /// どちらも既存の網 (<see cref="SaveLoadKeyOrderTests"/> /
    /// <see cref="DeepCopyIsSaveEquivalentTests"/>) があるが、例題が持っていない設定は
    /// 永久に既定値のままなので<b>そこだけ検査されない</b>。値を動かしてから同じことを
    /// 見れば、例題の中身に依存せず全域を張れる。
    ///
    /// 動かす仕掛けと、なぜそれが許されるのかは <see cref="ModelPerturbation"/> にある。
    /// </summary>
    [TestClass]
    public class PerturbedInvariantTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PileDesign_PerturbedInv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve,
        };

        private static readonly Regex IdLine = new("\"Id\": \\d+", RegexOptions.Compiled);
        private static string Normalize(string json) => IdLine.Replace(json, "\"Id\": *");

        private static readonly (string Ground, string Pile)[] Examples =
        [
            ("Example9", "PileExample9"),              // 場所打ちRC
            ("Example3_1", "PileExample3_1"),          // 既製杭 (SC + PRC)
            ("Example3_5", "PileExample3_5"),          // 鋼管杭
            ("Example3_8_1", "PileExample3_8"),        // 場所打ち鋼管コンクリート杭
            ("ExampleCPT2018", "PileExampleCPT2018"),  // キャプテンパイル工法
            ("ExampleCAP3_7", "PileExampleCAP3_7"),    // キャプリングパイル工法
        ];

        /// <summary>
        /// 値を動かしたうえで、ファイル内の値の並び順が違っても読み込み結果が同じこと。
        ///
        /// 並び順に依存するのは「セッターが別のプロパティを読んで計算していて、相手が
        /// まだ既定値のまま計算されている」形。<b>相手が既定値と違って初めて見える</b>ので、
        /// 例題そのままでは検出できない範囲が残る。
        /// 実際に主筋の全断面積 (カタログ値 4592 ⇄ 公称値 4584) がこの形だった。
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(ExampleRows), DynamicDataSourceType.Property)]
        public void PerturbedModel_ReadsTheSameInAnyKeyOrder(string groundName, string pileName)
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            int moved = ModelPerturbation.PerturbAll(inputModel);
            TestSource.AssertScanned(moved, 200, $"{groundName} で動かした値");

            var svc = new FileOperationService(MakeOptions());

            var straight = Path.Combine(_tempDir, "straight.json");
            svc.SaveProjectData(straight, inputModel, new AnaModel(), null!);

            var reversed = Path.Combine(_tempDir, "reversed.json");
            var root = JsonNode.Parse(File.ReadAllText(straight))!;
            File.WriteAllText(reversed,
                SaveLoadKeyOrderTests.ReverseScalarKeys(root)
                    .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

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
                    $"{groundName}+{pileName}: 値を動かすと、並び順の違いで読み込み結果が変わります "
                    + $"({moved} 個の値を動かしました)。"
                    + "セッターが別のプロパティを読んで計算していて、相手がまだ既定値のまま"
                    + "計算されている箇所があります。読み込み中は連鎖を止め、"
                    + "揃ってから一度だけ計算し直してください。"
                    + Environment.NewLine + ModelPerturbation.DifferingProperties(na, nb)
                    + Environment.NewLine + ModelPerturbation.FirstDiff(na, nb, "通常順", "逆順"));
            }
        }

        /// <summary>
        /// 値を動かしたうえで、<c>DeepCopy</c> が<b>値を</b>写し漏らさないこと。
        ///
        /// 写し漏らした項目は<b>元に戻した瞬間に既定値へ落ちる</b>。手書きの複製なので
        /// 足し忘れが起きやすく、しかも<b>元の値が既定値のままだと写し漏らしても一致する</b>。
        ///
        /// 比べるのは<b>値</b>で、直列化した文字列ではない。DeepCopy は共有していた参照を
        /// 2 つに分けるので、保存の形 ($id/$ref の畳まれ方) は<b>変わるのが正しい</b>
        /// (だから保存には使えず、SnapshotForSaving が別に在る)。
        /// 文字列で比べると、その正しい違いに本当の写し漏らしが埋もれる。
        /// </summary>
        [DataTestMethod]
        [DynamicData(nameof(ExampleRows), DynamicDataSourceType.Property)]
        public void PerturbedModel_IsCopiedInFull(string groundName, string pileName)
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            int moved = ModelPerturbation.PerturbAll(inputModel);
            TestSource.AssertScanned(moved, 200, $"{groundName} で動かした値");

            var copy = inputModel.DeepCopy();

            var diffs = new System.Collections.Generic.List<string>();
            int compared = ModelPerturbation.CompareScalars(inputModel, copy, diffs);

            TestSource.AssertScanned(compared, 500, $"{groundName} で見比べた値");

            Assert.AreEqual(0, diffs.Count,
                $"{groundName}+{pileName}: DeepCopy が値を写し漏らしています "
                + $"({moved} 個を動かし、{compared} 個を見比べました)。"
                + "写し漏らした項目は「元に戻す」で既定値へ落ちます:"
                + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", diffs.Take(30)));
        }

        public static System.Collections.Generic.IEnumerable<object[]> ExampleRows
        {
            get
            {
                foreach (var (g, p) in Examples) yield return [g, p];
            }
        }
    }
}
