using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 解析の出口側の不変条件: モデル全体を平面で平行移動しても、検定の結果 (全項目の検定比・支配ケース) は変わらない。
    ///
    /// <para>杭・地盤・荷重の関係は相対的な位置だけで決まるので、座標の原点をどこに置いても結果は同じはず。
    /// 非線形のままで成り立つ (線形域に限る反転・比例 (<see cref="AnalysisOutputInvariantTests"/>) より強い)。
    /// 作用点や剛体の中心を原点基準で決めている、座標の一部 (通り心・基礎梁の節点) だけをずらし忘れた計算がある、
    /// といった誤りを、正解の値を用意せずに捕まえる。</para>
    ///
    /// <para>座標の丸めの違いで非線形の反復の経路がわずかに変わりうるので、検定比は相対 1e-4 まで許す。</para>
    /// </summary>
    [TestClass]
    public class AnalysisTranslationInvarianceTests
    {
        private const double Dx = 37.5, Dy = -12.25;

        /// <summary>
        /// 平面の座標を持つ入力をすべてずらす。同じ物は 1 回だけ。
        /// 節点・杭配置・基礎梁の節点・通り心に加え、荷重の作用点 (荷重ケースごとと共通の設定) と、根入部・土圧合力ばねの範囲。
        /// どれも利用者が絶対座標で入れる値なので、平行移動ならいっしょに動かす。
        /// </summary>
        internal static void Translate(InputModel model, double dx, double dy)
        {
            var moved = new HashSet<object>(ReferenceEqualityComparer.Instance);
            void Move(InputNode n) { if (moved.Add(n)) { n.X += dx; n.Y += dy; } }

            foreach (var n in model.InputNodes ?? []) Move(n);
            foreach (var p in model.PileLayoutItems ?? []) Move(p);
            foreach (var f in model.FoundationBeamInput?.Nodes ?? [])
                if (moved.Add(f)) { f.X += dx; f.Y += dy; }
            foreach (var g in model.GridXItems ?? []) if (moved.Add(g)) g.Coord += dx;
            foreach (var g in model.GridYItems ?? []) if (moved.Add(g)) g.Coord += dy;

            foreach (var lc in model.LoadCasesInput?.AllLoadCases ?? [])
                if (moved.Add(lc)) { lc.ForceActionPointX += dx; lc.ForceActionPointY += dy; }
            foreach (var common in new[] { model.LoadCasesInput?.LoadCaseLevel1Common, model.LoadCasesInput?.LoadCaseLevel2Common })
                if (common != null && moved.Add(common)) { common.ForceActionPointX += dx; common.ForceActionPointY += dy; }

            foreach (var e in model.EmbedmentInput?.EmbedmentLayers ?? [])
                if (moved.Add(e)) { e.X1 += dx; e.X2 += dx; e.Y1 += dy; e.Y2 += dy; }
            foreach (var d in model.ElementDivision?.DoatsuGoryokuBane?.Items ?? [])
                if (moved.Add(d)) { d.X1 += dx; d.X2 += dx; d.Y1 += dy; d.Y2 += dy; }
            TestSource.AssertScanned(moved.Count, 2, "ずらした座標");
        }

        private static string Identity(EvaluationItem i)
            => $"{i.Category} | {i.TargetDescription} | L{i.Level} {i.LoadCaseName} | {i.LoadCombinationName} | {i.LiquefactionLabel}";

        private static EvaluationResult Evaluate(string ground, string pile, Action<InputModel>? customize)
        {
            MainWindowViewModel vm;
            try
            {
                vm = HeadlessHorizontalRunner.RunExampleForViewModel(ground, pile, new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = 4,
                    Level2Steps = 8,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    UseLineSearch = true,
                    Parallelism = 1,
                    Customize = customize,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                throw new AssertInconclusiveException(ex.Message);
            }
            vm.ApplyConcreteModelOptions();
            return EvaluationService.BuildEvaluationResult(vm, factored: true);
        }

        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]   // 場所打ち RC 杭 18 本
        [DataRow("ExampleK8", "PileExampleK8")] // 関東支部 計算例8
        [DataRow("Example3_4", "PileExample3_4")]
        [DataRow("Example3_8_1", "PileExample3_8")]
        public void TranslatingTheWholeModel_DoesNotChangeTheEvaluation(string ground, string pile)
        {
            var original = Evaluate(ground, pile, null);
            var moved = Evaluate(ground, pile, m => Translate(m, Dx, Dy));

            var before = original.Items.Where(i => i.IsJudged).ToDictionary(Identity, i => i.Ratio);
            var after = moved.Items.Where(i => i.IsJudged).ToDictionary(Identity, i => i.Ratio);
            TestSource.AssertScanned(before.Count, 100, "検定の項目");

            var problems = new List<string>();
            if (before.Count != after.Count) problems.Add($"検定の項目の数: {before.Count} → {after.Count}");
            // 杭頭変形角は杭の組ごとの最大の組を 1 つ出す。変形角が同じ値の組が複数あると、どの組を出すかは
            // 座標の丸めで入れ替わりうる (値は同じ)。組の名前が違っても、同じ検定・荷重条件で同じ検定比の項目があればよい
            static string WithoutTarget(string key)
            {
                var parts = key.Split(" | ");
                return string.Join(" | ", parts.Where((_, i) => i != 1));
            }
            foreach (var (key, ratio) in before)
            {
                if (!after.TryGetValue(key, out var r))
                {
                    bool sameValueElsewhere = after.Any(a => WithoutTarget(a.Key) == WithoutTarget(key)
                        && Math.Abs(a.Value - ratio) <= 1e-4 * Math.Max(Math.Abs(ratio), 1e-3));
                    if (!sameValueElsewhere) problems.Add($"平行移動すると無くなった: {key}");
                    continue;
                }
                if (!(Math.Abs(r - ratio) <= 1e-4 * Math.Max(Math.Abs(ratio), 1e-3)))
                    problems.Add($"{key}: 検定比 {ratio:F6} → {r:F6}");
            }
            if (original.Governing is { } g && moved.Governing is { } h && Identity(g) != Identity(h)
                && Math.Abs((g.Ratio) - (h.Ratio)) > 1e-4 * Math.Abs(g.Ratio))
                problems.Add($"支配ケース: {Identity(g)} → {Identity(h)}");

            Assert.AreEqual(0, problems.Count,
                $"[{ground}] モデルを平面で ({Dx}, {Dy}) m ずらすと検定の結果が変わりました ({problems.Count} 件):\n  "
                + string.Join("\n  ", problems.Take(20)));
        }
    }
}
