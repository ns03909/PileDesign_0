using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel — コンストラクタと購読の設定。
    ///
    /// 起動時に一度だけ行うこと。
    ///
    /// <list type="bullet">
    /// <item>画面が使う下位オブジェクトの生成（キャンバスの幾何、docx 出力は遅延生成）</item>
    /// <item>入力モデルの変更を拾うための購読（杭配置・荷重ケース・群杭沈下）</item>
    /// <item>コマンドの可否をボタンへ問い直させる仕掛け</item>
    /// </list>
    ///
    /// 購読のハンドラは<b>フィールドに置く</b>こと。弱参照で保持されるものがあり、
    /// その場で作ったラムダは回収されて通知が来なくなる。
    ///
    /// このファイルは以前 4,542 行あり、名前のとおりのコンストラクタは最後の 9% だけで、
    /// 残りは表示オプションやプロパティパネルの寄せ集めだった。中身を
    /// <c>MainWindowViewModel.PropertyPanel.cs</c>・<c>.LoadSummary.cs</c>・
    /// <c>.DisplayOptions.cs</c> に分け、ここは名前のとおりのものだけにした。
    /// </summary>
    public partial class MainWindowViewModel
    {
        public MainCanvasGeometry CanvasGeometry { get; }

        // docx 出力設定（計算書レベル・Include* フラグ・液状化出力・まとめ方・一括選択・出力前検証）は
        // 専用 ViewModel (DocxOutputViewModel) に分離した。遅延生成で ctor 順序に依存しない。
        private DocxOutputViewModel _docxOutput;
        public DocxOutputViewModel DocxOutput => _docxOutput ??= new DocxOutputViewModel(this);


        // クロススレッドで AnalysisResultContentOption を変更したときに CollectionView が例外を出すのを防ぐための同期ロック
        private readonly object _analysisResultContentOptionLock = new();

        // コンストラクタ //
        /// <summary>
        /// 解析結果コンテンツの正規並び順（水平解析→沈下解析）。
        /// AnalysisResultContentOption はこの順で並ぶように CollectionChanged で自動整列する。
        /// </summary>
        private static readonly List<string> CanonicalAnalysisContentOrder =
        [
            // 水平解析結果
            "梁応力（水平）",
            "節点変位（水平）",
            "地盤反力（水平）",
            "杭頭Mマップ",
            "杭頭Qマップ",
            "接合点Mマップ",
            "接合点Qマップ",
            // 沈下解析結果
            "沈下量",
            "沈下部材角",
            "沈下反力（地盤）",
            "沈下反力（杭頭集約）",
            "沈下応力",
        ];

        private bool _reorderingAnalysisContentOption;

        /// <summary>
        /// 解析結果コンテンツの候補を、決められた順に並べ替える。
        ///
        /// <b>並べ替えの最中に中身が変わりうる。</b> この整列は
        /// <c>CollectionChanged</c> から <c>BeginInvoke</c> で遅らせて呼ばれるので、
        /// 走る頃には候補が増減していることがある。並び順を先に計算してから
        /// <c>Move</c> で当てはめるため、その間に消えた項目は
        /// <c>IndexOf</c> が −1 を返し、<c>Move(-1, i)</c> で落ちる。
        ///
        /// 実際にテストの全体実行が 3 回、これでテストホストごと止まった
        /// (ArgumentOutOfRangeException)。投げ放しの処理なので誰も受け取らない。
        /// 実機でも、解析が終わって候補が入れ替わる瞬間に同じことが起こりうる。
        ///
        /// 毎回の <c>IndexOf</c> で今の位置を取り直し、見つからない項目は飛ばす。
        /// 並べ替えは<b>やり直せる</b> (次の CollectionChanged でまた呼ばれる) ので、
        /// 途中で諦めても順序が崩れたままにはならない。
        /// </summary>
        private void EnsureAnalysisResultContentOrder()
        {
            if (_reorderingAnalysisContentOption) return;
            _reorderingAnalysisContentOption = true;
            try
            {
                lock (_analysisResultContentOptionLock)
                {
                    var sorted = AnalysisResultContentOption
                        .Select(item => (item, idx: CanonicalAnalysisContentOrder.IndexOf(item)))
                        .OrderBy(x => x.idx < 0 ? int.MaxValue : x.idx)
                        .Select(x => x.item)
                        .ToList();

                    ApplyContentOrder(AnalysisResultContentOption, sorted);
                }
            }
            finally { _reorderingAnalysisContentOption = false; }
        }

        /// <summary>
        /// <paramref name="desired"/> の順になるよう <paramref name="live"/> を並べ替える。
        ///
        /// <b><paramref name="desired"/> は古いかもしれない。</b> 並び順を決めてから
        /// 当てはめるまでの間に、別のところが候補を足したり消したりしうる。
        /// 見つからない項目は飛ばし、長さも都度見る。途中で諦めても、
        /// 次の変更でまた呼ばれるので順序は崩れたままにならない。
        ///
        /// 分けてあるのは<b>試せるようにするため</b>。本物のすれ違いは時機次第で
        /// 再現しないが、古い並び順を渡せば同じ状況を作れる。
        /// </summary>
        internal static void ApplyContentOrder(
            System.Collections.ObjectModel.ObservableCollection<string> live,
            System.Collections.Generic.IReadOnlyList<string> desired)
        {
            if (live == null || desired == null) return;

            for (int i = 0; i < desired.Count && i < live.Count; i++)
            {
                int cur = live.IndexOf(desired[i]);
                if (cur < 0) continue;          // 並べ替えの間に消えた
                if (cur != i) live.Move(cur, i);
            }
        }

        /// <summary>
        /// 群杭沈下解析ボタンの可否と、押せない理由 (ツールチップ) を UI 操作のたびに問い直すハンドラ。
        ///
        /// 判定材料 (荷重タイプ・矩形荷重・杭の軸力・土層・荷重面 Z) が多く、
        /// すべての変更に通知を張ると必ずどれか漏れ、<b>押せるはずのボタンが灰色のまま</b>になる。
        /// WPF の <c>CommandManager.RequerySuggested</c> はフォーカス移動やクリックのたびに
        /// 発火するので、これに任せるほうが取りこぼしが無い。
        /// 弱参照で保持されるため、ハンドラはフィールドに置いて回収されないようにする。
        ///
        /// <b>ツールチップも同じ信号で更新する。</b>以前は可否だけをここで問い直しており、
        /// 文言のほうは通知が無かったため、矩形荷重を足したあとも
        /// 「矩形荷重が定義されていません」と出たままになっていた
        /// (ボタンは押せるのに、理由だけが古い)。
        /// </summary>
        private readonly EventHandler _requeryGroupSettlement;

        /// <summary>直近に評価した「実行できない理由」。文言が変わったときだけ通知するために持つ。</summary>
        private string? _lastGroupSettlementBlocker;
        private bool _hasEvaluatedGroupSettlementBlocker;

        public MainWindowViewModel()
        {
            _requeryGroupSettlement = (_, _) => RefreshGroupSettlementGuard();
            System.Windows.Input.CommandManager.RequerySuggested += _requeryGroupSettlement;

            // WPF にクロススレッド変更の同期化を許可（Add/Remove が背景スレッドから来ても UI スレッドへ安全にマーシャル）
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(
                _analysisResultContentOption, _analysisResultContentOptionLock);

            // 解析結果コンテンツ候補の自動整列（CollectionChanged 内で Move すると InvalidOperationException になるため遅延実行）
            _analysisResultContentOption.CollectionChanged += (s, e) =>
            {
                if (_reorderingAnalysisContentOption) return;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                    dispatcher.BeginInvoke(new Action(EnsureAnalysisResultContentOrder));
                else
                    EnsureAnalysisResultContentOrder();
            };

            // Services の初期化
            _fileOperationService = new FileOperationService(_jsonOptions);
            _pileLayoutService = new PileLayoutService();
            _settlementAnalysisService = new SettlementAnalysisService();
            _autoSaveService = new AutoSaveService(_fileOperationService);
            _mruService = new MruService();

            // 自動保存が保存時に参照する「ライブ状態」を提供する。
            // Start 時の固定参照ではなく毎回ここで現在値を返すことで、解析完了後・Undo/Redo 後の
            // 最新状態と「自動保存に解析結果を含める」チェックボックスを保存時点で正しく反映する。
            _autoSaveService.LiveStateProvider = () => (
                CurrentInputModel,
                IsSaveAnalysisResultsAutoSave ? CurrentModel : null,
                IsSaveAnalysisResultsAutoSave ? VerticalBeamCaseResults : null);

            // 自動保存イベントの購読
            _autoSaveService.AutoSaveCompleted += OnAutoSaveCompleted;

            // MRUリスト変更イベントの購読
            _mruService.MruListChanged += OnMruListChanged;

            CurrentInputModel = new InputModel();
            CurrentInputModel.SetMainWindowViewModel(this);

            // ここで各アイテムのPropertyChangedを購読
            foreach (var item in CurrentInputModel.PileLayoutItems)
                item.PropertyChanged += PileLayoutItem_PropertyChanged;
            CurrentInputModel.PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;

            // LoadCase.IsApplicable の変更監視を追加
            SubscribeLoadCaseApplicabilityChanged();

            CanvasGeometry = new MainCanvasGeometry(this);

            UpdateLoadCaseOption();
            //SelectedLoadCaseName = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[0].LoadName;
            if (CurrentInputModel.LoadCasesInput.LoadCasesLevel1?.Count > 0)
                SelectedLoadCaseName = CurrentInputModel.LoadCasesInput.LoadCasesLevel1[0].LoadName;

            // LoadCombinationOptionの初期化
            UpdateLoadCombinationOption();
            //SelectedLoadCombinationName = LoadCombinationNameOption[0];
            if (LoadCombinationNameOption != null && LoadCombinationNameOption.Count > 0)
                SelectedLoadCombinationName = LoadCombinationNameOption[0];

            CanvasThreeDView = new CanvasThreeDView();

            DataGridSettlementSoilLayersCellEditEnding += HandleDataGridSettlementSoilLayersCellEditEnding;

            // 初期化処理
            StatusMessage = "準備完了";

            // 沈下コンター図キャッシュ無効化・群杭沈下 UI プロキシ更新の購読をセットアップ。
            // CurrentInputModel / PileGroupSettlement はファイルロード/Undo/Redo で新インスタンスに
            // 置換されるため、named handler を使って setter から再アタッチできるようにする。
            SubscribeSettlementChanged();

            // コンストラクタ内の適当な位置
            OpenTableWindowCommand = new ToolkitRelayCommand(
                OpenTableWindow,
                () => (LatestResultTables != null && LatestResultTables.Count > 0) ||
                      (VerticalBeamCaseResults != null && VerticalBeamCaseResults.Count > 0) ||
                      HasGroupSettlementBeamAwareCases);

        }

        /// <summary>
        /// 名前の無いセッションとして自動保存を始める。<b>画面が使う ViewModel だけが呼ぶ。</b>
        ///
        /// 以前は「開く」「名前を付けて保存」でしか始めておらず、起動して新規に入力した
        /// だけの状態は 3 分ごとの自動保存も緊急保存も動かなかった。落ちると作業が丸ごと消える。
        ///
        /// かといってコンストラクタで始めると、画面が使わない ViewModel の分まで動く。
        /// 同じ "Untitled_autosave_&lt;秒&gt;.pdj" を取り合い、一時ファイルの作成が
        /// 「別のプロセスが使用中」で落ちる。呼ぶのは <see cref="Views.MainWindow"/> だけ。
        /// </summary>
        internal void BeginAutoSaveSession()
        {
            _autoSaveService.Start(null, CurrentInputModel, null, null);
        }

        private void PileLayoutItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel1s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel2s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVL0) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVLAdditional) ||
                e.PropertyName == nameof(PileLayoutDataItem.X) ||
                e.PropertyName == nameof(PileLayoutDataItem.Y) ||
                e.PropertyName == "Item[]") // ObservableCollection内要素の変更（インデクサ経由）
            {
                UpdateSumAndOTM();
            }
        }

        private void LoadCasesInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCasesInput.LoadCombinations))
            {
                UpdateLoadCombinationOption();
            }
        }

        public void UpdateSumAndOTM()
        {
            // 集計値、OTM、重心、外接範囲を一括通知（配列ループで効率化）
            string[] propertiesToNotify = [
                nameof(Sum1_1), nameof(Sum1_2), nameof(Sum1_3), nameof(Sum1_4),
                nameof(Sum2_1), nameof(Sum2_2), nameof(Sum2_3), nameof(Sum2_4),
                nameof(SumVL0), nameof(SumVLadd), nameof(SumVL),
                nameof(OverturningMoment1_1X), nameof(OverturningMoment1_1Y),
                nameof(OverturningMoment1_2X), nameof(OverturningMoment1_2Y),
                nameof(OverturningMoment1_3X), nameof(OverturningMoment1_3Y),
                nameof(OverturningMoment1_4X), nameof(OverturningMoment1_4Y),
                nameof(OverturningMoment2_1X), nameof(OverturningMoment2_1Y),
                nameof(OverturningMoment2_2X), nameof(OverturningMoment2_2Y),
                nameof(OverturningMoment2_3X), nameof(OverturningMoment2_3Y),
                nameof(OverturningMoment2_4X), nameof(OverturningMoment2_4Y),
                nameof(GravityCenterVL0), nameof(GravityCenterVLadd), nameof(GravityCenterVLPlusVLadd),
                nameof(GroupPileSettlementXMin), nameof(GroupPileSettlementXMax),
                nameof(GroupPileSettlementYMin), nameof(GroupPileSettlementYMax)
            ];

            foreach (var propertyName in propertiesToNotify)
            {
                OnPropertyChanged(propertyName);
            }
        }

        // LoadCombinationOptionの更新メソッド
        private void UpdateLoadCombinationOption()
        {
            var loadCombinationNames = new ObservableCollection<string>();

            foreach (var loadCombination in CurrentInputModel.LoadCasesInput.LoadCombinations)
            {
                loadCombinationNames.Add(loadCombination.GetName());
            }
            LoadCombinationNameOption = loadCombinationNames;

            // 現在の選択値が新オプションに存在しなければ先頭にフォールバック。
            // (factor 変更/Undo/Redo 等で組合せ名が変わった後にコンボボックスが空表示になるのを防ぐ)
            if (loadCombinationNames.Count == 0)
            {
                SelectedLoadCombinationName = null;
            }
            else if (string.IsNullOrEmpty(SelectedLoadCombinationName)
                     || !loadCombinationNames.Contains(SelectedLoadCombinationName))
            {
                SelectedLoadCombinationName = loadCombinationNames[0];
            }
        }

        // DataGridSelectionコピーメソッド
        [RelayCommand]
        private static void CopyDataGridSelection(DataGrid dataGrid)
            => Output.DataGridCsv.CopySelectionToClipboard(dataGrid);

        //
        private void PileLayoutItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (PileLayoutDataItem newItem in e.NewItems)
                    newItem.PropertyChanged += PileLayoutItem_PropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (PileLayoutDataItem oldItem in e.OldItems)
                    oldItem.PropertyChanged -= PileLayoutItem_PropertyChanged;
            }

            // 一括通知
            UpdateSumAndOTM();
            OnPropertyChanged(nameof(PileCountText));
            OnPropertyChanged(nameof(ModelExtent));
            // 杭数 0/>0 の境目で 基礎梁考慮沈下解析 ボタンが活性化／非活性化する
            OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();
        }


        // ===== 群杭沈下 (PileGroupSettlement) 関連の PropertyChanged 購読 =====
        // CurrentInputModel / PileGroupSettlement はファイルロード/Undo/Redo で新インスタンスに置換される。
        // 匿名ラムダだと初期インスタンスにピン留めされ再アタッチできないため named handler 化し、
        // CurrentInputModel setter から SubscribeSettlementChanged() を呼んで再購読する。
        private System.ComponentModel.PropertyChangedEventHandler _inputModelSettlementCacheHandler;
        private System.ComponentModel.PropertyChangedEventHandler _pileGroupSettlementHandler;

        private void SubscribeSettlementChanged()
        {
            if (CurrentInputModel == null) return;

            // InputModel 自体の PropertyChanged: PileGroupSettlement プロパティが別インスタンスに
            // 差し替わった場合にキャッシュ無効化 + 新インスタンスへ再購読
            _inputModelSettlementCacheHandler ??= (sender, e) =>
            {
                if (e.PropertyName == nameof(InputModel.PileGroupSettlement))
                {
                    IsSettlementGridCacheValid = false;
                    ResubscribePileGroupSettlement();
                }
            };
            CurrentInputModel.PropertyChanged -= _inputModelSettlementCacheHandler;
            CurrentInputModel.PropertyChanged += _inputModelSettlementCacheHandler;

            ResubscribePileGroupSettlement();
        }

        private void ResubscribePileGroupSettlement()
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs == null) return;

            _pileGroupSettlementHandler ??= (sender, e) =>
            {
                if (e.PropertyName == nameof(PileGroupSettlement.SettlementGridX) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridY) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridData))
                {
                    IsSettlementGridCacheValid = false;
                }

                // 土層上端 が変わったら 各層の Thickness を再計算
                if (e.PropertyName == nameof(PileGroupSettlement.SoilLayersTopAltitude))
                {
                    UpdateSettlementSoilLayer();
                }

                // LoadingType が外部から変更されたら 2 段 ComboBox プロキシを更新
                if (e.PropertyName == nameof(PileGroupSettlement.LoadingType))
                {
                    OnPropertyChanged(nameof(GroupSettlementBeamSelector));
                    OnPropertyChanged(nameof(GroupSettlementLoadType));
                    OnPropertyChanged(nameof(GroupSettlementLoadTypeOptions));
                    OnPropertyChanged(nameof(IsManualRectLoadEditingEnabled));
                }

                // 例題ロード等で外部から荷重面標高が変わったら、TextBox バインド先 (プロキシ) を更新
                if (e.PropertyName == nameof(PileGroupSettlement.LoadingPlaneAltitudeNonBeam))
                {
                    OnPropertyChanged(nameof(LoadingPlaneAltitudeNonBeamProxy));
                }
                if (e.PropertyName == nameof(PileGroupSettlement.LoadingPlaneAltitudeBeamAware))
                {
                    OnPropertyChanged(nameof(LoadingPlaneAltitudeBeamAwareProxy));
                }
            };
            pgs.PropertyChanged -= _pileGroupSettlementHandler;
            pgs.PropertyChanged += _pileGroupSettlementHandler;
        }

        // 追加: IsApplicable 変更監視の購読セットアップ
        // 重複登録防止のため、ハンドラを named field に置き換え -= でクリーン後に += する。
        // CurrentInputModel 置換 (Undo/Redo / ファイルロード / LoadCaseWindow.Save) 時にも
        // 同じハンドラを再アタッチできるようにする。
        private NotifyCollectionChangedEventHandler _loadCasesLevel1ChangedHandler;
        private NotifyCollectionChangedEventHandler _loadCasesLevel2ChangedHandler;
        private NotifyCollectionChangedEventHandler _loadCombinationsChangedHandler;

        private void SubscribeLoadCaseApplicabilityChanged()
        {
            var lci = CurrentInputModel.LoadCasesInput;
            if (lci == null) return;

            void attach(IEnumerable<LoadCase> cases)
            {
                if (cases == null) return;
                foreach (var lc in cases)
                {
                    lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                    lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                }
            }

            attach(lci.LoadCasesLevel1);
            attach(lci.LoadCasesLevel2);

            // 旧購読を解除 (古い CurrentInputModel の collection は別インスタンスなので無害だが、
            // 同一インスタンスで複数回呼ばれた場合の重複発火を防ぐ)
            if (_loadCasesLevel1ChangedHandler != null)
                lci.LoadCasesLevel1.CollectionChanged -= _loadCasesLevel1ChangedHandler;
            if (_loadCasesLevel2ChangedHandler != null)
                lci.LoadCasesLevel2.CollectionChanged -= _loadCasesLevel2ChangedHandler;
            if (_loadCombinationsChangedHandler != null)
                lci.LoadCombinations.CollectionChanged -= _loadCombinationsChangedHandler;

            _loadCasesLevel1ChangedHandler = (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            _loadCasesLevel2ChangedHandler = (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            _loadCombinationsChangedHandler = (s, e) =>
            {
                // 組合せが UI に影響する場合に再構築
                UpdateLoadCombinationOption();
            };

            lci.LoadCasesLevel1.CollectionChanged += _loadCasesLevel1ChangedHandler;
            lci.LoadCasesLevel2.CollectionChanged += _loadCasesLevel2ChangedHandler;
            lci.LoadCombinations.CollectionChanged += _loadCombinationsChangedHandler;
        }

        // 追加: IsApplicable 変更時にオプション更新
        private void LoadCase_PropertyChanged_ForOption(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LoadCase.IsApplicable))
            {
                UpdateLoadCaseOption();
                // 現在選択が非適用になったときのフォールバック
                if (!LoadCaseNameOption.Contains(SelectedLoadCaseName))
                {
                    SelectedLoadCaseName = LoadCaseNameOption.FirstOrDefault() ?? "VL";
                }
            }
        }

        // 既存: LoadCaseOptionの更新
        private void UpdateLoadCaseOption()
        {
            var loadCaseNames = new ObservableCollection<string>();
            var allLoadCases = CurrentInputModel.LoadCasesInput.AllLoadCases;

            // IsApplicable=true のみ表示したい場合は以下のフィルタを有効化
            foreach (var loadCase in allLoadCases.Where(lc => lc.IsApplicable))
                loadCaseNames.Add(loadCase.GetLoadName());

            // IsApplicable 無視して全件表示したいなら上の Where を外す

            LoadCaseNameOption = loadCaseNames;

            // 現在の選択値が新オプションに存在しなければ先頭にフォールバック
            if (loadCaseNames.Count == 0)
            {
                SelectedLoadCaseName = null;
            }
            else if (string.IsNullOrEmpty(SelectedLoadCaseName)
                     || !loadCaseNames.Contains(SelectedLoadCaseName))
            {
                SelectedLoadCaseName = loadCaseNames[0];
            }
        }
    }
}
