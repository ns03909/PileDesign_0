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
    public class CapringPile : BaseModel, System.Text.Json.Serialization.IJsonOnDeserialized
    {
        /// <summary>
        /// 読込の直後にカタログ (PC リング・引張定着筋) を読み直す。選んだもの (<see cref="PCRing"/>・<see cref="TensionBar"/>) は保存した値のまま使う。
        /// 派生値は PileTop の読込の仕上げで Update() が求め直す。
        /// </summary>
        void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
        {
            if (PCRings == null || PCRings.Count == 0) LoadPCRingOptions();
            if (TensionBars == null || TensionBars.Count == 0) LoadTensionBarOptions();
        }
        // ───────── 定数 ─────────
        /// <summary>
        /// 限界回転角 (rad)。
        /// 出典: キャプリングパイル工法 設計施工マニュアル 3.4(1)
        /// 「限界回転角θu は実験結果及び設計許容範囲等を考慮し、0.03rad とする」。
        /// 照査は略算解析法の設計フロー 11) に置かれており、フロー全体が短期
        /// (短期軸力・短期許容せん断力) なので<b>レベル1 の照査</b>にあたる。
        /// </summary>
        public const double ThetaU = 0.03;
        /// <summary>鋼材ヤング係数 (N/mm²)</summary>
        public const double EsRebar = 2.05e5;
        /// <summary>鋼管杭の鋼材ヤング係数 (N/mm²)</summary>
        public const double EsSteelPipe = 2.05e5;
        /// <summary>
        /// 杭体と PC リングとの重なり長さ Hp (mm)。
        /// 出典: 設計マニュアル (2023 年 4 月版) 3.5 設計例「杭体と PC リングの重なり長さ Hp = 0.07m」・
        /// 「Hc = 0.15 − 0.07 = 0.08 m」、Q&amp;A (5-9)「PC リングと杭頭との重なり 70mm … せん断力の伝達に必要な重なり」。
        /// 以前は「標準値の明記なし」としてリング高さの半分 (75 mm、hr = 200 では 100 mm) を使っていた (2026-11-25 修正)。
        /// </summary>
        public const double PileRingOverlap = 70.0;

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

        // Ep・Ip・Hp・Hc・CompositeEpIp は Update() が求める派生値。画面では編集できないので保存しない。
        // 以前は保存していたので、一度入った値 (パイルキャップの Ec・杭径の中実円・リング高さの半分) が
        // ファイルに残り続けた。保存しないので、以前のファイルも開き直すと求め直した値になる (2026-11-25)

        private double _ep;
        /// <summary>杭体ヤング係数 (N/mm²)。杭断面が配られていれば最上段の断面の値 (<see cref="SetPileBodyFromSection"/>)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ep { get => _ep; set => SetProperty(ref _ep, value); }

        private double _ip;
        /// <summary>杭体断面二次モーメント (mm⁴)。杭断面が配られていれば EI / Ep (中空・鋼材の換算を含む)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ip { get => _ip; set => SetProperty(ref _ip, value); }

        private double _hp;
        /// <summary>杭体と PC リングとの重なり長さ (mm)。<see cref="PileRingOverlap"/></summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Hp { get => _hp; set => SetProperty(ref _hp, value); }

        // 杭体 (最上段の断面) のヤング係数 (N/mm²) と曲げ剛性 EI (N·mm²)。SetPileBodyFromSection で配る。保存はしない
        // (解析の入口 PileBodyInput.ApplyTopSectionToPileTop・例題の読込・杭頭部ウィンドウが入れ直す)
        private double _pileBodyEp;
        private double _pileBodyEI;
        [System.Text.Json.Serialization.JsonIgnore]
        internal double PileBodyEp => _pileBodyEp;
        [System.Text.Json.Serialization.JsonIgnore]
        internal double PileBodyEI => _pileBodyEI;

        /// <summary>
        /// 杭体の Ep と EI を最上段の杭断面から受け取り、変わったときだけ組み直す。変わったら true。
        /// マニュアル 3.4 (3-2) の Kp = Ep·Ip/Hp は杭体の値 (設計例 3.5 は PHC 800 の Ep = 39,200 N/mm²、Ip は中空の環で
        /// 1.46×10⁻² m⁴)。以前は杭断面から何も渡っておらず、Ep にパイルキャップの Ec、Ip に杭径の中実円を使っていた (2026-11-25 修正)。
        /// </summary>
        /// <param name="ep">杭体のヤング係数 (N/mm²)。コンクリート杭は杭体コンクリートの Ec、鋼管杭は鋼材の Es</param>
        /// <param name="eiNmm2">杭体の曲げ剛性 (N·mm²)。解析が使う断面の EI (SC 杭の鋼管・PRC の鋼材の換算を含む)</param>
        internal bool SetPileBodyFromSection(double ep, double eiNmm2)
        {
            if (!(ep > 0.0) || !(eiNmm2 > 0.0)) return false;
            if (_pileBodyEp == ep && _pileBodyEI == eiNmm2) return false;
            _pileBodyEp = ep;
            _pileBodyEI = eiNmm2;
            Update();
            return true;
        }

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

        /// <summary>コンクリート充填鋼管部の合成 EpIp (N·mm²)。0 なら Ep·Ip を使用。Update() が求める</summary>
        private double _compositeEpIp;
        [System.Text.Json.Serialization.JsonIgnore]
        public double CompositeEpIp { get => _compositeEpIp; set => SetProperty(ref _compositeEpIp, value); }

        // ───────── PC リング ─────────
        private ObservableCollection<CapringPCRing> _pcRings = [];
        // カタログ (CSV から読む選択肢) なので保存しない。読込後に読み直す (OnDeserialized)。
        // 保存すると杭体ごとにカタログの写しが入り、ファイルの半分ほどを占めていた
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<CapringPCRing> PCRings { get => _pcRings; set => SetProperty(ref _pcRings, value); }

        private ObservableCollection<string> _pcRingOption = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<string> PCRingOption { get => _pcRingOption; set => SetProperty(ref _pcRingOption, value); }

        private CapringPCRing _pcRing = new();
        public CapringPCRing PCRing { get => _pcRing; set => SetProperty(ref _pcRing, value); }

        private string _selectedPCRingName = string.Empty;
        public string SelectedPCRingName { get => _selectedPCRingName; set => SetProperty(ref _selectedPCRingName, value); }

        private bool _ultimateShearConsidersFriction;
        /// <summary>
        /// PC リングの終局せん断耐力に、圧縮軸力を受ける杭頭の接合面摩擦 μN (μ = 0.1) を見込むか。既定は見込まない。
        /// 設計マニュアル (2023 年 4 月版) 2.2(4)「終局せん断耐力は『摩擦無視』を用いる。ただし、終局時圧縮軸力を受ける杭頭では
        /// (2-9) 式を用い、『摩擦考慮上限』まで使用可能」。
        /// </summary>
        public bool UltimateShearConsidersFriction
        {
            get => _ultimateShearConsidersFriction;
            set => SetProperty(ref _ultimateShearConsidersFriction, value);
        }

        /// <summary>選んだ PC リングのせん断耐力 (設計マニュアル 2.2(4) の表)。表に無ければ null。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public PCRingShearCapacity? ShearCapacity => PCRingShearTable.Find(PCRingShearTable.CapringFile, PCRing?.Name);

        // ───────── PC リング内コンクリート ─────────
        private double _ec;
        // [System.Text.Json.Serialization.JsonIgnore] を付けたものは Update() が求める派生値か、PileTop がパイルキャップの Fc・Ec を配るもの。保存しない
        // (読込後は PileTop の読込の仕上げ ApplyPileCapConcrete → SetPileCapConcrete → Update で求め直す)
        /// <summary>PC リング内コンクリートのヤング係数 (N/mm²)、パイルキャップと同じ</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ec { get => _ec; set => SetProperty(ref _ec, value); }

        private double _ic;
        /// <summary>PC リング内側コンクリートの断面二次モーメント (mm⁴)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ic { get => _ic; set => SetProperty(ref _ic, value); }

        private double _hc;
        /// <summary>杭頭接合面から PC リング上端までの長さ (mm)。Hc = hr − Hp</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Hc { get => _hc; set => SetProperty(ref _hc, value); }

        // ───────── パイルキャップ仮想円柱 ─────────
        private double _eb;
        /// <summary>パイルキャップコンクリートのヤング係数 (N/mm²)、Eb = Ec</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Eb { get => _eb; set => SetProperty(ref _eb, value); }

        private double _ib;
        /// <summary>仮想円柱の断面二次モーメント (mm⁴)、Ib = Ic</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ib { get => _ib; set => SetProperty(ref _ib, value); }

        private double _hb;
        /// <summary>仮想円柱の高さ (mm)、Hb = D/2</summary>
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<CapringTensionBar> TensionBars { get => _tensionBars; set => SetProperty(ref _tensionBars, value); }

        private ObservableCollection<string> _tensionBarOption = [];
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
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

        [System.Text.Json.Serialization.JsonIgnore] // 選択肢 (get のみ)。保存しない
        public ObservableCollection<string> TensionBarGradeOption { get; } = ["SD345", "SD390"];

        /// <summary>定着筋鋼材ヤング係数 (N/mm²)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Es => EsRebar;

        /// <summary>定着筋規格降伏強度 (N/mm²)</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double SigmaY => TensionBarGrade switch
        {
            "SD390" => 390.0,
            _ => 345.0,
        };

        /// <summary>1 本あたりの呼び径から断面積 (mm²)。表は <see cref="PileDesign.Constants.RebarAreas"/> の 1 か所 (JIS の公称値)。</summary>
        public static double GetBarArea(string? barSize) => PileDesign.Constants.RebarAreas.Of(barSize?.Trim().ToUpperInvariant());

        // ───────── 計算結果 ─────────
        private double _ke;
        /// <summary>等価初期回転剛性 Ke (N·mm/rad) — 圧縮時 Ki と等しい</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double Ke { get => _ke; set => SetProperty(ref _ke, value); }

        private ObservableCollection<double> _Ns = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<double> Ns { get => _Ns; set => SetProperty(ref _Ns, value); }

        private ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> _thetasMs = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> ThetasMs
        {
            get => _thetasMs;
            set => SetProperty(ref _thetasMs, value);
        }

        // ───────── パイルキャップ参照 ─────────
        private double _pileCapEc;
        [System.Text.Json.Serialization.JsonIgnore]
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
                // 初期回転剛性の元になる量 (計算書で検算できるように)
                double epIp = CompositeEpIp > 0 ? CompositeEpIp : Ep * Ip;
                combined.Add(new PileLibrary.Spec("杭体の曲げ剛性", "Ep·Ip", $"{epIp * 1e-9:N0}", "kN·m²"));
                combined.Add(new PileLibrary.Spec("杭体と PC リングの重なり長さ", "Hp", $"{Hp:N0}", "mm"));
                combined.Add(new PileLibrary.Spec("杭頭接合面から PC リング上端までの長さ", "Hc", $"{Hc:N0}", "mm"));
                combined.Add(new PileLibrary.Spec("初期回転剛性 (圧縮時)", "Ke", $"{Ke * 1e-6:N0}", "kN·m/rad"));
                PCRingShearTable.AddSpecs(combined, ShearCapacity, UltimateShearConsidersFriction);
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

        /// <summary>
        /// パイルキャップの Fc・Ec を反映し、変わったときだけ組み直す。
        /// <see cref="Models.InputData.PileTop.ApplyPileCapConcrete"/> から呼ばれる。
        ///
        /// PileCapEc は Ec / Eb / Ep に入って杭頭ばねの剛性そのものになる。
        /// これまでは杭頭部ウィンドウを<b>開いた瞬間</b>にしか同期しておらず、
        /// Fc を変えて OK を押しても古い剛性のまま解析していた。
        /// </summary>
        internal void SetPileCapConcrete(double pileCapFc, double pileCapEc)
        {
            if (pileCapFc <= 0.0 || pileCapEc <= 0.0) return;
            if (PileCapFc == pileCapFc && PileCapEc == pileCapEc) return;
            PileCapFc = pileCapFc;
            PileCapEc = pileCapEc;
            Update();
        }

        /// <summary>
        /// 杭体が鋼管かどうかと、その管厚を杭断面に合わせ、
        /// 変わったときだけ組み直す。変わったら true。
        ///
        /// 管厚は合成 EI (Es·I_pipe + Ec·I_fill) に効くが、PC リングの自動選定と
        /// 例題読込でしか入っていなかった。画面で編集できる値ではないので、
        /// 杭断面が持つ値をそのまま写す。PC リングと D は利用者の選択なので触らない。
        /// </summary>
        internal bool SetSteelPipeFromSection(bool isConcreteFilled, double wallThickness)
        {
            bool changed = false;
            if (IsConcreteFilledSteelPipe != isConcreteFilled)
            {
                IsConcreteFilledSteelPipe = isConcreteFilled;
                changed = true;
            }
            if (isConcreteFilled && wallThickness > 0.0 && SteelPipeWallThickness != wallThickness)
            {
                SteelPipeWallThickness = wallThickness;
                changed = true;
            }
            if (changed) Update();
            return changed;
        }

        /// <summary>
        /// Undo のスナップショット用。
        ///
        /// 子は <c>PCRing</c> と <c>TensionBar</c> (どちらもカタログの行) だけで、
        /// どちらも差し替えでしか変わらないので <c>MemberwiseClone</c> で足りる。
        /// 表と選択肢も差し替えのみ。<b>中身がその場で書き換わる子を足したら、
        /// ここで写すこと</b> (<c>DeepCopyIsActuallyDeepTests</c> が見張る)。
        /// </summary>
        public CapringPile DeepCopy() => CloneWithoutSubscribers<CapringPile>();

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
            // Hp = 70 mm、Hc = hr − Hp (設計例 3.5)。標準の PC リングは hr = 150・200 なので Hc > 0。
            // 70 mm 以下のリング (カタログには無い) だけは以前の折半で、Hc が 0 以下にならないようにする
            if (hr > PileRingOverlap)
            {
                Hp = PileRingOverlap;
                Hc = hr - PileRingOverlap;
            }
            else
            {
                Hp = hr * 0.5;
                Hc = hr * 0.5;
            }

            // PC リング内側コンクリートの断面二次モーメント (rd1 直径の充実円)。
            // マニュアル 3.4 (3-3)「Ic: PC リング内側コンクリートの断面二次モーメント」、設計例は 800N (rD1 = 820) で Ic = 2.22×10⁻² m⁴。
            // キャプテンパイル工法 (Ic は杭径断面) とは違う
            Ic = Math.PI * Math.Pow(rd1, 4) / 64.0;

            // パイルキャップ仮想円柱 (Ib = Ic, Eb = Ec, Hb = D/2)
            Ib = Ic;
            if (D <= 0 && PCRing.D > 0) D = PCRing.D;
            Hb = D * 0.5;

            // ヤング係数: PC リング内コンクリート = パイルキャップ
            Ec = PileCapEc;
            Eb = PileCapEc;

            // 杭体の Ep・Ip。杭断面が配られていれば Ep = 断面の E、Ip = EI / Ep (中空・鋼材の換算を含む)。
            // 配られる前 (杭断面の無い単体の組み立て) だけ、以前と同じ近似 (パイルキャップの Ec または鋼材の Es、杭径の中実円)
            if (PileBodyEp > 0 && PileBodyEI > 0)
            {
                Ep = PileBodyEp;
                Ip = PileBodyEI / PileBodyEp;
            }
            else
            {
                Ep = (PileBodyType?.Contains("鋼管") ?? false) ? EsSteelPipe : Math.Max(PileCapEc, 1.0);
                Ip = D > 0 ? Math.PI * Math.Pow(D, 4) / 64.0 : 0.0;
            }

            // 鋼管杭 + キャプリング: 杭頭はコンクリート充填鋼管部 (マニュアル Q&A 5-5: 鋼管杭は杭径程度の深さまで中詰コンクリートを充填する)。
            // EpIp = Es·I_pipe(環) + Ec·I_filled(中実円) で合成剛性を算定。マニュアルに Ep·Ip の算定式は無い。
            // 合成でないときは 0 に戻す (以前は鋼管杭から杭種を変えても前の合成 EpIp が残り、Kp に使われていた)
            CompositeEpIp = 0.0;
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
            _updating = true;
            try { (Ns, ThetasMs) = GetNMThetaRelationship(); }
            finally { _updating = false; }
        }

        // Update() が曲線群を求めている間は true (GetMThetaRelationship から Update() を呼び直さないため)
        private bool _updating;

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
            // 派生量未初期化の場合は安全に Update() を呼ぶ (idempotent)。
            // Update() の中 (曲線群の算定) からは呼ばない。パイルキャップの Ec が配られる前などで Ke が 0 のままだと、
            // Update → GetNMThetaRelationship → ここ → Update と再帰してスタックが溢れ、プロセスごと落ちていた
            if (Ke <= 0 && !_updating && PCRing != null && PCRing.D > 0)
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
        [System.Text.Json.Serialization.JsonIgnore]
        public double PileCapFc { get => _pileCapFc; set => SetProperty(ref _pileCapFc, value); }
    }
}
