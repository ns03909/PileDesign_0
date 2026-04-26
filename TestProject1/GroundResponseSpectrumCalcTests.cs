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
        // ---------- Sa0(T) 境界値 (告示 1457 / 論文式 (2)) ----------
        [TestMethod]
        public void Sa0_AtT0p10_Equals_6p2()
        {
            // T=0.10 → 3.2 + 30·0.10 = 6.2
            Assert.AreEqual(6.2, GroundResponseSpectrumCalc.Sa0(0.10), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p16_Equals_8p0()
        {
            // T=0.16 → 3.2 + 30·0.16 = 8.0 (slope/plateau 連続境界)
            Assert.AreEqual(8.0, GroundResponseSpectrumCalc.Sa0(0.16), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p40_Equals_8p0()
        {
            // 0.16 ≤ T ≤ 0.64 → 8.0 (plateau)
            Assert.AreEqual(8.0, GroundResponseSpectrumCalc.Sa0(0.40), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT0p64_Equals_8p0()
        {
            // T=0.64 → plateau の上端、5.12/0.64 = 8.0 で 1/T 区間と連続
            Assert.AreEqual(8.0, GroundResponseSpectrumCalc.Sa0(0.64), 1e-9);
        }

        [TestMethod]
        public void Sa0_AtT1p28_Equals_4p0()
        {
            // T>0.64 → 5.12/T。T=1.28 → 5.12/1.28 = 4.0
            Assert.AreEqual(4.0, GroundResponseSpectrumCalc.Sa0(1.28), 1e-9);
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

        // ---------- per-layer Gamma05 オーバーライド ----------
        [TestMethod]
        public void Compute_HigherGamma05_GivesLessGReduction()
        {
            // 同条件で Gamma05 を per-layer で 2e-3 (大) と 7e-4 (小) に設定 → 大きい方が G 低下少
            var massesLow = BuildHighStrainMasses(7e-4);
            var massesHigh = BuildHighStrainMasses(2e-3);

            var rLow = GroundResponseSpectrumCalc.Compute(massesLow, 20.0, 400.0, "砂質土", 1.0);
            var rHigh = GroundResponseSpectrumCalc.Compute(massesHigh, 20.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(rLow.T1));
            Assert.IsFalse(double.IsNaN(rHigh.T1));
            Assert.IsTrue(rHigh.G[0] > rLow.G[0],
                $"Gamma05=2e-3 の G[0]={rHigh.G[0]:F1} は Gamma05=7e-4 の G[0]={rLow.G[0]:F1} より大きいべき");
        }

        // ---------- 増幅率 Gs1, Gs2 ----------
        [TestMethod]
        public void Compute_ReturnsValidGs1Gs2AndImpedance()
        {
            var masses = BuildHighStrainMasses();
            var result = GroundResponseSpectrumCalc.Compute(masses, 20.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(result.T1));
            // インピーダンス比 e は 0 < e < 1 の範囲 (表層が基盤より柔らかい場合)
            Assert.IsTrue(result.Impedance > 0 && result.Impedance < 1.0,
                $"Impedance={result.Impedance:F3} は (0, 1) 範囲外");
            // Gs1 = 1/(e+1.57·ξe) は正の値、典型 1〜5
            Assert.IsTrue(result.Gs1 > 0 && result.Gs1 < 100,
                $"Gs1={result.Gs1:F3} は妥当範囲外");
            // Gs2 = 1/(e+4.71·ξe) は Gs1 より小さい (4.71 > 1.57 で分母が大きい)
            Assert.IsTrue(result.Gs2 > 0 && result.Gs2 <= result.Gs1,
                $"Gs2={result.Gs2:F3} は Gs1={result.Gs1:F3} 以下のはず");
        }

        [TestMethod]
        public void SaSurface_AtPeakPeriod_AmplifiedByGs1()
        {
            // ピーク域 (0.8·T1 ≤ T ≤ 1.2·T1) では Gs1 が適用される
            double Gs1 = 2.5, Gs2 = 1.8, L = 1.0;
            double T1 = 0.5, T2_period = 0.17;
            double TPeak = T1; // ピーク中心 — Gs1 が適用される
            double saB = GroundResponseSpectrumCalc.SaBedrock(TPeak, L);
            double saS = GroundResponseSpectrumCalc.SaSurface(TPeak, T1, T2_period, Gs1, Gs2, L);
            Assert.IsTrue(saS > saB, $"saS={saS:F3} は saB={saB:F3} より大きいはず");
            Assert.AreEqual(Gs1 * saB, saS, 1e-9);
        }

        // ---------- Gs(T) 4 区分の連続性 ----------
        [TestMethod]
        public void Gs_PiecewiseFunction_IsContinuousAtBoundaries()
        {
            double Gs1 = 2.0, Gs2 = 1.5, T1 = 0.5, T2 = 0.17;
            double tA = 0.8 * T2;  // 0.136
            double tB = 0.8 * T1;  // 0.4
            double tC = 1.2 * T1;  // 0.6

            // 境界 tA: 区間1終端 = Gs2, 区間2始端 = Gs2
            double gAleft = GroundResponseSpectrumCalc.Gs(tA - 1e-9, T1, T2, Gs1, Gs2);
            double gAright = GroundResponseSpectrumCalc.Gs(tA + 1e-9, T1, T2, Gs1, Gs2);
            Assert.AreEqual(Gs2, gAleft, 1e-6);
            Assert.AreEqual(Gs2, gAright, 1e-6);

            // 境界 tB: 区間2終端 = Gs1, 区間3始端 = Gs1
            double gBleft = GroundResponseSpectrumCalc.Gs(tB - 1e-9, T1, T2, Gs1, Gs2);
            double gBright = GroundResponseSpectrumCalc.Gs(tB + 1e-9, T1, T2, Gs1, Gs2);
            Assert.AreEqual(Gs1, gBleft, 1e-6);
            Assert.AreEqual(Gs1, gBright, 1e-6);

            // 境界 tC: 区間3終端 = Gs1, 区間4始端 = Gs1
            double gCleft = GroundResponseSpectrumCalc.Gs(tC - 1e-9, T1, T2, Gs1, Gs2);
            double gCright = GroundResponseSpectrumCalc.Gs(tC + 1e-9, T1, T2, Gs1, Gs2);
            Assert.AreEqual(Gs1, gCleft, 1e-6);
            Assert.AreEqual(Gs1, gCright, 1e-6);
        }

        // ---------- 互換性: Gamma05==0 → ShallowSoilType フォールバック ----------
        [TestMethod]
        public void Compute_ZeroGamma05_FallsBackToShallowSoilTypeDefault()
        {
            // 旧データ (Gamma05=0) を読み込んだケース → ShallowSoilType に基づくフォールバック
            // 砂質土→γ0.5=7e-4, 粘性土→γ0.5=2e-3
            var massesSand = BuildHighStrainMasses(0.0);  // 0 → fallback
            var massesClay = BuildHighStrainMasses(0.0);

            var rSand = GroundResponseSpectrumCalc.Compute(massesSand, 20.0, 400.0, "砂質土", 1.0);
            var rClay = GroundResponseSpectrumCalc.Compute(massesClay, 20.0, 400.0, "粘性土", 1.0);

            Assert.IsFalse(double.IsNaN(rSand.T1));
            Assert.IsFalse(double.IsNaN(rClay.T1));
            // 粘性土の方が G の低下が少ない (γ0.5 が大きい)
            Assert.IsTrue(rClay.G[0] > rSand.G[0],
                $"粘性土フォールバック (γ0.5=2e-3) の G[0]={rClay.G[0]:F1} は砂質土フォールバック (γ0.5=7e-4) の G[0]={rSand.G[0]:F1} より大きいべき");
        }

        // ---------- per-layer HMax ----------
        [TestMethod]
        public void Compute_HigherHMax_GivesHigherDamping()
        {
            // 同条件で h_max を 0.10 と 0.30 に変えて、xiE が h_max=0.30 で大きいことを確認
            var massesLowH = BuildHighStrainMasses(0.0, 0.10);
            var massesHighH = BuildHighStrainMasses(0.0, 0.30);

            var rLow = GroundResponseSpectrumCalc.Compute(massesLowH, 20.0, 400.0, "砂質土", 1.0);
            var rHigh = GroundResponseSpectrumCalc.Compute(massesHighH, 20.0, 400.0, "砂質土", 1.0);

            Assert.IsFalse(double.IsNaN(rLow.T1));
            Assert.IsFalse(double.IsNaN(rHigh.T1));
            Assert.IsTrue(rHigh.XiE > rLow.XiE,
                $"h_max=0.30 の XiE={rHigh.XiE:F4} は h_max=0.10 の XiE={rLow.XiE:F4} より大きいべき");
        }

        private static ObservableCollection<GroundMassDataInput> BuildHighStrainMasses(double gamma05 = 0.0, double hMax = 0.0)
        {
            var masses = new ObservableCollection<GroundMassDataInput>();
            for (int i = 0; i < 4; i++)
            {
                masses.Add(new GroundMassDataInput
                {
                    H = 5.0, Density = 17.0, VS0 = 150.0,
                    Mass = 17.0 / 9.80665 * 5.0, IsEngineeringBedrock = false,
                    Gamma05 = gamma05, HMax = hMax
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
