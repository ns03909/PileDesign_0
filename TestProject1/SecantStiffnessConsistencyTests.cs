using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System;

namespace TestProject1
{
    /// <summary>
    /// 変位制御法の割線剛性が、内力の評価と同じものであること。
    ///
    /// 変位制御法は $K_{sec}(x)\,x = F$ を繰り返し解く。収束した解が釣り合い
    /// $R(x) = F$ を満たすのは、<b>$K_{sec}(x)\,x = R(x)$ が恒等的に成り立つ</b>ときだけ。
    /// 内力 (<c>GetSoilReactionVector</c>) は割線剛性 × 変位で作っているので、
    /// 剛性側に接線を混ぜるとこの恒等式が崩れ、
    /// 収束しても釣り合っていない解に落ち着く。
    ///
    /// 実際に周面ばねだけ接線剛性を足しており (先端のみ割線)、
    /// 非線形域の荷重-変位曲線がずれていた。既定の荷重制御法は接線剛性で正しく組んでいる。
    /// </summary>
    [TestClass]
    public class SecantStiffnessConsistencyTests
    {
        // 代表的な周面ばね諸元 (τ1 < τ2 の 2 折れ線)
        private const double Tau1 = 50.0;    // kN/m2
        private const double Tau2 = 100.0;   // kN/m2
        private const double S1 = 0.005;     // m
        private const double S2 = 0.020;     // m
        private const double PsiL = 1.5;     // m2

        private static double Secant(double s) =>
            VerticalLoadTransferMethod.GetSecantStiffnessPilePerimeter(
                "final", s, true, true, Tau1, Tau2, S1, S2, PsiL);

        private static double Tangent(double s) =>
            VerticalLoadTransferMethod.GetTangentStiffnessPilePerimeter(
                "final", s, true, true, Tau1, Tau2, S1, S2, PsiL);

        /// <summary>
        /// 非線形域では割線と接線がはっきり違うこと。
        /// ここが同じなら、この検査自体が空振りになる。
        /// </summary>
        [TestMethod]
        public void SecantAndTangent_DifferInTheNonlinearRange()
        {
            foreach (double s in new[] { 0.010, 0.015, 0.030, 0.050 })
            {
                double sec = Secant(s);
                double tan = Tangent(s);
                Assert.AreNotEqual(sec, tan, Math.Abs(sec) * 1e-6,
                    $"変位 {s} m で割線と接線が一致している (検査が成立しない)");
            }
        }

        /// <summary>
        /// 割線剛性 × 変位が、その変位における反力そのものになること。
        /// 変位制御法が釣り合いに収束するための条件。
        /// </summary>
        [TestMethod]
        public void SecantTimesDisplacement_EqualsTheReaction()
        {
            foreach (double s in new[] { 0.001, 0.005, 0.010, 0.020, 0.040, -0.010 })
            {
                // 反力の定義そのもの: 2 折れ線を τ で積分した値 × 周面積
                double magnitude = Math.Abs(s);
                double tau = magnitude <= S1
                    ? Tau1 * magnitude / S1
                    : magnitude <= S2
                        ? Tau1 + (Tau2 - Tau1) * (magnitude - S1) / (S2 - S1)
                        : Tau2;
                double expected = Math.Sign(s) * tau * PsiL;

                Assert.AreEqual(expected, Secant(s) * s, Math.Max(Math.Abs(expected) * 1e-9, 1e-12),
                    $"変位 {s} m で 割線剛性 × 変位 が反力に一致しない");
            }
        }

        /// <summary>
        /// 変位制御法の剛性が周面・先端とも割線であること。
        /// 片方だけ接線にすると、上の恒等式が壊れる。
        /// </summary>
        [TestMethod]
        public void DisplacementControl_UsesSecantForEverySpring()
        {
            string source = System.IO.File.ReadAllText(FindSource());
            int start = source.IndexOf("public List<double> GetSecantSoilStiffness", StringComparison.Ordinal);
            Assert.IsTrue(start > 0, "GetSecantSoilStiffness が見つからない");

            int end = source.IndexOf("private static double GetTangentStiffnessPileToeFromRp", start, StringComparison.Ordinal);
            Assert.IsTrue(end > start, "メソッドの終端が特定できない");

            string body = source[start..end];
            StringAssert.Contains(body, "GetSecantStiffnessPilePerimeter", "周面ばねが割線になっていない");
            StringAssert.Contains(body, "GetSecantStiffnessPileToeFromSettlement", "先端ばねが割線になっていない");
            Assert.IsFalse(body.Contains("GetTangentStiffness"),
                "割線剛性の中で接線剛性を呼んでいる (収束しても釣り合わない)");
        }

        private static string FindSource()
        {
            var dir = new System.IO.DirectoryInfo(
                System.IO.Path.GetDirectoryName(typeof(SecantStiffnessConsistencyTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(
                    dir.FullName, "Graphics_r1", "FEM", "VerticalLoadTransferMethod.cs");
                if (System.IO.File.Exists(candidate)) return candidate;
            }
            throw new System.IO.FileNotFoundException("VerticalLoadTransferMethod.cs が見つかりません");
        }
    }
}
