using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class PileTop : BaseModel
    {
        // フィールド
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        private string _pileBodyType;
        public string PileBodyType
        {
            get => _pileBodyType;
            set => SetProperty(ref _pileBodyType, value);
        }

        private string _pileTopType;
        public string PileTopType
        {
            get => _pileTopType;
            set => SetProperty(ref _pileTopType, value);
        }

        // パイルキャップ
        private double _pileCapFc = 24.0;
        public double PileCapFc
        {
            get => _pileCapFc;
            set => SetProperty(ref _pileCapFc, value);
        }

        private double _pileCapGamma = 23.0;
        public double PileCapGamma
        {
            get => _pileCapGamma;
            set => SetProperty(ref _pileCapGamma, value);
        }

        private double _pileCapEc;
        public double PileCapEc
        {
            get => 3.35 * Math.Pow(10, 4) * Math.Pow(PileCapGamma / 24.0, 2.0) * Math.Pow(PileCapFc / 60.0, 1.0 / 3.0);
            set
            {
                if (SetProperty(ref _pileCapEc, value))
                {
                    _pileCapEc = 3.35 * Math.Pow(10, 4) * Math.Pow(PileCapGamma / 24.0, 2.0) * Math.Pow(PileCapFc / 60.0, 1.0 / 3.0);
                }
            }
        }

        private ObservableCollection<Spec> _selectedPileTopSpecification;
        public ObservableCollection<Spec> SelectedPileTopSpecification
        {
            get => _selectedPileTopSpecification;
            set => SetProperty(ref _selectedPileTopSpecification, value);
        }


        // PCリングオプション
        //public ObservableCollection<string> PCRingOption { get; }
        public ObservableCollection<string> FTCapOption { get; set; }

        //public List<PCRing> PCRings { get; set; }
        public ObservableCollection<(int, string, string)> Description { get; set; }

        // FTパイルオプション
        public int SelectedFTCap { get; set; }

        private FTPile _FTPile;
        public FTPile FTPile
        {
            get => _FTPile;
            set => SetProperty(ref _FTPile, value);
        }

        private CaptainPile _captainPile;
        public CaptainPile CaptainPile
        {
            get => _captainPile;
            set => SetProperty(ref _captainPile, value);
        }

        private CapringPile _capringPile;
        public CapringPile CapringPile
        {
            get => _capringPile;
            set => SetProperty(ref _capringPile, value);
        }


        // コンクリート外径
        private double _concreteOutDia;
        public double ConcreteOutDia
        {
            get => _concreteOutDia;
            set
            {
                if (SetProperty(ref _concreteOutDia, value))
                {
                    // 鉄筋配置直径 (MainBarDr1/Dr2) は ConcreteOutDia とかぶり厚から派生する量。
                    // かぶり厚 setter (MainBarCenterCover1/2) でも再計算しているが、外径が後から
                    // 設定された場合 (例: 鋼管杭+鉄筋定着工法 で SteelPipePileLowerRcDiameter で自動算定) に
                    // Dr が古いままだと GetSteelPilePileHeadJointInfo が null を返してしまうため、
                    // ここでも同期的に再計算する。
                    MainBarDr1 = _concreteOutDia - 2.0 * MainBarCenterCover1;
                    MainBarDr2 = _concreteOutDia - 2.0 * MainBarCenterCover2;
                }
            }
        }

        // 鋼管杭+鉄筋定着工法用 — 杭頭部の下層 RC 断面径 [mm]
        // 公式 (場所打ち鋼管コンクリート杭の +200mm ルールから派生):
        //   pile_dia ≤ 1200 → pile_dia × 1.25 + 100
        //   pile_dia >  1200 → pile_dia + 200
        // 入力する pile_dia は最上部杭区間 (= コンクリート充填鋼管部) の腐食代考慮後の有効径。
        // 鋼管杭以外の杭体タイプでは未使用。
        public double SteelPipePileLowerRcDiameter(double pileDiaMm)
        {
            if (pileDiaMm <= 0.0) return 0.0;
            return pileDiaMm <= 1200.0
                ? pileDiaMm * 1.25 + 100.0
                : pileDiaMm + 200.0;
        }

        /// <summary>
        /// 鋼管杭+鉄筋定着工法 の杭頭接合部の弾性回転剛性に関する情報。
        /// Ktheta [kN·m/rad] と参考値としての降伏点 (PtThetaY [rad], RMtyKNm [kN·m])。
        /// FEM では Ktheta の線形ばねのみ使用し降伏点は無視するが、グラフ表示の
        /// 上限点として PtThetaY/RMtyKNm を利用する。
        /// </summary>
        internal sealed record SteelPilePileHeadJointInfo(double Ktheta, double PtThetaY, double RMtyKNm);

        /// <summary>
        /// 鋼管杭+鉄筋定着工法 の付着長さ Ld 算定に用いる軸力非依存の諸係数。
        /// 杭基礎指針:
        ///   Ld = pcλ × pcα × S × rσy × dd / 10 / fb
        ///   pcλ = 0.86 (付着長さの補正係数)
        ///   pcα = 1.0  (割裂破壊に対する補正係数 — 横補強筋拘束あり想定。拘束なしは 1.25)
        ///   S   = 1.0  (必要長さの修正係数 — 直線定着筋)
        ///   fb  = Fc/40 + 0.9 [N/mm²] (付着割裂の基準となる強度)
        ///   dd  = 定着筋の呼び名数値 (D41 → 41)
        ///   rσy = 定着筋規格降伏点 [N/mm²]
        /// </summary>
        internal sealed record SteelPilePileHeadJointConstants(
            double Pclambda, double Pcalpha, double S, double Fb, double Ld,
            double Dd, double RsigmaY);

        /// <summary>
        /// 鋼管杭+鉄筋定着工法 の付着長さ Ld と関連諸係数を返す (軸力非依存)。
        /// 必要入力 (PileCapFc, MainBarSize1, MainBarSpec1) が欠落していれば null。
        /// </summary>
        internal SteelPilePileHeadJointConstants? GetSteelPilePileHeadJointConstants()
        {
            if (PileCapFc <= 0.0) return null;
            if (string.IsNullOrEmpty(MainBarSize1) || string.IsNullOrEmpty(MainBarSpec1)) return null;

            double rsigmay = GetMainBarYieldStress(MainBarSpec1);
            double dd = InsituReinforcedConcreteSection.ExtractBarSizeNumber(MainBarSize1);
            double fb = PileCapFc / 40.0 + 0.9;
            if (rsigmay <= 0.0 || dd <= 0.0 || fb <= 0.0) return null;

            const double pclambda = 0.86;
            const double pcalpha = 1.0;
            const double S = 1.0;
            double Ld = pclambda * pcalpha * S * rsigmay * dd / 10.0 / fb;
            if (Ld <= 0.0) return null;

            return new SteelPilePileHeadJointConstants(pclambda, pcalpha, S, fb, Ld, dd, rsigmay);
        }

        // 鉄筋規格降伏点 [N/mm²]
        private static double GetMainBarYieldStress(string? grade) => grade switch
        {
            "SD295" => 295.0,
            "SD345" => 345.0,
            "SD390" => 390.0,
            "SD490" => 490.0,
            "SD685" => 685.0,
            _ => 0.0,
        };

        /// <summary>
        /// 鋼管杭+鉄筋定着工法 用の杭頭接合部の弾性回転剛性 Kθ と降伏点を算定する。
        ///
        /// 仕様 (杭基礎指針):
        ///   - 仮想 RC 断面 (パイルキャップコンクリート + 定着筋 MainBars1) の M-φ 解析で
        ///     最外縁定着筋が降伏するときの (rMty, rφty) を算定
        ///   - 付着長さ Ld = ptλ × ptα × S × rσy × dd / 10 / fb
        ///       ptλ = 0.86 (付着長さ補正)、ptα = 1.0 (横補強筋拘束あり想定)、S = 1.0 (直線定着)
        ///       fb = Fc/40 + 0.9 (付着割裂強度)
        ///       dd = 定着筋の呼び名数値 (D41 → 41)
        ///   - 杭頭接合部の降伏回転角 ptθy = rφty × Ld
        ///   - 弾性回転剛性 Kθ = rMty / ptθy
        ///
        /// FEM 上の M-θ は降伏後挙動を考慮しない純粋な線形ばね (Kθ) として扱う。
        /// 必要入力が欠落している場合は null。
        /// </summary>
        internal SteelPilePileHeadJointInfo? GetSteelPilePileHeadJointInfo(double axialN_kN)
        {
            // 必須入力チェック
            if (ConcreteOutDia <= 0.0 || PileCapFc <= 0.0) return null;
            if (MainBarNum1 <= 0 || string.IsNullOrEmpty(MainBarSize1) || string.IsNullOrEmpty(MainBarSpec1))
                return null;
            // MainBarDr1 (鉄筋配置直径) は派生量 = ConcreteOutDia - 2·cover。
            // setter 連動で同期している筈だが、既存セーブデータや setter 順序由来で 0 のままの
            // ケースに備えて、ここで just-in-time 再計算する (defensive)。
            double effectiveDr1 = MainBarDr1;
            if (effectiveDr1 <= 0.0)
                effectiveDr1 = ConcreteOutDia - 2.0 * MainBarCenterCover1;
            if (effectiveDr1 <= 0.0) return null;

            // 1. 仮想 RC 断面: パイルキャップコンクリート (Fc, ξ=1.0, D=ConcreteOutDia)
            //    + 定着筋 MainBars1 のみ (杭体主筋 MainBars2 は接合部の付着定着と無関係)
            var insituConcrete = new InsituConcrete(ConcreteOutDia, 1.0, PileCapFc);
            var anchorBars = new MainBars(effectiveDr1, MainBarNum1, MainBarSpec1, MainBarSize1);
            // 杭頭接合部（定着筋）は杭体の鉄筋 1.1F オプションの対象外
            var jointSection = new InsituReinforcedConcreteSection(insituConcrete, anchorBars, applyBodyMaterialOptions: false);

            // 2. 引張側最外縁定着筋が降伏するときの (rMty, rφty)
            //    GetSteelYieldMoment は (M[N·mm], curvature[1/mm]), Ntarget は [N]
            double Ntarget_N = axialN_kN * 1000.0;
            (double rMty_Nmm, double rPhiTy) = jointSection.GetSteelYieldMoment(Ntarget_N);
            if (!double.IsFinite(rMty_Nmm) || !double.IsFinite(rPhiTy) || rMty_Nmm <= 0.0 || rPhiTy <= 0.0)
                return null;

            // 3. 付着長さ Ld [mm] (軸力非依存の係数群)
            var constants = GetSteelPilePileHeadJointConstants();
            if (constants == null) return null;
            double Ld = constants.Ld;

            // 4. 杭頭接合部の降伏回転角 ptθy [rad]
            double ptThetaY = rPhiTy * Ld;
            if (ptThetaY <= 0.0) return null;

            // 5. 弾性回転剛性 Kθ
            //    rMty: N·mm → kN·m に単位変換。Kθ [kN·m/rad] = rMty[kN·m] / ptθy[rad]
            double rMty_kNm = rMty_Nmm * 1e-6;
            double Ktheta = rMty_kNm / ptThetaY;
            return new SteelPilePileHeadJointInfo(Ktheta, ptThetaY, rMty_kNm);
        }

        /// <summary>
        /// 鋼管杭+鉄筋定着工法 用の杭頭接合部の弾性回転剛性 Kθ [kN·m/rad] のみを返す。
        /// 詳細仕様は <see cref="GetSteelPilePileHeadJointInfo"/> を参照。
        /// </summary>
        internal double? GetSteelPilePileHeadJointKtheta(double axialN_kN)
            => GetSteelPilePileHeadJointInfo(axialN_kN)?.Ktheta;

        /// <summary>
        /// 鉄筋定着工法 の諸元 (<see cref="SelectedPileTopSpecification"/>) を入力値・派生値から再構築する。
        /// PileBodyType が「鋼管杭」/「場所打ち鋼管コンクリート杭」/「既製コンクリート杭」のいずれかで動作。
        /// 鋼管杭のみ付着長さ Ld・補正係数群・弾性回転剛性 Kθ(N=0) も併記する。
        /// </summary>
        public void UpdateRebarAnchorageSpecs(string pileBodyType, PileSection? pileSection)
        {
            var specs = new ObservableCollection<Spec>
            {
                // パイルキャップコンクリート
                new Spec("パイルキャップ コンクリート基準強度", "Fc", $"{PileCapFc:N0}", "N/mm2"),
                new Spec("パイルキャップ コンクリート単位体積重量", "γc", $"{PileCapGamma:N1}", "kN/m3"),
                new Spec("パイルキャップ コンクリート縦弾性係数", "Ec", $"{PileCapEc:N0}", "N/mm2"),
                // 杭頭部 RC 径
                new Spec("杭頭部 RC径", "Dc", $"{ConcreteOutDia:N0}", "mm"),
            };

            // 鋼管 (鋼管杭・場所打ち鋼管コンクリート杭の場合のみ)
            bool hasPipe = pileSection != null
                && (pileBodyType == "鋼管杭" || pileBodyType == "場所打ち鋼管コンクリート杭")
                && pileSection.PipeDia > 0.0;
            if (hasPipe)
            {
                specs.Add(new Spec("鋼管外径", "PipeDia", $"{pileSection!.PipeDia:N0}", "mm"));
                specs.Add(new Spec("鋼管厚", "Ts", $"{pileSection.PipeTs:N0}", "mm"));
                string ratio = pileSection.PipeTs > 0
                    ? $"{pileSection.PipeDia / pileSection.PipeTs:N1}" : "N/A";
                specs.Add(new Spec("鋼管径厚比", "D/Ts", ratio, ""));
            }

            // 鉄筋 (最上部杭区間 — MainBars2)
            if (MainBarNum2 > 0 && !string.IsNullOrEmpty(MainBarSize2))
            {
                double ag2 = MainBarNum2 * GetBarArea(MainBarSize2);
                double sy2 = GetMainBarYieldStress(MainBarSpec2);
                specs.Add(new Spec("鉄筋(最上部杭区間) 数-呼び径", "", $"{MainBarNum2}-{MainBarSize2}", ""));
                specs.Add(new Spec("鉄筋(最上部杭区間) 規格", "", MainBarSpec2 ?? "", ""));
                specs.Add(new Spec("鉄筋(最上部杭区間) 断面積", "Ag2", $"{ag2:N0}", "mm2"));
                specs.Add(new Spec("鉄筋(最上部杭区間) 規格降伏点", "σy2", $"{sy2:N0}", "N/mm2"));
                specs.Add(new Spec("鉄筋(最上部杭区間) 重心かぶり厚", "", $"{MainBarCenterCover2:N0}", "mm"));
            }

            // 鉄筋 (定着筋 — MainBars1)
            if (MainBarNum1 > 0 && !string.IsNullOrEmpty(MainBarSize1))
            {
                double ag1 = MainBarNum1 * GetBarArea(MainBarSize1);
                double sy1 = GetMainBarYieldStress(MainBarSpec1);
                specs.Add(new Spec("定着筋 数-呼び径", "", $"{MainBarNum1}-{MainBarSize1}", ""));
                specs.Add(new Spec("定着筋 規格", "", MainBarSpec1 ?? "", ""));
                specs.Add(new Spec("定着筋 断面積", "Ag1", $"{ag1:N0}", "mm2"));
                specs.Add(new Spec("定着筋 規格降伏点", "σy1", $"{sy1:N0}", "N/mm2"));
                specs.Add(new Spec("定着筋 重心かぶり厚", "", $"{MainBarCenterCover1:N0}", "mm"));
            }

            // 鋼管杭 + 鉄筋定着工法 専用: 付着長さ・補正係数・弾性回転剛性 Kθ(N=0)
            if (pileBodyType == "鋼管杭")
            {
                var c = GetSteelPilePileHeadJointConstants();
                if (c != null)
                {
                    specs.Add(new Spec("付着長さの補正係数", "pcλ", $"{c.Pclambda:N2}", ""));
                    specs.Add(new Spec("割裂破壊に対する補正係数", "pcα", $"{c.Pcalpha:N2}", ""));
                    specs.Add(new Spec("必要長さの修正係数", "S", $"{c.S:N2}", ""));
                    specs.Add(new Spec("付着割裂の基準となる強度", "fb", $"{c.Fb:N2}", "N/mm2"));
                    specs.Add(new Spec("定着筋の付着長さ", "Ld", $"{c.Ld:N0}", "mm"));
                }
                var info = GetSteelPilePileHeadJointInfo(0.0);
                if (info != null && double.IsFinite(info.Ktheta) && info.Ktheta > 0.0)
                {
                    specs.Add(new Spec("杭頭接合部 弾性回転剛性 (N=0)", "Kθ", $"{info.Ktheta:N0}", "kN·m/rad"));
                }
            }

            SelectedPileTopSpecification = specs;
        }

        // コンクリート肉厚
        private double _concreteThickness;
        public double ConcreteThickness
        {
            get => _concreteThickness;
            set => SetProperty(ref _concreteThickness, value);
        }

        // 鉄筋径
        public string[] MainBarSizeOption { get; } =
        [
            "D10","D13","D16","D19","D22","D25","D29","D32","D35","D38","D41"
        ];

        // 鉄筋規格
        public string[] MainBarSpecOption { get; } =
        [
            "SD295", "SD345", "SD390", "SD490"
        ];

        internal static double GetBarArea(string barSize)
        {
            return barSize switch
            {
                "D10" => 71.3,
                "D13" => 127.0,
                "D16" => 199.0,
                "D19" => 287.0,
                "D22" => 387.0,
                "D25" => 507.0,
                "D29" => 642.0,
                "D32" => 794.0,
                "D35" => 957.0,
                "D38" => 1140.0,
                "D41" => 1340.0,
                "D51" => 2027.0,
                _ => 0.0
            };
        }

        // 鉄筋径
        private string _mainBarSize1 = "D41";
        public string MainBarSize1
        {
            get => _mainBarSize1;
            set
            {
                if (SetProperty(ref _mainBarSize1, value))
                {
                    MainBarAg1 = MainBarNum1 * GetBarArea(MainBarSize1);
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum1 = 16;
        public int MainBarNum1
        {
            get => _mainBarNum1;
            set
            {
                if (SetProperty(ref _mainBarNum1, value))
                {
                    MainBarAg1 = MainBarNum1 * GetBarArea(MainBarSize1);
                }
            }
        }

        // 鉄筋断面積 mm2
        private double _mainBarAg1;
        public double MainBarAg1
        {
            get => _mainBarAg1;
            set
            {
                if (SetProperty(ref _mainBarAg1, value))
                {
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec1 = "SD390";
        public string MainBarSpec1
        {
            get => _mainBarSpec1;
            set
            {
                if (SetProperty(ref _mainBarSpec1, value))
                {
                }
            }
        }

        // 鉄筋配置直径
        private double _mainBarDr1;
        public double MainBarDr1
        {
            get => _mainBarDr1;
            set
            {
                if (SetProperty(ref _mainBarDr1, value))
                {
                }
            }
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr1;
        public double MainBarFtr1
        {
            get => _mainbarFtr1;
            set => SetProperty(ref _mainbarFtr1, value);
        }

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover1 = 100;
        public double MainBarCenterCover1
        {
            get => _mainbarCenterCover1;
            set
            {
                if (SetProperty(ref _mainbarCenterCover1, value))
                {
                    MainBarDr1 = ConcreteOutDia - 2.0 * MainBarCenterCover1;
                }
            }
        }


        // 鉄筋径
        private string _mainBarSize2 = "D29";
        public string MainBarSize2
        {
            get => _mainBarSize2;
            set
            {
                if (SetProperty(ref _mainBarSize2, value))
                {
                    MainBarAg2 = MainBarNum1 * GetBarArea(MainBarSize2);
                }
            }
        }

        // 鉄筋本数
        private int _mainBarNum2;
        public int MainBarNum2
        {
            get => _mainBarNum2;
            set
            {
                if (SetProperty(ref _mainBarNum2, value))
                {
                    MainBarAg2 = MainBarNum2 * GetBarArea(MainBarSize2);
                }
            }
        }

        // 鉄筋断面積 mm2
        private double _mainBarAg2;
        public double MainBarAg2
        {
            get => _mainBarAg2;
            set
            {
                if (SetProperty(ref _mainBarAg2, value))
                {
                }
            }
        }

        // 鉄筋規格
        private string _mainBarSpec2 = "SD390";
        public string MainBarSpec2
        {
            get => _mainBarSpec2;
            set
            {
                if (SetProperty(ref _mainBarSpec2, value))
                {
                }
            }
        }

        // 鉄筋配置直径
        private double _mainBarDr2;
        public double MainBarDr2
        {
            get => _mainBarDr2;
            set
            {
                if (SetProperty(ref _mainBarDr2, value))
                {
                }
            }
        }

        // 鉄筋規格降伏点
        private double _mainbarFtr2;
        public double MainBarFtr2
        {
            get => _mainbarFtr2;
            set => SetProperty(ref _mainbarFtr2, value);
        }

        // 鉄筋重心かぶり厚 mm
        private double _mainbarCenterCover2;
        public double MainBarCenterCover2
        {
            get => _mainbarCenterCover2;
            set
            {
                if (SetProperty(ref _mainbarCenterCover2, value))
                {
                    MainBarDr2 = ConcreteOutDia - 2.0 * MainBarCenterCover2;
                }
            }
        }

        // 鉄筋縦弾性係数
        private double _mainbarEr = 205_000;
        public double MainBarEr
        {
            get => _mainbarEr;
            set
            {
                if (SetProperty(ref _mainbarEr, value))
                {
                }
            }
        }

        // NMキャッシュは使用しない（パラメータ変更の即時反映のため）
        // [JsonIgnore] 必須: GetNMRaw() で断面計算機 (PileTop の 2 段断面 NM 曲線) を毎回構築する
        // 重い computed プロパティ。System.Text.Json が保存時に getter を呼ぶと杭体ごとに数百 ms かかる。
        // 読取専用なので load 時にも復元できない (廃棄値)。PileSection.cs と同じ対策。
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredServiceNMRaw => GetNMRaw(nameof(UnfactoredServiceNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredDamageNMRaw => GetNMRaw(nameof(UnfactoredDamageNM));
        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredUltimateNMRaw => GetNMRaw(nameof(UnfactoredUltimateNM));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredServiceNM =>
            (GetMultipliedListValues(UnfactoredServiceNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredServiceNMRaw.M, 1e-6));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredDamageNM =>
            (GetMultipliedListValues(UnfactoredDamageNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredDamageNMRaw.M, 1e-6));

        [System.Text.Json.Serialization.JsonIgnore]
        public (List<double> N, List<double> M) UnfactoredUltimateNM =>
            (GetMultipliedListValues(UnfactoredUltimateNMRaw.N, 1e-3),
             GetMultipliedListValues(UnfactoredUltimateNMRaw.M, 1e-6));

        // コンストラクタ
        public PileTop()
        {
            CaptainPile = new CaptainPile(PileCapFc, PileCapEc);
        }

        // List<double>型データの構成要素すべてに係数を乗ずるメソッド
        internal static List<double> GetMultipliedListValues(List<double> originalList, double multiplier)
        {
            if (originalList == null)
                return [];
            List<double> result = [];
            for (int i = 0; i < originalList.Count; i++)
            {
                result.Add(originalList[i] * multiplier);
            }
            return result;
        }

        private (List<double> N, List<double> M) GetNMRaw(string propertyName)
        {
            // 2 段鉄筋 (杭体主筋 MainBars2 + 定着筋 MainBars1) を持つ円形 RC 断面で評価する。
            // 旧実装は判定が "場所打ち鉄筋コンクリート杭" になっていたが、これは 1 段断面で
            // 定着筋入力 UI も持たない (PileTopWindow.xaml も MainBars2 入力を表示しない)。
            // InsituSteelPipeReinforcedConcreteTopSection は本来「場所打ち鋼管コンクリート杭 +
            // 鉄筋定着工法」の杭頭部 (主筋 + 定着筋 2 段) 用のクラスなので判定をそちらへ修正。
            //
            // 鋼管杭 + 鉄筋定着工法 も同じ 2 段断面で扱う (場所打ち鋼管コンクリート杭と同じ思想)。
            // 唯一の差分: ConcreteOutDia (= 下層 RC 径) が公式から自動算定される点。
            // (公式は SteelPipePileLowerRcDiameter() を参照)
            if (PileBodyType == "場所打ち鋼管コンクリート杭" || PileBodyType == "鋼管杭")
            {
                double pileCapGsi = 1.0;
                var insituConcrete = new InsituConcrete(ConcreteOutDia, pileCapGsi, PileCapFc);
                var mainBars1 = new MainBars(MainBarDr1, MainBarNum1, MainBarSpec1, MainBarSize1);
                var mainBars2 = new MainBars(MainBarDr2, MainBarNum2, MainBarSpec2, MainBarSize2);
                // 鋼管杭の定着部は安全限界曲げモーメント低減率 β1 = 0.9 (一律) で評価
                // (場所打ち鋼管コンクリート杭は従来挙動: 低 N で 0.95 / 高 N で 0.80)
                double? beta1Override = PileBodyType == "鋼管杭" ? 0.9 : (double?)null;
                var pileTop = new InsituSteelPipeReinforcedConcreteTopSection(
                    insituConcrete, mainBars1, mainBars2, beta1Override);

                //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
                //UltimateLimitAxialForceThresholds = [.. pileTop.UltimateLimitAxialForceThresholds];


                (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
                {
                    "UnfactoredServiceNM" => pileTop.UnfactoredServiceNM,
                    "UnfactoredDamageNM" => pileTop.UnfactoredDamageNM,
                    "UnfactoredUltimateNM" => pileTop.UnfactoredUltimateNM,
                    "FactoredServiceNM" => pileTop.FactoredServiceNM,
                    "FactoredDamageNM" => pileTop.FactoredDamageNM,
                    "FactoredUltimateNM" => pileTop.FactoredUltimateNM,
                    _ => ([], [], [], [])
                };

                return (n, m);
            }
            else // (PileBodyType == "既製コンクリート杭" || || )
            {
                double pileCapGsi = 1.0;
                var insituConcrete = new InsituConcrete(ConcreteOutDia, pileCapGsi, PileCapFc);
                insituConcrete.ApplyBearingFactor(2.0); // 支圧係数: 許容ひずみ・降伏応力を2倍（Ecは不変）
                var mainBars = new MainBars(MainBarDr1, MainBarNum1, MainBarSpec1, MainBarSize1);
                var pileTop = new PrecastPileTopSection(insituConcrete, mainBars);

                //UltimateLimitAxialForceThresholds = pileSection.UltimateLimitAxialForceThresholds;
                //UltimateLimitAxialForceThresholds = [.. pileTop.UltimateLimitAxialForceThresholds];


                (List<double> n, List<double> m, List<double> _3, List<double> _4) = propertyName switch
                {
                    "UnfactoredServiceNM" => pileTop.UnfactoredServiceNM,
                    "UnfactoredDamageNM" => pileTop.UnfactoredDamageNM,
                    "UnfactoredUltimateNM" => pileTop.UnfactoredUltimateNM,
                    "FactoredServiceNM" => pileTop.FactoredServiceNM,
                    "FactoredDamageNM" => pileTop.FactoredDamageNM,
                    "FactoredUltimateNM" => pileTop.FactoredUltimateNM,
                    _ => ([], [], [], [])
                };

                return (n, m);
            }


        }


        // 浅いコピーを作成するメソッド
        public PileTop ShallowCopy()
        {
            return (PileTop)this.MemberwiseClone();
        }

        // 深いコピーを作成するメソッド
        public PileTop DeepCopy()
        {
            return (PileTop)this.MemberwiseClone();
        }
    }
}

