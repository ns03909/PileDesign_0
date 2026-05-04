using PileDesign.Models;
using PileDesign.Models.PileLibrary;

namespace TestProject1
{
    /// <summary>
    /// キャプリングパイル工法 (CapringPile) の設計式・単位・境界条件を保証する単体テスト群。
    ///
    /// 検証範囲:
    /// 1. M-θ バイリニア構造 (3 点)、限界回転角 0.03 rad
    /// 2. 3 ケース分岐 (引張定着筋 有無 × 軸力 圧縮/引張)
    /// 3. 直列ばね Ke 計算、コンクリート充填鋼管部の合成 EI
    /// 4. Ny / Nty / Kty / Mr の符号と境界連続性
    /// 5. 標準ライブラリ (PCRing N/S1/S2 × 12 径、引張定着筋 10 標準配筋) のロード
    /// 6. 杭頭諸元合成 (PCリング + 引張定着筋諸元)
    ///
    /// 単位: 入力 N [N], 出力 M [N·mm], θ [rad], K [N·mm/rad]
    /// (PileBodyInput が kN→N と N·mm→kN·m を変換するのは別レイヤの責務)
    /// </summary>
    [TestClass]
    public class CapringPileTests
    {
        // ------------------------------------------------------------
        // テスト用ヘルパ: 700mm 杭 (PHC杭, 既製コンクリート杭) の CapringPile を構築
        // ------------------------------------------------------------
        private static CapringPile CreateBasic700(bool hasTensionBars = false)
        {
            var ring = new CapringPCRing
            {
                D = 700,
                RD1 = 720,
                RD2 = 912,
                SD = 832,
                Tc = 87,
                Hr = 150,
                BarNum = 10,
                BarSize = "D16",
                L1 = 350,
                Name = "700N",
                Ts = 9.0,
                SteelGrade = "SS400",
                SpiralDia = "U7.1",
                SpiralNum = 6,
            };
            var cp = new CapringPile
            {
                PileBodyType = "既製コンクリート杭",
                PileCapFc = 24.0,
                PileCapEc = 22669.0,
                PCRing = ring,
                D = ring.D,
                HasTensionBars = hasTensionBars,
            };
            if (hasTensionBars)
            {
                cp.TensionBar = new CapringTensionBar
                {
                    No = 1,
                    BarNum = 3,
                    BarSize = "D19",
                    Dc = 110,
                    HoopOutDia = 150,
                    AnchorLengthCapWithPlate = 500,
                    AnchorLengthCapWithoutPlate = 800,
                    AnchorLengthPileSide = 800,
                    MinPileDia = 300,
                };
                cp.TensionBarGrade = "SD345";
            }
            cp.Update();
            return cp;
        }

        // ============================================================
        // 1. 基本的なモデル形状 (バイリニア・限界回転角)
        // ============================================================
        [TestMethod]
        public void ThetaU_IsExactly_0_03_rad()
        {
            Assert.AreEqual(0.03, CapringPile.ThetaU);
        }

        [TestMethod]
        public void Update_PopulatesKe_Positive()
        {
            var cp = CreateBasic700();
            Assert.IsTrue(cp.Ke > 0, $"Ke should be > 0 after Update. Got {cp.Ke}");
        }

        [TestMethod]
        public void GetMThetaRelationship_Returns3PointsBilinear()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            double N = 1_000_000.0; // 1000 kN
            var (thetas, ms) = cp.GetMThetaRelationship(N);
            Assert.AreEqual(3, thetas.Count, "M-θ should be 3-point bilinear (origin, (θy,Mu), (θu,Mu))");
            Assert.AreEqual(3, ms.Count);
            // 第1点 = 原点
            Assert.AreEqual(0.0, thetas[0], 1e-12);
            Assert.AreEqual(0.0, ms[0], 1e-12);
            // 第3点 θ = θu = 0.03
            Assert.AreEqual(CapringPile.ThetaU, thetas[2], 1e-12);
            // 第2点と第3点で M は同じ (バイリニアの上水平)
            Assert.AreEqual(ms[1], ms[2], 1.0,
                "Bilinear: M at (θy,Mu) should equal M at (θu,Mu)");
        }

