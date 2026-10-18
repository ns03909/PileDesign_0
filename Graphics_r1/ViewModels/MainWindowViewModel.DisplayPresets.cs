using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 表示プリセット (2026-10-16 のレビュー)。リボン「表示」タブの 50 近いトグルを、用途ごとにまとめて切り替える。
    ///
    /// <para>プリセットは 2 種類。</para>
    /// <list type="bullet">
    /// <item>用意したプリセット (<see cref="BuiltInDisplayPresets"/>): 「既定」は起動時の表示にすべて戻す。
    /// ほかは<b>挙げた項目だけ</b>を切り替え、挙げていない項目は今のまま (利用者の好みを消さない)。</item>
    /// <item>保存したプリセット: 「今の表示を保存」で、その時点のすべての項目を名前を付けて保存する
    /// (この PC の利用者設定 user_settings.json。プロジェクトファイルには入れない)。</item>
    /// </list>
    ///
    /// <para>対象は Is…Visible / Is…VisibleOnly の bool のうち、ウィンドウの部品 (<see cref="WindowChromeToggles"/>) を除いたもの。
    /// 一覧を手で並べず型から引くので、表示のトグルを足せばプリセットの対象にも自動で入る。</para>
    ///
    /// <para>プリセットを当てたあとに表示を手で変えると、選んでいたプリセットの表示を外す (プリセットと違う表示になったため)。</para>
    /// </summary>
    /// <summary>
    /// 欄の並び (表示プリセットの一部)。解析結果ダッシュボードを開いているか・プロパティ欄・ミニマップ欄を出しているか。
    /// null は「触らない」。
    /// </summary>
    public readonly record struct PanelLayout(bool? Dashboard, bool? Properties, bool? Minimap)
    {
        public bool IsEmpty => Dashboard == null && Properties == null && Minimap == null;
    }

    public partial class MainWindowViewModel
    {
        /// <summary>
        /// 用意したプリセットの欄の並び (2026-10-17 のレビュー)。「結果の確認」はダッシュボードを開き、「入力の確認」は閉じる。
        /// 「既定」は起動時の並び (ダッシュボードは閉じ、プロパティ欄・ミニマップ欄は出す)。
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, PanelLayout> BuiltInPanelLayouts = new Dictionary<string, PanelLayout>
        {
            [DefaultDisplayPresetName] = new(false, true, true),
            ["入力の確認"] = new(false, null, null),
            ["結果の確認"] = new(true, null, null),
        };

        /// <summary>今の欄の並びを取る (メイン画面が設定する。欄はメイン画面が持つ)。</summary>
        public Func<PanelLayout>? CapturePanelLayout { get; set; }

        /// <summary>欄の並びを当てる (メイン画面が設定する)。</summary>
        public Action<PanelLayout>? ApplyPanelLayout { get; set; }

        private void ApplyPanels(PanelLayout layout)
        {
            if (layout.IsEmpty) return;
            // 当てている途中の欄の開閉で、選んだプリセットを外さない (OnPanelLayoutChanged)
            bool wasApplying = _isApplyingDisplayPreset;
            _isApplyingDisplayPreset = true;
            try { ApplyPanelLayout?.Invoke(layout); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[表示プリセット] 欄の並びを当てられませんでした"); }
            finally { _isApplyingDisplayPreset = wasApplying; }
        }

        /// <summary>プリセットの欄の並び。欄を指定していないプリセット・知らない名前は空 (何も触らない)。</summary>
        internal PanelLayout PanelLayoutOfPreset(string name)
        {
            if (BuiltInPanelLayouts.TryGetValue(name, out var builtIn)) return builtIn;
            var saved = _userSettingsService.Settings.DisplayPresets.FirstOrDefault(p => p.Name == name);
            return saved == null ? default : new PanelLayout(saved.DashboardOpen, saved.PropertiesVisible, saved.MinimapVisible);
        }

        /// <summary>今の欄の並びが、プリセットの指定した欄 (null 以外) とすべて合っているか。指定していない欄は見ない。</summary>
        internal static bool PanelLayoutMatches(PanelLayout preset, PanelLayout actual) =>
            (preset.Dashboard == null || preset.Dashboard == actual.Dashboard)
            && (preset.Properties == null || preset.Properties == actual.Properties)
            && (preset.Minimap == null || preset.Minimap == actual.Minimap);

        /// <summary>
        /// 欄 (ダッシュボード・プロパティ欄・ミニマップ欄) が開閉した (メイン画面から)。選んでいるプリセットの欄の並びと
        /// 食い違ったら、選択を外す (2026-10-18 のレビュー)。以前はモデル図の表示項目しか見ておらず、「結果の確認」を選んで
        /// ダッシュボードを閉じても「結果の確認」のままで、選び直しても (値が同じなので) 当て直されず、ダッシュボードを戻せなかった。
        ///
        /// <para>「当てている最中か」だけで手の操作を見分けず、並びを比べる。AvalonDock の IsVisibleChanged は
        /// Show/Hide のあと、レイアウトの更新で遅れて来ることがあり、印を下ろしたあとに届いても、比べれば合っているので外さない。</para>
        /// </summary>
        internal void OnPanelLayoutChanged()
        {
            if (_isApplyingDisplayPreset || _selectedDisplayPreset == null || CapturePanelLayout == null) return;
            var preset = PanelLayoutOfPreset(_selectedDisplayPreset);
            if (preset.IsEmpty) return;
            PanelLayout actual;
            try { actual = CapturePanelLayout(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "[表示プリセット] 欄の並びを取れませんでした"); return; }
            if (PanelLayoutMatches(preset, actual)) return;
            ClearSelectedDisplayPreset();
        }

        /// <summary>プリセットに含めない、ウィンドウの部品の表示 (リボン・ミニマップなど。モデル図の中身ではない)。</summary>
        internal static readonly string[] WindowChromeToggles =
        {
            nameof(IsRibbonVisible), nameof(IsInputVisualizerVisible), nameof(IsMinimapVisible),
            nameof(IsCenterCoordEditorVisible), nameof(IsViewCubeVisible), nameof(IsRightToolbarVisible),
            nameof(IsQuickHintVisible),
        };

        private static PropertyInfo[]? _displayToggleProperties;

        /// <summary>プリセットの対象になる表示のトグル。</summary>
        internal static IReadOnlyList<PropertyInfo> DisplayToggleProperties => _displayToggleProperties ??= typeof(MainWindowViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanRead && p.SetMethod?.IsPublic == true
                && p.Name.StartsWith("Is", StringComparison.Ordinal)
                && (p.Name.EndsWith("Visible", StringComparison.Ordinal) || p.Name.EndsWith("VisibleOnly", StringComparison.Ordinal))
                && !WindowChromeToggles.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        internal const string DefaultDisplayPresetName = "既定";

        /// <summary>用意したプリセット。「既定」以外は挙げた項目だけを切り替える。</summary>
        internal static readonly IReadOnlyList<(string Name, IReadOnlyDictionary<string, bool> Toggles)> BuiltInDisplayPresets = new[]
        {
            ("入力の確認", (IReadOnlyDictionary<string, bool>)new Dictionary<string, bool>
            {
                // 結果を消して、入力 (荷重・作用点・杭と土質の参照・地盤) を出す
                [nameof(IsAnalysisResultVisible)] = false,
                [nameof(IsResultValueVisible)] = false,
                [nameof(IsEvaluationColoringVisible)] = false,
                [nameof(IsEvaluationDetailVisible)] = false,
                [nameof(IsBubbleVisible)] = false,
                [nameof(IsArrowVisible)] = false,
                [nameof(IsDeformedElementVisible)] = false,
                [nameof(IsGroupPileGridDeformationVisible)] = false,
                [nameof(IsMassLoadingVisible)] = true,
                [nameof(IsAxialLoadingVisible)] = true,
                [nameof(IsActionPointVisible)] = true,
                [nameof(IsPileRefVisible)] = true,
                [nameof(IsSoilRefVisible)] = true,
                [nameof(IsGroundVisible)] = true,
                [nameof(IsNodeVisible)] = true,
                [nameof(IsGridLineVisible)] = true,
            }),
            ("結果の確認", new Dictionary<string, bool>
            {
                // 結果と検定比の色分けを出し、入力の文字と荷重を消す
                [nameof(IsAnalysisResultVisible)] = true,
                [nameof(IsResultValueVisible)] = true,
                [nameof(IsEvaluationColoringVisible)] = true,
                [nameof(IsMassLoadingVisible)] = false,
                [nameof(IsAxialLoadingVisible)] = false,
                [nameof(IsLoadingPlaneVisible)] = false,
                [nameof(IsPileRefVisible)] = false,
                [nameof(IsSoilRefVisible)] = false,
                [nameof(IsSoilValueVisible)] = false,
                [nameof(IsNodeNoVisible)] = false,
                [nameof(IsElementNoVisible)] = false,
                [nameof(IsPileTopLevelVisible)] = false,
                [nameof(IsConnectionNodeZVisible)] = false,
            }),
        };

        /// <summary>起動時 (このインスタンスを作ったとき) の表示。「既定」で戻す先。</summary>
        private Dictionary<string, bool>? _defaultDisplayToggles;

        private bool _isApplyingDisplayPreset;
        private string? _selectedDisplayPreset;

        /// <summary>リボンの「表示プリセット」に並べる名前 (用意したプリセット → 保存したプリセット)。</summary>
        public ObservableCollection<string> DisplayPresetNames { get; } = new();

        /// <summary>
        /// 選んでいるプリセット。選ぶとそのプリセットを当てる。プリセットを当てたあとに表示を手で変えると null に戻る。
        /// </summary>
        public string? SelectedDisplayPreset
        {
            get => _selectedDisplayPreset;
            set
            {
                if (!SetProperty(ref _selectedDisplayPreset, value)) return;
                if (value != null) ApplyDisplayPreset(value);
                DeleteSelectedDisplayPresetCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>今の表示を、すべての項目について控える。</summary>
        internal Dictionary<string, bool> CaptureDisplayToggles() =>
            DisplayToggleProperties.ToDictionary(p => p.Name, p => (bool)p.GetValue(this)!, StringComparer.Ordinal);

        /// <summary>
        /// 表示を当てる。知らない名前 (古い版で保存したプリセットの、いまは無い項目) は飛ばす。
        ///
        /// <para>false を先に、true をあとに当てる (「要素中間のみ」「杭頭のみ」などの相互排他は、true にした側が他を落とすため)。
        /// 項目ごとの 3D の描き直しは止め、最後に 1 回だけ描き直す。</para>
        ///
        /// <para>解析結果が無いときは「解析結果表示」を ON にしない (手で押したときの「解析結果がまだありません」の警告を、
        /// プリセットを選んだだけで出さないため)。</para>
        /// </summary>
        internal void ApplyDisplayToggles(IReadOnlyDictionary<string, bool> toggles)
        {
            var byName = DisplayToggleProperties.ToDictionary(p => p.Name, StringComparer.Ordinal);
            var redraw = UpdateCanvas3DAction;
            bool wasApplying = _isApplyingDisplayPreset;
            _isApplyingDisplayPreset = true;
            UpdateCanvas3DAction = null;
            try
            {
                foreach (var (name, value) in toggles.OrderBy(t => t.Value))
                {
                    if (!byName.TryGetValue(name, out var p)) continue;
                    if (name == nameof(IsAnalysisResultVisible) && value && !HasAnyAnalysisResultToShow) continue;
                    p.SetValue(this, value);
                }
            }
            finally
            {
                UpdateCanvas3DAction = redraw;
                _isApplyingDisplayPreset = wasApplying;
            }
            redraw?.Invoke();
            RequestUpdateWindow();
        }

        /// <summary>名前のプリセットを当てる。見つからなければ何もしない。</summary>
        internal void ApplyDisplayPreset(string name)
        {
            if (name == DefaultDisplayPresetName)
            {
                if (_defaultDisplayToggles != null) ApplyDisplayToggles(_defaultDisplayToggles);
                ApplyPanels(BuiltInPanelLayouts[DefaultDisplayPresetName]);
                return;
            }
            var builtIn = BuiltInDisplayPresets.FirstOrDefault(b => b.Name == name);
            if (builtIn.Toggles != null)
            {
                ApplyDisplayToggles(builtIn.Toggles);
                if (BuiltInPanelLayouts.TryGetValue(name, out var panels)) ApplyPanels(panels);
                return;
            }
            var saved = _userSettingsService.Settings.DisplayPresets.FirstOrDefault(p => p.Name == name);
            if (saved != null)
            {
                ApplyDisplayToggles(saved.Toggles);
                ApplyPanels(new PanelLayout(saved.DashboardOpen, saved.PropertiesVisible, saved.MinimapVisible));
            }
        }

        /// <summary>手で表示を変えたら、選んでいたプリセットの表示を外す (OnPropertyChanged から)。</summary>
        private void OnDisplayToggleChanged(string? propertyName)
        {
            if (_isApplyingDisplayPreset || _selectedDisplayPreset == null || propertyName == null) return;
            if (!DisplayToggleProperties.Any(p => p.Name == propertyName)) return;
            ClearSelectedDisplayPreset();
        }

        /// <summary>選んでいたプリセットの表示を外す (当て直しはしない)。外したあとは、同じプリセットを選ぶと当て直る。</summary>
        private void ClearSelectedDisplayPreset()
        {
            _selectedDisplayPreset = null;
            OnPropertyChanged(nameof(SelectedDisplayPreset));
            DeleteSelectedDisplayPresetCommand.NotifyCanExecuteChanged();
        }

        private void InitializeDisplayPresets()
        {
            _defaultDisplayToggles = CaptureDisplayToggles();
            RefreshDisplayPresetNames();
        }

        private void RefreshDisplayPresetNames()
        {
            var keep = _selectedDisplayPreset;
            DisplayPresetNames.Clear();
            DisplayPresetNames.Add(DefaultDisplayPresetName);
            foreach (var b in BuiltInDisplayPresets) DisplayPresetNames.Add(b.Name);
            // 用意したプリセットと同じ名前は並べない (2 つ並び、選ぶと用意した方が当たる)。設定の中身は読込で直してある
            foreach (var p in _userSettingsService.Settings.DisplayPresets)
                if (!IsBuiltInDisplayPresetName(p.Name) && !DisplayPresetNames.Contains(p.Name)) DisplayPresetNames.Add(p.Name);
            // 一覧を作り直すと ComboBox が選択を外すことがあるので戻す (当て直しはしない)
            if (keep != null && DisplayPresetNames.Contains(keep) && _selectedDisplayPreset != keep)
            {
                _selectedDisplayPreset = keep;
                OnPropertyChanged(nameof(SelectedDisplayPreset));
            }
        }

        /// <summary>用意したプリセットの名前か (保存したプリセットで上書き・削除できない)。</summary>
        internal static bool IsBuiltInDisplayPresetName(string name) =>
            name == DefaultDisplayPresetName || BuiltInDisplayPresets.Any(b => b.Name == name);

        /// <summary>
        /// 今の表示を名前を付けて保存する。同じ名前の保存したプリセットは置き換える。用意したプリセットの名前は使えない (false)。
        /// </summary>
        internal bool SaveDisplayPreset(string name)
        {
            name = name.Trim();
            if (name.Length == 0 || IsBuiltInDisplayPresetName(name)) return false;
            var presets = _userSettingsService.Settings.DisplayPresets;
            var panels = CapturePanelLayout?.Invoke() ?? default;
            var preset = new DisplayPreset
            {
                Name = name,
                Toggles = CaptureDisplayToggles(),
                DashboardOpen = panels.Dashboard,
                PropertiesVisible = panels.Properties,
                MinimapVisible = panels.Minimap,
            };
            int at = presets.FindIndex(p => p.Name == name);
            if (at >= 0) presets[at] = preset; else presets.Add(preset);
            if (!_userSettingsService.Save())
                ReportUserSettingsSaveFailed($"表示プリセット「{name}」を保存しました");
            RefreshDisplayPresetNames();
            _selectedDisplayPreset = name;
            OnPropertyChanged(nameof(SelectedDisplayPreset));
            DeleteSelectedDisplayPresetCommand.NotifyCanExecuteChanged();
            return true;
        }

        /// <summary>保存したプリセットを消す。用意したプリセットは消せない。</summary>
        internal bool DeleteDisplayPreset(string name)
        {
            if (IsBuiltInDisplayPresetName(name)) return false;
            if (_userSettingsService.Settings.DisplayPresets.RemoveAll(p => p.Name == name) == 0) return false;
            if (!_userSettingsService.Save())
                ReportUserSettingsSaveFailed($"表示プリセット「{name}」を削除しました", "次回の起動では元に戻ります");
            if (_selectedDisplayPreset == name)
            {
                _selectedDisplayPreset = null;
                OnPropertyChanged(nameof(SelectedDisplayPreset));
            }
            RefreshDisplayPresetNames();
            DeleteSelectedDisplayPresetCommand.NotifyCanExecuteChanged();
            return true;
        }

        /// <summary>
        /// 利用者の設定を書き込めなかったことを知らせる (2026-10-18 のレビュー)。以前はログに残すだけで、画面では
        /// 保存・削除できたように見え、次の起動で保存したプリセットが消えたり、削除したものが戻ったりした。
        /// 画面の状態は変えたままにする (このまま使える)。
        /// </summary>
        internal void ReportUserSettingsSaveFailed(string change, string nextLaunch = "次回の起動には残りません")
        {
            UserSettingsSaveFailures++;
            MessageService.ShowWarning(UserSettingsService.DescribeSaveFailed(change, nextLaunch), "設定の保存");
        }

        /// <summary>設定を書き込めなかった回数 (テスト用)。</summary>
        internal int UserSettingsSaveFailures { get; private set; }

        /// <summary>今の表示を保存する。名前は画面側 (<see cref="AskDisplayPresetName"/>) で聞く。</summary>
        [RelayCommand]
        private void SaveDisplayPresetAs()
        {
            string? name = AskDisplayPresetName?.Invoke(_selectedDisplayPreset is { } s && !IsBuiltInDisplayPresetName(s) ? s : "");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!SaveDisplayPreset(name))
                MessageService.ShowWarning($"「{name.Trim()}」は用意したプリセットの名前なので使えません。別の名前にしてください。", "表示プリセット");
        }

        /// <summary>選んでいる保存したプリセットを消す。</summary>
        [RelayCommand(CanExecute = nameof(CanDeleteDisplayPreset))]
        private void DeleteSelectedDisplayPreset()
        {
            if (_selectedDisplayPreset is { } name &&
                MessageService.Confirm($"表示プリセット「{name}」を削除しますか。", "表示プリセット"))
                DeleteDisplayPreset(name);
        }

        private bool CanDeleteDisplayPreset() => _selectedDisplayPreset is { } name && !IsBuiltInDisplayPresetName(name);

        /// <summary>保存するプリセットの名前を聞く (画面側が立てる。キャンセルは null)。引数は初期値。</summary>
        public Func<string, string?>? AskDisplayPresetName { get; set; }

        /// <summary>テスト用: 利用者の設定ファイルに触れない保存先に差し替える。</summary>
        internal void UseUserSettingsForTests(UserSettingsService service)
        {
            _userSettingsService = service;
            RefreshDisplayPresetNames();
        }
    }
}
