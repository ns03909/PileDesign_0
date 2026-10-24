using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace PileDesign.Models.InputData
{
    public class GroundInput : BaseModel
    {
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsErrorGroundWaterTableAltitude => GroundWaterTableAltitude > GroundTopAltitude;
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsErrorStressAltitude => StressAltitude > GroundTopAltitude;
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsErrorGroundWaterGLDepth => GroundWaterGLDepth > 0;
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool IsErrorStressGLDepth => StressGLDepth > 0;

        // 地盤参照
        private string _groundRef = null!;   // コンストラクタで既定値が入る
        public string GroundRef
        {
            get => _groundRef;
            set => SetProperty(ref _groundRef, value);
        }

        // 孔口Z (有限値必須。NaN/±∞ なら 0.0 にフォールバックしエラー記録)
        private double _groundTopAltitude;
        public double GroundTopAltitude
        {
            get => _groundTopAltitude;
            set
            {
                if (SetFiniteDouble(ref _groundTopAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterTableAltitude));
                    OnPropertyChanged(nameof(IsErrorStressAltitude));
                }
            }
        }

        // 地下水位Z (有限値必須)
        private double _groundWaterTableAltitude;
        public double GroundWaterTableAltitude
        {
            get => _groundWaterTableAltitude;
            set
            {
                if (SetFiniteDouble(ref _groundWaterTableAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterTableAltitude));
                    OnPropertyChanged(nameof(IsErrorGroundWaterGLDepth));
                }
            }
        }

        /// <summary>
        /// 杭の有効応力 (水平地盤反力の塑性地盤反力 py・内部摩擦角) を、杭ごとの基礎底 (杭頭の高さ) から数えるか (2026-10-24)。
        /// false (既定) は入力した地中応力計算用標高 (<see cref="StressAltitude"/>) から数える。
        ///
        /// <para>杭頭レベルが複数ある (地下の深さが場所で違う) モデルでは、応力計算面が地盤ごとに 1 つだと、
        /// 深い段の杭にも浅い段の基礎底より上の土被りが掛かる (掘削した土を数える)。これを選ぶと、土層-杭セットごとに
        /// その杭頭の高さから下の土被りで数える。地表より上にはしない。地盤の計算 (σ'・FL) は今までどおり入力した値を使う。</para>
        /// </summary>
        private bool _isStressFromPileFoundationBottom;
        public bool IsStressFromPileFoundationBottom
        {
            get => _isStressFromPileFoundationBottom;
            set => SetProperty(ref _isStressFromPileFoundationBottom, value);
        }

        /// <summary>杭頭が <paramref name="pileHeadZ"/> の杭の有効応力を数える面 (<see cref="IsStressFromPileFoundationBottom"/>)。</summary>
        internal double StressTopLevelForPile(double pileHeadZ)
            => IsStressFromPileFoundationBottom ? Math.Min(pileHeadZ, GroundTopAltitude) : StressAltitude;

        /// <summary>
        /// 杭頭が <paramref name="pileHeadZ"/> の杭で、粘性土の塑性水平地盤反力 py の深さ z を測り始める面。
        /// 有効応力を数える面 (<see cref="StressTopLevelForPile"/>) と同じ面で、地表より上にはしない
        /// (有効応力も地表より上の土被りは数えないので、実際に数え始める面はこの高さ)。
        ///
        /// <para>2026-10-24 までは常に地表から測っていた。有効応力を基礎底や地中応力計算用Z から数えても、
        /// 粘性土だけは地表から測るので、砂と粘土で「地表」の考え方が食い違っていた。py の浅い側の式
        /// 2(1 + μz/B)cu は、地表近くの土がくさび状に押し出されて壊れることを表すので、z は掘削した面
        /// (土被りを数え始める面) から測るのが筋 (利用者の判断で、有効応力と揃えた)。</para>
        /// </summary>
        internal double ReactionSurfaceLevelForPile(double pileHeadZ)
            => Math.Min(StressTopLevelForPile(pileHeadZ), GroundTopAltitude);

        // 応力Z (有限値必須)
        private double _stressAltitude;
        public double StressAltitude
        {
            get => _stressAltitude;
            set
            {
                if (SetFiniteDouble(ref _stressAltitude, value))
                {
                    OnPropertyChanged(nameof(IsErrorStressAltitude));
                }
            }
        }

        // 地表深さ = 0
        private double _gLDepth;
        public double GLDepth
        {
            get => _gLDepth;
            set => SetProperty(ref _gLDepth, value);
        }

        // 地下水位深さ
        private double _groundWaterGLDepth;
        public double GroundWaterGLDepth
        {
            get => _groundWaterGLDepth;
            set
            {
                if (SetProperty(ref _groundWaterGLDepth, value))
                {
                    OnPropertyChanged(nameof(IsErrorGroundWaterGLDepth));
                }
            }
        }

        // 応力計算用深さ
        private double _stressGLDepth;
        public double StressGLDepth
        {
            get => _stressGLDepth;
            set
            {
                if (SetProperty(ref _stressGLDepth, value))
                {
                    OnPropertyChanged(nameof(IsErrorStressGLDepth));
                }
            }
        }

        // 地表面における設計用レベル1地震水平加速度 (m/s²、非負の有限値)
        private double _groundAcceleration1;
        public double GroundAcceleration1
        {
            get => _groundAcceleration1;
            set => SetFiniteClampedDouble(ref _groundAcceleration1, value, min: 0.0, max: 100.0, fallback: 1.5);
        }

        // 地表面における設計用レベル2地震水平加速度
        private double _groundAcceleration2;
        public double GroundAcceleration2
        {
            get => _groundAcceleration2;
            set => SetFiniteClampedDouble(ref _groundAcceleration2, value, min: 0.0, max: 100.0, fallback: 3.5);
        }

        // 浅層地盤のタイプ
        private string _shallowSoilType = null!;   // コンストラクタで既定値が入る
        public string ShallowSoilType
        {
            get => _shallowSoilType;
            set => SetProperty(ref _shallowSoilType, value);
        }

        private string? _responseSpectrumWarning;

        /// <summary>
        /// 応答スペクトル法で計算できずに略算法 (a2(b2)) で代用したときの理由。代用していなければ null。
        /// 地盤ウィンドウの算定法の横に出す。保存しない (開き直すと計算し直して決まる)。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string? ResponseSpectrumWarning
        {
            get => _responseSpectrumWarning;
            set
            {
                if (SetProperty(ref _responseSpectrumWarning, value))
                    OnPropertyChanged(nameof(HasResponseSpectrumWarning));
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool HasResponseSpectrumWarning => !string.IsNullOrEmpty(ResponseSpectrumWarning);

        // 算定法
        private string _calculationMethod = null!;   // コンストラクタで既定値が入る
        public string CalculationMethod
        {
            get => _calculationMethod;
            set => SetProperty(ref _calculationMethod, value);
        }

        // 工学的基盤の単位体積重量
        private double _bedrockDensity;
        public double BedrockDensity
        {
            get => _bedrockDensity;
            set => SetProperty(ref _bedrockDensity, value);
        }

        // 工学的基盤のせん断波速度
        private double _bedrockShearWaveVelocity;
        public double BedrockShearWaveVelocity
        {
            get => _bedrockShearWaveVelocity;
            set => SetProperty(ref _bedrockShearWaveVelocity, value);
        }

        // 土層データ（デシリアライズ時はコンストラクタのデフォルト値を上書きする）
        private ObservableCollection<GroundLayerInput> _groundLayers = null!;   // コンストラクタで入る
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<GroundLayerInput> GroundLayers
        {
            get => _groundLayers;
            set => SetProperty(ref _groundLayers, value);
        }

        // 土質点データ（デシリアライズ時はコンストラクタのデフォルト値を上書きする）
        private ObservableCollection<GroundMassDataInput> _groundMassesData = null!;   // コンストラクタで入る
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<GroundMassDataInput> GroundMassesData
        {
            get => _groundMassesData;
            set => SetProperty(ref _groundMassesData, value);
        }

        // 任意地盤変位プロファイル
        private CustomDisplacementProfile _customDisplacementProfile = new();
        public CustomDisplacementProfile CustomDisplacementProfile
        {
            get => _customDisplacementProfile;
            set => SetProperty(ref _customDisplacementProfile, value);
        }

        /// <summary>
        /// 地盤変位を「考慮しない」モード。true の場合、水平解析にて地盤変位を全層 0 として扱い、
        /// 杭周地盤の強制変位入力を無視する。
        /// グラフは何も描画されない。GroundMassesData の DmaxUStar 等の値は保持される (再計算不要)。
        /// </summary>
        private bool _isGroundDisplacementIgnored;
        public bool IsGroundDisplacementIgnored
        {
            get => _isGroundDisplacementIgnored;
            set => SetProperty(ref _isGroundDisplacementIgnored, value);
        }

        /// <summary>
        /// 標高 <paramref name="z"/> [m] を GL 基準深さ [m] (地表 0・地中で負) にする。
        /// 地盤変位のグラフは土層も自動計算値もこの向きで描くので、任意入力の点もここを通す
        /// (計算書だけ「地表 − Z」と逆向きにしていた。GroundDisplacementDisplayTests)。
        /// </summary>
        internal double ToGLDepth(double z) => z - GroundTopAltitude;

        /// <summary>
        /// 標高を GL 深さから求め直す (標高 = GL 深さ + 孔口Z)。食い違っていた項目の名前を返す (無ければ空)。
        ///
        /// <para>地下水位・応力計算用の面・土層の下端・土質点は、標高と GL 深さの両方を持つ。杭の土層-杭セット
        /// (有効応力・層の対応) は<b>標高</b>を、地盤の計算 (σ'・FL など) は<b>GL 深さ</b>を読む。画面の入力は両方を
        /// 同期させるが、そこを通らずに作った入力 (例題を JSON から直接読んだもの・同期の無い時期のファイル) では
        /// 食い違うことがあり、杭のばねだけが別の地下水位で計算された (2026-10-23 の調査。テストが読む例題は
        /// 地下水位の標高が地表 0 のまま、GL 深さは −3 m だった)。正は GL 深さ (孔口Z を変えたときの
        /// <see cref="InputModel.ShiftGroundZByDelta"/> と同じ不変関係)。</para>
        /// </summary>
        internal List<string> SyncAltitudesFromGLDepths()
        {
            const double tol = 1e-9;
            var fixes = new List<string>();
            double top = GroundTopAltitude;
            bool Differs(double altitude, double glDepth) => !(Math.Abs(altitude - (glDepth + top)) <= tol);

            if (Differs(GroundWaterTableAltitude, GroundWaterGLDepth))
            {
                fixes.Add($"地下水位 (標高 {GroundWaterTableAltitude:F3} → {GroundWaterGLDepth + top:F3})");
                GroundWaterTableAltitude = GroundWaterGLDepth + top;
            }
            if (Differs(StressAltitude, StressGLDepth))
            {
                fixes.Add($"応力計算用の面 (標高 {StressAltitude:F3} → {StressGLDepth + top:F3})");
                StressAltitude = StressGLDepth + top;
            }
            int layers = 0, thicknesses = 0;
            double previousBottomGL = 0.0;   // 1 層目の上端は孔口 (GL 0)
            foreach (var layer in GroundLayers ?? [])
            {
                if (layer == null) continue;
                if (Differs(layer.BottomAltitude, layer.BottomGLDepth))
                {
                    layer.BottomAltitude = layer.BottomGLDepth + top;
                    layers++;
                }
                // 層厚も GL 深さ (上の層の下端との差) から決める。有効応力は「下端 + 層厚」を上端として数えるので、
                // 食い違うと土被りを二重に数えたり抜かしたりした (2026-10-24 のレビュー)。逆順 (負) の層厚は直さず、検査で止める
                double thickness = previousBottomGL - layer.BottomGLDepth;
                if (thickness > 0 && !(Math.Abs(layer.LayerThickness - thickness) <= tol))
                {
                    layer.LayerThickness = thickness;
                    thicknesses++;
                }
                previousBottomGL = layer.BottomGLDepth;
            }
            if (layers > 0) fixes.Add($"土層の下端 {layers} 層");
            if (thicknesses > 0) fixes.Add($"土層の層厚 {thicknesses} 層");
            int masses = 0;
            foreach (var mass in GroundMassesData ?? [])
            {
                if (mass == null || !Differs(mass.AltitudeDepth, mass.GLDepth)) continue;
                mass.AltitudeDepth = mass.GLDepth + top;
                masses++;
            }
            if (masses > 0) fixes.Add($"土質点 {masses} 点");
            return fixes;
        }

        /// <summary>
        /// 各土質点の変位 (U_i) が表す位置の標高 [m]。土質点 i は層 i の上端、つまり地表から層厚 H を
        /// 積んだ位置 −(H0 + … + H_{i−1}) にある。質量・モード形の漸化式 (U0 = 地表) と同じ置き方で、
        /// 最後の土質点が基盤面 (U* = 0)。土質点の GLDepth / AltitudeDepth は N 値などの土質データの
        /// 深度で、変位の位置ではない。解析・画面・計算書はすべてここを通す
        /// (以前は解析が AltitudeDepth、グラフが GLDepth＋間隔×(1/0.5/0) と別々だった。2026-09-12)。
        /// </summary>
        internal double[] MassTopAltitudes()
        {
            var masses = GroundMassesData;
            var tops = new double[masses?.Count ?? 0];
            double depth = 0.0;
            for (int i = 0; i < tops.Length; i++)
            {
                tops[i] = GroundTopAltitude + depth;
                // 層厚が未入力なら間隔 (RecalculateH と同じ既定)。0 で積むと全土質点が地表に重なる
                depth -= masses![i].H ?? masses[i].Spacing;
            }
            return tops;
        }

        /// <summary>
        /// 標高 <paramref name="zTop"/>〜<paramref name="zBtm"/> の杭要素に掛ける、液状化による
        /// 水平地盤反力の低減率 βL (基礎指針'19 表4.5.1)。kh0 と py の両方に掛ける。1 = 低減なし。
        ///
        /// <para>土質点の占める深さの範囲は層の上端 (地表から層厚 H を積んだ位置) で決まる
        /// (<see cref="MassTopAltitudes"/> と同じ置き方)。要素の中央が入る土質点の βL を使う。</para>
        ///
        /// <para><b>βL の初期値 [0, 0] は「未判定」で「完全液状化」ではない。</b> 判定を一度も
        /// 行っていない地盤で 0 を低減率として使うと、地盤ばねが全部消える。
        /// <c>IsLiquefactionLayer</c> が立っている土質点だけを対象とし、対象外 (βL が null)・
        /// 土質点の範囲外・地表より上は 1 を返す (<c>SteelPipeBuckling.IsLiquefied</c> と同じ切り分け)。</para>
        /// </summary>
        /// <param name="levelIndex">0 = レベル1、1 = レベル2。負 (VL) なら 1 を返す。</param>
        internal double LiquefactionReductionAt(double zTop, double zBtm, int levelIndex)
        {
            if (levelIndex < 0) return 1.0;
            var masses = GroundMassesData;
            if (masses == null || masses.Count == 0) return 1.0;

            double zMid = 0.5 * (zTop + zBtm);
            double[] tops = MassTopAltitudes();
            for (int i = 0; i < masses.Count; i++)
            {
                if (zMid > tops[i] + 1e-9) return 1.0;   // 最初の土質点 (地表) より上
                double bottom = tops[i] - (masses[i].H ?? masses[i].Spacing);
                if (zMid < bottom - 1e-9) continue;      // この土質点より下 → 次を見る

                var mass = masses[i];
                if (!mass.IsLiquefactionLayer) return 1.0;
                var betas = mass.BetaL;
                if (betas == null || levelIndex >= betas.Count) return 1.0;
                double? beta = betas[levelIndex];
                if (!beta.HasValue || !double.IsFinite(beta.Value)) return 1.0;
                return Math.Clamp(beta.Value, 0.0, 1.0);
            }
            return 1.0;                                   // 最下の土質点より深い
        }

        /// <summary>
        /// 土質点 <paramref name="massIndex"/> の位置 (層の上端) の地盤変位 [mm]。解析
        /// (<c>ZDataItem.SetSoilDisplacement</c>) と同じく地盤変位のモードに従う: 考慮しない → 0、
        /// 任意入力 → その位置の標高で補間、それ以外 → 自動計算値。
        /// 画面ごとに自前で書くと、モードを見ない画面が解析と違う値を見せる (杭体ウィンドウで実際に起きた)。
        /// </summary>
        /// <param name="levelIndex">0 = レベル1、1 = レベル2</param>
        internal double GetMassDisplacement(int massIndex, int levelIndex, bool isLiquefaction)
        {
            if (IsGroundDisplacementIgnored) return 0.0;

            var custom = CustomDisplacementProfile;
            if (custom != null && custom.IsEnabled)
            {
                var profile = levelIndex == 0
                    ? (isLiquefaction ? custom.Level1Liq : custom.Level1NonLiq)
                    : (isLiquefaction ? custom.Level2Liq : custom.Level2NonLiq);
                return custom.Interpolate(profile, MassTopAltitudes()[massIndex]);
            }

            return AutoMassDisplacement(GroundMassesData[massIndex], levelIndex, isLiquefaction);
        }

        /// <summary>自動計算 (基礎指針'19 4.5) の地盤変位 [mm]。値が無ければ 0。</summary>
        internal static double AutoMassDisplacement(GroundMassDataInput mass, int levelIndex, bool isLiquefaction)
        {
            var values = isLiquefaction ? mass.DmaxUStarSigmaGammaCyH : mass.DmaxUStar;
            return values != null && values.Count > levelIndex ? values[levelIndex] : 0.0;
        }

        // 内部摩擦角とN値の関係　p30
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public List<string> SelectedInternalFrictionAngleCalcumationMethodOption { get; } =
        [
            "大崎式",
            "畑中式",
        ];

        // 内部摩擦角とN値の関係　p30
        private string _selectedInternalFrictionAngleCalcumationMethod = "大崎式";
        public string SelectedInternalFrictionAngleCalcumationMethod
        {
            get => _selectedInternalFrictionAngleCalcumationMethod;
            set => SetProperty(ref _selectedInternalFrictionAngleCalcumationMethod, value);
        }

        //微小変形時の地盤の固有周期
        private double _naturalPeriod;
        public double NaturalPeriod
        {
            get => _naturalPeriod;
            set => SetProperty(ref _naturalPeriod, value);
        }

        //地震時の地盤の固有周期
        private ObservableCollection<double> _naturalPeriods = [0.0, 0.0];
        public ObservableCollection<double> NaturalPeriods
        {
            get => _naturalPeriods;
            set => SetProperty(ref _naturalPeriods, value);
        }

        // 液状化指標 PL 値 (岩崎ら 1982): PL = Σ (1-FL)·(10-0.5z)·H, FLが1超のときF=0, 深さは0~20m対象
        private ObservableCollection<double> _pl = [0.0, 0.0];
        public ObservableCollection<double> PL
        {
            get => _pl;
            set => SetProperty(ref _pl, value);
        }

        // 応答スペクトル法: 地盤増幅率 Gs1 (加速度一定領域、レベル1/2)
        private ObservableCollection<double> _gs1Levels = [0.0, 0.0];
        public ObservableCollection<double> Gs1Levels
        {
            get => _gs1Levels;
            set => SetProperty(ref _gs1Levels, value);
        }

        // 応答スペクトル法: 地盤増幅率 Gs2 (速度一定領域、レベル1/2)
        private ObservableCollection<double> _gs2Levels = [0.0, 0.0];
        public ObservableCollection<double> Gs2Levels
        {
            get => _gs2Levels;
            set => SetProperty(ref _gs2Levels, value);
        }

        // 応答スペクトル法: 収束後の表層/基盤インピーダンス比 e (レベル1/2)
        private ObservableCollection<double> _impedanceLevels = [0.0, 0.0];
        public ObservableCollection<double> ImpedanceLevels
        {
            get => _impedanceLevels;
            set => SetProperty(ref _impedanceLevels, value);
        }

        // 応答スペクトル法: 2次卓越周期 T2 (レベル1/2) — Gs(T) 式 (8) で必要
        private ObservableCollection<double> _t2Levels = [0.0, 0.0];
        public ObservableCollection<double> T2Levels
        {
            get => _t2Levels;
            set => SetProperty(ref _t2Levels, value);
        }

        // 応答スペクトル法: モード等価減衰 ξe (レベル1/2)
        private ObservableCollection<double> _xiELevels = [0.0, 0.0];
        public ObservableCollection<double> XiELevels
        {
            get => _xiELevels;
            set => SetProperty(ref _xiELevels, value);
        }

        // 応答スペクトル法: 1次モード参加係数 β (レベル1/2)
        private ObservableCollection<double> _betaLevels = [0.0, 0.0];
        public ObservableCollection<double> BetaLevels
        {
            get => _betaLevels;
            set => SetProperty(ref _betaLevels, value);
        }

        // コンストラクタ
        public GroundInput()
        {
            GroundRef = "(GR1)";
            GroundTopAltitude = 0.0;
            GroundWaterTableAltitude = 0.0;
            StressAltitude = 0.0;
            GLDepth = 0.0;
            GroundWaterGLDepth = 0.0;
            StressGLDepth = 0.0;

            GroundAcceleration1 = 1.5;
            GroundAcceleration2 = 3.5;

            ShallowSoilType = "砂質土";
            CalculationMethod = "a1(b1)";

            BedrockDensity = 19;
            BedrockShearWaveVelocity = 400;

            GroundLayers = [new GroundLayerInput() { No = 1, }];
            GroundMassesData = [new GroundMassDataInput() { No = 1, }];
        }

        public double GetFrictionAngle(double nValue, double sigmaZPrime)
        {
            if (SelectedInternalFrictionAngleCalcumationMethod == "大崎式")
            {
                return Math.Min(Math.Sqrt(20 * nValue) + 15, 40); // 大崎式
            }
            else if (SelectedInternalFrictionAngleCalcumationMethod == "畑中式")
            {
                double n1 = nValue / Math.Sqrt(sigmaZPrime / 100);
                return Math.Min(Math.Sqrt(20 * n1) + 20, 40); // N1が3.5以下の場合を含む
            }
            else
            {
                return Math.Min(Math.Sqrt(20 * nValue) + 15, 40); // 大崎式
            }
        }

        // 浅いコピーを作成するメソッド
        public GroundInput ShallowCopy()
        {
            return CloneWithoutSubscribers<GroundInput>();
        }

        // 深いコピーを作成するメソッド
        /// <summary>
        /// 地盤の画面で編集を始めたときの地盤番号 (1 から)。画面を開いてから足した地盤は 0。保存しない。
        /// 杭配置・根入部の地盤番号は、画面を OK で閉じたときにこれを手掛かりに付け直す
        /// (<c>GroundLayerViewModel.RenumberGroundReferences</c>)。複製はメンバーの写しから始めるので、元に戻したあとも残る。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        internal int NoAtEditStart { get; set; }

        public GroundInput DeepCopy()
        {
            var copy = CloneWithoutSubscribers<GroundInput>();
            copy.GroundLayers = new ObservableCollection<GroundLayerInput>(this.GroundLayers.Select(layer => layer.DeepCopy()));
            copy.GroundMassesData = new ObservableCollection<GroundMassDataInput>(this.GroundMassesData.Select(mass => mass.DeepCopy()));
            copy.CustomDisplacementProfile = this.CustomDisplacementProfile?.DeepCopy() ?? new CustomDisplacementProfile();
            copy.Gs1Levels = new ObservableCollection<double>(this.Gs1Levels);
            copy.Gs2Levels = new ObservableCollection<double>(this.Gs2Levels);
            copy.ImpedanceLevels = new ObservableCollection<double>(this.ImpedanceLevels);
            copy.T2Levels = new ObservableCollection<double>(this.T2Levels);
            copy.XiELevels = new ObservableCollection<double>(this.XiELevels);
            copy.BetaLevels = new ObservableCollection<double>(this.BetaLevels);
            return copy;
        }

        //  解析のための確認メソッド
        public bool ValidateForAnalysis(out string warningMessage)
        {
            bool hasWarning = false;
            warningMessage = "\n";
            {
                for (int i = 0; i < GroundLayers.Count; i++)
                {
                    var groundLayer = GroundLayers[i];

                    if (groundLayer.Es == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 変形係数が0です。\n";
                    }
                    // 土層の N 値は砂質土・礫質土の周面摩擦力度 τ2 などに使う。0 のままだとその層の周面抵抗が 0 になる。
                    // 以前は土質点の N 値だけを見ていて、土層の N 値が 0 でも何も知らせなかった
                    if (groundLayer.NValue == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: N値が0です。\n";
                    }
                    if (groundLayer.GranularityClass == "粘性土" && groundLayer.Cohesive == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 粘性土で粘着力が0です。\n";
                    }
                    if (i >= 1 && groundLayer.BottomGLDepth >= GroundLayers[i - 1].BottomGLDepth)
                    {
                        hasWarning = true;
                        warningMessage += $"- 層番号 {groundLayer.No}: 下端深度が直上層の下端深度より高いです。\n";
                    }

                }
                for (int i = 0; i < GroundMassesData.Count; i++)
                {
                    var groundMassData = GroundMassesData[i];
                    if (groundMassData.NValue == 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: N値が0です。\n";
                    }
                    if (i >= 1 && groundMassData.GLDepth >= GroundMassesData[i - 1].GLDepth)
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: 深度が直上層の深度より高いです。\n";
                    }
                    // 以下は以前どこも検査しておらず、数値でない値・0 が黙って地盤変位・液状化の判定に入っていた (2026-10-09)
                    if (groundMassData.H is double h && !(double.IsFinite(h) && h > 0))
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: 受け持つ層厚 H が正の数ではありません ({h:0.###})。下端深度を確認してください。\n";
                    }
                    if (!(double.IsFinite(groundMassData.VS0) && groundMassData.VS0 > 0))
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: せん断波速度 VS0 が正の数ではありません。略算法の地盤変位が 0 になります。\n";
                    }
                    // σz' は地盤ウィンドウが求める派生値で、求める前は 0。負 (水中重量を全重量の欄に入れたなど) と数値でないものだけを止める
                    if (groundMassData.GLDepth < -1e-9 && !(groundMassData.SigmaZPrime >= 0))
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: 有効上載圧 σz' が負か数値ではありません ({groundMassData.SigmaZPrime:0.##})。"
                            + "地下水位と単位体積重量 (水中重量ではなく全重量) を確認してください。液状化の判定が正しく求まらず、この深さが砂質土・礫質土なら水平地盤ばねが 0 になります (塑性水平地盤反力 py が 0 以下になるため)。\n";
                    }
                    if (groundMassData.NValue < 0)
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: N値が負です。\n";
                    }
                    if (!(groundMassData.Fc >= 0 && groundMassData.Fc <= 100))
                    {
                        hasWarning = true;
                        warningMessage += $"- 土質点番号 {groundMassData.No}: 細粒分含有率 Fc が 0〜100% ではありません ({groundMassData.Fc:0.##})。\n";
                    }
                    // 最下層より深い土質点は、どの土層からも密度などを写せない。密度 0 のまま地盤の
                    // 計算に入ると、せん断ばね剛性が 0 になってその下の地盤変位が求まらない
                    // (地盤変位はその位置から下を 0 として扱う)。土層データの入力漏れなので警告する。
                    if (GroundLayers.Count > 0)
                    {
                        double deepestLayerBottom = GroundLayers.Min(layer => layer.BottomGLDepth);
                        if (groundMassData.GLDepth < deepestLayerBottom - 1e-9)
                        {
                            hasWarning = true;
                            warningMessage += $"- 土質点番号 {groundMassData.No}: 最下層 (GL{deepestLayerBottom:0.00}m) より深いです。"
                                + "土層を追加してください。ここから下の地盤変位は 0 として扱います。\n";
                        }
                    }
                }
                return !hasWarning;
            }
        }
    }
}

