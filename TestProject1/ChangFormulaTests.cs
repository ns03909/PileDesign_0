using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System;

namespace TestProject1
{
    /// <summary>
    /// Chang の式 (杭頭の水平荷重を受ける半無限長の杭) のウィンドウが、式として成り立っていること。
    ///
    /// <para>2026-09-11 に 2 件見つかった。</para>
    /// <para>1. 地盤反力係数の低減 kh = kh0·(y/0.01)^(-1/2) の閾値が、反復 (<c>Update</c>) だけ
    /// 0.1 m になっていた。同じクラスの <c>GetHorizontalForce</c> と水平解析は 0.01 m。
    /// 0.01 m なら閾値で kh0 と連続につながるが、0.1 m では 1〜10 cm で低減されず、
    /// 10 cm で一気に 0.32 倍へ跳ぶ。</para>
    /// <para>2. 杭頭が地表から H だけ上にある (H &gt; 0) ときの地中部のたわみとせん断力で、
    /// 深さ方向に減衰する <c>exp(-βx)</c> が <c>exp(-βH)</c> になっていた。せん断力は
    /// 第 2 項の <c>cos + sin</c> も <c>cos - sin</c> だった。曲げモーメントは正しかったので、
    /// 「Q = dM/dx」「M = -EI·y''」「地表で上下の式がつながる」を数値微分で確かめる。
    /// いまのウィンドウは H を 0 に固定しているので表には出ていなかった。</para>
    /// </summary>
    [TestClass]
    public class ChangFormulaTests
    {
        private const double EI = 1.0e5;       // kN·m²
        private const double Beta0 = 0.5;      // 1/m
        private const double Kh0 = 2.0e4;      // kN/m³ (値そのものは式の検査に効かない)

        private static Chang Build(double h, double ar, double load)
        {
            var c = new Chang(_EI: EI, _beta: Beta0, _h: 0.0, _horizontalLoad: 0.0, _ar: ar);
            c.Kh0 = Kh0;
            c.Beta0 = Beta0;
            c.H = h;
            c.HorizontalLoad = load;
            return c;
        }

        [TestMethod]
        public void KhIsReducedFromOneCentimetre()
        {
            // 地表変位が 1〜10 cm に収まる荷重 (収束後およそ 5.8 cm)
            var c = Build(h: 0.0, ar: 1.0, load: 1500.0);
            double y = c.GroundSurfaceDisplacement;
            Assert.IsTrue(y > 0.01 && y < 0.1, $"前提: 地表変位 {y} m が 1〜10 cm にありません");

            double expected = c.Kh0 / Math.Sqrt(y / 0.01);
            Assert.AreEqual(expected, c.Kh, expected * 1e-4,
                $"地表変位 {y:F4} m で kh が低減されていません (kh = {c.Kh}, kh0 = {c.Kh0})");
        }

        [TestMethod]
        public void KhIsContinuousAtTheThreshold()
        {
            var c = Build(h: 0.0, ar: 1.0, load: 100.0);
            double below = c.GetHorizontalForce(0.01 - 1e-9) / (0.01 - 1e-9);
            double above = c.GetHorizontalForce(0.01 + 1e-9) / (0.01 + 1e-9);
            Assert.AreEqual(below, above, Math.Abs(below) * 1e-6, "kh の低減が閾値で跳んでいます");
        }

        [DataTestMethod]
        [DataRow(0.0, 1.0)]
        [DataRow(0.0, 0.5)]
        [DataRow(2.0, 1.0)]
        [DataRow(2.0, 0.5)]
        [DataRow(2.0, 0.0)]
        public void ShearIsTheSlopeOfTheMoment(double h, double ar)
        {
            var c = Build(h, ar, load: 100.0);
            const double d = 1e-5;
            foreach (double x in new[] { -1.0, 0.3, 1.0, 2.5, 6.0 })
            {
                if (x < 0 && h == 0) continue;
                double slope = (c.GetBendingMoment(x + d) - c.GetBendingMoment(x - d)) / (2 * d);
                Assert.AreEqual(slope, c.GetShearForce(x), 1e-4 * c.HorizontalLoad,
                    $"H={h}, Ar={ar}, x={x}: Q と dM/dx が合いません");
            }
        }

        [DataTestMethod]
        [DataRow(0.0, 1.0)]
        [DataRow(0.0, 0.5)]
        [DataRow(2.0, 1.0)]
        [DataRow(2.0, 0.5)]
        [DataRow(2.0, 0.0)]
        public void MomentIsMinusEITimesCurvature(double h, double ar)
        {
            var c = Build(h, ar, load: 100.0);
            const double d = 1e-3;
            foreach (double x in new[] { -1.0, 0.3, 1.0, 2.5, 6.0 })
            {
                if (x < 0 && h == 0) continue;
                double curvature = (c.GetDeflection(x + d) - 2 * c.GetDeflection(x) + c.GetDeflection(x - d)) / (d * d);
                double m = c.GetBendingMoment(x);
                Assert.AreEqual(m, -c.EI * curvature, 1e-4 * Math.Max(1.0, Math.Abs(m)),
                    $"H={h}, Ar={ar}, x={x}: M と -EI·y'' が合いません");
            }
        }

        [DataTestMethod]
        [DataRow(2.0, 1.0)]
        [DataRow(2.0, 0.5)]
        [DataRow(2.0, 0.0)]
        public void AboveAndBelowGroundMeetAtTheSurface(double h, double ar)
        {
            var c = Build(h, ar, load: 100.0);
            const double e = 1e-9;
            foreach (var (name, f) in new (string, Func<double, double>)[]
            {
                ("たわみ", c.GetDeflection),
                ("曲げモーメント", c.GetBendingMoment),
                ("せん断力", c.GetShearForce),
            })
            {
                double above = f(-e), below = f(0.0);
                Assert.AreEqual(above, below, 1e-6 * Math.Max(1.0, Math.Abs(above)),
                    $"H={h}, Ar={ar}: {name}が地表で跳んでいます (地上 {above} / 地中 {below})");
            }

            // 地表のたわみは、専用の式 (ComputeGroundSurfaceDisplacement) とも一致する
            Assert.AreEqual(c.GroundSurfaceDisplacement, c.GetDeflection(0.0),
                1e-9 * Math.Abs(c.GroundSurfaceDisplacement), "地表のたわみが地表変位と違います");
        }
    }
}
