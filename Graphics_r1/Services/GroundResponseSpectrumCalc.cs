using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;

namespace PileDesign.Services
{
    /// <summary>
    /// 多層地盤の応答スペクトル法 (MDOF + 等価線形化反復) による地盤水平変位計算。
    /// 出典: Inoue/Hayashi 他「表層地盤による地震動増幅率評価法に関する研究」
    /// 日本建築学会技術報告集 Vol.32 No.107。
    ///
    /// 単位系: GroundMassDataInput.Density は重量密度 [kN/m³]。g=9.80665 で割って質量密度 [t/m³] に
    /// 変換する慣習に従う。これに伴い G [kPa]、VS [m/s]、H [m] で内部演算する。
    /// </summary>
    internal static class GroundResponseSpectrumCalc
    {
        private const int MaxIter = 20;
        private const double Tol = 1e-3;
        private const double GReductionFloor = 0.05;       // G/G0 下限
        private const double EffectiveStrainCoeff = 0.65;  // γeff = 0.65 γmax (SHAKE 慣行)
        private const double XiInitial = 0.05;             // 材料減衰
        private const double Gravity = 9.80665;            // m/s²

        // Gamma05/HMax が未設定 (= 0) の場合のフォールバック (ShallowSoilType ベース)
        internal const double Gamma05DefaultSand = 7e-4;
        internal const double Gamma05DefaultClay = 2e-3;
        internal const double HMaxDefault = 0.20;

        internal readonly record struct LevelResult(
            double T1, double T2,  // 1次・2次卓越周期 (固有値解析の最小・第2小)
            double XiE, double Gamma,
            double Impedance,  // αE = ρ_E·Vs_E / (ρ_N·Vs_N) — 表層/基盤インピーダンス比
            double Gs1,        // 1次モード(T1)での地盤増幅率 1 / (αE + 1.57·ξe)
            double Gs2,        // 2次モード(T2)での地盤増幅率 1 / (αE + 4.71·ξe)
            double[] G,        // 収束後 G_i [kPa] (アクティブ質点ぶんのみ)
            double[] PhiU0_1,  // U[0]=1 正規化済モード形 (アクティブ質点ぶんのみ)
            double[] DispMm    // u_i [mm] (アクティブ質点ぶんのみ)
        );

        /// <summary>
        /// 1 レベル分の応答スペクトル法計算。
        /// </summary>
        /// <param name="masses">表層から順 (i=0 地表)。bedrock 判定は IsEngineeringBedrock。</param>
        /// <param name="bedrockDensity">工学的基盤の重量密度 [kN/m³]</param>
        /// <param name="bedrockVs">工学的基盤 VS [m/s]</param>
        /// <param name="shallowSoilType">"砂質土" or "粘性土" → 層 Gamma05 が 0 のときのフォールバック値選択</param>
        /// <param name="L">レベル係数 (Level1=0.2, Level2=1.0)</param>
        /// <param name="Z">地域係数 (既定 1.0)</param>
        internal static LevelResult Compute(
            IReadOnlyList<GroundMassDataInput> masses,
            double bedrockDensity, double bedrockVs,
            string shallowSoilType, double L, double Z = 1.0)
        {
            try
            {
                int firstBedrockIdx = masses.Count;
                for (int i = 0; i < masses.Count; i++)
                {
                    if (masses[i].IsEngineeringBedrock) { firstBedrockIdx = i; break; }
                }
                int n = firstBedrockIdx; // active mass points: [0, n-1]
                if (n <= 0)
                    return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);

                // ShallowSoilType ベースの γ0.5 デフォルト (per-layer 値が 0 のときのフォールバック)
                double gamma05Fallback = (shallowSoilType == "粘性土") ? Gamma05DefaultClay : Gamma05DefaultSand;

                // 入力配列を作成（H, mass density, G0, per-layer Gamma05/HMax）
                double[] H = new double[n];
                double[] rhoMass = new double[n]; // [t/m³]
                double[] G0 = new double[n];      // [kPa]
                double[] m = new double[n];       // mass [t] (Mass プロパティ)
                double[] gamma05 = new double[n]; // per-layer 基準せん断ひずみ
                double[] hMax = new double[n];    // per-layer h_max
                for (int i = 0; i < n; i++)
                {
                    var md = masses[i];
                    double h = md.H.GetValueOrDefault();
                    if (h <= 0)
                        return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);
                    H[i] = h;
                    rhoMass[i] = md.Density / Gravity;
                    G0[i] = rhoMass[i] * md.VS0 * md.VS0;
                    m[i] = md.Mass;
                    if (m[i] <= 0 || G0[i] <= 0)
                        return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);
                    // 0 以下なら ShallowSoilType デフォルト (旧データの互換性)
                    gamma05[i] = (md.Gamma05 > 0) ? md.Gamma05 : gamma05Fallback;
                    hMax[i] = (md.HMax > 0) ? md.HMax : HMaxDefault;
                }

