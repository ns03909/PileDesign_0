using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace PileDesign.Models
{
    /// <summary>
    /// キャプリングパイル工法 (既製コンクリート杭 PHC/PRC/SC、鋼管杭) の杭頭接合部設計クラス。
    /// M-θ 関係は完全バイリニア (原点 → (θy, Mu) → (θu, Mu))。
    /// 引張定着筋の有無 × 軸力方向 の 3 ケースに分岐:
    ///   ① 定着筋なし × 圧縮: Ki = Ke,  Mu = (D/2)·N
    ///   ② 定着筋あり × 圧縮: Ki = Ke,  Mu = (D/2)·N + Mr
    ///   ③ 定着筋あり × 引張: 楕円相互作用で Ki, Mu = Mr·(1 - |N|/Ny)
    /// 単位系: 入力 N [N], 出力 M [N·mm], θ [rad], K [N·mm/rad] (CaptainPile と同じ)
    /// </summary>
    public class CapringPile : BaseModel
    {
        // ───────── 定数 ─────────
        /// <summary>限界回転角 (rad)</summary>
        public const double ThetaU = 0.03;
        /// <summary>鋼材ヤング係数 (N/mm²)</summary>
        public const double EsRebar = 2.05e5;
        /// <summary>鋼管杭の鋼材ヤング係数 (N/mm²)</summary>
        public const double EsSteelPipe = 2.05e5;

        // ───────── 杭種・杭体寸法 ─────────
        private string _pileBodyType = string.Empty;
        public string PileBodyType { get => _pileBodyType; set => SetProperty(ref _pileBodyType, value); }

        private double _d;
        /// <summary>杭径 (mm)</summary>
        public double D
        {
            get => _d;
            set
            {
                if (SetProperty(ref _d, value))
                {
                    OnPropertyChanged(nameof(TensionBarWarning));
                    // 3-D19 + D ≥ 400 の例外ルールで Dc / HoopOutDia が変わるため通知
                    OnPropertyChanged(nameof(EffectiveDc));
                    OnPropertyChanged(nameof(EffectiveHoopOutDia));
                }
            }
        }

        private double _ep;
        /// <summary>杭体ヤング係数 (N/mm²)</summary>
        public double Ep { get => _ep; set => SetProperty(ref _ep, value); }

        private double _ip;
        /// <summary>杭体断面二次モーメント (mm⁴)</summary>
        public double Ip { get => _ip; set => SetProperty(ref _ip, value); }

        private double _hp;
        /// <summary>杭体と PC リングとの重なり長さ (mm)</summary>
        public double Hp { get => _hp; set => SetProperty(ref _hp, value); }

        // ───────── 鋼管杭+キャプリング (コンクリート充填鋼管部) 専用 ─────────
        /// <summary>
        /// true の場合、杭頭をコンクリート充填鋼管断面として扱い、
        /// EpIp = E_steel · I_pipe(中空環) + E_concrete · I_filled(中実円) で合成剛性を算定する。
        /// 鋼管杭 + キャプリングパイル工法を選択した場合に自動 ON。
        /// </summary>
        private bool _isConcreteFilledSteelPipe;
        public bool IsConcreteFilledSteelPipe { get => _isConcreteFilledSteelPipe; set => SetProperty(ref _isConcreteFilledSteelPipe, value); }

        /// <summary>鋼管杭の管厚 (mm) — IsConcreteFilledSteelPipe=true のとき使用</summary>
        private double _steelPipeWallThickness;
        public double SteelPipeWallThickness { get => _steelPipeWallThickness; set => SetProperty(ref _steelPipeWallThickness, value); }

        /// <summary>充填コンクリートのヤング係数 (N/mm²) — 0 なら PileCapEc を使用</summary>
        private double _pipeFillEc;
        public double PipeFillEc { get => _pipeFillEc; set => SetProperty(ref _pipeFillEc, value); }

        /// <summary>合成 EpIp が直接設定されている場合に使用 (N·mm²)。0 なら Ep·Ip を使用。</summary>
        private double _compositeEpIp;
        public double CompositeEpIp { get => _compositeEpIp; set => SetProperty(ref _compositeEpIp, value); }

        // ───────── PC リング ─────────
        private ObservableCollection<CapringPCRing> _pcRings = [];
        public ObservableCollection<CapringPCRing> PCRings { get => _pcRings; set => SetProperty(ref _pcRings, value); }

        private ObservableCollection<string> _pcRingOption = [];
        public ObservableCollection<string> PCRingOption { get => _pcRingOption; set => SetProperty(ref _pcRingOption, value); }

        private CapringPCRing _pcRing = new();
        public CapringPCRing PCRing { get => _pcRing; set => SetProperty(ref _pcRing, value); }

        private string _selectedPCRingName = string.Empty;
        public string SelectedPCRingName { get => _selectedPCRingName; set => SetProperty(ref _selectedPCRingName, value); }

        // ───────── PC リング内コンクリート ─────────
        private double _ec;
        /// <summary>PC リング内コンクリートのヤング係数 (N/mm²)、パイルキャップと同じ</summary>
        public double Ec { get => _ec; set => SetProperty(ref _ec, value); }

        private double _ic;
        /// <summary>PC リング内側コンクリートの断面二次モーメント (mm⁴)</summary>
        public double Ic { get => _ic; set => SetProperty(ref _ic, value); }

        private double _hc;
        /// <summary>杭頭接合面から PC リング上端までの長さ (mm)</summary>
        public double Hc { get => _hc; set => SetProperty(ref _hc, value); }

        // ───────── パイルキャップ仮想円柱 ─────────
        private double _eb;
        /// <summary>パイルキャップコンクリートのヤング係数 (N/mm²)、Eb = Ec</summary>
        public double Eb { get => _eb; set => SetProperty(ref _eb, value); }

        private double _ib;
        /// <summary>仮想円柱の断面二次モーメント (mm⁴)、Ib = Ic</summary>
        public double Ib { get => _ib; set => SetProperty(ref _ib, value); }

        private double _hb;
        /// <summary>仮想円柱の高さ (mm)、Hb = D/2</summary>
        public double Hb { get => _hb; set => SetProperty(ref _hb, value); }

        // ───────── 引張定着筋 ─────────
        private bool _hasTensionBars;
        public bool HasTensionBars
        {
            get => _hasTensionBars;
            set
            {
                if (SetProperty(ref _hasTensionBars, value))
                    OnPropertyChanged(nameof(TensionBarWarning));
            }
        }

        private ObservableCollection<CapringTensionBar> _tensionBars = [];
        public ObservableCollection<CapringTensionBar> TensionBars { get => _tensionBars; set => SetProperty(ref _tensionBars, value); }

        private ObservableCollection<string> _tensionBarOption = [];
        public ObservableCollection<string> TensionBarOption { get => _tensionBarOption; set => SetProperty(ref _tensionBarOption, value); }

        private CapringTensionBar _tensionBar = new();
        public CapringTensionBar TensionBar
        {
            get => _tensionBar;
            set
            {
                if (SetProperty(ref _tensionBar, value))
                {
                    OnPropertyChanged(nameof(TensionBarWarning));
                    OnPropertyChanged(nameof(EffectiveDc));
                    OnPropertyChanged(nameof(EffectiveHoopOutDia));
                }
            }
        }

        /// <summary>
        /// 引張定着筋の有効配置径 Dc (mm)。
        /// 例外ルール: 配筋 3-D19 を杭径 D ≥ 400 mm に適用する場合、配置径は 180 mm
        /// (CSV 表の既定値 110 mm を上書き)。それ以外は CSV 表の値をそのまま返す。
        /// </summary>
        public double EffectiveDc
        {
            get
            {
                if (TensionBar == null) return 0;
                if (TensionBar.BarNum == 3
                    && string.Equals(TensionBar.BarSize, "D19", StringComparison.OrdinalIgnoreCase)
                    && D >= 400.0)
                {
                    return 180.0;
                }
                return TensionBar.Dc;
            }
        }

        /// <summary>
        /// 引張定着筋の有効帯筋外径 (mm)。3-D19 + D ≥ 400 で 220 mm、それ以外は CSV 表の値。
        /// </summary>
        public double EffectiveHoopOutDia
        {
            get
            {
                if (TensionBar == null) return 0;
                if (TensionBar.BarNum == 3
                    && string.Equals(TensionBar.BarSize, "D19", StringComparison.OrdinalIgnoreCase)
                    && D >= 400.0)
                {
                    return 220.0;
                }
                return TensionBar.HoopOutDia;
            }
        }

        /// <summary>
        /// 引張定着筋の適用可能最小杭径が実際の杭径より大きい場合の警告メッセージ。
        /// 警告がない場合は空文字列。XAML 側で TextBlock の Text にバインドして赤字表示する。
        /// </summary>
        public string TensionBarWarning
        {
            get
            {
                if (!HasTensionBars || TensionBar == null || TensionBar.MinPileDia <= 0) return string.Empty;
                if (D <= 0) return string.Empty;
                if (TensionBar.MinPileDia > D)
                {
                    return $"⚠ 引張定着筋の適用最小杭径 {TensionBar.MinPileDia:N0} mm > 杭径 {D:N0} mm — 配筋を見直してください";
                }
                return string.Empty;
            }
        }

        private string _selectedTensionBarName = string.Empty;
        public string SelectedTensionBarName { get => _selectedTensionBarName; set => SetProperty(ref _selectedTensionBarName, value); }

        /// <summary>引張定着筋鋼種 ("SD345" or "SD390")</summary>
        private string _tensionBarGrade = "SD345";
        public string TensionBarGrade
        {
            get => _tensionBarGrade;
            set => SetProperty(ref _tensionBarGrade, value);
        }

        public ObservableCollection<string> TensionBarGradeOption { get; } = ["SD345", "SD390"];

        /// <summary>定着筋鋼材ヤング係数 (N/mm²)</summary>
        public double Es => EsRebar;

        /// <summary>定着筋規格降伏強度 (N/mm²)</summary>
        public double SigmaY => TensionBarGrade switch
        {
            "SD390" => 390.0,
            _ => 345.0,
        };

        /// <summary>1 本あたりの呼び径から断面積 (mm²) を計算</summary>
        public static double GetBarArea(string? barSize) => barSize?.Trim().ToUpperInvariant() switch
        {
            "D10" => 71.33,
            "D13" => 126.7,
            "D16" => 198.6,
            "D19" => 286.5,
            "D22" => 387.1,
            "D25" => 506.7,
            "D29" => 642.4,
            "D32" => 794.2,
            "D35" => 956.6,
            "D38" => 1140.0,
            "D41" => 1340.0,
            _ => 0.0,
        };

        // ───────── 計算結果 ─────────
        private double _ke;
        /// <summary>等価初期回転剛性 Ke (N·mm/rad) — 圧縮時 Ki と等しい</summary>
        public double Ke { get => _ke; set => SetProperty(ref _ke, value); }

        private ObservableCollection<double> _Ns = [];
        public ObservableCollection<double> Ns { get => _Ns; set => SetProperty(ref _Ns, value); }

        private ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> _thetasMs = [];
        public ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> ThetasMs
        {
            get => _thetasMs;
            set => SetProperty(ref _thetasMs, value);
        }

        // ───────── パイルキャップ参照 ─────────
        private double _pileCapEc;
        public double PileCapEc { get => _pileCapEc; set => SetProperty(ref _pileCapEc, value); }

        // ───────── コンストラクタ ─────────
        public CapringPile() { }

        internal CapringPile(double pileCapEc)
        {
            PileCapEc = pileCapEc;
            LoadPCRingOptions();
            LoadTensionBarOptions();
        }

        // ───────── ライブラリ読込 ─────────
        public void LoadPCRingOptions()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(baseDir, "Models", "PileLibrary", "CapringPCRing.csv");
            if (!File.Exists(filePath)) return;
            try
            {
                PCRings = CapringPCRingLoader.LoadFromCsv(filePath);
                PCRingOption = [];
                foreach (var ring in PCRings) PCRingOption.Add(ring.Name ?? "");
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[CapringPile] PCRing CSV 読込失敗: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// PCリング + (引張定着筋ありの場合) 引張定着筋諸元を合成した諸元リストを返す。
        /// PileTop.SelectedPileTopSpecification および計算書の杭頭諸元表で使用する。
        /// </summary>
        public ObservableCollection<PileLibrary.Spec> GetCombinedSpecs()
        {
            var combined = new ObservableCollection<PileLibrary.Spec>();
            if (PCRing != null)
            {
                foreach (var s in PCRing.GetSpecs()) combined.Add(s);
            }
            if (HasTensionBars && TensionBar != null && TensionBar.BarNum > 0)
            {
                combined.Add(new PileLibrary.Spec("引張定着筋", "", TensionBar.Name, ""));
                combined.Add(new PileLibrary.Spec("引張定着筋鋼種", "", TensionBarGrade, ""));
                combined.Add(new PileLibrary.Spec("引張定着筋断面積", "Ag", $"{GetTensionBarArea():N0}", "mm²"));
                // EffectiveDc / EffectiveHoopOutDia は 3-D19 + D ≥ 400 の例外ルールを反映
                bool exceptionApplied = TensionBar.BarNum == 3
                    && string.Equals(TensionBar.BarSize, "D19", StringComparison.OrdinalIgnoreCase)
                    && D >= 400.0;
                string dcNote = exceptionApplied ? " (3-D19, D≥400 例外)" : "";
                combined.Add(new PileLibrary.Spec("引張定着筋配置径", "Dc", $"{EffectiveDc:N0}{dcNote}", "mm"));
                combined.Add(new PileLibrary.Spec("引張定着筋帯筋外径", "", $"{EffectiveHoopOutDia:N0}{dcNote}", "mm"));
                combined.Add(new PileLibrary.Spec("定着長さ (パイルキャップ側、定着版あり)", "", $"{TensionBar.AnchorLengthCapWithPlate:N0}", "mm"));
                combined.Add(new PileLibrary.Spec("定着長さ (パイルキャップ側、定着版なし)", "", $"{TensionBar.AnchorLengthCapWithoutPlate:N0}", "mm"));
                combined.Add(new PileLibrary.Spec("定着長さ (杭体側)", "", $"{TensionBar.AnchorLengthPileSide:N0}", "mm"));
                combined.Add(new PileLibrary.Spec("引張定着筋適用最小杭径", "", $"{TensionBar.MinPileDia:N0}", "mm"));
            }
            return combined;
        }

        public void LoadTensionBarOptions()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(baseDir, "Models", "PileLibrary", "CapringTensionBar.csv");
            if (!File.Exists(filePath)) return;
            try
            {
                TensionBars = CapringTensionBarLoader.LoadFromCsv(filePath);
                TensionBarOption = [];
                foreach (var bar in TensionBars) TensionBarOption.Add(bar.Name);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[CapringPile] TensionBar CSV 読込失敗: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ───────── 諸元更新 ─────────
        /// <summary>
        /// 杭体プロパティ (Ep, Ip, D) と PC リングが設定された後に呼ぶ。
        /// 派生量 (Ec, Ic, Hc, Eb, Ib, Hb, Ke) と M-θ 曲線群を再計算する。
        /// </summary>
        public void Update()
        {
            if (PCRing == null || PCRing.D <= 0) return;

            // PC リングから派生
            double rd1 = PCRing.RD1;          // mm
            double hr = PCRing.Hr;            // mm
            // Hp / Hc は標準値の明記なし → リング高さの折半をデフォルトとする
            if (Hp <= 0) Hp = hr * 0.5;
            if (Hc <= 0) Hc = hr * 0.5;

            // PC リング内コンクリート断面二次モーメント (rd1 直径の充実円)
            Ic = Math.PI * Math.Pow(rd1, 4) / 64.0;

            // パイルキャップ仮想円柱 (Ib = Ic, Eb = Ec, Hb = D/2)
            Ib = Ic;
            if (D <= 0 && PCRing.D > 0) D = PCRing.D;
            Hb = D * 0.5;

            // ヤング係数: PC リング内コンクリート = パイルキャップ
            Ec = PileCapEc;
            Eb = PileCapEc;

            // 杭体 Ep, Ip が未設定 (鋼管杭 etc) ならフォールバック
            if (Ep <= 0)
            {
                Ep = (PileBodyType?.Contains("鋼管") ?? false) ? EsSteelPipe : Math.Max(PileCapEc, 1.0);
            }
            if (Ip <= 0 && D > 0)
            {
                Ip = Math.PI * Math.Pow(D, 4) / 64.0;
            }

            // 鋼管杭 + キャプリング: 杭頭はコンクリート充填鋼管部。
            // EpIp = Es·I_pipe(環) + Ec·I_filled(中実円) で合成剛性を算定。
            if (IsConcreteFilledSteelPipe && D > 0 && SteelPipeWallThickness > 0)
            {
                double dOuter = D;
                double dInner = D - 2.0 * SteelPipeWallThickness;
                if (dInner > 0)
                {
                    double iPipe = Math.PI / 64.0 * (Math.Pow(dOuter, 4) - Math.Pow(dInner, 4));
                    double iFill = Math.PI / 64.0 * Math.Pow(dInner, 4);
                    double eFill = PipeFillEc > 0 ? PipeFillEc : PileCapEc;
                    CompositeEpIp = EsSteelPipe * iPipe + eFill * iFill;
                }
            }

            // 等価初期回転剛性 Ke
            Ke = ComputeKe();

            // M-θ 曲線群を更新
            (Ns, ThetasMs) = GetNMThetaRelationship();
        }

        // ───────── 直列ばね計算 ─────────
        /// <summary>杭体回転剛性 Kp = EpIp / Hp。コンクリート充填鋼管部の場合は合成 EpIp を使用。</summary>
        public double ComputeKp()
        {
            double epIp = CompositeEpIp > 0 ? CompositeEpIp : Ep * Ip;
            return SafeRatio(epIp, Hp);
        }
        public double ComputeKc() => SafeRatio(Ec * Ic, Hc);
        public double ComputeKb() => SafeRatio(Eb * Ib, Hb);

        public double ComputeKe()
        {
            double kp = ComputeKp(), kc = ComputeKc(), kb = ComputeKb();
            if (kp <= 0 || kc <= 0 || kb <= 0) return 0;
            return 1.0 / (1.0 / kp + 1.0 / kc + 1.0 / kb);
        }

        private static double SafeRatio(double num, double den) => den > 0 ? num / den : 0;

        // ───────── 引張定着筋関連量 ─────────
        /// <summary>引張定着筋総断面積 ns·as (mm²)</summary>
        public double GetTensionBarArea()
        {
            if (!HasTensionBars || TensionBar == null) return 0;
            return TensionBar.BarNum * GetBarArea(TensionBar.BarSize);
        }

        /// <summary>引張定着筋総降伏軸力 Ny = ns·as·σy (N)</summary>
        public double GetNy() => GetTensionBarArea() * SigmaY;

        /// <summary>引張軸力時遷移境界軸力 Nty = Ny · D / (D + Dc) (N)。Dc は EffectiveDc を使用 (3-D19 例外ルール対応)</summary>
        public double GetNty()
        {
            if (!HasTensionBars || TensionBar == null) return 0;
            double dc = EffectiveDc;
            if (D + dc <= 0) return 0;
            return GetNy() * D / (D + dc);
        }

        /// <summary>等価円環断面係数 Z (mm³)。Dc は EffectiveDc を使用</summary>
        public double GetZ()
        {
            if (!HasTensionBars || TensionBar == null) return 0;
            double dc = EffectiveDc;
            if (dc <= 0) return 0;
            double a = GetTensionBarArea(); // ns·as
            double inner = dc * dc - 4 * a / Math.PI;
            if (inner < 0) inner = 0;
            return Math.PI / 32.0 * (Math.Pow(dc, 4) - inner * inner) / dc;
        }

        /// <summary>引張軸力時の限界初期回転剛性 Kty = Dc·Z·Es / (2D) (N·mm/rad)。Dc は EffectiveDc を使用</summary>
        public double GetKty()
        {
            if (!HasTensionBars || TensionBar == null || D <= 0) return 0;
            return EffectiveDc * GetZ() * Es / (2.0 * D);
        }

        /// <summary>引張定着筋による最大抵抗モーメント寄与 Mr = ns·as·σy · (7/8) · D/2 (N·mm)</summary>
        public double GetMr() => GetTensionBarArea() * SigmaY * (7.0 / 8.0) * (D / 2.0);

        // ───────── M-θ 計算 ─────────
        /// <summary>
        /// 軸力 N に対する初期回転剛性 Ki と最大抵抗モーメント Mu を返す。
        /// N: 圧縮を正、引張を負 (N)
        /// </summary>
        public (double Ki, double Mu) GetKiMu(double N)
        {
            double ke = Ke > 0 ? Ke : ComputeKe();
            double ki, mu;

            if (!HasTensionBars)
            {
                // ケース① 引張定着筋なし
                if (N >= 0)
                {
                    ki = ke;
                    mu = D / 2.0 * N;
                }
                else
                {
                    // 引張軸力 + 定着筋なし → 想定外、安全側に Mu=0
                    ki = ke;
                    mu = 0;
                }
            }
            else
            {
                if (N >= 0)
                {
                    // ケース② 圧縮
                    ki = ke;
                    mu = D / 2.0 * N + GetMr();
                }
                else
                {
                    // ケース③ 引張
                    double absN = Math.Abs(N);
                    double nty = GetNty();
                    double kty = GetKty();
                    double ny = GetNy();
                    if (nty > 0 && absN <= nty)
                    {
                        // 楕円相互作用: ((|N|-Nty)/Nty)^2 + ((Ke-Ki)/(Ke-Kty))^2 = 1
                        // → Ki = Ke - (Ke - Kty) · sqrt(1 - ((|N|-Nty)/Nty)^2)
                        double x = (absN - nty) / nty;
                        double under = 1.0 - x * x;
                        if (under < 0) under = 0;
                        ki = ke - (ke - kty) * Math.Sqrt(under);
                    }
                    else
                    {
                        ki = kty;
                    }
                    if (ny > 0)
                    {
                        mu = GetMr() * (1.0 - absN / ny);
                        if (mu < 0) mu = 0;
                    }
                    else
                    {
                        mu = 0;
                    }
                }
            }
            if (ki < 0) ki = 0;
            return (ki, mu);
        }

        /// <summary>
        /// 軸力 N に対する完全バイリニア M-θ 曲線を返す (3 点: 原点, (θy, Mu), (θu, Mu))。
        /// FEM 解析時に Update() が未呼び出しの場合 (deserialize 直後等) は自動的に Update() を呼んで派生量を初期化する。
        /// </summary>
        public (ObservableCollection<double>, ObservableCollection<double>) GetMThetaRelationship(double N)
        {
            // 派生量未初期化の場合は安全に Update() を呼ぶ (idempotent)
            if (Ke <= 0 && PCRing != null && PCRing.D > 0)
            {
                Update();
            }

            ObservableCollection<double> thetas = [];
            ObservableCollection<double> Ms = [];

            (double ki, double mu) = GetKiMu(N);
            double thetaY = (ki > 0 && mu > 0) ? mu / ki : 0;
            if (thetaY > ThetaU) thetaY = ThetaU;

            thetas.Add(0.0);
            Ms.Add(0.0);

            thetas.Add(thetaY);
            Ms.Add(mu);

            thetas.Add(ThetaU);
            Ms.Add(mu);

            return (thetas, Ms);
        }

        /// <summary>
        /// N の範囲を NMin〜NMax で 11 点サンプリングし、各 N の M-θ 曲線を返す。
        /// </summary>
        public (ObservableCollection<double>, ObservableCollection<(
            ObservableCollection<double>, ObservableCollection<double>)>) GetNMThetaRelationship()
        {
            ObservableCollection<double> ns = [];
            ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> tms = [];

            int nNum = 10;
            double nMin = GetNMin();
            double nMax = GetNMax();
            if (nMax <= nMin) { nMax = nMin + 1; }

            for (int i = 0; i <= nNum; i++)
            {
                double n = nMin + (nMax - nMin) * i / nNum;
                tms.Add(GetMThetaRelationship(n));
                ns.Add(n);
            }
            return (ns, tms);
        }

        /// <summary>軸力サンプリング下限 (引張側、N)</summary>
        public double GetNMin()
        {
            if (HasTensionBars) return -GetNy();
            return 0;
        }

        /// <summary>軸力サンプリング上限 (圧縮側、N) — パイルキャップコンクリートの軸圧耐力相当</summary>
        public double GetNMax()
        {
            // 簡易: 0.85 · Fc · Ae。Fc は PileCapEc 相当の Fc 推定が必要だが、
            // ここでは広めに取って M-θ サンプリングが破綻しない程度にする。
            // Ae = π/4 · D²
            double ae = Math.PI / 4.0 * D * D;
            // パイルキャップ Fc は PileCapEc から逆算困難なため、固定 30 N/mm² を仮置き。
            // 実運用では PileTopViewModel から PileCapFc を別途設定すること。
            double fc = PileCapFc > 0 ? PileCapFc : 30.0;
            return 0.85 * fc * ae;
        }

        private double _pileCapFc;
        public double PileCapFc { get => _pileCapFc; set => SetProperty(ref _pileCapFc, value); }
    }
}
