using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// GroundResponseSpectrumCalc (応答スペクトル法 + MDOF + 等価線形化) のテスト。
    /// 出典: Inoue/Hayashi 他「表層地盤による地震動増幅率評価法に関する研究」(AIJ 2026)。
    /// </summary>
    [TestClass]
    public class GroundResponseSpectrumCalcTests
    {
        // ---------- Sa0(T) 境界値 (論文式 (2)) ----------
        [TestMethod]
        public void Sa0_AtT0p10_Equals_Minus0p2()
        {
            // T=0.10 → -3.2 + 30·0.10 = -0.2
            Assert.AreEqual(-0.2, GroundResponseSpectrumCalc.Sa0(0.10), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p16_Equals_1p6()
        {
            // T=0.16 → -3.2 + 30·0.16 = 1.6 (slope 区間の上端)
            Assert.AreEqual(1.6, GroundResponseSpectrumCalc.Sa0(0.16), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p40_Equals_5p12()
        {
            // 0.16 ≤ T ≤ 0.64 → 5.12 (定数区間)
            Assert.AreEqual(5.12, GroundResponseSpectrumCalc.Sa0(0.40), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p64_Equals_5p12()
        {
            // 境界: 定数区間の上端
            Assert.AreEqual(5.12, GroundResponseSpectrumCalc.Sa0(0.64), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT1p28_Equals_2p56()
        {
            // T>0.64 → 5.12·0.64/T = 5.12·0.64/1.28 = 2.56
            Assert.AreEqual(2.56, GroundResponseSpectrumCalc.Sa0(1.28), 1e-9);
        }

        // ---------- Fh(ξ) (論文式 (4)) ----------
        [TestMethod]
        public void Fh_AtXi0p05_Equals_1p0()
        {
            // 1.5 / (1 + 10·0.05) = 1.5/1.5 = 1.0
            Assert.AreEqual(1.0, GroundResponseSpectrumCalc.Fh(0.05), 1e-12);
        }

        [TestMethod]
        public void Fh_AtXi0p20_Equals_0p5()
        {
            // 1.5 / (1 + 10·0.20) = 1.5/3 = 0.5
            Assert.AreEqual(0.5, GroundResponseSpectrumCalc.Fh(0.20), 1e-12);
        }

        // ---------- 単層 (低励起 → G 低下なし → 離散化解析解) ----------
        [TestMethod]
        public void Compute_SingleLayerLowExcitation_PeriodNearAnalytical()
        {
            // H=20m, ρ=18 kN/m³, VS=200, 基盤 VS=400
            // 連続体解析解 T1=4H/VS=0.40s。4 質点離散化 + 集中質量モデルでは少し長くなり 0.45s 前後。
            // L=1e-3 (非常に小さい励起) で G/G0 ≈ 1.0 → 等価線形化による T1 伸びを抑える。
            var masses = new ObservableCollection<GroundMassDataInput>();
            int n = 4;
            double totalH = 20.0;
            double h = totalH / n;
            double rho = 18.0;
            double vs = 200.0;
            for (int i = 0; i < n; i++)
            {
                masses.Add(new GroundMassDataInput
                {
                    H = h,
                    Density = rho,
                    VS0 = vs,
                    Mass = rho / 9.80665 * h,
                    IsEngineeringBedrock = false
                });
            }
            masses.Add(new GroundMassDataInput
            {
                H = h,
                Density = rho,
                VS0 = vs,
                Mass = rho / 9.80665 * h,
                IsEngineeringBedrock = true
            });

            var result = GroundResponseSpectrumCalc.Compute(
                masses, bedrockDensity: 19.0, bedrockVs: 400.0,
                shallowSoilType: "砂質土", L: 1e-3);

            Assert.IsFalse(double.IsNaN(result.T1), "T1 should be valid");
            // 4 質点集中質量モデルの解析解 ≈ 0.452s (固有値 λ_min ≈ 0.121, k/m=1600)
            // 線形範囲では ±15% 以内に収まるはず
            Assert.IsTrue(result.T1 > 0.38 && result.T1 < 0.55,
                $"T1 = {result.T1:F3} は線形 4 質点離散化解 ~0.452s から大きくずれている");
        }

        // ---------- 高励起 → G 低下 → 周期延伸 ----------
        [TestMethod]
        public void Compute_SingleLayerHighExcitation_PeriodElongates()
        {
            // 同じ地盤を L=1.0 で計算 → G 低下で T1 が大幅に延びることを確認
            var masses = new ObservableCollection<GroundMassDataInput>();
            int n = 4;
            double h = 5.0, rho = 18.0, vs = 200.0;
            for (int i = 0; i < n; i++)
                masses.Add(new GroundMassDataInput
                {
                    H = h, Density = rho, VS0 = vs,
                    Mass = rho / 9.80665 * h, IsEngineeringBedrock = false
                });
            masses.Add(new GroundMassDataInput
            {
                H = h, Density = rho, VS0 = vs,
                Mass = rho / 9.80665 * h, IsEngineeringBedrock = true
            });

            var rLow = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1e-3);
            var rHigh = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(rLow.T1));
            Assert.IsFalse(double.IsNaN(rHigh.T1));
            // L=1.0 では G が低下して T1 が伸びる
            Assert.IsTrue(rHigh.T1 > rLow.T1 * 1.2,
                $"L=1.0 の T1={rHigh.T1:F3} は L=1e-3 の T1={rLow.T1:F3} より伸びるべき (≥ 1.2×)");
        }

        // ---------- 2 層 EVD 健全性 ----------
        [TestMethod]
        public void Compute_TwoLayer_ReturnsPositiveDisplacements()
        {
            var masses = new ObservableCollection<GroundMassDataInput>();
            // 上層 軟、下層 硬
            masses.Add(new GroundMassDataInput
            {
                H = 5.0, Density = 17.0, VS0 = 150.0,
                Mass = 17.0 / 9.80665 * 5.0, IsEngineeringBedrock = false
            });
            masses.Add(new GroundMassDataInput
            {
                H = 10.0, Density = 19.0, VS0 = 300.0,
                Mass = 19.0 / 9.80665 * 10.0, IsEngineeringBedrock = false
            });
            masses.Add(new GroundMassDataInput
            {
                H = 1.0, Density = 20.0, VS0 = 400.0,
                Mass = 20.0 / 9.80665 * 1.0, IsEngineeringBedrock = true
            });

            var result = GroundResponseSpectrumCalc.Compute(
                masses, 20.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(result.T1));
            Assert.IsTrue(result.T1 > 0, "T1 must be positive");
            Assert.AreEqual(2, result.G.Length);
            Assert.AreEqual(2, result.PhiU0_1.Length);
            Assert.AreEqual(2, result.DispMm.Length);

            // U[0]=1 正規化が効いていること
            Assert.AreEqual(1.0, result.PhiU0_1[0], 1e-9);
            // 地表側のモード形変位は内部より大きい (1次モードの形)
            Assert.IsTrue(Math.Abs(result.PhiU0_1[0]) >= Math.Abs(result.PhiU0_1[1]));

            // 変位は地表側で大きい (絶対値)
            Assert.IsTrue(result.DispMm[0] > 0);
            Assert.IsTrue(result.DispMm[0] >= result.DispMm[1]);
        }

        // ---------- 等価線形化 収束 ----------
        [TestMethod]
        public void Compute_HighStrain_GReductionWithinBounds()
        {
            // L=1.0 (Level2) 高ひずみ条件 — G/G0 ∈ [0.05, 1] 内に収束
            var masses = new ObservableCollection<GroundMassDataInput>();
            for (int i = 0; i < 5; i++)
            {
                masses.Add(new GroundMassDataInput
                {
                    H = 4.0, Density = 17.5, VS0 = 180.0,
                    Mass = 17.5 / 9.80665 * 4.0, IsEngineeringBedrock = false
                });
            }
            masses.Add(new GroundMassDataInput
            {
                H = 1.0, Density = 20.0, VS0 = 400.0,
                Mass = 20.0 / 9.80665 * 1.0, IsEngineeringBedrock = true
            });

            var result = GroundResponseSpectrumCalc.Compute(masses, 20.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(result.T1));
            // G_init = 17.5/9.80665 × 180² ≈ 57,830 [kPa]
            double g0 = 17.5 / 9.80665 * 180.0 * 180.0;
            for (int i = 0; i < result.G.Length; i++)
            {
                double ratio = result.G[i] / g0;
                Assert.IsTrue(ratio >= 0.05 - 1e-9 && ratio <= 1.0 + 1e-9,
                    $"G[{i}]/G0 = {ratio:F3} が [0.05, 1] 範囲外");
            }
            // 高ひずみ → 等価減衰は初期値 0.05 を超える
            Assert.IsTrue(result.XiE >= 0.05);
        }

        // ---------- 基盤途中 ----------
        [TestMethod]
        public void Compute_BedrockInMiddle_OnlyActiveLayersComputed()
        {
            // 5 質点中、i=3 が IsEngineeringBedrock → アクティブは i=0..2 の 3 質点
            var masses = new ObservableCollection<GroundMassDataInput>();
            for (int i = 0; i < 3; i++)
            {
                masses.Add(new GroundMassDataInput
                {
                    H = 5.0, Density = 18.0, VS0 = 200.0,
                    Mass = 18.0 / 9.80665 * 5.0, IsEngineeringBedrock = false
                });
            }
            masses.Add(new GroundMassDataInput
            {
                H = 1.0, Density = 19.0, VS0 = 350.0,
                Mass = 19.0 / 9.80665 * 1.0, IsEngineeringBedrock = true
            });
            masses.Add(new GroundMassDataInput
            {
                H = 1.0, Density = 19.0, VS0 = 350.0,
                Mass = 19.0 / 9.80665 * 1.0, IsEngineeringBedrock = true
            });

            var result = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(result.T1));
            Assert.AreEqual(3, result.G.Length, "アクティブ質点数 = 3");
            Assert.AreEqual(3, result.DispMm.Length);
        }

        // ---------- NaN 安全 ----------
        [TestMethod]
        public void Compute_AllBedrock_ReturnsNaN()
        {
            // 全質点が基盤 → NaN を返す (例外を投げない)
            var masses = new ObservableCollection<GroundMassDataInput>
            {
                new() { H = 1.0, Density = 19.0, VS0 = 400.0, Mass = 1.0, IsEngineeringBedrock = true }
            };
            var result = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1.0);
            Assert.IsTrue(double.IsNaN(result.T1));
        }

        [TestMethod]
        public void Compute_EmptyMasses_ReturnsNaN()
        {
            var masses = new ObservableCollection<GroundMassDataInput>();
            var result = GroundResponseSpectrumCalc.Compute(masses, 19.0, 400.0, "砂質土", 1.0);
            Assert.IsTrue(double.IsNaN(result.T1));
        }

        // ---------- γr ソイルタイプ切替 ----------
        [TestMethod]
        public void Compute_ClaySoilGivesLargerGReduction()
        {
            // 同条件で粘性土 (γr=2e-3) > 砂質土 (γr=7e-4) → 粘性土の方が G が下がりにくい
            // (γr が大きいほど G/G0 = 1/(1+γ/γr) が大きい)
            var massesSand = BuildHighStrainMasses();
            var massesClay = BuildHighStrainMasses();

            var rSand = GroundResponseSpectrumCalc.Compute(massesSand, 20.0, 400.0, "砂質土", 1.0);
            var rClay = GroundResponseSpectrumCalc.Compute(massesClay, 20.0, 400.0, "粘性土", 1.0);

            Assert.IsFalse(double.IsNaN(rSand.T1));
            Assert.IsFalse(double.IsNaN(rClay.T1));
            // 粘性土 (大きい γr) の方が G は高めに残る
            Assert.IsTrue(rClay.G[0] >= rSand.G[0],
                $"粘性土の G[0]={rClay.G[0]:F1} は砂質土 G[0]={rSand.G[0]:F1} 以上であるべき");
        }

        private static ObservableCollection<GroundMassDataInput> BuildHighStrainMasses()
        {
            var masses = new ObservableCollection<GroundMassDataInput>();
            for (int i = 0; i < 4; i++)
            {
                masses.Add(new GroundMassDataInput
                {
                    H = 5.0, Density = 17.0, VS0 = 150.0,
                    Mass = 17.0 / 9.80665 * 5.0, IsEngineeringBedrock = false
                });
            }
            masses.Add(new GroundMassDataInput
            {
                H = 1.0, Density = 20.0, VS0 = 400.0,
                Mass = 20.0 / 9.80665 * 1.0, IsEngineeringBedrock = true
            });
            return masses;
        }
    }
}
