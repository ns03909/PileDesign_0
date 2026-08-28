using PileDesign.Constants;
using Newtonsoft.Json;
using PileDesign.ViewModels;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel; // 追加
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Media3D;
using PileDesign.Services;

namespace PileDesign.Models.InputData
{
    /// <summary>水平解析の杭先端Zばねに使う P-S 曲線のソース。</summary>
    public enum PsSpringSourceMode
    {
        /// <summary>常時 (LoadDisplacements)</summary>
        Normal = 0,
        /// <summary>極限 (LoadDisplacementsLimit)</summary>
        Ultimate = 1,
    }

    public class InputModel : BaseModel
    {
        private MainWindowViewModel _mainWindowViewModel;

        // 基本設定
        private FundamentalInput _foundationInput;
        public FundamentalInput FundamentalInput
        {
            get => _foundationInput;
            set => SetProperty(ref _foundationInput, value);
        }

        // 荷重ケース
        private LoadCasesInput _loadCasesInput;
        public LoadCasesInput LoadCasesInput
        {
            get => _loadCasesInput;
            set => SetProperty(ref _loadCasesInput, value);
        }

        // 地盤情報
        private ObservableCollection<GroundInput> _groundsInput;
        public ObservableCollection<GroundInput> GroundsInput
        {
            get => _groundsInput;
            set
            {
                if (SetProperty(ref _groundsInput, value))
                {
                    OnPropertyChanged(nameof(GroundsInputCountList));
                }
            }
        }

        // 地盤数リスト（内部 backing field を持ち、クラス内部で更新可能にする）
        private ObservableCollection<int> _groundsInputCountList = [];
        public ObservableCollection<int> GroundsInputCountList
        {
            get => _groundsInputCountList;
            private set => SetProperty(ref _groundsInputCountList, value);
        }

        // 杭体
        private ObservableCollection<PileBodyInput> _pileBodies;
        public ObservableCollection<PileBodyInput> PileBodies
        {
            get => _pileBodies;
            set
            {
                if (SetProperty(ref _pileBodies, value))
                {
                    OnPropertyChanged(nameof(PileBodiesCountList));
                }
            }
        }

        // 杭体数リスト（内部 backing field を持ち、クラス内部で更新可能にする）
        private ObservableCollection<int> _pileBodiesCountList = [];
        public ObservableCollection<int> PileBodiesCountList
        {
            get => _pileBodiesCountList;
            private set => SetProperty(ref _pileBodiesCountList, value);
        }

        // 杭配置
        private ObservableCollection<PileLayoutDataItem> _pileLayoutItems;
        public ObservableCollection<PileLayoutDataItem> PileLayoutItems
        {
            get => _pileLayoutItems;
            set
            {
                if (_pileLayoutItems == value) return;
                _pileLayoutItems = value;
                OnPropertyChanged(nameof(PileLayoutItems));  // ← PropertyChanged が発火する
                RefreshAvailableNodeReferenceOptions();
                WirePileLayoutItemsHandlers(); // 追加: ハンドラ再配線
            }
        }

        // 根入
        private EmbedmentInput _embedmentInput;
        public EmbedmentInput EmbedmentInput
        {
            get => _embedmentInput;
            set => SetProperty(ref _embedmentInput, value);
        }

        // 基礎梁
        private FoundationBeamInput _foundationBeamInput;
        public FoundationBeamInput FoundationBeamInput
        {
            get => _foundationBeamInput;
            set
            {
                if (SetProperty(ref _foundationBeamInput, value))
                    RefreshAvailableNodeReferenceOptions();
            }
        }

        // 杭軸力モード: 入力値＋応力解析結果を使用するか
        private bool _useAnalysisAxialForce = false;
        public bool UseAnalysisAxialForce
        {
            get => _useAnalysisAxialForce;
            set => SetProperty(ref _useAnalysisAxialForce, value);
        }

        // 地震時軸力 入力モード: false = 絶対 (AxialForceLevel1s/2s をそのまま編集)、true = 変動 (= 絶対 − VL を編集)
        // ファイル別に永続化。モードフラグ自体は AxialForceModeContext (静的) に同期される。
        private bool _isAxialForceVariationMode = false;
        public bool IsAxialForceVariationMode
        {
            get => _isAxialForceVariationMode;
            set => SetProperty(ref _isAxialForceVariationMode, value);
        }

        // 水平解析: 杭先端鉛直境界を P-S 非線形ばねに置換するか (true: 沈下解析の LoadDisplacements を流用)
        // false (既定): 従来通り Uz 固定
        private bool _usePsSpringAtPileTip = false;
        public bool UsePsSpringAtPileTip
        {
            get => _usePsSpringAtPileTip;
            set => SetProperty(ref _usePsSpringAtPileTip, value);
        }

        // P-S 曲線ソース: 常時(LoadDisplacements) / 極限(LoadDisplacementsLimit)
        private PsSpringSourceMode _psSpringSource = PsSpringSourceMode.Normal;
        public PsSpringSourceMode PsSpringSource
        {
            get => _psSpringSource;
            set => SetProperty(ref _psSpringSource, value);
        }

        // VL (常時) 単独ケースの解析実施フラグ (P-S 非線形ばね有効時のみ意味あり)
        // ON: 水平荷重なし + 各杭頭に AxialForceVL を外力として適用したケースを「VL」として追加解析
        // OFF: 地震ケースのみ解析 (従来挙動)
        private bool _isVLAnalysisEnabled = false;
        public bool IsVLAnalysisEnabled
        {
            get => _isVLAnalysisEnabled;
            set => SetProperty(ref _isVLAnalysisEnabled, value);
        }

        // 水平解析: 基礎のねじれ (Z 軸まわりの回転) を拘束するか
        // true: 代表節点 (ActionPoint) の Rz を拘束する。剛体で繋がった杭頭は
        //       ねじれ成分を持たなくなり、水平変位が全杭で揃う。
        // false (既定): 従来通り。偏心と杭剛性の非対称からねじれが生じる。
        private bool _ignoreFoundationTorsion = false;
        public bool RestrainFoundationTorsion
        {
            get => _ignoreFoundationTorsion;
            set => SetProperty(ref _ignoreFoundationTorsion, value);
        }

        // 一般節点
        private ObservableCollection<InputNode> _inputNodes;
        public ObservableCollection<InputNode> InputNodes
        {
            get => _inputNodes;
            set
            {
                if (SetProperty(ref _inputNodes, value))
                    RefreshAvailableNodeReferenceOptions();
            }
        }

        // クラス内フィールドに追加
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _suppressSoilPileNotify;
        private static readonly Dictionary<(int groundNo, int pileBodyNo, double z), SoilPile> value = [];

        // Phase 1: SoilPile キャッシュ最適化 (ランタイム一時データ — シリアライズ対象外)
        [System.Text.Json.Serialization.JsonIgnore]
        private Dictionary<(int groundNo, int pileBodyNo, double z), SoilPile> _soilPileCache = value;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _soilPileCacheValid = false;
        // _soilPileCache / _soilPileCacheValid の同時アクセス保護用 lock。
        // LookupSoilPile は UI binding / docx 出力 / AutoSave / 解析からも呼ばれうるため、
        // 複数スレッドからの並行呼出で Dictionary が破損 (ConcurrentOperationsNotSupported) する
        // 問題を防ぐ。Rebuild は短時間で済むため lock 競合は無視できる。
        [System.Text.Json.Serialization.JsonIgnore]
        private readonly object _soilPileCacheLock = new();

        // Phase 2: デバウンス (ランタイム制御 — シリアライズ対象外)
        [System.Text.Json.Serialization.JsonIgnore]
        private System.Windows.Threading.DispatcherTimer? _regenerateDebounceTimer;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _regeneratePending = false;

        /// <summary>
        /// 大量の変更を行う前に通知を一時抑制する。
        /// 終了後は必ず ResumeAndNotify() を呼ぶこと。
        /// </summary>
        public void SuppressNotifications()
        {
            _suppressSoilPileNotify = true;
        }

        /// <summary>
        /// 通知を再開し、SoilPiles を再生成して一括通知を行う。
        /// </summary>
        public void ResumeAndNotify()
        {
            _suppressSoilPileNotify = false;
            RegenerateSoilPilesAndNotifyImmediate(); // Phase 2: Undo/Redo は即時実行
        }

        /// <summary>
        /// 通知抑制を解除するだけで、SoilPile 再生成や再描画はトリガーしない。
        /// 呼び出し元で GenerateSoilPiles() と描画更新を個別に行う場合に使用。
        /// </summary>
        public void ResumeNotificationsQuiet()
        {
            _suppressSoilPileNotify = false;
            _regeneratePending = false;
            _regenerateDebounceTimer?.Stop();
        }

        // Phase 1: SoilPile キャッシュ管理メソッド

        /// <summary>
        /// SoilPile キャッシュを無効化します。GenerateSoilPiles 後に呼び出されます。
        /// </summary>
        private void InvalidateSoilPileCache()
        {
            lock (_soilPileCacheLock)
            {
                _soilPileCacheValid = false;
            }
        }

