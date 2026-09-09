using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 場所打ち RC 杭のせん断 Q-N 曲線を数値で固定する。
    ///
    /// 3 つの限界状態が、等価な幅 b = πD/4・有効せい d = 0.9D・応力中心距離 j = (7/8)d と
    /// 断面形状係数 kc = 0.72 を<b>それぞれ書き写して</b>いた。1 か所にまとめたが、
    /// まとめ方を間違えても例外にはならず、曲線の高さが変わるだけで気づけない。
    ///
    /// 期待値はまとめる前のコードから取った実測値。
    /// </summary>
    [TestClass]
    public class InsituRcShearQNTests
    {
        private static InsituReinforcedConcreteSection Calc()
        {
            var s = new PileSection
            {
                PileBodyType = PileTypeNames.InsituRc,
                PileSectionType = PileTypeNames.RcSection,
                ConcreteOutDia = 1000.0, ConcreteFc = 27.0, ConcreteGsi = 0.75,
                MainBarNum = 20, MainBarSize = "D25", MainBarSpec = "SD390", MainBarDr = 600.0,
                HoopSize = "D13", HoopSpacing = 150.0, HoopSpec = "SD295", HoopCenterCover = 150.0,
                PileDiameter = 1000.0,
            };
            var calc = s.CreateSectionCalculator() as InsituReinforcedConcreteSection;
            Assert.IsNotNull(calc, "場所打ち RC の断面計算が作れません");
            return calc!;
        }

        private static void Curve(
            (List<double> Qs, List<double> Ns) c,
            int count, double q0, double qMid, double n0, double nLast, string what)
        {
            Assert.AreEqual(count, c.Qs.Count, $"{what}: 点数");
            Assert.AreEqual(count, c.Ns.Count, $"{what}: せん断力と軸力で点数が違う");
            Assert.AreEqual(q0, c.Qs[0], $"{what}: 先頭のせん断力");
            Assert.AreEqual(qMid, c.Qs[count / 2], $"{what}: 中央のせん断力");
            Assert.AreEqual(n0, c.Ns[0], $"{what}: 先頭の軸力");
            Assert.AreEqual(nLast, c.Ns[count - 1], $"{what}: 末尾の軸力");
        }

        [TestMethod]
        public void TheShearCurves_KeepTheirValues()
        {
            var s = Calc();
            const double n0 = -883203.0457934595;
            const double nLast = 6986136.092226266;

            Curve(s.GetServiceLimitQNInteraction(3.0, false), 100,
                264742.55452046875, 352869.1856827618, n0, nLast, "使用限界");
            Curve(s.GetServiceLimitQNInteraction(3.0, true), 100,
                238268.29906842185, 317582.26711448556, n0, nLast, "使用限界(低減後)");

            Curve(s.GetDamageLimitQNInteraction(3.0, false, 1), 100,
                397113.8317807031, 529303.7785241426, n0, nLast, "損傷限界 L1");
            Curve(s.GetDamageLimitQNInteraction(3.0, true, 1), 100,
                357402.44860263285, 476373.4006717284, n0, nLast, "損傷限界 L1(低減後)");
            Curve(s.GetDamageLimitQNInteraction(3.0, true, 2), 102,
                268051.8364519746, 359064.61478483275, n0, nLast, "損傷限界 L2(低減後)");

            Curve(s.GetUltimateQNInteraction(3.0, 0.004, 295.0, false), 100,
                818301.2213933341, 1100105.7639742296, n0, nLast, "安全限界");
            Curve(s.GetUltimateQNInteraction(3.0, 0.004, 295.0, true), 102,
                490980.7328360006, 663445.1128955086, n0, nLast, "安全限界(低減後)");
        }

        /// <summary>
        /// 低減後の損傷限界 L2 と安全限界に、β2 が切り替わる段差の 2 点が入ること。
        ///
        /// 同じ軸力の点を 2 つ入れて段差を垂直に描いている。入っていないと段差が
        /// 斜めにつながり、曲線としては成立するので気づけない。
        /// </summary>
        [TestMethod]
        public void TheFactoredCurves_CarryTheBetaStep()
        {
            var s = Calc();

            foreach (var (name, c) in new (string, (List<double> Qs, List<double> Ns))[]
            {
                ("損傷限界 L2", s.GetDamageLimitQNInteraction(3.0, true, 2)),
                ("安全限界", s.GetUltimateQNInteraction(3.0, 0.004, 295.0, true)),
            })
            {
                Assert.AreEqual(102, c.Ns.Count, $"{name}: 段差の 2 点が入っていない");

                var repeated = c.Ns.GroupBy(n => n).Where(g => g.Count() == 2).ToList();
                Assert.AreEqual(1, repeated.Count,
                    $"{name}: 同じ軸力の点が 1 組だけあるはず (段差の位置)");

                int at = c.Ns.IndexOf(repeated[0].Key);
                Assert.AreNotEqual(c.Qs[at], c.Qs[at + 1],
                    $"{name}: 段差の 2 点でせん断力が同じです。β2 が切り替わっていません");
            }
        }

        /// <summary>
        /// 低減前の曲線には段差を入れないこと。β を掛けないので切り替わりが無い。
        /// </summary>
        [TestMethod]
        public void TheUnfactoredCurves_HaveNoStep()
        {
            var s = Calc();

            foreach (var (name, c) in new (string, (List<double> Qs, List<double> Ns))[]
            {
                ("損傷限界 L2", s.GetDamageLimitQNInteraction(3.0, false, 2)),
                ("安全限界", s.GetUltimateQNInteraction(3.0, 0.004, 295.0, false)),
            })
            {
                Assert.AreEqual(100, c.Ns.Count, $"{name}: 低減前に段差が入っている");
                Assert.AreEqual(100, c.Ns.Distinct().Count(), $"{name}: 軸力が重複している");
            }
        }

        /// <summary>
        /// せん断の幾何が 1 か所だけで定義されていること。
        ///
        /// 3 つの限界状態に書き写すと、片方だけ直したときに静かに食い違う。
        /// </summary>
        [TestMethod]
        public void TheShearGeometry_IsDefinedOnce()
        {
            var lines = TestSource
                .Read("Graphics_r1", "Models", "InputData", "InsituReinforcedConcreteSection.cs")
                .Split('\n');

            foreach (var (fragment, what) in new[]
            {
                ("Math.PI * PileDia / 4.0", "等価な幅 b"),
                ("0.9 * PileDia", "有効せい d"),
                ("7.0 / 8.0 * ShearD", "応力中心距離 j"),
                ("0.72", "断面形状係数 kc"),
            })
            {
                int count = lines.Count(l => !l.TrimStart().StartsWith("//") && l.Contains(fragment));
                Assert.AreEqual(1, count,
                    $"{what} ({fragment}) が {count} か所にあります。1 か所にまとめてください");
            }
        }
    }
}
