using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // 杭断面抽象クラス
    internal abstract class AbstractPileSection : IPileSectionCalculation
    {
        public double CurvatureMaxServiceLimit { get; protected set; }
        public double CurvatureMaxDamageLimit { get; protected set; }
        public double CurvatureMaxUltimateLimit { get; protected set; }

        public double AxialForceCurvatureMaxServiceLimit { get; protected set; }
        public double AxialForceCurvatureMaxDamageLimit { get; protected set; }
        public double AxialForceCurvatureMaxUltimateLimit { get; protected set; }

        public List<double> Prestrains { get; protected set; } = [];

        public List<double> ServiceLimitStrainCs { get; protected set; } = [];
        public List<double> DamageLimitStrainCs { get; protected set; } = [];
        public List<double> UltimateLimitStrainCs { get; protected set; } = [];
        public List<double> PositionCs { get; protected set; } = [];

        public List<double> ServiceLimitStrainTs { get; protected set; } = [];
        public List<double> DamageLimitStrainTs { get; protected set; } = [];
        public List<double> UltimateLimitStrainTs { get; protected set; } = [];

        public List<double> PositionTs { get; protected set; } = [];

        public double PileDia { get; protected set; }

        /// <summary>
        /// 圧縮縁位置（コンクリート圧縮縁）。鋼管コンクリート杭では鋼管厚を減じた位置。
        /// </summary>
        protected virtual double CompressionEdgePosition => -PileDia / 2;

        public List<double> ServiceLimitAxialForceThresholds { get; protected set; } = [];
        public List<double> ServiceLimitBendingMomentThresholds { get; protected set; } = [];
        public List<double> ServiceLimitBeta { get; protected set; } = [];

        public List<double> DamageLimitAxialForceThresholds { get; protected set; } = [];
        public List<double> DamageLimitBendingMomentThresholds { get; protected set; } = [];
        public List<double> DamageLimitBeta { get; protected set; } = [];  // レベル2（β1×β2）
        public List<double> DamageLimitBetaL1 { get; protected set; } = [];  // レベル1（β2=1.0、β1のみ）

        public List<double> UltimateLimitAxialForceThresholds { get; protected set; } = [];
        public List<double> UltimateLimitBendingMomentThresholds { get; protected set; } = [];
        public List<double> UltimateLimitBeta { get; protected set; } = [];

        public int DivisionNum { get; protected set; } = 100;
        public double DeltaCurvature { get; protected set; }

        public (List<double>, List<double>, List<double>, List<double>) UnfactoredServiceNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) UnfactoredDamageNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) UnfactoredUltimateNM { get; protected set; }

        public (List<double>, List<double>, List<double>, List<double>) FactoredServiceNM { get; protected set; }
        public (List<double>, List<double>, List<double>, List<double>) FactoredDamageNM { get; protected set; }  // レベル2（β1×β2）
        public (List<double>, List<double>, List<double>, List<double>) FactoredDamageNMLevel1 { get; protected set; }  // レベル1（β1のみ）
        public (List<double>, List<double>, List<double>, List<double>) FactoredUltimateNM { get; protected set; }

        /// <summary>
        /// レベル別の損傷限界 NM インタラクションを返す。
        /// level == 1: β2 を乗じない（β1 のみ）
        /// level == 2: β2 も乗じる（デフォルト、既存 FactoredDamageNM）
        /// </summary>
        public virtual (List<double>, List<double>, List<double>, List<double>) GetFactoredDamageNM(int level)
        {
            return level == 1 ? FactoredDamageNMLevel1 : FactoredDamageNM;
        }

        public (List<double>, List<double>, List<double>, List<double>) SteelYieldNM { get; protected set; }

        public (List<double>, List<double>) UnfactoredServiceNQ { get; protected set; }
        public (List<double>, List<double>) UnfactoredDamageNQ { get; protected set; }
        public (List<double>, List<double>) UnfactoredUltimateNQ { get; protected set; }

        public (List<double>, List<double>) FactoredServiceNQ { get; protected set; }
        public (List<double>, List<double>) FactoredDamageNQ { get; protected set; }
        public (List<double>, List<double>) FactoredUltimateNQ { get; protected set; }

        // コンストラクタ
        internal AbstractPileSection()
        {
        }

        // ===== 断面ひずみ度・応力度プロファイル (N-M 曲線クリック→断面応答表示用) =====

        /// <summary>
        /// 指定した圧縮縁ひずみ εc と曲率 φ に対する断面のひずみ度・応力度分布を返す。
        /// 既定は空。各杭種でオーバーライドして材料層を構築する。
        /// </summary>
        internal virtual SectionStrainStressProfile GetStrainStressProfile(
            double epsilonC, double curvature, bool ultimate, int division = 200)
            => new();

        /// <summary>
        /// N-M グラフに描かれる各曲線を (名称, (N[N],M[Nmm],εc,φ[1/mm]), 安全限界系か) で列挙する。
        /// 既定は低減前/後の使用・損傷・安全限界。ひび割れ開始・主筋降伏開始を持つ杭種はオーバーライドで追加。
        /// </summary>
        internal virtual IEnumerable<(string Name, (List<double> N, List<double> M, List<double> Eps, List<double> Phi) Curve, bool Ultimate)> GetProfileSourceCurves()
        {
            yield return ("(低減前)使用限界", UnfactoredServiceNM, false);
            yield return ("(低減前)損傷限界", UnfactoredDamageNM, false);
            yield return ("(低減前)安全限界", UnfactoredUltimateNM, true);
            yield return ("(低減後)使用限界", FactoredServiceNM, false);
            yield return ("(低減後)損傷限界", FactoredDamageNM, false);
            yield return ("(低減後)損傷限界(L1)", FactoredDamageNMLevel1, false);
            yield return ("(低減後)安全限界", FactoredUltimateNM, true);
        }

        /// <summary>
        /// 弾性換算断面の (コンクリートEc, 換算断面二次モーメントIe, 換算断面積Ae, 外縁半径ROuter) を返す。
        /// NM曲線が εc/φ を保持しない杭種(既製杭の使用・損傷限界=許容応力度式)で、
        /// クリック点 (N,M) から線形の (εc,φ) を復元するのに用いる。既定は (0,0,0,0)。
        /// </summary>
        internal virtual (double Ec, double Ie, double Ae, double ROuter) GetElasticSectionProps()
            => (0.0, 0.0, 0.0, 0.0);

        // 実心/中空円板の材料プロファイル。rInner>0 のとき |z|&lt;rInner を中空(NaN)とする。
        // 表示ひずみは断面の平面保持ひずみ ε(z)=ε0-φz、応力は材料全ひずみ (ε(z)+prestrain) から算定。
        protected static MaterialProfile BuildSolidProfile(
            SectionMaterialKind kind, string name, Material mat, MaterialLaw type,
            double epsilon0, double curvature, double rOuter, double rInner, double prestrain, int division)
        {
            var mp = new MaterialProfile { Kind = kind, Name = name };
            double tol = Math.Max(1e-9, rInner * 1e-6);
            for (int i = 0; i <= division; i++)
            {
                double z = -rOuter + 2.0 * rOuter * i / division;
                double epsGeom = epsilon0 - curvature * z;
                mp.Z.Add(z);
                if (rInner <= 0.0 || Math.Abs(z) >= rInner - tol)
                {
                    mp.Strain.Add(epsGeom);
                    mp.Stress.Add(mat.GetStress(type, epsGeom + prestrain));
                }
                else
                {
                    mp.Strain.Add(double.NaN);
                    mp.Stress.Add(double.NaN);
                }
            }
            return mp;
        }

        // リング材料 (主筋/PC鋼材/鋼管) の材料プロファイル。z=±ringRadius を連続サンプリング。
        protected static MaterialProfile BuildRingProfile(
            SectionMaterialKind kind, string name, Material mat, MaterialLaw type,
            double epsilon0, double curvature, double ringRadius, double prestrain, int division)
        {
            var mp = new MaterialProfile { Kind = kind, Name = name };
            for (int i = 0; i <= division; i++)
            {
                double z = -ringRadius + 2.0 * ringRadius * i / division;
                double epsGeom = epsilon0 - curvature * z;
                mp.Z.Add(z);
                mp.Strain.Add(epsGeom);
                mp.Stress.Add(mat.GetStress(type, epsGeom + prestrain));
            }
            return mp;
        }

        // 損傷限界曲げモーメント閾値を返すメソッド
        internal List<double> GetDamageLimitBendingMomentThresholds()
        {
            List<double> Ms = [];
            foreach (double NTarget in DamageLimitAxialForceThresholds)
            {
                double targetM = GetAllowableMomentForSpecificN(1, NTarget);
                Ms.Add(targetM);
            }
            return Ms;
        }

        // 安全限界曲げモーメント閾値を返すメソッド
        internal virtual List<double> GetUltimateLimitBendingMomentThresholds()
        {
            List<double> Ms = [];
            foreach (double NTarget in UltimateLimitAxialForceThresholds)
            {
                (double targetM, double _) = GetUltimateMomentForSpecificN(NTarget);
                Ms.Add(targetM);
            }
            return Ms;
        }

        // 特定の軸力時の使用、損傷限界曲げモーメントを返すメソッド
        internal double GetAllowableMomentForSpecificN(int limitStateNo, double NTarget)
        {
            try
            {
                double N = 0.0;
                double N1;
                double M = 0.0;
                bool isCompressionSide;
                double curvature = Math.Pow(10, -4);
                double deltaCurvature = curvature / 100;
                List<double> Ns, Ms;
                List<double> curvatures;
                double axialForceCurvatureMax;

                if (limitStateNo == 0)
                {
                    Ns = UnfactoredServiceNM.Item1;
                    Ms = UnfactoredServiceNM.Item2;
                    curvatures = UnfactoredServiceNM.Item4;
                    axialForceCurvatureMax = AxialForceCurvatureMaxServiceLimit;
                }
                else if (limitStateNo == 1)
                {
                    Ns = UnfactoredDamageNM.Item1;
                    Ms = UnfactoredDamageNM.Item2;
                    curvatures = UnfactoredDamageNM.Item4;
                    axialForceCurvatureMax = AxialForceCurvatureMaxDamageLimit;
                }
                else // (limitStateNo == 2)
                {
                    Ns = UnfactoredUltimateNM.Item1;
                    Ms = UnfactoredUltimateNM.Item2;
                    curvatures = UnfactoredUltimateNM.Item4;
                    axialForceCurvatureMax = AxialForceCurvatureMaxUltimateLimit;
                }

                for (int i = 0; i < Ns.Count; i++)
                {
                    if (NTarget < Ns[i])
                    {
                        if (i == 0) { return 0.0; }
                        N = (Ns[i - 1] + Ns[i]) / 2.0;
                        M = (Ms[i - 1] + Ms[i]) / 2.0;
                        curvature = (curvatures[i - 1] + curvatures[i]) / 2.0;
                        deltaCurvature = (curvatures[i] - curvatures[i - 1]) / 100;
                        break;
                    }
                    else if (i == Ns.Count - 1)
                    { return 0.0; }
                }

                if (axialForceCurvatureMax < NTarget) { isCompressionSide = true; } else { isCompressionSide = false; }

                // 反復上限とゼロ除算ガードのみ追加（dN/dφ≈0 で curvature が Inf/NaN になり
                // 無限ループ・ハングするのを防ぐ）。割線ステップ自体は従来どおりで収束解を保持する。
                int maxIter = 50;
                int iter = 0;
                while (Math.Abs(N - NTarget) > SectionSolverTolerances.ULTIMATE_AXIAL_RESIDUAL_N && iter < maxIter) // 0.1N 以上の差がある場合
                {
                    N1 = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature + deltaCurvature).Item1;
                    if (Math.Abs(N1 - N) < 1e-8) break;                 // ゼロ除算防止
                    curvature = deltaCurvature / (N1 - N) * (NTarget - N) + curvature;
                    (double, double, double) forceAndMoment = GetAllowableForceAndMoment(limitStateNo, isCompressionSide, curvature);
                    N = forceAndMoment.Item1;
                    M = forceAndMoment.Item2;
                    iter++;
                }
                return M;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("許容曲げモーメントの算定（→0）", ex, $"limitStateNo={limitStateNo}, NTarget={NTarget:F0}");
                return 0.0;
            }
        }

        // 特定の軸力時の安全限界曲げモーメントを返すメソッド
        internal virtual (double, double) GetUltimateMomentForSpecificN(double NTarget)
        {
            try
            {
                double N = 0.0; double N1;
                double M = 0.0;
                double epsilonC = SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN;
                double curvature = 1.0 * Math.Pow(10, -6);
                double deltaCurvature = curvature / 500.0;

                List<double> Ns = UnfactoredUltimateNM.Item1;
                List<double> Ms = UnfactoredUltimateNM.Item2;
                List<double> curvatures = UnfactoredUltimateNM.Item4;

                // 初期値の設定
                for (int i = 0; i < Ns.Count; i++)
                {
                    if (NTarget < Ns[i])
                    {
                        if (i == 0) { return (0.0, 0.0); }
                        N = (Ns[i - 1] + Ns[i]) * 0.5;
                        M = (Ms[i - 1] + Ms[i]) * 0.5;
                        curvature = (curvatures[i - 1] + curvatures[i]) * 0.5;
                        deltaCurvature = (curvatures[i] - curvatures[i - 1]) / 100;
                        break;
                    }
                    else if (i == Ns.Count - 1)
                    { return (0.0, 0.0); }
                }

                int maxIter = 50;
                int iter = 0;
                while (Math.Abs(N - NTarget) > SectionSolverTolerances.ULTIMATE_AXIAL_RESIDUAL_N && iter < maxIter)
                {
                    N1 = GetUltimateForceAndMoment(epsilonC, curvature + deltaCurvature).Item1;
                    double deltaN = N1 - N;
                    if (Math.Abs(deltaN) < 1e-8)
                        break;

                    double step = deltaCurvature / deltaN * (NTarget - N);
                    if (Math.Abs(step) > Math.Abs(curvature) * 0.5)
                        step = Math.Sign(step) * Math.Abs(curvature) * 0.5;

                    curvature += step;
                    (N, M) = GetUltimateForceAndMoment(epsilonC, curvature);
                    iter++;
                }

                return (M, curvature);
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("安全限界曲げモーメントの算定（→0）", ex, $"NTarget={NTarget:F0}");
                return (0.0, 0.0);
            }
        }

        // ─────────────── ファイバーモデル M-φ（全断面型共通の掃引基盤） ───────────────

        // 解析用 M-φ / ファイバー掃引の端点算定中だけ true にして、材料オプション
        // （e関数・指針トリリニア等）の軟化を持ち込まず常にバイリニアで算定させるガード。
        // 軟化があると M-φ が非単調（負勾配ばね）となり FEM が収束不能になるため。
        // NM 曲線（検定の耐力側）は本フラグを立てないので、オプション時は指針準拠のまま。
        protected bool _forceBilinearUltimate;

        /// <summary>
        /// ファイバー M-φ 掃引の終点 (Mu0, φu)。既定は安全限界ソルバ（圧縮縁 εc=0.003）。
        /// 終局の定義が異なる断面型（PHC/PRC/SC のコンクリート圧壊状態など）はオーバーライドする。
        /// </summary>
        internal virtual (double Mu0, double PhiU) GetFiberSweepEndPoint(double Ntarget)
            => GetUltimateMomentForSpecificN(Ntarget);

        /// <summary>
        /// ファイバー掃引で圧縮縁ひずみ εc を探索する上限。既定はコンクリート安全限界圧縮ひずみ εcu=0.003
        /// （InsituConcrete.GetStress は ε&gt;εcu で σ=0 に脱落し N(εc) が非単調になるため超えない）。
        /// </summary>
        internal virtual double FiberSweepEdgeStrainMax => SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN;

        /// <summary>
        /// ファイバーモデル（断面分割積分）による M-φ 関係。
        /// 指針ポリリニア（GetMPhiRelationship）の代替として、各曲率 φ で軸力つり合い
        /// N(εc, φ) = Ntarget を満たす断面ひずみ状態を解き、M を断面積分で直接求める。
        ///
        /// - 材料構成則は解析用 M-φ と同じバイリニア（材料オプションの軟化は
        ///   _forceBilinearUltimate ガードで持ち込まない）。
        /// - 掃引終点は <see cref="GetFiberSweepEndPoint"/>（既定: 圧縮縁 εc=0.003 の安全限界状態）。
        /// - β1・β2 等の指針低減係数は乗じない「素の」断面応答である点に注意。
        /// - ひび割れ直後にコンクリート引張負担の脱落で M が局所的に微減し得る（生曲線のまま返す。
        ///   FEM ばねとして使う場合は呼び出し側で単調化が必要）。
        ///
        /// 単位: Ntarget [N], φ [1/mm], M [N·mm]（GetMPhiRelationship と同一）。
        /// 軸力が耐力範囲外などで解けない場合は null。
        /// </summary>
        internal (List<double> Phis, List<double> Moments)? GetMPhiRelationshipFiber(double Ntarget, int numPoints = 50)
        {
            bool prevForceBilinear = _forceBilinearUltimate;
            _forceBilinearUltimate = true;
            try
            {
                // 掃引終点 φu（断面型ごとの終局状態。既存ソルバを流用）
                (double mu0, double phiU) = GetFiberSweepEndPoint(Ntarget);
                if (!double.IsFinite(phiU) || phiU <= 1e-12 || !double.IsFinite(mu0) || mu0 <= 0.0)
                    return null;

                var phis = new List<double>(numPoints + 1) { 0.0 };
                var ms = new List<double>(numPoints + 1) { 0.0 };

                // 圧縮縁ひずみのウォームスタート初期値（初回は Newton 失敗時に二分法が拾う）
                double epsC = 0.0;

                for (int i = 1; i <= numPoints; i++)
                {
                    // ひび割れ・降伏の折れ点が低 φ 側に集まるため 1.5 乗スペーシングで低 φ 側を密にする
                    double t = (double)i / numPoints;
                    double phi = phiU * Math.Pow(t, 1.5);

                    // 前点の εc をウォームスタートに軸力つり合いを解く（解けない φ はスキップ）
                    if (!SolveFiberAxialEquilibrium(Ntarget, phi, ref epsC))
                        continue;

                    (_, double m) = GetUltimateForceAndMoment(epsC, phi);
                    if (!double.IsFinite(m)) continue;
                    phis.Add(phi);
                    ms.Add(m);
                }

                // 実質的に曲線を成さない場合は失敗扱い
                return phis.Count >= 5 ? (phis, ms) : null;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("ファイバー M-φ の算定（→ポリリニアで代替）", ex, $"Ntarget={Ntarget:F0}");
                return null;
            }
            finally { _forceBilinearUltimate = prevForceBilinear; }
        }

        /// <summary>
        /// 与曲率 φ の下で軸力つり合い N(εc, φ) = Ntarget を満たす圧縮縁ひずみ εc を解く。
        /// Newton 法（数値微分、ウォームスタート）＋失敗時は二分法フォールバック。
        /// 探索範囲は εc ∈ [-0.05, FiberSweepEdgeStrainMax]（深い引張状態〜圧縮終局ひずみ）。
        /// バイリニアコンクリートのひび割れ脱落（引張弾性→σ=0 の不連続）で N(εc) に
        /// 微小な非単調が生じ得るため、Newton が停滞したら二分法に切り替える。
        /// </summary>
        private bool SolveFiberAxialEquilibrium(double Ntarget, double phi, ref double epsC)
        {
            const double epsLo = -0.05;
            double epsHi = FiberSweepEdgeStrainMax;
            // [N]（M への影響は無視できる規模）
            double tolN = Math.Max(SectionSolverTolerances.FIBER_AXIAL_ABS_N,
                                   Math.Abs(Ntarget) * SectionSolverTolerances.FIBER_AXIAL_REL);

            double x = Math.Clamp(epsC, epsLo, epsHi);

            for (int iter = 0; iter < 40; iter++)
            {
                double f = GetUltimateForceAndMoment(x, phi).Item1 - Ntarget;
                if (Math.Abs(f) < tolN) { epsC = x; return true; }

                const double dEps = 1e-7;
                double f2 = GetUltimateForceAndMoment(x + dEps, phi).Item1 - Ntarget;
                double dfdx = (f2 - f) / dEps;
                // dN/dεc は概ね EA (~1e10 N) オーダー。実質ゼロ・負勾配（ひび割れ不連続）なら二分法へ
                if (dfdx < 1e-3) break;

                double step = f / dfdx;
                double maxStep = 0.5 * (epsHi - epsLo);
                if (Math.Abs(step) > maxStep) step = Math.Sign(step) * maxStep;

                double xNext = Math.Clamp(x - step, epsLo, epsHi);
                if (Math.Abs(xNext - x) < 1e-12) break; // クランプ端で停滞 → 二分法へ
                x = xNext;
            }

            // 二分法フォールバック（N(εc) は大局的に単調増加）
            double fLo = GetUltimateForceAndMoment(epsLo, phi).Item1 - Ntarget;
            double fHi = GetUltimateForceAndMoment(epsHi, phi).Item1 - Ntarget;
            // 境界そのものが解（掃引終点 φu では εc=上限ちょうどが解になり得る）の場合を先に許容
            if (Math.Abs(fHi) < tolN) { epsC = epsHi; return true; }
            if (Math.Abs(fLo) < tolN) { epsC = epsLo; return true; }
            if (fLo > 0.0 || fHi < 0.0) return false; // ブラケット不能（軸力がこの φ で釣り合わない）

            double lo = epsLo, hi = epsHi;
            for (int iter = 0; iter < 80; iter++)
            {
                double mid = 0.5 * (lo + hi);
                double fMid = GetUltimateForceAndMoment(mid, phi).Item1 - Ntarget;
                if (Math.Abs(fMid) < tolN) { epsC = mid; return true; }
                if (fMid < 0.0) lo = mid; else hi = mid;
            }
            // 80 回二分後は区間幅が機械精度以下。ひび割れ不連続をまたぐ場合のみ残差が tolN を超え得るが、
            // その残差は短冊 1 枚分のひび割れ荷重程度で M への影響は無視できるため収束扱いとする。
            epsC = 0.5 * (lo + hi);
            return true;
        }

        // 限界ひずみ状態を超えない最大曲率取得メソッド
        internal static double GetAllowableMaxCurvature(
            List<double> allowableStrainCs, List<double> positionCs, List<double> allowableStrainTs, List<double> positionTs)
        {
            try
            {
                double maxCurvature = double.MaxValue;
                for (int i = 0; i < allowableStrainCs.Count; i++)
                {
                    for (int j = 0; j < allowableStrainTs.Count; j++)
                    {
                        double denominator = positionTs[j] - positionCs[i];
                        if (Math.Abs(denominator) < 1e-12) continue; // 0除算防止
                        double curvature = -(allowableStrainTs[j] - allowableStrainCs[i]) / denominator;
                        if (curvature < maxCurvature) { maxCurvature = curvature; }
                    }
                }
                return maxCurvature;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("最大許容曲率の算定（→0）", ex);
                return 0.0;
            }
        }

        /// <summary>
        /// 純引張時のひずみ度（全材料が引張耐力に達するひずみの最大値）
        /// 派生クラスでオーバーライド可能
        /// </summary>
        internal virtual double GetPureTensionStrain() => -0.006;

        // <抽象> 軸力、曲げモーメント取得メソッド
        internal abstract (double, double, double) GetAllowableForceAndMoment(
            int limitStateNo, bool isCompressionSide, double curvature);

        // <抽象> 安全限界軸力、曲げモーメント取得メソッド
        internal abstract (double, double) GetUltimateForceAndMoment(double epsilonC, double curvature);

        // ある曲率時の圧縮縁ひずみ度取得メソッド
        internal double GetAllowableCompressionEdgeStrain(
           int limitStateNo, bool isCompressionSide, double curvature)
        {
            try
            {
                var allowablesStrains = GetAllowableStrains(limitStateNo);
                List<double> allowableStrainCs = allowablesStrains.Item1;
                List<double> allowableStrainTs = allowablesStrains.Item2;

                double epsilonC;
                if (isCompressionSide)
                {
                    epsilonC = double.MaxValue;
                    foreach (var pair in allowableStrainCs.Zip(PositionCs, (allowableStrainC, positionC) => (allowableStrainC, positionC)))
                        epsilonC = Math.Min(epsilonC, -curvature * (CompressionEdgePosition - pair.positionC) + pair.allowableStrainC);
                }
                else // (isCompressionSide == false)
                {
                    epsilonC = double.MinValue;
                    foreach (var pair in allowableStrainTs.Zip(PositionTs, (allowableStrainT, positionT) => (allowableStrainT, positionT)))
                        epsilonC = Math.Max(epsilonC, -curvature * (CompressionEdgePosition - pair.positionT) + pair.allowableStrainT);
                }
                return epsilonC;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("圧縮縁ひずみの算定（→0）", ex);
                return 0.0;
            }
        }

        // 使用損傷限界ひずみ度取得メソッド
        internal (List<double>, List<double>) GetAllowableStrains(int limitStateNo)
        {
            if (limitStateNo == 0)
            {
                return (ServiceLimitStrainCs, ServiceLimitStrainTs);
            }
            else if (limitStateNo == 1)
            {
                return (DamageLimitStrainCs, DamageLimitStrainTs);
            }
            else //if (limitStateNo == 2)
            {
                return (UltimateLimitStrainCs, UltimateLimitStrainTs);
            }
        }

        // 使用限界MNインタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetServiceLimitMNInteraction()
        {
            return GetAllowableMNInteraction(CurvatureMaxServiceLimit, 0);
        }

        // 損傷限界MNインタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetDamageLimitMNInteraction()
        {
            return GetAllowableMNInteraction(CurvatureMaxDamageLimit, 1);
        }

        // 使用損傷限界MNインタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetAllowableMNInteraction(double maxCurvature, int LimitStateNo)
        {
            List<double> axialForces = [];
            List<double> bendingMoments = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];

            for (int i = 0; i <= DivisionNum; i++)
            {
                double curvature = maxCurvature * i / DivisionNum;
                var result = GetAllowableForceAndMoment(LimitStateNo, false, curvature); // 引張側 純引張～
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(result.Item3);
                curvatures.Add(curvature);
            }

            for (int i = DivisionNum; i >= 0; i--)
            {
                double curvature = maxCurvature * i / DivisionNum;
                var result = GetAllowableForceAndMoment(LimitStateNo, true, curvature); // 圧縮側 ～純圧縮
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(result.Item3);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        // 安全限界MN インタラクション取得メソッド
        internal virtual (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInteraction()
        {
            List<double> axialForces = [];
            List<double> bendingMoments = [];
            List<double> epsilonCs = [];
            List<double> curvatures = [];
            double epsilonC;
            double curvature;
            double maxCurvatureDefault = (0.003 + 0.0025) * 20.0 / PileDia;
            // CurvatureMaxUltimateLimitが設定されていて、デフォルトより小さい場合はそちらを使用
            // ただし極端に小さい値（デフォルトの1/10未満）は無視して退化した曲線を防ぐ
            double minAllowed = maxCurvatureDefault * 0.1;
            double maxCurvature = CurvatureMaxUltimateLimit > minAllowed && CurvatureMaxUltimateLimit < maxCurvatureDefault
                ? CurvatureMaxUltimateLimit
                : maxCurvatureDefault;
            double maxEpsilonC = 0.003;

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                if (i == 0)
                {
                    epsilonC = GetPureTensionStrain();
                    curvature = 0.0;
                }
                else if (i != DivisionNum * 2)
                {
                    epsilonC = maxEpsilonC;
                    curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
                }
                else { epsilonC = maxEpsilonC; curvature = 0.0; }

                var result = GetUltimateForceAndMoment(epsilonC, curvature);
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }

            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        /// <summary>
        /// NM曲線上に指定軸力Nの補間点を挿入する
        /// </summary>
        private static void InsertInterpolatedPoint(
            List<double> ns, List<double> ms, List<double> ecs, List<double> ks, double nTarget)
        {
            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                if ((n0 - nTarget) * (n1 - nTarget) <= 0 && Math.Abs(n1 - n0) > 1e-10)
                {
                    double t = (nTarget - n0) / (n1 - n0);
                    double mInterp = ms[i] + t * (ms[i + 1] - ms[i]);
                    double ecInterp = ecs[i] + t * (ecs[i + 1] - ecs[i]);
                    double kInterp = ks[i] + t * (ks[i + 1] - ks[i]);

                    ns.Insert(i + 1, nTarget);
                    ms.Insert(i + 1, mInterp);
                    ecs.Insert(i + 1, ecInterp);
                    ks.Insert(i + 1, kInterp);
                    return; // 最初にヒットした区間のみ
                }
            }
        }

        // 軸力制限の組み込みメソッド
        internal static (List<double>, List<double>, List<double>, List<double>)
            GetFactoredMNInteraction(
            (List<double>, List<double>, List<double>, List<double>) unfactoredNM,
            (List<double>, List<double>) additionalNM, List<double> factor)
        {
            List<double> factoredNs = [];
            List<double> factoredMs = [];

            List<double> unfactoredNs = unfactoredNM.Item1;
            List<double> unfactoredMs = unfactoredNM.Item2;
            List<double> epsilonCs = unfactoredNM.Item3;
            List<double> curvatures = unfactoredNM.Item4;

            List<double> additionalNs = additionalNM.Item1;
            List<double> additionalMs = additionalNM.Item2;

            int j = 0;
            for (int i = 0; i < unfactoredNs.Count; i++)
            {
                if (j < additionalNs.Count)
                {
                    while (additionalNs[j] < unfactoredNs[i])
                    {
                        factoredNs.Add(additionalNs[j]);
                        factoredMs.Add(additionalMs[j] * factor[j]);
                        factoredNs.Add(additionalNs[j]);
                        factoredMs.Add(additionalMs[j] * factor[j + 1]);
                        j += 1;

                        if (j >= additionalNs.Count) { break; }
                    }
                }
                factoredNs.Add(unfactoredNs[i]);
                factoredMs.Add(unfactoredMs[i] * factor[j]);
            }
            return (factoredNs, factoredMs, epsilonCs, curvatures);
        }

        /// <summary>
        /// M-φ 関係を取得します（IPileSectionCalculation インターフェース実装）
        /// 派生クラスでオーバーライドして具体的な実装を提供します。
        /// </summary>
        /// <param name="axialN">軸力 [N]</param>
        /// <returns>(曲率リスト [1/mm], モーメントリスト [N·mm])</returns>
        public virtual (List<double> Phis, List<double> Moments) GetMPhiRelationship(double axialN)
        {
            // デフォルト実装: 空リストを返す
            // 派生クラス（InsituReinforcedConcreteSection 等）で適切にオーバーライドされる
            return (new List<double>(), new List<double>());
        }
    }
}
