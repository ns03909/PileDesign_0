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
    /// MainWindowViewModel — 画面の表示オプション。
    ///
    /// リボンやステータスバーの表示状態、何を描くか（応力・変位・地盤値・作用点…）、
    /// 描く大きさ（矢印・吹き出し・線の太さ・図の倍率）、そして
    /// 「どの解析結果を表示するか」の選択（荷重ケース・方向・内容とそのサブ選択）。
    ///
    /// ほとんどが <c>SetProperty</c> で通知するだけの状態だが、
    /// 選択肢どうしが連動するもの（表示する内容とサブ選択、排他のトグル）は
    /// setter で辻褄を合わせている。
    ///
    /// 以前は <c>MainWindowViewModel.Constructor.cs</c> という名前で 4,542 行あり、
    /// コンストラクタ・プロパティパネル・集計もここに混ざっていた。
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        // ステータスメッセージ
        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    StatusMessageColor = value == "要素追加モード(解除: [Esc], [Alt]+[1])" ? Brushes.Red : Brushes.Black;
                }
            }
        }

        private AnaModel _currentModel;
        public AnaModel CurrentModel
        {
            get => _currentModel;
            set
            {
                if (SetProperty(ref _currentModel, value))
                {
                    // 結果が差し替わったら、選んでいる液状化の状態に結果があるかを見直す。
                    // 結果を引く関数は逆の状態の結果を黙って返さなくなったので (FEM.ResultLookup)、
                    // 荷重ケースの選択が変わらないまま再解析で片方の状態だけになると、切替が結果の無い側に残って何も描かれない
                    AutoDetectLiquefactionState();
                    OnPropertyChanged(nameof(IsCurrentNonLiquefactionAnalyzed));
                    OnPropertyChanged(nameof(IsCurrentLiquefactionAnalyzed));
                }
            }
        }

        private Brush _statusMessageColor = Brushes.Black;
        public Brush StatusMessageColor
        {
            get => _statusMessageColor;
            set => SetProperty(ref _statusMessageColor, value);
        }

        private int _selectedGroundInputModelNo = 1;
        public int SelectedGroundInputModelNo
        {
            get => _selectedGroundInputModelNo;
            set => SetProperty(ref _selectedGroundInputModelNo, value);
        }

        public ObservableCollection<RectLoad> RectLoads
        {
            get => CurrentInputModel!.PileGroupSettlement.RectLoads;
            set
            {
                if (!ReferenceEquals(CurrentInputModel!.PileGroupSettlement.RectLoads, value))
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads = value ?? [];
                    OnPropertyChanged(nameof(RectLoads));
                    RequestUpdateWindow();
                }
            }
        }

        [ObservableProperty] // レベル1地震時軸力
        public bool _isElastic;

        public CanvasThreeDView CanvasThreeDView { get; set; }

        private ObservableCollection<int> _labelSizeOption = new(Enumerable.Range(7, 14)); // 7 to 20
        public ObservableCollection<int> LabelSizeOption
        {
            get => _labelSizeOption;
            set => SetProperty(ref _labelSizeOption, value);
        }

        // MRUリスト
        public ObservableCollection<MruItem> MruItems { get; } = [];

        // Undo/Redo状態表示
        public string UndoRedoStatusText => _undoManager != null
            ? $"元に戻す: {(_undoManager.CanUndo ? "可能" : "不可")} | やり直し: {(_undoManager.CanRedo ? "可能" : "不可")}"
            : "";

        // Undo/Redoツールチップ（操作名付き）
        public string UndoToolTip
        {
            get
            {
                var desc = _undoManager?.PeekUndoDescription;
                return string.IsNullOrEmpty(desc) ? "元に戻す (Ctrl+Z)" : $"元に戻す: {desc} (Ctrl+Z)";
            }
        }

        public string RedoToolTip
        {
            get
            {
                var desc = _undoManager?.PeekRedoDescription;
                return string.IsNullOrEmpty(desc) ? "やり直し (Ctrl+Y)" : $"やり直し: {desc} (Ctrl+Y)";
            }
        }

        // 杭本数表示
        public string PileCountText => CurrentInputModel?.PileLayoutItems != null
            ? $"杭本数: {CurrentInputModel.PileLayoutItems.Count}本"
            : "杭本数: 0本";

        // 選択数表示
        public string SelectionCountText
        {
            get
            {
                int piles = CurrentInputModel?.PileLayoutItems?.Count(p => p.IsSelected) ?? 0;
                int nodes = CurrentInputModel?.InputNodes?.Count(n => n.Type == Models.InputData.NodeType.General && n.IsSelected) ?? 0;
                int beams = CurrentInputModel?.FoundationBeamInput?.Beams?.Count(b => b.IsSelected) ?? 0;
                int total = piles + nodes + beams;
                if (total == 0) return "";
                var parts = new List<string>();
                if (piles > 0) parts.Add($"杭{piles}");
                if (nodes > 0) parts.Add($"節点{nodes}");
                if (beams > 0) parts.Add($"梁{beams}");
                return $"選択: {string.Join(", ", parts)}";
            }
        }


        // マウス座標表示
        private string _mouseCoordinateText = "";
        public string MouseCoordinateText
        {
            get => _mouseCoordinateText;
            set => SetProperty(ref _mouseCoordinateText, value);
        }

        // ズーム倍率表示
        public string ZoomText => CanvasThreeDView != null
            ? $"×{CanvasThreeDView.Scale:F1}"
            : "";

        // 解析状態表示 (1 行プレーンテキスト。ステータスバーのツールチップに使う)
        public string AnalysisStatusText
        {
            get
            {
                var items = AnalysisStatusItems;
                if (items.Count == 0) return "未解析";

                // ✓ の付いていない項目 = これから行うもの。何をすればよいかを添える。
                var pending = items.Where(s => !s.Text.EndsWith(" ✓", System.StringComparison.Ordinal))
                                   .Select(s => s.Text)
                                   .ToList();

                string line = string.Join(" | ", items.Select(s => s.Text));
                return pending.Count == 0
                    ? line
                    : line + "\n\n灰色の項目はまだ実行していません: " + string.Join("、", pending);
            }
        }

        /// <summary>
        /// ステータスバー表示用: 解析項目ごとの実施状態 + 色種別。
        /// 色種別 (Color フィールド) は "Success" / "Info" / "Warning" / "Inactive" のいずれか。
        /// XAML 側で DynamicResource (StatusSuccessBrush / StatusInfoBrush / StatusWarningDarkBrush) と紐付ける。
        /// リボンボタンの ✓ 色と統一: 杭要素分割・水平解析=Success、単杭沈下=Info、群杭沈下=Warning。
        /// </summary>
        public List<AnalysisStatusItem> AnalysisStatusItems
        {
            get
            {
                var items = new List<AnalysisStatusItem>();

                // 必ず通る 2 段は、未完了でも灰色で先に並べる。
                // 完了したものだけを出していると「次に何をすればよいか」が読み取れない。
                // (Color が既知の値でなければテンプレート側の既定色 = 灰色になる)
                items.Add(IsElementSplit
                    ? new() { Text = "杭要素分割 ✓", Color = "SkyBlue" }
                    : new() { Text = "杭要素分割", Color = "Inactive" });
                items.Add(IsHorizontalAnalysisDone
                    ? new() { Text = "水平解析 ✓", Color = "Success" }
                    : new() { Text = "水平解析", Color = "Inactive" });

                // 沈下系は必須ではないので、実行したものだけを出す。
                if (IsVerticalAnalysisDone)
                    items.Add(new() { Text = "単杭沈下解析 ✓", Color = "Info" });
                if (IsVerticalBeamAnalysisDone)
                    items.Add(new() { Text = "単杭沈下解析（基礎梁考慮） ✓", Color = "Info" });

                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs?.CaseRecords?.Any(r => !r.IsBeamAware) == true)
                    items.Add(new() { Text = "群杭沈下解析（基礎梁なし） ✓", Color = "Warning" });
                if (pgs?.CaseRecords?.Any(r => r.IsBeamAware) == true)
                    items.Add(new() { Text = "群杭沈下解析（基礎梁あり） ✓", Color = "Warning" });
                return items;
            }
        }

        /// <summary>ステータスバー表示用 1 項目: 表示テキスト + 色種別 (Success/Info/Warning)。</summary>
        public class AnalysisStatusItem
        {
            public string Text { get; set; } = "";
            public string Color { get; set; } = "Inactive";
        }

        // 直近の解析完了時刻 (ステータスバー表示用)。各解析の完了処理から SetLatestAnalysisCompleted() で更新。
        private DateTime? _lastAnalysisTime;
        public DateTime? LastAnalysisTime
        {
            get => _lastAnalysisTime;
            private set
            {
                if (SetProperty(ref _lastAnalysisTime, value))
                    OnPropertyChanged(nameof(LastAnalysisTimeText));
            }
        }

        public string LastAnalysisTimeText => _lastAnalysisTime is { } t
            ? $"最終解析: {t:HH:mm:ss}"
            : "";

        /// <summary>
        /// 各解析 (水平 / 沈下 / 梁鉛直) が完了したときに呼び出す。ステータスバーに完了時刻を表示する。
        /// </summary>
        public void SetLatestAnalysisCompleted()
        {
            LastAnalysisTime = DateTime.Now;

            // 解析には時間がかかる。入力を編集していなくても、
            // 結果を保存せずに閉じようとしたら確認する。
            MarkUnsavedWork();
        }

        // 直近の自動保存状態表示 (ステータスバー用)。OnAutoSaveCompleted から更新。
        // StatusMessage を上書きしないことで一過性メッセージとの衝突を避ける。
        private string _lastAutoSaveText = "";
        public string LastAutoSaveText
        {
            get => _lastAutoSaveText;
            private set => SetProperty(ref _lastAutoSaveText, value);
        }

        private string _lastAutoSaveToolTip = "自動保存の最終結果（失敗時は赤）";
        /// <summary>自動保存の表示のツールチップ。失敗時は理由と次の試行を出す。</summary>
        public string LastAutoSaveToolTip
        {
            get => _lastAutoSaveToolTip;
            private set => SetProperty(ref _lastAutoSaveToolTip, value);
        }

        private Brush _lastAutoSaveBrush = Brushes.Gray;
        public Brush LastAutoSaveBrush
        {
            get => _lastAutoSaveBrush;
            private set => SetProperty(ref _lastAutoSaveBrush, value);
        }

        // リボン最小化
        private bool _isRibboNMinimized;
        public bool IsRibboNMinimized
        {
            get => _isRibboNMinimized;
            set => SetProperty(ref _isRibboNMinimized, value);
        }

        // リボン表示/非表示
        private bool _isRibbonVisible = true;
        public bool IsRibbonVisible
        {
            get => _isRibbonVisible;
            set => SetProperty(ref _isRibbonVisible, value);
        }

        // 入力ビジュアライザー表示/非表示
        private bool _isInputVisualizerVisible = false;
        public bool IsInputVisualizerVisible
        {
            get => _isInputVisualizerVisible;
            set => SetProperty(ref _isInputVisualizerVisible, value);
        }

        // ミニマップ表示/非表示
        private bool _isMinimapVisible = true;
        public bool IsMinimapVisible
        {
            get => _isMinimapVisible;
            set => SetProperty(ref _isMinimapVisible, value);
        }

        // XYZ軸トグル用プロパティ
        private bool _isCenterCoordEditorVisible;
        public bool IsCenterCoordEditorVisible
        {
            get => _isCenterCoordEditorVisible;
            set
            {
                if (_isCenterCoordEditorVisible == value) return;
                _isCenterCoordEditorVisible = value;
                OnPropertyChanged(nameof(IsCenterCoordEditorVisible));
            }
        }

        // 慣性力描画
        private bool _isMassLoadingVisible;
        public bool IsMassLoadingVisible
        {
            get => _isMassLoadingVisible;
            set
            {
                if (SetProperty(ref _isMassLoadingVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバスを更新
                }
            }
        }


        // 軸力描画
        private bool _isAxialLoadingVisible;
        public bool IsAxialLoadingVisible
        {
            get => _isAxialLoadingVisible;
            set
            {
                if (SetProperty(ref _isAxialLoadingVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 荷重面描画
        private bool _isLoadingPlaneVisible;
        public bool IsLoadingPlaneVisible
        {
            get => _isLoadingPlaneVisible;
            set
            {
                if (SetProperty(ref _isLoadingPlaneVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 地盤変位描画
        private bool _isForcedDisplacementVisible;
        public bool IsForcedDisplacementVisible
        {
            get => _isForcedDisplacementVisible;
            set
            {
                //if (value && !IsElementSplit)
                //{
                //    MessageService.Show("杭要素分割が完了していないため、地盤変位描画を有効にできません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                //    return;
                //}

                if (SetProperty(ref _isForcedDisplacementVisible, value))
                {
                    // 変位表示ONで比率が未設定(0.0)なら、見やすい初期値にブートストラップ
                    if (value && DisplacementDiagramRatio == 0.0)
                    {
                        IsDisplacementDiagramRatioApplicable = true;
                        DisplacementDiagramRatio = 0.3;
                    }

                    UpdateCanvas3DAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // LabelSize プロパティ
        private int _labelSize = 10;
        public int LabelSize
        {
            get => _labelSize;
            set
            {
                if (SetProperty(ref _labelSize, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // エリア塗りつぶし描画
        private bool _isAreaPainted = true;
        public bool IsAreaPainted
        {
            get => _isAreaPainted;
            set
            {
                if (SetProperty(ref _isAreaPainted, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }


        // バブル描画
        private bool _isBubbleVisible;
        public bool IsBubbleVisible
        {
            get => _isBubbleVisible;
            set
            {
                if (SetProperty(ref _isBubbleVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢描画
        private bool _isArrowVisible;
        public bool IsArrowVisible
        {
            get => _isArrowVisible;
            set
            {
                if (SetProperty(ref _isArrowVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // バブルサイズ
        private double _bubbleDia = 50;
        public double BubbleDia
        {
            get => _bubbleDia;
            set
            {
                if (SetProperty(ref _bubbleDia, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印サイズ
        private double _arrowLength = 50;
        public double ArrowLength
        {
            get => _arrowLength;
            set
            {
                if (SetProperty(ref _arrowLength, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印頭サイズ
        private double _arrowHeadLength = 15;
        public double ArrowHeadLength
        {
            get => _arrowHeadLength;
            set
            {
                if (SetProperty(ref _arrowHeadLength, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 矢印サイズ
        private double _arrowHeadDia = 5;
        public double ArrowHeadDia
        {
            get => _arrowHeadDia;
            set
            {
                if (SetProperty(ref _arrowHeadDia, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // LoadOption
        private ObservableCollection<string> _loadCaseNameOption = [];

        public ObservableCollection<string> LoadCaseNameOption
        {
            get => _loadCaseNameOption;
            set => SetProperty(ref _loadCaseNameOption, value);
        }

        // LoadCombination プロパティ
        private string _selectedLoadCaseName = "VL";
        public string SelectedLoadCaseName
        {
            get => _selectedLoadCaseName;
            set
            {
                if (SetProperty(ref _selectedLoadCaseName, value))
                {
                    UpdateDirectionOption();
                    AutoDetectLiquefactionState();
                    OnPropertyChanged(nameof(IsCurrentNonLiquefactionAnalyzed));
                    OnPropertyChanged(nameof(IsCurrentLiquefactionAnalyzed));
                    // 群杭沈下のケース別結果がある場合、荷重ケース連動でアクティブケースを切替
                    SyncGroupSettlementActiveCaseFromLoadCase(value);
                    // 荷重ケース変更で解析結果表示を再描画（VB 沈下反力など）
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        /// <summary>
        /// SelectedLoadCaseName の変更に応じて、群杭沈下解析の CaseRecords から
        /// 一致するケースを ActiveCase として選択する。
        /// 一致するケースが無い場合 (VL0/VLadd や 解析対象外ケース) は
        /// ActiveCaseIndex = -1 にしてコンタを非表示化する。
        /// </summary>
        private void SyncGroupSettlementActiveCaseFromLoadCase(string loadCaseName)
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null || pgs.CaseRecords.Count == 0) return;

            int idx = FindMatchingCaseRecordIndex(pgs.CaseRecords, loadCaseName, pgs.ActiveLoadingType);
            if (idx == pgs.ActiveCaseIndex) return;

            if (idx < 0)
            {
                // 該当ケースなし: コンタ・杭沈下をクリアして非表示
                pgs.ActiveCaseIndex = -1;
                pgs.SettlementGridData = [];
                if (CurrentInputModel?.PileLayoutItems != null)
                {
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                        pile.NotifyGroupPileSettlementChanged();
                }
                OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
                return;
            }

            pgs.ActiveCaseIndex = idx;
            GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(
                pgs, pgs.CaseRecords[idx], CurrentInputModel?.PileLayoutItems);
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
        }

        /// <summary>
        /// LoadCase 名 (例: "VL", "U1") から CaseRecord の index を見つける。
        /// activeLoadingType が指定された場合、その LoadingType の record だけを対象とする。
        /// 一致順位: 厳密一致 → 末尾 ": <name>" 一致 → 完全一致 "VL" のみ VL ケースへフォールバック。
        /// VL0 / VLadd など部分一致は無視する (一致なし → -1 で結果非表示)。
        /// </summary>
        private static int FindMatchingCaseRecordIndex(
            ObservableCollection<Models.Results.GroupSettlementCaseRecord> records,
            string loadCaseName, string activeLoadingType = null)
        {
            if (records == null || records.Count == 0 || string.IsNullOrEmpty(loadCaseName)) return -1;
            bool TypeMatch(int i) =>
                string.IsNullOrEmpty(activeLoadingType) || records[i].LoadingType == activeLoadingType;

            // 1. 厳密一致
            for (int i = 0; i < records.Count; i++)
                if (TypeMatch(i) && records[i].LoadCaseName == loadCaseName) return i;

            // 2. 末尾一致 (例: "L1-1: U1" の "U1")
            for (int i = 0; i < records.Count; i++)
            {
                if (!TypeMatch(i)) continue;
                var name = records[i].LoadCaseName;
                if (name.EndsWith(": " + loadCaseName)) return i;
            }

            // 3. "VL" の厳密一致のみ VL ケースへフォールバック (VL0/VLadd は対象外)
            if (loadCaseName == "VL")
            {
                for (int i = 0; i < records.Count; i++)
                {
                    if (!TypeMatch(i)) continue;
                    var name = records[i].LoadCaseName;
                    // "矩形荷重 (VL)" / "杭軸力 VL" など VL ケースを示すラベル
                    if (name == "VL" || name.EndsWith(" VL") || name.EndsWith("(VL)")) return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 選択された荷重ケースと荷重組合せに対して、解析結果の液状化状態を自動検出し、IsLiquefaction を設定する
        /// </summary>
        private void AutoDetectLiquefactionState()
        {
            // CurrentModelが存在しない場合は何もしない
            if (CurrentModel?.AnalysisStepResults == null || CurrentModel.AnalysisStepResults.Count == 0)
                return;
            if (CurrentInputModel == null) return;

            // 選択されたLoadCaseを取得
            var selectedLoadCase = CurrentInputModel.LoadCasesInput.AllLoadCases
                .FirstOrDefault(lc => lc.LoadName == SelectedLoadCaseName);
            if (selectedLoadCase == null)
                return;

            // 地震荷重ケース（Level 1 または Level 2）でない場合は IsLiquefaction = false
            if (selectedLoadCase.Level != 1 && selectedLoadCase.Level != 2)
            {
                IsLiquefaction = false;
                return;
            }

            // 荷重組合せが選択されている場合はその組合せの結果だけを見る。選択肢の文字列は表示名 (重なるときは番号付き) なので、
            // 組合せに解いてから番号で比べる (表示名で比べると、表示名の同じ別の組合せの結果を拾う)
            LoadCombination? selectedCombination = null;
            if (!string.IsNullOrEmpty(SelectedLoadCombinationName))
            {
                selectedCombination = LoadCombinations.GetLoadCombination(
                    CurrentInputModel?.LoadCasesInput?.LoadCombinations, SelectedLoadCombinationName);
                if (selectedCombination == null) return;   // 選択を組合せに解けない (結果は引けない)
            }

            // 選んでいる液状化の状態に結果が無く、逆の状態にだけあれば切替を動かす。
            // 結果を引く関数 (Beam.GetBeamResult など) は逆の状態を黙って返さないので、
            // 逆の状態を見せるのはここだけで、そのときは切替の表示も変わる。
            // 両方ある・どちらも無いときは利用者の選択のまま
            var (liquefaction, switched) = CurrentModel.ResolveLiquefactionWithResults(
                selectedLoadCase, selectedCombination, IsLiquefaction);
            if (switched) IsLiquefaction = liquefaction;
        }

        private void UpdateDirectionOption()
        {
            if (CurrentInputModel == null) { DirectionOption = []; return; }
            var selectedLoadCase = CurrentInputModel.LoadCasesInput.AllLoadCases
                .FirstOrDefault(lc => lc.LoadName == SelectedLoadCaseName);

            if (selectedLoadCase == null)
            {
                DirectionOption = [];
                return;
            }

            if (selectedLoadCase.Level == 1)
            {
                DirectionOption = new ObservableCollection<string>(
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1
                        .Select(lc => lc.LoadAngle.ToString("N1"))
                );
            }
            else if (selectedLoadCase.Level == 2)
            {
                DirectionOption = new ObservableCollection<string>(
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2
                        .Select(lc => lc.LoadAngle.ToString("N1"))
                );
            }

            // 選択中の荷重ケースの角度を SelectedDirection に反映
            SelectedDirection = selectedLoadCase.LoadAngle;
        }

        private ObservableCollection<string> _directionOption = [];

        public ObservableCollection<string> DirectionOption
        {
            get => _directionOption;
            set => SetProperty(ref _directionOption, value);
        }

        // Direction プロパティ
        private double _selectedDirection;
        public double SelectedDirection
        {
            get => _selectedDirection;
            set => SetProperty(ref _selectedDirection, value);
        }

        // LoadCombinationOption プロパティ
        private ObservableCollection<string> _loadCombinationNameOption;
        public ObservableCollection<string> LoadCombinationNameOption
        {
            get => _loadCombinationNameOption;
            set => SetProperty(ref _loadCombinationNameOption, value);
        }

        // LoadCombination プロパティ
        private string _selectedLoadCombinationName;
        public string SelectedLoadCombinationName
        {
            get => _selectedLoadCombinationName;
            set
            {
                if (SetProperty(ref _selectedLoadCombinationName, value))
                {
                    AutoDetectLiquefactionState();
                }
            }
        }

        private ObservableCollection<string> _analysisResultContentOption = []; /*= [*/
        public ObservableCollection<string> AnalysisResultContentOption
        {
            get => _analysisResultContentOption;
            set => SetProperty(ref _analysisResultContentOption, value);
        }

        private string _analysisResultContent /*= "梁応力（水平）"*/;
        public string AnalysisResultContent
        {
            get => _analysisResultContent;
            set
            {
                if (SetProperty(ref _analysisResultContent, value))
                {
                    // 杭頭Mマップ/Qマップ選択時は応力ダイアグラムスケールを0.1、値表示をON
                    if (value == "杭頭Mマップ" || value == "杭頭Qマップ")
                    {
                        ForceDiagramRatio = 0.1;
                        IsResultValueVisible = true;
                    }

                    // 各種マップ（杭頭M/Q・接合点M/Q）は平面分布が見やすいようトップビュー（XY平面）に切替える
                    if (value == "杭頭Mマップ" || value == "杭頭Qマップ"
                        || value == "接合点Mマップ" || value == "接合点Qマップ")
                    {
                        ViewXYPlane();
                    }

                    // 沈下系コンテンツ選択時も値表示をON
                    if (value == "沈下量" || value == "沈下部材角"
                        || value == "沈下反力（地盤）" || value == "沈下反力（杭頭集約）"
                        || value == "沈下応力")
                    {
                        IsResultValueVisible = true;
                    }

                    // 沈下系コンテンツのサブオプションを動的に更新
                    UpdateSettlementSubOptions(value);

                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        /// <summary>
        /// AnalysisResultContent + AnalysisResultSettlementType から
        /// 描画ロジック用の実効コンテンツ名を返す
        /// </summary>
        public string EffectiveSettlementContent
        {
            get
            {
                string sub = AnalysisResultSettlementType;
                return AnalysisResultContent switch
                {
                    "沈下量" when sub == "基礎梁考慮" => "基礎梁考慮沈下",
                    "沈下量" when sub == "基礎梁考慮+群杭" => "基礎梁考慮+群杭沈下",
                    "沈下量" => "沈下",
                    "沈下部材角" => sub switch
                    {
                        "基礎梁考慮" => "基礎梁考慮沈下部材角",
                        "基礎梁考慮+群杭" => "基礎梁考慮+群杭沈下部材角",
                        "単杭+群杭" => "単杭+群杭沈下部材角",
                        "群杭" => "群杭沈下部材角",
                        _ => "単杭沈下部材角",
                    },
                    "沈下反力（地盤）" => sub == "単杭" ? "単杭反力（地盤）" : "基礎梁考慮反力（地盤）",
                    "沈下反力（杭頭集約）" => sub == "単杭" ? "単杭反力（杭頭集約）" : "基礎梁考慮反力（杭頭集約）",
                    "沈下応力" => "基礎梁考慮沈下梁応力",
                    _ => AnalysisResultContent,
                };
            }
        }

        /// <summary>
        /// 沈下系コンテンツ選択時にサブオプション一覧を動的に更新
        /// </summary>
        private void UpdateSettlementSubOptions(string content)
        {
            switch (content)
            {
                case "沈下量":
                    // 解析済みフラグから組み直す。
                    // 以前は「既存の一覧をそのまま使う」としていたが、他の表示内容
                    // (沈下反力など) に切り替えると一覧ごと差し替えられてしまい、
                    // 戻ってきたときに<b>群杭が選べない</b>状態になっていた。
                    RebuildSettlementTypeOptions();
                    break;
                case "沈下部材角":
                {
                    var opts = new ObservableCollection<string>();
                    if (IsVerticalAnalysisDone) opts.Add("単杭");
                    if (IsGroupPileSettlementAnalysisDone) opts.Add("群杭");
                    if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone) opts.Add("単杭+群杭");
                    if (IsVerticalBeamAnalysisDone) opts.Add("基礎梁考慮");
                    if (IsVerticalBeamAnalysisDone && IsGroupPileSettlementAnalysisDone) opts.Add("基礎梁考慮+群杭");
                    AnalysisResultSettlementOption = opts;
                    if (opts.Count > 0 && !opts.Contains(AnalysisResultSettlementType))
                        AnalysisResultSettlementType = opts[0];
                    break;
                }
                case "沈下反力（地盤）":
                case "沈下反力（杭頭集約）":
                {
                    var opts = new ObservableCollection<string>();
                    if (IsVerticalAnalysisDone) opts.Add("単杭");
                    if (IsVerticalBeamAnalysisDone) opts.Add("基礎梁考慮");
                    AnalysisResultSettlementOption = opts;
                    if (opts.Count > 0 && !opts.Contains(AnalysisResultSettlementType))
                        AnalysisResultSettlementType = opts[0];
                    break;
                }
                case "沈下応力":
                {
                    var opts = new ObservableCollection<string>();
                    if (IsVerticalBeamAnalysisDone) opts.Add("基礎梁考慮");
                    AnalysisResultSettlementOption = opts;
                    if (opts.Count > 0 && !opts.Contains(AnalysisResultSettlementType))
                        AnalysisResultSettlementType = opts[0];
                    break;
                }
            }
        }

        private ObservableCollection<string> _analysisResultSettlementOption = []; //= [
        public ObservableCollection<string> AnalysisResultSettlementOption
        {
            get => _analysisResultSettlementOption;
            set => SetProperty(ref _analysisResultSettlementOption, value);
        }
        private string _analysisResultSettlementType /*= "群杭"*/;
        public string AnalysisResultSettlementType
        {
            get => _analysisResultSettlementType;
            set
            {
                if (SetProperty(ref _analysisResultSettlementType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisResultBeamForceOption = [
            "Fh",
            "Mh",
            "Fx",
            "Fy",
            "Fz",
            "Mx",
            "My",
            "Mz",
            ];

        public ObservableCollection<string> AnalysisResultBeamForceOption
        {
            get => _analysisResultBeamForceOption;
            set => SetProperty(ref _analysisResultBeamForceOption, value);
        }
        private string _analysisResultBeamForceType = "Mh";
        public string AnalysisResultBeamForceType
        {
            get => _analysisResultBeamForceType;
            set
            {
                if (SetProperty(ref _analysisResultBeamForceType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisResultNodeDisplacementOption = [
            "UH",
            "U",
            "θH",
            "UX",
            "UY",
            "UZ",
            "θX",
            "θY",
            "θZ",
            ];

        public ObservableCollection<string> AnalysisResultNodeDisplacementOption
        {
            get => _analysisResultNodeDisplacementOption;
            set => SetProperty(ref _analysisResultNodeDisplacementOption, value);
        }
        private string _analysisResultNodeDisplacementType = "UH";
        public string AnalysisResultNodeDisplacementType
        {
            get => _analysisResultNodeDisplacementType;
            set
            {
                if (SetProperty(ref _analysisResultNodeDisplacementType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        private ObservableCollection<string> _analysisSoilSpringOption = [
            "RH",
            "R",
            "MH",
            "RX",
            "RY",
            "RZ",
            "MX",
            "MY",
            "MZ",
            ];

        public ObservableCollection<string> AnalysisResultSoilSpringOption
        {
            get => _analysisSoilSpringOption;
            set => SetProperty(ref _analysisSoilSpringOption, value);
        }
        private string _analysisResultSoilSpringType = "RH";
        public string AnalysisResultSoilSpringType
        {
            get => _analysisResultSoilSpringType;
            set
            {
                if (SetProperty(ref _analysisResultSoilSpringType, value))
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        // 値表示
        private bool _isSoilValueVisible = false;
        public bool IsSoilValueVisible
        {
            get => _isSoilValueVisible;
            set
            {
                if (SetProperty(ref _isSoilValueVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 解析結果値表示
        private bool _isResultValueVisible = false;
        public bool IsResultValueVisible
        {
            get => _isResultValueVisible;
            set
            {
                if (SetProperty(ref _isResultValueVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }

        // 相互排他の内側更新・再描画抑止用
        private bool _suppressMutualToggle;

        // 梁中央値表示
        private bool _isMidSpanResultValueVisibleOnly = false;
        public bool IsMidSpanResultValueVisibleOnly
        {
            get => _isMidSpanResultValueVisibleOnly;
            set
            {
                if (!SetProperty(ref _isMidSpanResultValueVisibleOnly, value)) return;

                // 自分がtrueになったら、他のオンリー系をfalseに
                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsPileTopResultValueVisibleOnly = false;
                    IsPileMaxMinResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

                OnPropertyChanged(nameof(SelectedResultValueDisplayMode));
                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新（内側更新では抑止）
            }
        }

        // 梁中央値表示
        private bool _isPileTopResultValueVisibleOnly = false;
        public bool IsPileTopResultValueVisibleOnly
        {
            get => _isPileTopResultValueVisibleOnly;
            set
            {
                if (!SetProperty(ref _isPileTopResultValueVisibleOnly, value)) return;

                // 自分がtrueになったら、もう片方をfalseに
                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsMidSpanResultValueVisibleOnly = false;
                    IsPileMaxMinResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

                OnPropertyChanged(nameof(SelectedResultValueDisplayMode));
                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新（内側更新では抑止）
            }
        }

        // 杭最大最小値表示: 各杭の応力ダイアグラム値のうち最大と最小だけを表示する。
        // 多数の杭でログが埋まる問題への対策。要素中間 / 杭頭 とは相互排他。
        private bool _isPileMaxMinResultValueVisibleOnly = false;
        public bool IsPileMaxMinResultValueVisibleOnly
        {
            get => _isPileMaxMinResultValueVisibleOnly;
            set
            {
                if (!SetProperty(ref _isPileMaxMinResultValueVisibleOnly, value)) return;

                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsMidSpanResultValueVisibleOnly = false;
                    IsPileTopResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

                OnPropertyChanged(nameof(SelectedResultValueDisplayMode));
                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke();
            }
        }

        // 値表示モードの選択肢 (ComboBox 用)。
        // 4 つの "Only" 系 bool プロパティ (3 つは相互排他、すべて false で「全表示」) を 1 列で表現。
        public System.Collections.ObjectModel.ObservableCollection<string> ResultValueDisplayModeOptions { get; } =
            ["全表示", "要素中間のみ", "杭頭のみ", "杭MaxMinのみ"];

        // ComboBox の SelectedItem を受ける string プロパティ。get で 3 bool から、set で 3 bool に書き戻す。
        public string SelectedResultValueDisplayMode
        {
            get
            {
                if (IsMidSpanResultValueVisibleOnly) return "要素中間のみ";
                if (IsPileTopResultValueVisibleOnly) return "杭頭のみ";
                if (IsPileMaxMinResultValueVisibleOnly) return "杭MaxMinのみ";
                return "全表示";
            }
            set
            {
                _suppressMutualToggle = true;
                _isMidSpanResultValueVisibleOnly = value == "要素中間のみ";
                _isPileTopResultValueVisibleOnly = value == "杭頭のみ";
                _isPileMaxMinResultValueVisibleOnly = value == "杭MaxMinのみ";
                _suppressMutualToggle = false;
                OnPropertyChanged(nameof(IsMidSpanResultValueVisibleOnly));
                OnPropertyChanged(nameof(IsPileTopResultValueVisibleOnly));
                OnPropertyChanged(nameof(IsPileMaxMinResultValueVisibleOnly));
                OnPropertyChanged(nameof(SelectedResultValueDisplayMode));
                UpdateCanvas3DAction?.Invoke();
            }
        }

        // 基礎梁応力ダイアグラムを 90° 回転表示 (上面図用)
        // 通常 My/Qz 等の鉛直方向成分は梁軸まわりの上下に張り出すが、
        // ON にすると beam-axis 周り 90° 回転して水平面に張り出す → 上面図で確認しやすい。
        private bool _isFoundationBeamStressRotatedToHorizontal = false;
        public bool IsFoundationBeamStressRotatedToHorizontal
        {
            get => _isFoundationBeamStressRotatedToHorizontal;
            set
            {
                if (SetProperty(ref _isFoundationBeamStressRotatedToHorizontal, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 杭応力ダイアグラム表示 (false にすると 上面図モードと併用で基礎梁応力だけが見える)
        private bool _isPileStressVisible = true;
        public bool IsPileStressVisible
        {
            get => _isPileStressVisible;
            set
            {
                if (SetProperty(ref _isPileStressVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 基礎梁応力ダイアグラム表示 (false にすると基礎梁応力ダイアグラムをスキップ)
        private bool _isFoundationBeamStressVisible = true;
        public bool IsFoundationBeamStressVisible
        {
            get => _isFoundationBeamStressVisible;
            set
            {
                if (SetProperty(ref _isFoundationBeamStressVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 杭変位ダイアグラム表示 (false にすると杭体・RigidLink・根入れ部の変位描画をスキップ)
        private bool _isPileDisplacementVisible = true;
        public bool IsPileDisplacementVisible
        {
            get => _isPileDisplacementVisible;
            set
            {
                if (SetProperty(ref _isPileDisplacementVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 基礎梁変位ダイアグラム表示 (false にすると基礎梁の変位描画をスキップ)
        private bool _isFoundationBeamDisplacementVisible = true;
        public bool IsFoundationBeamDisplacementVisible
        {
            get => _isFoundationBeamDisplacementVisible;
            set
            {
                if (SetProperty(ref _isFoundationBeamDisplacementVisible, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 値小数点位置
        private int _decimalPlaces = 1;
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set
            {
                if (SetProperty(ref _decimalPlaces, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }


        // 力ダイアグラム表示比率（モデル範囲に対する最大ダイアグラム長の比率）
        private double _forceDiagramRatio = 0.3;
        public double ForceDiagramRatio
        {
            get => _forceDiagramRatio;
            set
            {
                if (SetProperty(ref _forceDiagramRatio, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 変位ダイアグラム表示比率適用
        private bool _isDisplacementDiagramRatioApplicable = true;
        public bool IsDisplacementDiagramRatioApplicable
        {
            get => _isDisplacementDiagramRatioApplicable;
            set
            {
                if (SetProperty(ref _isDisplacementDiagramRatioApplicable, value))
                {
                    if (!value) DisplacementDiagramRatio = 0.0;
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // 変位ダイアグラム表示比率（モデル範囲に対する最大ダイアグラム長の比率）
        private double _displacementDiagramRatio = 0.3;
        public double DisplacementDiagramRatio
        {
            get => _displacementDiagramRatio;
            set
            {
                if (SetProperty(ref _displacementDiagramRatio, value))
                {
                    UpdateCanvas3DAction?.Invoke();
                }
            }
        }

        // モデル範囲（杭配置の max(maxX-minX, maxY-minY)、最小1.0m）
        public double ModelExtent
        {
            get
            {
                var items = CurrentInputModel?.PileLayoutItems;
                if (items == null || items.Count == 0) return 1.0;
                double dx = items.Max(p => p.X) - items.Min(p => p.X);
                double dy = items.Max(p => p.Y) - items.Min(p => p.Y);
                return Math.Max(Math.Max(dx, dy), 1.0);
            }
        }


        // テキスト位置調整　
        private double _textPositionAdjuster = 0.0;
        public double TextPositionAdjuster
        {
            get => _textPositionAdjuster;
            set
            {
                if (SetProperty(ref _textPositionAdjuster, value))
                {
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新
                }
            }
        }


        // 作用点描画
        private bool _isActionPointVisible = true;
        public bool IsActionPointVisible
        {
            get => _isActionPointVisible;
            set
            {
                if (SetProperty(ref _isActionPointVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 沈下検討用荷重面描画
        private bool _isSettlementLoadVisible = true;
        public bool IsSettlementLoadVisible
        {
            get => _isSettlementLoadVisible;
            set
            {
                if (SetProperty(ref _isSettlementLoadVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 液状化
        private bool _isLiquefaction = false;
        public bool IsLiquefaction
        {
            get => _isLiquefaction;
            set
            {
                if (SetProperty(ref _isLiquefaction, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 液状化解析済みインジケータ用
        public bool IsCurrentNonLiquefactionAnalyzed =>
            CurrentModel?.AnalysisStepResults?.Any(r =>
                r.LoadCase?.LoadName == SelectedLoadCaseName && !r.IsLiquefaction) == true;

        public bool IsCurrentLiquefactionAnalyzed =>
            CurrentModel?.AnalysisStepResults?.Any(r =>
                r.LoadCase?.LoadName == SelectedLoadCaseName && r.IsLiquefaction) == true;

        // 剛床描画
        private bool _isRigidFloorVisible = true;
        public bool IsRigidFloorVisible
        {
            get => _isRigidFloorVisible;
            set
            {
                if (SetProperty(ref _isRigidFloorVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭中心線描画
        private bool _isPileCenterLineVisible = true;
        public bool IsPileCenterLineVisible
        {
            get => _isPileCenterLineVisible;
            set
            {
                if (SetProperty(ref _isPileCenterLineVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 基礎梁描画
        private bool _isFoundationBeamVisible = true;
        public bool IsFoundationBeamVisible
        {
            get => _isFoundationBeamVisible;
            set
            {
                if (SetProperty(ref _isFoundationBeamVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // ビューキューブ表示
        private bool _isViewCubeVisible = true;
        public bool IsViewCubeVisible
        {
            get => _isViewCubeVisible;
            set
            {
                if (SetProperty(ref _isViewCubeVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 右ツール群表示
        private bool _isRightToolbarVisible = true;
        public bool IsRightToolbarVisible
        {
            get => _isRightToolbarVisible;
            set
            {
                if (SetProperty(ref _isRightToolbarVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 接合節点表示（杭頭+ΔZc位置）
        private bool _isConnectionNodeVisible = true;
        public bool IsConnectionNodeVisible
        {
            get => _isConnectionNodeVisible;
            set
            {
                if (SetProperty(ref _isConnectionNodeVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // キャンバス編集モード
        private CanvasEditMode _currentEditMode = CanvasEditMode.None;
        public CanvasEditMode CurrentEditMode
        {
            get => _currentEditMode;
            set
            {
                if (SetProperty(ref _currentEditMode, value))
                {
                    // ステータスバーにモード表示
                    StatusMessage = value switch
                    {
                        CanvasEditMode.AddNode => "ノード追加モード（画面上をクリック、杭位置に自動スナップ）",
                        CanvasEditMode.AddElement => "要素追加モード（2つのノードをクリック）",
                        CanvasEditMode.Delete => "削除モード（ノードまたは要素をクリック）",
                        _ => string.Empty
                    };

                    // 編集モードに入ったときに自動的に接続モードを基礎梁に変更
                    if (value != CanvasEditMode.None)
                    {
                        if (CurrentInputModel?.FoundationBeamInput == null)
                        {
                            CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
                        }

                        if (CurrentInputModel.FoundationBeamInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody)
                        {
                            CurrentInputModel.FoundationBeamInput.ConnectionMode = FoundationBeamConnectionMode.RigidFloor;
                        }
                    }

                    // カーソルを更新
                    OnPropertyChanged(nameof(CanvasCursor));

                    // 画面更新
                    RequestUpdateWindow();
                }
            }
        }

        /// <summary>
        /// 編集モードに応じたキャンバスカーソル
        /// </summary>
        public System.Windows.Input.Cursor CanvasCursor => CurrentEditMode switch
        {
            CanvasEditMode.AddNode => System.Windows.Input.Cursors.Cross,
            CanvasEditMode.AddElement => System.Windows.Input.Cursors.Pen,
            CanvasEditMode.Delete => System.Windows.Input.Cursors.No,
            _ => System.Windows.Input.Cursors.Arrow,
        };

        // 基礎梁ノード追加時のデフォルトZ座標（杭頭の平均高さなど）
        private double _defaultFoundationBeamZ = 1.0;
        public double DefaultFoundationBeamZ
        {
            get => _defaultFoundationBeamZ;
            set => SetProperty(ref _defaultFoundationBeamZ, value);
        }

        // 要素接続時の一時的な開始ノード参照
        private NodeReference? _tempStartNode = null;
        public NodeReference? TempStartNode
        {
            get => _tempStartNode;
            set => SetProperty(ref _tempStartNode, value);
        }

        // ラベル描画
        private bool _isLabelVisible = true;
        public bool IsLabelVisible
        {
            get => _isLabelVisible;
            set
            {
                if (SetProperty(ref _isLabelVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭符号ラベル描画
        private bool _isPileRefVisible = false;
        public bool IsPileRefVisible
        {
            get => _isPileRefVisible;
            set
            {
                if (SetProperty(ref _isPileRefVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 地盤符号ラベル描画
        private bool _isSoilRefVisible = false;
        public bool IsSoilRefVisible
        {
            get => _isSoilRefVisible;
            set
            {
                if (SetProperty(ref _isSoilRefVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 接合節点レベル(m)ラベル描画
        private bool _isConnectionNodeZVisible = false;
        public bool IsConnectionNodeZVisible
        {
            get => _isConnectionNodeZVisible;
            set
            {
                if (SetProperty(ref _isConnectionNodeZVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 杭頭レベル(m)ラベル描画
        private bool _isPileTopLevelVisible = false;
        public bool IsPileTopLevelVisible
        {
            get => _isPileTopLevelVisible;
            set
            {
                if (SetProperty(ref _isPileTopLevelVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 郡杭係数ラベル描画
        private bool _isGroupPileFactorLabelVisible = false;
        public bool IsGroupPileFactorLabelVisible
        {
            get => _isGroupPileFactorLabelVisible;
            set
            {
                if (SetProperty(ref _isGroupPileFactorLabelVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }
        // 杭間隔比ラベル描画
        private bool _isPileDiaSpacingRatioLabelVisible = false;
        public bool IsPileDiaSpacingRatioLabelVisible
        {
            get => _isPileDiaSpacingRatioLabelVisible;
            set
            {
                if (SetProperty(ref _isPileDiaSpacingRatioLabelVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 前後ラベル描画
        private bool _isFrontPileLabelVisible = false;
        public bool IsFrontPileLabelVisible
        {
            get => _isFrontPileLabelVisible;
            set
            {
                if (SetProperty(ref _isFrontPileLabelVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 結果描画
        private bool _isAnalysisResultVisible = false;
        public bool IsAnalysisResultVisible
        {
            get => _isAnalysisResultVisible;
            set
            {
                if (value && !IsVerticalAnalysisDone && !IsHorizontalAnalysisDone && !IsGroupPileSettlementAnalysisDone && !IsVerticalBeamAnalysisDone && !HasGroupSettlementBeamAwareCases)
                {
                    MessageService.Show("解析結果がまだありません。\nリボン「解析条件/解析」で水平解析・単杭沈下解析・群杭沈下解析のいずれかを実行してください。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SetProperty(ref _isAnalysisResultVisible, false); // 明示的に戻す
                    return;
                }
                if (SetProperty(ref _isAnalysisResultVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 杭形状描画
        private bool _isPileSectionVisible = true;
        public bool IsPileSectionVisible
        {
            get => _isPileSectionVisible;
            set
            {
                if (SetProperty(ref _isPileSectionVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 一般梁要素形状描画
        private bool _isBeamElementSectionVisible = true;
        public bool IsBeamElementSectionVisible
        {
            get => _isBeamElementSectionVisible;
            set
            {
                if (SetProperty(ref _isBeamElementSectionVisible, value))
                {
                    // 梁要素形状をONにしたとき、梁要素表示もONにする
                    if (value && !IsFoundationBeamVisible)
                    {
                        IsFoundationBeamVisible = true;
                    }
                    RequestUpdateWindow();
                }
            }
        }

        // 根入れ形状描画
        private bool _isEmbedmentBoxVisible = true;
        public bool IsEmbedmentBoxVisible
        {
            get => _isEmbedmentBoxVisible;
            set
            {
                if (SetProperty(ref _isEmbedmentBoxVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // XYZ軸描画
        private bool _isXYZAxesVisible = false;
        public bool IsXYZAxesVisible
        {
            get => _isXYZAxesVisible;
            set
            {
                if (SetProperty(ref _isXYZAxesVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        //要素座標描画
        private bool _isBeamLocalAxesVisible = false;
        public bool IsBeamLocalAxesVisible
        {
            get => _isBeamLocalAxesVisible;
            set
            {
                if (SetProperty(ref _isBeamLocalAxesVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // ティックマーク描画
        private bool _isTickMarkVisible = true;
        public bool IsTickMarkVisible
        {
            get => _isTickMarkVisible;
            set
            {
                if (SetProperty(ref _isTickMarkVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 通り心描画
        private bool _isGridLineVisible = true;
        public bool IsGridLineVisible
        {
            get => _isGridLineVisible;
            set
            {
                if (SetProperty(ref _isGridLineVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭周地盤描画
        private bool _isGroundVisible = true;
        public bool IsGroundVisible
        {
            get => _isGroundVisible;
            set
            {
                if (SetProperty(ref _isGroundVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // N値描画
        private bool _isNValueVisible = false;
        public bool IsNValueVisible
        {
            get => _isNValueVisible;
            set
            {
                if (SetProperty(ref _isNValueVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // VS0描画
        private bool _isVS0Visible = false;
        public bool IsVS0Visible
        {
            get => _isVS0Visible;
            set
            {
                if (SetProperty(ref _isVS0Visible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Fc描画
        private bool _isFcVisible = false;
        public bool IsFcVisible
        {
            get => _isFcVisible;
            set
            {
                if (SetProperty(ref _isFcVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private bool _isSoilMassParamDisplayEnabled;
        public bool IsSoilMassParamDisplayEnabled
        {
            get => _isSoilMassParamDisplayEnabled;
            set
            {
                if (SetProperty(ref _isSoilMassParamDisplayEnabled, value))
                    ApplySoilMassParamDisplay();
            }
        }

        public ObservableCollection<string> SoilMassParamDisplayOptions { get; } =
        [
            "N値",
            "Vs0",
            "Fc",
        ];

        private string _selectedSoilMassParamDisplay = "N値";
        public string SelectedSoilMassParamDisplay
        {
            get => _selectedSoilMassParamDisplay;
            set
            {
                if (SetProperty(ref _selectedSoilMassParamDisplay, value))
                    ApplySoilMassParamDisplay();
            }
        }

        private void ApplySoilMassParamDisplay()
        {
            // 既存の個別表示フラグを一旦クリア
            IsNValueVisible = false;
            IsVS0Visible = false;
            IsFcVisible = false;

            if (!IsSoilMassParamDisplayEnabled) return;

            switch (SelectedSoilMassParamDisplay)
            {
                case "N値":
                    IsNValueVisible = true;
                    break;
                case "Vs0":
                    IsVS0Visible = true;
                    break;
                case "Fc":
                    IsFcVisible = true;
                    break;
            }

            // 画面更新が必要な場合
            UpdateViewCommand?.Execute(null);
        }

        // 既存フラグから初期値を推定（任意）
        public void InitializeSoilMassParamDisplayFromLegacyFlags()
        {
            if (IsNValueVisible || IsVS0Visible || IsFcVisible)
            {
                IsSoilMassParamDisplayEnabled = true;
                if (IsNValueVisible) SelectedSoilParamDisplay = "N値";
                else if (IsVS0Visible) SelectedSoilParamDisplay = "Vs0";
                else if (IsFcVisible) SelectedSoilParamDisplay = "Fc";
            }
            else
            {
                IsSoilMassParamDisplayEnabled = false;
                SelectedSoilMassParamDisplay = SoilMassParamDisplayOptions.First();
            }

            ApplySoilMassParamDisplay();
        }


        private bool _isSoilLayerParamDisplayEnabled;
        public bool IsSoilLayerParamDisplayEnabled
        {
            get => _isSoilLayerParamDisplayEnabled;
            set
            {
                if (SetProperty(ref _isSoilLayerParamDisplayEnabled, value))
                    ApplySoilLayerParamDisplay();
            }
        }

        public ObservableCollection<string> SoilParamDisplayOptions { get; } =
        [
            "密度",
            "粘着力",
            "Vs",
            "Es",
            "平均N値",
        ];

        private string _selectedSoilParamDisplay = "粘着力";
        public string SelectedSoilParamDisplay
        {
            get => _selectedSoilParamDisplay;
            set
            {
                if (SetProperty(ref _selectedSoilParamDisplay, value))
                    ApplySoilLayerParamDisplay();
            }
        }

        private void ApplySoilLayerParamDisplay()
        {
            // 既存の個別表示フラグを一旦クリア
            IsDensityVisible = false;
            IsCohesiveVisible = false;
            IsVsVisible = false;
            IsEsVisible = false;
            IsLayerNValueVisible = false;

            if (!IsSoilLayerParamDisplayEnabled) return;

            switch (SelectedSoilParamDisplay)
            {
                case "密度":
                    IsDensityVisible = true;
                    break;
                case "粘着力":
                    IsCohesiveVisible = true;
                    break;
                case "Vs":
                    IsVsVisible = true;
                    break;
                case "Es":
                    IsEsVisible = true;
                    break;
                case "平均N値":
                    IsLayerNValueVisible = true;
                    break;
            }

            // 画面更新が必要な場合
            UpdateViewCommand?.Execute(null);
        }

        // 既存フラグから初期値を推定（任意）
        public void InitializeSoilParamDisplayFromLegacyFlags()
        {
            if (IsDensityVisible || IsCohesiveVisible || IsVsVisible || IsEsVisible || IsLayerNValueVisible)
            {
                IsSoilLayerParamDisplayEnabled = true;
                if (IsDensityVisible) SelectedSoilParamDisplay = "密度";
                else if (IsCohesiveVisible) SelectedSoilParamDisplay = "粘着力";
                else if (IsVsVisible) SelectedSoilParamDisplay = "Vs";
                else if (IsEsVisible) SelectedSoilParamDisplay = "Es";
                else if (IsLayerNValueVisible) SelectedSoilParamDisplay = "平均N値";
            }
            else
            {
                IsSoilLayerParamDisplayEnabled = false;
                SelectedSoilParamDisplay = SoilParamDisplayOptions.First();
            }

            ApplySoilLayerParamDisplay();
        }

        // 密度描画
        private bool _isDensityVisible = false;
        public bool IsDensityVisible
        {
            get => _isDensityVisible;
            set
            {
                if (SetProperty(ref _isDensityVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 粘着力描画
        private bool _isCohesiveVisible = false;
        public bool IsCohesiveVisible
        {
            get => _isCohesiveVisible;
            set
            {
                if (SetProperty(ref _isCohesiveVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Vs描画
        private bool _isVsVisible = false;
        public bool IsVsVisible
        {
            get => _isVsVisible;
            set
            {
                if (SetProperty(ref _isVsVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // Es描画
        private bool _isEsVisible = false;
        public bool IsEsVisible
        {
            get => _isEsVisible;
            set
            {
                if (SetProperty(ref _isEsVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 平均N値描画 (土層の代表 N 値)
        private bool _isLayerNValueVisible = false;
        public bool IsLayerNValueVisible
        {
            get => _isLayerNValueVisible;
            set
            {
                if (SetProperty(ref _isLayerNValueVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }


        // 沈下検討用土層描画
        private bool _isSettlementGroundVisible = false;
        public bool IsSettlementGroundVisible
        {
            get => _isSettlementGroundVisible;
            set
            {
                if (SetProperty(ref _isSettlementGroundVisible, value))
                {
                    RequestUpdateWindow();
                    // ONにしたとき: 群杭沈下ウィンドウの土層タブを表示し、θ=0にする
                    if (value)
                    {
                        ActivateSettlementSoilTabAction?.Invoke();
                        if (AnimateViewAnglesAction != null)
                            AnimateViewAnglesAction(CanvasThreeDView.Tht, 0);
                        else
                        {
                            CanvasThreeDView.Phi = 0;
                            UpdateCanvas3DAction?.Invoke();
                        }
                    }
                }
            }
        }

        // 節点描画
        private bool _isNodeVisible = true;
        public bool IsNodeVisible
        {
            get => _isNodeVisible;
            set
            {
                if (SetProperty(ref _isNodeVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 要素描画
        private bool _isElementVisible = true;
        public bool IsElementVisible
        {
            get => _isElementVisible;
            set
            {
                if (SetProperty(ref _isElementVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 節点番号描画 (デフォルト OFF: 番号表示は煩雑になるため必要時のみ有効化)
        private bool _isNodeNoVisible = false;
        public bool IsNodeNoVisible
        {
            get => _isNodeNoVisible;
            set
            {
                if (SetProperty(ref _isNodeNoVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 一般節点Z座標描画
        private bool _isGeneralNodeZVisible = false;
        public bool IsGeneralNodeZVisible
        {
            get => _isGeneralNodeZVisible;
            set
            {
                if (SetProperty(ref _isGeneralNodeZVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 要素番号描画 (デフォルト OFF: 梁要素番号は煩雑になるため必要時のみ有効化)
        private bool _isElementNoVisible = false;
        public bool IsElementNoVisible
        {
            get => _isElementNoVisible;
            set
            {
                if (SetProperty(ref _isElementNoVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 梁要素 材料No描画
        private bool _isBeamMaterialNoVisible = false;
        public bool IsBeamMaterialNoVisible
        {
            get => _isBeamMaterialNoVisible;
            set
            {
                if (SetProperty(ref _isBeamMaterialNoVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 梁要素 材料名称描画
        private bool _isBeamMaterialNameVisible = false;
        public bool IsBeamMaterialNameVisible
        {
            get => _isBeamMaterialNameVisible;
            set
            {
                if (SetProperty(ref _isBeamMaterialNameVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 梁要素 断面No描画
        private bool _isBeamSectionNoVisible = false;
        public bool IsBeamSectionNoVisible
        {
            get => _isBeamSectionNoVisible;
            set
            {
                if (SetProperty(ref _isBeamSectionNoVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 梁要素 断面名称描画
        private bool _isBeamSectionNameVisible = false;
        public bool IsBeamSectionNameVisible
        {
            get => _isBeamSectionNameVisible;
            set
            {
                if (SetProperty(ref _isBeamSectionNameVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }

        // 梁要素 β角度描画
        private bool _isBeamAngleBetaVisible = false;
        public bool IsBeamAngleBetaVisible
        {
            get => _isBeamAngleBetaVisible;
            set
            {
                if (SetProperty(ref _isBeamAngleBetaVisible, value))
                {
                    RequestUpdateWindow();
                }
            }
        }


        // 変形後の要素描画
        private bool _isDeformedElementVisible = false;
        public bool IsDeformedElementVisible
        {
            get => _isDeformedElementVisible;
            set
            {
                if (SetProperty(ref _isDeformedElementVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }


        // 要素レベル
        private bool _isElementShownAtSettlementPlane = false;
        public bool IsElementShownAtSettlementPlane
        {
            get => _isElementShownAtSettlementPlane;
            set/* => SetProperty(ref _isElementShownAtSettlementPlane, value);*/
            {
                if (SetProperty(ref _isElementShownAtSettlementPlane, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭心地下外壁間距離
        private double _embedmentPileDistance = 1.5;
        public double EmbedmentPileDistance
        {
            get => _embedmentPileDistance;
            set => SetProperty(ref _embedmentPileDistance, value);
        }

        // 群杭沈下荷重面距離
        private double _rectLoadPileDistance = 1.5;
        public double RectLoadPileDistance
        {
            get => _rectLoadPileDistance;
            set => SetProperty(ref _rectLoadPileDistance, value);
        }

        private string _comboBox3DLabelContent_LabelContent;
        public string ComboBox3DLabelContent_LabelContent
        {
            get => _comboBox3DLabelContent_LabelContent;
            set => SetProperty(ref _comboBox3DLabelContent_LabelContent, value);
        }

        // 群杭沈下量カラーバブル表示
        private bool _isGroupPileSettlementColorBubbleVisible = false;
        public bool IsGroupPileSettlementColorBubbleVisible
        {
            get => _isGroupPileSettlementColorBubbleVisible;
            set => SetProperty(ref _isGroupPileSettlementColorBubbleVisible, value);
        }

        // 群杭沈下量カラー矢印表示
        private bool _isGroupPileSettlementColorArrowVisible = false;
        public bool IsGroupPileSettlementColorArrowVisible
        {
            get => _isGroupPileSettlementColorArrowVisible;
            set => SetProperty(ref _isGroupPileSettlementColorArrowVisible, value);
        }

        // 杭要素分割済か否か
        private bool _isElementSplit;
        public bool IsElementSplit
        {
            get => _isElementSplit;
            set
            {
                if (SetProperty(ref _isElementSplit, value))
                {
                    if (value)
                    {
                        // 杭要素分割が完了したら、保留中のSoilPiles生成をキャンセル
                        _generateSoilPilesDebounceTimer?.Stop();
                        _generateSoilPilesDebounceTimer = null;
                        _soilPilesGenerationPending = false;
                    }
                    else
                    {
                        IsForcedDisplacementVisible = false;
                    }

                    // ステータスバー更新
                    OnPropertyChanged(nameof(AnalysisStatusText));
                    OnPropertyChanged(nameof(AnalysisStatusItems));

                    // 水平解析 (F5) と単杭沈下解析 (F6) は杭要素分割が前提。
                    // ボタンもキーボードもこのコマンドの CanExecute で同時に有効/無効が切り替わる。
                    OpenLateralLoadAnalysisWindowCommand.NotifyCanExecuteChanged();
                    OpenSettlementWindowCommand.NotifyCanExecuteChanged();
                }
            }
        }

        // 鉛直解析済か否か
        private bool _isVerticalAnalysisDone;
        public bool IsVerticalAnalysisDone
        {
            get => _isVerticalAnalysisDone;
            set
            {
                if (!SetProperty(ref _isVerticalAnalysisDone, value))
                    return;

                if (value) SetLatestAnalysisCompleted();
                OnPropertyChanged(nameof(HasAnyAnalysisResult));
                // 結果ウィンドウのコマンド (グラフ出力・テーブル出力・ログ表示) を再評価する。
                // CanExecute は HasAnyAnalysisResult を見るが、CommunityToolkit の RelayCommand は
                // 通知しないと聞き直さない。プロパティの通知だけではボタンが押せないままになる。
                RaiseResultCommandsCanExecute();
                // 基礎梁考慮沈下解析の活性化条件が変わるため再評価
                OpenVerticalBeamCalculationCommand?.NotifyCanExecuteChanged();

                const string settlementLabel = "沈下量";

                if (value)
                {
                    if (!AnalysisResultContentOption.Contains(settlementLabel))
                        AddAnalysisContentOption(settlementLabel);
                }
                else
                {
                    RemoveAnalysisContentOption(settlementLabel);
                }

                // ステータスバー更新
                OnPropertyChanged(nameof(AnalysisStatusText));
                OnPropertyChanged(nameof(AnalysisStatusItems));

                // docx 出力 CheckBox の表示更新
                DocxOutput.OnVerticalAnalysisDoneChanged();

                RebuildSettlementTypeOptions();
                UpdateSettlementCategories();
            }
        }

        /// <summary>
        /// 沈下系カテゴリ（沈下部材角/沈下反力/沈下応力）の表示を更新
        /// </summary>
        private void UpdateSettlementCategories()
        {
            bool hasBeams = CurrentInputModel?.FoundationBeamInput?.Beams?.Count > 0;
            bool hasAnySettlement = IsVerticalAnalysisDone || IsGroupPileSettlementAnalysisDone || IsVerticalBeamAnalysisDone;

            void Toggle(string label, bool condition)
            {
                if (condition)
                {
                    if (!AnalysisResultContentOption.Contains(label))
                        AddAnalysisContentOption(label);
                }
                else
                {
                    RemoveAnalysisContentOption(label);
                }
            }

            Toggle("沈下部材角", hasBeams && hasAnySettlement);
            Toggle("沈下反力（地盤）", IsVerticalAnalysisDone || (hasBeams && IsVerticalBeamAnalysisDone));
            Toggle("沈下反力（杭頭集約）", IsVerticalAnalysisDone || (hasBeams && IsVerticalBeamAnalysisDone));
            // 沈下応力 (= 基礎梁の梁応力) は単杭基礎梁解析 OR 個別矩形（基礎梁考慮）反復のいずれかで生成される
            Toggle("沈下応力", hasBeams && (IsVerticalBeamAnalysisDone || HasGroupSettlementBeamAwareCases));
        }

        /// <summary>
        /// 「沈下量」の沈下種別 (単杭 / 群杭 / 単杭+群杭 / 基礎梁考慮 / 基礎梁考慮+群杭) の一覧を、
        /// 解析済みフラグから<b>組み直す</b>。
        ///
        /// 以前は各フラグのセッターが自分の分だけを足し引きしていた。そのため
        /// <list type="bullet">
        /// <item>表示内容を沈下反力などに切り替えると一覧ごと差し替えられ、沈下量に戻っても復元されない</item>
        /// <item>フラグが既に true のまま解析し直しても、値が変わらないのでセッターが走らず足し直されない</item>
        /// </list>
        /// のどちらでも<b>群杭が選べない</b>状態になった。一覧はフラグだけで決まるので、
        /// 継ぎ足しではなく毎回作り直す。
        ///
        /// ItemsSource のインスタンスは差し替えない (差し替えると選択中の項目が飛ぶ)。
        /// </summary>
        private void RebuildSettlementTypeOptions()
        {
            var wanted = new List<string>();
            if (IsVerticalAnalysisDone) wanted.Add("単杭");
            if (IsGroupPileSettlementAnalysisDone) wanted.Add("群杭");
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone) wanted.Add("単杭+群杭");
            if (IsVerticalBeamAnalysisDone) wanted.Add("基礎梁考慮");
            if (IsVerticalBeamAnalysisDone && IsGroupPileSettlementAnalysisDone) wanted.Add("基礎梁考慮+群杭");

            var list = AnalysisResultSettlementOption;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(list[i])) list.RemoveAt(i);
            }
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!list.Contains(wanted[i])) list.Insert(Math.Min(i, list.Count), wanted[i]);
            }

            if (list.Count > 0 && !list.Contains(AnalysisResultSettlementType))
                AnalysisResultSettlementType = list[0];
        }

        /// <summary>テスト用フック (一覧の組み立てをそのまま検証する)。</summary>
        internal void RebuildSettlementTypeOptionsForTest() => RebuildSettlementTypeOptions();

        // 鉛直解析済か否か
        private bool _isGroupPileSettlementAnalysisDone;
        public bool IsGroupPileSettlementAnalysisDone
        {
            get => _isGroupPileSettlementAnalysisDone;
            set
            {
                if (SetProperty(ref _isGroupPileSettlementAnalysisDone, value))
                {
                    if (value) SetLatestAnalysisCompleted();
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));
                    // 結果ウィンドウのコマンド (グラフ出力・テーブル出力・ログ表示) を再評価する。
                    // CanExecute は HasAnyAnalysisResult を見るが、CommunityToolkit の RelayCommand は
                    // 通知しないと聞き直さない。プロパティの通知だけではボタンが押せないままになる。
                    RaiseResultCommandsCanExecute();

                    const string settlementLabel = "沈下量";
                    if (value)
                    {
                        if (!AnalysisResultContentOption.Contains(settlementLabel))
                            AddAnalysisContentOption(settlementLabel);
                    }
                    else
                    {
                        RemoveAnalysisContentOption(settlementLabel);
                    }

                    RebuildSettlementTypeOptions();
                    UpdateSettlementCategories();

                    // docx 出力 CheckBox の表示更新
                    DocxOutput.OnGroupPileSettlementAnalysisDoneChanged();
                }
            }
        }

        // 基礎梁鉛直解析済か否か
        private bool _isVerticalBeamAnalysisDone;
        public bool IsVerticalBeamAnalysisDone
        {
            get => _isVerticalBeamAnalysisDone;
            set
            {
                if (SetProperty(ref _isVerticalBeamAnalysisDone, value))
                {
                    if (value) SetLatestAnalysisCompleted();
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));
                    OnPropertyChanged(nameof(AnalysisStatusText));
                    OnPropertyChanged(nameof(AnalysisStatusItems));
                    OnPropertyChanged(nameof(AvailableLoadingTypeOptions));
                    RaiseResultCommandsCanExecute();

                    if (!value)
                    {
                        VerticalBeamCaseResults = null;

                        // 基礎梁考慮オプションが選択中だった場合は「個別十字」にフォールバック
                        if (CurrentInputModel?.PileGroupSettlement?.LoadingType == "個別十字（基礎梁反力）")
                            CurrentInputModel.PileGroupSettlement.LoadingType = "個別十字";
                    }
                    RebuildSettlementTypeOptions();
                    UpdateSettlementCategories();

                    // docx 出力 CheckBox の表示更新
                    DocxOutput.OnVerticalBeamAnalysisDoneChanged();
                }
            }
        }

        // 荷重タイプコンボボックスに表示するオプション
        //  - 個別十字（基礎梁反力）: 基礎梁鉛直解析 (単杭曲線+NR) の結果を直接使うため VB 解析が必須
        //  - 個別矩形（基礎梁考慮）: Steinbrenner ↔ 線形ばね基礎梁の反復で完結するため基礎梁が必須
        public List<string> AvailableLoadingTypeOptions
        {
            get
            {
                var all = CurrentInputModel?.PileGroupSettlement?.LoadingTypeOptions;
                if (all == null) return [];
                bool hasFoundationBeams = (CurrentInputModel?.FoundationBeamInput?.Beams?.Count ?? 0) > 0;
                var current = CurrentInputModel?.PileGroupSettlement?.LoadingType;
                // 現在の値は条件を満たさなくても残す (基礎梁を消したあとなど)。外すと ComboBox が null を書き戻す
                return [.. all.Where(o =>
                    o == current ||
                    ((o != "個別十字（基礎梁反力）" || IsVerticalBeamAnalysisDone) &&
                     (o != "個別矩形（基礎梁考慮）" || hasFoundationBeams)))];
            }
        }

        /// <summary>
        /// 個別矩形系モードで RectLoads が auto-gen 直後の状態かどうか (transient)。
        /// true: ユーザ未編集 → 荷重面等価径 (GroupPileLoadDia) DataGrid を表示
        /// false: ユーザが矩形を編集済 → GroupPileLoadDia を変更しても整合しないため非表示
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsRectLoadFreshFromAutoGen
        {
            get => _isRectLoadFreshFromAutoGen;
            set => SetProperty(ref _isRectLoadFreshFromAutoGen, value);
        }
        private bool _isRectLoadFreshFromAutoGen = true;

        /// <summary>群杭沈下解析結果のケースレコードが 1 つ以上あるか (XAML Visibility 用)。</summary>
        public bool HasGroupSettlementCaseRecords =>
            (CurrentInputModel?.PileGroupSettlement?.CaseRecords?.Count ?? 0) > 0;

        /// <summary>群杭沈下のアクティブケースが基礎梁考慮反復の結果か (XAML バッジ用)。</summary>
        public bool IsGroupSettlementActiveCaseBeamAware
        {
            get
            {
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs?.CaseRecords == null) return false;
                int idx = pgs.ActiveCaseIndex;
                if (idx < 0 || idx >= pgs.CaseRecords.Count) return false;
                return pgs.CaseRecords[idx].IsBeamAware;
            }
        }

        /// <summary>
        /// 矩形荷重の手動追加・削除を許可するか。
        /// 個別矩形（基礎梁考慮）では矩形荷重は反復解析で自動生成・収束されるため、
        /// 手動編集を禁止して整合性を保つ。
        /// </summary>
        public bool IsManualRectLoadEditingEnabled
            => CurrentInputModel?.PileGroupSettlement?.LoadingType != "個別矩形（基礎梁考慮）";

        /// <summary>
        /// 群杭沈下ウィンドウの「解析」タブで、基礎梁ありの解析を選んでいるか (画面の状態。保存しない)。
        /// 基礎梁ありのときだけ、反復の条件と注意を出す。
        /// </summary>
        private bool _isGroupSettlementWithBeamSelected;
        public bool IsGroupSettlementWithBeamSelected
        {
            get => _isGroupSettlementWithBeamSelected;
            set => SetProperty(ref _isGroupSettlementWithBeamSelected, value);
        }

        /// <summary>基礎梁考慮反復の CaseRecord が 1 件以上あるか。</summary>
        public bool HasGroupSettlementBeamAwareCases
        {
            get
            {
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs?.CaseRecords == null) return false;
                return pgs.CaseRecords.Any(r => r.IsBeamAware);
            }
        }

        /// <summary>
        /// 現在 CaseRecords に保持されている結果タイプ (LoadingType) の一覧 (重複なし、追加順)。
        /// 解析結果メニューの「結果タイプ」ComboBox の ItemsSource に使う。
        /// </summary>
        public ObservableCollection<string> AvailableActiveLoadingTypes
        {
            get
            {
                var result = new ObservableCollection<string>();
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs?.CaseRecords == null) return result;
                foreach (var rec in pgs.CaseRecords)
                {
                    var lt = string.IsNullOrEmpty(rec.LoadingType)
                        ? (rec.IsBeamAware ? "個別矩形（基礎梁考慮）" : "")
                        : rec.LoadingType;
                    if (!string.IsNullOrEmpty(lt) && !result.Contains(lt))
                        result.Add(lt);
                }
                return result;
            }
        }

        /// <summary>
        /// 荷重条件の 荷重面Z 入力用プロキシ。基礎梁なし・ありの両方の解析が使う 1 つの値。
        /// 値を変えるとき、どちらかの解析結果が保存されていれば確かめ、OK で結果を削除してから値を反映する。
        /// 変更の前後は履歴に積み、群杭沈下の結果に再解析が必要の印を付ける (以前は履歴にも未保存の印にも残らなかった)。
        /// </summary>
        public double LoadingPlaneAltitudeProxy
        {
            get => CurrentInputModel?.PileGroupSettlement?.LoadingPlaneAltitude ?? 0.0;
            set
            {
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs == null) return;
                if (Math.Abs(pgs.LoadingPlaneAltitude - value) < 1e-9) return;
                if (!ConfirmAnalysisConditionChange("両方", "荷重面Z")) {
                    OnPropertyChanged(nameof(LoadingPlaneAltitudeProxy));
                    return;
                }
                var before = CaptureInputEdit();
                pgs.LoadingPlaneAltitude = value;
                CompleteInputEdit(before, "荷重面Z 変更", AnalysisInputScope.Settlement);
                OnPropertyChanged(nameof(LoadingPlaneAltitudeProxy));
                UpdateCanvas3DAction?.Invoke();
            }
        }

        /// <summary>
        /// 解析条件変更時の確認ダイアログ + 結果削除ヘルパ。
        /// </summary>
        /// <param name="route">対象ルート: "一般"=非beam-aware, "反復"=beam-aware, "両方"=両ルート (土層やグリッドなど共通入力)</param>
        /// <param name="itemLabel">変更対象の項目名 (ダイアログメッセージに使用)</param>
        /// <returns>true=変更を続行, false=ユーザーがキャンセル</returns>
        internal bool ConfirmAnalysisConditionChange(string route, string itemLabel)
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return true;

            List<GroupSettlementCaseRecord> doomed;
            if (route == "両方")
            {
                doomed = [.. pgs.CaseRecords];
            }
            else
            {
                bool beamAware = route == "反復";
                doomed = [.. pgs.CaseRecords.Where(r => r.IsBeamAware == beamAware)];
            }
            if (doomed.Count == 0) return true; // 該当結果なし → そのまま続行

            var res = PileDesign.Services.MessageService.Show(
                $"{(route == "両方" ? "群杭沈下解析" : route == "反復" ? "群杭沈下解析（基礎梁あり）" : "群杭沈下解析（基礎梁なし）")}の結果が保存されています。\n" +
                $"「{itemLabel}」を変えると、この結果は使えなくなるため削除します。\n\n" +
                $"続けますか？",
                "解析条件変更の確認",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning);
            if (res != System.Windows.MessageBoxResult.OK) return false;

            // 該当ルートの CaseRecord を削除
            pgs.Result = pgs.Result.Without(doomed);
            EnsureSettlementResultSharedWithSnapshot();

            // ActiveCase が無効なら -1、Legacy フィールドも対応してクリア
            if (pgs.ActiveCaseIndex >= pgs.CaseRecords.Count)
                pgs.ActiveCaseIndex = pgs.CaseRecords.Count - 1;
            bool activeWasDeleted;
            if (route == "両方") activeWasDeleted = true;
            else
            {
                bool beamAware = route == "反復";
                activeWasDeleted = (pgs.ActiveLoadingType == "個別矩形（基礎梁考慮）") == beamAware;
            }
            if (activeWasDeleted)
            {
                pgs.ActiveCaseIndex = -1;
                pgs.SettlementGridData = [];
                if (CurrentInputModel?.PileLayoutItems != null)
                    foreach (var pile in CurrentInputModel.PileLayoutItems) pile.NotifyGroupPileSettlementChanged();
            }

            // 通知 + Canvas 更新
            OnPropertyChanged(nameof(HasGroupSettlementCaseRecords));
            OnPropertyChanged(nameof(HasGroupSettlementBeamAwareCases));
            OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
            OnPropertyChanged(nameof(AvailableActiveLoadingTypes));
            OnPropertyChanged(nameof(GroupSettlementRouteOptions));
            OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            UpdateCanvas3DAction?.Invoke();
            return true;
        }

        /// <summary>
        /// 群杭沈下「基礎梁なし / 基礎梁あり」切替 ComboBox 用 (表示メニュー荷重ケースグループ + 右ペイン用)。
        /// 解析有無に関わらず両方の選択肢を常に表示 (荷重描画ルートの切替にも使うため)。
        /// 以前の「一般 / 反復」は計算方法の名前で、何を選んでいるか (基礎梁を考えるか) が読み取れなかった。
        /// </summary>
        public ObservableCollection<string> GroupSettlementRouteOptions { get; }
            = [RouteWithoutBeam, RouteWithBeam];

        internal const string RouteWithoutBeam = "基礎梁なし";
        internal const string RouteWithBeam = "基礎梁あり";

        /// <summary>
        /// 群杭沈下「一般 / 反復」切替の SelectedItem。内部 ActiveLoadingType と双方向マッピング。
        /// "個別矩形（基礎梁考慮）" → "反復"、それ以外 (基礎梁無し系) → "一般"。
        /// </summary>
        public string GroupSettlementRouteSelector
        {
            get
            {
                var lt = CurrentInputModel?.PileGroupSettlement?.ActiveLoadingType ?? "";
                return lt == "個別矩形（基礎梁考慮）" ? RouteWithBeam : RouteWithoutBeam;
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                string current = GroupSettlementRouteSelector;
                if (current == value) return;

                if (value == RouteWithBeam)
                {
                    SelectedActiveLoadingType = "個別矩形（基礎梁考慮）";
                }
                else // "一般"
                {
                    var pgs = CurrentInputModel?.PileGroupSettlement;
                    string target = pgs?.CaseRecords?
                        .FirstOrDefault(r => !r.IsBeamAware)?.LoadingType
                        ?? "任意矩形";
                    SelectedActiveLoadingType = target;
                }
                OnPropertyChanged(nameof(GroupSettlementRouteSelector));
            }
        }

        /// <summary>
        /// 「結果タイプ」ComboBox の SelectedItem 用プロキシ。
        /// PileGroupSettlement.ActiveLoadingType に対する MVVM ラッパで、
        /// 切替時にアクティブケースを再評価し、legacy フィールドへ反映する。
        /// </summary>
        public string SelectedActiveLoadingType
        {
            get => CurrentInputModel?.PileGroupSettlement?.ActiveLoadingType ?? "";
            set
            {
                var pgs = CurrentInputModel?.PileGroupSettlement;
                if (pgs == null) return;
                if (pgs.ActiveLoadingType == value) return;

                // 反復解析は pgs.RectLoads を上書きしないので、入力を退避する必要は無い。
                pgs.ActiveLoadingType = value ?? "";

                // 新タイプの中から SelectedLoadCaseName に対応するケースを探して切替。
                // 一致するケースが無ければコンタを消去 (1回解析の結果を地震時ケースに誤って表示しないため)。
                if (pgs.CaseRecords != null && pgs.CaseRecords.Count > 0)
                {
                    int idx = FindMatchingCaseRecordIndex(pgs.CaseRecords, SelectedLoadCaseName, pgs.ActiveLoadingType);
                    if (idx >= 0)
                    {
                        pgs.ActiveCaseIndex = idx;
                        GroupSettlementWithBeamCalculationViewModel.ApplyActiveCaseToLegacyFields(
                pgs, pgs.CaseRecords[idx], CurrentInputModel?.PileLayoutItems);
                    }
                    else
                    {
                        pgs.ActiveCaseIndex = -1;
                        pgs.SettlementGridData = [];

                        if (CurrentInputModel?.PileLayoutItems != null)
                            foreach (var pile in CurrentInputModel.PileLayoutItems) pile.NotifyGroupPileSettlementChanged();
                    }
                }

                OnPropertyChanged(nameof(SelectedActiveLoadingType));
                OnPropertyChanged(nameof(IsGroupSettlementActiveCaseBeamAware));
                OnPropertyChanged(nameof(GroupSettlementRouteSelector));
                UpdateCanvas3DAction?.Invoke();
            }
        }

        /// <summary>
        /// 基礎梁鉛直解析または個別矩形（基礎梁考慮）反復のアクティブケース結果を、
        /// 既存描画コード (DrawVBBeamForce / DrawVBDeformedElements 等) 用の
        /// VerticalBeamCaseResult 形式で返す。
        /// 個別矩形（基礎梁考慮）の CaseRecord が active なら CaseRecord から合成、
        /// そうでなければ既存 VerticalBeamCaseResults から名前マッチで取得。
        /// </summary>
        public FEM.VerticalBeamCaseResult GetActiveVerticalBeamCaseResult()
        {
            // 個別矩形（基礎梁考慮）のアクティブケースを優先
            var pgs = CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords != null
                && pgs.ActiveCaseIndex >= 0
                && pgs.ActiveCaseIndex < pgs.CaseRecords.Count
                && pgs.CaseRecords[pgs.ActiveCaseIndex].IsBeamAware)
            {
                var rec = pgs.CaseRecords[pgs.ActiveCaseIndex];
                var synth = new FEM.VerticalBeamCaseResult
                {
                    LoadCaseName = rec.LoadCaseName,
                    IsConverged = rec.IsConverged,
                };
                if (rec.NodeResults != null) synth.NodeResults.AddRange(rec.NodeResults);
                if (rec.BeamResults != null) synth.BeamResults.AddRange(rec.BeamResults);
                if (CurrentInputModel?.PileLayoutItems != null)
                {
                    foreach (var pile in CurrentInputModel.PileLayoutItems)
                    {
                        rec.PileSettlements_mm.TryGetValue(pile.PileNo, out double settlement);
                        rec.PileReactions_kN.TryGetValue(pile.PileNo, out double reaction);
                        synth.PileResults.Add(new FEM.VerticalBeamPileResult(
                            pile.No, pile.X, pile.Y, reaction, reaction, settlement));
                    }
                }
                return synth;
            }

            // フォールバック: 既存 VerticalBeamCaseResults
            if (VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0) return null;
            string selectedName = SelectedLoadCaseName;
            return VerticalBeamCaseResults.FirstOrDefault(c =>
                       ExtractVBCaseBaseName(c.LoadCaseName) == selectedName)
                   ?? VerticalBeamCaseResults[0];
        }

        /// <summary>VB 解析の LoadCaseName ("1-1: U1" 等) からベース名を抽出。</summary>
        private static string ExtractVBCaseBaseName(string caseName)
        {
            if (string.IsNullOrEmpty(caseName)) return caseName ?? string.Empty;
            int colonIdx = caseName.IndexOf(": ", StringComparison.Ordinal);
            string stripped = colonIdx >= 0 ? caseName[(colonIdx + 2)..].Trim() : caseName;
            int parenIdx = stripped.IndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0) stripped = stripped[..parenIdx].Trim();
            return stripped;
        }

        // 基礎梁鉛直解析結果
        private ObservableCollection<FEM.VerticalBeamCaseResult> _verticalBeamCaseResults;
        public ObservableCollection<FEM.VerticalBeamCaseResult> VerticalBeamCaseResults
        {
            get => _verticalBeamCaseResults;
            set => SetProperty(ref _verticalBeamCaseResults, value);
        }

        // 水平解析済か否か
        private bool _isHorizontalAnalysisDone;
        public bool IsHorizontalAnalysisDone
        {
            get => _isHorizontalAnalysisDone;
            set
            {
                if (SetProperty(ref _isHorizontalAnalysisDone, value))
                {
                    if (value) SetLatestAnalysisCompleted();
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));
                    // 結果ウィンドウのコマンド (グラフ出力・テーブル出力・ログ表示) を再評価する。
                    // CanExecute は HasAnyAnalysisResult を見るが、CommunityToolkit の RelayCommand は
                    // 通知しないと聞き直さない。プロパティの通知だけではボタンが押せないままになる。
                    RaiseResultCommandsCanExecute();

                    // "梁応力（水平）"の表示制御
                    const string beamForceLabel = "梁応力（水平）";
                    const string nodeDisplacementLabel = "節点変位（水平）";
                    const string nodeSoilSpringLabel = "地盤反力（水平）";
                    const string nodeSoilSpringDistLabel = "地盤反力（分布）";
                    const string pileHeadMLabel = "杭頭Mマップ";
                    const string pileHeadQLabel = "杭頭Qマップ";
                    const string connectionMLabel = "接合点Mマップ";
                    const string connectionQLabel = "接合点Qマップ";
                    if (value)
                    {
                        // true: がなければ追加
                        if (!AnalysisResultContentOption.Contains(beamForceLabel))
                            AddAnalysisContentOption(beamForceLabel);
                        if (!AnalysisResultContentOption.Contains(nodeDisplacementLabel))
                            AddAnalysisContentOption(nodeDisplacementLabel);
                        if (!AnalysisResultContentOption.Contains(nodeSoilSpringLabel))
                            AddAnalysisContentOption(nodeSoilSpringLabel);
                        if (!AnalysisResultContentOption.Contains(nodeSoilSpringDistLabel))
                            AddAnalysisContentOption(nodeSoilSpringDistLabel);
                        if (!AnalysisResultContentOption.Contains(pileHeadMLabel))
                            AddAnalysisContentOption(pileHeadMLabel);
                        if (!AnalysisResultContentOption.Contains(pileHeadQLabel))
                            AddAnalysisContentOption(pileHeadQLabel);
                        if (!AnalysisResultContentOption.Contains(connectionMLabel))
                            AddAnalysisContentOption(connectionMLabel);
                        if (!AnalysisResultContentOption.Contains(connectionQLabel))
                            AddAnalysisContentOption(connectionQLabel);
                    }
                    else
                    {
                        // false: があれば削除
                        RemoveAnalysisContentOption(beamForceLabel);
                        RemoveAnalysisContentOption(nodeDisplacementLabel);
                        RemoveAnalysisContentOption(nodeSoilSpringLabel);
                        RemoveAnalysisContentOption(nodeSoilSpringDistLabel);
                        RemoveAnalysisContentOption(pileHeadMLabel);
                        RemoveAnalysisContentOption(pileHeadQLabel);
                        RemoveAnalysisContentOption(connectionMLabel);
                        RemoveAnalysisContentOption(connectionQLabel);
                    }

                    // 解析済みインジケータ更新
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(IsCurrentNonLiquefactionAnalyzed));
                    OnPropertyChanged(nameof(IsCurrentLiquefactionAnalyzed));

                    // ステータスバー更新
                    OnPropertyChanged(nameof(AnalysisStatusText));
                    OnPropertyChanged(nameof(AnalysisStatusItems));

                    // docx 出力 CheckBox の表示更新
                    // (実体は保持されているが getter で IsHorizontalAnalysisDone を AND しているため)
                    DocxOutput.OnHorizontalAnalysisDoneChanged();
                }
            }
        }

        // 要素タイプオプション
        private List<string> _elementTypeOption = ["ダミー"];
        public List<string> ElementTypeOption
        {
            get => _elementTypeOption;
            set => SetProperty(ref _elementTypeOption, value);
        }

        // 要素タイプ
        private string _elementType = "ダミー";
        public string ElementType
        {
            get => _elementType;
            set => SetProperty(ref _elementType, value);
        }

        // マージ対象限界距離
        private double _editDistanceThreshold = 0.005;
        private string? _editDistanceThresholdProblem;
        public double EditDistanceThreshold
        {
            get => _editDistanceThreshold;
            set
            {
                var problem = PileDesign.Services.MoveCopyValidation.DescribeToleranceProblem(value);
                _editDistanceThresholdProblem = problem;
                if (problem != null) throw new System.ArgumentOutOfRangeException(nameof(value), problem);
                SetProperty(ref _editDistanceThreshold, value);
            }
        }

        // 等分割数
        private int _equalDivisionCount = 2;
        public int EqualDivisionCount
        {
            get => _equalDivisionCount;
            set => SetProperty(ref _equalDivisionCount, Math.Max(2, value));
        }


        public TextBox TextBoxElementNodeInput { get; set; }

        // 要素縮小表示
        private bool _isShrinkElementMode = false;
        public bool IsShrinkElementMode
        {
            get => _isShrinkElementMode;
            set
            {
                if (SetProperty(ref _isShrinkElementMode, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 交差選択窓モード
        private bool _isCrossSelectionMode = false;
        public bool IsCrossSelectionMode
        {
            get => _isCrossSelectionMode;
            set => SetProperty(ref _isCrossSelectionMode, value);
        }

        // 目盛り帯の幅
        private double _tickZoneWidth = 35;
        public double TickZoneWidth
        {
            get => _tickZoneWidth;
            set => SetProperty(ref _tickZoneWidth, value);
        }

        // 目盛り文字位置
        public double TickTextPos => TickZoneWidth - 5;


        // 通り心シンボル径
        private double _gridSymbolCircleDia = 20;
        public double GridSymbolCircleDia
        {
            get => _gridSymbolCircleDia;
            set => SetProperty(ref _gridSymbolCircleDia, value);
        }

        // 通り心帯の幅
        public double GridSymbolZoneWidth => GridSymbolCircleDia * 1.5;

        // 土層ライン幅
        private double _soilStrokeThickness = 0.75;
        public double SoilStrokeThickness
        {
            get => _soilStrokeThickness;
            set
            {
                if (SetProperty(ref _soilStrokeThickness, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 杭ライン幅
        private double _pileStrokeThickness = 1;
        public double PileStrokeThickness
        {
            get => _pileStrokeThickness;
            set
            {
                if (SetProperty(ref _pileStrokeThickness, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 群杭沈下グリッド表示 //
        private bool _isGroupPileGridVisible;
        public bool IsGroupPileGridVisible
        {
            get => _isGroupPileGridVisible;
            set
            {
                if (SetProperty(ref _isGroupPileGridVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // 群杭沈下グリッド変位表示 //
        private bool _isGroupPileGridDeformationVisible;
        public bool IsGroupPileGridDeformationVisible
        {
            get => _isGroupPileGridDeformationVisible;
            set
            {
                if (SetProperty(ref _isGroupPileGridDeformationVisible, value))
                {
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }


        // 群杭沈下 //
        private double _groupPileSettlementXOffset;
        public double GroupPileSettlementXOffset
        {
            get => _groupPileSettlementXOffset;
            set
            {
                if (Math.Abs(_groupPileSettlementXOffset - value) < 1e-9) return;
                if (!ConfirmAnalysisConditionChange("両方", "グリッド X オフセット"))
                {
                    OnPropertyChanged(nameof(GroupPileSettlementXOffset));
                    return;
                }
                if (SetProperty(ref _groupPileSettlementXOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    RequestUpdateWindow();
                }
            }
        }

        private double _groupPileSettlementYOffset;
        public double GroupPileSettlementYOffset
        {
            get => _groupPileSettlementYOffset;
            set
            {
                if (Math.Abs(_groupPileSettlementYOffset - value) < 1e-9) return;
                if (!ConfirmAnalysisConditionChange("両方", "グリッド Y オフセット"))
                {
                    OnPropertyChanged(nameof(GroupPileSettlementYOffset));
                    return;
                }
                if (SetProperty(ref _groupPileSettlementYOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    RequestUpdateWindow();
                }
            }
        }

        // 群杭沈下 //
        public double GroupPileSettlementXMin
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Min(pile => pile.X);
            }
        }

        public double GroupPileSettlementXMax
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Max(pile => pile.X);
            }
        }

        public double GroupPileSettlementYMin
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Min(pile => pile.Y);
            }
        }

        public double GroupPileSettlementYMax
        {
            get
            {
                // 杭が1本もない場合は0.0（またはdouble.NaN等）を返す
                if (CurrentInputModel?.PileLayoutItems == null || CurrentInputModel.PileLayoutItems.Count == 0)
                    return 0.0;

                return CurrentInputModel.PileLayoutItems.Max(pile => pile.Y);
            }
        }



        private double _groupPileSettlementXSpacing = 1.8;
        public double GroupPileSettlementXSpacing
        {
            get => _groupPileSettlementXSpacing;
            set
            {
                if (Math.Abs(_groupPileSettlementXSpacing - value) < 1e-9) return;
                if (!ConfirmAnalysisConditionChange("両方", "グリッド X 間隔"))
                {
                    OnPropertyChanged(nameof(GroupPileSettlementXSpacing));
                    return;
                }
                if (SetProperty(ref _groupPileSettlementXSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    RequestUpdateWindow();
                }
            }
        }

        private double _groupPileSettlementYSpacing = 1.8;
        public double GroupPileSettlementYSpacing
        {
            get => _groupPileSettlementYSpacing;
            set
            {
                if (Math.Abs(_groupPileSettlementYSpacing - value) < 1e-9) return;
                if (!ConfirmAnalysisConditionChange("両方", "グリッド Y 間隔"))
                {
                    OnPropertyChanged(nameof(GroupPileSettlementYSpacing));
                    return;
                }
                if (SetProperty(ref _groupPileSettlementYSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        // QuickHintの表示制御 //
        private bool _isQuickHintVisible;
        public bool IsQuickHintVisible
        {
            get => _isQuickHintVisible;
            set
            {
                if (SetProperty(ref _isQuickHintVisible, value))
                {

                }
            }
        }

        // 変位コンター図キャッシュ
        private PathGeometry _cachedSettlementGridGeometry;
        public PathGeometry CachedSettlementGridGeometry
        {
            get => _cachedSettlementGridGeometry;
            set => SetProperty(ref _cachedSettlementGridGeometry, value);
        }

        private bool _isSettlementGridCacheValid = false;
        public bool IsSettlementGridCacheValid
        {
            get => _isSettlementGridCacheValid;
            set => SetProperty(ref _isSettlementGridCacheValid, value);
        }

    }
}