                // 反復ループ初期値
                double[] G = (double[])G0.Clone();
                double xiE = XiInitial;

                double[] phi = new double[n];      // 生固有ベクトル (sign-normalized)
                double[] phiU0 = new double[n];    // U[0]=1 正規化
                double[] u = new double[n];        // 変位 [m]
                double T1 = double.NaN;
                double T2Period = double.NaN;
                double Gamma = 0;

                for (int iter = 0; iter < MaxIter; iter++)
                {
                    // 質量行列 M^(-1/2) (diagonal)
                    // 一般化固有値問題 K Φ = ω² M Φ を、A = M^(-1/2) K M^(-1/2) の標準固有値問題に変換。
                    // Ψ = M^(1/2) Φ → A Ψ = ω² Ψ
                    double[] mSqrtInv = new double[n];
                    for (int i = 0; i < n; i++) mSqrtInv[i] = 1.0 / Math.Sqrt(m[i]);

                    // 三重対角剛性 K (per unit area)
                    // k_i = G_i / H_i, i ∈ [0, n-1]。i=n-1 はベッドロックへの剛性 (固定端)。
                    double[] k = new double[n];
                    for (int i = 0; i < n; i++) k[i] = G[i] / H[i];

                    var A = Matrix<double>.Build.Dense(n, n);
                    for (int i = 0; i < n; i++)
                    {
                        // K[i,i]
                        double kii;
                        if (i == 0) kii = k[0];                          // 地表: k_0 のみ
                        else if (i == n - 1) kii = k[n - 2] + k[n - 1];  // 基盤側: 上層 + 基盤剛性
                        else kii = k[i - 1] + k[i];
                        A[i, i] = kii * mSqrtInv[i] * mSqrtInv[i];

                        // K[i,i+1] = K[i+1,i] = -k_i
                        if (i < n - 1)
                        {
                            double off = -k[i] * mSqrtInv[i] * mSqrtInv[i + 1];
                            A[i, i + 1] = off;
                            A[i + 1, i] = off;
                        }
                    }

                    var evd = A.Evd(Symmetricity.Symmetric);
                    var eigVals = evd.EigenValues.Real();

                    // 最小・第2小の正固有値を探す (T1=1次, T2=2次卓越周期)
                    int minIdx = -1, secondIdx = -1;
                    double minOmegaSq = double.PositiveInfinity;
                    double secondOmegaSq = double.PositiveInfinity;
                    for (int i = 0; i < n; i++)
                    {
                        double ev = eigVals[i];
                        if (ev <= 1e-12) continue;
                        if (ev < minOmegaSq)
                        {
                            secondOmegaSq = minOmegaSq;
                            secondIdx = minIdx;
                            minOmegaSq = ev;
                            minIdx = i;
                        }
                        else if (ev < secondOmegaSq)
                        {
                            secondOmegaSq = ev;
                            secondIdx = i;
                        }
                    }
                    if (minIdx < 0 || double.IsNaN(minOmegaSq) || double.IsInfinity(minOmegaSq))
                        return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);

                    double omega1 = Math.Sqrt(minOmegaSq);
                    T1 = 2.0 * Math.PI / omega1;
                    // 2次モードが取れなければ T1/3 で代用 (一様地盤の解析解)
                    T2Period = (secondIdx >= 0 && !double.IsInfinity(secondOmegaSq))
                        ? 2.0 * Math.PI / Math.Sqrt(secondOmegaSq)
                        : T1 / 3.0;

                    // EigenVectors の minIdx 列を取り出して Φ = M^(-1/2) Ψ
                    var psi = evd.EigenVectors.Column(minIdx);
                    for (int i = 0; i < n; i++) phi[i] = psi[i] * mSqrtInv[i];

