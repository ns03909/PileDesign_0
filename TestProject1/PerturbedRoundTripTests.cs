using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// <b>全プロパティを既定値から動かしてから</b>保存の往復が壊れないこと。
    ///
    /// 往復の網は「保存 → 開く → 保存」で 2 つのファイルが 1 バイトも違わないことを
    /// 見る。この判定は<b>値が物理的に妥当かどうかに依存しない</b>。セッターが
    /// クランプしても、書き出したものを読み戻せば同じ形になるはずだからである。
    ///
    /// だから逆に、値を<b>でたらめに動かしてよい</b>。動かす目的はただ 1 つで、
    /// <b>既定値のままのプロパティを無くすこと</b>。
    ///
    /// これが要るのは 2026-09-10 の経験から。例題を読むテストのビルダーが断面を
    /// 読んでいなかったあいだ、往復の網は<b>既定値だけのモデルを往復させていた</b>。
    /// ビルダーを実機に合わせた瞬間に、本体の不具合 2 件が落ちた。
    ///
    /// <list type="number">
    /// <item>主筋の配置直径が、読み込みの仕上げで既定のかぶり厚から導いた値に
    ///   塗り潰される (計算例9 φ1000 で 700 → 600、曲げ耐力が下がる)</item>
    /// <item>主筋の全断面積が、ファイル内の値の並び順でカタログ値と公称値のどちらが
    ///   残るか変わる</item>
    /// </list>
    ///
    /// どちらも<b>「セッターが別の値から計算し直して塗り潰す」</b>型で、型を静的に
    /// 走査する <see cref="PersistedPropertyRestorabilityTests"/> では捕まらない
    /// (セッターは public なので形の上では問題がない)。値が既定値と違って初めて見える。
    ///
    /// 例題に頼ると「例題が持っていない設定」は永久に既定値のままなので、
    /// ここでは型から辿って機械的に全部動かす。
    /// </summary>
    [TestClass]
    public class PerturbedRoundTripTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PileDesign_Perturbed_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        private static System.Text.Json.JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        };

        // 読込で振り直される runtime-only な番号。永続識別子は UniqueId。
        private static readonly Regex IdLine = new("\"Id\": \\d+", RegexOptions.Compiled);
        private static string Normalize(string json) => IdLine.Replace(json, "\"Id\": *");

        /// <summary>
        /// 例題を読み、全スカラーを既定値から動かしてから往復させる。
        ///
        /// 動かすのは double / int / bool だけ。<b>文字列と列挙は動かさない</b> —
        /// 断面タイプや工法の名前を壊すと、そもそも別のモデルの話になってしまう。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]            // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]        // 既製杭 (SC + PRC)
        [DataRow("Example3_5", "PileExample3_5")]        // 鋼管杭
        [DataRow("Example3_8_1", "PileExample3_8")]      // 場所打ち鋼管コンクリート杭
        [DataRow("ExampleCPT2018", "PileExampleCPT2018")] // キャプテンパイル工法
        [DataRow("ExampleCAP3_7", "PileExampleCAP3_7")]  // キャプリングパイル工法
        public void EveryValueMovedOffItsDefault_StillRoundTrips(string groundName, string pileName)
        {
            var (inputModel, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (inputModel == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            int moved = ModelPerturbation.PerturbAll(inputModel);

            // 網が空振りしていないこと。既定値のままなら何も検査できていない
            TestSource.AssertScanned(moved, 200, $"{groundName} で動かした値");

            var svc = new FileOperationService(MakeOptions());

            var f1 = Path.Combine(_tempDir, "p1.json");
            svc.SaveProjectData(f1, inputModel, new AnaModel(), null!);

            var loaded = svc.LoadProjectData(f1);
            var f2 = Path.Combine(_tempDir, "p2.json");
            svc.SaveProjectData(f2, loaded.InputModel, loaded.AnaModel, loaded.VerticalBeamCaseResults!);

            string n1 = Normalize(File.ReadAllText(f1));
            string n2 = Normalize(File.ReadAllText(f2));

            if (n1 != n2)
            {
                Assert.Fail(
                    $"{groundName}+{pileName}: 値を既定値から動かすと、保存して開き直した結果が変わります "
                    + $"({moved} 個の値を動かしました)。"
                    + "セッターが別の値から計算し直して、ファイルに書いた値を塗り潰しています。"
                    + Environment.NewLine + ModelPerturbation.DifferingProperties(n1, n2)
                    + Environment.NewLine + ModelPerturbation.FirstDiff(n1, n2, "保存", "開き直し"));
            }
        }

    }
}
