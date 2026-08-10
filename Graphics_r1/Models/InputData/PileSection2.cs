using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models.InputData
{
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

        internal abstract double GetStress(string type, double epsilon);
    }

    // 現地打ちコンクリートクラス
    internal class InsituConcrete : Material
    {
        public double DO { get; }
        public string Type { get; }
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
                Serilog.Log.Warning(ex, "[InsituConcrete] 初期化に失敗（Ec/Ac/SigmaCr=0 にフォールバック）: DO={DO}, Gsi={Gsi}, Fc={Fc}", DO, Gsi, Fc);
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
                Serilog.Log.Warning(ex, "[Material.SetAllowableStrain] 限界ひずみの算定に失敗（すべて 0 にフォールバック）");
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
        internal override double GetStress(string type, double epsilon)
        {
            if (type == "linear")
            {
                //if (epsilon > -EpsilonCr_linear) { return Ec * epsilon; } // 引張側を無視した線形弾性
                if (epsilon > 0) { return Math.Min(Ec * epsilon, BearingFactor * Gsi * Fc); } // 引張側を無視した線形弾性（支圧Fc上限）

                else
                { return 0.0; }
            }
            else if (type == "eFunction")
            {
                if (-EpsilonCr_eFunction <= epsilon && epsilon <= 0.003)
                {
                    epsilon = Math.Min(epsilon, EpsilonCu);
                    return 6.75 * (Math.Exp(-0.812 * epsilon / EpsilonM) - Math.Exp(-1.218 * epsilon / EpsilonM)) * Gsi * Fc;
                }
                else
                { return 0.0; }
            }
            else // (type == "bilinear")
            {
                // 引張側下限ひずみ: 既定はひび割れひずみ -EpsilonCr_bilinear（それまでは弾性で引張負担）。
                // 引張無視オプション時は 0 とし、引張域 (epsilon<0) は常に σ=0 とする。
                double tensionMinStrain = ConcreteModelOptions.IgnoreTensileStrength ? 0.0 : -EpsilonCr_bilinear;

                // 圧縮側折れ点応力度: 既定 Gsi·Fc、低減オプション時は 0.85·Fc（Gsi は乗じない）。
                double compressionPlateau = ConcreteModelOptions.UseReducedCompression
                    ? BearingFactor * ConcreteModelOptions.CompressionReductionFactor * Fc
                    : BearingFactor * Gsi * Fc;

                if (tensionMinStrain <= epsilon && epsilon <= 0.003)
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
                System.Diagnostics.Debug.WriteLine($"[PileSection2.GetEFuncEpsilon] sigma={sigma}, EpsilonM={EpsilonM}, Ec={Ec}, Fc={Fc}: {ex.GetType().Name}: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"Warning: Invalid BarSize '{BarSize}' detected.");
                }
                Ag = Number * area;
            }
            catch (Exception)
            {
                Ag = 0.0;
            }
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (RSigmaY / Er < epsilon + EpsilonSi) { return RSigmaY; }
            else if (epsilon + EpsilonSi < -RSigmaY / Er) { return -RSigmaY; }
            else { return Er * (epsilon + EpsilonSi); }
        }
    }

    /// <summary>
    /// 鋼管規格の材料特性を管理する静的クラス
    /// </summary>
    internal static class SteelPipeGrades
    {
        private static readonly Dictionary<string, (double SigmaU, double F)> Properties = new()
        {
            ["SKK400"] = (400.0, 235.0),
            ["SKK490"] = (490.0, 315.0)
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
        internal override double GetStress(string type, double epsilon)
        {
            // 指針(案) 準拠の安全限界トリリニア: 圧縮側は 0.85×引張強さで頭打ち、引張側は引張強さで頭打ち。
            // 折れ点=材料強度 sσy(=1.1F)、第2勾配 SE2。圧縮限界ひずみ = (0.85·SSigmaU−SSigmaY)/SE2 + SEpsilonY。
            // 注: 鋼管 1.1F オプション(PerfectBilinear11F)より優先する。安全限界を指針で算定する場合は
            // 本トリリニアが正となるため（UI でも 1.1F 鋼管はグレーアウトして併用を防ぐ）。
            if (type == "guidelineUltimate")
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
        internal override double GetStress(string type, double epsilon)
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
                Serilog.Log.Warning(ex, "[Material.SetAllowableStrain] 限界ひずみの算定に失敗（すべて 0 にフォールバック）");
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
                Serilog.Log.Warning(ex, "[Material.SetAllowableStrain] 限界ひずみの算定に失敗（すべて 0 にフォールバック）");
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
                Serilog.Log.Warning(ex, "[Material.SetAllowableStrain] 限界ひずみの算定に失敗（すべて 0 にフォールバック）");
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
        internal override double GetStress(string type, double epsilon)
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
            catch (Exception)
            {
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
                Serilog.Log.Warning(ex, "[Material.SetAllowableStrain] 限界ひずみの算定に失敗（すべて 0 にフォールバック）");
                ServiceLimitStrainC = 0.0;
                DamageLimitStrainC = 0.0;
                UltimateLimitStrainC = 0.0;
                ServiceLimitStrainT = 0.0;
                DamageLimitStrainT = 0.0;
                UltimateLimitStrainT = 0.0;
            }
        }

        // ひずみ度から応力を計算するメソッド
        internal override double GetStress(string type, double epsilon)
        {
            if (EpsilonY < epsilon) { return Fys; }
            else if (epsilon < -EpsilonY) { return -Fys; }
            else { return epsilon * SE1; }
        }
    }

    /// <summary>
    /// /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// </summary>
    // 断面プロファイルの材料種別 (描画色分け用)
    internal enum SectionMaterialKind { Concrete, MainBar, Tendon, SteelPipe }

    // 1 材料分のひずみ度・応力度プロファイル (z 配列ごとの ε, σ)。
    // 中空部などデータの無い区間は ε,σ に double.NaN を入れて線を分断する。
    internal class MaterialProfile
    {
        public SectionMaterialKind Kind { get; set; }
        public string Name { get; set; } = "";
        public List<double> Z { get; } = new();       // 断面高さ z [mm]
        public List<double> Strain { get; } = new();   // 断面の平面保持ひずみ ε [-]
        public List<double> Stress { get; } = new();   // 材料応力度 σ [N/mm2]
    }

    /// <summary>
    /// 杭断面のひずみ度・応力度分布 (描画用)。各材料を 1 本の MaterialProfile として持つ。
    /// z は断面高さ [mm]（圧縮縁 z=-Radius、引張縁 z=+Radius、平面保持で ε(z)=ε0-φz）。
    /// </summary>
    internal class SectionStrainStressProfile
    {
        public double Radius { get; set; }                       // 断面外縁半径 [mm]
        public List<MaterialProfile> Materials { get; } = new();
        public double CompressionEdgeStrain { get; set; }        // 圧縮縁ひずみ (z=-Radius)
        public double TensionEdgeStrain { get; set; }            // 引張縁ひずみ (z=+Radius)
    }

    // 杭断面抽象クラス
    internal abstract class AbstractPileSection : IPileSectionCalculation
    {
        public double CurvatureMaxServiceLimit { get; protected set; }
        public double CurvatureMaxDamageLimit { get; protected set; }
        public double CurvatureMaxUltimateLimit { get; protected set; }

        public double AxialForceCurvatureMaxServiceLimit { get; protected set; }
        public double AxialForceCurvatureMaxDamageLimit { get; protected set; }
        public double AxialForceCurvatureMaxUltimateLimit { get; protected set; }

        public List<double> Prestrains { get; protected set; }

        public List<double> ServiceLimitStrainCs { get; protected set; }
        public List<double> DamageLimitStrainCs { get; protected set; }
        public List<double> UltimateLimitStrainCs { get; protected set; }
        public List<double> PositionCs { get; protected set; }

        public List<double> ServiceLimitStrainTs { get; protected set; }
        public List<double> DamageLimitStrainTs { get; protected set; }
        public List<double> UltimateLimitStrainTs { get; protected set; }

        public List<double> PositionTs { get; protected set; }

        public double PileDia { get; protected set; }

        /// <summary>
        /// 圧縮縁位置（コンクリート圧縮縁）。鋼管コンクリート杭では鋼管厚を減じた位置。
        /// </summary>
        protected virtual double CompressionEdgePosition => -PileDia / 2;

        public List<double> ServiceLimitAxialForceThresholds { get; protected set; }
        public List<double> ServiceLimitBendingMomentThresholds { get; protected set; }
        public List<double> ServiceLimitBeta { get; protected set; }

        public List<double> DamageLimitAxialForceThresholds { get; protected set; }
        public List<double> DamageLimitBendingMomentThresholds { get; protected set; }
        public List<double> DamageLimitBeta { get; protected set; }  // レベル2（β1×β2）
        public List<double> DamageLimitBetaL1 { get; protected set; }  // レベル1（β2=1.0、β1のみ）

        public List<double> UltimateLimitAxialForceThresholds { get; protected set; }
        public List<double> UltimateLimitBendingMomentThresholds { get; protected set; }
        public List<double> UltimateLimitBeta { get; protected set; }

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
            SectionMaterialKind kind, string name, Material mat, string type,
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
            SectionMaterialKind kind, string name, Material mat, string type,
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
                while (Math.Abs(N - NTarget) > 0.1 && iter < maxIter) // 0.1N 以上の差がある場合
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
                Serilog.Log.Warning(ex, "[GetAllowableMomentForSpecificN] 算定に失敗（M=0 にフォールバック）: limitStateNo={LimitStateNo}, NTarget={NTarget}", limitStateNo, NTarget);
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
                double epsilonC = 0.003;
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
                while (Math.Abs(N - NTarget) > 0.1 && iter < maxIter)
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
                Serilog.Log.Warning(ex, "[GetUltimateMomentForSpecificN] 算定に失敗（0 にフォールバック）: NTarget={NTarget}", NTarget);
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
        /// ファイバー掃引で圧縮縁ひずみ εc を探索する上限。既定はコンクリート安全限界圧縮ひずみ 0.003
        /// （InsituConcrete.GetStress は ε&gt;0.003 で σ=0 に脱落し N(εc) が非単調になるため超えない）。
        /// </summary>
        internal virtual double FiberSweepEdgeStrainMax => 0.003;

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
                Serilog.Log.Warning(ex, "[GetMPhiRelationshipFiber] 算定に失敗（null にフォールバック）: Ntarget={Ntarget}", Ntarget);
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
            double tolN = Math.Max(100.0, Math.Abs(Ntarget) * 1e-6); // [N]（M への影響は無視できる規模）

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
            catch (Exception)
            {
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
            catch (Exception)
            {
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

    // 円形断面クラス
    internal class CircularSolidSection
    {
        private double Dia { get; }

        // コンストラクタ
        internal CircularSolidSection(double diameter)
        {
            Dia = diameter;
        }

        // 軸力、曲げモーメント取得メソッド
        //
        // 各短冊の幾何量（面積・断面一次モーメント）を解析的に厳密計算する補正版。
        // 弦長 w(z)=2√(R²-z²) は縁 z=±R で接線が垂直（√特異性）となるため、
        // 「幅(中点)×dz」の中点則では分割を増やしても面積が真円 πR² に収束しにくい（誤差 O(n^-1.5)、端の短冊が支配的）。
        // そこで短冊 [z1,z2] ごとに
        //   面積          A = ∫ 2√(R²-z²) dz
        //   断面一次モーメント Q = ∫ 2z√(R²-z²) dz
        // を閉形式で評価する。これにより ΣA は任意の分割数で厳密に πR² となり、
        // モーメントの幾何重み付けも厳密になる。応力は各短冊の図心 z̄ = Q/A で評価し、
        // 残る近似は「短冊内で応力一定」のみ（縁の特異性を持たないため速やかに収束する）。
        internal (double, double) GetForceAndMoment(string type, Material material, double epsilon0, double curvature, int division = 200)
        {
            try
            {
                double r = Dia * 0.5;
                double dz = Dia / division;

                double axialForce = 0.0;
                double bendingMoment = 0.0;

                // 望遠鏡和: 各短冊の境界での不定積分値を 1 回ずつ評価して差分をとる。
                double prevArea = AreaAntiderivative(-r, r);
                double prevMoment = FirstMomentAntiderivative(-r, r);

                for (int i = 0; i < division; i++)
                {
                    // 端の丸め誤差を避けるため最終短冊上端は厳密に +r とする。
                    double z2 = (i == division - 1) ? r : -r + (i + 1) * dz;
                    double curArea = AreaAntiderivative(z2, r);
                    double curMoment = FirstMomentAntiderivative(z2, r);

                    double area = curArea - prevArea;            // 短冊の面積 (>0)
                    double firstMoment = curMoment - prevMoment; // 短冊の断面一次モーメント Q

                    prevArea = curArea;
                    prevMoment = curMoment;

                    if (area <= 0.0) continue; // 退化短冊の保護

                    double zBar = firstMoment / area;            // 図心
                    double epsilon = epsilon0 - curvature * zBar;
                    double sigma = material.GetStress(type, epsilon);

                    axialForce += sigma * area;
                    bendingMoment += -sigma * firstMoment;       // M = ∫ -z·σ·w dz = -σ·Q
                }
                return (axialForce, bendingMoment);
            }
            catch (Exception)
            {
                return (0.0, 0.0);
            }
        }

        // 面積の不定積分: ∫ 2√(r²-z²) dz = z√(r²-z²) + r²·asin(z/r)
        private static double AreaAntiderivative(double z, double r)
        {
            double t = Math.Max(0.0, r * r - z * z);
            double ratio = Math.Clamp(z / r, -1.0, 1.0);
            return z * Math.Sqrt(t) + r * r * Math.Asin(ratio);
        }

        // 断面一次モーメントの不定積分: ∫ 2z√(r²-z²) dz = -(2/3)(r²-z²)^{3/2}
        private static double FirstMomentAntiderivative(double z, double r)
        {
            double t = Math.Max(0.0, r * r - z * z);
            return -(2.0 / 3.0) * Math.Pow(t, 1.5);
        }
    }

    // 円環断面クラス
    internal class CircularPipeSection(double diameter, double t)
    {
        private double Dia { get; } = diameter;
        private double T { get; } = t;

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(string type, Material material, double epsilon0, double curvature, int division = 200)
        {
            try
            {
                double z;
                double dCirc = Math.PI * Dia / division;
                double epsilon;
                double sigma;

                double axialForce = 0.0;
                double bendingMoment = 0.0;

                for (int i = 0; i < division; i++)
                {
                    z = Dia * 0.5 * Math.Cos(2.0 * Math.PI * i / division);
                    epsilon = epsilon0 - curvature * z;
                    sigma = material.GetStress(type, epsilon);
                    axialForce += T * sigma * dCirc;
                    bendingMoment += T * sigma * dCirc * -z;
                }
                return (axialForce, bendingMoment);
            }
            catch (Exception)
            {
                return (0.0, 0.0);
            }
        }
    }
}