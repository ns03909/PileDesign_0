using MathNet.Numerics.LinearAlgebra;
using PileDesign.Constants;
using PileDesign.FEM;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    // SoilPileクラス
    // Smart-MAGNUM 工法の支持力算定は SoilPile.SmartMagnum.cs に分離している。
    public partial class SoilPile : BaseModel, IJsonOnDeserializing, IJsonOnDeserialized
    {
        public int No { get; set; }
        //private readonly InputModel inputModel = InputModel.Instance;
        public int GroundNo { get; set; }
        public int PileBodyNo { get; set; }


        // 地盤
        private GroundInput _groundInput;
        public GroundInput GroundInput
        {
            get => _groundInput;
            set => SetProperty(ref _groundInput, value);
        }

        // 杭体
        private PileBodyInput _pileBodyInput;
        public PileBodyInput PileBodyInput
        {
            get => _pileBodyInput;
            set => SetProperty(ref _pileBodyInput, value);
        }

        // 杭頭Z
        private double _z;
        public double Z
        {
            get => _z;
            set
            {
                if (SetProperty(ref _z, value))
                {
                    UpdatePileBottomAltitude();
                }
            }
        }

        // 杭先端標高
        private double _pileBottomAltitude;
        public double PileBottomAltitude
        {
            get => _pileBottomAltitude;
            set => SetProperty(ref _pileBottomAltitude, value);
        }

        // 子要素 (ZDataItems) への自動同期 (SetSoilDisplacement) を抑止するフラグ。
        // JSON デシリアライズ中は GroundInput や ZDataItem.GroundDisp* が個別に loaded されるため、
        // ここで再計算すると loaded 済みの GroundDisp* を破壊する可能性がある。
        private bool _suppressChildSync = false;

        // System.Text.Json 用コールバック (本プロジェクトの主デシリアライザ)
        void IJsonOnDeserializing.OnDeserializing() => _suppressChildSync = true;
        void IJsonOnDeserialized.OnDeserialized() => _suppressChildSync = false;

        // Newtonsoft.Json 用コールバック (副デシリアライザ経由でロードされる場合に備えて)
        [OnDeserializing]
        internal void OnDeserializingHandler(StreamingContext _) => _suppressChildSync = true;

        [OnDeserialized]
        internal void OnDeserializedHandler(StreamingContext _) => _suppressChildSync = false;

        // 節点 (ZDataItems.Z: Z)
        private ObservableCollection<PileZDataItem> _zDataItems;
        public ObservableCollection<PileZDataItem> ZDataItems
        {
            get => _zDataItems;
            set
            {
                if (SetProperty(ref _zDataItems, value))
                {
                    if (_suppressChildSync) return;
                    foreach (var zDataItem in _zDataItems)
                    {
                        zDataItem.SetSoilDisplacement(/*InputModel.Instance*/);
                    }
                }
            }
        }

        // 土層(総数：節点数-1 = 要素数分)
        //public ObservableCollection<GroundLayerInput> GroundLayers { get; set; }
        private ObservableCollection<GroundLayerInput> _groundLayers;
        public ObservableCollection<GroundLayerInput> GroundLayers
        {
            get => _groundLayers;
            set => SetProperty(ref _groundLayers, value);
        }

        // 杭断面(総数：節点数-1 = 要素数分)
        private ObservableCollection<PileBodySegment> _pileBodySegments;
        public ObservableCollection<PileBodySegment> PileBodySegments
        {
            get => _pileBodySegments;
            set
            {
                if (SetProperty(ref _pileBodySegments, value))
                {
                    _pileBodySegments.CollectionChanged += (s, e) => UpdatePileBottomAltitude();
                    UpdatePileBottomAltitude();
                }
            }
        }

        // 水平地盤反力(総数：節点数-1 = 要素数分)
        private ObservableCollection<HorizontalSoilReactionItem> _horizontalSoilReactions = [];
        public ObservableCollection<HorizontalSoilReactionItem> HorizontalSoilReactions
        {
            get => _horizontalSoilReactions;
            set => SetProperty(ref _horizontalSoilReactions, value);
        }

        // 基準水平地盤反力係数 kh0 の土層ごと手入力オーバーライド（土層名 → 値）。
        // 空 = 全土層で自動計算。SetHorizontalSoilReaction() で該当土層の要素に適用される。
        private ObservableCollection<Kh0LayerOverride> _kh0LayerOverrides = [];
        public ObservableCollection<Kh0LayerOverride> Kh0LayerOverrides
        {
            get => _kh0LayerOverrides;
            set => SetProperty(ref _kh0LayerOverrides, value);
        }

        /// <summary>指定土層名の kh0 手入力オーバーライドを返す（未設定なら null）。</summary>
        public double? GetKh0Override(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || Kh0LayerOverrides == null) return null;
            foreach (var o in Kh0LayerOverrides)
                if (o.LayerName == layerName) return o.Kh0;
            return null;
        }

        /// <summary>指定土層名に kh0 手入力オーバーライドを設定し、水平地盤反力を再構築する。</summary>
        public void SetKh0Override(string layerName, double value)
        {
            if (string.IsNullOrEmpty(layerName)) return;
            Kh0LayerOverrides ??= [];
            var existing = Kh0LayerOverrides.FirstOrDefault(o => o.LayerName == layerName);
            if (existing != null) existing.Kh0 = value;
            else Kh0LayerOverrides.Add(new Kh0LayerOverride { LayerName = layerName, Kh0 = value });
            RebuildHorizontalSoilReactions();
        }

        /// <summary>指定土層名の kh0 手入力オーバーライドを解除し（自動計算に戻す）、再構築する。</summary>
        public void ClearKh0Override(string layerName)
        {
            if (Kh0LayerOverrides == null) return;
            var existing = Kh0LayerOverrides.FirstOrDefault(o => o.LayerName == layerName);
            if (existing != null) Kh0LayerOverrides.Remove(existing);
            RebuildHorizontalSoilReactions();
        }

        /// <summary>全土層の kh0 手入力オーバーライドを解除し（自動計算に戻す）、再構築する。</summary>
        public void ClearAllKh0Overrides()
        {
            if (Kh0LayerOverrides == null || Kh0LayerOverrides.Count == 0) return;
            Kh0LayerOverrides.Clear();
            RebuildHorizontalSoilReactions();
        }

        /// <summary>水平地盤反力（HorizontalSoilReactions）を再構築する公開メソッド。</summary>
        public void RebuildHorizontalSoilReactions() => SetHorizontalSoilReaction();

        // 杭周鉛直力
        public ObservableCollection<PileCircumVertical> PileCircumVerticals { get; set; }


        // 群杭荷重円径
        private double _groupPileLoadDia;
        public double GroupPileLoadDia
        {
            get => _groupPileLoadDia;
            set => SetProperty(ref _groupPileLoadDia, value);
        }

        // 杭先端N値
        public double PileToeNValue
        {
            //get
            //{
            //    var groundMassesData = InputModel.Instance.GroundsInput[GroundNo - 1].GroundMassesData;
            //    var relevantMasses = groundMassesData
            //        .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude && 
            //        data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
            //        .ToList();

            //    if (relevantMasses.Count == 0)
            //    {
            //        return 0; // 適切なデフォルト値を返す
            //    }

            //    return Math.Min(relevantMasses.Average(data => Math.Min(data.NValue, 100)), 60);
            //}
            get
            {
                // Smart-MAGNUM は Nu（上方2m）と Nl（下方 LL+Den+Don）の重み付き平均で、
                // 単純平均ではないため別経路で算定する。
                if (IsSmartMagnum) return SmartMagnumPileToeNValue;
                if (IsHybridKneading) return HybridToeNValue;

                var groundMassesData = GroundInput.GroundMassesData;
                var relevantMasses = groundMassesData
                    .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude &&
                                   data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
                    .ToList();

                if (relevantMasses.Count == 0) return 0;
                return Math.Min(relevantMasses.Average(data => Math.Min(data.NValue, 100)), 60);
            }
        }

        // 杭先端平均N値用N値
        public string NValuesForAverage
        {
            //get
            //{
            //    var groundMassesData = InputModel.Instance.GroundsInput[GroundNo - 1].GroundMassesData;
            //    var relevantMasses = groundMassesData
            //        .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude && 
            //        data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
            //        .Select(data => Math.Min(data.NValue, 100))
            //        .ToArray();

            //    if (relevantMasses.Length == 0)
            //    {
            //        return string.Empty; // 適切なデフォルト値を返す
            //    }

            //    return string.Join(", ", relevantMasses);
            //}
            get
            {
                var groundMassesData = GroundInput.GroundMassesData;
                var relevantMasses = groundMassesData
                    .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude &&
                                   data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
                    .Select(data => Math.Min(data.NValue, 100))
                    .ToArray();

                if (relevantMasses.Length == 0) return string.Empty;
                return string.Join(", ", relevantMasses);
            }
        }


        // 杭先端N値平均範囲上端
        // Smart-MAGNUM は Nu の範囲（杭先端から上方 2m）。一般式は杭先端 +D。
        public double PileToeNValueAverageRangeUpperAltitude
        {
            get => IsSmartMagnum ? PileBottomAltitude + SmartMagnumNuRangeAboveToe
                 : IsHybridKneading ? HybridToeEvaluationAltitude + HybridBulbTopAboveEvaluation
                 : PileBottomAltitude + D;
        }

        // 杭先端N値平均範囲下端
        // Smart-MAGNUM は Nl の範囲（杭先端から下方 LL+Den+Don）。一般式は杭先端 -D。
        public double PileToeNValueAverageRangeLowerAltitude
        {
            get => IsSmartMagnum ? PileBottomAltitude - (SmartMagnumLL + SmartMagnumDen + SmartMagnumDon)
                 : IsHybridKneading ? HybridToeEvaluationAltitude - HybridD1
                 : PileBottomAltitude - D;
        }

        // 杭先端粘着力
        private double _pileToeCohesive;
        public double PileToeCohesive
        {
            get => _pileToeCohesive;
            set => SetProperty(ref _pileToeCohesive, value);
        }

        // 杭先端閉塞率
        private double _pileToeEta;
        public double PileToeEta
        {
            get => _pileToeEta;
            set => SetProperty(ref _pileToeEta, value);
        }

        // 極限先端支持力度
        private double _qpu;
        public double Qpu
        {
            get => _qpu;
            set => SetProperty(ref _qpu, value);
        }

        // 沈下検討用極限先端支持力度
        private double _settleQpu;
        public double SettleQpu
        {
            get => _settleQpu;
            set => SetProperty(ref _settleQpu, value);
        }

        // 沈下検討用先端面積 m2 (沈下検討用杭先端径 Dp[mm] 基準)。
        // 支持力検討の Ap は構造先端径 D[m] 基準で別物。Dp は mm 保持のため /1000 で m 換算する。
        public double SettleAp => Math.PI * Math.Pow(Dp / 1000.0, 2) * 0.25;

        // 沈下検討用極限先端支持力（沈下計算で使用）
        public double SettleRpu => SettleQpu * SettleAp;

        // 杭工法
        private string _pileConstructionType;
        public string PileConstructionType
        {
            get => _pileConstructionType;
            set => SetProperty(ref _pileConstructionType, value);
        }

        // 杭先端土質
        private string _pileToeGranularityClass;
        public string PileToeGranularityClass
        {
            get => _pileToeGranularityClass;
            set => SetProperty(ref _pileToeGranularityClass, value);
        }

        // 沈下プリセットを自動選択済みか（SettlementWindow 初回表示時に一度だけセット）。
        // 一度 true になったら手動編集を尊重し、再適用しない。
        public bool IsSettlementPresetInitialized { get; set; } = false;

        // 杭先端径 m
        private double _d;
        public double D
        {
            get => _d;
            set => SetProperty(ref _d, value);
        }

        // 沈下検討用杭先端径 mm (PileBodyInput.SettlePileToeDia と同期。FEM 側は /1000 で m 換算)
        private double _dp;
        public double Dp
        {
            get => _dp;
            set => SetProperty(ref _dp, value);
        }

        // 杭先端面積 m2（構造先端径 D = 杭先端径/根固め部径 基準）
        public double Ap => Math.PI * Math.Pow(D, 2) * 0.25;

        // 先端支持力の算定に使う面積 m2。
        // Smart-MAGNUM は「根固め部に位置する節杭の節部有効断面積 Ap = π·Don²/4」であり、
        // 姿図・N値範囲に使う根固め部径 Den 基準の Ap とは別物。
        public double ApBearing =>
            IsSmartMagnum ? SmartMagnumAp :
            IsHybridKneading ? HybridAp : Ap;

        // 極限先端支持力 kN
        public double Rpu => Qpu * ApBearing;

        // 極限周面抵抗力 kN
        private double _rfu;
        public double Rfu
        {
            get => _rfu;
            set => SetProperty(ref _rfu, value);
        }

        // 極限鉛直支持力 kN
        public double Ru => Rpu + Rfu;

        // 使用限界支持力 kN
        /// <summary>
        /// 指定した杭頭荷重（絶対値, kN）に対応する杭頭沈下量（mm, 絶対値）を
        /// LoadDisplacements から線形補間で返す。未解析・曲線範囲外は 0 を返す。
        /// </summary>
        private double InterpolateD0sForLoadMagnitude(double targetForceMagnitude)
        {
            if (LoadDisplacements == null || LoadDisplacements.Count == 0 || targetForceMagnitude <= 0)
                return 0;

            var pts = LoadDisplacements
                .Where(ld => !double.IsNaN(ld.PileTopLoad) && !double.IsNaN(ld.D0s))
                .Select(ld => (load: Math.Abs(ld.PileTopLoad), disp: Math.Abs(ld.D0s)))
                .OrderBy(p => p.load)
                .ToList();
            if (pts.Count < 2) return 0;

            if (targetForceMagnitude >= pts[^1].load) return pts[^1].disp;
            if (targetForceMagnitude <= pts[0].load) return pts[0].disp;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                if (targetForceMagnitude >= pts[i].load && targetForceMagnitude <= pts[i + 1].load)
                {
                    double span = pts[i + 1].load - pts[i].load;
                    if (span <= 1e-12) return pts[i].disp;
                    double t = (targetForceMagnitude - pts[i].load) / span;
                    return pts[i].disp * (1 - t) + pts[i + 1].disp * t;
                }
            }
            return 0;
        }

        // [JsonIgnore]: LoadDisplacements を補間する computed プロパティ。保存対象ではない
        // (ElementDivision.SoilPiles 経由でシリアライズされるため明示的に除外)
        /// <summary>使用限界支持力 R_SLS 時の杭頭沈下量 [mm]</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double SettlementAtR_SLS => InterpolateD0sForLoadMagnitude(R_SLS);

        /// <summary>損傷限界支持力 R_DLS 時の杭頭沈下量 [mm]</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double SettlementAtR_DLS => InterpolateD0sForLoadMagnitude(R_DLS);

        /// <summary>終局限界支持力 R_ULS 時の杭頭沈下量 [mm]</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double SettlementAtR_ULS => InterpolateD0sForLoadMagnitude(R_ULS);

        public double R_SLS => (1.0 / 3.0) * Ru;

        // 損傷限界支持力 kN
        public double R_DLS => (1.0 / 1.5) * Ru;

        // 終局限界支持力 kN
        public double R_ULS => Ru;

        // 使用限界引抜力 kN
        //public double Rt_SLS => (1.0 / 3.0) * Rtu;
        private double _rt_SLS;
        public double Rt_SLS
        {
            get => _rt_SLS;
            set => SetProperty(ref _rt_SLS, value);
        }

        // 損傷限界引抜力 kN
        //public double Rt_DLS => Rty;
        private double _rt_DLS;
        public double Rt_DLS
        {
            get => _rt_DLS;
            set => SetProperty(ref _rt_DLS, value);
        }

        // 終局限界引抜力 kN
        private double _rt_ULS;
        public double Rt_ULS
        {
            get => _rt_ULS;
            set => SetProperty(ref _rt_ULS, value);
        }

        // 最大引き抜き抵抗力 kN
        private double _rtu;
        public double Rtu
        {
            get => _rtu;
            set => SetProperty(ref _rtu, value);
        }

        // 残留引抜き抵抗力 kN
        private double _rtr;
        public double Rtr
        {
            get => _rtr;
            set => SetProperty(ref _rtr, value);
        }

        // 降伏引抜き抵抗力 kN
        private double _rty;
        public double Rty
        {
            get => _rty;
            set => SetProperty(ref _rty, value);
        }


        /// <summary>
        /// 単杭沈下解析の結果 (常時の荷重-沈下曲線)。
        ///
        /// <b>置き場所はここのままにしてある。</b>基礎梁考慮沈下の杭頭ばねと、
        /// 水平解析の杭先端 P-S ばねが<b>次の解析の入力として</b>これを読むため、
        /// 結果側へ動かすと数値の出る経路に手が入る。
        ///
        /// ただし<b>入力の一部としては保存しない</b> ([JsonIgnore])。以前は現在の入力と
        /// 解析時のスナップショットの両方に同じ曲線が書き出され、保存・Undo のたびに
        /// 曲線ぶんの直列化が走っていた。保存はファイルの
        /// <c>SinglePileSettlementResult</c> の節が 1 回だけ受け持つ。
        /// 旧ファイルは <see cref="LegacyLoadDisplacements"/> が受け取る。
        ///
        /// 計算プロパティ (SettlementAtR_SLS/DLS/ULS 等) の更新通知を飛ばすため SetProperty 経由。
        /// </summary>
        private ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> _loadDisplacements = [];
        [JsonIgnore]
        public ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacements
        {
            get => _loadDisplacements;
            set
            {
                if (SetProperty(ref _loadDisplacements, value))
                {
                    OnPropertyChanged(nameof(SettlementAtR_SLS));
                    OnPropertyChanged(nameof(SettlementAtR_DLS));
                    OnPropertyChanged(nameof(SettlementAtR_ULS));
                }
            }
        }

        /// <summary>単杭沈下解析の結果 (極限の荷重-沈下曲線)。<see cref="LoadDisplacements"/> と同じ扱い。</summary>
        [JsonIgnore]
        public ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacementsLimit { get; set; } = [];

        /// <summary>
        /// 旧ファイルの "LoadDisplacements" / "LoadDisplacementsLimit" を受け取るためだけのプロパティ。
        /// 読込時に本体へ移して空にするので、保存し直したファイルでは空配列になる。
        /// <b>ここを読んでよいのは読込の移行だけ。</b>
        /// </summary>
        [JsonPropertyName("LoadDisplacements")]
        public List<VerticalLoadTransferMethod.LoadDisplacement> LegacyLoadDisplacements { get; set; } = [];

        /// <summary>旧ファイルの極限側。<see cref="LegacyLoadDisplacements"/> と同じ。</summary>
        [JsonPropertyName("LoadDisplacementsLimit")]
        public List<VerticalLoadTransferMethod.LoadDisplacement> LegacyLoadDisplacementsLimit { get; set; } = [];

        /// <summary>
        /// 旧ファイルから受け取った曲線を本体へ移し、受け取り口を空にする。
        /// 移すものが無ければ何もしない (新しいファイルは結果の節から入る)。
        /// </summary>
        internal void MigrateLegacyLoadDisplacements()
        {
            if (LegacyLoadDisplacements.Count > 0)
            {
                if (LoadDisplacements.Count == 0)
                    LoadDisplacements = [.. LegacyLoadDisplacements];
                LegacyLoadDisplacements = [];
            }
            if (LegacyLoadDisplacementsLimit.Count > 0)
            {
                if (LoadDisplacementsLimit.Count == 0)
                    LoadDisplacementsLimit = [.. LegacyLoadDisplacementsLimit];
                LegacyLoadDisplacementsLimit = [];
            }
        }

        // 全節点変位・反力（各荷重ステップ）— DeepCopy/JSON保存から除外
        [JsonIgnore]
        public List<Vector<double>> NodeDisplacements { get; set; }

        [JsonIgnore]
        public List<Vector<double>> NodeReactions { get; set; }

        /// <summary>
        /// 指定した杭頭荷重に対する全節点変位ベクトルを線形補間で返す（単位: m）
        /// </summary>
        public Vector<double> GetFullDisplacementForLoad(double pileTopForce)
        {
            return InterpolateNodeVector(NodeDisplacements, pileTopForce);
        }

        /// <summary>
        /// 指定した杭頭荷重に対する全節点反力ベクトルを線形補間で返す（単位: kN）
        /// </summary>
        public Vector<double> GetFullReactionForLoad(double pileTopForce)
        {
            return InterpolateNodeVector(NodeReactions, pileTopForce);
        }

        private Vector<double> InterpolateNodeVector(List<Vector<double>> vectors, double pileTopForce)
        {
            if (vectors == null || LoadDisplacements == null ||
                vectors.Count != LoadDisplacements.Count || vectors.Count < 2)
                return null;

            var sorted = LoadDisplacements
                .Select((ld, i) => (ld, i))
                .OrderBy(x => x.ld.PileTopLoad)
                .ToList();

            if (pileTopForce <= sorted[0].ld.PileTopLoad)
                return vectors[sorted[0].i].Clone();
            if (pileTopForce >= sorted[^1].ld.PileTopLoad)
                return vectors[sorted[^1].i].Clone();

            for (int k = 0; k < sorted.Count - 1; k++)
            {
                var lower = sorted[k];
                var upper = sorted[k + 1];
                if (pileTopForce >= lower.ld.PileTopLoad && pileTopForce <= upper.ld.PileTopLoad)
                {
                    double ratio = (pileTopForce - lower.ld.PileTopLoad) / (upper.ld.PileTopLoad - lower.ld.PileTopLoad);
                    return vectors[lower.i] * (1 - ratio) + vectors[upper.i] * ratio;
                }
            }
            return null;
        }


        const double epsilon = 1e-5; // 許容誤差（必要に応じて調整）

        // コンストラクタ
        public SoilPile()
        {
            GroundLayers = [];
            PileBodySegments = [];
            PileCircumVerticals = [];
            HorizontalSoilReactions = [];
            LoadDisplacements = [];
            LoadDisplacementsLimit = [];
        }

        // 追加: 初期化メソッド
        public void Initialize(
            int no,
            int groundNo,
            GroundInput groundInput,
            int pileBodyNo,
            PileBodyInput pileBodyInput,
            double z,
            ObservableCollection<PileZDataItem> zDataItems)
        {
            No = no;
            GroundNo = groundNo;
            PileBodyNo = pileBodyNo;
            GroundInput = groundInput;
            PileBodyInput = pileBodyInput;
            Z = z;
            ZDataItems = zDataItems;

            foreach (var zDataItem in zDataItems)
            {
                zDataItem.GroundInput = groundInput;
                zDataItem.SetSoilDisplacement();
            }

            PileBodySegments.CollectionChanged += (s, e) => UpdatePileBottomAltitude();
            UpdateProperties();

            //InputModel inputModel = InputModel.Instance;
            //Dp = inputModel.PileBodies[PileBodyNo - 1].SettlePileToeDia;
            Dp = PileBodyInput.SettlePileToeDia;
        }

        // メソッド //

        public void NotifyAllPropertiesChanged()
        {
            // バックフィールドを持つプロパティ
            OnPropertyChanged(nameof(No));
            OnPropertyChanged(nameof(GroundInput));
            OnPropertyChanged(nameof(PileBodyInput));
            OnPropertyChanged(nameof(Z));
            OnPropertyChanged(nameof(PileBottomAltitude));
            OnPropertyChanged(nameof(ZDataItems));
            OnPropertyChanged(nameof(GroundLayers));
            OnPropertyChanged(nameof(PileBodySegments));
            OnPropertyChanged(nameof(HorizontalSoilReactions));
            OnPropertyChanged(nameof(PileCircumVerticals));
            OnPropertyChanged(nameof(GroupPileLoadDia));
            OnPropertyChanged(nameof(PileToeCohesive));
            OnPropertyChanged(nameof(PileToeEta));
            OnPropertyChanged(nameof(Qpu));
            OnPropertyChanged(nameof(PileConstructionType));
            OnPropertyChanged(nameof(PileToeGranularityClass));
            OnPropertyChanged(nameof(D));
            OnPropertyChanged(nameof(Dp));
            OnPropertyChanged(nameof(Rfu));
            OnPropertyChanged(nameof(Rt_SLS));
            OnPropertyChanged(nameof(Rt_DLS));
            OnPropertyChanged(nameof(Rt_ULS));
            OnPropertyChanged(nameof(Rtu));
            OnPropertyChanged(nameof(Rtr));
            OnPropertyChanged(nameof(Rty));
            OnPropertyChanged(nameof(LoadDisplacements));
            OnPropertyChanged(nameof(LoadDisplacementsLimit));

            // 計算プロパティ（getのみ）
            OnPropertyChanged(nameof(PileToeNValue));
            OnPropertyChanged(nameof(NValuesForAverage));
            OnPropertyChanged(nameof(PileToeNValueAverageRangeUpperAltitude));
            OnPropertyChanged(nameof(PileToeNValueAverageRangeLowerAltitude));
            OnPropertyChanged(nameof(Ap));
            OnPropertyChanged(nameof(Rpu));
            OnPropertyChanged(nameof(Ru));
            OnPropertyChanged(nameof(R_SLS));
            OnPropertyChanged(nameof(R_DLS));
            OnPropertyChanged(nameof(R_ULS));
            OnPropertyChanged(nameof(SettlementAtR_SLS));
            OnPropertyChanged(nameof(SettlementAtR_DLS));
            OnPropertyChanged(nameof(SettlementAtR_ULS));
        }

        // PileBottomAltitude更新メソッド
        private void UpdatePileBottomAltitude()
        {
            if (PileBodySegments != null && PileBodySegments.Count > 0)
            {
                PileBottomAltitude = Z - PileBodySegments[^1].SegmentDepth; // 杭頭標高 - 最下段区間深さ
            }
        }

        // ZDataItemsの更新後に実行するメソッド
        public void OnZDataItemsChanged(
            ObservableCollection<PileZDataItem> zDataItems
            )
        {
            ZDataItems = zDataItems;
            UpdateProperties();
        }

        // プロパティ更新メソッド
        public void UpdateProperties()
        {

            foreach (var zDataItem in ZDataItems)
            {
                zDataItem.SetSoilDisplacement();
            }

            Dp = PileBodyInput.SettlePileToeDia;

            double groundTopAltitude = GroundInput.GroundTopAltitude;
            var groundLayers = GroundInput.GroundLayers;
            var pileBodySegments = PileBodyInput.PileBodySegments;

            MatchGroundLayersAndPileBodySegments(/*groundTopAltitude,*/ groundLayers, pileBodySegments);
            CreatePileCircumVerticals();
            UpdatePileProperties();
            UpdatePileToeProperties();
            UpdatePileCircumVerticalProperties();
            CalculateResistances();
            SetHorizontalSoilReaction();
        }

        // GroundLayers, PileBodySegmentsの更新
        private void MatchGroundLayersAndPileBodySegments(
            //double groundTopAltitude,
            ObservableCollection<GroundLayerInput> groundLayers,
            ObservableCollection<PileBodySegment> pileBodySegments
            )
        {
            // Nullチェックと初期化
            if (groundLayers == null || pileBodySegments == null) return;

            if (GroundLayers == null) return;
            if (PileBodySegments == null) return;

            GroundLayers.Clear();
            PileBodySegments.Clear();

            for (int i = 1; i < ZDataItems.Count; i++)
            {
                GroundLayerInput matchedGroundLayer/* = null*/;
                for (int j = 0; j < groundLayers.Count; j++)
                {
                    if (/*Z +*/ ZDataItems[i].Z >= groundLayers[j].BottomAltitude)
                    {
                        matchedGroundLayer = groundLayers[j];
                        GroundLayers.Add(matchedGroundLayer);
                        break;
                    }
                }

                for (int k = 0; k < pileBodySegments.Count; k++)
                {
                    if (ZDataItems[i].Z >= Z - pileBodySegments[k].SegmentDepth ||
                        k == pileBodySegments.Count - 1)
                    {
                        var copiedSegment = pileBodySegments[k].DeepCopy();
                        copiedSegment.SegmentLength = ZDataItems[i - 1].Z - ZDataItems[i].Z;
                        copiedSegment.SegmentDepth = Z - ZDataItems[i].Z; // 杭頭からの相対深さ
                        PileBodySegments.Add(copiedSegment); // SoilPileのPileBodySegments

                        break;
                    }
                }
            }
        }

        // PileCircumVerticals
        private void CreatePileCircumVerticals()
        {
            if (PileCircumVerticals == null) return;
            PileCircumVerticals.Clear();

            for (int j = 0; j < GroundLayers.Count; j++)
            {
                if (j + 1 < ZDataItems.Count && j < PileBodySegments.Count)
                {
                    PileCircumVerticals.Add(new PileCircumVertical()
                    {
                        Top = ZDataItems[j].Z,
                        Bottom = ZDataItems[j + 1].Z,
                        PileBodySegment = PileBodySegments[j],
                        GroundLayer = GroundLayers[j],
                        IsPositiveCircumResistance = GroundLayers[j].IsPositiveCircumResistance,
                        IsNegativeCircumResistance = GroundLayers[j].IsNegativeCircumResistance,
                    });
                }
            }
        }

        // PileProperties
        private void UpdatePileProperties()
        {
            var selectedPileBody = PileBodyInput;
            PileConstructionType = selectedPileBody.PileConstructionType;
            PileToeEta = selectedPileBody.TipNonPermability;

            // Hybrid ニーディングの根固め部径 D3 は設計拡径比 e と節部径 D1 から決まる導出値。
            // 姿図・3D・地盤柱・CAD 出力はいずれも PileToeDia を見るため、ここで書き戻して
            // 下流の消費側に分岐を持ち込まないようにする。
            if (IsHybridKneading && HybridD3 > 0)
                selectedPileBody.PileToeDia = HybridD3 * 1000.0;

            D = selectedPileBody.PileToeDia / 1000.0; // mm -> m 
        }

        // 杭先端プロパティの更新メソッド
        private void UpdatePileToeProperties()
        {
            if (GroundLayers == null || GroundLayers.Count == 0) return;

            var lastGroundLayer = GroundLayers[^1];
            PileToeGranularityClass = lastGroundLayer.GranularityClass;
            PileToeCohesive = lastGroundLayer.Cohesive;

            Qpu = PileConstructionType switch
            {
                // Smart-MAGNUM: Rp = α·N·Ap（Ap は節部径 Don 基準）なので、
                // 支持力度としては α·N を持たせる。面積側は ApBearing が Don 基準に切り替わる。
                PileConstructionTypeNames.SmartMagnum =>
                    SmartMagnumAlphaValue * SmartMagnumPileToeNValue,
                // Hybrid ニーディング: Rp = α·N·Ap（Ap は節部径 D1 基準）。
                // N が 5 未満のとき α = 0 とする規定は HybridQpu 側で処理している。
                PileConstructionTypeNames.HybridKneading => HybridQpu,
                PileConstructionTypeNames.Insitu => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(120 * PileToeNValue, 7500),
                    "礫質土" => Math.Min(120 * PileToeNValue, 7500),
                    "粘性土" => Math.Min(6 * PileToeCohesive, 7500),
                    _ => Qpu
                },
                _ when PileConstructionTypeNames.IsPreboring(PileConstructionType) => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeNValue, 9000),
                    "礫質土" => Math.Min(150 * PileToeNValue, 9000),
                    "粘性土" => Math.Min(150 * PileToeNValue, 9000),
                    _ => Qpu
                },
                PileConstructionTypeNames.Chubori => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeNValue, 9000),
                    "礫質土" => Math.Min(150 * PileToeNValue, 9000),
                    "粘性土" => Math.Min(6 * PileToeCohesive, 9000),
                    _ => Qpu
                },
                PileConstructionTypeNames.Rotary => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    "礫質土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    "粘性土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    _ => Qpu
                },
                PileConstructionTypeNames.Driven => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    "礫質土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    "粘性土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    _ => Qpu
                },
                _ => Qpu
            };

            if (IsSmartMagnum || IsHybridKneading)
            {
                // 沈下曲線の極限先端支持力には工法の極限先端支持力そのものを使う。
                // 曲線の変位スケール 0.1·Dp には球根が地盤を支える実態に合わせて根固め部径 Den を
                // 使うため、Dp 基準の面積 SettleAp で逆算し SettleRpu == Rpu を厳密に成立させる。
                // （α·N をそのまま Den 面積に掛けると球根径基準で過大評価になる）
                Dp = PileBodyInput.PileToeDia;
                PileBodyInput.SettlePileToeDia = Dp; // 沈下ウィンドウの表示と一致させる
                SettleQpu = SettleAp > 0 ? Rpu / SettleAp : 0;
            }
            else
            {
                // 沈下検討用極限先端支持力度をデフォルトで極限先端支持力度と同じ値に設定
                SettleQpu = Qpu;
            }
        }

        // 杭周面抵抗プロパティの更新メソッド
        private void UpdatePileCircumVerticalProperties()
        {
            if (PileCircumVerticals == null) return;

            if (IsSmartMagnum || IsHybridKneading)
            {
                // τ2 / τT は工法別評定式で決まる。τ1 / S1 / S2 はカタログに規定が無いため
                // 土質別の既存値をそのまま使う（下のループで設定される）。
                if (IsSmartMagnum)
                {
                    ApplySmartMagnumCircumProperties();
                    ApplySmartMagnumCircumExclusion();
                }
                else
                {
                    ApplyHybridCircumProperties();
                    ApplyHybridCircumExclusion();
                }

                foreach (var pcv in PileCircumVerticals)
                {
                    (pcv.Tau1, pcv.S1, pcv.S2) = pcv.GroundLayer.GranularityClass switch
                    {
                        "砂質土" => (0.8 * pcv.Tau2, 5.0, 20.0),
                        "礫質土" => (0.7 * pcv.Tau2, 10.0, 30.0),
                        "粘性土" => (0.8 * pcv.Tau2, 3.0, 10.0),
                        _ => (pcv.Tau1, pcv.S1, pcv.S2)
                    };
                }
                return;
            }

            ResetCircumOverrides();

            foreach (var pileCircumVertical in PileCircumVerticals)
            {
                var granularityClass = pileCircumVertical.GroundLayer.GranularityClass;
                var cohesive = pileCircumVertical.GroundLayer.Cohesive;
                var nValue = pileCircumVertical.GroundLayer.NValue;

                pileCircumVertical.Tau2 = PileConstructionType switch
                {
                    PileConstructionTypeNames.Insitu => granularityClass switch
                    {
                        "砂質土" => Math.Min(3.3 * nValue, 165),
                        "礫質土" => Math.Min(3.3 * nValue, 165),
                        "粘性土" => Math.Min(cohesive, 100),
                        _ => pileCircumVertical.Tau2
                    },
                    _ when PileConstructionTypeNames.IsPreboring(PileConstructionType) => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.5 * nValue, 125),
                        "礫質土" => Math.Min(2.5 * nValue, 125),
                        "粘性土" => Math.Min(cohesive, 125),
                        _ => pileCircumVertical.Tau2
                    },
                    PileConstructionTypeNames.Chubori => granularityClass switch
                    {
                        "砂質土" => Math.Min(1.5 * nValue, 75),
                        "礫質土" => Math.Min(1.5 * nValue, 75),
                        "粘性土" => Math.Min(0.4 * cohesive, 50),
                        _ => pileCircumVertical.Tau2
                    },
                    PileConstructionTypeNames.Rotary => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.0 * nValue, 100),
                        "礫質土" => Math.Min(2.0 * nValue, 100),
                        "粘性土" => Math.Min(0.5 * cohesive, 62.5),
                        _ => pileCircumVertical.Tau2
                    },
                    PileConstructionTypeNames.Driven => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.0 * nValue, 100),
                        "礫質土" => Math.Min(2.0 * nValue, 100),
                        "粘性土" => Math.Min(0.8 * cohesive, 100),
                        _ => pileCircumVertical.Tau2
                    },
                    _ => pileCircumVertical.Tau2
                };

                (pileCircumVertical.Tau1, pileCircumVertical.S1, pileCircumVertical.S2, pileCircumVertical.TauT) = granularityClass switch
                {
                    "砂質土" => (0.8 * pileCircumVertical.Tau2, 5.0, 20.0, -2.0 / 3.0 * pileCircumVertical.Tau2),
                    "礫質土" => (0.7 * pileCircumVertical.Tau2, 10.0, 30.0, -2.0 / 3.0 * pileCircumVertical.Tau2),
                    "粘性土" => (0.8 * pileCircumVertical.Tau2, 3.0, 10.0, -4.0 / 5.0 * pileCircumVertical.Tau2),
                    _ => (pileCircumVertical.Tau1, pileCircumVertical.S1, pileCircumVertical.S2, pileCircumVertical.TauT)
                };

                // 注: τ1、τ2、τTはフラグに関係なく計算する
                // IsPositiveCircumResistance/IsNegativeCircumResistanceフラグは
                // CalculateResistances()での抵抗力集計時にのみ使用する
            }
        }

        // 抵抗力の計算メソッド
        public void CalculateResistances()
        {
            if (PileCircumVerticals == null) return;

            // PileCircumVerticalのフラグを使用（チェックボックスで編集可能）
            Rfu = PileCircumVerticals.Where(pcv => pcv.IsPositiveCircumResistance).Sum(pcv => pcv.Rf);
            Rtu = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rtu);
            Rtr = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rtr);
            Rty = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rty);

            // Hybrid ニーディングは引抜きにも先端項 κ·N·Ap がある (tRa = 2/3{κNAp + 周面} + Ws)。
            // 引抜き抵抗は負値で保持する規約なので、抵抗を増やす向きは減算になる。
            if (IsHybridKneading)
            {
                double toe = HybridUpliftToeResistance;
                Rtu -= toe;
                Rty -= (2.0 / 3.0) * toe;
                Rtr -= (1.0 / 1.2) * toe;
            }

            double w = PileCircumVerticals.Sum(pcv => pcv.PileBodySegment.PileSection.W * pcv.L);

            Rtu -= w;
            Rty -= w;
            Rtr -= w;

            Rt_SLS = (1.0 / 3.0) * Rtu;
            Rt_DLS = Rty;
            Rt_ULS = Rtr;
        }

        // HorizontalSoilReactionの更新
        private void SetHorizontalSoilReaction()
        {
            if (ZDataItems == null || ZDataItems.Count < 2) return;

            if (PileBodySegments == null || GroundLayers == null) return;

            HorizontalSoilReactions.Clear();

            double zTop = ZDataItems[0].Z;
            for (int i = 0; i < ZDataItems.Count - 1; i++)
            {
                var zDataTop = ZDataItems[i].Z;
                var zDataBtm = ZDataItems[i + 1].Z;

                double b = 0;
                double cohesive = 0;
                double nValue = 0;
                double gamma = 0;
                double phi = 0;
                double stressTop = 0;
                double stressBtm = 0;
                string soilType = string.Empty;
                double e0 = 0;
                string name = string.Empty;
                double xi = 1;
                double rOnB = 10_000;

                double upper = zDataTop - zTop; // 杭頭基準深さ
                double lower = zDataBtm - zTop; // 杭頭基準深さ

                foreach (PileBodySegment pileBodySegment in PileBodyInput.PileBodySegments)
                {
                    double segmentTop = -pileBodySegment.SegmentDepth + pileBodySegment.SegmentLength;
                    double segmentBtm = -pileBodySegment.SegmentDepth;

                    if (segmentBtm - epsilon <= lower && upper <= segmentTop + epsilon)
                    {
                        b = pileBodySegment.PileSection?.PileDiameter / 1000.0 ?? 0;
                        break;
                    }
                }

                // フォールバック: マッチしない場合は最も近い区間の杭径を使用
                if (Math.Abs(b) < epsilon && PileBodyInput.PileBodySegments.Count > 0)
                {
                    double midDepth = (upper + lower) * 0.5;
                    PileBodySegment closest = null;
                    double closestDist = double.MaxValue;
                    foreach (var seg in PileBodyInput.PileBodySegments)
                    {
                        double segMid = (-seg.SegmentDepth + seg.SegmentLength * 0.5);
                        double dist = Math.Abs(midDepth - segMid);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = seg;
                        }
                    }
                    if (closest?.PileSection != null)
                    {
                        b = closest.PileSection.PileDiameter / 1000.0;
                        Serilog.Log.Debug(
                            $"[SetHorizontalSoilReaction] WARNING: 要素{i}(Z={zDataTop:F3}~{zDataBtm:F3})の杭区間マッチなし→最近接区間(b={b:F4}m)を使用");
                    }
                }

                bool groundFound = false;
                foreach (GroundLayerInput groundLayer in GroundLayers)
                {
                    var groundInput = GroundInput;
                    double top = groundLayer.LayerThickness + groundLayer.BottomAltitude;
                    double bottom = groundLayer.BottomAltitude;

                    if (bottom - epsilon <= zDataBtm && zDataTop <= top + epsilon)
                    {
                        cohesive = groundLayer.Cohesive;
                        nValue = groundLayer.NValue;
                        gamma = groundLayer.Density;

                        stressTop = GetEffectiveStress(groundInput, zDataTop);
                        stressBtm = GetEffectiveStress(groundInput, zDataBtm);

                        phi = groundInput.GetFrictionAngle(nValue, (stressTop + stressBtm) * 0.5);
                        soilType = groundLayer.GranularityClass;
                        e0 = groundLayer.Es;
                        name = groundLayer.Name;
                        groundFound = true;
                        break;
                    }
                }

                // フォールバック: マッチしない場合は入力地盤層から直接検索
                if (!groundFound && GroundInput?.GroundLayers != null)
                {
                    foreach (GroundLayerInput gl in GroundInput.GroundLayers)
                    {
                        double top = gl.LayerThickness + gl.BottomAltitude;
                        double bottom = gl.BottomAltitude;

                        if (bottom - epsilon <= zDataBtm && zDataTop <= top + epsilon)
                        {
                            cohesive = gl.Cohesive;
                            nValue = gl.NValue;
                            gamma = gl.Density;

                            stressTop = GetEffectiveStress(GroundInput, zDataTop);
                            stressBtm = GetEffectiveStress(GroundInput, zDataBtm);

                            phi = GroundInput.GetFrictionAngle(nValue, (stressTop + stressBtm) * 0.5);
                            soilType = gl.GranularityClass;
                            e0 = gl.Es;
                            name = gl.Name;
                            groundFound = true;
                            Serilog.Log.Debug(
                                $"[SetHorizontalSoilReaction] WARNING: 要素{i}(Z={zDataTop:F3}~{zDataBtm:F3})のSoilPile.GroundLayersマッチなし→入力地盤層'{name}'(top={top:F3},btm={bottom:F3})を使用");
                            break;
                        }
                    }
                }

                // 最終フォールバック: 要素の中点に最も近い入力地盤層を使用
                if (!groundFound && GroundInput?.GroundLayers != null && GroundInput.GroundLayers.Count > 0)
                {
                    double midZ = (zDataTop + zDataBtm) * 0.5;
                    GroundLayerInput closest = null;
                    double closestDist = double.MaxValue;
                    foreach (var gl in GroundInput.GroundLayers)
                    {
                        double layerMid = gl.BottomAltitude + gl.LayerThickness * 0.5;
                        double dist = Math.Abs(midZ - layerMid);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closest = gl;
                        }
                    }
                    if (closest != null)
                    {
                        cohesive = closest.Cohesive;
                        nValue = closest.NValue;
                        gamma = closest.Density;
                        stressTop = GetEffectiveStress(GroundInput, zDataTop);
                        stressBtm = GetEffectiveStress(GroundInput, zDataBtm);
                        phi = GroundInput.GetFrictionAngle(nValue, (stressTop + stressBtm) * 0.5);
                        soilType = closest.GranularityClass;
                        e0 = closest.Es;
                        name = closest.Name;
                        Serilog.Log.Debug(
                            $"[SetHorizontalSoilReaction] WARNING: 要素{i}(Z={zDataTop:F3}~{zDataBtm:F3})の地盤層マッチなし→最近接地盤層'{name}'を使用");
                    }
                }

                HorizontalSoilReactionItem horizontalSoilReactionItem = new();
                horizontalSoilReactionItem.SetParameters(
                name, soilType, gamma, b, e0,
                zDataTop, zDataBtm,
                xi, rOnB, nValue, phi, cohesive, stressTop, stressBtm);

                // 土層ごとの kh0 手入力オーバーライドがあれば適用（自動計算値を上書き）
                double? kh0Override = GetKh0Override(name);
                if (kh0Override.HasValue)
                {
                    horizontalSoilReactionItem.Kh0 = kh0Override.Value;
                    horizontalSoilReactionItem.IsKh0Manual = true;
                }

                HorizontalSoilReactions.Add(horizontalSoilReactionItem);
            }
        }

        // 有効応力を得るメソッド
        private static double GetEffectiveStress(GroundInput groundInput, double z)
        {
            double stressLevel = groundInput.StressAltitude;
            double waterLevel = groundInput.GroundWaterTableAltitude;

            double effectiveStress = 0;

            foreach (var groundLayer in groundInput.GroundLayers)
            {
                double density = groundLayer.Density;
                double topAltitude = groundLayer.BottomAltitude + groundLayer.LayerThickness;
                double layerTopLevel = Math.Min(stressLevel, topAltitude);
                double layerBottomLevel = Math.Max(z, groundLayer.BottomAltitude);

                effectiveStress += Math.Max((layerTopLevel - layerBottomLevel) * density, 0);
            }
            effectiveStress -= Math.Max(waterLevel - z, 0) * 10;

            return effectiveStress;
        }


        // DeepCopy メソッド
        public SoilPile DeepCopy()
        {
            var copy = new SoilPile();
            copy.Initialize(
                this.No,
                this.GroundNo,
                this.GroundInput,
                this.PileBodyNo,
                this.PileBodyInput,
                this.Z,
                new ObservableCollection<PileZDataItem>(
                    (this.ZDataItems ?? []).Select(item => item.DeepCopy()))
            );
            {
                copy.PileBottomAltitude = this.PileBottomAltitude;
                //PileToeNValue = this.PileToeNValue,
                copy.PileToeCohesive = this.PileToeCohesive;
                copy.PileToeEta = this.PileToeEta;
                copy.Qpu = this.Qpu;
                copy.SettleQpu = this.SettleQpu;
                copy.PileConstructionType = this.PileConstructionType;
                copy.PileToeGranularityClass = this.PileToeGranularityClass;
                copy.D = this.D;
                copy.Dp = this.Dp;
                copy.Rfu = this.Rfu;
                copy.Rt_SLS = this.Rt_SLS;
                copy.Rt_DLS = this.Rt_DLS;
                copy.Rt_ULS = this.Rt_ULS;
                copy.Rtu = this.Rtu;
                copy.Rtr = this.Rtr;
                copy.Rty = this.Rty;
                copy.GroupPileLoadDia = this.GroupPileLoadDia;

                // 解析結果（荷重-沈下曲線）
                copy.LoadDisplacements = this.LoadDisplacements;
                copy.LoadDisplacementsLimit = this.LoadDisplacementsLimit;
                // 全節点データ — [JsonIgnore] フィールドのため参照コピーで保持
                // （基礎梁考慮沈下解析の per-node 表示に使用）
                copy.NodeDisplacements = this.NodeDisplacements;
                copy.NodeReactions = this.NodeReactions;

                // 空を許す。Undo のたびに全 SoilPile でここを通るので、
                // 途中まで組んだモデルで落とさない
                copy.PileBodySegments = new ObservableCollection<PileBodySegment>
                    ((this.PileBodySegments ?? []).Select(segment => segment.DeepCopy()));
                copy.GroundLayers = new ObservableCollection<GroundLayerInput>
                    ((this.GroundLayers ?? []).Select(layer => layer.DeepCopy()));
                copy.PileCircumVerticals = new ObservableCollection<PileCircumVertical>
                    ((this.PileCircumVerticals ?? []).Select(pileCircumVertical => pileCircumVertical.DeepCopy()));
                copy.HorizontalSoilReactions = new ObservableCollection<HorizontalSoilReactionItem>
                    (this.HorizontalSoilReactions.Select(horizontalSoilReaction => horizontalSoilReaction.DeepCopy()));
                copy.Kh0LayerOverrides = new ObservableCollection<Kh0LayerOverride>
                    (this.Kh0LayerOverrides.Select(o => o.DeepCopy()));
            }
            ;
            return copy;
        }
    }
}

