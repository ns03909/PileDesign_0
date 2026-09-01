using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 材料構成則（応力ひずみ関係）の種別。
    /// 従来は "linear" / "bilinear" 等の文字列セレクタで、typo が実行時まで検出できなかったため
    /// enum 化した（2026-08）。各材料の <see cref="Material.GetStress"/> が解釈する。
    /// </summary>
    internal enum MaterialLaw
    {
        /// <summary>線形弾性（使用・損傷限界＝許容応力度系の断面解析）</summary>
        Linear,

        /// <summary>バイリニア（弾完全塑性。安全限界・解析用 M-φ の既定）</summary>
        Bilinear,

        /// <summary>e関数法（RC基礎構造部材の耐震設計指針(案) 5.4.1。コンクリートの安全限界 NM 曲線用）</summary>
        EFunction,

        /// <summary>指針(案)準拠の鋼管トリリニア（圧縮側 0.85×引張強さ・引張側 引張強さで頭打ち）</summary>
        GuidelineUltimate,
    }

    // 材料の基底クラス
    internal abstract class Material
    {
        public double Prestrain { get; internal set; }

        public double ServiceLimitStrainC { get; protected set; }
        public double DamageLimitStrainC { get; protected set; }
        public double UltimateLimitStrainC { get; protected set; }

        public double ServiceLimitStrainT { get; protected set; }
        public double DamageLimitStrainT { get; protected set; }
        public double UltimateLimitStrainT { get; protected set; }

        internal abstract double GetStress(MaterialLaw type, double epsilon);
    }

    // 現地打ちコンクリートクラス
    internal class InsituConcrete : Material
    {
        public double DO { get; }
        public string Type { get; } = "普通"; // コンクリート種別（初期化失敗時も非 null を保証）
        public double Gsi { get; }
        public double BearingFactor { get; private set; } = 1.0;
        public double Fc { get; }
        public double Ec { get; private set; }
        public double Ac { get; private set; }
        public double Epsilon { get; private set; }
        public double EpsilonM { get; }
        public double EpsilonCu { get; }
        public double EpsilonCr_bilinear { get; private set; }
        public double EpsilonCr_eFunction { get; private set; }
        public double SigmaCr { get; }

        // コンストラクタ
        public InsituConcrete(double _DO, double gsi, double _Fc, string type = "普通", double epsilonM = 0.002, double epsilonCu = 0.003)
        {
            try
            {
                DO = _DO;
                Gsi = gsi;
                Fc = _Fc;
                EpsilonM = epsilonM;
                EpsilonCu = epsilonCu;
                Type = type;
                Ec = GetEc();
                Ac = Math.PI * Math.Pow(DO, 2) / 4.0;
                SigmaCr = 0.56 * Math.Sqrt(Gsi * Fc);
                SetEpsilonCr();
                SetAllowableStrain();
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("コンクリート諸元の初期化（Ec等→0）", ex, $"DO={DO}, Gsi={Gsi}, Fc={Fc}");
                Ec = 0.0;
                Ac = 0.0;
                SigmaCr = 0.0;
            }
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            try
            {
                double serviceLimitStressC; // 使用限界圧縮応力度（長期許容相当）
                double damageLimitStressC;  // 損傷限界圧縮応力度（短期許容相当）
                if (ConcreteModelOptions.UseNotification1113Compression)
                {
                    // 告示 平13国交告第1113号(第8): 長期許容圧縮 = [1] Fc/4 または [2] min(Fc/4.5, 6.0)、短期 = 2×長期。
                    double longTerm = ConcreteModelOptions.Notification1113CompressionCase == 2
                        ? Math.Min(Fc / 4.5, 6.0)
                        : Fc / 4.0;
                    serviceLimitStressC = longTerm;
                    damageLimitStressC = 2.0 * longTerm;
                }
                else
                {
                    // 基礎部材の強度と変形性能: 使用限界 (1/3)ξFc、損傷限界 (2/3)ξFc。
                    serviceLimitStressC = 1.0 / 3.0 * Gsi * Fc;
                    damageLimitStressC = 2.0 / 3.0 * Gsi * Fc;
                }
                ServiceLimitStrainC = serviceLimitStressC / Ec; // 使用限界圧縮ひずみ度
                DamageLimitStrainC = damageLimitStressC / Ec; // 損傷限界圧縮ひずみ度
                ServiceLimitStrainT = double.MinValue; // 使用限界引張ひずみ度（コンクリート引張は無視）
                DamageLimitStrainT = double.MinValue; // 損傷限界引張ひずみ度
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("限界ひずみの算定（→0）", ex);
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }

        /// <summary>
        /// 支圧倍率を適用（既製杭定着部用）
        /// 許容ひずみ度を倍率倍にし、bilinear降伏応力用にGsiを更新
        /// Ecは変更しない
        /// </summary>
        internal void ApplyBearingFactor(double factor)
        {
            BearingFactor = factor;
            ServiceLimitStrainC *= factor;
            DamageLimitStrainC *= factor;
        }

        // 密度を計算するメソッド
        internal static double GetDensity(double fc = 27, string type = "普通")
        {
            return type switch
            {
                "軽量1種" => fc switch
                {
                    <= 27 => 19.0,
                    <= 36 => 21.0,
                    _ => 24.5
                },
                "軽量2種" => fc <= 27 ? 17.0 : 24.5,
                _ => fc switch  // 普通
                {
                    <= 36 => 23.0,
                    <= 48 => 23.5,
                    <= 60 => 24.0,
                    _ => 24.5
                }
            };
        }

        // 弾性係数を計算するメソッド
        internal double GetEc(double gamma = 0)
        {
            if (gamma == 0)
            {
                gamma = GetDensity(Fc);
            }
            // Ec 算定用 ξ: オプション時は 1.0（強度側 Gsi·Fc 等は実 Gsi のまま）
            double gsiForEc = ConcreteModelOptions.UseUnitGsiForConcreteE ? 1.0 : Gsi;
            return 3.35 * Math.Pow(10, 4) * Math.Pow(gamma / 24, 2) * Math.Pow(gsiForEc * Fc / 60, 1.0 / 3.0);
        }

        internal void SetEpsilonCr()
        {
            EpsilonCr_bilinear = SigmaCr / Ec;
            EpsilonCr_eFunction = GetEFuncEpsilon(SigmaCr);
        }

        // ひずみ度から応力を計算するメソッド// ひずみ度から応力を計算するメソッド 使用限界、損傷限界用
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            if (type == MaterialLaw.Linear)
            {
                //if (epsilon > -EpsilonCr_linear) { return Ec * epsilon; } // 引張側を無視した線形弾性
                if (epsilon > 0) { return Math.Min(Ec * epsilon, BearingFactor * Gsi * Fc); } // 引張側を無視した線形弾性（支圧Fc上限）

                else
                { return 0.0; }
            }
            else if (type == MaterialLaw.EFunction)
            {
                if (-EpsilonCr_eFunction <= epsilon && epsilon <= SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN)
                {
                    epsilon = Math.Min(epsilon, EpsilonCu);
                    return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
                }
                else
                { return 0.0; }
            }
            else // (type == MaterialLaw.Bilinear)
            {
                // 引張側下限ひずみ: 既定はひび割れひずみ -EpsilonCr_bilinear（それまでは弾性で引張負担）。
                // 引張無視オプション時は 0 とし、引張域 (epsilon<0) は常に σ=0 とする。
                double tensionMinStrain = ConcreteModelOptions.IgnoreTensileStrength ? 0.0 : -EpsilonCr_bilinear;

                // 圧縮側折れ点応力度: 既定 Gsi·Fc、低減オプション時は 0.85·Fc（Gsi は乗じない）。
                double compressionPlateau = ConcreteModelOptions.UseReducedCompression
                    ? BearingFactor * ConcreteModelOptions.CompressionReductionFactor * Fc
                    : BearingFactor * Gsi * Fc;

                if (tensionMinStrain <= epsilon && epsilon <= SectionDesignConstants.ULTIMATE_COMPRESSIVE_STRAIN)
                {
                    return Math.Min(Ec * epsilon, compressionPlateau);
                }
                else
                { return 0.0; }
            }
        }

        internal double GetEFuncSigma(double epsilon)
        {
            return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
        }

        internal double GetEFuncDSonDEpsilon(double epsilon)
        {
            return 6.75 * (-0.812 / EpsilonM * Math.Exp(-0.812 * epsilon / EpsilonM) - (-1.218 / EpsilonM) * Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
        }

        internal double GetEFuncEpsilon(double sigma)
        {
            try
            {
                double epsilon = Math.Max(0.0, Math.Min(sigma / Ec, EpsilonCu));
                const int maxIter = 30;
                const double tol = 1e-6;

                for (int i = 0; i < maxIter; i++)
                {
                    double f = GetEFuncSigma(epsilon) - sigma;
                    if (Math.Abs(f) < tol)
                        return epsilon;

                    double df = GetEFuncDSonDEpsilon(epsilon);
                    if (Math.Abs(df) < 1e-12)
                        break;

                    double step = f / df;
                    if (Math.Abs(step) > Math.Abs(epsilon) * 0.5)
                        step = Math.Sign(step) * Math.Abs(epsilon) * 0.5;

                    double epsilonNext = epsilon - step;
                    epsilonNext = Math.Max(0.0, Math.Min(epsilonNext, EpsilonCu));

                    if (Math.Abs(epsilonNext - epsilon) < tol)
                        return epsilonNext;

                    epsilon = epsilonNext;
                }
                return Math.Max(0.0, Math.Min(epsilon, EpsilonCu));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[PileSection2.GetEFuncEpsilon] sigma={sigma}, EpsilonM={EpsilonM}, Ec={Ec}, Fc={Fc}: {ex.GetType().Name}: {ex.Message}");
                return 0.0;
            }
        }
    }

    // 主筋クラス
    internal class MainBars : Material
    {
        public double PCD { get; }
        public int Number { get; }
        public string Grade { get; }
        public string BarSize { get; }
        public double Ag { get; private set; }
        public double Er { get; private set; }
        public double RSigmaY { get; private set; }

        public double EpsilonE { get; private set; }
        public double EpsilonSi { get; set; }

        // 1.1×F（= 1.1×σy）で降伏する完全バイリニア型オプション（場所打ち RC / 場所打ち鋼管コンクリート杭）。
        // 断面コンストラクタで ConcreteModelOptions.RebarYieldAt11F から転写する。
        // setter で降伏応力度 RSigmaY と RSigmaY 依存の限界ひずみを再計算する。
        private bool _yieldAt11F;
        public bool YieldAt11F
        {
            get => _yieldAt11F;
            set
            {
                if (_yieldAt11F == value) return;
                _yieldAt11F = value;
                SetRSigmaY();
                SetAllowableStrain();
            }
        }

        // コンストラクタ
        internal MainBars(double pcd, int number, string grade, string barSize)
        {
            PCD = Math.Max(pcd, 100);
            // 主筋は CircularPipeSection で平均化したリングとして積分され、
            // 寄与は Ag = Number × area にのみ依存する。本数そのものは積分式に現れず、
            // 1～3 本でも安定に計算できるため最小本数のクランプは不要。
            // 負値のみ防ぐ (0 は「主筋なし」、Ag=0 として正しく寄与ゼロになる)。
            Number = Math.Max(number, 0);
            Grade = string.IsNullOrEmpty(grade) ? "SD345" : grade;
            BarSize = string.IsNullOrEmpty(barSize) ? "D25" : barSize;

            SetRSigmaY();
            SetAg();
            Er = 205000;
            EpsilonSi = 0.0;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            // 損傷限界（短期許容）・使用限界（長期許容）は 1.1F 完全バイリニア型オプションによらず
            // 基準降伏点 σy を用いる（1.1F は安全限界＝バイリニア/終局のみに適用）。
            // 使用限界＝長期許容引張応力度は RC 規準に従い min(σy/1.5, 径による上限)。
            // 異形鉄筋の上限は D≤25mm で 215、D>25mm で 195 N/mm²。
            double longTermCap = BarDiameter > 25.0 ? 195.0 : 215.0;
            double serviceLimitStress = Math.Min(BaseSigmaY / 1.5, longTermCap);
            double serviceLimitStressC = serviceLimitStress;  // 使用限界圧縮応力度
            double damageLimitStressC = BaseSigmaY;           // 損傷限界圧縮応力度 = σy（短期許容）
            double serviceLimitStressT = -serviceLimitStress; // 使用限界引張応力度
            double damageLimitStressT = -BaseSigmaY;          // 損傷限界引張応力度 = -σy

            ServiceLimitStrainC = serviceLimitStressC / Er; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / Er; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = serviceLimitStressT / Er; // 使用限界引張ひずみ度
            DamageLimitStrainT = damageLimitStressT / Er; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = double.MinValue; // 安全限界引張ひずみ度
        }

        // 降伏応力度を設定するメソッド
        private static readonly Dictionary<string, double> GradeYieldStrengths = new()
        {
            ["SD295"] = 295.0,
            ["SD345"] = 345.0,
            ["SD390"] = 390.0,
            ["SD490"] = 490.0,
            ["SD685"] = 685.0
        };

        // 基準降伏点 σy（規格値）。損傷限界・使用限界はこの値を用いる（1.1F オプション非適用）。
        private double BaseSigmaY => GradeYieldStrengths.GetValueOrDefault(Grade, 295.0);

        /// <summary>鉄筋規格名から規格降伏点 [N/mm²] を返す（諸元表の規格不一致チェック用）。</summary>
        public static double GradeYieldStrength(string grade) =>
            GradeYieldStrengths.GetValueOrDefault(grade ?? "", 295.0);

        // 主筋の呼び径 [mm]（"D25" → 25）。使用限界（長期許容）の径による上限（215/195）判定に用いる。
        private double BarDiameter =>
            (!string.IsNullOrEmpty(BarSize) && BarSize.Length > 1 && double.TryParse(BarSize.Substring(1), out double d))
                ? d : 25.0;

        internal void SetRSigmaY()
        {
            // 1.1F 完全バイリニア型オプション時は降伏応力度を 1.1×σy に引き上げる（安全限界にのみ効く）。
            // ただし RC基礎構造部材の耐震設計指針(案) に従い、SD490 は 1.1 倍の対象外（規格降伏点のまま）。
            // 主筋（せん断補強以外）は圧縮・引張とも 1.1 倍可のため、SD490 以外は ±1.1σy とする。
            bool apply11F = YieldAt11F && Grade != "SD490";
            RSigmaY = apply11F ? 1.1 * BaseSigmaY : BaseSigmaY;
        }
        private static readonly Dictionary<string, double> BarAreas = new()
        {
            ["D10"] = 71.3,
            ["D13"] = 127.0,
            ["D16"] = 199.0,
            ["D19"] = 287.0,
            ["D22"] = 387.0,
            ["D25"] = 507.0,
            ["D29"] = 642.0,
            ["D32"] = 794.0,
            ["D35"] = 957.0,
            ["D38"] = 1140.0,
            ["D41"] = 1340.0,
            ["D51"] = 2027.0
        };

        internal void SetAg()
        {
            try
            {
                if (!BarAreas.TryGetValue(BarSize, out double area))
                {
                    area = 0.0;
                    Serilog.Log.Debug($"Warning: Invalid BarSize '{BarSize}' detected.");
                }
                Ag = Number * area;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("主筋断面積の算定（→0）", ex, $"BarSize={BarSize}, Number={Number}");
                Ag = 0.0;
            }
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            if (RSigmaY / Er < epsilon + EpsilonSi) { return RSigmaY; }
            else if (epsilon + EpsilonSi < -RSigmaY / Er) { return -RSigmaY; }
            else { return Er * (epsilon + EpsilonSi); }
        }
    }

    /// <summary>
    /// 鋼管規格の材料特性を管理する静的クラス。
    ///
    /// F は<b>基準強度</b>（平成12年建設省告示第2464号）であって、JIS A 5525 の規格降伏点ではない。
    /// SKK490 は規格降伏点が 315 N/mm²、基準強度が 325 N/mm² と値が分かれるので注意すること
    /// （SKK400 はどちらも 235 N/mm²）。
    /// このクラスの F は許容応力度（長期 F/1.5・短期 F）と材料強度（1.1F）の基準になるため、
    /// 規格降伏点ではなく基準強度を持つのが正しい。
    /// ジャパンパイル Technical Note Vol.1-5 (2022年11月) 表1 も SKK490 の基準強度を 325 とする。
    /// </summary>
    internal static class SteelPipeGrades
    {
        private static readonly Dictionary<string, (double SigmaU, double F)> Properties = new()
        {
            ["SKK400"] = (400.0, 235.0),
            ["SKK490"] = (490.0, 325.0)
        };

        public static (double SigmaU, double F) GetProperties(string grade)
            => Properties.GetValueOrDefault(grade, (400.0, 235.0));
    }

    // 現場打ち鋼管クラス
    internal class InsituSteelPipe : Material
    {
        public string Grade { get; }
        public double OutDia { get; }
        public double T { get; }
        // 有効板厚: コンストラクタで既に「公称板厚 − 腐食代」を反映済みのため、
        // ここでは追加の減厚を行わない（板厚の負の許容差 1mm は控除しない方針）。
        public double TMinus => T;
        public double SSigmaU { get; private set; }
        public double F { get; private set; }
        public double Fcy => 1.1 * F;
        public double OutDiaMinus => OutDia - 2.0;
        public double AMinus => Math.PI * (OutDia - TMinus) * TMinus;
        public double SSigmaY { get; private set; }　// 材料強度 1.1F
        public double SEpsilonY { get; private set; }
        public double SEpsilonU { get; private set; }
        public double SE1 { get; private set; } = 205000.0;
        public double SE2 { get; private set; } = 205000.0 / 30.0;
        public double IMinus => Math.PI * (Math.Pow(OutDiaMinus, 4) - Math.Pow(OutDiaMinus - 2 * TMinus, 4)) / 64.0;

        // 1.1×F で降伏する完全バイリニア型オプション（場所打ち鋼管コンクリート杭のみ）。
        // 断面コンストラクタで ConcreteModelOptions.SteelPipeYieldAt11F から転写する。
        // true のとき GetStress はひずみ硬化(SE2)・破断応力(SSigmaU)を廃し ±SSigmaY(=1.1F) で頭打ちにする。
        public bool PerfectBilinear11F { get; set; }

        // コンストラクタ
        public InsituSteelPipe(string _Grade, double _OutDia, double _T, double _corrosionDepth)
        {
            Grade = _Grade;
            OutDia = _OutDia - 2 * _corrosionDepth;
            T = _T - _corrosionDepth;

            SetMaterialProperties();
        }

        private void SetMaterialProperties()
        {
            var (sigmaU, f) = SteelPipeGrades.GetProperties(Grade);
            SSigmaU = sigmaU;
            F = f;
            SSigmaY = 1.1 * F;
            SEpsilonY = SSigmaY / SE1;
            SEpsilonU = (SSigmaU - SSigmaY) / SE2 + SEpsilonY;
            SetAllowableStrain(F);
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain(double F)
        {
            double serviceLimitStressC = F / 1.5;
            double damageLimitStressC = F;
            double serviceLimitStressT = -F / 1.5;
            double damageLimitStressT = -F;
            ServiceLimitStrainC = serviceLimitStressC / SE1; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = damageLimitStressC / SE1; // 損傷限界圧縮ひずみ度
            ServiceLimitStrainT = serviceLimitStressT / SE1; // 使用限界引張ひずみ度
            DamageLimitStrainT = damageLimitStressT / SE1; // 損傷限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            // 指針(案) 準拠の安全限界トリリニア: 圧縮側は 0.85×引張強さで頭打ち、引張側は引張強さで頭打ち。
            // 折れ点=材料強度 sσy(=1.1F)、第2勾配 SE2。圧縮限界ひずみ = (0.85·SSigmaU−SSigmaY)/SE2 + SEpsilonY。
            // 注: 鋼管 1.1F オプション(PerfectBilinear11F)より優先する。安全限界を指針で算定する場合は
            // 本トリリニアが正となるため（UI でも 1.1F 鋼管はグレーアウトして併用を防ぐ）。
            if (type == MaterialLaw.GuidelineUltimate)
            {
                double compCap = 0.85 * SSigmaU;                                  // sσcu = 0.85·sσtb
                double epsCompCap = (compCap - SSigmaY) / SE2 + SEpsilonY;         // sεcu
                if (epsilon > epsCompCap) { return compCap; }                      // 圧縮プラトー 0.85·SSigmaU
                else if (epsilon > SEpsilonY) { return SSigmaY + SE2 * (epsilon - SEpsilonY); } // 圧縮硬化
                else if (epsilon < -SEpsilonU) { return -SSigmaU; }                // 引張プラトー -SSigmaU
                else if (epsilon < -SEpsilonY) { return -SSigmaY + SE2 * (epsilon + SEpsilonY); } // 引張硬化
                else { return epsilon * SE1; }                                     // 弾性
            }

            // 完全バイリニア型オプション: 弾性 → ±SSigmaY(=1.1F) で頭打ち（ひずみ硬化・破断なし）
            if (PerfectBilinear11F)
            {
                if (epsilon > SEpsilonY) { return SSigmaY; }
                else if (epsilon < -SEpsilonY) { return -SSigmaY; }
                else { return epsilon * SE1; }
            }

            if (SEpsilonU < epsilon) { return SSigmaU; }
            else if (SEpsilonY < epsilon) { return SSigmaY + SE2 * (epsilon - SEpsilonY); }
            else if (epsilon < -SEpsilonU) { return -SSigmaU; }
            else if (epsilon < -SEpsilonY) { return -SSigmaY + SE2 * (epsilon + SEpsilonY); }
            else { return epsilon * SE1; }
        }
    }

    // プレキャストコンクリートクラス
    internal class PrecastConcrete : Material
    {
        public double Fc { get; protected set; }
        public double EpsilonCu { get; protected set; }
        public double EpsilonCy { get; protected set; }
        public double Ec { get; protected set; } = 40_000.0;

        public double DO { get; protected set; }
        public double DI { get; protected set; }
        public double EpsilonE { get; set; }

        // コンストラクタ
        public PrecastConcrete()
        {

        }

        /// <summary>
        /// PHC/PRC共通の初期化処理
        /// </summary>
        protected void InitializeForPHCPRC(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            if (Fc < 105)
            {
                EpsilonCu = 0.0025;
                EpsilonCy = Fc / Ec;
            }
            else
            {
                EpsilonCu = Fc / Ec;
                EpsilonCy = Fc / Ec;
            }
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            // 算術式のみで例外は発生しない。以前の try/catch (Exception ex) は無意味だったため削除。
            if (EpsilonCy <= EpsilonE + epsilon) // && (EpsilonE + epsilon) <= EpsilonCu) // 降伏域
            {
                return Fc;
            }
            else if (0 < EpsilonE + epsilon && EpsilonE + epsilon < EpsilonCy)
            {
                return Ec * (EpsilonE + epsilon); // 弾性範囲
            }
            else
            {
                return 0.0;
            }
        }
    }

    internal class PrecastPHCConcrete : PrecastConcrete
    {

        // コンストラクタ
        public PrecastPHCConcrete(double _DO, double _DI, double _Fc)
        {
            InitializeForPHCPRC(_DO, _DI, _Fc);
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            try
            {
                double serviceLimitStressC = Fc / 3.5;
                double damageLimitStressC = Fc * 2.0 / 3.5;
                double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
                double damageLimitStressT = -0.56 * Math.Sqrt(Fc);

                ServiceLimitStrainC = serviceLimitStressC / Ec;
                DamageLimitStrainC = damageLimitStressC / Ec;
                UltimateLimitStrainC = EpsilonCu;

                ServiceLimitStrainT = serviceLimitStressT / Ec;
                DamageLimitStrainT = damageLimitStressT / Ec;
                UltimateLimitStrainT = double.MinValue;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("限界ひずみの算定（→0）", ex);
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }
    }

    internal class PrecastPRCConcrete : PrecastConcrete
    {
        // コンストラクタ
        public PrecastPRCConcrete(double _DO, double _DI, double _Fc)
        {
            InitializeForPHCPRC(_DO, _DI, _Fc);
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            try
            {
                double serviceLimitStressC = Fc / 3.5;
                double damageLimitStressC = Fc * 2.0 / 3.5;

                double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
                double damageLimitStressT = double.MinValue; // <<<<<

                ServiceLimitStrainC = serviceLimitStressC / Ec;
                DamageLimitStrainC = damageLimitStressC / Ec; // <<<<<
                UltimateLimitStrainC = EpsilonCu;

                ServiceLimitStrainT = serviceLimitStressT / Ec;
                DamageLimitStrainT = damageLimitStressT / Ec;
                UltimateLimitStrainT = double.MinValue;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("限界ひずみの算定（→0）", ex);
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }
    }

    internal class PrecastSCConcrete : PrecastConcrete
    {
        // コンストラクタ
        public PrecastSCConcrete(double _DO, double _DI, double _Fc)
        {
            DO = _DO;
            DI = _DI;
            Fc = _Fc;
            //double ts = (DO - DI) * 0.5;
            //if ((DO - 2 * ts) / ts <= 6)
            //{
            EpsilonCu = 0.0030; // とりあえず3000マイクロとする。(7.6)
            EpsilonCy = Fc / Ec;
            //}
            //else //  ((DO - 2 * ts) / ts > 6)
            //{
            //    EpsilonCu = 0.0030;
            //    EpsilonCy = Fc / Ec;
            //}
            //DO = _DO;
            //DI = _DI;
            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            try
            {
                double serviceLimitStressC = Fc / 3.5;
                double damageLimitStressC = Fc * 2.0 / 3.5;

                //double serviceLimitStressT = -0.56 * Math.Sqrt(Fc) / 2.0;
                /* double damageLimitStressT = Double.MinValue;*/ // <<<<<

                ServiceLimitStrainC = serviceLimitStressC / Ec;
                DamageLimitStrainC = damageLimitStressC / Ec; // <<<<<
                                                              //UltimateLimitStrainC = EpsilonCu;

                ServiceLimitStrainT = double.MinValue;
                DamageLimitStrainT = double.MinValue;
                UltimateLimitStrainT = double.MinValue;
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("限界ひずみの算定（→0）", ex);
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }
    }

    // PC鋼材クラス
    internal class Tendons : Material
    {
        public double Fpy { get; }
        public double Fpu { get; }
        public double PCD { get; }
        public double Ap { get; }
        public double Ep { get; private set; } = 200000.0;
        public double Ep2 { get; private set; }
        public double EpsilonPu { get; private set; }
        public double EpsilonPy { get; private set; }
        public double SigmaE { get; private set; }
        public double EpsilonPi { get; set; }

        // コンストラクタ
        public Tendons(double _PCD, double _ap, double _fpy = 1226.0, double _fpu = 1418.0, double _epsilonPu = 0.02)
        {
            if (_fpy == 0.0) { Fpy = 1226.0; }
            else { Fpy = _fpy; }
            if (_fpu == 0.0) { Fpu = 1418.0; }
            else { Fpu = _fpu; }

            PCD = _PCD;
            Ap = _ap;
            EpsilonPu = _epsilonPu; // 正値
            EpsilonPy = Fpy / Ep; // 正値
            Ep2 = (Fpu - Fpy) / (0.015 - EpsilonPy);

            SetAllowableStrain();
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            ServiceLimitStrainC = double.MaxValue; // 使用限界圧縮ひずみ度
            DamageLimitStrainC = double.MaxValue; // 損傷限界圧縮ひずみ度
            UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

            ServiceLimitStrainT = double.MinValue; // 使用限界引張ひずみ度
            DamageLimitStrainT = double.MinValue; // 損傷限界引張ひずみ度
            UltimateLimitStrainT = -EpsilonPu; // 安全限界引張ひずみ度
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            if (0 < EpsilonPi + epsilon) { return 0.0; } // 圧縮の場合
            else if (-EpsilonPy < EpsilonPi + epsilon) // 第一勾配
            {
                return Ep * (EpsilonPi + epsilon);
            }
            else if (-0.015 < EpsilonPi + epsilon) // 第二勾配
            {
                return -Fpy + (EpsilonPi + epsilon + EpsilonPy) * Ep2;
            }
            else
            {
                return -Fpu;
            }
        }
    }

    // プレキャスト鋼管クラス
    internal class PrecastSteelPipe : Material
    {
        public string Grade { get; private set; }
        public double OutDia { get; private set; }
        public double T { get; private set; }
        public double F { get; private set; }
        public double SE1 { get; private set; } = 205000.0;
        public double Ftsp { get; private set; }
        public double Fcsp { get; private set; }
        public double Ftdp { get; private set; }
        public double Fcdp { get; private set; }
        public double Fys { get; private set; }
        public double EpsilonY { get; private set; }
        public double As { get; private set; }

        // コンストラクタ
        public PrecastSteelPipe(string _Grade, double _OutDia, double _T, double _corrosionDepth)
        {
            Grade = _Grade;
            OutDia = _OutDia - 2 * _corrosionDepth;
            T = _T - _corrosionDepth;

            SetMaterialProperties();
        }

        //プロパティをセットするメソッド
        private void SetMaterialProperties()
        {
            try
            {
                //// 鋼管の設計基準強度
                var (_, f) = SteelPipeGrades.GetProperties(Grade);
                F = f;
                As = (OutDia - T) * Math.PI * T;
                Ftsp = -F / 1.5; //鋼管の使用限界引張応力度
                Fcsp = F / 1.5; //鋼管の使用限界圧縮応力度
                Ftdp = -F; //鋼管の使用限界引張応力度
                Fcdp = F; //鋼管の使用限界圧縮応力度
                Fys = 1.1 * F; // 鋼管の降伏強度
                EpsilonY = Fys / SE1; // 降伏ひずみ

                SetAllowableStrain();
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("鋼管諸元の算定（→0）", ex, $"Grade={Grade}, OutDia={OutDia}, T={T}");
                F = 0.0;
                As = 0.0;
                Ftsp = 0.0;
                Fcsp = 0.0;
                Ftdp = 0.0;
                Fcdp = 0.0;
                Fys = 0.0;
                EpsilonY = 0.0;
            }
        }

        // 限界ひずみ度を計算するメソッド
        internal void SetAllowableStrain()
        {
            try
            {
                ServiceLimitStrainC = Fcsp / SE1; // 使用限界圧縮ひずみ度
                DamageLimitStrainC = Fcdp / SE1; // 損傷限界圧縮ひずみ度
                UltimateLimitStrainC = double.MaxValue; // 安全限界圧縮ひずみ度

                ServiceLimitStrainT = Ftsp / SE1; // 使用限界引張ひずみ度
                DamageLimitStrainT = Ftdp / SE1; // 損傷限界引張ひずみ度
                UltimateLimitStrainT = double.MinValue; // 安全限界引張ひずみ度
            }
            catch (Exception ex)
            {
                PileDesign.Common.CalcFallbackTracker.Report("限界ひずみの算定（→0）", ex);
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(MaterialLaw type, double epsilon)
        {
            if (EpsilonY < epsilon) { return Fys; }
            else if (epsilon < -EpsilonY) { return -Fys; }
            else { return epsilon * SE1; }
        }
    }
}
