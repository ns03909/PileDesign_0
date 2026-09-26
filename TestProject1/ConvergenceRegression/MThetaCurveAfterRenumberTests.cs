using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;

using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 読込で荷重ケースの番号を振り直したあとも、杭頭 M–θ 曲線の表が各ケースの曲線を出すこと。
    ///
    /// ケース別の控え (<see cref="RotationalSpring.CaseMThetaSnapshots"/>) の鍵には荷重ケースの番号が入る
    /// (<see cref="RotationalSpring.MakeCaseKey"/>)。読込は番号を一覧の並び順に振り直す
    /// (<see cref="LoadCasesInput.NormalizeLoadCaseNumbers"/>) ので、解析したときの番号が並び順と違う
    /// (欠番・重複) ファイルでは、鍵が振り直す前の番号のまま残る。
    /// </summary>
    [TestClass]
    public class MThetaCurveAfterRenumberTests
    {
        private static MainWindowViewModel? _analyzed;
        private static readonly object Gate = new();

        /// <summary>計算例9 を解析したもの (レベル2 の軸力を 1.5 倍にして、ケースごとに曲線を変えてある)。テストどうしで使い回す。</summary>
        private static MainWindowViewModel Analyzed()
        {
            lock (Gate)
            {
                if (_analyzed != null) return _analyzed;
                try
                {
                    _analyzed = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                        LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                        Customize = input =>
                        {
                            foreach (var pile in input.PileLayoutItems!)
                                for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
                                    pile.AxialForceLevel2s[i] *= 1.5;
                        },
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
                {
                    Assert.Inconclusive("例題ファイルなし");
                }
                _analyzed!.CaptureAnalysisResultSet();
                return _analyzed;
            }
        }

        /// <summary>
        /// ケースごとの M–θ 曲線の表の行。ケースは解析時の入力の一覧での位置 (「L2#0」) で表す
        /// (番号は振り直され、名前は空欄だと読込で付け直されるので、どちらも鍵にできない)。曲線が無いケースは状態欄の文を入れる。
        /// </summary>
        private static Dictionary<string, string> CurvesPerCase(MainWindowViewModel vm)
        {
            var model = vm.CurrentModel!;
            var lci = vm.ResultInputModel.LoadCasesInput!;
            var service = new AnalysisResultTableService();
            var result = new Dictionary<string, string>();
            foreach (var step in model.AnalysisStepResults
                         .GroupBy(s => (s.LoadCase, s.LoadCombination?.No, s.IsLiquefaction))
                         .Select(g => g.OrderBy(s => s.Step).Last()))
            {
                var list = step.LoadCase.Level == 1 ? lci.LoadCasesLevel1 : lci.LoadCasesLevel2;
                int index = list.ToList().FindIndex(c => ReferenceEquals(c, step.LoadCase));
                Assert.IsTrue(index >= 0, "解析結果が指す荷重ケースが、解析時の入力の荷重ケースと別の実体です (番号を振り直しても結果の側は変わらない)");
                var table = service.BuildTables(model, step.LoadCase, step.LoadCombination, step.IsLiquefaction, step.Step)
                    .FirstOrDefault(t => t.Category == "MThetaCurve");
                if (table == null) continue;
                var rows = table.Rows.OfType<MThetaCurveRow>().Where(r => r.SpringIndex == 1).ToList();
                string curve = rows.Any(r => r.PointIndex >= 1)
                    ? string.Join(";", rows.Where(r => r.PointIndex >= 1).Select(r => $"{r.Theta:G6},{r.Moment:G6}"))
                    : "曲線なし: " + string.Join(" ", rows.Select(r => r.Status));
                result[$"L{step.LoadCase.Level}#{index}|{step.LoadCombination?.No}|{step.IsLiquefaction}"] = curve;
            }
            return result;
        }

        /// <summary>
        /// 解析したときの番号が <paramref name="numbering"/> だったことにして保存し、実際の読込の経路で読み直す。
        /// 荷重ケースの実体 (入力・解析時の入力・結果が指すもの) の番号と、控えの鍵を書き換えて、その番号で解析したファイルを作る。
        /// </summary>
        private static void SaveAsIfAnalyzedWith(MainWindowViewModel vm, string path, Func<LoadCase, int> numbering, Action<MainWindowViewModel> check)
        {
            var model = vm.CurrentModel!;
            var cases = new List<LoadCase>();
            void Add(LoadCase? lc) { if (lc != null && lc.Level is 1 or 2 && !cases.Any(c => ReferenceEquals(c, lc))) cases.Add(lc); }
            foreach (var input in new[] { vm.CurrentInputModel, vm.ResultInputModel })
                foreach (var lc in input!.LoadCasesInput!.LoadCasesLevel1.Concat(input.LoadCasesInput.LoadCasesLevel2)) Add(lc);
            foreach (var s in model.AnalysisStepResults) Add(s.LoadCase);

            var original = cases.Select(c => (Case: c, c.No)).ToList();
            var originalKeys = model.RotationalSprings.Select(rs => rs.CaseMThetaSnapshots.ToArray()).ToList();
            try
            {
                // 控えの鍵を、その番号で解析したときの鍵にする (同じ番号のケースは後から解析した方で上書きされる)
                var byKey = model.AnalysisStepResults.Select(s => s.LoadCase).Distinct()
                    .SelectMany(lc => new[] { false, true }.SelectMany(liq => model.AnalysisStepResults
                        .Where(s => ReferenceEquals(s.LoadCase, lc)).Select(s => s.LoadCombination?.No ?? 0).Distinct()
                        .Select(comb => (Old: RotationalSpring.MakeCaseKey(lc, comb, liq),
                                         New: $"L{lc.Level}-{numbering(lc)}|{comb}|{liq}"))))
                    .ToList();
                foreach (var rs in model.RotationalSprings)
                {
                    var old = rs.CaseMThetaSnapshots.ToArray();
                    rs.CaseMThetaSnapshots.Clear();
                    foreach (var (oldKey, newKey) in byKey)
                        if (old.FirstOrDefault(kv => kv.Key == oldKey) is { Value: not null } kv)
                            rs.CaseMThetaSnapshots[newKey] = kv.Value;
                }
                foreach (var c in cases) c.No = numbering(c);

                var options = (JsonSerializerOptions)typeof(MainWindowViewModel)
                    .GetField("_jsonOptions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                new FileOperationService(options).SaveProjectDataAsync(path, vm.CurrentInputModel!, model, null,
                    vm.ResultInputModel, DateTime.Now, PileDesign.Models.PileFemLinkTable.Build(vm.ResultInputModel, model), false, false)
                    .GetAwaiter().GetResult();
            }
            finally
            {
                foreach (var (c, no) in original) c.No = no;
                for (int i = 0; i < model.RotationalSprings.Count; i++)
                {
                    model.RotationalSprings[i].CaseMThetaSnapshots.Clear();
                    foreach (var kv in originalKeys[i]) model.RotationalSprings[i].CaseMThetaSnapshots[kv.Key] = kv.Value;
                }
            }

            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var error = XamlSmokeTestSupport.RunOnStaThread(() =>
                {
                    var loaded = new MainWindowViewModel();
                    try
                    {
                        Assert.IsTrue(loaded.TryRestoreAutoSave(new AutoSaveService.RestoreCandidate(path, DateTime.Now, null)),
                            "(前提) 読み込めていません");
                        check(loaded);
                    }
                    finally
                    {
                        loaded.EndAutoSaveSessionNormally();
                    }
                }, out bool timedOut);
                Assert.IsFalse(timedOut);
                if (error != null) throw error;
            }
            finally
            {
                MessageService.IsUnattended = unattended;
                File.Delete(path);
            }
        }

        private static string TempPath() => Path.Combine(Path.GetTempPath(), $"MThetaRenumber_{Guid.NewGuid():N}.pdj");

        /// <summary>欠番のある番号 (11, 12, …) で解析したファイル: 振り直したあとも、各ケースに自分の曲線が出ること。</summary>
        [TestMethod]
        public void GappedNumbersKeepEachCaseCurve()
        {
            var vm = Analyzed();
            var before = CurvesPerCase(vm);
            Assert.IsTrue(before.Values.Where(v => !v.StartsWith("曲線なし")).Distinct().Count() >= 2,
                "(前提) ケースごとに M–θ 曲線が違いません。この検査が成立していません");

            SaveAsIfAnalyzedWith(vm, TempPath(), lc => lc.No + 10, loaded =>
            {
                var model = loaded.CurrentModel!;
                Assert.IsTrue(loaded.ResultInputModel.LoadCasesInput!.LoadCasesLevel1.Select(c => c.No).SequenceEqual(
                    Enumerable.Range(1, loaded.ResultInputModel.LoadCasesInput.LoadCasesLevel1.Count)), "(前提) 番号が振り直されていません");
                foreach (var s in model.AnalysisStepResults)
                    Assert.IsTrue(s.LoadCase.No <= 10, "解析結果が指す荷重ケースの番号が振り直されていません (入力と別の実体?)");

                var after = CurvesPerCase(loaded);
                foreach (var (key, curve) in before)
                {
                    Assert.IsTrue(after.TryGetValue(key, out var reloaded), $"{key}: 読み直したあと、このケースの表がありません");
                    Assert.AreEqual(curve, reloaded, $"{key}: 番号を振り直したあと、M–θ 曲線が解析したときと違います");
                }
            });
        }

        /// <summary>
        /// 鍵の移し方: 欠番は新しい番号へ移す。振り直す前に番号が重複していたケースの控えと、一覧に無い番号の控えは捨てる
        /// (前者はどのケースのものか決められず、後者は振り直したあとの番号と重なって別のケースの曲線として出る)。
        /// 計算例はレベルごとに荷重ケースが 1 つなので、重複は実際の解析では組めない。鍵の移し方だけを見る。
        /// </summary>
        [TestMethod]
        public void KeysOfDuplicatedOrUnlistedNumbersAreDropped()
        {
            var rs = new RotationalSpring();
            var snap = new Dictionary<string, MThetaCaseSnapshot>();
            foreach (var key in new[] { "L1-5|1|False", "L1-5|1|True", "L1-7|1|False", "L1-1|1|False", "L2-1|1|False", "L2-3|2|False" })
                rs.CaseMThetaSnapshots[key] = snap[key] = new MThetaCaseSnapshot();

            // レベル1: 5 → 1, 7 → 2 (欠番)。番号 1 の控えは一覧に無い。レベル2: 番号 1 のケースが 2 つ → 1, 2。番号 3 は一覧に無い
            RotationalSpring.RenumberCaseKeys([rs], [(1, 5, 1), (1, 7, 2), (2, 1, 1), (2, 1, 2)]);

            CollectionAssert.AreEquivalent(new[] { "L1-1|1|False", "L1-1|1|True", "L1-2|1|False" }, rs.CaseMThetaSnapshots.Keys.ToArray(),
                "鍵の移し方が違います");
            Assert.AreSame(snap["L1-5|1|False"], rs.CaseMThetaSnapshots["L1-1|1|False"], "欠番のケースの控えが、新しい番号へ移っていません");
            Assert.AreSame(snap["L1-7|1|False"], rs.CaseMThetaSnapshots["L1-2|1|False"]);
        }

        /// <summary>読込で番号を振り直したら、控えの鍵も移すこと (解析したときの入力の、振り直す前の番号を控えてから)。</summary>
        [TestMethod]
        public void LoadingRenumbersTheCaseKeys()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            int capture = body.IndexOf("var resultCases =", StringComparison.Ordinal);
            int normalize = body.IndexOf("NormalizeLoadCaseNumbers()", StringComparison.Ordinal);
            int move = body.IndexOf("RotationalSpring.RenumberCaseKeys(", StringComparison.Ordinal);
            Assert.IsTrue(capture >= 0 && capture < normalize && normalize < move,
                "読込で荷重ケースの番号を振り直したあと、杭頭 M–θ の控えの鍵を移していません");
        }
    }
}
