using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// F.T.Pile 構法のパイルキャップの支承面積 Ac は入力 (FTPile.PileCapArea、m²) で、未入力 (0) なら
    /// 杭径の 2 倍の正方形 (2Φ)² と仮定する (2026-11-23 利用者の判断)。以前は入力が無く、いつも (2Φ)² だった。
    ///
    /// <para>Ac は支圧係数 φc = min(√(Ac/Ap), 4) を通して、許容回転角 θac = 0.03 − 0.05·σn/(φc·Fc) と
    /// 軸力の上限 (3/5)·φc·Ap·Fc に効く。</para>
    /// </summary>
    [TestClass]
    public class FTPileCapAreaTests
    {
        private const double Fc = 24.0;

        private static FTPile Pile600()
        {
            var ft = new FTPile(Fc, 33_500.0 * Math.Pow(23.0 / 24.0, 2.0) * Math.Pow(Fc / 60.0, 1.0 / 3.0));
            ft.SetDimensionsFromSection(pileDiameter: 600.0, concreteThickness: 90.0);
            Assert.IsTrue(ft.SelectCap(600.0), "(前提) φ600 の FT キャップ");
            return ft;
        }

        [TestMethod]
        public void UnsetAreaAssumesASquareOfTwiceTheDiameter()
        {
            var ft = Pile600();
            Assert.IsTrue(ft.IsPileCapAreaAssumed);
            Assert.AreEqual(1200.0 * 1200.0, ft.FTPileCap.Ac, 1e-6, "未入力は (2Φ)²");
            Assert.AreEqual(Math.Min(Math.Sqrt(1200.0 * 1200.0 / ft.FTPilePile.Ap), 4.0), ft.PhiC, 1e-12);
        }

        [TestMethod]
        public void TheInputAreaDrivesTheBearingFactor()
        {
            var ft = Pile600();
            double ap = ft.FTPilePile.Ap;   // mm²
            ft.PileCapArea = 1.0;           // m²
            Assert.IsFalse(ft.IsPileCapAreaAssumed);
            Assert.AreEqual(1.0e6, ft.FTPileCap.Ac, 1e-6, "入力 (m²) を mm² にして使う");
            Assert.AreEqual(Math.Sqrt(1.0e6 / ap), ft.PhiC, 1e-12, "φc = √(Ac/Ap) (4 未満)");

            ft.PileCapArea = 10.0;
            Assert.AreEqual(4.0, ft.PhiC, 1e-12, "φc は 4 で頭打ち");

            // 許容回転角 θac にも届く (φc が大きいほど θac は大きい)
            ft.PileCapArea = 0.5;
            double small = ft.GetAllowableRotationAngle(1000e3);
            ft.PileCapArea = 1.0;
            double large = ft.GetAllowableRotationAngle(1000e3);
            Assert.IsTrue(large > small, $"支承面積が許容回転角に届いていない ({small} → {large})");
        }

        [TestMethod]
        public void TheAreaSurvivesDeepCopy()
        {
            var ft = Pile600();
            ft.PileCapArea = 1.5;
            Assert.AreEqual(1.5, ft.DeepCopy().PileCapArea, 0.0, "Undo の写しで残る");
        }
    }
}
