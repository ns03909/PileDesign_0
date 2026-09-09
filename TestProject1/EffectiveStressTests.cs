using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 有効応力が負にならないこと。
    ///
    /// 土被りは<b>応力計算面より下だけ</b>を数えるのに、水圧は地下水位から数えていた。
    /// 地下水位が応力計算面より上にある場合 (地下室で基礎底に応力計算面を置いた場合など)、
    /// その差ぶんの水柱まで引いてしまい、有効応力が負になる。
    ///
    /// 負になると畑中式の内部摩擦角が sqrt(負) で NaN になり、受働土圧が静かに NaN になる。
    /// 例外にならないので、結果を見るまで気づかない。
    /// </summary>
    [TestClass]
    public class EffectiveStressTests
    {
        private static double EffectiveStress(GroundInput ground, double z)
        {
            var m = typeof(SoilPile).GetMethod("GetEffectiveStress",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("GetEffectiveStress が見つかりません");
            return (double)m.Invoke(null, [ground, z])!;
        }

        /// <summary>地表 0、応力計算面 -3、地下水位 -1、単位体積重量 18 の地盤。</summary>
        private static GroundInput MakeGround(double waterLevel, double stressLevel)
        {
            var ground = new GroundInput
            {
                GroundTopAltitude = 0.0,
                GroundWaterTableAltitude = waterLevel,
                StressAltitude = stressLevel,
                GroundLayers = [new GroundLayerInput
                {
                    No = 1,
                    BottomAltitude = -20.0,
                    LayerThickness = 20.0,
                    Density = 18.0,
                }],
            };
            return ground;
        }

        /// <summary>
        /// 地下水位が応力計算面より上でも、有効応力は負にならないこと。
        ///
        /// 応力計算面 -3 の下 0.5m の点。土被りは 0.5 × 18 = 9。
        /// 水柱も応力計算面から数えて 0.5m なので 5。差し引き 4。
        /// 以前は地下水位 -1 から 2.5m 数えて 25 を引き、-16 になっていた。
        /// </summary>
        [TestMethod]
        public void WaterAboveTheStressLevel_DoesNotMakeItNegative()
        {
            var ground = MakeGround(waterLevel: -1.0, stressLevel: -3.0);

            double sigma = EffectiveStress(ground, -3.5);

            Assert.IsTrue(sigma >= 0.0,
                $"有効応力が負になっている ({sigma})。内部摩擦角が NaN になり受働土圧が静かに壊れる");
            Assert.AreEqual(4.0, sigma, 1e-9,
                "応力計算面より下の水柱だけを引いていない");
        }

        /// <summary>
        /// 内部摩擦角が NaN にならないこと。負の有効応力が実際に何を壊すかの確認。
        /// </summary>
        [TestMethod]
        public void TheFrictionAngle_StaysANumber()
        {
            var ground = MakeGround(waterLevel: -1.0, stressLevel: -3.0);

            double sigma = EffectiveStress(ground, -3.5);
            double hatanaka = Math.Sqrt(sigma / 100.0);   // 畑中式の中の平方根

            Assert.IsFalse(double.IsNaN(hatanaka),
                "有効応力が負で内部摩擦角が NaN になる");
        }

        /// <summary>
        /// 地下水位が応力計算面より下にある通常の場合は、従来どおりであること。
        ///
        /// 応力計算面 0、地下水位 -2、z=-5。土被り 5 × 18 = 90、水柱 3m で 30。差し引き 60。
        /// </summary>
        [TestMethod]
        public void TheOrdinaryCase_IsUnchanged()
        {
            var ground = MakeGround(waterLevel: -2.0, stressLevel: 0.0);

            Assert.AreEqual(60.0, EffectiveStress(ground, -5.0), 1e-9,
                "通常の配置で有効応力が変わっている");
        }

        /// <summary>水位が応力計算面と同じなら、境目で連続すること。</summary>
        [TestMethod]
        public void ItIsContinuousWhenTheWaterMeetsTheStressLevel()
        {
            double atSame = EffectiveStress(MakeGround(waterLevel: -3.0, stressLevel: -3.0), -5.0);
            double atAbove = EffectiveStress(MakeGround(waterLevel: -2.9, stressLevel: -3.0), -5.0);

            Assert.AreEqual(atSame, atAbove, 1e-9,
                "水位が応力計算面を越えた途端に値が飛ぶ");
        }
    }
}
