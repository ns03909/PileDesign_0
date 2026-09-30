using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// せん断の限界値の根拠 (式と係数の内訳) を、断面の種類ごとに持つこと。内訳の値が、検定が使う Q-N 曲線と一致すること。
    ///
    /// <para>内訳の値は曲線の点を作るのと同じ関数で求めている (写しを持たない)。ここでは曲線の点のあいだ
    /// (検定が補間する位置) で、式で求めた値と曲線の補間値を突き合わせる。曲線が軸力について直線でない断面
    /// (既製杭の斜めひび割れ・鋼管の軸力の項) では補間の誤差の分だけ違うので、その幅を許す。</para>
    /// </summary>
    [TestClass]
    public class ShearLimitBasisTests
    {
        [TestInitialize]
        public void Init()
        {
            ConcreteModelOptions.UseNotification1113Shear = false;
            ConcreteModelOptions.UseInsituUltimateEFunction = false;
        }

        [TestCleanup]
        public void Cleanup() => Init();

        /// <summary>代表断面 (断面の種類ごと) と、場所打ち RC 杭の工法 2 種 × 算定式の選び方。</summary>
        private static IEnumerable<(string Name, PileSection Section)> Sections()
        {
            foreach (var (name, recipe) in SectionInvariantTests.Recipes)
                yield return (name, recipe());

            foreach (var (method, damage, ultimate) in new[]
            {
                (ShearReinforcementMethods.MkPileRing785, ShearReinforcementMethods.DamageFormulaDamageLimit, ShearReinforcementMethods.UltimateFormulaArakawa),
                (ShearReinforcementMethods.MkPileRing785, ShearReinforcementMethods.DamageFormulaSafetyShortTerm, ShearReinforcementMethods.UltimateFormulaArakawa),
                (ShearReinforcementMethods.UlbonSpiral, ShearReinforcementMethods.DamageFormulaDamageLimit, ShearReinforcementMethods.UltimateFormulaTrussArch),
            })
            {
                var s = SectionInvariantTests.Recipes[PileTypeNames.RcSection]();
                s.HoopMethod = method;
                s.HoopDamageFormula = damage;
                s.HoopUltimateFormula = ultimate;
                yield return ($"{PileTypeNames.RcSection} ({method}・{damage}・{ultimate})", s);
            }
        }

        [TestMethod]
        public void EverySectionType_DescribesItsShearLimit_AndMatchesTheCurve()
        {
            int compared = 0;
            var problems = new List<string>();
            foreach (var (name, section) in Sections())
            {
                foreach (int damageLevel in new[] { 1, 2 })
                {
                    const double monQd = 2.5;
                    var curves = section.GetQNCurvesForLevel(damageLevel, monQd);
                    foreach (var (limit, factored, curve) in new[]
                    {
                        (SectionLimitState.Service, false, curves.UnfactoredService),
                        (SectionLimitState.Service, true, curves.FactoredService),
                        (SectionLimitState.Damage, false, curves.UnfactoredDamage),
                        (SectionLimitState.Damage, true, curves.FactoredDamage),
                        (SectionLimitState.Ultimate, false, curves.UnfactoredUltimate),
                        (SectionLimitState.Ultimate, true, curves.FactoredUltimate),
                    })
                    {
                        if (curve.N == null || curve.N.Count < 4) { problems.Add($"{name} {limit}: 曲線がありません"); continue; }
                        // 曲線の点のあいだ (検定が補間する位置) を 3 か所。端と段差の点は避ける
                        foreach (int at in new[] { curve.N.Count / 4, curve.N.Count / 2, curve.N.Count * 3 / 4 })
                        {
                            double n = (curve.N[at] + curve.N[at + 1]) / 2;
                            if (Math.Abs(curve.N[at + 1] - curve.N[at]) < 1e-9) continue;
                            double limitOnCurve = PileSection.InterpolateLimitAtAxialForce(curve.N, curve.Q, n);
                            var basis = section.DescribeShearLimit(limit, damageLevel, factored, monQd, n);
                            string where = $"{name} {limit} {(factored ? "低減後" : "低減前")} L{damageLevel} N={n:N1}kN";
                            if (basis == null) { problems.Add($"{where}: 内訳がありません"); continue; }
                            if (string.IsNullOrWhiteSpace(basis.Formula) || basis.Terms.Count < 3)
                                problems.Add($"{where}: 式か係数が足りません ({basis.Terms.Count} 項)");
                            double fromFormula = basis.ValueN / 1000.0;
                            double tolerance = 5e-3 * Math.Abs(limitOnCurve) + 1e-3;
                            if (!(Math.Abs(fromFormula - limitOnCurve) <= tolerance))
                                problems.Add($"{where}: 式で求めた値 {fromFormula:N3} kN が曲線の補間値 {limitOnCurve:N3} kN と違います ({basis.Formula})");
                            compared++;
                        }
                    }
                }
            }
            TestSource.AssertScanned(compared, 200, "式と曲線を突き合わせた点");
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems.Take(30)));
        }

        /// <summary>告示1113号のせん断を選んだときは、場所打ち RC 杭の内訳も告示の式になる。</summary>
        [TestMethod]
        public void Notification1113Shear_IsDescribedAsTheNotificationFormula()
        {
            ConcreteModelOptions.UseNotification1113Shear = true;
            var s = SectionInvariantTests.Recipes[PileTypeNames.RcSection]();
            var service = s.DescribeShearLimit(SectionLimitState.Service, 1, true, 2.5, 1000);
            var damage = s.DescribeShearLimit(SectionLimitState.Damage, 2, true, 2.5, 1000);
            StringAssert.Contains(service!.Formula, "告示1113号");
            StringAssert.Contains(damage!.Formula, "1.5·fs");
            Assert.AreEqual(1.5, damage.ValueN / service.ValueN, 1e-9, "短期は長期の 1.5 倍");
        }
    }
}
