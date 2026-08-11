using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 計算書 (docx) 出力設定の専用 ViewModel。
    /// DocxOutputWindow のチェックボックス（出力セクションの Include* フラグ・計算書レベル・
    /// 液状化出力・杭応力のまとめ方）と、一括選択/解除・出力前検証を担う。
    /// MainWindowViewModel.DocxOutput として公開され、解析完了状態（IsXxxAnalysisDone）と
    /// 荷重ケース（CurrentInputModel.LoadCasesInput）は親 (_main) を参照する。
    /// 実際の Word 生成は Output/WordDocument.*（OutputWordFile コマンドは MainWindowViewModel）。
    /// これらのフラグはプロジェクトファイルには保存されない（セッション限り）。
    /// </summary>
    public partial class DocxOutputViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _main;

        public DocxOutputViewModel(MainWindowViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));
        }

        [ObservableProperty] private int calculationReportLevel = 1;
        [ObservableProperty] private bool includeGroundInformation = true;
        [ObservableProperty] private bool includeLiquefaction = false;
        // 解析未完了 OR 親 OFF 時は getter で false を返す → CheckBox が「未チェック+灰色」表示になり
        // 「チェック付き+灰色」の違和感を解消。実体 (_includeXxx) は保持されるので解析後/親 ON 時に
        // 自動的に元の意図 (true/false) が復活する。
        // 親 (IncludeHorizontal/IncludeVertical) の setter で全子の OnPropertyChanged を発火させ、
        // 子の IsEnabled は CanEditHorizontalChildren / CanEditVerticalChildren にバインドする。
        private bool _includeHorizontal = true;
        public bool IncludeHorizontal
        {
            get => _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set
            {
                if (_includeHorizontal != value)
                {
                    _includeHorizontal = value;
                    OnPropertyChanged();
                    NotifyHorizontalChildrenChanged();
                }
            }
        }
        private bool _includeVertical = true;
        public bool IncludeVertical
        {
            get => _includeVertical && _main.IsVerticalAnalysisDone;
            set
            {
                if (_includeVertical != value)
                {
                    _includeVertical = value;
                    OnPropertyChanged();
                    NotifyVerticalChildrenChanged();
                }
            }
        }

        // 子 CheckBox の IsEnabled バインド用 — 親 ON かつ 解析完了 の時のみ編集可
        public bool CanEditHorizontalChildren => _includeHorizontal && _main.IsHorizontalAnalysisDone;
        public bool CanEditVerticalChildren => _includeVertical && _main.IsVerticalAnalysisDone;

        // ===== 解析完了状態の変更を親 (MainWindowViewModel) から通知するフック =====
        // IsXxxAnalysisDone の setter から呼ばれ、ゲート付き getter を持つプロパティの再評価を促す。
        internal void OnHorizontalAnalysisDoneChanged()
        {
            OnPropertyChanged(nameof(IncludeHorizontal));
            NotifyHorizontalChildrenChanged();
        }
        internal void OnVerticalAnalysisDoneChanged()
        {
            OnPropertyChanged(nameof(IncludeVertical));
            NotifyVerticalChildrenChanged();
        }
        internal void OnGroupPileSettlementAnalysisDoneChanged()
            => OnPropertyChanged(nameof(IncludeGroupPileSettlement));
        internal void OnVerticalBeamAnalysisDoneChanged()
            => OnPropertyChanged(nameof(IncludeVerticalBeamResults));

        private void NotifyHorizontalChildrenChanged()
        {
            OnPropertyChanged(nameof(IncludeHorizontal_Bending));
            OnPropertyChanged(nameof(IncludeHorizontal_Shear));
            OnPropertyChanged(nameof(IncludeHorizontal_NMinT));
            OnPropertyChanged(nameof(IncludeHorizontal_QNInT));
            OnPropertyChanged(nameof(IncludeHorizontal_MPhi));
            OnPropertyChanged(nameof(IncludeHorizontal_MTheta));
            OnPropertyChanged(nameof(IncludeHorizontal_NGReport));
            OnPropertyChanged(nameof(IncludeHorizontal_StressLimitState));
            OnPropertyChanged(nameof(IncludeAnalysisSummaryReport));
            OnPropertyChanged(nameof(IncludePileHeadMomentMap));
            OnPropertyChanged(nameof(IncludePileHeadShearMap));
            OnPropertyChanged(nameof(CanEditHorizontalChildren));
        }
        private void NotifyVerticalChildrenChanged()
        {
            OnPropertyChanged(nameof(IncludeSettlement));
            OnPropertyChanged(nameof(CanEditVerticalChildren));
        }

        // 水平検討の子フラグ — 親 (_includeHorizontal) が true かつ 解析完了 の時のみ display=true
        private bool _includeHorizontal_Bending = true;
        public bool IncludeHorizontal_Bending
        {
            get => _includeHorizontal_Bending && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_Bending != value) { _includeHorizontal_Bending = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_Shear = true;
        public bool IncludeHorizontal_Shear
        {
            get => _includeHorizontal_Shear && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_Shear != value) { _includeHorizontal_Shear = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_NMinT = true;
        public bool IncludeHorizontal_NMinT
        {
            get => _includeHorizontal_NMinT && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_NMinT != value) { _includeHorizontal_NMinT = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_QNInT = true;
        public bool IncludeHorizontal_QNInT
        {
            get => _includeHorizontal_QNInT && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_QNInT != value) { _includeHorizontal_QNInT = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_MPhi = true;
        public bool IncludeHorizontal_MPhi
        {
            get => _includeHorizontal_MPhi && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_MPhi != value) { _includeHorizontal_MPhi = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_MTheta = true;
        public bool IncludeHorizontal_MTheta
        {
            get => _includeHorizontal_MTheta && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_MTheta != value) { _includeHorizontal_MTheta = value; OnPropertyChanged(); } }
        }
        private bool _includeHorizontal_NGReport = true;
        public bool IncludeHorizontal_NGReport
        {
            get => _includeHorizontal_NGReport && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_NGReport != value) { _includeHorizontal_NGReport = value; OnPropertyChanged(); } }
        }
        // 杭変位・応力ダイアグラムへの限界状態線重ね描き (default ON)
        // - レベル1 → 損傷限界
        // - レベル2 + 耐震グレード S → 損傷限界
        // - レベル2 + 耐震グレード A → 安全限界
        // 親 (曲げモーメント検討/せん断力検討 = IncludeHorizontal_Bending/Shear) のいずれかが ON のとき意味あり
        private bool _includeHorizontal_StressLimitState = true;
        public bool IncludeHorizontal_StressLimitState
        {
            get => _includeHorizontal_StressLimitState && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeHorizontal_StressLimitState != value) { _includeHorizontal_StressLimitState = value; OnPropertyChanged(); } }
        }

        [ObservableProperty] private bool includePileLocationMap = false;
        [ObservableProperty] private bool includePileAxialLoadMap = false;
        [ObservableProperty] private bool includeIsFrontMap = false;
        // 杭頭M/Qマップは XAML 上「水平検討」GroupBox 内の子。親 + 解析完了 でガード
        private bool _includePileHeadMomentMap = false;
        public bool IncludePileHeadMomentMap
        {
            get => _includePileHeadMomentMap && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includePileHeadMomentMap != value) { _includePileHeadMomentMap = value; OnPropertyChanged(); } }
        }
        private bool _includePileHeadShearMap = false;
        public bool IncludePileHeadShearMap
        {
            get => _includePileHeadShearMap && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includePileHeadShearMap != value) { _includePileHeadShearMap = value; OnPropertyChanged(); } }
        }
        // 沈下系: 親 (IncludeVertical) + 解析完了 でガード
        private bool _includeSettlement = true;
        public bool IncludeSettlement
        {
            get => _includeSettlement && _includeVertical && _main.IsVerticalAnalysisDone;
            set { if (_includeSettlement != value) { _includeSettlement = value; OnPropertyChanged(); } }
        }

        private bool _includeGroupPileSettlement = false;
        public bool IncludeGroupPileSettlement
        {
            get => _includeGroupPileSettlement && _main.IsGroupPileSettlementAnalysisDone;
            set { if (_includeGroupPileSettlement != value) { _includeGroupPileSettlement = value; OnPropertyChanged(); } }
        }
        private bool _includeVerticalBeamResults = false;
        public bool IncludeVerticalBeamResults
        {
            get => _includeVerticalBeamResults && _main.IsVerticalBeamAnalysisDone;
            set { if (_includeVerticalBeamResults != value) { _includeVerticalBeamResults = value; OnPropertyChanged(); } }
        }

        // Phase 1: 地盤グラフ (5 件) — DocxOutputWindow チェックで個別 ON/OFF
        [ObservableProperty] private bool includeNValueGraph = false;
        [ObservableProperty] private bool includeCuGraph = false;
        [ObservableProperty] private bool includeVsGraph = false;
        [ObservableProperty] private bool includeEsGraph = false;
        [ObservableProperty] private bool includeFLGraph = false;

        // Phase 1: 杭関連の図/表 (5 件)
        [ObservableProperty] private bool includePileElevation = false;       // 杭姿図 (杭体ごと)
        [ObservableProperty] private bool includePileSectionDiagram = false;  // 杭断面図 (杭断面ごと)
        [ObservableProperty] private bool includePileTopView = false;         // 杭頭上面図
        [ObservableProperty] private bool includeAxialLimitTable = false;     // 軸力制限テーブル
        [ObservableProperty] private bool includePileTopSpecs = false;        // 杭頭諸元テーブル

        // Phase 2: 中優先項目
        [ObservableProperty] private bool includeGroundDisplacementGraph = false;  // 任意地盤変位グラフ
        [ObservableProperty] private bool includeResponseSpectrumGraph = false;    // 応答スペクトルグラフ

        // Phase 3: 要素分割関連 (3 件)
        [ObservableProperty] private bool includeElementDivisionPileShape = false;       // 要素分割杭姿図 (分割点マーク付き)
        [ObservableProperty] private bool includeHorizontalSoilReactionGraph = false;    // 水平地盤反力分布グラフ
        [ObservableProperty] private bool includeDoatsuGoryokuBaneGraph = false;         // 土圧合力ばね分布グラフ
        private bool _includeAnalysisSummaryReport = false;
        public bool IncludeAnalysisSummaryReport
        {
            get => _includeAnalysisSummaryReport && _includeHorizontal && _main.IsHorizontalAnalysisDone;
            set { if (_includeAnalysisSummaryReport != value) { _includeAnalysisSummaryReport = value; OnPropertyChanged(); } }
        }

        // 入力データ基本セクション (7 件) — これまで無条件出力だったものを CheckBox 制御化。
        // 既存ユーザの体感を変えないため初期値は true (出力する)。
        [ObservableProperty] private bool includeFundamental = true;       // 基本設定
        [ObservableProperty] private bool includeLoadCondition = true;     // 荷重条件
        [ObservableProperty] private bool includePileBodies = true;        // 杭体 (杭体明細表を含む)
        [ObservableProperty] private bool includePileLayoutTable = true;   // 杭配置
        [ObservableProperty] private bool includePileAxialLoad = true;     // 杭軸力
        [ObservableProperty] private bool includeIsFrontPile = true;       // 前後方杭
        [ObservableProperty] private bool includeDesignApproach = true;    // 検討方針 (杭頭接続仮定を含む)
        [ObservableProperty] private bool includeAssumptions = true;       // 計算条件・仮定 (材料モデル化オプション等。レベル非依存)

        // 水平解析完了時に HorizontalCalculationViewModel が cache する解析サマリーテキスト
        // (docx 出力で先頭の "━━━ 解析サマリーレポート ━━━" ブロックを再利用するため)
        public string LastAnalysisSummaryText { get; set; }

        // 液状化有無の出力オプション
        [ObservableProperty] private bool includeOutputLiquefactionYes = true;
        [ObservableProperty] private bool includeOutputLiquefactionNo = true;

        // 杭変位応力ダイアグラムのグループ化
        // false: 杭ごとに個別の図（既定・従来動作）
        // true: 杭地盤セット（ElementDivision.SoilPiles）ごとに 1 図にまとめ、同一セット内の杭は系列オーバーレイ
        [ObservableProperty] private bool groupPileStressBySoilPile = false;
        [ObservableProperty] private bool isLiquefactionYesAnalyzed = false;
        [ObservableProperty] private bool isLiquefactionNoAnalyzed = false;

        // 出力内容セクション（Include* の bool プロパティ）と荷重ケースを一括で選択/解除する。
        [RelayCommand]
        private void SelectAllDocxSections() => SetAllDocxSections(true);

        [RelayCommand]
        private void DeselectAllDocxSections() => SetAllDocxSections(false);

        private void SetAllDocxSections(bool value)
        {
            // Include* の bool プロパティを反射で一括設定。解析未完了で無効な項目は
            // getter 側でゲートされるため、値の設定自体は安全（表示は解析状態に追随）。
            foreach (var p in GetType().GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (p.PropertyType == typeof(bool) && p.CanRead && p.CanWrite
                    && p.GetSetMethod() != null
                    && p.Name.StartsWith("Include", StringComparison.Ordinal))
                {
                    try { p.SetValue(this, value); }
                    catch (Exception ex) { Serilog.Log.Debug(ex, "[Docx] 出力項目の一括設定に失敗: {Prop}", p.Name); }
                }
            }
            // 荷重ケース・組合せも一括
            var lci = _main.CurrentInputModel?.LoadCasesInput;
            if (lci != null)
            {
                foreach (var c in lci.LoadCasesLevel1) c.IsApplicableForDocxDisplay = value;
                foreach (var c in lci.LoadCasesLevel2) c.IsApplicableForDocxDisplay = value;
                foreach (var c in lci.LoadCombinations) c.IsApplicableForDocxDisplay = value;
            }
        }

        // 出力前チェック: 荷重ケース未選択 / 液状化未選択 なら警告して続行可否を確認する。
        // OK ボタン（code-behind）から呼び、No のときはウィンドウを閉じずに戻す。
        public bool ValidateDocxSelectionOrConfirm()
        {
            var lci = _main.CurrentInputModel?.LoadCasesInput;
            bool anyCase = lci != null && (
                lci.LoadCasesLevel1.Any(c => c.IsApplicableForDocxDisplay) ||
                lci.LoadCasesLevel2.Any(c => c.IsApplicableForDocxDisplay) ||
                lci.LoadCombinations.Any(c => c.IsApplicableForDocxDisplay));
            bool anyLiq = IncludeOutputLiquefactionYes || IncludeOutputLiquefactionNo;

            var missing = new List<string>();
            if (!anyCase) missing.Add("・荷重ケース／組合せが 1 つも選択されていません");
            if (!anyLiq) missing.Add("・液状化（あり／なし）が選択されていません");
            if (missing.Count == 0) return true;

            var result = MessageService.Show(
                "次の項目が未選択です。解析結果が計算書に出力されない可能性があります。\n\n"
                + string.Join("\n", missing) + "\n\nこのまま計算書を作成しますか？",
                "計算書出力の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }
    }
}
