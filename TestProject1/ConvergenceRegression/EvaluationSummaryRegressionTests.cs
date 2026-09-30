using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 代表の例題 7 題の<b>検定の要約</b> (支配ケース・最大検定比・件数・検定項目ごとの最大検定比・ケースの収束状態) を固定する。
    ///
    /// <para>収束の回帰 (<see cref="ConvergenceRegressionTests"/>) は変位・反力・曲げモーメントを比べ、検定の文の golden は
    /// 3 題だけの全文を比べていた。解析結果が変わる変更で「どのケース・どの杭が支配するか」「どのケースが収束しなくなったか」が
    /// 変わっても、検定値の小さなずれに埋もれて気付きにくかった。ここは全 7 題で、判定を決める側の量を見る。</para>
    ///
    /// <para>比べ方 (非線形の反復の経路で値がわずかに動くので、その幅は許す):</para>
    /// <list type="bullet">
    /// <item>支配ケース: 記録した項目が、いまも最大検定比の 2% 以内にあること (ほぼ同じ値の項目の入れ替わりは許す)</item>
    /// <item>最大検定比・検定項目ごとの最大検定比: 相対 5% (収束の回帰の物理量と同じ幅)</item>
    /// <item>検定項目・未収束・適用範囲外・検定不能の件数、ケースの収束状態: 完全一致</item>
    /// <item>NG の件数: 検定比が 1.0 の前後 5% にある項目の数までの増減を許す</item>
    /// </list>
    ///
    /// 意図して結果を変えたときは <c>UPDATE_EVALUATION_SUMMARY=1</c> で取り直し、差分に意図した変更だけが入っているかを確かめる。
    /// </summary>
    [TestClass]
    public class EvaluationSummaryRegressionTests
    {
        private const double RatioTolerance = 0.05;
        private const double GoverningBand = 0.02;

        private static bool IsUpdateMode => Environment.GetEnvironmentVariable("UPDATE_EVALUATION_SUMMARY") == "1";

        private static string SnapshotPath(string example)
            => Path.Combine(TestSource.Dir("TestProject1", "ConvergenceRegression", "Snapshots"), "Evaluation", $"{example}.json");

        /// <summary>検定の要約 (比べる量)。</summary>
        public sealed class Summary
        {
            public int Items { get; set; }
            public int Ng { get; set; }
            public int Unconverged { get; set; }
            public int OutOfScope { get; set; }
            public int Unavailable { get; set; }
            public double? MaxRatio { get; set; }
            public string? Governing { get; set; }
            public SortedDictionary<string, double> MaxRatioByCategory { get; set; } = new(StringComparer.Ordinal);
            public SortedDictionary<string, string> CaseStatus { get; set; } = new(StringComparer.Ordinal);
        }

        private static string Identity(EvaluationItem i)
            => $"{i.Category} | {i.TargetDescription} | L{i.Level} {i.LoadCaseName} | {i.LoadCombinationName} | {i.LiquefactionLabel}";

        private static Summary Summarize(MainWindowViewModel vm)
        {
            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var summary = new Summary
            {
                Items = result.Items.Count,
                Ng = result.NgCount,
                Unconverged = result.UnconvergedCount,
                OutOfScope = result.OutOfScopeCount,
                Unavailable = result.UnavailableCount,
                MaxRatio = result.MaxRatio,
                Governing = result.Governing is { } g ? Identity(g) : null,
            };
            foreach (var group in result.Items.Where(i => i.IsJudged && double.IsFinite(i.Ratio)).GroupBy(i => i.Category))
                summary.MaxRatioByCategory[group.Key] = group.Max(i => i.Ratio);
            foreach (var (key, status) in vm.CurrentModel!.BuildCaseConvergenceMap())
                summary.CaseStatus[$"L{key.Level}-{key.LoadCaseNo}.C{key.LoadCombinationNo}{(key.IsLiquefaction ? ".Liq" : "")}"] = status.ToString();
            return summary;
        }

        [DataTestMethod]
        [DataRow("Example9", "PileExample9", 4, 8)]
        [DataRow("Example3_5", "PileExample3_5", 4, 16)]
        [DataRow("ExampleK8", "PileExampleK8", 4, 8)]
        [DataRow("Example10", "PileExample10", 4, 16)]
        [DataRow("Example3_1", "PileExample3_1", 4, 8)]
        [DataRow("Example3_4", "PileExample3_4", 4, 8)]
        [DataRow("Example3_8_1", "PileExample3_8", 4, 8)]
        public void EvaluationSummaryMatchesSnapshot(string groundName, string pileName, int level1Steps, int level2Steps)
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel(groundName, pileName, new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = level1Steps,
                    Level2Steps = level2Steps,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            vm.ApplyConcreteModelOptions();
            var actual = Summarize(vm);
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            string path = SnapshotPath(groundName);

            if (IsUpdateMode)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(actual, options));
                return;
            }
            Assert.IsTrue(File.Exists(path), $"検定の要約のスナップショットがありません: {path} (UPDATE_EVALUATION_SUMMARY=1 で作る)");
            var expected = JsonSerializer.Deserialize<Summary>(File.ReadAllText(path))!;

            var result = EvaluationService.BuildEvaluationResult(vm, factored: true);
            var problems = new List<string>();

            void Exact(string what, object? e, object? a) { if (!Equals(e, a)) problems.Add($"{what}: {e} → {a}"); }
            Exact("検定項目の件数", expected.Items, actual.Items);
            Exact("未収束の件数", expected.Unconverged, actual.Unconverged);
            Exact("適用範囲外の件数", expected.OutOfScope, actual.OutOfScope);
            Exact("検定不能の件数", expected.Unavailable, actual.Unavailable);

            int borderline = result.Items.Count(i => i.IsJudged && i.Ratio is double r && Math.Abs(r - 1.0) <= RatioTolerance);
            if (Math.Abs(expected.Ng - actual.Ng) > borderline)
                problems.Add($"NG の件数: {expected.Ng} → {actual.Ng} (検定比が 1.0 の前後 5% の項目は {borderline} 件)");

            void Ratio(string what, double? e, double? a)
            {
                if (e is double x && a is double y)
                {
                    if (Math.Abs(y - x) > RatioTolerance * Math.Abs(x)) problems.Add($"{what}: {x:F3} → {y:F3} ({(y / x - 1) * 100:+0.0;-0.0}%)");
                }
                else if (e.HasValue != a.HasValue) problems.Add($"{what}: {e} → {a}");
            }
            Ratio("最大検定比", expected.MaxRatio, actual.MaxRatio);
            foreach (var category in expected.MaxRatioByCategory.Keys.Union(actual.MaxRatioByCategory.Keys))
                Ratio($"最大検定比 ({category})",
                    expected.MaxRatioByCategory.TryGetValue(category, out var e) ? e : null,
                    actual.MaxRatioByCategory.TryGetValue(category, out var a) ? a : null);

            // 支配ケース: 記録した項目が、いまも最大検定比の 2% 以内にあること
            if (expected.Governing != null && actual.MaxRatio is double max)
            {
                var recorded = result.Items.FirstOrDefault(i => Identity(i) == expected.Governing);
                if (recorded?.Ratio is not double r || r < max * (1 - GoverningBand))
                    problems.Add($"支配ケースが変わった: {expected.Governing} → {actual.Governing} (記録した項目の検定比 {recorded?.Ratio:F3} / 最大 {max:F3})");
            }

            foreach (var key in expected.CaseStatus.Keys.Union(actual.CaseStatus.Keys))
                Exact($"ケースの収束状態 {key}",
                    expected.CaseStatus.TryGetValue(key, out var e) ? e : "(無し)",
                    actual.CaseStatus.TryGetValue(key, out var a) ? a : "(無し)");

            Assert.AreEqual(0, problems.Count, $"[{groundName}] 検定の要約が変わりました:\n  " + string.Join("\n  ", problems));
        }
    }
}