        /// <summary>
        /// 必要に応じて SoilPile キャッシュを再構築します (lock 保護下)。
        /// </summary>
        private void RebuildSoilPileCacheIfNeeded_NoLock()
        {
            if (_soilPileCacheValid) return;

            _soilPileCache.Clear();
            if (ElementDivision?.SoilPiles == null) return;

            // ObservableCollection の列挙中に別スレッドが SoilPiles を書き換えると
            // System.InvalidOperationException (collection was modified) が出るため、
            // スナップショットを取ってから列挙する。
            var snapshot = ElementDivision.SoilPiles.ToList();
            foreach (var sp in snapshot)
            {
                if (sp == null) continue;
                // 許容差を考慮したキー生成
                double zKey = Math.Round(sp.Z / NumericalConstants.COORDINATE_TOLERANCE)
                              * NumericalConstants.COORDINATE_TOLERANCE;
                var key = (sp.GroundNo, sp.PileBodyNo, zKey);
                _soilPileCache[key] = sp;
            }

            _soilPileCacheValid = true;
        }

        /// <summary>
        /// SoilPile をキャッシュから高速検索します。
        /// 複数スレッドからの同時呼出に対して thread-safe。
        /// </summary>
        public SoilPile? LookupSoilPile(int groundNo, int pileBodyNo, double z)
        {
            double zKey = Math.Round(z / NumericalConstants.COORDINATE_TOLERANCE)
                          * NumericalConstants.COORDINATE_TOLERANCE;
            var key = (groundNo, pileBodyNo, zKey);

            lock (_soilPileCacheLock)
            {
                RebuildSoilPileCacheIfNeeded_NoLock();
                return _soilPileCache.TryGetValue(key, out var result) ? result : null;
            }
        }

        /// <summary>
        /// 杭体 No (1-based) に対する概要文字列を生成する。
        /// プロパティパネル / DataGrid セル等の ToolTip で「どんな杭体か」を即座に確認するための要約。
        /// 該当杭体が存在しない場合や情報が不足する場合は空文字を返す。
        /// </summary>
        public string GetPileBodySummary(int pileBodyNo)
        {
            int idx = pileBodyNo - 1;
            if (PileBodies == null || idx < 0 || idx >= PileBodies.Count) return string.Empty;
            var pb = PileBodies[idx];
            if (pb == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.Append($"杭体 No.{pileBodyNo}");
            if (!string.IsNullOrWhiteSpace(pb.PileBodyRef)) sb.Append($"  {pb.PileBodyRef}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(pb.PileBodyType)) sb.AppendLine($"種類: {pb.PileBodyType}");
            if (!string.IsNullOrWhiteSpace(pb.PileConstructionType)) sb.AppendLine($"工法: {pb.PileConstructionType}");
            if (pb.PileBodySegments != null && pb.PileBodySegments.Count > 0)
            {
                double totalLength = pb.PileBodySegments.Sum(s => s.SegmentLength);
                sb.AppendLine($"全長: {totalLength:F2} m  ({pb.PileBodySegments.Count} 区間)");
                var topSeg = pb.PileBodySegments[0];
                if (topSeg?.PileSection != null && topSeg.PileSection.PileDiameter > 0)
                    sb.AppendLine($"杭頭径: {topSeg.PileSection.PileDiameter:F0} mm");
            }
            if (pb.PileToeDia > 0) sb.Append($"先端径: {pb.PileToeDia:F0} mm");
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 地盤 No (1-based) に対する概要文字列を生成する。
        /// </summary>
        public string GetGroundSummary(int groundNo)
        {
            int idx = groundNo - 1;
            if (GroundsInput == null || idx < 0 || idx >= GroundsInput.Count) return string.Empty;
            var g = GroundsInput[idx];
            if (g == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.Append($"地盤 No.{groundNo}");
            if (!string.IsNullOrWhiteSpace(g.GroundRef)) sb.Append($"  {g.GroundRef}");
            sb.AppendLine();
            sb.AppendLine($"地表面標高: {g.GroundTopAltitude:F2} m");
            sb.AppendLine($"地下水位 GL深: {g.GroundWaterGLDepth:F2} m");
            int layerCount = g.GroundLayers?.Count ?? 0;
            int massCount = g.GroundMassesData?.Count ?? 0;
            sb.Append($"土層数: {layerCount}  /  土質点数: {massCount}");
            if (g.GroundLayers != null && g.GroundLayers.Count > 0)
            {
                double bottomDepth = g.GroundLayers.Max(l => -l.BottomAltitude + g.GroundTopAltitude);
                sb.AppendLine();
                sb.Append($"最下層 GL深: {bottomDepth:F2} m");
            }
            return sb.ToString().TrimEnd();
        }

        // Phase 2: デバウンス管理メソッド

        /// <summary>
        /// デバウンスタイマーを遅延初期化します（Dispatcher コンテキスト確保のため）。
        /// </summary>
        private void EnsureDebounceTimerInitialized()
        {
            if (_regenerateDebounceTimer != null) return;

            _regenerateDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50) // 50ms デバウンス
            };
            _regenerateDebounceTimer.Tick += (s, e) =>
            {
                _regenerateDebounceTimer.Stop();
                if (_regeneratePending && !_suppressSoilPileNotify)
                {
                    _regeneratePending = false;
                    RegenerateSoilPilesAndNotifyImmediate();
                }
            };
        }

        /// <summary>
        /// デバウンス付きで SoilPiles 再生成をスケジュールします。
        /// </summary>
        private void RegenerateSoilPilesAndNotifyDebounced()
        {
            if (_suppressSoilPileNotify) return;

            EnsureDebounceTimerInitialized(); // 遅延初期化

            _regeneratePending = true;
            _regenerateDebounceTimer?.Stop();
            _regenerateDebounceTimer?.Start();
        }

        /// <summary>
        /// SoilPiles を即時再生成します（Undo/Redo用）。
        /// </summary>
        private void RegenerateSoilPilesAndNotifyImmediate()
        {
            try
            {
                _suppressSoilPileNotify = true;
                GenerateSoilPiles();
                InvalidateSoilPileCache();
            }
            finally
            {
                _suppressSoilPileNotify = false;
            }

            NotifySoilPileChangedForAll();
            _mainWindowViewModel?.UpdateViewCommand?.Execute(null);
        }

        // 杭要素分割
        private ElementDivision _elementDivision;
        public ElementDivision ElementDivision
        {
            get => _elementDivision;
            set
            {
                if (SetProperty(ref _elementDivision, value))
                {
                    AttachElementDivisionHandlers();
                }
            }
        }

        // SoilPiles の変更を購読
        private void AttachElementDivisionHandlers()
        {
            if (ElementDivision?.SoilPiles == null) return;
            ElementDivision.SoilPiles.CollectionChanged -= SoilPiles_CollectionChanged;
            ElementDivision.SoilPiles.CollectionChanged += SoilPiles_CollectionChanged;
        }

        private void SoilPiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return;
            NotifySoilPileChangedForAll();
        }

        public void NotifySoilPileChangedForAll()
        {
            if (PileLayoutItems == null) return;
            foreach (var item in PileLayoutItems)
                item.NotifySoilPileChanged();
        }


        // ---- PileLayoutItems の追加/削除/編集 監視 ----

        private void WirePileLayoutItemsHandlers()
        {
            if (PileLayoutItems == null) return;

            PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
            PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;

            foreach (var item in PileLayoutItems)
            {
                item.PropertyChanged -= OnPileLayoutItemPropertyChanged;
                item.PropertyChanged += OnPileLayoutItemPropertyChanged;
            }
        }


        private void PileLayoutItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<PileLayoutDataItem>())
                {
                    it.SetMainWindowViewModel(_mainWindowViewModel);
                    it.PropertyChanged += OnPileLayoutItemPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<PileLayoutDataItem>())
                {
                    it.PropertyChanged -= OnPileLayoutItemPropertyChanged;
                }
            }

