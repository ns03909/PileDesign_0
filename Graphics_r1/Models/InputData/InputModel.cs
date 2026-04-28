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

namespace PileDesign.Models.InputData
{
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
        private ObservableCollection<int> _groundsInputCountList = new();
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
        private ObservableCollection<int> _pileBodiesCountList = new();
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
            set => SetProperty(ref _foundationBeamInput, value);
        }

        // 杭軸力モード: 入力値＋応力解析結果を使用するか
        private bool _useAnalysisAxialForce = false;
        public bool UseAnalysisAxialForce
        {
            get => _useAnalysisAxialForce;
            set => SetProperty(ref _useAnalysisAxialForce, value);
        }

        // 一般節点
        private ObservableCollection<InputNode> _inputNodes;
        public ObservableCollection<InputNode> InputNodes
        {
            get => _inputNodes;
            set => SetProperty(ref _inputNodes, value);
        }

        // クラス内フィールドに追加
        private bool _suppressSoilPileNotify;

        // Phase 1: SoilPile キャッシュ最適化
        private Dictionary<(int groundNo, int pileBodyNo, double z), SoilPile> _soilPileCache = new();
        private bool _soilPileCacheValid = false;

        // Phase 2: デバウンス
        private System.Windows.Threading.DispatcherTimer? _regenerateDebounceTimer;
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
            _soilPileCacheValid = false;
        }

        /// <summary>
        /// 必要に応じて SoilPile キャッシュを再構築します。
        /// </summary>
        private void RebuildSoilPileCacheIfNeeded()
        {
            if (_soilPileCacheValid) return;

            _soilPileCache.Clear();
            if (ElementDivision?.SoilPiles == null) return;

            foreach (var sp in ElementDivision.SoilPiles)
            {
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
        /// </summary>
        public SoilPile? LookupSoilPile(int groundNo, int pileBodyNo, double z)
        {
            RebuildSoilPileCacheIfNeeded();

            double zKey = Math.Round(z / NumericalConstants.COORDINATE_TOLERANCE)
                          * NumericalConstants.COORDINATE_TOLERANCE;
            var key = (groundNo, pileBodyNo, zKey);

            return _soilPileCache.TryGetValue(key, out var result) ? result : null;
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

        // 要素分割
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
            Elements = [];
            GridXItems = [];
            GridYItems = [];

            // 基礎梁入力データの初期化（デフォルト材料・断面を作成）
            FoundationBeamInput = new FoundationBeamInput();
            FoundationBeamInput.Materials.Add(new BeamMaterial
            {
                No = 1,
                Name = "C24",
                YoungModulus = 2.5e7,
                ShearModulus = 1.04e7,
                PoissonRatio = 0.2
            });
            FoundationBeamInput.Sections.Add(new BeamSection
            {
                No = 1,
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

        // 要素
        private ObservableCollection<Element> _elements;
        public ObservableCollection<Element> Elements
        {
            get => _elements;
            set => SetProperty(ref _elements, value);
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
            catch (InvalidOperationException ex)
            {
                return 0.0;
            }
            catch (ArgumentNullException ex)
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
            catch (InvalidOperationException ex)
            {
                return 0.0;
            }
            catch (ArgumentNullException ex)
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
            catch (OverflowException ex)
            {
                return 0.0;
            }
        }

        // VLadd重心を返すメソッド
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
            catch (DivideByZeroException ex)
            {
                return new Point3D(0, 0, 0);
            }
            catch (InvalidOperationException ex)
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
                    return new List<double>();

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

                return raw.Select(r => r / otm).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[InputModel.GetReactionForUnitMoment] angle={Angle}", angle);
                return new List<double>();
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

                // 旧データとの互換性: Element → FoundationBeamElement への自動変換
                loaded.MigrateElementsToFoundationBeams();

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
                        Formatting = Formatting.Indented,
                    };
                    var loaded = JsonConvert.DeserializeObject<InputModel>(json, settings)
                    ?? throw new InvalidOperationException("Newtonsoft によるデシリアライズで失敗しました。");

                    // 旧データとの互換性: InputNodesがnullの場合は空のコレクションを作成
                    loaded.InputNodes ??= [];

                    // 旧データとの互換性: Element → FoundationBeamElement への自動変換
                    loaded.MigrateElementsToFoundationBeams();

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
                    Formatting = Formatting.Indented,
                };
                loaded = JsonConvert.DeserializeObject<InputModel>(json, settings);
            }

            if (loaded == null)
                throw new InvalidOperationException("ファイルの内容をデシリアライズできませんでした。");

            loaded.InputNodes ??= [];
            loaded.MigrateElementsToFoundationBeams();
            loaded.EnsureFoundationBeamDefaults();
            loaded.EnsureAnalysisTargetDefaults();
            return loaded;
        }

        /// <summary>
        /// FoundationBeamInputのデフォルト値を確保する
        /// </summary>
        internal void EnsureFoundationBeamDefaults()
        {
            // FoundationBeamInputがnullの場合は作成
            FoundationBeamInput ??= new FoundationBeamInput();

            // Materialsが空の場合はデフォルトを追加
            FoundationBeamInput.Materials ??= [];
            if (FoundationBeamInput.Materials.Count == 0)
            {
                FoundationBeamInput.Materials.Add(new BeamMaterial
                {
                    No = 1,
                    Name = "C24",
                    YoungModulus = 2.5e7,
                    ShearModulus = 1.04e7,
                    PoissonRatio = 0.2
                });
            }

            // Sectionsが空の場合はデフォルトを追加
            FoundationBeamInput.Sections ??= [];
            if (FoundationBeamInput.Sections.Count == 0)
            {
                FoundationBeamInput.Sections.Add(new BeamSection
                {
                    No = 1,
                    Name = "G1",
                    Width = 0.8,
                    Height = 2.0
                });
            }

            // 梁要素で使用されている材料No・断面Noに対応する定義がない場合は追加
            if (FoundationBeamInput.Beams != null)
            {
                var existingMatNos = new HashSet<int>(FoundationBeamInput.Materials.Select(m => m.No));
                var existingSecNos = new HashSet<int>(FoundationBeamInput.Sections.Select(s => s.No));

                foreach (var beam in FoundationBeamInput.Beams)
                {
                    if (!existingMatNos.Contains(beam.MaterialNo))
                    {
                        FoundationBeamInput.Materials.Add(new BeamMaterial
                        {
                            No = beam.MaterialNo,
                            Name = $"C24(自動)",
                            YoungModulus = 2.5e7,
                            ShearModulus = 1.04e7,
                            PoissonRatio = 0.2
                        });
                        existingMatNos.Add(beam.MaterialNo);
                    }

                    if (!existingSecNos.Contains(beam.SectionNo))
                    {
                        FoundationBeamInput.Sections.Add(new BeamSection
                        {
                            No = beam.SectionNo,
                            Name = $"G{beam.SectionNo}(自動)",
                            Width = 0.8,
                            Height = 2.0
                        });
                        existingSecNos.Add(beam.SectionNo);
                    }
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
        /// 旧形式のElementデータをFoundationBeamInputに自動変換する
        /// </summary>
        internal void MigrateElementsToFoundationBeams()
        {
            // 変換条件チェック: Elementsが存在し、FoundationBeamInputが空の場合のみ変換
            if (Elements == null || Elements.Count == 0) return;
            if (FoundationBeamInput == null) FoundationBeamInput = new FoundationBeamInput();
            if (FoundationBeamInput.Nodes.Count > 0 || FoundationBeamInput.Beams.Count > 0) return;

            try
            {
                var nodeDict = new Dictionary<string, FoundationNode>(); // 座標をキーにノードを管理
                int nodeCounter = 1;

                // Elementから節点と梁要素を変換
                foreach (var element in Elements)
                {
                    if (element.Nodes == null || element.Nodes.Count < 2) continue;

                    // 始点ノード変換
                    var startNode = element.Nodes[0];
                    string startKey = $"{startNode.X:F6},{startNode.Y:F6},{startNode.Z:F6}";
                    if (!nodeDict.ContainsKey(startKey))
                    {
                        var newNode = new FoundationNode
                        {
                            No = nodeCounter++,
                            X = startNode.X,
                            Y = startNode.Y,
                            Z = startNode.Z,
                            Name = $"Node_{startNode.No}"
                        };
                        nodeDict[startKey] = newNode;
                        FoundationBeamInput.Nodes.Add(newNode);
                    }

                    // 終点ノード変換
                    var endNode = element.Nodes[1];
                    string endKey = $"{endNode.X:F6},{endNode.Y:F6},{endNode.Z:F6}";
                    if (!nodeDict.ContainsKey(endKey))
                    {
                        var newNode = new FoundationNode
                        {
                            No = nodeCounter++,
                            X = endNode.X,
                            Y = endNode.Y,
                            Z = endNode.Z,
                            Name = $"Node_{endNode.No}"
                        };
                        nodeDict[endKey] = newNode;
                        FoundationBeamInput.Nodes.Add(newNode);
                    }

                    // 梁要素変換
                    var beam = new FoundationBeamElement
                    {
                        No = FoundationBeamInput.Beams.Count + 1,
                        NodeI_No = nodeDict[startKey].No,
                        NodeJ_No = nodeDict[endKey].No,
                        MaterialNo = 1,
                        SectionNo = 1,
                        Width = 0.5,  // デフォルト値
                        Height = 0.8, // デフォルト値
                        SectionName = "Default"
                    };
                    FoundationBeamInput.Beams.Add(beam);
                }

                // 旧データ変換した梁要素は MaterialNo=1 / SectionNo=1 を参照するため、参照先を保証
                if (FoundationBeamInput.Beams.Count > 0)
                {
                    FoundationBeamInput.EnsureDefaultMaterialAndSection();
                }

                // 変換成功メッセージ
                if (FoundationBeamInput.Nodes.Count > 0)
                {
                    System.Windows.MessageBox.Show(
                        $"旧形式のデータを新しい形式に変換しました。\n" +
                        $"節点: {FoundationBeamInput.Nodes.Count} 個\n" +
                        $"梁要素: {FoundationBeamInput.Beams.Count} 個",
                        "データ変換完了",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                // 変換後、Elementsをクリア（ただしコレクション自体は残す）
                Elements.Clear();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"データ変換中にエラーが発生しました: {ex.Message}",
                    "変換エラー",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
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
        /// SoilPiles（杭と地盤の組み合わせ）を生成するメソッド
        /// GenerateSoilPiles 完了後に一括通知（イベント多発を抑制）
        /// </summary>
        public void GenerateSoilPiles()
        {
            // 要素分割済みの場合は再生成しない（分割結果を保持）
            if (_mainWindowViewModel?.IsElementSplit == true)
            {
                return;
            }

            _suppressSoilPileNotify = true;
            try
            {
                // O(1) 重複チェック用HashSet（座標は離散化してキー化）
                var usedCombinations = new HashSet<(int groundNo, int pileBodyNo, long zKey)>();
                var newPiles = new List<SoilPile>();

                foreach (PileLayoutDataItem pileLayoutDataItem in PileLayoutItems)
                {
                    int pileBodyNo = pileLayoutDataItem.PileBodyNo;
                    int groundNo = pileLayoutDataItem.GroundNo;
                    double pileTopAltitude = pileLayoutDataItem.Point3D.Z;

                    if (pileBodyNo - 1 < 0 || pileBodyNo - 1 >= PileBodies.Count) continue;
                    if (groundNo - 1 < 0 || groundNo - 1 >= GroundsInput.Count) continue;

                    PileBodies[pileBodyNo - 1].PileBodySegmentsUpdate();
                    var pileBodySegments = PileBodies[pileBodyNo - 1].PileBodySegments;
                    var groundLayerDataItems = GroundsInput[groundNo - 1].GroundLayers;

                    // O(1) 重複チェック（座標を離散化してHashSetで判定）
                    long zKey = (long)Math.Round(pileTopAltitude / NumericalConstants.COORDINATE_TOLERANCE);
                    if (!usedCombinations.Add((groundNo, pileBodyNo, zKey)))
                        continue; // 既に処理済みの組み合わせ

                    // Z座標リストをList<double>で構築（ObservableCollectionより高速）
                    var zs = new List<double> { pileTopAltitude };
                    foreach (PileBodySegment pileBodySegment in pileBodySegments)
                        zs.Add(pileTopAltitude - pileBodySegment.SegmentDepth);

                    // 杭先端は杭区間境界の最小値（0.5D点追加前に確定）
                    double pileBottomAltitude = zs.Min();

                    // 場所打ち鋼管コンクリート杭の場合、杭頭から0.5Dの位置に分割点を追加
                    // （杭頭部と杭中間部で異なるM-φ関係を適用するため）
                    if (PileBodies[pileBodyNo - 1].PileBodyType == "場所打ち鋼管コンクリート杭")
                    {
                        // 杭頭区間（鋼管コンクリート部）の杭径を取得
                        var topSection = pileBodySegments
                            .Select(s => s.PileSection)
                            .FirstOrDefault(s => s?.PileSectionType == "鋼管コンクリート部");
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
                    long pZKey = (long)Math.Round(pileLayoutDataItem.Point3D.Z / NumericalConstants.COORDINATE_TOLERANCE);
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
            // 解析結果（SoilPiles）を一時退避してシリアライズ対象から除外（OOM対策）
            ObservableCollection<SoilPile> savedSoilPiles = null;
            try
            {
                if (ElementDivision?.SoilPiles != null && ElementDivision.SoilPiles.Count > 0)
                {
                    savedSoilPiles = ElementDivision.SoilPiles;
                    ElementDivision.SoilPiles = [];
                }

                return PileDesign.Common.DeepCopyUtil.CloneJson(this);
            }
            finally
            {
                // 退避したSoilPilesを復元
                if (savedSoilPiles != null && ElementDivision != null)
                {
                    ElementDivision.SoilPiles = savedSoilPiles;
                }
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
                        // 杭頭 + FoundationBeamDeltaZc の位置
                        double z = pile.Z + pile.FoundationBeamDeltaZc;
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

