using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 検定テキストが変わっていないことを保証する。
    ///
    /// 検定の内部を「テキストを積む」から「構造化した結果を返し、そこからテキストを組む」へ
    /// 作り替えるにあたり、<b>出力が 1 文字も変わらない</b>ことが合格条件になる。
    /// 検定テキストの利用先は 2 箇所しかない:
    ///   ・Views/EvaluationWindow.xaml       … TextBox に OneWay バインド
    ///   ・Output/WordDocument.SummaryTables … 計算書に埋め込み
    /// ここが同一なら、画面も計算書も影響を受けない。
    ///
    /// golden ファイルの更新は環境変数 <c>UPDATE_EVALUATION_GOLDEN=1</c>
    /// (ConvergenceRegressionTests の UPDATE_SNAPSHOTS と同じ流儀)。
    /// </summary>
    [TestClass]
    public class EvaluationTextGoldenTests
    {
        /// <summary>
        /// golden を取る例題。NG が出るものと出ないものの両方を含めること
        /// (NG 側の書式も守るため)。
        /// </summary>
        private static readonly (string Ground, string Pile, int L1, int L2)[] Examples =
        {
            ("Example9",   "PileExample9",   4, 8),    // 場所打ちRC + 18 杭
            ("Example3_5", "PileExample3_5", 4, 16),   // 鋼管杭基礎 (液状化有)
            ("Example10",  "PileExample10",  4, 16),   // 場所打ち杭 (液状化)
        };

        private static bool IsUpdateMode =>
            Environment.GetEnvironmentVariable("UPDATE_EVALUATION_GOLDEN") == "1";

        private static string GoldenDir
        {
            get
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(EvaluationTextGoldenTests).Assembly.Location)!);
                for (; dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "TestProject1", "GoldenFiles", "Evaluation");
                    if (Directory.Exists(Path.Combine(dir.FullName, "TestProject1")))
                        return candidate;
                }
                throw new DirectoryNotFoundException("TestProject1 が見つかりません");
            }
        }

        /// <summary>解析は重いので例題ごとに 1 回だけ実行して使い回す。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, MainWindowViewModel?> _analyzed = new();
        private static readonly System.Collections.Generic.Dictionary<string, string> _loadError = new();

        private static MainWindowViewModel? GetAnalyzedViewModel(string ground)
        {
            if (_analyzed.TryGetValue(ground, out var cached)) return cached;
            if (_loadError.ContainsKey(ground)) return null;

            var spec = Examples.First(e => e.Ground == ground);
            var options = new HeadlessHorizontalRunner.RunOptions
            {
                Level1Steps = spec.L1,
                Level2Steps = spec.L2,
                LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                UseLineSearch = true,
                Parallelism = 1,   // 決定性のため逐次
            };

            try
            {
                var vm = HeadlessHorizontalRunner.RunExampleForViewModel(spec.Ground, spec.Pile, options);
                _analyzed[ground] = vm;
                return vm;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                _loadError[ground] = ex.Message;
                return null;
            }
        }

        [DataTestMethod]
        [DataRow("Example9", false, 0)]   // NG のみ (計算書が使う組合せ)
        [DataRow("Example9", false, 2)]   // OK/NG 両方
        [DataRow("Example9", true, 0)]
        [DataRow("Example9", true, 2)]
        [DataRow("Example3_5", false, 0)]
        [DataRow("Example3_5", false, 2)]
        [DataRow("Example3_5", true, 2)]
        [DataRow("Example10", false, 0)]
        [DataRow("Example10", false, 2)]
        [DataRow("Example10", true, 2)]
        public void EvaluationText_IsUnchanged(string GroundExample, bool factored, int displayFilter)
        {
            var mainVm = GetAnalyzedViewModel(GroundExample);
            if (mainVm == null)
            {
                Assert.Inconclusive($"例題ファイルなしのためスキップ: {_loadError.GetValueOrDefault(GroundExample)}");
                return;
            }

            string actual = EvaluationWindowViewModel.BuildEvaluationText(mainVm, factored, displayFilter);

            string name = $"{GroundExample}_{(factored ? "factored" : "unfactored")}_filter{displayFilter}.txt";
            string path = Path.Combine(GoldenDir, name);

            if (IsUpdateMode)
            {
                Directory.CreateDirectory(GoldenDir);
                File.WriteAllText(path, actual);
                Console.WriteLine($"[UPDATE] 検定テキストを保存: {path} ({actual.Length} 文字)");
                return;
            }

            if (!File.Exists(path))
            {
                Assert.Inconclusive(
                    $"golden ファイルがありません: {path}\n" +
                    "UPDATE_EVALUATION_GOLDEN=1 で実行して作成してください。");
                return;
            }

            string expected = File.ReadAllText(path);

            if (expected != actual)
            {
                // 何行目で食い違ったかを出す (全文を並べても読めない)
                var e = expected.Replace("\r\n", "\n").Split('\n');
                var a = actual.Replace("\r\n", "\n").Split('\n');
                int i = 0;
                while (i < e.Length && i < a.Length && e[i] == a[i]) i++;

                Assert.Fail(
                    $"検定テキストが変わりました ({name})\n" +
                    $"  食い違い: {i + 1} 行目\n" +
                    $"  期待: {(i < e.Length ? e[i] : "(行なし)")}\n" +
                    $"  実際: {(i < a.Length ? a[i] : "(行なし)")}\n" +
                    $"  行数: 期待 {e.Length} / 実際 {a.Length}");
            }
        }

        /// <summary>
        /// golden が空でないこと。解析が空振りしていると全部が空文字で一致してしまい、
        /// 上の比較が意味を失う。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9")]
        [DataRow("Example3_5")]
        [DataRow("Example10")]
        public void EvaluationText_IsNotEmpty(string ground)
        {
            var mainVm = GetAnalyzedViewModel(ground);
            if (mainVm == null)
            {
                Assert.Inconclusive($"例題ファイルなしのためスキップ: {_loadError.GetValueOrDefault(ground)}");
                return;
            }

            string text = EvaluationWindowViewModel.BuildEvaluationText(mainVm, factored: false, displayFilter: 2);

            Assert.IsTrue(text.Length > 500, $"検定テキストが短すぎます ({text.Length} 文字)。解析が空振りしている可能性があります。");
            StringAssert.Contains(text, "検定", "検定テキストになっていない");
            Assert.IsTrue(text.Contains("[OK]") || text.Contains("[NG]"),
                "検定項目が 1 件も出ていない (解析結果が空の可能性)");
        }
    }
}