            // 追加/削除でもセット全体を再計算
            RegenerateSoilPilesAndNotify();
        }

        private void OnPileLayoutItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // SoilPile 再生成が必要な変更のみ
            if (e.PropertyName is nameof(PileLayoutDataItem.GroundNo)
                || e.PropertyName is nameof(PileLayoutDataItem.PileBodyNo)
                || e.PropertyName == "Z")
            {
                RegenerateSoilPilesAndNotify();
            }
        }


        // ---- Ground の変更監視（土層境界や層数が変わったら再生成） ----

        private void AttachGroundsHandlers()
        {
            if (GroundsInput == null) return;

            GroundsInput.CollectionChanged -= GroundsInput_CollectionChanged;
            GroundsInput.CollectionChanged += GroundsInput_CollectionChanged;

            foreach (var g in GroundsInput)
                HookGround(g);
        }

        private void GroundsInput_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var g in e.NewItems.OfType<GroundInput>())
                    HookGround(g);

            if (e.OldItems != null)
                foreach (var g in e.OldItems.OfType<GroundInput>())
                    UnhookGround(g);

            // 追加: 件数リストの再通知（ComboBox ItemsSource を更新させる）
            OnPropertyChanged(nameof(GroundsInputCountList));

            RegenerateSoilPilesAndNotify();

            UpdateCountLists();
        }

        private void HookGround(GroundInput g)
        {
            if (g == null) return;
            g.PropertyChanged -= OnGroundChanged;
            g.PropertyChanged += OnGroundChanged;

            if (g.GroundLayers != null)
            {
                g.GroundLayers.CollectionChanged -= GroundLayers_CollectionChanged;
                g.GroundLayers.CollectionChanged += GroundLayers_CollectionChanged;
                foreach (var layer in g.GroundLayers)
                {
                    layer.PropertyChanged -= OnGroundLayerChanged;
                    layer.PropertyChanged += OnGroundLayerChanged;
                }
            }
        }

        private void UnhookGround(GroundInput g)
        {
            if (g == null) return;
            g.PropertyChanged -= OnGroundChanged;
            if (g.GroundLayers != null)
            {
                g.GroundLayers.CollectionChanged -= GroundLayers_CollectionChanged;
                foreach (var layer in g.GroundLayers)
                    layer.PropertyChanged -= OnGroundLayerChanged;
            }
        }

        private void OnGroundChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            // 地盤上端/水位/液状化設定・層更新など、基本的に全て再生成で安全
            RegenerateSoilPilesAndNotify();
        }

        private void GroundLayers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var layer in e.NewItems.OfType<GroundLayerInput>())
                {
                    layer.PropertyChanged -= OnGroundLayerChanged;
                    layer.PropertyChanged += OnGroundLayerChanged;
                }
            if (e.OldItems != null)
                foreach (var layer in e.OldItems.OfType<GroundLayerInput>())
                    layer.PropertyChanged -= OnGroundLayerChanged;

            RegenerateSoilPilesAndNotify();
        }

        private void OnGroundLayerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            RegenerateSoilPilesAndNotify();
        }

        // ---- PileBody の変更監視（セグメント長などが変わったら再生成） ----

        private void AttachPileBodiesHandlers()
        {
            if (PileBodies == null) return;

            PileBodies.CollectionChanged -= PileBodies_CollectionChanged;
            PileBodies.CollectionChanged += PileBodies_CollectionChanged;

            foreach (var pb in PileBodies)
                HookPileBody(pb);
        }

        private void PileBodies_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var pb in e.NewItems.OfType<PileBodyInput>())
                    HookPileBody(pb);

            if (e.OldItems != null)
                foreach (var pb in e.OldItems.OfType<PileBodyInput>())
                    UnhookPileBody(pb);

            // 追加: 件数リストの再通知（ComboBox ItemsSource を更新させる）
            OnPropertyChanged(nameof(PileBodiesCountList));

            RegenerateSoilPilesAndNotify();

            UpdateCountLists();
        }

        private void HookPileBody(PileBodyInput pb)
        {
            if (pb == null) return;
            pb.PropertyChanged -= OnPileBodyChanged;
            pb.PropertyChanged += OnPileBodyChanged;

            if (pb.PileBodySegments != null)
            {
                pb.PileBodySegments.CollectionChanged -= PileBodySegments_CollectionChanged;
                pb.PileBodySegments.CollectionChanged += PileBodySegments_CollectionChanged;
                foreach (var seg in pb.PileBodySegments)
                {
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
                    seg.PropertyChanged += OnPileBodySegmentChanged;
                }
            }
        }

        private void UnhookPileBody(PileBodyInput pb)
        {
            if (pb == null) return;
            pb.PropertyChanged -= OnPileBodyChanged;
            if (pb.PileBodySegments != null)
            {
                pb.PileBodySegments.CollectionChanged -= PileBodySegments_CollectionChanged;
                foreach (var seg in pb.PileBodySegments)
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
            }
        }

        private void OnPileBodyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加
            RegenerateSoilPilesAndNotify();
        }

        private void PileBodySegments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            if (e.NewItems != null)
                foreach (var seg in e.NewItems.OfType<PileBodySegment>())
                {
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
                    seg.PropertyChanged += OnPileBodySegmentChanged;
                }
            if (e.OldItems != null)
                foreach (var seg in e.OldItems.OfType<PileBodySegment>())
                    seg.PropertyChanged -= OnPileBodySegmentChanged;

            RegenerateSoilPilesAndNotify();
        }

        private void OnPileBodySegmentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            RegenerateSoilPilesAndNotify();
        }

        // ---- 再生成＋通知の共通ハブ ----

        /// <summary>
        /// SoilPiles を再生成して通知します（Phase 2: デバウンス版に置き換え）。
        /// </summary>
        private void RegenerateSoilPilesAndNotify()
        {
            RegenerateSoilPilesAndNotifyDebounced();
        }






        // Reset/Set/Attach の最後でハンドラ張り直し
        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            if (PileLayoutItems != null)
            {
                foreach (var item in PileLayoutItems)
                {
                    item.SetMainWindowViewModel(mainWindowViewModel);
                    item.SetOwner(this);   // VM 経由で「現在の入力」に化けないよう親を固定
                }
            }

            Reset(); // ElementDivision等を初期化

            // 初期化後に各種購読を張る
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }

        /// <summary>
        /// 地盤関係の Z 値を一括シフトする。ReferenceAltitude 変更時に呼び、
        /// 絶対標高を保持したまま Z 座標だけ更新するのに使う。
        ///
        /// 実装は「標高 = GL深さ + 孔口Z」の不変関係を使って冪等に再計算する。
        /// GroundLayerViewModel.SyncDepthAltitude などが動いても二重シフトしない。
        /// </summary>
        public void ShiftGroundZByDelta(double deltaZ)
        {
            if (deltaZ == 0.0) return;
            if (GroundsInput == null || GroundsInput.Count == 0) return;

            SuppressNotifications();
            try
            {
                foreach (var ground in GroundsInput)
                {
                    if (ground == null) continue;

                    // 孔口Z を delta 分だけシフト
                    ground.GroundTopAltitude += deltaZ;

                    // 他の標高は不変関係「標高 = GL深さ + 孔口Z」で再計算（冪等）
                    ground.GroundWaterTableAltitude = ground.GroundWaterGLDepth + ground.GroundTopAltitude;
                    ground.StressAltitude = ground.StressGLDepth + ground.GroundTopAltitude;

                    if (ground.GroundLayers != null)
                    {
                        foreach (var layer in ground.GroundLayers)
                        {
                            if (layer == null) continue;
                            layer.BottomAltitude = layer.BottomGLDepth + ground.GroundTopAltitude;
                        }
                    }
                    if (ground.GroundMassesData != null)
                    {
                        foreach (var mass in ground.GroundMassesData)
                        {
                            if (mass == null) continue;
                            mass.AltitudeDepth = mass.GLDepth + ground.GroundTopAltitude;
                        }
                    }
                }
            }
            finally
            {
                ResumeAndNotify();
            }
        }

        // 軽量版
        public void AttachViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            if (PileLayoutItems != null)
            {
                foreach (var item in PileLayoutItems)
                {
                    item.SetMainWindowViewModel(mainWindowViewModel);
                    item.SetOwner(this);   // VM 経由で「現在の入力」に化けないよう親を固定
                }
            }

            // LoadCase の MainWindowViewModel を再セット（デシリアライズ後は null のため）
            if (LoadCasesInput != null)
            {
                LoadCasesInput.LoadCaseVL0?.SetMainWindowViewModel(mainWindowViewModel);
                LoadCasesInput.LoadCaseVLadd?.SetMainWindowViewModel(mainWindowViewModel);
                LoadCasesInput.LoadCaseVL?.SetMainWindowViewModel(mainWindowViewModel);
                if (LoadCasesInput.LoadCasesLevel1 != null)
                    foreach (var lc in LoadCasesInput.LoadCasesLevel1)
                        lc.SetMainWindowViewModel(mainWindowViewModel);
                if (LoadCasesInput.LoadCasesLevel2 != null)
                    foreach (var lc in LoadCasesInput.LoadCasesLevel2)
                        lc.SetMainWindowViewModel(mainWindowViewModel);
            }

            // 重要: DeepCopy 後に null になり得るコレクションを補正
            GridXItems ??= [];
            GridYItems ??= [];

            // 各種購読
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }

        // Reset 内の ElementDivision 生成後に購読をセット
        public void Reset()
        {
            FundamentalInput = new FundamentalInput();
            LoadCasesInput = new LoadCasesInput();
            LoadCasesInput.SetMainWindowViewModel(_mainWindowViewModel);
            GroundsInput = [new GroundInput()];
            PileBodies = [new PileBodyInput()];
            PileLayoutItems = [];
            InputNodes = [];
            EmbedmentInput = new EmbedmentInput();
            ElementDivision = new ElementDivision()
            {
                SoilPiles = [],
                SoilEmbedment = new SoilEmbedment(1, 0.0, [])
            };
            PileGroupSettlement = new PileGroupSettlement();
            GridXItems = [];
            GridYItems = [];

            // 基礎梁入力データの初期化（デフォルト材料・断面を作成）— No プロパティは廃止 (位置 = ID)
            FoundationBeamInput = new FoundationBeamInput();
            FoundationBeamInput.Materials.Add(new BeamMaterial
            {
                Name = "C24",
                YoungModulus = 2.5e7,
                ShearModulus = 1.04e7,
                PoissonRatio = 0.2
            });
            FoundationBeamInput.Sections.Add(new BeamSection
            {
                Name = "G1",
                Width = 0.8,
                Height = 2.0
            });

            // 初期コレクションにも購読を張る
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }


        private PileGroupSettlement _pileGroupSettlement;
        public PileGroupSettlement PileGroupSettlement
        {
            get => _pileGroupSettlement;
            set => SetProperty(ref _pileGroupSettlement, value);
        }

        // グリッド
        private ObservableCollection<GridDataItem> _gridXItems;
        //[JsonIgnore]
        public ObservableCollection<GridDataItem> GridXItems
        {
            get => _gridXItems;
            set => SetProperty(ref _gridXItems, value);
        }

        private ObservableCollection<GridDataItem> _gridYItems;
        //[JsonIgnore]
        public ObservableCollection<GridDataItem> GridYItems
        {
            get => _gridYItems;
            set => SetProperty(ref _gridYItems, value);
        }

        // コンストラクタ
        public InputModel()
        {
            UpdateCountLists();
            // Phase 2: デバウンスタイマーは遅延初期化（RegenerateSoilPilesAndNotifyDebounced() 内で）
        }


        //public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        //{
        //    _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        //    if (PileLayoutItems != null)
        //    {
        //        foreach (var item in PileLayoutItems)
        //        {
        //            item.SetMainWindowViewModel(mainWindowViewModel);
        //        }
        //    }

        //    // Elements内のNodesにもViewModelを再セット
        //    if (Elements != null)
        //    {
        //        foreach (var element in Elements)
        //        {
        //            if (element.Nodes != null)
        //            {
        //                foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
        //                {
        //                    node.SetMainWindowViewModel(mainWindowViewModel);
        //                }
        //            }
        //        }
        //    }

        //    Reset();
        //}

        //// 追加: 復元・読込時などに使う「Reset しない」軽量版
        //public void AttachViewModel(MainWindowViewModel mainWindowViewModel)
        //{
        //    _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        //    if (PileLayoutItems != null)
        //    {
        //        foreach (var item in PileLayoutItems)
        //        {
        //            item.SetMainWindowViewModel(mainWindowViewModel);
        //        }
        //    }

        //    if (Elements != null)
        //    {
        //        foreach (var element in Elements)
        //        {
        //            if (element.Nodes != null)
        //            {
        //                foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
        //                {
        //                    node.SetMainWindowViewModel(mainWindowViewModel);
        //                }
        //            }
        //        }
        //    }
        //    // 重要: DeepCopy 後に null になり得るコレクションを補正
        //    GridXItems ??= [];
        //    GridYItems ??= [];
        //}

        // 
        //public void Reset()
        //{
        //    FundamentalInput = new FundamentalInput();
        //    LoadCasesInput = new LoadCasesInput();
        //    LoadCasesInput.SetMainWindowViewModel(_mainWindowViewModel);
        //    GroundsInput = [new GroundInput()];
        //    PileBodies = [new PileBodyInput()];
        //    PileLayoutItems = [];
        //    EmbedmentInput = new EmbedmentInput();
        //    ElementDivision = new ElementDivision()
        //    {
        //        SoilPiles = [],
        //        SoilEmbedment = new SoilEmbedment(1, 0.0, [])
        //    };
        //    PileGroupSettlement = new PileGroupSettlement();
        //    Elements = [];
        //    GridXItems = [];
        //    GridYItems = [];
        //}

        public double GetSumVL()
        {
            if (PileLayoutItems == null) return 0.0;
            try
            {
                return PileLayoutItems.Sum(item => item.AxialForceVL0);
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
            catch (ArgumentNullException)
            {
                return 0.0;
            }
        }

        public double GetSumVLadd()
        {
            if (PileLayoutItems == null) return 0.0;
            try
            {
                return PileLayoutItems.Sum(item => item.AxialForceVLAdditional);
            }
            catch (InvalidOperationException)
            {
                return 0.0;
            }
            catch (ArgumentNullException)
            {
                return 0.0;
            }
        }

        public double GetSumVLplusVLadd()
        {
            try
            {
                return GetSumVL() + GetSumVLadd();
            }
            catch (OverflowException)
            {
                return 0.0;
            }
        }

        // VLadd重心を返すメソッド
        // v2 セマンティクス: item.Z = 接合節点 Z なので、Z 重心は接合節点平均 (荷重作用点としての重心)
        public Point3D GetVLaddGravityCenter()
        {
            double sumW = 0;
            double sumMX = 0, sumMY = 0, sumMZ = 0;
            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVLAdditional;
                sumMX += item.X * item.AxialForceVLAdditional;
                sumMY += item.Y * item.AxialForceVLAdditional;
                sumMZ += item.Z * item.AxialForceVLAdditional;
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        // VL重心を返すメソッド
        public Point3D GetVLGravityCenter()
        {
            double sumW = 0;

            double sumMX = 0;
            double sumMY = 0;
            double sumMZ = 0;

            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVL0;

                sumMX += item.X * item.AxialForceVL0;
                sumMY += item.Y * item.AxialForceVL0;
                sumMZ += item.Z * item.AxialForceVL0;
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        public Point3D GetVLplusVLaddGravityCenter()
        {
            double sumW = 0;

            double sumMX = 0;
            double sumMY = 0;
            double sumMZ = 0;

            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVL0 + item.AxialForceVLAdditional;

                sumMX += item.X * (item.AxialForceVL0 + item.AxialForceVLAdditional);
                sumMY += item.Y * (item.AxialForceVL0 + item.AxialForceVLAdditional);
                sumMZ += item.Z * (item.AxialForceVL0 + item.AxialForceVLAdditional);
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        public Point3D GetCentroid()
        {
            try
            {
                if (PileLayoutItems == null || PileLayoutItems.Count == 0)
                    return new Point3D(0, 0, 0);

                double sumX = 0;
                double sumY = 0;
                double sumZ = 0;
                foreach (var item in PileLayoutItems)
                {
                    sumX += item.X;
                    sumY += item.Y;
                    sumZ += item.Z;
                }

                int count = PileLayoutItems.Count;
                if (count == 0) return new Point3D(0, 0, 0);

                return new Point3D(sumX / count, sumY / count, sumZ / count);
            }
            catch (DivideByZeroException)
            {
                return new Point3D(0, 0, 0);
            }
            catch (InvalidOperationException)
            {
                return new Point3D(0, 0, 0);
            }
        }

        // 軸力の合計を返すメソッド
        public double GetSum(int level, int index)
        {
            double sumLoads = 0;

            if (level == 1)
            {
                sumLoads += PileLayoutItems.Sum(item => item.AxialForceLevel1s[index]);
            }
            else if (level == 2)
            {
                sumLoads += PileLayoutItems.Sum(item => item.AxialForceLevel2s[index]);
            }

            return sumLoads;
        }

        // 転倒モーメント(MNm)を返すメソッド
        public (double, double) GetOverturningMoment(int level, int index) // degree
        {
            double otmX = 0;
            double otmY = 0;

            Point3D gravityCenter = GetVLplusVLaddGravityCenter();

            if (level == 1)
            {
                foreach (var item in PileLayoutItems)
                {
                    double verticalForce = item.AxialForceLevel1s[index];
                    otmX += verticalForce * (item.X - gravityCenter.X) * 0.001;
                    otmY += verticalForce * (item.Y - gravityCenter.Y) * 0.001;
                }
            }
            else if (level == 2)
            {
                foreach (var item in PileLayoutItems)
                {
                    double verticalForce = item.AxialForceLevel2s[index];
                    otmX += verticalForce * (item.X - gravityCenter.X) * 0.001;
                    otmY += verticalForce * (item.Y - gravityCenter.Y) * 0.001;
                }
            }
            return (otmX, otmY);
        }

        // 単位転倒モーメントに対する反力を返すメソッド
        public List<double> GetReactionForUnitMoment(double angle) // degree
        {
            try
            {
                if (PileLayoutItems == null || PileLayoutItems.Count == 0)
                    return [];

                double radian = angle * Math.PI / 180;
                double c = Math.Cos(radian);
                double s = Math.Sin(radian);
                Point3D centroid = GetCentroid();

                var raw = new List<double>();
                double otm = 0;
                foreach (var item in PileLayoutItems)
                {
                    double arm = c * (item.X - centroid.X) + s * (item.Y - centroid.Y);
                    raw.Add(arm);
                    otm += arm * arm;
                }

                if (otm <= 1e-12) // ほぼゼロ
                    return [.. raw.Select(_ => 0.0)];

                return [.. raw.Select(r => r / otm)];
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[InputModel.GetReactionForUnitMoment] angle={Angle}", angle);
                return [];
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve, // 循環参照対応を追加
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals // 
        };

        // データの保存
        public void SaveToFile(string filePath)
        {
            string jsonString = System.Text.Json.JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(filePath, jsonString);
        }

        // データの読み込み
        public static InputModel LoadFromFile(string filePath, MainWindowViewModel mainWindowViewModel)
        {


            if (!File.Exists(filePath))
                throw new FileNotFoundException("指定されたファイルが存在しません。", filePath);

            string json = File.ReadAllText(filePath);

            // まず System.Text.Json で試行（既定設定）
            try
            {
                var loaded = System.Text.Json.JsonSerializer.Deserialize<InputModel>(json, _jsonOptions)
                ?? throw new InvalidOperationException("ファイルの内容をデシリアライズできませんでした。");

                // 旧データとの互換性: InputNodesがnullの場合は空のコレクションを作成
                loaded.InputNodes ??= [];

                // 旧データとの互換性: Element → FoundationBeam への自動変換

                // 旧データとの互換性: Materials/Sections の初期化
                loaded.EnsureFoundationBeamDefaults();
                loaded.EnsureAnalysisTargetDefaults();

                // MainWindowViewModelをセット（Reset()を呼ばない軽量版を使用）
                loaded.AttachViewModel(mainWindowViewModel);
                return loaded;
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is System.Text.Json.JsonException || json.Contains("\"$ref\"") || json.Contains("\"$id\""))
            {
                // フォールバック: Newtonsoft.Json で参照メタデータを復元して読み込む
                try
                {
                    var settings = new Newtonsoft.Json.JsonSerializerSettings
                    {
                        PreserveReferencesHandling = PreserveReferencesHandling.All,
                        TypeNameHandling = TypeNameHandling.Auto,
                        // 復元してよい型をこのアプリの型に限る (細工したファイル対策)
                        SerializationBinder = Common.TrustedTypeBinder.Instance,
                        Formatting = Formatting.Indented,
                    };
                    var loaded = JsonConvert.DeserializeObject<InputModel>(json, settings)
                    ?? throw new InvalidOperationException("Newtonsoft によるデシリアライズで失敗しました。");

                    // 旧データとの互換性: InputNodesがnullの場合は空のコレクションを作成
                    loaded.InputNodes ??= [];

                    // 旧データとの互換性: Element → FoundationBeam への自動変換

                    // 旧データとの互換性: Materials/Sections の初期化
                    loaded.EnsureFoundationBeamDefaults();
                    loaded.EnsureAnalysisTargetDefaults();

                    // MainWindowViewModelをセット（Reset()を呼ばない軽量版を使用）
                    loaded.AttachViewModel(mainWindowViewModel);
                    return loaded;
                }
                catch (Exception ex2)
                {
                    // 最終的に失敗したら元の例外情報を包んで投げる
                    throw new InvalidOperationException("ファイル読み込みに失敗しました（System.Text.Json + Newtonsoft.Json 両方で失敗）。", ex2);
                }

            }
        }

        /// <summary>
        /// ヘッドレス（UI無し）でJSONファイルからInputModelを読み込む。
        /// CLI・バッチ処理用。MainWindowViewModelへの依存なし。
        /// </summary>
        public static InputModel LoadHeadless(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("指定されたファイルが存在しません。", filePath);

            string json = File.ReadAllText(filePath);

            InputModel? loaded = null;
            try
            {
                loaded = System.Text.Json.JsonSerializer.Deserialize<InputModel>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                Log.Information(ex, "[InputModel] STJ deserialize failed, falling back to Newtonsoft.Json");
                // フォールバック: Newtonsoft.Json
                var settings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    PreserveReferencesHandling = PreserveReferencesHandling.All,
                    TypeNameHandling = TypeNameHandling.Auto,
                    // 復元してよい型をこのアプリの型に限る (細工したファイル対策)
                    SerializationBinder = Common.TrustedTypeBinder.Instance,
                    Formatting = Formatting.Indented,
                };
                loaded = JsonConvert.DeserializeObject<InputModel>(json, settings);
            }

            if (loaded == null)
                throw new InvalidOperationException("ファイルの内容をデシリアライズできませんでした。");

            loaded.InputNodes ??= [];
            loaded.EnsureFoundationBeamDefaults();
            loaded.EnsureAnalysisTargetDefaults();

            // PileZ セマンティクス v1 → v2 マイグレーション
            // LoadHeadless は ProjectData レイヤを経由しないため、InputModel 単独ロード = 常に旧形式とみなす。
            loaded.MigratePileZSemantics_v1_to_v2();

            return loaded;
        }

        /// <summary>
        /// FoundationBeamInputのデフォルト値を確保する
        /// </summary>
        internal void EnsureFoundationBeamDefaults()
        {
            // FoundationBeamInputがnullの場合は作成
            FoundationBeamInput ??= new FoundationBeamInput();

            // Materialsが空の場合はデフォルトを追加 — No プロパティ廃止 (位置 = ID)
            FoundationBeamInput.Materials ??= [];
            if (FoundationBeamInput.Materials.Count == 0)
            {
                FoundationBeamInput.Materials.Add(new BeamMaterial
                {
                    Name = "C24",
                    YoungModulus = 2.5e7,
                    ShearModulus = 1.04e7,
                    PoissonRatio = 0.2
                });
            }

            // Sectionsが空の場合はデフォルトを追加 — No プロパティ廃止 (位置 = ID)
            FoundationBeamInput.Sections ??= [];
            if (FoundationBeamInput.Sections.Count == 0)
            {
                FoundationBeamInput.Sections.Add(new BeamSection
                {
                    Name = "G1",
                    Width = 0.8,
                    Height = 2.0
                });
            }

            // 梁要素で参照されている MaterialNo / SectionNo (1-based 位置) に必要な数の
            // 定義がコレクションにあることを保証 (位置 = ID 管理)
            if (FoundationBeamInput.Beams != null)
            {
                int requiredMatCount = FoundationBeamInput.Beams.Count > 0
                    ? FoundationBeamInput.Beams.Max(b => b.MaterialNo) : 0;
                int requiredSecCount = FoundationBeamInput.Beams.Count > 0
                    ? FoundationBeamInput.Beams.Max(b => b.SectionNo) : 0;

                while (FoundationBeamInput.Materials.Count < requiredMatCount)
                {
                    FoundationBeamInput.Materials.Add(new BeamMaterial
                    {
                        Name = $"C24(自動)",
                        YoungModulus = 2.5e7,
                        ShearModulus = 1.04e7,
                        PoissonRatio = 0.2
                    });
                }

                while (FoundationBeamInput.Sections.Count < requiredSecCount)
                {
                    int idx = FoundationBeamInput.Sections.Count + 1;
                    FoundationBeamInput.Sections.Add(new BeamSection
                    {
                        Name = $"G{idx}(自動)",
                        Width = 0.8,
                        Height = 2.0
                    });
                }
            }

            // NodesとBeamsも初期化
            FoundationBeamInput.Nodes ??= [];
            FoundationBeamInput.Beams ??= [];
        }

        /// <summary>
        /// 旧データとの互換性: IsAnalysisTarget が全て false の場合、
        /// IsApplicable の値をコピーして互換動作を維持する
        /// </summary>
        internal void EnsureAnalysisTargetDefaults()
        {
            if (LoadCasesInput == null) return;

            // Level1/Level2 の IsAnalysisTarget が全て false の場合（旧データ）
            bool allLevel1False = LoadCasesInput.LoadCasesLevel1?.All(x => !x.IsAnalysisTarget) ?? true;
            bool allLevel2False = LoadCasesInput.LoadCasesLevel2?.All(x => !x.IsAnalysisTarget) ?? true;

            if (allLevel1False && allLevel2False)
            {
                // 旧データ: IsApplicable の値を IsAnalysisTarget にコピー
                if (LoadCasesInput.LoadCasesLevel1 != null)
                    foreach (var lc in LoadCasesInput.LoadCasesLevel1)
                        lc.IsAnalysisTarget = lc.IsApplicable;
                if (LoadCasesInput.LoadCasesLevel2 != null)
                    foreach (var lc in LoadCasesInput.LoadCasesLevel2)
                        lc.IsAnalysisTarget = lc.IsApplicable;
            }
        }

        /// <summary>
        /// PileLayoutDataItem.Z セマンティクスを v1 (= 杭頭節点 Z) から v2 (= 接合節点 Z) へマイグレートする。
        /// 旧形式: cap_z = pile.Z, joint_z = pile.Z + ΔZc
        /// 新形式: joint_z = pile.Z, cap_z = pile.Z - ΔZc
        /// 変換式: new_Z = old_Z + FoundationBeamDeltaZc
        /// </summary>
        /// <remarks>
        /// AttachViewModel 前 (= イベントハンドラ未購読) に呼び出すこと。SoilPile キャッシュ再構築の連鎖を避ける。
        /// FormatVersion < 2 のロード時に ApplyPostLoadProtocol / LoadHeadless から呼ばれる。
        /// </remarks>
        internal void MigratePileZSemantics_v1_to_v2()
        {
            if (PileLayoutItems == null) return;

            int migratedCount = 0;
            foreach (var pile in PileLayoutItems)
            {
                if (pile == null) continue;
                // pile.Z setter 経由で SetProperty が走るが、AttachViewModel 前なら
                // OnPileLayoutItemPropertyChanged は未購読なので SoilPile キャッシュは触られない。
                pile.Z += pile.FoundationBeamDeltaZc;
                migratedCount++;
            }

            if (migratedCount > 0)
            {
                Log.Information("[Migration] PileZSemantics v1->v2 applied to {Count} piles", migratedCount);
            }
        }

        /// <summary>
        /// 指定された名前の杭体を返すメソッド
        /// </summary>
        /// <param name="pileBodyRef">杭体参照名</param>
        /// <returns>見つかった杭体、またはnull</returns>
        public PileBodyInput? GetPileBodyByPileBodyRef(string pileBodyRef)
        {
            foreach (var pileBody in this.PileBodies)
            {
                if (pileBody.PileBodyRef == pileBodyRef)
                {
                    return pileBody;
                }
            }

            // 見つからなかった場合はnullを返す
            return null;
        }

        /// <summary>
        /// SoilPiles（杭と地盤の組合せ）を生成するメソッド
        /// GenerateSoilPiles 完了後に一括通知（イベント多発を抑制）
        /// </summary>
        public void GenerateSoilPiles()
        {
            // 杭要素分割済みの場合は再生成しない（分割結果を保持）
            if (_mainWindowViewModel?.IsElementSplit == true)
            {
                return;
            }

            _suppressSoilPileNotify = true;
            try
            {
                // ユーザーが編集した GroupPileLoadDia (荷重面等価径) を再生成後も維持するため
                // (groundNo, pileBodyNo, zKey) → 値 のルックアップを保存しておく。
                // GenerateSoilPiles は元々 SoilPile を新規構築するためデフォルト 0 になり、
                // 個別十字荷重 編集が消える問題を回避する。
                var preservedGroupPileLoadDia = new Dictionary<(int groundNo, int pileBodyNo, long zKey), double>();
                // ユーザーが手入力した kh0 の土層ごとオーバーライドも再生成後に維持する。
                var preservedKh0Overrides = new Dictionary<(int groundNo, int pileBodyNo, long zKey), List<Kh0LayerOverride>>();
                if (ElementDivision?.SoilPiles != null)
                {
                    foreach (var oldSp in ElementDivision.SoilPiles)
                    {
                        long zk = (long)Math.Round(oldSp.Z / NumericalConstants.COORDINATE_TOLERANCE);
                        if (oldSp.GroupPileLoadDia > 0.0)
                        {
                            preservedGroupPileLoadDia[(oldSp.GroundNo, oldSp.PileBodyNo, zk)] = oldSp.GroupPileLoadDia;
                        }
                        if (oldSp.Kh0LayerOverrides != null && oldSp.Kh0LayerOverrides.Count > 0)
                        {
                            preservedKh0Overrides[(oldSp.GroundNo, oldSp.PileBodyNo, zk)] =
                                oldSp.Kh0LayerOverrides.Select(o => o.DeepCopy()).ToList();
                        }
                    }
                }

                // O(1) 重複チェック用HashSet（座標は離散化してキー化）
                var usedCombinations = new HashSet<(int groundNo, int pileBodyNo, long zKey)>();
                var newPiles = new List<SoilPile>();

                foreach (PileLayoutDataItem pileLayoutDataItem in PileLayoutItems)
                {
                    int pileBodyNo = pileLayoutDataItem.PileBodyNo;
                    int groundNo = pileLayoutDataItem.GroundNo;
                    // pileTopAltitude は杭頭高さ。v2 セマンティクスでは pile.Z は接合節点 Z なので
                    // PileHeadZ (= pile.Z - ΔZc) を起点にする。
                    double pileTopAltitude = pileLayoutDataItem.PileHeadZ;

                    if (pileBodyNo - 1 < 0 || pileBodyNo - 1 >= PileBodies.Count) continue;
                    if (groundNo - 1 < 0 || groundNo - 1 >= GroundsInput.Count) continue;

                    PileBodies[pileBodyNo - 1].PileBodySegmentsUpdate();
                    var pileBodySegments = PileBodies[pileBodyNo - 1].PileBodySegments;
                    var groundLayerDataItems = GroundsInput[groundNo - 1].GroundLayers;

                    // O(1) 重複チェック（座標を離散化してHashSetで判定）
                    long zKey = (long)Math.Round(pileTopAltitude / NumericalConstants.COORDINATE_TOLERANCE);
                    if (!usedCombinations.Add((groundNo, pileBodyNo, zKey)))
                        continue; // 既に処理済みの組合せ

                    // Z座標リストをList<double>で構築（ObservableCollectionより高速）
                    var zs = new List<double> { pileTopAltitude };
                    foreach (PileBodySegment pileBodySegment in pileBodySegments)
                        zs.Add(pileTopAltitude - pileBodySegment.SegmentDepth);

                    // 杭先端は杭区間境界の最小値（0.5D点追加前に確定）
                    double pileBottomAltitude = zs.Min();

                    // 場所打ち鋼管コンクリート杭の場合、杭頭から0.5Dの位置に分割点を追加
                    // （杭頭部と杭中間部で異なるM-φ関係を適用するため）
                    if (PileBodies[pileBodyNo - 1].PileBodyType == PileTypeNames.InsituSteelPipeConcrete)
                    {
                        // 杭頭区間（鋼管コンクリート部）の杭径を取得
                        var topSection = pileBodySegments
                            .Select(s => s.PileSection)
                            .FirstOrDefault(s => s?.PileSectionType == PileTypeNames.SteelPipeConcreteSection);
                        double pileDia_m = (topSection?.PileDiameter ?? pileBodySegments[0].PileSection?.PileDiameter ?? 0) / 1000.0;
                        if (pileDia_m > 0)
                        {
                            double zHalfD = pileTopAltitude - 0.5 * pileDia_m;
                            // 杭底より上、かつ杭頭より下の場合のみ追加
                            if (zHalfD > pileBottomAltitude + NumericalConstants.COORDINATE_TOLERANCE
                                && zHalfD < pileTopAltitude - NumericalConstants.COORDINATE_TOLERANCE)
                            {
                                zs.Add(zHalfD);
                            }
                        }
                    }

                    // 鋼管杭 + 鉄筋定着工法 の場合、杭頭から D (杭径) の位置に分割点を追加
                    // (杭頭部=コンクリート充填鋼管部 ≒ 杭径分の長さ、それ以下=鋼管部 として M-φ 切替)
                    if (PileBodies[pileBodyNo - 1].PileBodyType == PileTypeNames.SteelPipe)
                    {
                        var topSection = pileBodySegments
                            .Select(s => s.PileSection)
                            .FirstOrDefault(s => s?.PileSectionType == PileTypeNames.CftSection);
                        double pileDia_m = (topSection?.PileDiameter ?? pileBodySegments[0].PileSection?.PileDiameter ?? 0) / 1000.0;
                        if (pileDia_m > 0)
                        {
                            double zD = pileTopAltitude - pileDia_m;
                            if (zD > pileBottomAltitude + NumericalConstants.COORDINATE_TOLERANCE
                                && zD < pileTopAltitude - NumericalConstants.COORDINATE_TOLERANCE)
                            {
                                zs.Add(zD);
                            }
                        }
                    }

                    int glAdded = 0;
                    foreach (GroundLayerInput groundLayerDataItem in groundLayerDataItems)
                    {
                        if (pileTopAltitude > groundLayerDataItem.BottomAltitude && groundLayerDataItem.BottomAltitude > pileBottomAltitude)
                        {
                            zs.Add(groundLayerDataItem.BottomAltitude);
                            glAdded++;
                        }
                    }
                    Log.Debug(
                        "[GenerateSoilPiles] PileBody={PileBodyNo}, Ground={GroundNo}, " +
                        "top={Top:F2}, btm={Btm:F2}, " +
                        "segments={SegCount}, groundLayers={GlCount}, " +
                        "glBoundariesAdded={GlAdded}, totalNodes={NodeCount}",
                        pileBodyNo, groundNo, pileTopAltitude, pileBottomAltitude,
                        pileBodySegments.Count, groundLayerDataItems.Count, glAdded, zs.Count);

                    // トレランス付き重複除去（杭区間境界と地層境界が微小差で重複するケースを防止）
                    zs.Sort((a, b) => b.CompareTo(a)); // 降順ソート
                    var sortedZs = new List<double>(zs.Count);
                    foreach (var z in zs)
                    {
                        if (sortedZs.Count == 0 || Math.Abs(sortedZs[^1] - z) > NumericalConstants.COORDINATE_TOLERANCE)
                            sortedZs.Add(z);
                    }
                    var pileZDataItems = new ObservableCollection<PileZDataItem>();
                    foreach (double sortedZ in sortedZs)
                    {
                        PileZDataItem pileZDataItem = new()
                        {
                            Z = sortedZ,
                            GroundInput = GroundsInput[pileLayoutDataItem.GroundNo - 1],
                        };
                        pileZDataItem.SetSoilDisplacement();
                        pileZDataItems.Add(pileZDataItem);
                    }

                    var sp = new SoilPile()
                    {
                        No = newPiles.Count + 1,
                        GroundNo = groundNo,
                        GroundInput = GroundsInput[groundNo - 1],
                        PileBodyNo = pileBodyNo,
                        PileBodyInput = PileBodies[pileBodyNo - 1],
                        Z = pileTopAltitude,
                        ZDataItems = pileZDataItems
                    };

                    // 旧 SoilPile に保存されていた GroupPileLoadDia (荷重面等価径) を復元
                    long restoreKey = (long)Math.Round(pileTopAltitude / NumericalConstants.COORDINATE_TOLERANCE);
                    if (preservedGroupPileLoadDia.TryGetValue((groundNo, pileBodyNo, restoreKey), out double dia))
                    {
                        sp.GroupPileLoadDia = dia;
                    }

                    // kh0 の土層ごとオーバーライドを復元（UpdateProperties→SetHorizontalSoilReaction より前に設定）
                    if (preservedKh0Overrides.TryGetValue((groundNo, pileBodyNo, restoreKey), out var kh0Ovs))
                    {
                        sp.Kh0LayerOverrides = new ObservableCollection<Kh0LayerOverride>(kh0Ovs);
                    }

                    // 追加: R_* 等の特性を再計算
                    sp.UpdateProperties();

                    newPiles.Add(sp);
                }

                // バッチでObservableCollectionを更新（個別Addの通知コストを回避）
                ElementDivision.SoilPiles = new ObservableCollection<SoilPile>(newPiles);

                ElementDivision.UpdateSoilPileNumberOption();

                // SoilPileAltNo を付与（SoilPilesのルックアップ用Dictionary）
                var soilPileLookup = new Dictionary<(int groundNo, int pileBodyNo, long zKey), int>();
                for (int i = 0; i < ElementDivision.SoilPiles.Count; i++)
                {
                    var sp = ElementDivision.SoilPiles[i];
                    long spZKey = (long)Math.Round(sp.Z / NumericalConstants.COORDINATE_TOLERANCE);
                    soilPileLookup.TryAdd((sp.GroundNo, sp.PileBodyNo, spZKey), i + 1);
                }
                foreach (PileLayoutDataItem pileLayoutDataItem in PileLayoutItems)
                {
                    // SoilPile.Z は杭頭基準なので、ルックアップキーも PileHeadZ で揃える (v2 セマンティクス)
                    long pZKey = (long)Math.Round(pileLayoutDataItem.PileHeadZ / NumericalConstants.COORDINATE_TOLERANCE);
                    if (soilPileLookup.TryGetValue((pileLayoutDataItem.GroundNo, pileLayoutDataItem.PileBodyNo, pZKey), out int altNo))
                    {
                        pileLayoutDataItem.SoilPileAltNo = altNo;
                    }
                }
            }
            finally
            {
                _suppressSoilPileNotify = false;
            }

            // 一括通知は RegenerateSoilPilesAndNotify 側で実施
            // NotifySoilPileChangedForAll();
        }

        // 地盤根入れ生成メソッド
        public void GenerateSoilEmbedment()
        {
            if (ElementDivision.SoilEmbedment == null) { return; }

            int groundNo = EmbedmentInput.GroundNo;
            double embedmentTopAltitude;
            if (EmbedmentInput.EmbedmentLayers.Count != 0)
            {
                embedmentTopAltitude = EmbedmentInput.EmbedmentLayers[0].TopAltitude;
            }
            else
            {
                embedmentTopAltitude = EmbedmentInput.BottomAltitude; // Bottom Altitudeとする。
            }

            ObservableCollection<double> zs = [embedmentTopAltitude];

            foreach (EmbedmentDataItem embedmentDataItem in EmbedmentInput.EmbedmentLayers)
            {
                zs.Add(embedmentDataItem.BottomAltitude);
            }

            double embedmentBottomAltitude = zs[^1];

            if (groundNo - 1 < 0 || groundNo - 1 >= GroundsInput.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(groundNo), "Ground number is out of range.");
            }

            ObservableCollection<GroundLayerInput> groundLayerDataItems = GroundsInput[groundNo - 1].GroundLayers;

            foreach (GroundLayerInput groundLayerDataItem in groundLayerDataItems)
            {
                if (embedmentTopAltitude > groundLayerDataItem.BottomAltitude && groundLayerDataItem.BottomAltitude > embedmentBottomAltitude)
                {
                    // zsが降順に並んでいるため、逆順で反復処理を行います。
                    for (int i = zs.Count - 1; i >= 0; i--)
                    {
                        double z = zs[i];
                        // zがgroundLayerDataItem.BottomAltitudeよりも小さくなった場合
                        if (z < groundLayerDataItem.BottomAltitude)
                        {
                            // zの手前にgroundLayerDataItem.BottomAltitudeを挿入
                            zs.Insert(i, groundLayerDataItem.BottomAltitude);
                            break;
                        }
                    }
                }
            }

            // zsを降順に並び替え
            List<double> sortedZs = [.. zs.Distinct().OrderByDescending(z => z)];
            ObservableCollection<EmbedmentZDataItem> zDataItems = [];
            foreach (double sortedZ in sortedZs)
            {
                EmbedmentZDataItem embedmentZDataItem = new()
                {
                    Z = sortedZ,
                    GroundInput = GroundsInput[EmbedmentInput.GroundNo - 1],
                };
                embedmentZDataItem.SetSoilDisplacement();
                zDataItems.Add(embedmentZDataItem);
            }
            ElementDivision.SoilEmbedment = new(EmbedmentInput.GroundNo, embedmentTopAltitude, zDataItems);
        }

        public InputModel DeepCopy()
        {
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            // Phase C 案 D-2: ハイブリッド手書き Clone。
            // ・既存の手書き DeepCopy がある型はそれを使用 (LoadCasesInput, GroundInput, PileBodyInput, ...)
            // ・PileLayoutItems は QuickClone (108 杭で 3ms)
            // ・FundamentalInput は MemberwiseClone ベースの ShallowCopy (primitive のみのため OK)
            // ・残り (ElementDivision, EmbedmentInput, FoundationBeamInput, InputNodes, Elements, Grids,
            //   PileGroupSettlement) は per-item JSON で複製 — sub-tree 単位なら 1 件あたり 1〜数 ms
            //
            // 大きな InputModel 全体 1 回の JSON serialize (~200ms) を、各サブツリーへの分割で大幅高速化。
            // 解析結果系 (SoilPiles) は ElementDivision 内で個別退避してシリアライズから除外。

            ObservableCollection<SoilPile> savedSoilPiles = null;
            try
            {
                // PropertyChanged を発火しないよう SetSoilPilesSilently 経由で退避・復元する。
                // 公開セッター経由だと DataGrid (ItemsSource バインディング) の再構築が走り、
                // セル編集中の値が 0 にリセットされる問題が発生する (SoilPile.GroupPileLoadDia 編集問題)。
                if (ElementDivision?.SoilPiles != null && ElementDivision.SoilPiles.Count > 0)
                {
                    savedSoilPiles = ElementDivision.SoilPiles;
                    ElementDivision.SetSoilPilesSilently([]);
                }

                var copy = new InputModel
                {
                    UseAnalysisAxialForce = this.UseAnalysisAxialForce,
                    IsAxialForceVariationMode = this.IsAxialForceVariationMode,
                };

                long tStart = swTotal.ElapsedMilliseconds;

                // ── 既存の手書き DeepCopy を使う型 ──
                copy.FundamentalInput = this.FundamentalInput?.ShallowCopy();
                copy.LoadCasesInput = this.LoadCasesInput?.DeepCopy();
                copy.PileBodies = this.PileBodies != null
                    ? new ObservableCollection<PileBodyInput>(this.PileBodies.Select(p => p.DeepCopy()))
                    : null;
                copy.GroundsInput = this.GroundsInput != null
                    ? new ObservableCollection<GroundInput>(this.GroundsInput.Select(g => g.DeepCopy()))
                    : null;
                long tHand = swTotal.ElapsedMilliseconds - tStart;

                // ── PileLayoutItems は QuickClone (高速) ──
                if (_pileLayoutItems != null)
                {
                    var clonedPli = new ObservableCollection<PileLayoutDataItem>();
                    foreach (var pile in _pileLayoutItems)
                        clonedPli.Add(pile.QuickClone());
                    copy._pileLayoutItems = clonedPli;
                }
                long tQc = swTotal.ElapsedMilliseconds - tStart - tHand;

                // ── DeepCopy 未実装型は per-item JSON でクローン ──
                copy.ElementDivision = PileDesign.Common.DeepCopyUtil.CloneJson(this.ElementDivision);
                copy.EmbedmentInput = PileDesign.Common.DeepCopyUtil.CloneJson(this.EmbedmentInput);
                copy.FoundationBeamInput = PileDesign.Common.DeepCopyUtil.CloneJson(this.FoundationBeamInput);
                copy.PileGroupSettlement = PileDesign.Common.DeepCopyUtil.CloneJson(this.PileGroupSettlement);
                copy.InputNodes = PileDesign.Common.DeepCopyUtil.CloneCollectionViaJson(this.InputNodes);
                copy.GridXItems = PileDesign.Common.DeepCopyUtil.CloneCollectionViaJson(this.GridXItems);
                copy.GridYItems = PileDesign.Common.DeepCopyUtil.CloneCollectionViaJson(this.GridYItems);
                long tJsonRest = swTotal.ElapsedMilliseconds - tStart - tHand - tQc;

                // パフォーマンス監視用ログ (Verbose レベル: 通常は出力されない)。
                // 大規模モデルで遅さが気になる場合は Serilog の MinimumLevel を Verbose に下げて確認。
                Serilog.Log.Verbose(
                    "InputModel.DeepCopy total={Total}ms (hand={Hand}, quickClone={Qc}, json-rest={Json}) piles={Piles}",
                    swTotal.ElapsedMilliseconds, tHand, tQc, tJsonRest, _pileLayoutItems?.Count ?? 0);

                return copy;
            }
            finally
            {
                // 退避した SoilPiles を Silent 復元 (PropertyChanged を発火させない)
                if (savedSoilPiles != null && ElementDivision != null)
                {
                    ElementDivision.SetSoilPilesSilently(savedSoilPiles);
                }
                swTotal.Stop();
            }
        }

        // Infinity / NaN を 0 (または任意の代替値) に置換
        private static void CleanFloatingPointSpecials(object obj, int depth = 0, int maxDepth = 4)
        {
            if (obj == null || depth > maxDepth) return;

            var type = obj.GetType();

            // IEnumerable (string 除外) の場合は列挙
            if (obj is System.Collections.IEnumerable en && obj is not string)
            {
                foreach (var item in en)
                {
                    if (item != null && !item.GetType().IsPrimitive)
                        CleanFloatingPointSpecials(item, depth + 1, maxDepth);
                }
                return;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // インデクサ等パラメータ付きプロパティはスキップ
                if (prop.GetIndexParameters().Length > 0) continue;
                if (!prop.CanRead) continue;

                var pt = prop.PropertyType;

                try
                {
                    if (pt == typeof(double) && prop.CanWrite)
                    {
                        double v = (double)(prop.GetValue(obj) ?? 0d);
                        if (double.IsNaN(v) || double.IsInfinity(v))
                        {
                            prop.SetValue(obj, 0d); // 必要なら他の既定値へ
                        }
                    }
                    else if (pt == typeof(float) && prop.CanWrite)
                    {
                        float v = (float)(prop.GetValue(obj) ?? 0f);
                        if (float.IsNaN(v) || float.IsInfinity(v))
                        {
                            prop.SetValue(obj, 0f);
                        }
                    }
                    // 再帰 (クラス/レコード/コレクション)
                    else if (!pt.IsPrimitive && pt != typeof(string))
                    {
                        var child = prop.GetValue(obj);
                        if (child != null)
                            CleanFloatingPointSpecials(child, depth + 1, maxDepth);
                    }
                }
                catch
                {
                    // 失敗は無視（読み込み専用/インデクサ等）
                }
            }
        }

        // デバッグ用: どこに特殊値があるか列挙 (必要時のみ呼ぶ)
        public IEnumerable<string> FindSpecialNumberLocations()
        {
            var list = new List<string>();
            void Scan(object target, string path, int depth)
            {
                if (target == null || depth > 5) return;
                var t = target.GetType();

                if (target is System.Collections.IEnumerable en && target is not string)
                {
                    int i = 0;
                    foreach (var item in en)
                    {
                        Scan(item, $"{path}[{i}]", depth + 1);
                        i++;
                    }
                    return;
                }

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // インデクサ等パラメータ付きプロパティはスキップ
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (!p.CanRead) continue;
                    object val;
                    try { val = p.GetValue(target); } catch { continue; }

                    if (val is double d)
                    {
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            list.Add($"{path}.{p.Name} = {d}");
                    }
                    else if (val is float f)
                    {
                        if (float.IsNaN(f) || float.IsInfinity(f))
                            list.Add($"{path}.{p.Name} = {f}");
                    }
                    else if (val != null && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string))
                    {
                        Scan(val, $"{path}.{p.Name}", depth + 1);
                    }
                }
            }
            Scan(this, "InputModel", 0);
            return list;
        }

        /// <summary>
        /// 節点参照（Type + Guid）から表示用の連番 No を解決します。
        /// 杭は PileLayoutItems の No、一般節点は InputNodes の No、専用節点は FoundationBeamInput.Nodes の No。
        /// 見つからない場合は 0。
        /// </summary>
        public int GetNodeDisplayNo(NodeReferenceType type, Guid id)
        {
            return type switch
            {
                NodeReferenceType.GeneralNode => InputNodes?.FirstOrDefault(n => n.UniqueId == id)?.No ?? 0,
                NodeReferenceType.PileLayout => PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id)?.No ?? 0,
                NodeReferenceType.FoundationNode => FoundationBeamInput?.Nodes.FirstOrDefault(n => n.Id == id)?.No ?? 0,
                _ => 0,
            };
        }

        /// <summary>
        /// 節点参照（Type + Guid）から座標を解決します。
        /// </summary>
        /// <returns>座標が見つかった場合は (X, Y, Z)、見つからない場合は null</returns>
        public (double X, double Y, double Z)? GetNodeCoordinates(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.GeneralNode:
                    var node = InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return node != null ? (node.X, node.Y, node.Z) : null;

                case NodeReferenceType.PileLayout:
                    var pile = PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    if (pile != null)
                    {
                        // 接合節点位置 (v2 セマンティクス: pile.Z は接合節点 Z)
                        double z = pile.Z;
                        return (pile.X, pile.Y, z);
                    }
                    return null;

                case NodeReferenceType.FoundationNode:
                    var fnode = FoundationBeamInput?.Nodes.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? (fnode.X, fnode.Y, fnode.Z) : null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// 梁要素 ComboBox 用の全節点候補リスト (杭配置 + 一般節点 + 基礎梁節点)。
        /// 表示順: P:1..P:N → G:1..G:M → F:1..F:K。
        /// 安定参照を保つため ObservableCollection で保持し、Refresh で内容を更新する。
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<NodeReferenceOption> AvailableNodeReferenceOptions { get; } = [];

        /// <summary>
        /// AvailableNodeReferenceOptions を最新の PileLayoutItems / InputNodes / FoundationBeamInput.Nodes から再構築する。
        /// 既存エントリを再利用できる場合はそのまま (ComboBox の SelectedItem 参照を破壊しない)。
        /// </summary>
        public void RefreshAvailableNodeReferenceOptions()
        {
            var newList = new System.Collections.Generic.List<NodeReferenceOption>();
            if (PileLayoutItems != null)
                foreach (var p in PileLayoutItems)
                    newList.Add(new NodeReferenceOption { Type = NodeReferenceType.PileLayout, Id = p.UniqueId, Display = $"P:{p.PileNo}" });
            if (InputNodes != null)
                foreach (var n in InputNodes)
                    newList.Add(new NodeReferenceOption { Type = NodeReferenceType.GeneralNode, Id = n.UniqueId, Display = $"G:{n.No}" });
            if (FoundationBeamInput?.Nodes != null)
                foreach (var f in FoundationBeamInput.Nodes)
                    newList.Add(new NodeReferenceOption { Type = NodeReferenceType.FoundationNode, Id = f.Id, Display = $"F:{f.No}" });

            // 差分更新: SelectedItem が指すインスタンスを温存しつつ、追加・削除・並び替えに対応
            AvailableNodeReferenceOptions.Clear();
            foreach (var opt in newList) AvailableNodeReferenceOptions.Add(opt);

            // ComboBox の SelectedValue 再マッチングを促す:
            // 初回レンダリング時に ItemsSource が空でマッチに失敗していた場合、
            // ItemsSource 更新後に SelectedValue の PropertyChanged を発火させて再評価させる。
            if (FoundationBeamInput?.Beams != null)
            {
                foreach (var beam in FoundationBeamInput.Beams)
                {
                    beam.OnPropertyChanged(nameof(FoundationBeam.NodeI_Key));
                    beam.OnPropertyChanged(nameof(FoundationBeam.NodeJ_Key));
                }
            }
        }

        /// <summary>
        /// 節点参照（Type + Guid）から表示用の文字列を生成します（例: "G:3", "P:5", "F:2"）。
        /// </summary>
        public string GetNodeReferenceDisplayString(NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.GeneralNode:
                    var node = InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return node != null ? $"G:{node.No}" : "G:?";

                case NodeReferenceType.PileLayout:
                    var pile = PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    return pile != null ? $"P:{pile.PileNo}" : "P:?";

                case NodeReferenceType.FoundationNode:
                    var fnode = FoundationBeamInput?.Nodes.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? $"F:{fnode.No}" : "F:?";

                default:
                    return "?";
            }
        }

        // 追加: Ground / PileBody の番号リストを更新するユーティリティ
        // バッチ置換で更新（Clear+Addだと ComboBox の SelectedItem がリセットされるため）
        public void UpdateCountLists()
        {
            int gCount = Math.Max(1, GroundsInput?.Count ?? 0);
            var newGroundsList = new System.Collections.ObjectModel.ObservableCollection<int>();
            for (int i = 1; i <= gCount; i++)
                newGroundsList.Add(i);
            GroundsInputCountList = newGroundsList;

            int pbCount = Math.Max(1, PileBodies?.Count ?? 0);
            var newPilesList = new System.Collections.ObjectModel.ObservableCollection<int>();
            for (int i = 1; i <= pbCount; i++)
                newPilesList.Add(i);
            PileBodiesCountList = newPilesList;
        }
    }
}

