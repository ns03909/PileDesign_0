using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using System;

namespace TestProject1
{
    /// <summary>
    /// 割線剛性が、内力の評価と同じものであること。
    ///
    /// 割線剛性 $K_{sec}(x)$ は「その変位で出ている反力 ÷ 変位」なので、
    /// <b>$K_{sec}(x)\,x = R(x)$ が恒等的に成り立つ</b>ことが定義そのもの。
    /// いまこれを使っているのは鉛直解析の杭ばね
    /// (<c>PileVerticalSoilSpringModel.GetSecantStiffness</c>)。
    /// 恒等式が崩れると、ばねの力と剛性が食い違い、収束しても釣り合っていない解に落ち着く。
    ///
    /// <para>もともとは単杭沈下の変位制御法を守るために置いた検査で、周面ばねだけ
    /// 接線剛性を足していた誤り (1.0.24-beta で修正) を捕まえていた。変位制御法は
    /// 画面から一度も選べないまま別の誤りも抱えていたので 2026-09-11 に削除したが、
    /// 割線剛性の関数そのものは鉛直解析が使い続けているので、この 2 本は残す。</para>
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
        /// 割線剛性を使う側 (鉛直解析の杭ばね) が釣り合いに収束するための条件。
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
    }
}
