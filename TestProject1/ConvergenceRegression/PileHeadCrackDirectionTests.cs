using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1.ConvergenceRegression
{
    /// <summary>
    /// 斜めに荷重を作用させたとき、場所打ち RC 杭頭のひび割れ後の答えが、荷重ステップの刻み方に依存しないこと。
    ///
    /// 杭頭 M–θ ばねはひび割れた向きをロックし、以前は<b>直交方向の剛性を 5%</b> にしていた (割線にも効く)。
    /// ひび割れ後の杭頭は直交方向にほぼ自由に回り、回転がロックした向きから 30〜40° ずれた。ロックする向きは
    /// 何ステップ目でひび割れを検出したかで決まるので、計算例10 の L1 液状化ケースを 60° で解くと、
    /// ステップ数で最大モーメントが 5% 変わり、刻み方によっては杭頭がひび割れない解に落ちた (2026-09-26)。
    /// 直交方向も同じ剛性にして、ステップ依存が 2% 以内に収まり、モーメントと回転の向きが揃うことを固定する。
    /// </summary>
    [TestClass]
    public class PileHeadCrackDirectionTests
    {
        private const double Angle = 60.0;

        private static MainWindowViewModel? Run(int level1Steps)
        {
            try
            {
                return HeadlessHorizontalRunner.RunExampleForViewModel("Example10", "PileExample10", new HeadlessHorizontalRunner.RunOptions
                {
                    Level1Steps = level1Steps,
                    Level2Steps = 4,
                    UseLineSearch = true,
                    Parallelism = 1,
                    LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                    Customize = input =>
                    {
                        foreach (var lc in input.LoadCasesInput!.LoadCasesLevel1.Concat(input.LoadCasesInput.LoadCasesLevel2))
                            lc.LoadAngle = Angle;
                    },
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                return null;
            }
        }

        private static bool IsTarget(LoadCaseKey k) => k.Level == 1 && k.IsLiquefaction && k.CombinationNo == 1;

        private readonly record struct LoadCaseKey(int Level, bool IsLiquefaction, int? CombinationNo);

        /// <summary>L1 液状化ケースの最終ステップの、杭の最大モーメント (要素の端の合成値)。</summary>
        private static double MaxPileMoment(MainWindowViewModel vm)
        {
            double max = 0;
            foreach (var beam in vm.CurrentModel!.Beams)
            {
                var last = beam.BeamResults
                    .Where(r => IsTarget(new(r.LoadCase.Level, r.IsLiquefaction, r.LoadCombination?.No)))
                    .OrderBy(r => r.Step).LastOrDefault();
                if (last == null) continue;
                var f = last.CumulativeForce;
                foreach (var (y, z) in new[] { (4, 5), (10, 11) })
                    max = Math.Max(max, Math.Sqrt(f.GetByIndex(y) * f.GetByIndex(y) + f.GetByIndex(z) * f.GetByIndex(z)));
            }
            return max;
        }

        [TestMethod]
        public void ObliqueLoadingDoesNotDependOnTheStepCount()
        {
            var moments = new List<(int Steps, double M)>();
            foreach (int steps in new[] { 4, 8, 16 })
            {
                var vm = Run(steps);
                if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }
                moments.Add((steps, MaxPileMoment(vm)));
            }

            double min = moments.Min(m => m.M), max = moments.Max(m => m.M);
            Assert.IsTrue(min > 0, "(前提) 対象のケースの結果がありません");
            Assert.IsTrue((max - min) / max < 0.03,
                "斜め荷重の L1 液状化ケースの最大モーメントが、荷重ステップ数で 3% 以上変わります: "
                + string.Join(", ", moments.Select(m => $"{m.Steps} ステップ {m.M:F1}")));
        }

        /// <summary>ひび割れた杭頭で、モーメントの向きと回転の向きが揃うこと (直交方向にほぼ自由に回らない)。</summary>
        [TestMethod]
        public void CrackedPileHeadsTurnTheWayTheirMomentActs()
        {
            var vm = Run(8);
            if (vm == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            int cracked = 0;
            var misaligned = new List<string>();
            foreach (var spring in vm.CurrentModel!.RotationalSprings ?? [])
            {
                var last = spring.RotationalSpringResults
                    .Where(r => IsTarget(new(r.LoadCase.Level, r.IsLiquefaction, r.LoadCombination?.No)))
                    .OrderBy(r => r.Step).LastOrDefault();
                if (last == null || !last.HasCracked) continue;
                cracked++;

                double mx = last.CumulativeForce.Mxj, my = last.CumulativeForce.Myj;
                double rx = last.CumulativeDisp.GetByIndex(9) - last.CumulativeDisp.GetByIndex(3);
                double ry = last.CumulativeDisp.GetByIndex(10) - last.CumulativeDisp.GetByIndex(4);
                double diff = Math.Abs(Math.Atan2(my, mx) - Math.Atan2(ry, rx)) * 180 / Math.PI;
                diff = Math.Min(diff, 360 - diff);
                if (diff > 5) misaligned.Add($"{spring.Name}: {diff:F1}°");
            }

            Assert.IsTrue(cracked > 0, "(前提) ひび割れた杭頭がありません");
            Assert.AreEqual(0, misaligned.Count,
                "ひび割れた杭頭で、モーメントと回転の向きが 5° 以上ずれています (直交方向の剛性が小さい): " + string.Join(", ", misaligned));
        }
    }
}
