using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 降伏点を求めるときのレバー長が、断面が実際に積分しているひずみ分布と合っていること。
    ///
    /// 場所打ち鋼管コンクリート杭の降伏点は「最外縁の鋼管または主筋が引張降伏する」ときの
    /// 圧縮縁ひずみで決める。その圧縮縁が<b>どこか</b>は積分側の
    /// <c>epsilon0 = epsilonC - (D/2 - t)·φ</c> で決まっており、鋼管の内側の面である。
    ///
    /// ところが降伏点の式は板厚を引かずに測っていた
    /// (主筋 D/2 + PCD/2、鋼管は板厚と無関係な D - 1)。
    /// レバーが長すぎるぶん、降伏時の圧縮縁ひずみを過大に見積もっていた。
    /// </summary>
    [TestClass]
    public class SectionYieldLeverArmTests
    {
        private const double PipeDia = 1000.0;
        private const double PipeT = 12.0;
        private const double Curvature = 2.0e-6;   // 1/mm

        private static object CreateSection()
        {
            var asm = typeof(PileSection).Assembly;
            var type = asm.GetType("PileDesign.Models.InputData.InsituSteelPipeReinforcedConcreteSection")!;

            var pipe = new InsituSteelPipe("SKK400", PipeDia, PipeT, 0.0);
            var concrete = new InsituConcrete(PipeDia - 2 * PipeT, 1.0, 27.0);
            var bars = new MainBars(PipeDia - 2 * PipeT - 300.0, 20, "SD390", "D25");

            return Activator.CreateInstance(type,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, [pipe, concrete, bars, true], null)!;
        }

        private static SectionStrainStressProfile Profile(object section, double epsilonC)
        {
            var m = section.GetType().GetMethod("GetStrainStressProfile",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            return (SectionStrainStressProfile)m.Invoke(section, [epsilonC, Curvature, true, 200])!;
        }

        /// <summary>引張側 (z が正) で最も外側にある材料のひずみ。</summary>
        private static double TensionEdgeStrainOf(SectionStrainStressProfile p, SectionMaterialKind kind)
        {
            var mat = p.Materials.First(m => m.Kind == kind);
            int at = -1;
            double best = double.NegativeInfinity;
            for (int i = 0; i < mat.Z.Count; i++)
            {
                if (double.IsNaN(mat.Strain[i])) continue;
                if (mat.Z[i] > best) { best = mat.Z[i]; at = i; }
            }
            Assert.IsTrue(at >= 0, $"{kind} のひずみが取れない");
            return mat.Strain[at];
        }

        /// <summary>
        /// 鋼管が降伏するときの圧縮縁ひずみを、積分側の分布と突き合わせる。
        ///
        /// 正しいレバーで作った圧縮縁ひずみを断面に渡すと、引張側の鋼管がちょうど
        /// 降伏ひずみになる。板厚を引かない古いレバーではならない。
        /// </summary>
        [TestMethod]
        public void TheYieldPoint_PutsThePipeExactlyAtItsYieldStrain()
        {
            var section = CreateSection();
            var pipe = new InsituSteelPipe("SKK400", PipeDia, PipeT, 0.0);
            double epsilonY = pipe.SEpsilonY;

            // 圧縮縁 = 鋼管の内側の面。そこから引張側の鋼管リング中心まで
            double correctLever = (PipeDia * 0.5 - PipeT) + (PipeDia - PipeT) * 0.5;
            double epsilonC = -epsilonY + Curvature * correctLever;

            double atPipe = TensionEdgeStrainOf(Profile(section, epsilonC), SectionMaterialKind.SteelPipe);

            Assert.AreEqual(-epsilonY, atPipe, 1e-9,
                "正しいレバーで作った圧縮縁ひずみなのに、鋼管が降伏ひずみになっていない");

            // 板厚を引かない古いレバー (D - 1) では合わない
            double oldEpsilonC = -epsilonY + Curvature * (PipeDia - 1.0);
            double atPipeOld = TensionEdgeStrainOf(Profile(section, oldEpsilonC), SectionMaterialKind.SteelPipe);

            Assert.AreNotEqual(-epsilonY, atPipeOld,
                "古いレバーでも降伏ひずみになってしまう。テストの当て先を見直すこと");
        }

        /// <summary>主筋についても同じこと。</summary>
        [TestMethod]
        public void TheYieldPoint_PutsTheOutermostBarExactlyAtItsYieldStrain()
        {
            var section = CreateSection();
            var bars = new MainBars(PipeDia - 2 * PipeT - 300.0, 20, "SD390", "D25");
            double epsilonY = bars.RSigmaY / bars.Er;

            double correctLever = (PipeDia * 0.5 - PipeT) + bars.PCD * 0.5;
            double epsilonC = -epsilonY + Curvature * correctLever;

            double atBar = TensionEdgeStrainOf(Profile(section, epsilonC), SectionMaterialKind.MainBar);

            Assert.AreEqual(-epsilonY, atBar, 1e-9,
                "正しいレバーで作った圧縮縁ひずみなのに、主筋が降伏ひずみになっていない");

            // 板厚を引かない古いレバー (D/2 + PCD/2) では合わない
            double oldEpsilonC = -epsilonY + Curvature * (PipeDia * 0.5 + bars.PCD * 0.5);
            double atBarOld = TensionEdgeStrainOf(Profile(section, oldEpsilonC), SectionMaterialKind.MainBar);

            Assert.AreNotEqual(-epsilonY, atBarOld,
                "古いレバーでも降伏ひずみになってしまう。テストの当て先を見直すこと");
        }

        /// <summary>
        /// 降伏点の式が、実際にそのレバーを使っていること。
        ///
        /// 上の 2 件は「正しいレバーならこうなる」を確かめるだけなので、
        /// 式そのものが元に戻っても落ちない。ここで式を押さえる。
        /// </summary>
        [TestMethod]
        public void TheYieldFormula_MeasuresFromTheCompressionEdge()
        {
            var section = CreateSection();
            var m = section.GetType().GetMethod("GetYieldForceAndMoment",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

            var (n, mom) = ((double, double))m.Invoke(section, [Curvature])!;
            Assert.IsTrue(double.IsFinite(n) && double.IsFinite(mom), "降伏点が求まらない");

            // 支配する材料 (この諸元では鋼管) の降伏ひずみになっているか、
            // 断面のひずみ分布から逆に確かめる
            var pipe = new InsituSteelPipe("SKK400", PipeDia, PipeT, 0.0);
            var bars = new MainBars(PipeDia - 2 * PipeT - 300.0, 20, "SD390", "D25");
            double edgeToCenter = PipeDia * 0.5 - PipeT;
            double epsilonCPipe = -pipe.SEpsilonY + Curvature * (edgeToCenter + (PipeDia - PipeT) * 0.5);
            double epsilonCBar = -(bars.RSigmaY / bars.Er) + Curvature * (edgeToCenter + bars.PCD * 0.5);
            double expectedEpsilonC = Math.Min(epsilonCPipe, epsilonCBar);

            var expected = ((double, double))section.GetType()
                .GetMethod("GetUltimateForceAndMoment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .Invoke(section, [expectedEpsilonC, Curvature])!;

            Assert.AreEqual(expected.Item1, n, Math.Abs(expected.Item1) * 1e-9 + 1e-6,
                "降伏点の軸力が、圧縮縁から測ったレバーの結果と合わない");
            Assert.AreEqual(expected.Item2, mom, Math.Abs(expected.Item2) * 1e-9 + 1e-6,
                "降伏点の曲げが、圧縮縁から測ったレバーの結果と合わない");
        }
    }
}