                    // 符号正規化 (地表側を正)
                    if (phi[0] < 0)
                        for (int i = 0; i < n; i++) phi[i] = -phi[i];

                    // U[0]=1 正規化
                    double phi0 = phi[0];
                    if (Math.Abs(phi0) < 1e-300)
                        return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);
                    for (int i = 0; i < n; i++) phiU0[i] = phi[i] / phi0;

                    // 参加係数 Γ = (φ^T M 1) / (φ^T M φ)
                    double num = 0, den = 0;
                    for (int i = 0; i < n; i++) { num += phi[i] * m[i]; den += phi[i] * phi[i] * m[i]; }
                    if (den < 1e-300)
                        return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);
                    Gamma = num / den;

                    // 変位 u_i = |Γ φ_i| · (T₁/2π)² · Sa0(T₁) · Fh(ξe) · L · Z
                    double sa = Sa0(T1);
                    double fh = Fh(xiE);
                    double sd = Math.Pow(T1 / (2.0 * Math.PI), 2.0) * sa * fh * L * Z;
                    for (int i = 0; i < n; i++) u[i] = Math.Abs(Gamma * phi[i]) * sd;

                    // 層せん断ひずみ γ_i (i 質点と i+1 質点間の層) と下端 (基盤位置で u=0)
                    double[] gamma = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double uNext = (i + 1 < n) ? u[i + 1] : 0.0;
                        gamma[i] = EffectiveStrainCoeff * Math.Abs(u[i] - uNext) / H[i];
                    }

                    // G 更新 + 層減衰 (per-layer γ0.5 / h_max)
                    double[] Gnew = new double[n];
                    double[] xi = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double r = gamma[i] / gamma05[i];
                        Gnew[i] = Math.Max(G0[i] / (1.0 + r), GReductionFloor * G0[i]);
                        xi[i] = hMax[i] * r / (1.0 + r);
                    }

                    // モード減衰 (ひずみエネルギー重み): ξe = ξ0 + Σ(ξ_i - ξ0) w_i / Σ w_i
                    // w_i = G_i (Δφ_i)² / H_i
                    double xiDen = 0, xiWeighted = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double phiNext = (i + 1 < n) ? phi[i + 1] : 0.0;
                        double dphi = phi[i] - phiNext;
                        double w = G[i] * dphi * dphi / H[i];
                        xiDen += w;
                        xiWeighted += (xi[i] - XiInitial) * w;
                    }
                    double xiEnew = (xiDen > 0) ? XiInitial + xiWeighted / xiDen : XiInitial;
                    if (xiEnew < XiInitial) xiEnew = XiInitial;

                    // 収束判定
                    double maxRelChange = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double rel = Math.Abs(Gnew[i] - G[i]) / G[i];
                        if (rel > maxRelChange) maxRelChange = rel;
                    }

                    G = Gnew;
                    xiE = xiEnew;

                    if (maxRelChange < Tol) break;
                }

                if (double.IsNaN(T1))
                    return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);

                double[] dispMm = new double[n];
                for (int i = 0; i < n; i++) dispMm[i] = u[i] * 1000.0;

                // 表層代表インピーダンス比 αE = (ρ_E · Vs_E) / (ρ_N · Vs_N)
                //   ρ_E = Σ(ρ_i·h_i) / ΣH         (深さ重み平均密度)
                //   Vs_E = ΣH / Σ(h_i/Vs_i)         (走時平均 — T0 = 4ΣH/Vs_E が正しく出る)
                // 収束後の等価 Vs (= √(G_i/ρ_mass_i)) を用いる
                double sumRhoH = 0, sumH = 0, sumHoverVs = 0;
                for (int i = 0; i < n; i++)
                {
                    double vse = Math.Sqrt(G[i] / rhoMass[i]);
                    sumRhoH += rhoMass[i] * H[i];
                    sumH += H[i];
                    if (vse > 1e-12) sumHoverVs += H[i] / vse;
                }
                double bedrockRhoMass = bedrockDensity / Gravity;
                double rhoE = (sumH > 0) ? sumRhoH / sumH : 0.0;
                double VsE = (sumHoverVs > 1e-12) ? sumH / sumHoverVs : 0.0;
                double bedrockImpedance = bedrockRhoMass * bedrockVs;
                double alphaE = (bedrockImpedance > 1e-12) ? rhoE * VsE / bedrockImpedance : 0.0;

                // 地盤増幅率 (論文式 (9)):
                //   Gs1 = 1 / (αE + 1.57·ξe) — 1 次モード (T1 近傍) の増幅率
                //   Gs2 = 1 / (αE + 4.71·ξe) — 2 次モード (T2 近傍) の増幅率
                double Gs1 = (alphaE + 1.57 * xiE > 0) ? 1.0 / (alphaE + 1.57 * xiE) : 0.0;
                double Gs2 = (alphaE + 4.71 * xiE > 0) ? 1.0 / (alphaE + 4.71 * xiE) : 0.0;

                return new LevelResult(T1, T2Period, xiE, Gamma, alphaE, Gs1, Gs2, G, phiU0, dispMm);
            }
            catch (Exception)
            {
                return new LevelResult(double.NaN, 0, 0, 0, 0, 0, 0, [], [], []);
            }
        }

        /// <summary>
        /// 周期依存の地盤増幅率 Gs(T) (論文式 (8))。T1=1次卓越周期, T2=2次卓越周期。
        /// 4 区分:
        ///   0 &lt; T ≤ 0.8·T2:        Gs2·T/(0.8·T2) — 短周期側で線形上昇
        ///   0.8·T2 ≤ T ≤ 0.8·T1:    Gs2 から Gs1 へ線形遷移
        ///   0.8·T1 ≤ T ≤ 1.2·T1:    Gs1 (ピーク域)
        ///   T > 1.2·T1:             長周期側で 1 へ漸近的に減衰 (式 (8) 第 4 区分)
        /// </summary>
        internal static double Gs(double T, double T1, double T2, double Gs1, double Gs2)
        {
            if (T1 <= 0 || T2 <= 0 || T <= 0) return 1.0;
            double tA = 0.8 * T2;
            double tB = 0.8 * T1;
            double tC = 1.2 * T1;
            // T2 ≥ T1 のような不自然なケースは clamp
            if (tA >= tB) tA = tB * 0.5;

            if (T <= tA) return Gs2 * T / tA;
            if (T <= tB) return Gs2 + (Gs1 - Gs2) * (T - tA) / (tB - tA);
            if (T <= tC) return Gs1;
            // 第 4 区分: 1.2·T1 で Gs1, T→∞ で 1 (clamp で発散回避)
            double denomFactor = 1.0 / tC - 0.1;
            if (denomFactor <= 0)
            {
                // 数値特異点回避: シンプルな 1/T 漸近
                return 1.0 + (Gs1 - 1.0) * tC / T;
            }
            return (Gs1 - 1.0) / (denomFactor * T) + Gs1 - (Gs1 - 1.0) / (denomFactor * tC);
        }

        /// <summary>
        /// 収束後の地盤増幅率を加味した「地表」加速度応答スペクトル Sa_surface(T) [m/s²]。
        /// Sa_surface = Gs(T) · L · Z · Sa0(T)
        /// </summary>
        internal static double SaSurface(double T, double T1, double T2, double Gs1, double Gs2, double L, double Z = 1.0)
        {
            return Gs(T, T1, T2, Gs1, Gs2) * L * Z * Sa0(T);
        }

        /// <summary>
        /// 「基盤」加速度応答スペクトル Sa_bedrock(T) = L · Z · Sa0(T) [m/s²]。
        /// </summary>
        internal static double SaBedrock(double T, double L, double Z = 1.0)
        {
            return L * Z * Sa0(T);
        }

        // 告示 1457 / 論文式 (2): 加速度応答スペクトル h=0.05 (m/s²)
        // T=0.16 で 3.2+30·0.16 = 8.0 = plateau, T=0.64 で 5.12/0.64 = 8.0 で連続
        internal static double Sa0(double T)
        {
            if (T <= 0.16) return 3.2 + 30.0 * T;
            if (T <= 0.64) return 8.0;
            return 5.12 / T;
        }

        // 論文式 (4): 減衰補正係数
        internal static double Fh(double xi) => 1.5 / (1.0 + 10.0 * xi);
    }
}
