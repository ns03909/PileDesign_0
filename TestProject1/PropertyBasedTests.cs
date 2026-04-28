using FsCheck;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    /// <summary>
    /// FsCheck によるプロパティベーステスト。
    ///
    /// 通常のテストが「特定の入力値」に対する期待値を確認するのに対し、
    /// プロパティベーステストは「任意の入力に対して成り立つべき不変条件」を
    /// FsCheck が自動生成する 100 個のランダム入力で検証する。
    /// 失敗時は FsCheck が shrink によって最小再現ケースを表示する。
    /// </summary>
    [TestClass]
    public class PropertyBasedTests
    {
        // --- Sa0(T): 加速度応答スペクトル (論文式 2 / 告示 1457) ---------------
        //   T <= 0.16     : 3.2 + 30·T          (線形増加, 0→3.2, 0.16→8.0)
        //   0.16 < T <= 0.64 : 8.0              (定数 plateau)
        //   T > 0.64      : 5.12 / T            (双曲線で減少, 0.64→8.0)
        //
        // 不変条件:
        //  P1. T > 0 (有限) なら Sa0(T) > 0
        //  P2. T = 0.16 で連続 (左右極限が一致)
        //  P3. T = 0.64 で連続
        //  P4. (0, 0.16] では狭義単調増加
        //  P5. (0.64, ∞) では狭義単調減少
        //  P6. [0.16, 0.64] では一定 (8.0)

        [TestMethod]
        public void Sa0_PositiveT_ProducesPositiveValue()
        {
            Prop.ForAll<double>(t =>
            {
                if (!double.IsFinite(t) || t <= 0 || t > 1e6) return true; // 範囲外は discard 相当
                return GroundResponseSpectrumCalc.Sa0(t) > 0;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_AllFiniteT_ProducesFiniteValue()
        {
            Prop.ForAll<double>(t =>
            {
                if (!double.IsFinite(t) || t <= 1e-10 || t > 1e6) return true;
                return double.IsFinite(GroundResponseSpectrumCalc.Sa0(t));
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_ContinuousAt_T_0_16()
        {
            // 下枝 (3.2 + 30·T) の傾き = 30, 上枝 (plateau) の傾き = 0
            // → |Sa0(0.16 ± eps) - Sa0(0.16)| <= 30·eps + 数値誤差
            Prop.ForAll<double>(rawEps =>
            {
                if (!double.IsFinite(rawEps)) return true;
                var eps = Math.Abs(rawEps) % 1e-6; // [0, 1e-6)
                if (eps < 1e-15) return true;

                var below = GroundResponseSpectrumCalc.Sa0(0.16 - eps);
                var at = GroundResponseSpectrumCalc.Sa0(0.16);
                var above = GroundResponseSpectrumCalc.Sa0(0.16 + eps);

                var tol = 30.0 * eps + 1e-13;
                return Math.Abs(below - at) <= tol && Math.Abs(above - at) <= tol;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_ContinuousAt_T_0_64()
        {
            // 下枝 (plateau) の傾き = 0, 上枝 (5.12/T) の傾き = -5.12/T² = -12.5 at T=0.64
            // → tol = 12.5·eps + 数値誤差
            Prop.ForAll<double>(rawEps =>
            {
                if (!double.IsFinite(rawEps)) return true;
                var eps = Math.Abs(rawEps) % 1e-6;
                if (eps < 1e-15) return true;

                var below = GroundResponseSpectrumCalc.Sa0(0.64 - eps);
                var at = GroundResponseSpectrumCalc.Sa0(0.64);
                var above = GroundResponseSpectrumCalc.Sa0(0.64 + eps);

                var tol = 12.5 * eps + 1e-13;
                return Math.Abs(below - at) <= tol && Math.Abs(above - at) <= tol;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_StrictlyIncreasing_OnLowBranch()
        {
            // (0, 0.16] では狭義単調増加: t1 < t2 なら Sa0(t1) < Sa0(t2)
            Prop.ForAll<double, double>((a, b) =>
            {
                if (!double.IsFinite(a) || !double.IsFinite(b)) return true;
                var t1 = Math.Min(Math.Abs(a), Math.Abs(b));
                var t2 = Math.Max(Math.Abs(a), Math.Abs(b));
                if (t1 <= 0 || t2 > 0.16 || t1 == t2) return true;

                return GroundResponseSpectrumCalc.Sa0(t1) < GroundResponseSpectrumCalc.Sa0(t2);
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_StrictlyDecreasing_OnHighBranch()
        {
            // (0.64, ∞) では狭義単調減少
            Prop.ForAll<double, double>((a, b) =>
            {
                if (!double.IsFinite(a) || !double.IsFinite(b)) return true;
                var t1 = 0.64 + 1e-3 + Math.Abs(a) % 100;   // (0.641, 100.641)
                var t2 = t1 + 1e-3 + Math.Abs(b) % 100;
                if (t1 == t2) return true;

                return GroundResponseSpectrumCalc.Sa0(t1) > GroundResponseSpectrumCalc.Sa0(t2);
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Sa0_ConstantOnPlateau()
        {
            // [0.16, 0.64] で 8.0 一定
            Prop.ForAll<double>(rawT =>
            {
                if (!double.IsFinite(rawT)) return true;
                // [0.16, 0.64] にマップ
                var u = (Math.Abs(rawT) % 1.0);
                var t = 0.16 + u * (0.64 - 0.16);

                return Math.Abs(GroundResponseSpectrumCalc.Sa0(t) - 8.0) < 1e-12;
            }).QuickCheckThrowOnFailure();
        }

        // --- Fh(xi): 減衰補正係数 (論文式 4) ----------------------------------
        //   Fh(xi) = 1.5 / (1 + 10·xi)
        //
        // 不変条件:
        //  Q1. xi >= 0 で Fh > 0
        //  Q2. Fh(0) == 1.5
        //  Q3. xi >= 0 で Fh は狭義単調減少
        //  Q4. xi >= 0 で Fh <= 1.5

        [TestMethod]
        public void Fh_AtZero_Equals1_5()
        {
            Assert.AreEqual(1.5, GroundResponseSpectrumCalc.Fh(0.0), 1e-15);
        }

        [TestMethod]
        public void Fh_NonNegativeXi_PositiveAndBounded()
        {
            Prop.ForAll<double>(rawXi =>
            {
                if (!double.IsFinite(rawXi)) return true;
                var xi = Math.Abs(rawXi) % 10.0;  // [0, 10)
                var fh = GroundResponseSpectrumCalc.Fh(xi);
                return fh > 0 && fh <= 1.5 + 1e-12;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Fh_StrictlyDecreasing_ForNonNegativeXi()
        {
            Prop.ForAll<double, double>((a, b) =>
            {
                if (!double.IsFinite(a) || !double.IsFinite(b)) return true;
                var xi1 = Math.Abs(a) % 5.0;
                var xi2 = Math.Abs(b) % 5.0;
                // ギャップが浮動小数点の分解能未満だと Fh の差も丸められて 0 になる (denormal 起因)
                // 例: xi1 = 4.94e-324 (smallest subnormal) では 1 + 10·xi ≈ 1 で同値判定になる
                if (Math.Abs(xi1 - xi2) < 1e-15) return true;
                var (lo, hi) = xi1 < xi2 ? (xi1, xi2) : (xi2, xi1);

                return GroundResponseSpectrumCalc.Fh(lo) > GroundResponseSpectrumCalc.Fh(hi);
            }).QuickCheckThrowOnFailure();
        }

        // --- DeepCopyUtil.CloneJson<T>: JSON 往復による deep copy ----------------
        //
        // 不変条件:
        //  R1. プリミティブ (int / double / string) はビット完全に往復する
        //  R2. NaN / ±∞ は AllowNamedFloatingPointLiterals により往復可能
        //  R3. 配列の要素・順序は保たれる
        //  R4. 単純なドメインオブジェクトの double プロパティは保たれる
        //  R5. 元オブジェクトと clone は別インスタンス (参照不変ではない)

        [TestMethod]
        public void DeepCopy_Int_Roundtrips()
        {
            Prop.ForAll<int>(x => DeepCopyUtil.CloneJson(x) == x)
                .QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void DeepCopy_FiniteDouble_Roundtrips()
        {
            // 注意: ビット完全比較 (BitConverter) は -0.0 / +0.0 で別ビット表現になるため
            // 値レベル == 比較を採用する。-0.0 は System.Text.Json の数値表現で +0.0 に
            // 落ちることがあるが、数値計算上は等価なので問題ない。
            Prop.ForAll<double>(x =>
            {
                if (!double.IsFinite(x)) return true;
                var clone = DeepCopyUtil.CloneJson(x);
                return clone == x;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void DeepCopy_NonFiniteDouble_Roundtrips()
        {
            // DeepCopyUtil は AllowNamedFloatingPointLiterals 設定なので NaN/Inf も往復するはず
            foreach (var v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var clone = DeepCopyUtil.CloneJson(v);
                if (double.IsNaN(v))
                    Assert.IsTrue(double.IsNaN(clone), $"NaN が往復しない (got {clone})");
                else
                    Assert.AreEqual(v, clone, $"{v} が往復しない (got {clone})");
            }
        }

        [TestMethod]
        public void DeepCopy_String_Roundtrips()
        {
            Prop.ForAll<string>(s =>
            {
                var clone = DeepCopyUtil.CloneJson(s);
                return s == null ? clone == null : clone == s;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void DeepCopy_DoubleArray_RoundtripsElementsAndOrder()
        {
            Prop.ForAll<double[]>(arr =>
            {
                if (arr == null) return true;
                if (arr.Any(d => !double.IsFinite(d))) return true; // 非有限はスキップ
                var clone = DeepCopyUtil.CloneJson(arr);
                if (clone == null) return false;
                if (clone.Length != arr.Length) return false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (clone[i] != arr[i]) return false; // 値レベル比較 (-0.0 / +0.0 は等価)
                }
                return true;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void DeepCopy_GroundInput_PreservesScalarFields()
        {
            // 任意の (有限) GroundTopAltitude / GroundWaterTableAltitude 組合せに対して
            // GroundInput を構築し、deep copy 後も値が保たれることを確認
            Prop.ForAll<double, double>((topA, waterA) =>
            {
                if (!double.IsFinite(topA) || !double.IsFinite(waterA)) return true;
                var src = new GroundInput
                {
                    GroundTopAltitude = topA,
                    GroundWaterTableAltitude = waterA,
                };

                var clone = DeepCopyUtil.CloneJson(src);
                Assert.IsNotNull(clone, $"clone is null for topA={topA}, waterA={waterA}");

                // src の現在値 (BaseModel.SetProperty が -0.0/0.0 を同一視する点に注意) と
                // clone の値が一致することを確認。値レベル == 比較。
                Assert.AreEqual(src.GroundTopAltitude, clone.GroundTopAltitude,
                    $"GroundTopAltitude mismatch: src={src.GroundTopAltitude:G17} clone={clone.GroundTopAltitude:G17}");
                Assert.AreEqual(src.GroundWaterTableAltitude, clone.GroundWaterTableAltitude,
                    $"GroundWaterTableAltitude mismatch: src={src.GroundWaterTableAltitude:G17} clone={clone.GroundWaterTableAltitude:G17}");
                return true;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void DeepCopy_GroundInput_ManualDebug_Zeros()
        {
            var src = new GroundInput { GroundTopAltitude = 0.0, GroundWaterTableAltitude = 0.0 };
            var clone = DeepCopyUtil.CloneJson(src);
            Assert.IsNotNull(clone);
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(src.GroundTopAltitude),
                BitConverter.DoubleToInt64Bits(clone.GroundTopAltitude),
                $"GroundTopAltitude src={src.GroundTopAltitude} clone={clone.GroundTopAltitude}");
            Assert.AreEqual(
                BitConverter.DoubleToInt64Bits(src.GroundWaterTableAltitude),
                BitConverter.DoubleToInt64Bits(clone.GroundWaterTableAltitude),
                $"GroundWaterTableAltitude src={src.GroundWaterTableAltitude} clone={clone.GroundWaterTableAltitude}");
        }

        [TestMethod]
        public void DeepCopy_ProducesNewInstance()
        {
            var src = new GroundInput { GroundTopAltitude = 1.5 };
            var clone = DeepCopyUtil.CloneJson(src);
            Assert.IsNotNull(clone);
            Assert.AreNotSame(src, clone, "clone が同一参照");
            Assert.AreEqual(src.GroundTopAltitude, clone.GroundTopAltitude);

            // 元を変更しても clone は影響を受けない
            src.GroundTopAltitude = 999.0;
            Assert.AreEqual(1.5, clone.GroundTopAltitude, "clone が src と参照共有している");
        }

        // --- Save-Load 往復: FsCheck で任意の double を流す ---------------------
        //
        // 不変条件:
        //  S1. 任意の有限 double を InputModel に書き込み、Save→Load 後に同値が読める
        //  S2. 多数の GroundInput をセットしても件数が保たれる
        //  S3. 任意の double[] (有限値のみ) を SettlementGridX 等に書き込んでも
        //      順序と要素がビット完全に保たれる

        [TestMethod]
        public void SaveLoad_FiniteAltitudePair_Roundtrips()
        {
            var opts = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            };
            var svc = new FileOperationService(opts);
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "PileDesignFsCheck_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                Prop.ForAll<double, double>((topA, waterA) =>
                {
                    if (!double.IsFinite(topA) || !double.IsFinite(waterA)) return true;

                    var srcGround = new GroundInput
                    {
                        GroundTopAltitude = topA,
                        GroundWaterTableAltitude = waterA
                    };
                    var inputModel = new InputModel
                    {
                        GroundsInput = new ObservableCollection<GroundInput> { srcGround }
                    };
                    var file = System.IO.Path.Combine(dir, $"trip_{Guid.NewGuid():N}.json");
                    svc.SaveProjectData(file, inputModel, new PileDesign.FEM.AnaModel());
                    var loaded = svc.LoadProjectData(file);

                    var ground = loaded.InputModel?.GroundsInput?.FirstOrDefault();
                    if (ground == null) return false;

                    // 値レベル == 比較 (src の現在値と一致すること)
                    return ground.GroundTopAltitude == srcGround.GroundTopAltitude
                        && ground.GroundWaterTableAltitude == srcGround.GroundWaterTableAltitude;
                }).QuickCheckThrowOnFailure();
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
            }
        }

        // --- GetDistance.BetweenTwoPoint3Ds: ユークリッド距離 -------------------
        //
        // 不変条件:
        //  D1. 非負: d(p, q) >= 0
        //  D2. 同一性: d(p, p) == 0
        //  D3. 対称性: d(p, q) == d(q, p)
        //  D4. 平行移動不変: d(p+v, q+v) == d(p, q)
        //  D5. スケーリング: d(k·p, k·q) == |k|·d(p, q)

        [TestMethod]
        public void GetDistance_NonNegativeAndSymmetric()
        {
            // 原点と (a, b, c) の距離をチェック (3 引数で十分一般性がある: 任意の 2 点は差ベクトルだけで距離が決まる)
            Prop.ForAll<double, double, double>((a, b, c) =>
            {
                if (!IsAllFinite(a, b, c)) return true;
                if (Math.Abs(a) > 1e150 || Math.Abs(b) > 1e150 || Math.Abs(c) > 1e150) return true;

                var p = new Point3D(0, 0, 0);
                var q = new Point3D(a, b, c);
                var d_pq = GetDistance.BetweenTwoPoint3Ds(p, q);
                var d_qp = GetDistance.BetweenTwoPoint3Ds(q, p);

                if (!double.IsFinite(d_pq)) return true;
                return d_pq >= 0 && d_pq == d_qp;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void GetDistance_ZeroForIdenticalPoints()
        {
            Prop.ForAll<double, double, double>((x, y, z) =>
            {
                if (!IsAllFinite(x, y, z)) return true;
                var p = new Point3D(x, y, z);
                return GetDistance.BetweenTwoPoint3Ds(p, p) == 0;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void GetDistance_TranslationInvariant()
        {
            // 固定の 2 点 (0,0,0) と (3,4,12) (距離 = 13) に対して任意の (vx,vy,vz) で平行移動
            Prop.ForAll<double, double, double>((vx, vy, vz) =>
            {
                if (!IsAllFinite(vx, vy, vz)) return true;
                if (Math.Abs(vx) > 1e100 || Math.Abs(vy) > 1e100 || Math.Abs(vz) > 1e100) return true;

                var p = new Point3D(0, 0, 0);
                var q = new Point3D(3, 4, 12); // 距離 13
                var pv = new Point3D(vx, vy, vz);
                var qv = new Point3D(3 + vx, 4 + vy, 12 + vz);

                var d1 = GetDistance.BetweenTwoPoint3Ds(p, q);
                var d2 = GetDistance.BetweenTwoPoint3Ds(pv, qv);
                if (!double.IsFinite(d1) || !double.IsFinite(d2)) return true;

                var tol = 1e-9 * Math.Max(1.0, Math.Max(Math.Abs(d1), Math.Abs(d2)));
                return Math.Abs(d1 - d2) <= tol;
            }).QuickCheckThrowOnFailure();
        }

        // --- Utils.GetShearModulus: G = E / (2·(1+ν)) ----------------------------
        //
        // 不変条件:
        //  G1. 数式: G == E / (2·(1+ν))
        //  G2. ν 範囲 (-1, 0.5) で G は正、E が正なら
        //  G3. ν 固定で E に対し線形 (E倍 → G倍)
        //  G4. E 固定で ν を増やすと G は減少 (1+ν > 0 領域で)

        [TestMethod]
        public void GetShearModulus_FormulaIdentity()
        {
            Prop.ForAll<double, double>((rawE, rawNu) =>
            {
                if (!double.IsFinite(rawE) || !double.IsFinite(rawNu)) return true;
                var E = Math.Abs(rawE) % 1e10 + 1.0;       // (1, 1e10]
                var nu = (Math.Abs(rawNu) % 0.49) - 0.99;  // [-0.99, -0.50)
                nu += 0.5; // [-0.49, 0]  (常に 1+ν > 0)
                if (1 + nu == 0) return true;

                var G = Utils.GetShearModulus(E, nu);
                var expected = E / (2.0 * (1.0 + nu));
                return Math.Abs(G - expected) < 1e-9 * Math.Abs(expected);
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void GetShearModulus_PositiveForPhysicalDomain()
        {
            // E > 0 かつ ν ∈ (-1, 0.5) なら G > 0
            Prop.ForAll<double, double>((rawE, rawNu) =>
            {
                if (!double.IsFinite(rawE) || !double.IsFinite(rawNu)) return true;
                var E = Math.Abs(rawE) % 1e10 + 1e-3;      // (0, 1e10]
                var nu = ((Math.Abs(rawNu) % 1.49) - 0.99); // [-0.99, 0.50)
                if (1 + nu <= 0) return true;

                return Utils.GetShearModulus(E, nu) > 0;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void GetShearModulus_LinearInE()
        {
            // ν 固定で E を 2 倍すると G も 2 倍
            Prop.ForAll<double, double, double>((rawE, rawNu, rawK) =>
            {
                if (!double.IsFinite(rawE) || !double.IsFinite(rawNu) || !double.IsFinite(rawK)) return true;
                var E = Math.Abs(rawE) % 1e6 + 1.0;
                var nu = ((Math.Abs(rawNu) % 0.49) - 0.99) + 0.5; // [-0.49, 0]
                var k = Math.Abs(rawK) % 100 + 0.1; // (0.1, 100.1]

                var G1 = Utils.GetShearModulus(E, nu);
                var G2 = Utils.GetShearModulus(k * E, nu);
                return Math.Abs(G2 - k * G1) < 1e-9 * Math.Abs(G2 + 1);
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void GetShearModulus_DecreasesWithNu()
        {
            // E 固定で ν を増やすと G は減少 (1+ν > 0 領域)
            Prop.ForAll<double, double, double>((rawE, rawNu1, rawNu2) =>
            {
                if (!double.IsFinite(rawE) || !double.IsFinite(rawNu1) || !double.IsFinite(rawNu2)) return true;
                var E = Math.Abs(rawE) % 1e6 + 1.0;
                var nu1 = (Math.Abs(rawNu1) % 0.49) - 0.49; // [-0.49, 0]
                var nu2 = (Math.Abs(rawNu2) % 0.49) - 0.49; // [-0.49, 0]
                if (nu1 == nu2) return true;
                var (lo, hi) = nu1 < nu2 ? (nu1, nu2) : (nu2, nu1);

                return Utils.GetShearModulus(E, lo) > Utils.GetShearModulus(E, hi);
            }).QuickCheckThrowOnFailure();
        }

        private static bool IsAllFinite(params double[] values)
        {
            foreach (var v in values) if (!double.IsFinite(v)) return false;
            return true;
        }

        // --- PileGroupFactor.GetPileGroupFactor ---------------------------------
        // 数式: e = 1.2 / N^(0.65/r), result = min(e^(4/3), 1)
        //
        // 不変条件:
        //  PG1. result <= 1.0 (上限 clip)
        //  PG2. N >= 1, r > 0 で result > 0
        //  PG3. r 固定で N を増やすと result は非増加 (e は N について減少, 4/3 乗で単調)
        //  PG4. 数式恒等式: result == min(pow(1.2/pow(N, 0.65/r), 4/3), 1)

        [TestMethod]
        public void PileGroupFactor_BoundedAbove1()
        {
            Prop.ForAll<int, double>((rawN, rawR) =>
            {
                if (!double.IsFinite(rawR)) return true;
                var N = Math.Abs(rawN) % 1000 + 1;        // [1, 1000]
                var r = (Math.Abs(rawR) % 10) + 0.5;      // [0.5, 10.5]
                return PileGroupFactor.GetPileGroupFactor(N, r) <= 1.0 + 1e-12;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void PileGroupFactor_PositiveForValidDomain()
        {
            Prop.ForAll<int, double>((rawN, rawR) =>
            {
                if (!double.IsFinite(rawR)) return true;
                var N = Math.Abs(rawN) % 1000 + 1;
                var r = (Math.Abs(rawR) % 10) + 0.5;
                return PileGroupFactor.GetPileGroupFactor(N, r) > 0;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void PileGroupFactor_FormulaIdentity()
        {
            Prop.ForAll<int, double>((rawN, rawR) =>
            {
                if (!double.IsFinite(rawR)) return true;
                var N = Math.Abs(rawN) % 1000 + 1;
                var r = (Math.Abs(rawR) % 10) + 0.5;

                var e = 1.2 / Math.Pow(N, 0.65 / r);
                var expected = Math.Min(Math.Pow(e, 4.0 / 3.0), 1.0);
                var actual = PileGroupFactor.GetPileGroupFactor(N, r);

                return Math.Abs(actual - expected) < 1e-12 * Math.Max(1.0, Math.Abs(expected));
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void PileGroupFactor_NonIncreasingWithN()
        {
            // r 固定で N1 <= N2 なら f(N1, r) >= f(N2, r) (N が増えると e が減り、result も非増加)
            Prop.ForAll<int, int, double>((rawN1, rawN2, rawR) =>
            {
                if (!double.IsFinite(rawR)) return true;
                var N1 = Math.Abs(rawN1) % 1000 + 1;
                var N2 = Math.Abs(rawN2) % 1000 + 1;
                var r = (Math.Abs(rawR) % 10) + 0.5;
                if (N1 == N2) return true;
                var (lo, hi) = N1 < N2 ? (N1, N2) : (N2, N1);

                var fLo = PileGroupFactor.GetPileGroupFactor(lo, r);
                var fHi = PileGroupFactor.GetPileGroupFactor(hi, r);
                // 単純な >= 比較。ただし両方が 1 (上限 clip) の場合は等号
                return fLo >= fHi - 1e-12;
            }).QuickCheckThrowOnFailure();
        }

        // --- BoundingBoxCalculator.Calculate ------------------------------------
        // 不変条件:
        //  BB1. 空入力 → (0, 0, 0, 0) ボックス
        //  BB2. 入力点はすべてボックス内 (MinX <= x <= MaxX, MinY <= y <= MaxY)
        //  BB3. 単一点 → MinX == MaxX == x, 同様に Y
        //  BB4. margin >= 0 で Width >= 0
        //  BB5. margin を加えると幅・高さは 2·margin だけ増える

        [TestMethod]
        public void BoundingBox_Empty_ReturnsZeroBox()
        {
            var result = BoundingBoxCalculator.Calculate(new List<PileLayoutDataItem>(), margin: 5.0);
            Assert.AreEqual(0, result.MinX);
            Assert.AreEqual(0, result.MaxX);
            Assert.AreEqual(0, result.MinY);
            Assert.AreEqual(0, result.MaxY);
        }

        [TestMethod]
        public void BoundingBox_AllPointsContained()
        {
            Prop.ForAll<double, double, double>((rawSeed1, rawSeed2, rawSeed3) =>
            {
                if (!IsAllFinite(rawSeed1, rawSeed2, rawSeed3)) return true;
                if (Math.Abs(rawSeed1) > 1e6 || Math.Abs(rawSeed2) > 1e6 || Math.Abs(rawSeed3) > 1e6)
                    return true;

                var pts = new List<PileLayoutDataItem>
                {
                    new() { X = rawSeed1, Y = rawSeed2 },
                    new() { X = rawSeed2, Y = rawSeed3 },
                    new() { X = rawSeed3, Y = rawSeed1 },
                };
                var box = BoundingBoxCalculator.Calculate(pts, margin: 0);

                foreach (var p in pts)
                {
                    if (p.Point3D.X < box.MinX || p.Point3D.X > box.MaxX) return false;
                    if (p.Point3D.Y < box.MinY || p.Point3D.Y > box.MaxY) return false;
                }
                return true;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void BoundingBox_SinglePoint_DegenerateBox()
        {
            Prop.ForAll<double, double>((x, y) =>
            {
                if (!IsAllFinite(x, y)) return true;
                if (Math.Abs(x) > 1e6 || Math.Abs(y) > 1e6) return true;

                var pts = new List<PileLayoutDataItem> { new() { X = x, Y = y } };
                var box = BoundingBoxCalculator.Calculate(pts, margin: 0);

                return box.MinX == x && box.MaxX == x && box.MinY == y && box.MaxY == y
                    && box.Width == 0 && box.Height == 0;
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void BoundingBox_MarginExpandsByTwoMargin()
        {
            Prop.ForAll<double>(rawMargin =>
            {
                if (!double.IsFinite(rawMargin)) return true;
                var margin = Math.Abs(rawMargin) % 100;  // [0, 100)

                var pts = new List<PileLayoutDataItem>
                {
                    new() { X = 1.0, Y = 2.0 },
                    new() { X = 5.0, Y = 7.0 },
                };
                var noMargin = BoundingBoxCalculator.Calculate(pts, margin: 0);
                var withMargin = BoundingBoxCalculator.Calculate(pts, margin: margin);

                return Math.Abs(withMargin.Width - (noMargin.Width + 2 * margin)) < 1e-12
                    && Math.Abs(withMargin.Height - (noMargin.Height + 2 * margin)) < 1e-12;
            }).QuickCheckThrowOnFailure();
        }

        // --- HermiteBeamInterpolation.Hermite (private static) -------------------
        // v(s) = N1·vi + N2·θi·L + N3·vj + N4·θj·L
        //   N1 = 1 - 3s² + 2s³,  N2 = (s - 2s² + s³)·L (係数として (×L))
        //   N3 = 3s² - 2s³,      N4 = (-s² + s³)·L
        //
        // 不変条件:
        //  H1. 端点条件 s=0 → vi (θ・L 項は 0)
        //  H2. 端点条件 s=1 → vj
        //  H3. 対称性: 両端変位/回転を反転して s↔1-s 入替で結果一致 (鏡像)
        //  H4. θi=θj=0 のとき: 単純3次補間 N1·vi + N3·vj、s=0.5 で (vi+vj)/2

        [TestMethod]
        public void Hermite_AtZero_EqualsVi()
        {
            // vi の任意性を主軸に、(thetaI, thetaJ) を 3 引数で生成。vj は固定 0 でテスト。
            // s=0 では結果は θ や vj に無関係に vi であるべき。
            Prop.ForAll<double, double, double>((vi, thetaI, thetaJ) =>
            {
                if (!IsAllFinite(vi, thetaI, thetaJ)) return true;
                if (Math.Abs(vi) > 1e6 || Math.Abs(thetaI) > 1e6 || Math.Abs(thetaJ) > 1e6) return true;

                var L = 5.0;
                var vj = 999.99; // vi と異なる固定値で「vi 以外を返したら失敗」になることを担保
                var result = InvokeHermite(0.0, L, vi, thetaI, vj, thetaJ);
                return Math.Abs(result - vi) < 1e-12 * Math.Max(1.0, Math.Abs(vi));
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Hermite_AtOne_EqualsVj()
        {
            Prop.ForAll<double, double, double>((vj, thetaI, thetaJ) =>
            {
                if (!IsAllFinite(vj, thetaI, thetaJ)) return true;
                if (Math.Abs(vj) > 1e6 || Math.Abs(thetaI) > 1e6 || Math.Abs(thetaJ) > 1e6) return true;

                var L = 5.0;
                var vi = -999.99;
                var result = InvokeHermite(1.0, L, vi, thetaI, vj, thetaJ);
                return Math.Abs(result - vj) < 1e-12 * Math.Max(1.0, Math.Abs(vj));
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Hermite_NoRotation_AtMidpoint_IsAverageOfEnds()
        {
            // θi = θj = 0 のとき s=0.5 で (vi + vj) / 2 (cubic Hermite without rotations is symmetric)
            Prop.ForAll<double, double>((vi, vj) =>
            {
                if (!IsAllFinite(vi, vj)) return true;
                if (Math.Abs(vi) > 1e6 || Math.Abs(vj) > 1e6) return true;

                var L = 1.0;
                var result = InvokeHermite(0.5, L, vi, 0.0, vj, 0.0);
                var expected = (vi + vj) / 2.0;
                return Math.Abs(result - expected) < 1e-12 * Math.Max(1.0, Math.Abs(expected));
            }).QuickCheckThrowOnFailure();
        }

        [TestMethod]
        public void Hermite_LinearInVi_Vj()
        {
            // Hermite は (vi, thetaI, vj, thetaJ) について線形なので、
            // f(k·vi, k·thetaI, k·vj, k·thetaJ) == k · f(vi, thetaI, vj, thetaJ)
            Prop.ForAll<double, double, double>((rawVi, rawVj, rawK) =>
            {
                if (!IsAllFinite(rawVi, rawVj, rawK)) return true;
                if (Math.Abs(rawVi) > 1e6 || Math.Abs(rawVj) > 1e6) return true;
                var k = (Math.Abs(rawK) % 10) + 0.1;  // (0.1, 10.1]

                var L = 3.0;
                var s = 0.3;
                var thetaI = 0.05;
                var thetaJ = -0.02;

                var f = InvokeHermite(s, L, rawVi, thetaI, rawVj, thetaJ);
                var fScaled = InvokeHermite(s, L, k * rawVi, k * thetaI, k * rawVj, k * thetaJ);

                var tol = 1e-9 * Math.Max(1.0, Math.Abs(k * f));
                return Math.Abs(fScaled - k * f) <= tol;
            }).QuickCheckThrowOnFailure();
        }

        private static double InvokeHermite(double s, double L, double vi, double thetaI, double vj, double thetaJ)
        {
            var method = typeof(HermiteBeamInterpolation).GetMethod("Hermite",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("Hermite メソッドが見つかりません");
            return (double)method.Invoke(null, new object[] { s, L, vi, thetaI, vj, thetaJ })!;
        }

        [TestMethod]
        public void SaveLoad_MultipleGrounds_PreservesCount()
        {
            var opts = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
            };
            var svc = new FileOperationService(opts);
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "PileDesignFsCheck_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                Prop.ForAll<byte>(rawCount =>
                {
                    var count = (rawCount % 10) + 1;  // 1〜10 件
                    var inputModel = new InputModel
                    {
                        GroundsInput = new ObservableCollection<GroundInput>(
                            Enumerable.Range(0, count).Select(i => new GroundInput
                            {
                                GroundTopAltitude = i * 0.5
                            }))
                    };
                    var file = System.IO.Path.Combine(dir, $"count_{Guid.NewGuid():N}.json");
                    svc.SaveProjectData(file, inputModel, new PileDesign.FEM.AnaModel());
                    var loaded = svc.LoadProjectData(file);

                    return loaded.InputModel?.GroundsInput?.Count == count;
                }).QuickCheckThrowOnFailure();
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
            }
        }
    }
}
