using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
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
    /// 解析結果テーブルの杭頭 M–θ 曲線が、表示中の解析ケースのものであること (保存・再読込のあとも)。
    ///
    /// M–θ 曲線は杭の軸力で変わるので、荷重ケースごとに違う。テーブルは解析中に取ったケース別の控え
    /// (<see cref="RotationalSpring.CaseMThetaSnapshots"/>) を引き、控えが無ければ<b>ばね本体の曲線</b>へ切り替える。
    /// 控えは保存されないので、再読込のあとは必ず本体の曲線になり、別のケースの曲線が出る恐れがある。
    /// </summary>
    [TestClass]
    public class MThetaTableCaseCurveTests
    {
        private static Dictionary<string, List<(double Theta, double M)>> CurvesPerCase(AnaModel model)
        {
            var service = new AnalysisResultTableService();
            var result = new Dictionary<string, List<(double, double)>>();
            foreach (var step in model.AnalysisStepResults
                         .GroupBy(s => (s.LoadCase.Level, s.LoadCase.No, s.LoadCombination?.No, s.IsLiquefaction))
                         .Select(g => g.OrderBy(s => s.Step).Last()))
            {
                var table = service.BuildTables(model, step.LoadCase, step.LoadCombination, step.IsLiquefaction, step.Step)
                    .FirstOrDefault(t => t.Category == "MThetaCurve");
                if (table == null) continue;
                foreach (var row in table.Rows.OfType<MThetaCurveRow>().Where(r => r.PointIndex >= 1 && r.SpringIndex == 1))
                {
                    string key = $"L{step.LoadCase.Level}-{step.LoadCase.No}|{step.LoadCombination?.No}|{step.IsLiquefaction}";   // 名前は空欄のことがあるので使わない
                    if (!result.TryGetValue(key, out var list)) result[key] = list = [];
                    list.Add((row.Theta, row.Moment));
                }
            }
            return result;
        }

        [TestMethod]
        public void EachCaseShowsItsOwnCurveBeforeAndAfterReload()
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4, Level2Steps = 8, UseLineSearch = true, Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Both,
                    // M–θ 曲線は杭の軸力で決まる。計算例9 はレベル1・2 の地震時軸力が同じで、曲線がケースで変わらない
                    // (取り違えても気付けない) ので、レベル2 の軸力を変えて、ケースごとに違う曲線にする
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
                return;
            }

            var before = CurvesPerCase(vm.CurrentModel!);
            Assert.IsTrue(before.Count >= 2, "(前提) M–θ 曲線の表が 2 ケース以上ありません");
            Assert.IsTrue(before.Values.Select(c => string.Join(";", c)).Distinct().Count() >= 2,
                "(前提) ケースごとに M–θ 曲線が違いません (軸力が同じ?)。この検査が成立していません");

            // 保存して読み直す (本番と同じ設定)
            var options = (JsonSerializerOptions)typeof(MainWindowViewModel)
                .GetField("_jsonOptions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var service = new FileOperationService(options);
            string path = Path.Combine(Path.GetTempPath(), $"MThetaReload_{Guid.NewGuid():N}.pdj");
            try
            {
                service.SaveProjectData(path, vm.CurrentInputModel!, vm.CurrentModel);
                var loaded = service.LoadProjectData(path).AnaModel;
                var after = CurvesPerCase(loaded);

                foreach (var (key, curve) in before)
                {
                    if (!after.TryGetValue(key, out var reloaded))
                    {
                        Assert.Fail($"{key}: 読み直したあと、このケースの M–θ 曲線の表がありません");
                        continue;
                    }
                    Assert.AreEqual(curve.Count, reloaded.Count, $"{key}: 読み直すと曲線の点の数が変わります (別のケースの曲線?)");
                    for (int i = 0; i < curve.Count; i++)
                        Assert.AreEqual(curve[i].M, reloaded[i].M, 1e-6 * Math.Max(1, Math.Abs(curve[i].M)),
                            $"{key}: 読み直すと M–θ 曲線が変わります (点 {i + 1})。別のケースの曲線が出ています");
                }
            }
            finally
            {
                File.Delete(path);
            }

            // 控えの無いケース (控えを保存するようにする前のファイル) は、別のケースの曲線を出さず、そう知らせること
            foreach (var rs in vm.CurrentModel!.RotationalSprings) rs.CaseMThetaSnapshots.Clear();
            var last = vm.CurrentModel.AnalysisStepResults.OrderBy(s => s.Step).Last();
            var table = new AnalysisResultTableService().BuildTables(vm.CurrentModel, last.LoadCase, last.LoadCombination, last.IsLiquefaction, last.Step)
                .First(t => t.Category == "MThetaCurve");
            var rows = table.Rows.OfType<MThetaCurveRow>().ToList();
            Assert.IsFalse(rows.Any(r => r.PointIndex >= 1), "控えの無いケースに、ばね本体の (別のケースかもしれない) 曲線を出しています");
            Assert.IsTrue(rows.Any(r => r.PointIndex == 0 && r.Status.Contains("保存されていません", StringComparison.Ordinal)),
                "控えの無いケースで、曲線が無いことを知らせていません");
        }
    }
}
