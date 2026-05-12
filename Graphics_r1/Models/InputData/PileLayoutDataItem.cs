using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    public class PileLayoutDataItem : InputNode
    {
        private MainWindowViewModel _mainWindowViewModel;

        // [JsonIgnore]: InputModel への back-reference は循環参照を生むため JSON シリアライズ対象外
        [JsonIgnore]
        public InputModel InputModel => _mainWindowViewModel?.CurrentInputModel;

        private const double ZMatchTolerance = 1e-6;

        [JsonIgnore]
        public SoilPile? SoilPile
        {
            get
            {
                // Phase 1: キャッシュ検索に置き換え
                var inputModel = InputModel;
                if (inputModel == null) return CreateTemporarySoilPile();

                // SoilPile キャッシュキーは杭頭 Z 基準 (v2 セマンティクスでは pile.Z は接合節点 Z なので PileHeadZ を渡す)
                var found = inputModel.LookupSoilPile(GroundNo, PileBodyNo, this.PileHeadZ);
                return found ?? CreateTemporarySoilPile();
            }
        }

        private SoilPile? CreateTemporarySoilPile()
        {
            var input = InputModel;
            if (input == null) return null;

            int pileBodyIndex = PileBodyNo - 1;
            int groundIndex = GroundNo - 1;
            if (pileBodyIndex < 0 || input.PileBodies == null || pileBodyIndex >= input.PileBodies.Count) return null;
            if (groundIndex < 0 || input.GroundsInput == null || groundIndex >= input.GroundsInput.Count) return null;

            var pileBody = input.PileBodies[pileBodyIndex];
            var ground = input.GroundsInput[groundIndex];

            pileBody.PileBodySegmentsUpdate();
            var segments = pileBody.PileBodySegments;
            var layers = ground.GroundLayers;

            // SoilPile は杭頭 Z 基準で構築する。v2 セマンティクスでは pile.Z は接合節点 Z なので、
            // 杭頭は PileHeadZ (= pile.Z - ΔZc) を起点にする。
            double pileTopZ = this.PileHeadZ;
            var zs = new ObservableCollection<double> { pileTopZ };
            foreach (var seg in segments)
                zs.Add(pileTopZ - seg.SegmentDepth);

            double bottomZ = zs[^1];

            foreach (var gl in layers)
            {
                if (pileTopZ > gl.BottomAltitude && gl.BottomAltitude > bottomZ)
                {
                    for (int i = zs.Count - 1; i >= 0; i--)
                    {
                        if (zs[i] < gl.BottomAltitude)
                        {
                            zs.Insert(i, gl.BottomAltitude);
                            break;
                        }
                    }
                }
            }

            // トレランス付き重複除去（杭区間境界と地層境界が微小差で重複するケースを防止）
            var tempZs = zs.OrderByDescending(v => v).ToList();
            var sortedZs = new List<double>(tempZs.Count);
            foreach (var v in tempZs)
            {
                if (sortedZs.Count == 0 || Math.Abs(sortedZs[^1] - v) > NumericalConstants.COORDINATE_TOLERANCE)
                    sortedZs.Add(v);
            }
            var zItems = new ObservableCollection<PileZDataItem>();
            foreach (var z in sortedZs)
            {
                var zi = new PileZDataItem
                {
                    Z = z,
                    GroundInput = ground
                };
                zi.SetSoilDisplacement();
                zItems.Add(zi);
            }

            var temp = new SoilPile();
            temp.Initialize(
                no: 0,
                groundNo: GroundNo,
                groundInput: ground,
                pileBodyNo: PileBodyNo,
                pileBodyInput: pileBody,
                z: pileTopZ,  // SoilPile.Z は杭頭基準 (v2 セマンティクス)
                zDataItems: zItems);

            temp.UpdateProperties();
            return temp;
        }

        // 杭番号
        private int _pileNo;
        public int PileNo
        {
            get => _pileNo;
            set => SetProperty(ref _pileNo, value);
        }

        // 杭体番号
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        // 地盤番号
        private int _groundNo;
        public int GroundNo
        {
            get => _groundNo;
            set
            {
                if (SetProperty(ref _groundNo, value))
                {
                    OnPropertyChanged(nameof(SoilPile));
                }
            }
        }

        // 地盤杭高さ番号
        private int _soilPileAltNo;
        public int SoilPileAltNo
        {
            get => _soilPileAltNo;
            set
            {
                if (SetProperty(ref _soilPileAltNo, value))
                {
                    OnPropertyChanged(nameof(SoilPile));
                }
            }
        }

        // 接続先の基礎梁節点番号（剛床連結の場合）
        private int? _connectedFoundationNodeNo;
        public int? ConnectedFoundationNodeNo
        {
            get => _connectedFoundationNodeNo;
            set => SetProperty(ref _connectedFoundationNodeNo, value);
        }

        // 基礎梁接合節点の相対高さ（杭頭からの鉛直オフセット, 通常 +）
        private double _foundationBeamDeltaZc = 1.0;
        public double FoundationBeamDeltaZc
        {
            get => _foundationBeamDeltaZc;
            set => SetProperty(ref _foundationBeamDeltaZc, value);
        }

        /// <summary>
        /// 杭頭節点 (cap node) の Z 座標。
        /// v2 セマンティクスでは pile.Z は接合節点 Z を意味するため、
        /// 杭頭は ΔZc を差し引いた位置となる。FEM の cap node, SoilPile.Z (杭頭高さ) はこの値を使う。
        /// </summary>
        [JsonIgnore]
        public double PileHeadZ => Z - FoundationBeamDeltaZc;

        //群杭係数
        private double _groupPileFactor;
        public double GroupPileFactor
        {
            get => _groupPileFactor;
            set => SetProperty(ref _groupPileFactor, value);
        }

        // 杭間隔比
        private double _pileSpacingFactor;
        public double PileSpacingFactor
        {
            get => _pileSpacingFactor;
            set => SetProperty(ref _pileSpacingFactor, value);
        }

        // 常時軸力
        private double _axialForceVL0;
        public double AxialForceVL0
        {
            get => _axialForceVL0;
            set
            {
                double oldVL = AxialForceVL;
                if (SetProperty(ref _axialForceVL0, value))
                {
                    OnPropertyChanged(nameof(AxialForceVL)); // 追加: VL再計算通知
                    HandleVLChanged(oldVL, AxialForceVL);
                }
            }
        }

        // 追加軸力
        private double _axialForceVLAdditional;
        public double AxialForceVLAdditional
        {
            get => _axialForceVLAdditional;
            set
            {
                double oldVL = AxialForceVL;
                if (SetProperty(ref _axialForceVLAdditional, value))
                {
                    OnPropertyChanged(nameof(AxialForceVL)); // 追加: VL再計算通知
                    HandleVLChanged(oldVL, AxialForceVL);
                }
            }
        }

        public double AxialForceVL => AxialForceVL0 + AxialForceVLAdditional;

        // レベル1地震時軸力 (絶対値、ファイル保存対象)
        private ObservableCollection<double> _axialForceLevel1s;
        public ObservableCollection<double> AxialForceLevel1s
        {
            get => _axialForceLevel1s;
            set
            {
                if (_axialForceLevel1s != null)
                    _axialForceLevel1s.CollectionChanged -= OnAbsoluteL1Changed;
                if (SetProperty(ref _axialForceLevel1s, value))
                {
                    if (_axialForceLevel1s != null)
                        _axialForceLevel1s.CollectionChanged += OnAbsoluteL1Changed;
                    SyncVariationL1FromAbsolute();
                }
            }
        }

        // レベル2地震時軸力 (絶対値、ファイル保存対象)
        private ObservableCollection<double> _axialForceLevel2s;
        public ObservableCollection<double> AxialForceLevel2s
        {
            get => _axialForceLevel2s;
            set
            {
                if (_axialForceLevel2s != null)
                    _axialForceLevel2s.CollectionChanged -= OnAbsoluteL2Changed;
                if (SetProperty(ref _axialForceLevel2s, value))
                {
                    if (_axialForceLevel2s != null)
                        _axialForceLevel2s.CollectionChanged += OnAbsoluteL2Changed;
                    SyncVariationL2FromAbsolute();
                }
            }
        }

        // ─────────────── 変動軸力 (= 絶対 − VL、表示用、Json 非永続) ───────────────
        // AxialForceModeContext.IsVariationMode が true のとき、UI はこちらにバインドする。
        // 絶対値と双方向同期され、変動値の編集は絶対値の更新に連動する。
        // ファイル保存形式は絶対値のみ (これらは [JsonIgnore])。

        [JsonIgnore]
        private bool _isSyncingAxial;

        private ObservableCollection<double> _axialForceVariationLevel1s = [];
        [JsonIgnore]
        public ObservableCollection<double> AxialForceVariationLevel1s
        {
            get => _axialForceVariationLevel1s;
            private set
            {
                if (_axialForceVariationLevel1s != null)
                    _axialForceVariationLevel1s.CollectionChanged -= OnVariationL1Changed;
                if (SetProperty(ref _axialForceVariationLevel1s, value))
                {
                    if (_axialForceVariationLevel1s != null)
                        _axialForceVariationLevel1s.CollectionChanged += OnVariationL1Changed;
                }
            }
        }

        private ObservableCollection<double> _axialForceVariationLevel2s = [];
        [JsonIgnore]
        public ObservableCollection<double> AxialForceVariationLevel2s
        {
            get => _axialForceVariationLevel2s;
            private set
            {
                if (_axialForceVariationLevel2s != null)
                    _axialForceVariationLevel2s.CollectionChanged -= OnVariationL2Changed;
                if (SetProperty(ref _axialForceVariationLevel2s, value))
                {
                    if (_axialForceVariationLevel2s != null)
                        _axialForceVariationLevel2s.CollectionChanged += OnVariationL2Changed;
                }
            }
        }

        private void OnAbsoluteL1Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isSyncingAxial) return;
            _isSyncingAxial = true;
            try { SyncVariationL1FromAbsolute(); }
            finally { _isSyncingAxial = false; }
        }

        private void OnAbsoluteL2Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isSyncingAxial) return;
            _isSyncingAxial = true;
            try { SyncVariationL2FromAbsolute(); }
            finally { _isSyncingAxial = false; }
        }

        private void OnVariationL1Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isSyncingAxial) return;
            _isSyncingAxial = true;
            try { SyncAbsoluteL1FromVariation(); }
            finally { _isSyncingAxial = false; }
        }

        private void OnVariationL2Changed(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_isSyncingAxial) return;
            _isSyncingAxial = true;
            try { SyncAbsoluteL2FromVariation(); }
            finally { _isSyncingAxial = false; }
        }

        /// <summary>
        /// source の各要素に transform を適用して destination へ反映する。
        /// destination のサイズは source.Count に合わせて自動的にリサイズされる。
        /// 4 つの Sync* メソッド (絶対↔変動 × L1/L2) で重複していたコレクション同期処理を集約。
        /// </summary>
        private static void SyncCollectionWithTransform(
            ObservableCollection<double> source,
            ObservableCollection<double> destination,
            Func<double, double> transform)
        {
            if (source == null || destination == null) return;
            int n = source.Count;
            while (destination.Count > n)
                destination.RemoveAt(destination.Count - 1);
            while (destination.Count < n)
                destination.Add(0.0);
            for (int i = 0; i < n; i++)
                destination[i] = transform(source[i]);
        }

        private void SyncVariationL1FromAbsolute()
        {
            double vl = AxialForceVL;
            SyncCollectionWithTransform(_axialForceLevel1s, _axialForceVariationLevel1s, abs => abs - vl);
        }

        private void SyncVariationL2FromAbsolute()
        {
            double vl = AxialForceVL;
            SyncCollectionWithTransform(_axialForceLevel2s, _axialForceVariationLevel2s, abs => abs - vl);
        }

        private void SyncAbsoluteL1FromVariation()
        {
            double vl = AxialForceVL;
            SyncCollectionWithTransform(_axialForceVariationLevel1s, _axialForceLevel1s, var => vl + var);
        }

        private void SyncAbsoluteL2FromVariation()
        {
            double vl = AxialForceVL;
            SyncCollectionWithTransform(_axialForceVariationLevel2s, _axialForceLevel2s, var => vl + var);
        }

        /// <summary>
        /// VL (= VL0 + VLAdditional) 変更時の挙動。AxialForceModeContext.IsVariationMode に応じて分岐:
        ///   - 絶対モード: 絶対値はそのまま、変動値を新 VL から再計算 (= 絶対 − 新 VL)
        ///   - 変動モード: 変動値を保ち、絶対値を Δ VL だけシフト (= 旧絶対 + (新 VL − 旧 VL))
        /// </summary>
        private void HandleVLChanged(double oldVL, double newVL)
        {
            if (_isSyncingAxial) return;
            if (oldVL == newVL) return;
            _isSyncingAxial = true;
            try
            {
                if (Common.AxialForceModeContext.IsVariationMode)
                {
                    // 変動モード: 絶対値を Δ VL だけシフト、変動値は不変
                    double delta = newVL - oldVL;
                    if (_axialForceLevel1s != null)
                    {
                        for (int i = 0; i < _axialForceLevel1s.Count; i++)
                            _axialForceLevel1s[i] += delta;
                    }
                    if (_axialForceLevel2s != null)
                    {
                        for (int i = 0; i < _axialForceLevel2s.Count; i++)
                            _axialForceLevel2s[i] += delta;
                    }
                }
                else
                {
                    // 絶対モード: 絶対値はそのまま、変動値を再計算
                    SyncVariationL1FromAbsolute();
                    SyncVariationL2FromAbsolute();
                }
            }
            finally { _isSyncingAxial = false; }
        }

        // 常時単杭沈下
        private double _singlePileSettlementVL;
        public double SinglePileSettlementVL
        {
            get => _singlePileSettlementVL;
            set => SetProperty(ref _singlePileSettlementVL, value);
        }

        // レベル1地震時単杭沈下
        private ObservableCollection<double> _singlePileSettlementLevel1s;
        public ObservableCollection<double> SinglePileSettlementLevel1s
        {
            get => _singlePileSettlementLevel1s;
            set => SetProperty(ref _singlePileSettlementLevel1s, value);
        }

        // レベル1地震時単杭沈下
        private ObservableCollection<double> _singlePileSettlementLevel2s;
        public ObservableCollection<double> SinglePileSettlementLevel2s
        {
            get => _singlePileSettlementLevel2s;
            set => SetProperty(ref _singlePileSettlementLevel2s, value);
        }

        // 前方杭後方杭
        private ObservableCollection<bool> _isFrontPiles;
        public ObservableCollection<bool> IsFrontPiles
        {
            get => _isFrontPiles;
            set => SetProperty(ref _isFrontPiles, value);
        }

        // 杭先端N値
        private double _pileTipNValue;
        public double PileTipNValue
        {
            get => _pileTipNValue;
            set => SetFiniteClampedDouble(ref _pileTipNValue, value, min: 0.0, max: 1000.0, fallback: 0.0);
        }

        // 極限周面抵抗力
        private double _rf;
        public double Rf
        {
            get => _rf;
            set => SetProperty(ref _rf, value);
        }

        // 極限先端支持力
        private double _rp;
        public double Rp
        {
            get => _rp;
            set => SetProperty(ref _rp, value);
        }

        // 極限鉛直支持力
        private double _ru;
        public double Ru
        {
            get => _ru;
            set => SetProperty(ref _ru, value);
        }

        // 長期群杭沈下
        private double _groupPileSettlement;
        public double GroupPileSettlement
        {
            get => _groupPileSettlement;
            set => SetProperty(ref _groupPileSettlement, value);
        }

        // 解析杭節点 (FEM 解析ランタイム状態 — Undo/ファイル保存対象外)
        private ObservableCollection<FEM.Node> pileNodes = [];
        [JsonIgnore]
        public ObservableCollection<FEM.Node> PileNodes
        {
            get => pileNodes;
            set => SetProperty(ref pileNodes, value);
        }

        // 解析土節点 (FEM 解析ランタイム状態)
        private ObservableCollection<FEM.Node> soilNodes = [];
        [JsonIgnore]
        public ObservableCollection<FEM.Node> SoilNodes
        {
            get => soilNodes;
            set => SetProperty(ref soilNodes, value);
        }


        // 解析水平地盤ばね (FEM 解析ランタイム状態)
        private ObservableCollection<HorizontalSoilSpring> horizontalSoilSprings = [];
        [JsonIgnore]
        public ObservableCollection<HorizontalSoilSpring> HorizontalSoilSprings
        {
            get => horizontalSoilSprings;
            set => SetProperty(ref horizontalSoilSprings, value);
        }

        // 解析杭頭回転ばね (FEM 解析ランタイム状態)
        private RotationalSpring pileTopRotationalSpring;
        [JsonIgnore]
        public RotationalSpring PileTopRotationalSpring
        {
            get => pileTopRotationalSpring;
            set => SetProperty(ref pileTopRotationalSpring, value);
        }

        // 解析要素 (FEM 解析ランタイム状態)
        private ObservableCollection<FEM.Beam> beams = [];
        [JsonIgnore]
        public ObservableCollection<FEM.Beam> Beams
        {
            get => beams;
            set => SetProperty(ref beams, value);
        }

        // 解析軸力
        private double _axialForce;
        public double AxialForce
        {
            get => _axialForce;
            set => SetProperty(ref _axialForce, value);
        }

        // 解析軸力増分
        private double _axialForceIncrement;
        public double AxialForceIncrement
        {
            get => _axialForceIncrement;
            set => SetProperty(ref _axialForceIncrement, value);
        }

        // コンストラクタ初期化
        public PileLayoutDataItem()
        {
            IsVisible = true;

            GroundNo = 1;
            PileBodyNo = 1;
            SoilPileAltNo = 1;
            GroupPileFactor = 1;

            AxialForceLevel1s = [];
            AxialForceLevel2s = [];

            IsFrontPiles = [];

            SinglePileSettlementLevel1s = [];
            SinglePileSettlementLevel2s = [];
            // 4方向ずつ初期化
            for (int i = 0; i < 4; i++)
            {
                AxialForceLevel1s.Add(0.0);
                AxialForceLevel2s.Add(0.0);

                SinglePileSettlementLevel1s.Add(0.0);
                SinglePileSettlementLevel2s.Add(0.0);

                IsFrontPiles.Add(true);
            }

            // 変動軸力コレクションを初期化 (絶対値が全て 0、VL も 0 なので変動も全て 0)
            _axialForceVariationLevel1s.CollectionChanged += OnVariationL1Changed;
            _axialForceVariationLevel2s.CollectionChanged += OnVariationL2Changed;
            // 変動コレクションのサイズを絶対値に合わせる
            _isSyncingAxial = true;
            try
            {
                SyncVariationL1FromAbsolute();
                SyncVariationL2FromAbsolute();
            }
            finally { _isSyncingAxial = false; }

            // Z 変更時にも SoilPile 再評価を通知
            // Z または ΔZc 変更時に PileHeadZ (= Z - ΔZc) も再評価通知
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Z))
                {
                    OnPropertyChanged(nameof(SoilPile));
                    OnPropertyChanged(nameof(PileHeadZ));
                }
                else if (e.PropertyName == nameof(FoundationBeamDeltaZc))
                {
                    OnPropertyChanged(nameof(PileHeadZ));
                }
            };
        }

        /// <summary>
        /// Phase C 案 B: Undo スナップショット用の高速手書き Clone。
        /// JSON 経由 (DeepCopyUtil.CloneJson) は反射ベースで杭 1 件あたり ~1 ms 掛かるが、
        /// 本メソッドは値型コピーのみで杭 108 件分が合計 1〜2 ms に収まる。
        ///
        /// JsonIgnore 対象 (FEM ランタイム状態 / _isSyncingAxial 等) はコピーしない。
        /// 解析結果系フィールド (PileNodes, Beams 等) は再解析で復元するため Undo 対象外。
        /// </summary>
        public PileLayoutDataItem QuickClone()
        {
            var copy = new PileLayoutDataItem
            {
                // InputNode 基底
                UniqueId = this.UniqueId,
                No = this.No,
                Type = this.Type,
                LinkedPileNo = this.LinkedPileNo,
                IsSelected = this.IsSelected,
                IsVisible = this.IsVisible,
                X = this.X,
                Y = this.Y,
                Z = this.Z,

                // PileLayoutDataItem 固有 (primitive)
                PileNo = this.PileNo,
                PileBodyNo = this.PileBodyNo,
                GroundNo = this.GroundNo,
                SoilPileAltNo = this.SoilPileAltNo,
                ConnectedFoundationNodeNo = this.ConnectedFoundationNodeNo,
                FoundationBeamDeltaZc = this.FoundationBeamDeltaZc,
                GroupPileFactor = this.GroupPileFactor,
                PileSpacingFactor = this.PileSpacingFactor,
                AxialForceVL0 = this.AxialForceVL0,
                AxialForceVLAdditional = this.AxialForceVLAdditional,
                SinglePileSettlementVL = this.SinglePileSettlementVL,
                PileTipNValue = this.PileTipNValue,
                Rf = this.Rf,
                Rp = this.Rp,
                Ru = this.Ru,
                GroupPileSettlement = this.GroupPileSettlement,
                AxialForce = this.AxialForce,
                AxialForceIncrement = this.AxialForceIncrement,
            };

            // ObservableCollection の deep copy (要素は double / bool で値型)
            // setter 経由で代入すると変動軸力同期 (OnAbsoluteL?Changed) が走る点に注意。
            // QuickClone の出力先 copy では VL もこの段階で確定済なので、変動値の再計算が
            // 正しい値で実行される (VL=0 で先に L1/L2 が設定される事故を起こさない)。
            copy.AxialForceLevel1s = new ObservableCollection<double>(this.AxialForceLevel1s);
            copy.AxialForceLevel2s = new ObservableCollection<double>(this.AxialForceLevel2s);
            copy.SinglePileSettlementLevel1s = new ObservableCollection<double>(this.SinglePileSettlementLevel1s);
            copy.SinglePileSettlementLevel2s = new ObservableCollection<double>(this.SinglePileSettlementLevel2s);
            copy.IsFrontPiles = new ObservableCollection<bool>(this.IsFrontPiles);

            return copy;
        }

        /// <summary>
        /// JSON ロード後に呼び出し、絶対値コレクションから変動値コレクションを再構築する。
        /// JSON deserializer はプロパティ順序が不定で、AxialForceVL0/VLadd と AxialForceLevel1s/2s
        /// の設定順が逆になると初期同期が不正確になることがあるため、ロード完了後に明示的に再同期する。
        /// </summary>
        public void RebuildVariationFromAbsolute()
        {
            _isSyncingAxial = true;
            try
            {
                SyncVariationL1FromAbsolute();
                SyncVariationL2FromAbsolute();
            }
            finally { _isSyncingAxial = false; }
        }

        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            // readonlyを外して、後からセットできるようにする
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        }

        public double GetSeismicAxialForce(int loadCaseNo, int level)
        {
            if (level == 1)
            {
                if (loadCaseNo < 0 || AxialForceLevel1s.Count < loadCaseNo)
                    throw new ArgumentOutOfRangeException(nameof(loadCaseNo), "Invalid load case index for Level 1.");
                return AxialForceLevel1s[loadCaseNo - 1];
            }
            else // level == 2
            {
                if (loadCaseNo < 0 || AxialForceLevel1s.Count < loadCaseNo)
                    throw new ArgumentOutOfRangeException(nameof(loadCaseNo), "Invalid load case index for Level 2.");
                return AxialForceLevel2s[loadCaseNo - 1];
            }
        }


        // DataGrid再描画用: SoilPile再評価通知
        public void NotifySoilPileChanged() => OnPropertyChanged(nameof(SoilPile));
    }
}