        // ============================================================
        // 2. ケース① 引張定着筋なし × 軸力圧縮: Mu = (D/2)·N, Ki = Ke
        // ============================================================
        [TestMethod]
        public void Case1_NoTensionBars_Compression_Mu_Equals_HalfD_Times_N()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            double N = 1_000_000.0; // 1000 kN
            (double ki, double mu) = cp.GetKiMu(N);
            double expected = (cp.D / 2.0) * N; // mm × N = N·mm
            Assert.AreEqual(expected, mu, 1.0,
                $"Case 1 (圧縮 引張定着筋なし): Mu = (D/2)·N. expected={expected}, got={mu}");
            Assert.AreEqual(cp.Ke, ki, cp.Ke * 1e-9,
                "Case 1: Ki = Ke");
        }

        [TestMethod]
        public void Case1_Mu_LinearWithCompression()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            (_, double mu1) = cp.GetKiMu(1_000_000.0);
            (_, double mu5) = cp.GetKiMu(5_000_000.0);
            Assert.AreEqual(5.0, mu5 / mu1, 1e-6,
                $"Case 1: Mu should be linear in N. Ratio mu5/mu1 expected 5.0, got {mu5/mu1}");
        }

        // ============================================================
        // 3. ケース② 引張定着筋あり × 軸力圧縮: Mu = (D/2)·N + Mr, Ki = Ke
        // ============================================================
        [TestMethod]
        public void Case2_WithTensionBars_Compression_Mu_Equals_HalfD_N_Plus_Mr()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double N = 1_000_000.0;
            double mr = cp.GetMr();
            (double ki, double mu) = cp.GetKiMu(N);
            double expected = (cp.D / 2.0) * N + mr;
            Assert.AreEqual(expected, mu, 1.0,
                $"Case 2: Mu = (D/2)·N + Mr. expected={expected}, got={mu}");
            Assert.AreEqual(cp.Ke, ki, cp.Ke * 1e-9,
                "Case 2: Ki = Ke");
        }

        // ============================================================
        // 4. ケース③ 引張定着筋あり × 軸力引張: 楕円相互作用、Mu = Mr·(1-|N|/Ny)
        // ============================================================
        [TestMethod]
        public void Case3_AtNTensionZero_KiApproachesKe()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            // |N| → 0 の極限で楕円式: x = (-Nty)/Nty = -1, sqrt(1-1)=0, Ki = Ke - 0 = Ke
            // ただし |N|=1 N で評価すると Nty (~256 kN) との比から x = -1 + 1e-6 程度の誤差で
            // Ki = Ke - (Ke-Kty)·sqrt(2·|N|/Nty) ≈ Ke·(1 - 2.79e-3·(1-Kty/Ke)) になる。
            // 「Ki が Ke に十分近い」ことを 1% 許容で確認する。
            (double ki, _) = cp.GetKiMu(-1.0);
            Assert.AreEqual(cp.Ke, ki, cp.Ke * 0.01,
                $"Case 3 at |N|→0: Ki should approach Ke (within 1%). Got {ki}, Ke={cp.Ke}");
        }

        [TestMethod]
        public void Case3_AtNty_KiEqualsKty()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double nty = cp.GetNty();
            double kty = cp.GetKty();
            (double ki, _) = cp.GetKiMu(-nty);
            // |N|=Nty で楕円式: x=0, sqrt(1)=1, Ki = Ke - (Ke-Kty)·1 = Kty
            Assert.AreEqual(kty, ki, kty * 1e-3,
                $"Case 3 at |N|=Nty: Ki = Kty. expected={kty}, got={ki}");
        }

        [TestMethod]
        public void Case3_BeyondNty_KiClampedToKty()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double nty = cp.GetNty();
            double kty = cp.GetKty();
            (double ki, _) = cp.GetKiMu(-nty * 2.0);
            Assert.AreEqual(kty, ki, kty * 1e-9,
                $"Case 3 at |N|>Nty: Ki should be clamped to Kty. Got {ki}");
        }

        [TestMethod]
        public void Case3_AtMinusNy_MuIsZero()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double ny = cp.GetNy();
            (_, double mu) = cp.GetKiMu(-ny);
            Assert.AreEqual(0.0, mu, 1.0,
                $"Case 3 at |N|=Ny: Mu should be 0. Got {mu}");
        }

        [TestMethod]
        public void Case3_BeyondMinusNy_MuClampedToZero()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double ny = cp.GetNy();
            (_, double mu) = cp.GetKiMu(-ny * 1.5);
            Assert.AreEqual(0.0, mu, 1.0,
                $"Case 3 at |N|>Ny: Mu should be 0 (clamped, no negative). Got {mu}");
        }

        // ============================================================
        // 5. 境界連続性: ケース②/③ at N=0 で Mu が連続
        // ============================================================
        [TestMethod]
        public void Case2_3_Boundary_AtZero_Mu_Continuous_AtMr()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double mr = cp.GetMr();
            (_, double mu_pos) = cp.GetKiMu(1.0);   // N=0+
            (_, double mu_neg) = cp.GetKiMu(-1.0);  // N=0-
            Assert.AreEqual(mr, mu_pos, mr * 1e-3,
                "Case 2 at N→0+: Mu should equal Mr");
            Assert.AreEqual(mr, mu_neg, mr * 1e-3,
                "Case 3 at N→0-: Mu should equal Mr");
            Assert.AreEqual(mu_pos, mu_neg, mr * 1e-3,
                "Case 2/3 boundary at N=0: Mu should be continuous");
        }

        // ============================================================
        // 6. 引張定着筋関連量の符号と単位
        // ============================================================
        [TestMethod]
        public void Mr_Positive_WithTensionBars()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            Assert.IsTrue(cp.GetMr() > 0,
                $"Mr should be > 0 with tension bars. Got {cp.GetMr()}");
        }

        [TestMethod]
        public void Mr_Zero_WithoutTensionBars()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            Assert.AreEqual(0.0, cp.GetMr(), 1e-6);
        }

        [TestMethod]
        public void Ny_PositiveAndConsistentWithBars()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            double ny = cp.GetNy();
            // Ny = ns·as·σy = 3 × 286.5 × 345 ≈ 296,528 N (D19 SD345)
            Assert.IsTrue(ny > 200_000 && ny < 400_000,
                $"Ny for 3-D19 SD345 should be ~296 kN. Got {ny}");
        }

        [TestMethod]
        public void Nty_LessThan_Ny()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            cp.D = 300.0; // 例外条件外 (D < 400) → CSV 既定の Dc=110 が有効
            double ny = cp.GetNy();
            double nty = cp.GetNty();
            // Nty = Ny × D/(D+Dc) < Ny
            Assert.IsTrue(nty < ny, $"Nty should be < Ny. Got Nty={nty}, Ny={ny}");
            // For D=300, Dc=110: Nty = Ny × 300/410 ≈ 0.732 Ny
            Assert.AreEqual(0.732, nty / ny, 0.005,
                $"Nty/Ny ratio for D=300,Dc=110 should be ~0.732");
        }

        [TestMethod]
        public void Z_FormulaConsistency()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            cp.D = 300.0; // 例外条件外 → Dc=110 の CSV 値で公式整合性を検証
            double z = cp.GetZ();
            Assert.IsTrue(z > 0, $"Z (equivalent ring section modulus) should be > 0. Got {z}");
            // Z [mm³] for ns=3, as=286.5 (D19), Dc=110:
            // Z = π/32 × (Dc^4 - (Dc² - 4·ns·as/π)²) / Dc
            double a = 3 * 286.5;
            double inner = 110.0 * 110.0 - 4 * a / Math.PI;
            double zExpected = Math.PI / 32.0 * (Math.Pow(110.0, 4) - inner * inner) / 110.0;
            Assert.AreEqual(zExpected, z, zExpected * 1e-6,
                $"Z formula at D=300, Dc=110. expected={zExpected}, got={z}");
        }

        // ============================================================
        // 7. 鋼材鋼種の降伏強度
        // ============================================================
        [TestMethod]
        public void TensionBarGrade_SD345_SigmaY_Is_345()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            cp.TensionBarGrade = "SD345";
            Assert.AreEqual(345.0, cp.SigmaY);
        }

        [TestMethod]
        public void TensionBarGrade_SD390_SigmaY_Is_390()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            cp.TensionBarGrade = "SD390";
            Assert.AreEqual(390.0, cp.SigmaY);
        }

        [TestMethod]
        public void TensionBarGrade_SD390_NyHigherThan_SD345()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            cp.TensionBarGrade = "SD345";
            double ny345 = cp.GetNy();
            cp.TensionBarGrade = "SD390";
            double ny390 = cp.GetNy();
            Assert.AreEqual(390.0 / 345.0, ny390 / ny345, 1e-6,
                $"Ny ratio SD390/SD345 should equal 390/345. Got {ny390/ny345}");
        }

        // ============================================================
        // 8. 鋼管杭+キャプリング: コンクリート充填鋼管部の合成 EI
        // ============================================================
        [TestMethod]
        public void CompositeEpIp_ForConcreteFilledSteelPipe()
        {
            var ring800 = new CapringPCRing
            {
                D = 800, RD1 = 820, RD2 = 1012, SD = 932, Tc = 87, Hr = 150,
                BarNum = 10, BarSize = "D16", L1 = 350, Name = "800N",
                Ts = 9.0, SteelGrade = "SS400", SpiralDia = "U7.1", SpiralNum = 6,
            };
            var cp = new CapringPile
            {
                PileBodyType = "鋼管杭",
                PileCapFc = 24.0,
                PileCapEc = 22669.0,
                PCRing = ring800,
                D = 800.0,
                IsConcreteFilledSteelPipe = true,
                SteelPipeWallThickness = 17.0,
            };
            cp.Update();

            double dOut = 800.0;
            double dIn = dOut - 2 * 17.0; // 766
            double iPipe = Math.PI / 64.0 * (Math.Pow(dOut, 4) - Math.Pow(dIn, 4));
            double iFill = Math.PI / 64.0 * Math.Pow(dIn, 4);
            double expected = CapringPile.EsSteelPipe * iPipe + cp.PileCapEc * iFill;
            Assert.AreEqual(expected, cp.CompositeEpIp, expected * 1e-6,
                $"CompositeEpIp = E_steel·I_pipe + E_concrete·I_filled. expected={expected}, got={cp.CompositeEpIp}");
        }

        [TestMethod]
        public void CompositeEpIp_LargerThan_ConcreteOnly()
        {
            var ring800 = new CapringPCRing
            {
                D = 800, RD1 = 820, RD2 = 1012, Hr = 150, Ts = 9.0
            };
            var cpConcrete = new CapringPile
            {
                PileBodyType = "既製コンクリート杭",
                PileCapEc = 22669.0,
                PCRing = ring800,
                D = 800.0,
            };
            cpConcrete.Update();

            var cpSteel = new CapringPile
            {
                PileBodyType = "鋼管杭",
                PileCapEc = 22669.0,
                PCRing = ring800,
                D = 800.0,
                IsConcreteFilledSteelPipe = true,
                SteelPipeWallThickness = 17.0,
            };
            cpSteel.Update();

            // 鋼管 (E=205000) は コンクリート (E=22669) より E が約 9 倍大きいため
            // 合成 EpIp は コンクリート単独の EpIp より大きいはず
            double epipConcrete = cpConcrete.Ep * cpConcrete.Ip;
            double epipComposite = cpSteel.CompositeEpIp;
            Assert.IsTrue(epipComposite > epipConcrete,
                $"Composite EpIp ({epipComposite}) should exceed concrete-only ({epipConcrete})");
        }

        // ============================================================
        // 9. 杭頭諸元合成 (PCリング + 引張定着筋)
        // ============================================================
        [TestMethod]
        public void GetCombinedSpecs_NoTensionBars_HasOnlyRingSpecs()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            var specs = cp.GetCombinedSpecs();
            Assert.IsTrue(specs.Count >= 12,
                $"PCリング諸元 should have >=12 entries. Got {specs.Count}");
            foreach (var s in specs)
                Assert.IsFalse(s.Item.Contains("引張定着筋"),
                    $"Should not contain 引張定着筋 entry: {s.Item}");
        }

        [TestMethod]
        public void GetCombinedSpecs_WithTensionBars_IncludesBarSpecs()
        {
            var cp = CreateBasic700(hasTensionBars: true);
            var specs = cp.GetCombinedSpecs();
            bool hasBarHeader = false;
            bool hasGrade = false;
            bool hasDc = false;
            foreach (var s in specs)
            {
                if (s.Item == "引張定着筋") hasBarHeader = true;
                if (s.Item == "引張定着筋鋼種") hasGrade = true;
                if (s.Item == "引張定着筋配置径") hasDc = true;
            }
            Assert.IsTrue(hasBarHeader, "Combined specs should include 引張定着筋 row");
            Assert.IsTrue(hasGrade, "Combined specs should include 引張定着筋鋼種 row");
            Assert.IsTrue(hasDc, "Combined specs should include 引張定着筋配置径 row");
        }

        // ============================================================
        // 10. 標準ライブラリ (CSV) ロード
        // ============================================================
        [TestMethod]
        public void Library_LoadPCRingOptions_LoadsAll36StandardSizes()
        {
            var cp = new CapringPile(22669.0);
            cp.LoadPCRingOptions();
            Assert.AreEqual(36, cp.PCRings.Count,
                "CapringPCRing CSV should have 36 rows (3 タイプ × 12 径)");
            // N/S1/S2 各 12 サイズ
            int nCount = 0, s1Count = 0, s2Count = 0;
            foreach (var r in cp.PCRings)
            {
                if ((r.Name ?? "").EndsWith("N")) nCount++;
                else if ((r.Name ?? "").EndsWith("S1")) s1Count++;
                else if ((r.Name ?? "").EndsWith("S2")) s2Count++;
            }
            Assert.AreEqual(12, nCount, "12 N-type rings");
            Assert.AreEqual(12, s1Count, "12 S1-type rings");
            Assert.AreEqual(12, s2Count, "12 S2-type rings");
        }

        [TestMethod]
        public void Library_LoadTensionBarOptions_Loads10StandardConfigurations()
        {
            var cp = new CapringPile(22669.0);
            cp.LoadTensionBarOptions();
            Assert.AreEqual(10, cp.TensionBars.Count,
                "CapringTensionBar CSV should have 10 rows (3-D19 〜 5-D38)");
        }

        [TestMethod]
        public void Library_PCRing_700N_HasExpectedDimensions()
        {
            var cp = new CapringPile(22669.0);
            cp.LoadPCRingOptions();
            CapringPCRing? r = null;
            foreach (var x in cp.PCRings)
                if (x.Name == "700N") { r = x; break; }
            Assert.IsNotNull(r, "PCRing 700N should exist");
            Assert.AreEqual(700.0, r!.D);
            Assert.AreEqual(720.0, r.RD1);
            Assert.AreEqual(912.0, r.RD2);
            Assert.AreEqual(150.0, r.Hr);
            Assert.AreEqual(10, r.BarNum);
            Assert.AreEqual("D16", r.BarSize);
        }

        // ============================================================
        // 11. M-θ 曲線描画用: GetNMThetaRelationship が複数曲線を返す
        // ============================================================
        [TestMethod]
        public void GetNMThetaRelationship_Returns11AxialSamples()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            var (ns, tms) = cp.GetNMThetaRelationship();
            Assert.AreEqual(11, ns.Count, "Should sample 11 axial values (0..10 inclusive)");
            Assert.AreEqual(11, tms.Count);
            // 各曲線が 3 点
            foreach (var (thetas, ms) in tms)
            {
                Assert.AreEqual(3, thetas.Count);
                Assert.AreEqual(3, ms.Count);
            }
        }

        // ============================================================
        // 12. 単位スモークテスト: 設計上の典型値が現実的範囲に入る
        // ============================================================
        [TestMethod]
        public void TypicalCase_700mmPile_1000kN_MuIsRealistic()
        {
            // D=700mm, N=1000 kN (= 1e6 N) のとき Mu = D/2·N = 350 mm × 1e6 N = 3.5e8 N·mm = 350 kN·m
            var cp = CreateBasic700(hasTensionBars: false);
            (_, double mu) = cp.GetKiMu(1_000_000.0);
            double muKnm = mu * 1e-6; // N·mm → kN·m
            Assert.AreEqual(350.0, muKnm, 1.0,
                $"D=700mm, N=1000kN: Mu should be 350 kN·m. Got {muKnm} kN·m");
        }

        [TestMethod]
        public void TypicalCase_700mmPile_5000kN_MuIsRealistic()
        {
            var cp = CreateBasic700(hasTensionBars: false);
            (_, double mu) = cp.GetKiMu(5_000_000.0);
            double muKnm = mu * 1e-6;
            Assert.AreEqual(1750.0, muKnm, 1.0,
                $"D=700mm, N=5000kN: Mu should be 1750 kN·m. Got {muKnm} kN·m");
        }

        // ============================================================
        // 13. 例外ルール: 3-D19 配筋を D ≥ 400 mm 杭に適用する場合 Dc=180, 帯筋外径=220
        // ============================================================
        [TestMethod]
        public void ExceptionRule_3D19_D300_UsesDefaultDc110()
        {
            // 杭径 D=300 (例外条件外) → CSV 既定の Dc=110 を使用
            var cp = CreateBasic700(hasTensionBars: true);
            cp.D = 300.0;
            Assert.AreEqual(110.0, cp.EffectiveDc, 1e-9,
                $"3-D19 + D=300: EffectiveDc should be 110 (default). Got {cp.EffectiveDc}");
            Assert.AreEqual(150.0, cp.EffectiveHoopOutDia, 1e-9,
                $"3-D19 + D=300: EffectiveHoopOutDia should be 150 (default). Got {cp.EffectiveHoopOutDia}");
        }

        [TestMethod]
        public void ExceptionRule_3D19_D400_UsesExceptionDc180()
        {
            // 杭径 D=400 (例外条件境界) → 例外ルールで Dc=180
            var cp = CreateBasic700(hasTensionBars: true);
            cp.D = 400.0;
            Assert.AreEqual(180.0, cp.EffectiveDc, 1e-9,
                $"3-D19 + D=400: EffectiveDc should be 180 (exception). Got {cp.EffectiveDc}");
            Assert.AreEqual(220.0, cp.EffectiveHoopOutDia, 1e-9,
                $"3-D19 + D=400: EffectiveHoopOutDia should be 220 (exception). Got {cp.EffectiveHoopOutDia}");
        }

        [TestMethod]
        public void ExceptionRule_3D19_D700_UsesExceptionDc180()
        {
            // 杭径 D=700 → 例外ルール継続適用で Dc=180
            var cp = CreateBasic700(hasTensionBars: true);
            // CreateBasic700 で D=700 既設定
            Assert.AreEqual(700.0, cp.D);
            Assert.AreEqual(180.0, cp.EffectiveDc, 1e-9);
            Assert.AreEqual(220.0, cp.EffectiveHoopOutDia, 1e-9);
        }

        [TestMethod]
        public void ExceptionRule_NotApplied_For_4D19_AtAnyDiameter()
        {
            // 4-D19 (BarNum=4) は例外対象外。CSV 既定 Dc=180, HoopOutDia=220 (たまたま値は同じだが、3-D19 の例外ロジックには引っかからない)
            var cp = CreateBasic700(hasTensionBars: true);
            cp.TensionBar = new CapringTensionBar
            {
                No = 2, BarNum = 4, BarSize = "D19", Dc = 180,
                HoopOutDia = 220, AnchorLengthCapWithPlate = 500,
                AnchorLengthCapWithoutPlate = 800, AnchorLengthPileSide = 800,
                MinPileDia = 400,
            };
            cp.D = 700.0;
            Assert.AreEqual(180.0, cp.EffectiveDc, 1e-9,
                "4-D19: EffectiveDc should be 180 from CSV (no exception applied)");
            Assert.AreEqual(220.0, cp.EffectiveHoopOutDia, 1e-9);
        }

        [TestMethod]
        public void ExceptionRule_AffectsZAndKtyAndNty()
        {
            // 例外適用前後で Z, Kty, Nty の値が変わることを確認
            var cp1 = CreateBasic700(hasTensionBars: true);
            cp1.D = 300.0;  // 例外なし
            cp1.Update();
            double z1 = cp1.GetZ();
            double nty1 = cp1.GetNty();
            double kty1 = cp1.GetKty();

            var cp2 = CreateBasic700(hasTensionBars: true);
            cp2.D = 400.0;  // 例外あり
            cp2.Update();
            double z2 = cp2.GetZ();
            double nty2 = cp2.GetNty();
            double kty2 = cp2.GetKty();

            // Dc が 110→180 に増えるので Z は増える (薄肉円環近似で Z ≈ Dc·a/4 ∝ Dc、180/110 ≈ 1.64 倍)
            double expectedRatio = 180.0 / 110.0;
            double actualRatio = z2 / z1;
            Assert.IsTrue(actualRatio > 1.5,
                $"Z (Dc=180) should be ~{expectedRatio:F2}× larger than Z (Dc=110). " +
                $"z1={z1}, z2={z2}, ratio={actualRatio:F3}");

            // 同様に Nty / Kty も Dc 増加で変化する (Nty は微減 / Kty は増加方向)
            Assert.AreNotEqual(nty1, nty2, "Nty should differ when Dc differs");
            Assert.AreNotEqual(kty1, kty2, "Kty should differ when Dc differs");
        }

        [TestMethod]
        public void ExceptionRule_GetCombinedSpecs_ShowsEffectiveValues()
        {
            var cp = CreateBasic700(hasTensionBars: true);  // D=700 → 例外適用
            var specs = cp.GetCombinedSpecs();
            string? dcValue = null;
            string? hoopValue = null;
            foreach (var s in specs)
            {
                if (s.Item == "引張定着筋配置径") dcValue = s.Value;
                if (s.Item == "引張定着筋帯筋外径") hoopValue = s.Value;
            }
            Assert.IsNotNull(dcValue);
            Assert.IsNotNull(hoopValue);
            Assert.IsTrue(dcValue!.StartsWith("180"),
                $"Dc spec should show 180 with note. Got: {dcValue}");
            Assert.IsTrue(hoopValue!.StartsWith("220"),
                $"HoopOutDia spec should show 220 with note. Got: {hoopValue}");
            Assert.IsTrue(dcValue.Contains("例外"),
                $"Dc spec should include exception note. Got: {dcValue}");
        }
    }
}
