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

        /// <summary>
        /// 解析済みの ViewModel を返す。<b>返す前に材料オプションをこのモデルのものへ戻す。</b>
        ///
        /// コンクリートのモデル化オプション (<c>ConcreteModelOptions</c>) と M-φ キャッシュは
        /// <b>プロセス全体で共有される static</b>。他のテストが ViewModel を作って
        /// <c>ApplyConcreteModelOptions</c> を呼ぶと (新規作成・ファイル読込の経路が呼ぶ)、
        /// この static が別のモデルの設定に書き換わる。検定テキストは呼ばれた時点の
        /// 限界曲線で組み立てるので、解析が済んでいても出力が変わりうる。
        ///
        /// ※ <c>("Example3_5", factored: true, filter: 2)</c> の間欠的な失敗は<b>これが原因ではなく</b>、
        ///    断面の NM 曲線を並列に読んだときの競合だった (2026-08-30 に解決)。
        ///    解析値は正しく、安全限界 M だけが 710.3 と 664.8 の間で揺れていた。
        ///    詳細と回帰ガードは <see cref="PileSectionCurveThreadSafetyTests"/>。
        /// </summary>
        private static MainWindowViewModel? GetAnalyzedViewModel(string ground)
        {
            var vm = GetOrRunAnalysis(ground);
            // 静的オプションをこの例題のものへ戻してから使う
            vm?.ApplyConcreteModelOptions();
            return vm;
        }

        private static MainWindowViewModel? GetOrRunAnalysis(string ground)
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

            string actual = EvaluationService.BuildEvaluationText(mainVm, factored, displayFilter);

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

                // 実際の出力をファイルに残す。この食い違いは間欠的にしか出ないため、
                // 落ちた瞬間の全文が無いと後から追えない (golden との diff を取るのに使う)。
                string actualPath = Path.Combine(GoldenDir, name + ".actual.txt");
                try
                {
                    Directory.CreateDirectory(GoldenDir);
                    File.WriteAllText(actualPath, actual);
                }
                catch (IOException)
                {
                    actualPath = "(保存できませんでした)";
                }

                Assert.Fail(
                    $"検定テキストが変わりました ({name})\n" +
                    $"  食い違い: {i + 1} 行目\n" +
                    $"  期待: {(i < e.Length ? e[i] : "(行なし)")}\n" +
                    $"  実際: {(i < a.Length ? a[i] : "(行なし)")}\n" +
                    $"  行数: 期待 {e.Length} / 実際 {a.Length}\n" +
                    $"  前後: {string.Join(" / ", a.Skip(Math.Max(0, i - 2)).Take(5))}\n" +
                    $"  実際の全文: {actualPath}");
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

            string text = EvaluationService.BuildEvaluationText(mainVm, factored: false, displayFilter: 2);

            Assert.IsTrue(text.Length > 500, $"検定テキストが短すぎます ({text.Length} 文字)。解析が空振りしている可能性があります。");
            StringAssert.Contains(text, "検定", "検定テキストになっていない");
            Assert.IsTrue(text.Contains("[OK]") || text.Contains("[NG]"),
                "検定項目が 1 件も出ていない (解析結果が空の可能性)");
        }
    
        /// <summary>
        /// 杭体せん断の検定が、地震動レベルに応じた限界状態で並ぶこと。
        ///
        /// せん断はもともと検定されておらず、Q-N 曲線はグラフと計算書に描くだけだった。
        /// 割り当ては曲げと同じ規則にしてある。
        ///   レベル1 → 損傷限界 / レベル2 → 安全限界 (耐震グレード S は損傷限界)
        ///   長期 (VL 単独ケース) → 使用限界
        /// 長期は VL 単独解析を行った場合にだけ現れるので、ここでは有無を問わない。
        /// </summary>
        [TestMethod]
        public void ShearItems_UseTheSameLimitStateAsMoment()
        {
            var vm = GetAnalyzedViewModel("Example10");
            if (vm == null) { Assert.Inconclusive("例題の解析ができない環境"); return; }

            var result = EvaluationService.BuildEvaluationResult(vm, factored: false);
            var shear = result.Items
                .Where(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileSectionShear)
                .ToList();

            Assert.IsTrue(shear.Count > 0, "せん断の検定項目が 1 件も無い");

            foreach (var item in shear)
            {
                string expected = item.Level switch
                {
                    0 => "使用限界",
                    1 => "損傷限界",
                    _ => "安全限界",
                };
                Assert.AreEqual(expected, item.LimitName,
                    $"レベル{item.Level} のせん断が {item.LimitName} で検定されている");
                Assert.AreEqual("kN", item.Unit, "せん断の単位が kN でない");
                Assert.IsTrue(item.Limit > 0, "せん断の限界値が 0 以下のまま項目になっている");
                Assert.IsNotNull(item.AxialForce, "限界値の前提となった軸力が記録されていない");
            }

            // M/(Q·d) は解析した断面力から杭・荷重ケースごとに求める。
            // 既定値 3.0 で固定していると、解析した形状と関係のない耐力で検定することになる。
            Assert.IsTrue(shear.All(i => i.MonQd != null), "M/(Q·d) が記録されていない");
            Assert.IsTrue(shear.Select(i => System.Math.Round(i.MonQd!.Value, 3)).Distinct().Count() > 1
                          || shear.Any(i => System.Math.Abs(i.MonQd!.Value - PileDesign.Models.InputData.PileSection.DefaultMonQd) > 1e-6),
                "M/(Q·d) が全行で既定値のまま。解析結果から求めていない疑いがある。");

            // 曲げと同じ対象 (梁要素の i端/j端) で、同じ数だけ並ぶこと
            var moment = result.Items
                .Where(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileSectionMoment)
                .ToList();
            Assert.AreEqual(moment.Count, shear.Count,
                "曲げとせん断で検定した端部の数が食い違う (どちらかが欠けている)");
        }

        /// <summary>
        /// 検定は<b>杭ごとの軸力</b>で限界値を引くこと。
        ///
        /// 杭体番号 (PileBodyNo) は断面と区間の仕様の番号で、同じ杭体を複数の杭が使う。
        /// 杭体番号で杭を引くと最初の 1 本しか当たらず、その杭の軸力で全部を検定してしまう。
        /// 限界値は軸力で変わるので、他の杭は別の杭の軸力で引いた限界値と比べられていた
        /// (計算例3.5 では 安全限界M が 728.6 と 614.1 で 16% 違った)。
        /// </summary>
        [TestMethod]
        public void Items_UseEachPilesOwnAxialForce()
        {
            var vm = GetAnalyzedViewModel("Example3_5");
            if (vm == null) { Assert.Inconclusive("例題の解析ができない環境"); return; }

            var items = EvaluationService.BuildEvaluationResult(vm, factored: false).Items
                .Where(i => i.Kind == PileDesign.Models.Results.EvaluationKind.PileSectionMoment
                         || i.Kind == PileDesign.Models.Results.EvaluationKind.PileSectionShear)
                .ToList();
            Assert.IsTrue(items.Count > 0, "杭体の検定項目が無い");

            // 杭が特定できていること (できないと行が区別できず、軸力も引けない)
            Assert.IsTrue(items.All(i => i.PileNo != null),
                "杭を特定できていない項目がある (梁→杭の対応表を引けていない)");

            // 同じ杭体を共有する杭が、それぞれ自分の軸力で検定されていること
            var byPileBody = items.GroupBy(i => i.PileBodyNo)
                .FirstOrDefault(g => g.Select(i => i.PileNo).Distinct().Count() > 1);
            Assert.IsNotNull(byPileBody, "前提: 杭体を共有する杭が複数ある例題であること");

            int distinctAxial = byPileBody!.Select(i => i.AxialForce).Distinct().Count();
            Assert.IsTrue(distinctAxial > 1,
                $"杭体No.{byPileBody.Key} を共有する杭が全部同じ軸力 ({byPileBody.First().AxialForce:F1} kN) "
                + "で検定されている。杭体番号で杭を引いていないか確認すること。");
        }
}
}
