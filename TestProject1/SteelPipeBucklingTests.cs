using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 鋼管杭の座屈長（液状化区間の長さ）と、そこから求める許容曲げ座屈応力度 sfc2。
    ///
    /// 出典: 日本建築学会「基礎部材の強度と変形性能」
    ///   解説図 8.3 … 座屈長は液状化区間（β &lt; 1 の範囲）の長さ。連続する場合はその合計
    ///   (8.10)〜(8.12) … sfc2 の算定式
    ///
    /// <b>いちばん大事な性質</b>は「液状化しなければ何も変わらない」こと。
    /// 座屈長 0 → λc = 0 → sfc2 = F/1.5 となり、局部座屈側の sfc1 (≤ F/1.5) が必ず支配する。
    /// これが崩れると、液状化を検討していない全モデルの耐力が動く。
    /// </summary>
    [TestClass]
    public class SteelPipeBucklingTests
    {
        // ── 土質点の作り方 ───────────────────────────────────────

        /// <summary>
        /// 層厚 h (m)、レベル 1/2 の β を持つ土質点。β = null は液状化対象外。
        ///
        /// <c>IsLiquefactionLayer</c> を立てるのは「液状化の判定を行った地盤」を模すため。
        /// 判定していない地盤では BetaL が初期値 <c>[0.0, 0.0]</c> のまま残る
        /// (<see cref="UnassessedGround_IsNotTreatedAsLiquefied"/>)。
        /// </summary>
        private static GroundMassDataInput Mass(double h, double? betaL1, double? betaL2) => new()
        {
            H = h,
            IsLiquefactionLayer = true,
            BetaL = new ObservableCollection<double?> { betaL1, betaL2 },
        };

        private const int L1 = 0;
        private const int L2 = 1;

        private static double Lk(IReadOnlyList<GroundMassDataInput> masses, int level,
            double groundTop = 0.0, double pileTop = 0.0, double pileBottom = -100.0)
            => SteelPipeBuckling.ComputeBucklingLength(masses, groundTop, level, pileTop, pileBottom);

        // ── 座屈長: 数え方 ───────────────────────────────────────

        [TestMethod]
        public void NoLiquefaction_GivesZeroLength()
        {
            var masses = new List<GroundMassDataInput>
            {
                Mass(3.0, null, null),
                Mass(4.0, 1.0, 1.0),   // 液状化対象層だが FL ≥ 1 で低減なし
                Mass(5.0, null, null),
            };

            Assert.AreEqual(0.0, Lk(masses, L1), 1e-9);
            Assert.AreEqual(0.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void ConsecutiveLiquefiedLayers_AreSummed()
        {
            // 解説図 8.3「連続する場合はその合計」
            var masses = new List<GroundMassDataInput>
            {
                Mass(2.0, null, null),
                Mass(3.0, 0.5, 0.5),
                Mass(4.0, 0.0, 0.0),
                Mass(5.0, null, null),
            };

            Assert.AreEqual(7.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void SeparatedRuns_TakeTheLongest()
        {
            // 支えのある層が挟まれば区間は切れる。長い方が先に座屈する
            var masses = new List<GroundMassDataInput>
            {
                Mass(3.0, 0.5, 0.5),   // 区間 A = 3 m
                Mass(2.0, 1.0, 1.0),   // 支えあり → 区間が切れる
                Mass(4.0, 0.5, 0.5),   // 区間 B = 4 + 2 = 6 m
                Mass(2.0, 0.2, 0.2),
                Mass(5.0, null, null),
            };

            Assert.AreEqual(6.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void LevelsAreIndependent()
        {
            // レベル1 では液状化しない層が、レベル2 では液状化する
            var masses = new List<GroundMassDataInput>
            {
                Mass(3.0, 1.0, 0.5),
                Mass(4.0, 0.5, 0.5),
            };

            Assert.AreEqual(4.0, Lk(masses, L1), 1e-9);
            Assert.AreEqual(7.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void BetaExactlyOne_IsNotLiquefied()
        {
            var masses = new List<GroundMassDataInput> { Mass(5.0, 1.0, 1.0) };
            Assert.AreEqual(0.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void BetaJustBelowOne_IsLiquefied()
        {
            var masses = new List<GroundMassDataInput> { Mass(5.0, 0.9, 0.9) };
            Assert.AreEqual(5.0, Lk(masses, L2), 1e-9);
        }

        // ── 座屈長: 杭の範囲で切る ─────────────────────────────

        [TestMethod]
        public void OnlyThePartThePilePassesThroughCounts()
        {
            // 地表 Z=0 から 液状化 10 m。杭は Z=-4 から Z=-8 までしか無い → 4 m
            var masses = new List<GroundMassDataInput> { Mass(10.0, 0.5, 0.5), Mass(5.0, null, null) };

            Assert.AreEqual(4.0, Lk(masses, L2, groundTop: 0.0, pileTop: -4.0, pileBottom: -8.0), 1e-9);
        }

        [TestMethod]
        public void LiquefiedLayerBelowThePileToe_DoesNotCount()
        {
            // 杭先端 Z=-5。液状化は Z=-10 より下 → 杭は通らない
            var masses = new List<GroundMassDataInput>
            {
                Mass(10.0, null, null),
                Mass(6.0, 0.5, 0.5),
            };

            Assert.AreEqual(0.0, Lk(masses, L2, groundTop: 0.0, pileTop: 0.0, pileBottom: -5.0), 1e-9);
        }

        [TestMethod]
        public void PileEndingInsideTheLiquefiedRun_CountsOnlyItsPart()
        {
            // 液状化 Z=-2 〜 -12 (10 m)、杭先端 Z=-7 → 5 m
            var masses = new List<GroundMassDataInput>
            {
                Mass(2.0, null, null),
                Mass(10.0, 0.5, 0.5),
            };

            Assert.AreEqual(5.0, Lk(masses, L2, groundTop: 0.0, pileTop: 0.0, pileBottom: -7.0), 1e-9);
        }

        // ── 座屈長: 入力の欠けに耐える ─────────────────────────

        [TestMethod]
        public void MissingThickness_DoesNotBreakTheRun()
        {
            // 層厚が入っていない土質点は範囲を持たない。区間の連続性は β で決める
            var masses = new List<GroundMassDataInput>
            {
                Mass(3.0, 0.5, 0.5),
                new() { H = null, IsLiquefactionLayer = true,
                        BetaL = new ObservableCollection<double?> { 0.5, 0.5 } },
                Mass(4.0, 0.5, 0.5),
            };

            Assert.AreEqual(7.0, Lk(masses, L2), 1e-9);
        }

        /// <summary>
        /// <b>液状化の判定を行っていない地盤を「全層液状化」と読まないこと。</b>
        ///
        /// <c>GroundMassDataInput.BetaL</c> の初期値は null ではなく <c>[0.0, 0.0]</c>。
        /// β &lt; 1 だけで判定すると、判定前の地盤で杭全長が座屈長になり、
        /// 液状化を検討していないモデルの耐力が静かに落ちる。
        /// </summary>
        [TestMethod]
        public void UnassessedGround_IsNotTreatedAsLiquefied()
        {
            // 既定のまま (IsLiquefactionLayer = false、BetaL = [0,0]) の土質点
            var masses = new List<GroundMassDataInput>
            {
                new() { H = 5.0 },
                new() { H = 5.0 },
            };
            Assert.AreEqual(0.0, masses[0].BetaL[0], "前提: BetaL の初期値は 0");
            Assert.IsFalse(masses[0].IsLiquefactionLayer, "前提: 判定前は液状化対象層ではない");

            Assert.AreEqual(0.0, Lk(masses, L1), 1e-9);
            Assert.AreEqual(0.0, Lk(masses, L2), 1e-9);
        }

        /// <summary>β = 0 は「ゆるくて完全に低減」という正当な値なので、除外してはいけない。</summary>
        [TestMethod]
        public void AssessedBetaZero_IsLiquefied()
        {
            var masses = new List<GroundMassDataInput> { Mass(5.0, 0.0, 0.0) };
            Assert.AreEqual(5.0, Lk(masses, L2), 1e-9);
        }

        [TestMethod]
        public void EmptyOrNullInput_GivesZero()
        {
            Assert.AreEqual(0.0, SteelPipeBuckling.ComputeBucklingLength(null!, 0, L2, 0, -10), 1e-9);
            Assert.AreEqual(0.0, Lk(new List<GroundMassDataInput>(), L2), 1e-9);
        }

        [TestMethod]
        public void PileWithNoExtent_GivesZero()
        {
            var masses = new List<GroundMassDataInput> { Mass(10.0, 0.5, 0.5) };
            Assert.AreEqual(0.0, Lk(masses, L2, pileTop: -5.0, pileBottom: -5.0), 1e-9);
        }

        [TestMethod]
        public void BetaListShorterThanTheLevel_IsTreatedAsNotLiquefied()
        {
            var masses = new List<GroundMassDataInput>
            {
                new() { H = 5.0, IsLiquefactionLayer = true,
                        BetaL = new ObservableCollection<double?> { 0.5 } },   // L1 のみ
            };

            Assert.AreEqual(5.0, Lk(masses, L1), 1e-9);
            Assert.AreEqual(0.0, Lk(masses, L2), 1e-9);
        }

        // ── sfc2 ───────────────────────────────────────

        private static SteelPipeSection Section(double D, double t, double F, double bucklingLength)
        {
            var ctor = typeof(SteelPipeSection).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                [typeof(double), typeof(double), typeof(double), typeof(double),
                 typeof(double), typeof(double), typeof(double), typeof(double)], null)!;
            return (SteelPipeSection)ctor.Invoke([D, t, F, 1.0, 0.0, 0.0, 205000.0, bucklingLength]);
        }

        [TestMethod]
        public void NoBucklingLength_MeansNoReduction()
        {
            // これが崩れると、液状化を検討していない全モデルの耐力が動く
            foreach (var (D, t) in new[] { (600.0, 12.0), (1000.0, 10.0), (600.0, 24.0) })
            {
                var s = Section(D, t, 235, bucklingLength: 0.0);
                Assert.AreEqual(235.0 / 1.5, s.Sfc2, 1e-9, $"D={D}, t={t}");
                Assert.IsTrue(s.Sfc1 <= s.Sfc2 + 1e-9,
                    $"D={D}, t={t}: 局部座屈側が支配しなくなっています (sfc1={s.Sfc1}, sfc2={s.Sfc2})");
            }
        }

        [TestMethod]
        public void LongerBucklingLength_GivesLowerAllowableStress()
        {
            double prev = double.MaxValue;
            foreach (double lk in new[] { 1.0, 3.0, 5.0, 10.0, 20.0 })
            {
                double sfc2 = Section(600, 12, 235, lk).Sfc2;
                Assert.IsTrue(sfc2 < prev, $"lk={lk} で sfc2 が減っていません ({sfc2} vs {prev})");
                Assert.IsTrue(sfc2 > 0, $"lk={lk} で sfc2 が 0 以下です");
                prev = sfc2;
            }
        }

        [TestMethod]
        public void Sfc2_MatchesTheFormula()
        {
            // (8.10)〜(8.12) を独立に計算して突き合わせる
            const double D = 600, t = 12, F = 235, E = 205000, lk = 8.0;
            var s = Section(D, t, F, lk);

            double sAp = Math.PI * (D * D - (D - 2 * t) * (D - 2 * t)) / 4.0;
            double iSteel = Math.PI / 64.0 * (Math.Pow(D, 4) - Math.Pow(D - 2 * t, 4));
            double nc = Math.PI * Math.PI * E * iSteel / Math.Pow(lk * 1000.0, 2);
            double lambdaC = Math.Sqrt(F * sAp / nc);
            double eLambdaC = 1.0 / Math.Sqrt(0.6);
            double r = lambdaC / eLambdaC;
            double nu = 1.5 + (2.0 / 3.0) * r * r;
            double expected = lambdaC <= eLambdaC ? (1 - 0.4 * r * r) * F / nu : F / (nu * lambdaC * lambdaC);

            Assert.AreEqual(Math.Min(expected, F / 1.5), s.Sfc2, 1e-9);
        }

        [TestMethod]
        public void Sfc2_IsContinuousAtTheElasticLimitSlenderness()
        {
            // λc = eλc で (8.10) と (8.11) が一致すること。
            // 境界で跳ぶと、液状化区間の長さがわずかに違うだけで耐力が段差を持つ。
            //   λc = eλc となる lk を逆算する: λc² = Ny/Nc = Ny·lk²/(π²EI) = 1/0.6
            const double D = 600, t = 12, F = 235, E = 205000;
            double sAp = Math.PI * (D * D - (D - 2 * t) * (D - 2 * t)) / 4.0;
            double iSteel = Math.PI / 64.0 * (Math.Pow(D, 4) - Math.Pow(D - 2 * t, 4));
            double lkBoundary_mm = Math.Sqrt(Math.PI * Math.PI * E * iSteel / (0.6 * F * sAp));
            double lk = lkBoundary_mm / 1000.0;

            double below = Section(D, t, F, lk * 0.999).Sfc2;
            double above = Section(D, t, F, lk * 1.001).Sfc2;

            Assert.AreEqual(below, above, below * 5e-3,
                $"λc = eλc の境界で sfc2 が跳んでいます (下 {below:F3} / 上 {above:F3}、lk={lk:F2} m)");
            // 参考: 境界での値は 0.277·F (鋼構造の限界細長比における値と一致する)
            Assert.AreEqual(0.277 * F, below, 0.005 * F);
        }

        [TestMethod]
        public void BucklingReducesTheCompressionCapacity()
        {
            // sfc2 が下がると損傷限界・安全限界の圧縮軸力が下がる (min を取るため)
            var without = Section(600, 12, 235, 0.0);
            var with = Section(600, 12, 235, 10.0);

            Assert.AreEqual(without.sNdc1, without.Ndc, 1e-6, "液状化なしでは局部座屈側が支配するはず");
            Assert.IsTrue(with.Ndc < without.Ndc, "座屈を考慮しても損傷限界圧縮軸力が下がっていません");
            Assert.IsTrue(with.sNuc < without.sNuc, "座屈を考慮しても安全限界圧縮軸力が下がっていません");
        }
    }
}
