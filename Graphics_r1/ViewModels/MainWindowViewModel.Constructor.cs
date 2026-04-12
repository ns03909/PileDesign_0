using AvalonDock.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.FEM;
using PileDesign.Models.InputData;
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
    public enum PropertyInputType { ReadOnly, Number, ComboBox }

    public class PropertyPanelItem : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Unit { get; }
        public string NameColor { get; }
        public PropertyInputType InputType { get; }
        public IReadOnlyList<string>? Options { get; }

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                if (!_suppressCommit) CommitAction?.Invoke(this, value);
            }
        }

        private bool _suppressCommit;

        /// <summary>CommitAction を発火させずに Value を更新する（入力キャンセル時に元の値へ戻す場合など）</summary>
        public void SetValueSilent(string value)
        {
            _suppressCommit = true;
            Value = value;
            _suppressCommit = false;
        }

        /// <summary>値が確定したときに呼ばれるコールバック。引数は (this, rawValue)。</summary>
        public Action<PropertyPanelItem, string>? CommitAction { get; }

        public PropertyPanelItem(
            string name,
            string value,
            string unit = "",
            PropertyInputType inputType = PropertyInputType.ReadOnly,
            Action<PropertyPanelItem, string>? commitAction = null,
            IReadOnlyList<string>? options = null,
            string nameColor = null)
        {
            Name = name;
            _value = value;
            Unit = unit;
            InputType = inputType;
            CommitAction = commitAction;
            Options = options;
            NameColor = nameColor;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// MainWindowViewModel.Constructor.cs
    ///
    /// 責任範囲:
    /// - 全プロパティの定義（表示制御、描画設定、解析結果選択など）
    /// - コンストラクタと初期化処理
    /// - プロパティ変更イベントの購読設定
    /// - 集計計算プロパティ（VL合計、OTM、重心座標など）
    /// - LoadCase/LoadCombinationオプション更新
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
            set => SetProperty(ref _currentModel, value);
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
            get => CurrentInputModel.PileGroupSettlement.RectLoads;
            set
            {
                if (!ReferenceEquals(CurrentInputModel.PileGroupSettlement.RectLoads, value))
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads = value ?? [];
                    OnPropertyChanged(nameof(RectLoads));
                    RequestUpdateWindow();
                }
            }
        }

        [ObservableProperty] // レベル1地震時軸力
        public bool _isElastic;

        private ObservableCollection<CTreeViewData> _cTreeViewData = [];
        public ObservableCollection<CTreeViewData> CTreeViewData
        {
            get => _cTreeViewData;
            set => SetProperty(ref _cTreeViewData, value);
        }

        public CanvasThreeDView CanvasThreeDView { get; set; }

        private ObservableCollection<int> _labelSizeOption = new(Enumerable.Range(7, 14)); // 7 to 20
        public ObservableCollection<int> LabelSizeOption
        {
            get => _labelSizeOption;
            set => SetProperty(ref _labelSizeOption, value);
        }

        // MRUリスト
        public ObservableCollection<MruItem> MruItems { get; } = new();

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

        // プロパティパネル: 選択アイテムのプロパティ一覧
        public ObservableCollection<PropertyPanelItem> SelectedItemProperties { get; } = [];

        // プロパティパネル: 選択中アイテムのPropertyChanged購読管理
        private INotifyPropertyChanged? _subscribedPropertyItem;

        public string SelectedItemHeader
        {
            get
            {
                var piles = CurrentInputModel?.PileLayoutItems?.Where(p => p.IsSelected).ToList();
                if (piles?.Count == 1) return $"杭 #{CurrentInputModel.PileLayoutItems.IndexOf(piles[0]) + 1}";
                if (piles?.Count > 1) return $"杭 ×{piles.Count}";

                var beams = CurrentInputModel?.FoundationBeamInput?.Beams?.Where(b => b.IsSelected).ToList();
                if (beams?.Count == 1) return $"梁要素 #{beams[0].No}";
                if (beams?.Count > 1) return $"梁要素 ×{beams.Count}";

                var nodes = CurrentInputModel?.InputNodes?.Where(n => n.IsSelected && n.Type == Models.InputData.NodeType.General).ToList();
                if (nodes?.Count == 1) return $"一般節点 #{nodes[0].No}";
                if (nodes?.Count > 1) return $"一般節点 ×{nodes.Count}";

                var fNode = CurrentInputModel?.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.IsSelected);
                if (fNode != null) return $"基礎梁節点 #{fNode.No}";

                return "選択なし";
            }
        }

        public void UpdatePropertyPanel()
        {
            // 前回購読していたアイテムの購読を解除
            if (_subscribedPropertyItem != null)
            {
                _subscribedPropertyItem.PropertyChanged -= OnSelectedItemPropertyChanged;
                _subscribedPropertyItem = null;
            }

            SelectedItemProperties.Clear();
            OnPropertyChanged(nameof(SelectedItemHeader));

            // 杭
            var piles = CurrentInputModel?.PileLayoutItems?.Where(p => p.IsSelected).ToList();
            if (piles?.Count == 1)
            {
                SubscribeSelectedItem(piles[0]);
                BuildPileProperties(piles[0]);
                return;
            }
            if (piles?.Count > 1)
            {
                BuildMultiPileProperties(piles);
                return;
            }

            // 梁要素
            var beams = CurrentInputModel?.FoundationBeamInput?.Beams?.Where(b => b.IsSelected).ToList();
            if (beams?.Count == 1)
            {
                SubscribeSelectedItem(beams[0]);
                BuildBeamProperties(beams[0]);
                return;
            }
            if (beams?.Count > 1)
            {
                BuildMultiBeamProperties(beams);
                return;
            }

            // 一般節点
            var nodes = CurrentInputModel?.InputNodes?.Where(n => n.IsSelected && n.Type == Models.InputData.NodeType.General).ToList();
            if (nodes?.Count == 1)
            {
                SubscribeSelectedItem(nodes[0]);
                BuildInputNodeProperties(nodes[0]);
                return;
            }
            if (nodes?.Count > 1)
            {
                BuildMultiInputNodeProperties(nodes);
                return;
            }

            // 基礎梁節点
            var fNode = CurrentInputModel?.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.IsSelected);
            if (fNode != null)
            {
                SubscribeSelectedItem(fNode);
                BuildFoundationNodeProperties(fNode);
                return;
            }
        }

        private void SubscribeSelectedItem(INotifyPropertyChanged item)
        {
            _subscribedPropertyItem = item;
            item.PropertyChanged += OnSelectedItemPropertyChanged;
        }

        private void OnSelectedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 選択状態の変更はUpdatePropertyPanel自体が呼ばれるので無視
            if (e.PropertyName == nameof(PileLayoutDataItem.IsSelected)) return;

            // プロパティ一覧を再構築
            SelectedItemProperties.Clear();
            OnPropertyChanged(nameof(SelectedItemHeader));

            if (sender is PileLayoutDataItem pile) BuildPileProperties(pile);
            else if (sender is FoundationBeamElement beam) BuildBeamProperties(beam);
            else if (sender is InputNode node) BuildInputNodeProperties(node);
            else if (sender is FoundationNode fNode) BuildFoundationNodeProperties(fNode);
        }

        // -------------------------------------------------------
        // プロパティパネル Build ヘルパー
        // -------------------------------------------------------

        /// <summary>数値（double）の編集コミットアクションを生成する。</summary>
        private Action<PropertyPanelItem, string> MakeDoubleCommit(
            Func<double> getter, Action<double> setter, string format = "F3")
        {
            return (item, rawValue) =>
            {
                if (!double.TryParse(rawValue, out var newVal))
                {
                    item.SetValueSilent(getter().ToString(format));
                    return;
                }
                var oldVal = getter();
                if (Math.Abs(newVal - oldVal) < 1e-9) return;
                if (!CheckAndResetAnalysisResults())
                {
                    item.SetValueSilent(oldVal.ToString(format));
                    return;
                }
                SaveUndoState();
                setter(newVal);
                RequestUpdateWindow();
            };
        }

        /// <summary>整数（int）の編集コミットアクションを生成する（ComboBox 用）。</summary>
        private Action<PropertyPanelItem, string> MakeIntCommit(
            Func<int> getter, Action<int> setter)
        {
            return (item, rawValue) =>
            {
                if (!int.TryParse(rawValue, out var newVal))
                {
                    item.SetValueSilent(getter().ToString());
                    return;
                }
                if (newVal == getter()) return;
                if (!CheckAndResetAnalysisResults())
                {
                    item.SetValueSilent(getter().ToString());
                    return;
                }
                SaveUndoState();
                setter(newVal);
                RequestUpdateWindow();
            };
        }

        // -------------------------------------------------------
        // 単一選択 Build メソッド
        // -------------------------------------------------------

        private void BuildPileProperties(PileLayoutDataItem pile)
        {
            var pileBodyOptions = CurrentInputModel.PileBodiesCountList.Select(x => x.ToString()).ToList();
            var groundOptions   = CurrentInputModel.GroundsInputCountList.Select(x => x.ToString()).ToList();

            int no = CurrentInputModel.PileLayoutItems.IndexOf(pile) + 1;
            SelectedItemProperties.Add(new("番号", $"{no}"));

            SelectedItemProperties.Add(new("X", $"{pile.X:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.X, v => pile.X = v)));
            SelectedItemProperties.Add(new("Y", $"{pile.Y:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.Y, v => pile.Y = v)));
            SelectedItemProperties.Add(new("Z (杭頭)", $"{pile.Z:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.Z, v => pile.Z = v)));

            SelectedItemProperties.Add(new("杭体No", $"{pile.PileBodyNo}", "",
                PropertyInputType.ComboBox,
                MakeIntCommit(() => pile.PileBodyNo, v => pile.PileBodyNo = v),
                pileBodyOptions));
            SelectedItemProperties.Add(new("地盤No", $"{pile.GroundNo}", "",
                PropertyInputType.ComboBox,
                MakeIntCommit(() => pile.GroundNo, v => pile.GroundNo = v),
                groundOptions));

            var pileLen = CalcPileLength(pile);
            if (pileLen.HasValue)
                SelectedItemProperties.Add(new("杭長", $"{pileLen.Value:F3} m"));

            SelectedItemProperties.Add(new("群杭係数 ξ", $"{pile.GroupPileFactor:F3}", "",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.GroupPileFactor, v => pile.GroupPileFactor = v)));
            SelectedItemProperties.Add(new("間隔係数 R/B", $"{pile.PileSpacingFactor:F3}"));
            SelectedItemProperties.Add(new("ΔZc", $"{pile.FoundationBeamDeltaZc:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.FoundationBeamDeltaZc, v => pile.FoundationBeamDeltaZc = v)));

            // 軸力 VL（VL0 を編集、表示は VL0 の値）— ディープブルー
            SelectedItemProperties.Add(new("軸力 VL", $"{pile.AxialForceVL0:F1}", "kN",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.AxialForceVL0, v => pile.AxialForceVL0 = v, "F1"),
                nameColor: "#3271AD"));

            // 軸力: レベル1 — 緑
            for (int i = 0; i < pile.AxialForceLevel1s.Count; i++)
            {
                int idx = i;
                SelectedItemProperties.Add(new($"軸力 1-{i + 1}", $"{pile.AxialForceLevel1s[i]:F1}", "kN",
                    PropertyInputType.Number,
                    MakeDoubleCommit(
                        () => pile.AxialForceLevel1s[idx],
                        v => pile.AxialForceLevel1s[idx] = v, "F1"),
                    nameColor: "#238966"));
            }

            // 軸力: レベル2 — 赤
            for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
            {
                int idx = i;
                SelectedItemProperties.Add(new($"軸力 2-{i + 1}", $"{pile.AxialForceLevel2s[i]:F1}", "kN",
                    PropertyInputType.Number,
                    MakeDoubleCommit(
                        () => pile.AxialForceLevel2s[idx],
                        v => pile.AxialForceLevel2s[idx] = v, "F1"),
                    nameColor: "#D82531"));
            }
        }

        private void BuildBeamProperties(FoundationBeamElement beam)
        {
            SelectedItemProperties.Add(new("要素No",    $"{beam.No}"));
            SelectedItemProperties.Add(new("I端節点No", CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeI_Type, beam.NodeI_Id)));
            SelectedItemProperties.Add(new("J端節点No", CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeJ_Type, beam.NodeJ_Id)));
            SelectedItemProperties.Add(new("材料No",    $"{beam.MaterialNo}"));
            SelectedItemProperties.Add(new("断面No",    $"{beam.SectionNo}"));
            SelectedItemProperties.Add(new("幅",        $"{beam.Width:F3} m"));
            SelectedItemProperties.Add(new("高さ",      $"{beam.Height:F3} m"));
            SelectedItemProperties.Add(new("ヤング率",   $"{beam.YoungModulus / 1000.0:N0} N/mm²"));
            SelectedItemProperties.Add(new("横弾性係数", $"{beam.ShearModulus / 1000.0:N0} N/mm²"));

            // 角度β のみ編集可能
            SelectedItemProperties.Add(new("角度β", $"{beam.AngleBeta:F1}", "°",
                PropertyInputType.Number,
                MakeDoubleCommit(() => beam.AngleBeta, v => beam.AngleBeta = v, "F1")));

            var len = CalcBeamLength(beam);
            if (len.HasValue)
                SelectedItemProperties.Add(new("部材長", $"{len.Value:F3} m"));
        }

        private double? CalcBeamLength(FoundationBeamElement beam)
        {
            if (beam.NodeI_Id == Guid.Empty || beam.NodeJ_Id == Guid.Empty) return null;
            var ci = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
            var cj = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
            if (ci == null || cj == null) return null;
            double dx = ci.Value.X - cj.Value.X;
            double dy = ci.Value.Y - cj.Value.Y;
            double dz = ci.Value.Z - cj.Value.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private double? CalcPileLength(PileLayoutDataItem pile)
        {
            int idx = pile.PileBodyNo - 1;
            if (idx < 0 || CurrentInputModel.PileBodies == null || idx >= CurrentInputModel.PileBodies.Count)
                return null;
            var pileBody = CurrentInputModel.PileBodies[idx];
            if (pileBody.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                return null;
            return pileBody.PileBodySegments.Sum(s => s.SegmentLength);
        }

        private void BuildInputNodeProperties(InputNode node)
        {
            SelectedItemProperties.Add(new("節点No", $"{node.No}"));
            SelectedItemProperties.Add(new("X", $"{node.X:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => node.X, v => node.X = v)));
            SelectedItemProperties.Add(new("Y", $"{node.Y:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => node.Y, v => node.Y = v)));
            SelectedItemProperties.Add(new("Z", $"{node.Z:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => node.Z, v => node.Z = v)));
            SelectedItemProperties.Add(new("タイプ", $"{node.Type}"));
        }

        private void BuildFoundationNodeProperties(FoundationNode fNode)
        {
            SelectedItemProperties.Add(new("節点No", $"{fNode.No}"));
            SelectedItemProperties.Add(new("X", $"{fNode.X:F3} m"));
            SelectedItemProperties.Add(new("Y", $"{fNode.Y:F3} m"));
            SelectedItemProperties.Add(new("Z", $"{fNode.Z:F3} m"));
        }

        // -------------------------------------------------------
        // 複数選択 Build メソッド
        // -------------------------------------------------------

        private static string CommonOrVarious<T>(IEnumerable<T> values)
        {
            var distinct = values.Distinct().ToList();
            return distinct.Count == 1 ? $"{distinct[0]}" : "(様々)";
        }

        private static string CommonDoubleOrVarious(IEnumerable<double> values, string format = "F3", double scale = 1.0)
        {
            var distinct = values.Select(v => Math.Round(v, 6)).Distinct().ToList();
            return distinct.Count == 1 ? (distinct[0] * scale).ToString(format) : "(様々)";
        }

        private void BuildMultiPileProperties(List<PileLayoutDataItem> piles)
        {
            var pileBodyOptions = CurrentInputModel.PileBodiesCountList.Select(x => x.ToString()).ToList();
            var groundOptions   = CurrentInputModel.GroundsInputCountList.Select(x => x.ToString()).ToList();

            SelectedItemProperties.Add(new("選択数", $"{piles.Count} 本"));

            // 杭長合計（読み取り専用）
            double totalPileLen = 0; int countValidLen = 0;
            foreach (var p in piles) { var l = CalcPileLength(p); if (l.HasValue) { totalPileLen += l.Value; countValidLen++; } }
            if (countValidLen > 0)
                SelectedItemProperties.Add(new("杭長 (合計)", $"{totalPileLen:F3} m"));

            // 杭体No（ComboBox: 同一値なら選択可、様々なら空欄）
            var commonPileBodyNo = piles.Select(p => p.PileBodyNo).Distinct().ToList();
            SelectedItemProperties.Add(new("杭体No",
                commonPileBodyNo.Count == 1 ? commonPileBodyNo[0].ToString() : "",
                "", PropertyInputType.ComboBox,
                (item, rawValue) =>
                {
                    if (!int.TryParse(rawValue, out var newVal)) return;
                    if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(commonPileBodyNo.Count == 1 ? commonPileBodyNo[0].ToString() : ""); return; }
                    SaveUndoState();
                    foreach (var p in piles) p.PileBodyNo = newVal;
                    RequestUpdateWindow();
                }, pileBodyOptions));

            // 地盤No（ComboBox）
            var commonGroundNo = piles.Select(p => p.GroundNo).Distinct().ToList();
            SelectedItemProperties.Add(new("地盤No",
                commonGroundNo.Count == 1 ? commonGroundNo[0].ToString() : "",
                "", PropertyInputType.ComboBox,
                (item, rawValue) =>
                {
                    if (!int.TryParse(rawValue, out var newVal)) return;
                    if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(commonGroundNo.Count == 1 ? commonGroundNo[0].ToString() : ""); return; }
                    SaveUndoState();
                    foreach (var p in piles) p.GroundNo = newVal;
                    RequestUpdateWindow();
                }, groundOptions));

            // 群杭係数 ξ
            SelectedItemProperties.Add(new("群杭係数 ξ",
                CommonDoubleOrVarious(piles.Select(p => p.GroupPileFactor)), "",
                PropertyInputType.Number,
                (item, rawValue) =>
                {
                    if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.GroupPileFactor))); return; }
                    if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.GroupPileFactor))); return; }
                    SaveUndoState();
                    foreach (var p in piles) p.GroupPileFactor = newVal;
                    RequestUpdateWindow();
                }));

            // 間隔係数（読み取り専用）
            SelectedItemProperties.Add(new("間隔係数 R/B", CommonDoubleOrVarious(piles.Select(p => p.PileSpacingFactor))));

            // ΔZc
            SelectedItemProperties.Add(new("ΔZc",
                CommonDoubleOrVarious(piles.Select(p => p.FoundationBeamDeltaZc)), "m",
                PropertyInputType.Number,
                (item, rawValue) =>
                {
                    if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.FoundationBeamDeltaZc))); return; }
                    if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.FoundationBeamDeltaZc))); return; }
                    SaveUndoState();
                    foreach (var p in piles) p.FoundationBeamDeltaZc = newVal;
                    RequestUpdateWindow();
                }));

            // 軸力 VL（合計：読み取り専用）— ディープブルー
            SelectedItemProperties.Add(new("軸力 VL (合計)", $"{piles.Sum(p => p.AxialForceVL):F1} kN", nameColor: "#3271AD"));

            // レベル1軸力 — 緑
            int level1Count = piles.Min(p => p.AxialForceLevel1s.Count);
            for (int i = 0; i < level1Count; i++)
            {
                int idx = i;
                SelectedItemProperties.Add(new($"軸力 1-{i + 1}",
                    CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel1s[idx]), "F1"), "kN",
                    PropertyInputType.Number,
                    (item, rawValue) =>
                    {
                        if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel1s[idx]), "F1")); return; }
                        if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel1s[idx]), "F1")); return; }
                        SaveUndoState();
                        foreach (var p in piles) p.AxialForceLevel1s[idx] = newVal;
                        RequestUpdateWindow();
                    },
                    nameColor: "#238966"));
            }

            // レベル2軸力 — 赤
            int level2Count = piles.Min(p => p.AxialForceLevel2s.Count);
            for (int i = 0; i < level2Count; i++)
            {
                int idx = i;
                SelectedItemProperties.Add(new($"軸力 2-{i + 1}",
                    CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel2s[idx]), "F1"), "kN",
                    PropertyInputType.Number,
                    (item, rawValue) =>
                    {
                        if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel2s[idx]), "F1")); return; }
                        if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => p.AxialForceLevel2s[idx]), "F1")); return; }
                        SaveUndoState();
                        foreach (var p in piles) p.AxialForceLevel2s[idx] = newVal;
                        RequestUpdateWindow();
                    },
                    nameColor: "#D82531"));
            }
        }

        private void BuildMultiBeamProperties(List<FoundationBeamElement> beams)
        {
            SelectedItemProperties.Add(new("選択数",    $"{beams.Count} 本"));
            SelectedItemProperties.Add(new("材料No",    CommonOrVarious(beams.Select(b => b.MaterialNo))));
            SelectedItemProperties.Add(new("断面No",    CommonOrVarious(beams.Select(b => b.SectionNo))));
            SelectedItemProperties.Add(new("幅",        $"{CommonDoubleOrVarious(beams.Select(b => b.Width))} m"));
            SelectedItemProperties.Add(new("高さ",      $"{CommonDoubleOrVarious(beams.Select(b => b.Height))} m"));
            SelectedItemProperties.Add(new("ヤング率",   $"{CommonDoubleOrVarious(beams.Select(b => b.YoungModulus), "N0", 0.001)} N/mm²"));
            SelectedItemProperties.Add(new("横弾性係数", $"{CommonDoubleOrVarious(beams.Select(b => b.ShearModulus), "N0", 0.001)} N/mm²"));
            SelectedItemProperties.Add(new("角度β",     $"{CommonDoubleOrVarious(beams.Select(b => b.AngleBeta), "F1")}°"));

            double totalLen = 0; int countValid = 0;
            foreach (var b in beams) { var l = CalcBeamLength(b); if (l.HasValue) { totalLen += l.Value; countValid++; } }
            if (countValid > 0)
                SelectedItemProperties.Add(new("部材長 (合計)", $"{totalLen:F3} m"));
        }

        private void BuildMultiInputNodeProperties(List<InputNode> nodes)
        {
            SelectedItemProperties.Add(new("選択数", $"{nodes.Count} 個"));
            SelectedItemProperties.Add(new("タイプ", CommonOrVarious(nodes.Select(n => n.Type))));
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

        // 解析状態表示
        public string AnalysisStatusText
        {
            get
            {
                var statuses = new List<string>();
                if (IsElementSplit) statuses.Add("要素分割済み");
                if (IsHorizontalAnalysisDone) statuses.Add("水平解析完了");
                if (IsVerticalAnalysisDone) statuses.Add("沈下解析完了");
                if (IsVerticalBeamAnalysisDone) statuses.Add("梁鉛直解析完了");
                return statuses.Count > 0 ? string.Join(" | ", statuses) : "未解析";
            }
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

        // 展開トグル用プロパティ（バブル設定）
        private bool _isBubbleSettingExpanded = false;
        public bool IsBubbleSettingExpanded
        {
            get => _isBubbleSettingExpanded;
            set => SetProperty(ref _isBubbleSettingExpanded, value);
        }

        // 展開トグル用プロパティ（矢印設定）
        private bool _isArrowSettingExpanded = false;
        public bool IsArrowSettingExpanded
        {
            get => _isArrowSettingExpanded;
            set => SetProperty(ref _isArrowSettingExpanded, value);
        }

        // プロパティ
        public LayoutAnchorable InputDataAnchorable { get; set; }

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
                //    MessageBox.Show("要素分割が完了していないため、地盤変位描画を有効にできません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                }
            }
        }

        /// <summary>
        /// 選択された荷重ケースと荷重組み合わせに対して、解析結果の液状化状態を自動検出し、IsLiquefaction を設定する
        /// </summary>
        private void AutoDetectLiquefactionState()
        {
            // CurrentModelが存在しない場合は何もしない
            if (CurrentModel?.AnalysisStepResults == null || CurrentModel.AnalysisStepResults.Count == 0)
                return;

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

            // 選択されたLoadCaseとLoadCombinationに対応する結果を検索
            var results = CurrentModel.AnalysisStepResults
                .Where(r => r.LoadCase?.LoadName == selectedLoadCase.LoadName);

            // 荷重組み合わせが選択されている場合はさらにフィルタリング
            // SelectedLoadCombinationNameはGetName()形式（"1.00/1.00/1.00"）なので、
            // LoadCombination.Name（"αL:1.00/βU:1.00/βL:1.00"）ではなくGetName()で比較する
            if (!string.IsNullOrEmpty(SelectedLoadCombinationName))
            {
                results = results.Where(r => r.LoadCombination?.GetName() == SelectedLoadCombinationName);
            }

            var resultList = results.ToList();
            bool hasLiquefactionResults = resultList.Any(r => r.IsLiquefaction);
            bool hasNonLiquefactionResults = resultList.Any(r => !r.IsLiquefaction);

            // 液状化結果のみがある場合は自動的にtrueに設定
            if (hasLiquefactionResults && !hasNonLiquefactionResults)
            {
                IsLiquefaction = true;
            }
            // 非液状化結果のみがある場合は自動的にfalseに設定
            else if (!hasLiquefactionResults && hasNonLiquefactionResults)
            {
                IsLiquefaction = false;
            }
            // 両方の結果がある場合は現在の設定を維持（ユーザーが選択）
        }

        private void UpdateDirectionOption()
        {
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

        private string _analysisResultContent /*= "梁応力"*/;
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

                    // 沈下系コンテンツ選択時も値表示をON
                    if (value == "沈下量" || value == "沈下部材角" || value == "沈下反力" || value == "沈下応力")
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
                    "沈下反力" => "基礎梁考慮反力",
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
                    // 既存のSettlementOptionをそのまま使用（単杭/群杭/単杭+群杭 + 基礎梁考慮は別途管理済み）
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
                case "沈下反力":
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
            "UX",
            "UY",
            "UZ",
            "θX",
            "θY",
            "θZ",
            "θH",
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
            "R",
            "RX",
            "RY",
            "RZ",
            "MX",
            "MY",
            "MZ",
            "MH",
            ];

        public ObservableCollection<string> AnalysisResultSoilSpringOption
        {
            get => _analysisSoilSpringOption;
            set => SetProperty(ref _analysisSoilSpringOption, value);
        }
        private string _analysisResultSoilSpringType = "R";
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

                // 自分がtrueになったら、もう片方をfalseに
                if (value && !_suppressMutualToggle)
                {
                    _suppressMutualToggle = true;
                    IsPileTopResultValueVisibleOnly = false;
                    _suppressMutualToggle = false;
                }

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
                    _suppressMutualToggle = false;
                }

                if (!_suppressMutualToggle)
                    UpdateCanvas3DAction?.Invoke(); // 3Dキャンバス更新（内側更新では抑止）
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

        // VL0合計
        public double SumVL0 => GetSumVL0();

        // VLadd合計
        public double SumVLadd => GetSumVLadd();

        // VL+VLadd合計
        public double SumVL => GetSumVL0() + GetSumVLadd();

        // VL重心
        public Point3D GravityCenterVL0 => CurrentInputModel.GetVLGravityCenter();

        // VLadd重心
        public Point3D GravityCenterVLadd => CurrentInputModel.GetVLaddGravityCenter();

        // VL+VLadd重心
        public Point3D GravityCenterVLPlusVLadd => CurrentInputModel.GetVLplusVLaddGravityCenter();

        private double GetSumVL0()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVL0;
            return sum;
        }


        private double GetSumVLadd()
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceVLAdditional;
            return sum;
        }

        // sum（get専用の計算プロパティに変更）
        public double Sum1_1 => GetSumLevel1(1);
        public double Sum1_2 => GetSumLevel1(2);
        public double Sum1_3 => GetSumLevel1(3);
        public double Sum1_4 => GetSumLevel1(4);

        public double Sum2_1 => GetSumLevel2(1);
        public double Sum2_2 => GetSumLevel2(2);
        public double Sum2_3 => GetSumLevel2(3);
        public double Sum2_4 => GetSumLevel2(4);

        // 合計計算（null/空に強い）
        private double GetSumLevel1(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel1s[no - 1];
            return sum;
        }

        private double GetSumLevel2(int no)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            double sum = 0.0;
            foreach (var item in items)
                sum += item.AxialForceLevel2s[no - 1];
            return sum;
        }

        // OTM（get専用の計算プロパティに変更）
        public double OverturningMoment1_1X => GetOverturningMoment(level: 1, dir: 1, axis: 'X');
        public double OverturningMoment1_2X => GetOverturningMoment(level: 1, dir: 2, axis: 'X');
        public double OverturningMoment1_3X => GetOverturningMoment(level: 1, dir: 3, axis: 'X');
        public double OverturningMoment1_4X => GetOverturningMoment(level: 1, dir: 4, axis: 'X');

        public double OverturningMoment1_1Y => GetOverturningMoment(level: 1, dir: 1, axis: 'Y');
        public double OverturningMoment1_2Y => GetOverturningMoment(level: 1, dir: 2, axis: 'Y');
        public double OverturningMoment1_3Y => GetOverturningMoment(level: 1, dir: 3, axis: 'Y');
        public double OverturningMoment1_4Y => GetOverturningMoment(level: 1, dir: 4, axis: 'Y');

        public double OverturningMoment2_1X => GetOverturningMoment(level: 2, dir: 1, axis: 'X');
        public double OverturningMoment2_2X => GetOverturningMoment(level: 2, dir: 2, axis: 'X');
        public double OverturningMoment2_3X => GetOverturningMoment(level: 2, dir: 3, axis: 'X');
        public double OverturningMoment2_4X => GetOverturningMoment(level: 2, dir: 4, axis: 'X');

        public double OverturningMoment2_1Y => GetOverturningMoment(level: 2, dir: 1, axis: 'Y');
        public double OverturningMoment2_2Y => GetOverturningMoment(level: 2, dir: 2, axis: 'Y');
        public double OverturningMoment2_3Y => GetOverturningMoment(level: 2, dir: 3, axis: 'Y');
        public double OverturningMoment2_4Y => GetOverturningMoment(level: 2, dir: 4, axis: 'Y');

        // OTM計算ヘルパー（回転中心はVL+VLadd重心を採用）
        private double GetOverturningMoment(int level, int dir, char axis)
        {
            var items = CurrentInputModel?.PileLayoutItems;
            if (items == null || items.Count == 0) return 0.0;

            // 回転中心（必要に応じて 0,0 に変更可）
            var pivot = GravityCenterVLPlusVLadd;

            double sum = 0.0;
            foreach (var item in items)
            {
                // レベル/方向別の鉛直力成分
                double f = level == 1
                    ? item.AxialForceLevel1s[dir - 1]
                    : item.AxialForceLevel2s[dir - 1];

                // X回りはY距離、Y回りはX距離
                if (axis == 'X')
                    sum += f * (item.Y - pivot.Y);
                else
                    sum += f * (item.X - pivot.X);
            }
            return sum;
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

        // 接続用節点表示（杭頭+ΔZc位置）
        private bool _isConnectingNodeVisible = true;
        public bool IsConnectingNodeVisible
        {
            get => _isConnectingNodeVisible;
            set
            {
                if (SetProperty(ref _isConnectingNodeVisible, value))
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
                if (value && !IsVerticalAnalysisDone && !IsHorizontalAnalysisDone && !IsGroupPileSettlementAnalysisDone && !IsVerticalBeamAnalysisDone)
                {
                    MessageBox.Show("水平解析、単杭解析、群杭解析、基礎梁鉛直解析のいずれかを実行後でないと解析結果表示はできません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            }

            // 画面更新が必要な場合
            UpdateViewCommand?.Execute(null);
        }

        // 既存フラグから初期値を推定（任意）
        public void InitializeSoilParamDisplayFromLegacyFlags()
        {
            if (IsDensityVisible || IsCohesiveVisible || IsVsVisible || IsEsVisible)
            {
                IsSoilLayerParamDisplayEnabled = true;
                if (IsDensityVisible) SelectedSoilParamDisplay = "密度";
                else if (IsCohesiveVisible) SelectedSoilParamDisplay = "粘着力";
                else if (IsVsVisible) SelectedSoilParamDisplay = "Vs";
                else if (IsEsVisible) SelectedSoilParamDisplay = "Es";
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
                    // ONにしたとき: 群杭荷重タブ→土層タブを表示し、θ=0にする
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

        // 節点番号描画
        private bool _isNodeNoVisible = true;
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

        // 要素番号描画
        private bool _isElementNoVisible = true;
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

        // 要素分割済か否か
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
                        // 要素分割が完了したら、保留中のSoilPiles生成をキャンセル
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

                OnPropertyChanged(nameof(HasAnyAnalysisResult));

                const string settlementLabel = "沈下量";
                const string singlePileLabel = "単杭";

                if (value)
                {
                    if (!AnalysisResultContentOption.Contains(settlementLabel))
                        AnalysisResultContentOption.Add(settlementLabel);
                    if (!AnalysisResultSettlementOption.Contains(singlePileLabel))
                        AnalysisResultSettlementOption.Add(singlePileLabel);
                }
                else
                {
                    AnalysisResultContentOption.Remove(settlementLabel);
                    AnalysisResultSettlementOption.Remove(singlePileLabel);
                }

                // ステータスバー更新
                OnPropertyChanged(nameof(AnalysisStatusText));

                Both(); // 単杭+群杭 の表示制御
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
                        AnalysisResultContentOption.Add(label);
                }
                else
                {
                    AnalysisResultContentOption.Remove(label);
                }
            }

            Toggle("沈下部材角", hasBeams && hasAnySettlement);
            Toggle("沈下反力", hasBeams && IsVerticalBeamAnalysisDone);
            Toggle("沈下応力", hasBeams && IsVerticalBeamAnalysisDone);
        }

        private void Both()
        {
            // "単杭+群杭"の表示制御
            const string bothLabel = "単杭+群杭";
            if (IsVerticalAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                if (!AnalysisResultSettlementOption.Contains(bothLabel))
                    AnalysisResultSettlementOption.Add(bothLabel);
            }
            else
            {
                AnalysisResultSettlementOption.Remove(bothLabel);
            }

            // "基礎梁考慮+群杭"の表示制御
            const string vbGroupLabel = "基礎梁考慮+群杭";
            if (IsVerticalBeamAnalysisDone && IsGroupPileSettlementAnalysisDone)
            {
                if (!AnalysisResultSettlementOption.Contains(vbGroupLabel))
                    AnalysisResultSettlementOption.Add(vbGroupLabel);
            }
            else
            {
                AnalysisResultSettlementOption.Remove(vbGroupLabel);
            }
        }

        // 鉛直解析済か否か
        private bool _isGroupPileSettlementAnalysisDone;
        public bool IsGroupPileSettlementAnalysisDone
        {
            get => _isGroupPileSettlementAnalysisDone;
            set
            {
                if (SetProperty(ref _isGroupPileSettlementAnalysisDone, value))
                {
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));

                    const string settlementLabel = "沈下量";
                    if (value)
                    {
                        if (!AnalysisResultContentOption.Contains(settlementLabel))
                            AnalysisResultContentOption.Add(settlementLabel);
                    }
                    else
                    {
                        AnalysisResultContentOption.Remove(settlementLabel);
                    }

                    const string groupPileLabel = "群杭";
                    if (value)
                    {
                        if (!AnalysisResultSettlementOption.Contains(groupPileLabel))
                            AnalysisResultSettlementOption.Add(groupPileLabel);
                    }
                    else
                    {
                        AnalysisResultSettlementOption.Remove(groupPileLabel);
                    }
                    Both();
                    UpdateSettlementCategories();
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
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));
                    OnPropertyChanged(nameof(AnalysisStatusText));
                    RaiseResultCommandsCanExecute();

                    // 基礎梁考慮 サブオプションの追加/削除
                    const string vbSubLabel = "基礎梁考慮";
                    if (value)
                    {
                        if (!AnalysisResultSettlementOption.Contains(vbSubLabel))
                            AnalysisResultSettlementOption.Add(vbSubLabel);
                    }
                    else
                    {
                        AnalysisResultSettlementOption.Remove(vbSubLabel);
                        VerticalBeamCaseResults = null;
                    }
                    UpdateSettlementCategories();
                }
            }
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
                    OnPropertyChanged(nameof(HasAnyAnalysisResult));

                    // "梁応力"の表示制御
                    const string beamForceLabel = "梁応力";
                    const string nodeDisplacementLabel = "節点変位";
                    const string nodeSoilSpringLabel = "地盤反力";
                    const string pileHeadMLabel = "杭頭Mマップ";
                    const string pileHeadQLabel = "杭頭Qマップ";
                    const string connectionMLabel = "接合点Mマップ";
                    const string connectionQLabel = "接合点Qマップ";
                    if (value)
                    {
                        // true: がなければ追加
                        if (!AnalysisResultContentOption.Contains(beamForceLabel))
                            AnalysisResultContentOption.Add(beamForceLabel);
                        if (!AnalysisResultContentOption.Contains(nodeDisplacementLabel))
                            AnalysisResultContentOption.Add(nodeDisplacementLabel);
                        if (!AnalysisResultContentOption.Contains(nodeSoilSpringLabel))
                            AnalysisResultContentOption.Add(nodeSoilSpringLabel);
                        if (!AnalysisResultContentOption.Contains(pileHeadMLabel))
                            AnalysisResultContentOption.Add(pileHeadMLabel);
                        if (!AnalysisResultContentOption.Contains(pileHeadQLabel))
                            AnalysisResultContentOption.Add(pileHeadQLabel);
                        if (!AnalysisResultContentOption.Contains(connectionMLabel))
                            AnalysisResultContentOption.Add(connectionMLabel);
                        if (!AnalysisResultContentOption.Contains(connectionQLabel))
                            AnalysisResultContentOption.Add(connectionQLabel);
                    }
                    else
                    {
                        // false: があれば削除
                        AnalysisResultContentOption.Remove(beamForceLabel);
                        AnalysisResultContentOption.Remove(nodeDisplacementLabel);
                        AnalysisResultContentOption.Remove(nodeSoilSpringLabel);
                        AnalysisResultContentOption.Remove(pileHeadMLabel);
                        AnalysisResultContentOption.Remove(pileHeadQLabel);
                        AnalysisResultContentOption.Remove(connectionMLabel);
                        AnalysisResultContentOption.Remove(connectionQLabel);
                    }

                    // 解析済みインジケータ更新
                    OnPropertyChanged(nameof(CurrentModel));
                    OnPropertyChanged(nameof(IsCurrentNonLiquefactionAnalyzed));
                    OnPropertyChanged(nameof(IsCurrentLiquefactionAnalyzed));

                    // ステータスバー更新
                    OnPropertyChanged(nameof(AnalysisStatusText));
                }
            }
        }

        // 解析後処理モード
        private bool _isPostAnalysisMode = false;
        public bool IsPostAnalysisMode
        {
            get => _isPostAnalysisMode;
            set => SetProperty(ref _isPostAnalysisMode, value);
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
        public double EditDistanceThreshold
        {
            get => _editDistanceThreshold;
            set => SetProperty(ref _editDistanceThreshold, value);
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
                if (SetProperty(ref _groupPileSettlementXOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double _groupPileSettlementYOffset;
        public double GroupPileSettlementYOffset
        {
            get => _groupPileSettlementYOffset;
            set
            {
                if (SetProperty(ref _groupPileSettlementYOffset, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
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
            //set => SetProperty(ref _groupPileSettlementXSpacing, value);
            set
            {
                if (SetProperty(ref _groupPileSettlementXSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
                    RequestUpdateWindow(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                }
            }
        }

        private double _groupPileSettlementYSpacing = 1.8;
        public double GroupPileSettlementYSpacing
        {
            get => _groupPileSettlementYSpacing;
            set
            {
                if (SetProperty(ref _groupPileSettlementYSpacing, value))
                {
                    IsGroupPileSettlementAnalysisDone = false;
                    IsGroupPileGridDeformationVisible = false;
                    CurrentInputModel.PileGroupSettlement.RemoveGridDataSettlement();
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

        public MainCanvasGeometry CanvasGeometry { get; }

        // MainWindowViewModel の partial 部分に追加

        [ObservableProperty] private int calculationReportLevel = 1;
        [ObservableProperty] private bool includeGroundInformation = true;
        [ObservableProperty] private bool includeLiquefaction = false;
        [ObservableProperty] private bool includeHorizontal = true;
        [ObservableProperty] private bool includeVertical = true;
        [ObservableProperty] private bool includeHorizontal_Bending = true;
        [ObservableProperty] private bool includeHorizontal_Shear = true;
        [ObservableProperty] private bool includeHorizontal_NMinT = true;
        [ObservableProperty] private bool includeHorizontal_QNInT = true;
        [ObservableProperty] private bool includeHorizontal_MPhi = true;
        [ObservableProperty] private bool includeHorizontal_MTheta = true;

        [ObservableProperty] private bool includePileLocationMap = false;
        [ObservableProperty] private bool includePileAxialLoadMap = false;
        [ObservableProperty] private bool includeIsFrontMap = false;
        [ObservableProperty] private bool includePileHeadMomentMap = false;
        [ObservableProperty] private bool includePileHeadShearMap = false;
        [ObservableProperty] private bool includeSettlement = true;
        [ObservableProperty] private bool includeLoadSettlementCurve = false;

        [ObservableProperty] private bool includeGroupPileSettlement = false;
        [ObservableProperty] private bool includeVerticalBeamResults = false;

        // 液状化有無の出力オプション
        [ObservableProperty] private bool includeOutputLiquefactionYes = true;
        [ObservableProperty] private bool includeOutputLiquefactionNo = true;
        [ObservableProperty] private bool isLiquefactionYesAnalyzed = false;
        [ObservableProperty] private bool isLiquefactionNoAnalyzed = false;

        // コンストラクタ //
        public MainWindowViewModel()
        {
            // Services の初期化
            _fileOperationService = new FileOperationService(_jsonOptions);
            _pileLayoutService = new PileLayoutService();
            _settlementAnalysisService = new SettlementAnalysisService();
            _autoSaveService = new AutoSaveService(_fileOperationService);
            _mruService = new MruService();

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

            // 沈下コンター図のキャッシュを無効化
            CurrentInputModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(CurrentInputModel.PileGroupSettlement))
                {
                    IsSettlementGridCacheValid = false;
                }
            };

            // 沈下コンター図のキャッシュを無効化
            CurrentInputModel.PileGroupSettlement.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PileGroupSettlement.SettlementGridX) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridY) ||
                    e.PropertyName == nameof(PileGroupSettlement.SettlementGridData))
                {
                    IsSettlementGridCacheValid = false;
                }
            };

            // コンストラクタ内の適当な位置
            OpenTableWindowCommand = new ToolkitRelayCommand(
                OpenTableWindow,
                () => (LatestResultTables != null && LatestResultTables.Count > 0) ||
                      (VerticalBeamCaseResults != null && VerticalBeamCaseResults.Count > 0));

        }

        private void PileLayoutItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel1s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceLevel2s) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVL0) ||
                e.PropertyName == nameof(PileLayoutDataItem.AxialForceVLAdditional) ||
                e.PropertyName == nameof(PileLayoutDataItem.X) ||
                e.PropertyName == nameof(PileLayoutDataItem.Y))
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

        private void UpdateSumAndOTM()
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
        }

        // DataGridSelectionコピーメソッド
        [RelayCommand]
        private static void CopyDataGridSelection(DataGrid dataGrid)
        {
            if (dataGrid == null || dataGrid.SelectedCells.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();

            var selectedCells = dataGrid.SelectedCells.GroupBy(cell => cell.Item).ToList();

            foreach (var row in selectedCells)
            {
                var rowValues = new List<string>();

                foreach (var cell in row)
                {
                    if (cell.Column.GetCellContent(cell.Item) is TextBlock textBlock)
                    {
                        rowValues.Add(textBlock.Text);
                    }
                }

                sb.AppendLine(string.Join("\t", rowValues));
            }

            Clipboard.SetText(sb.ToString());
        }

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
        }


        // 追加: IsApplicable 変更監視の購読セットアップ
        private void SubscribeLoadCaseApplicabilityChanged()
        {
            var lci = CurrentInputModel.LoadCasesInput;
            if (lci == null) return;

            void attach(IEnumerable<LoadCase> cases)
            {
                if (cases == null) return;
                foreach (var lc in cases)
                    lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
            }

            attach(lci.LoadCasesLevel1);
            attach(lci.LoadCasesLevel2);
            // attach(lci.AllLoadCombinations); // ← これが型不一致。不要なので削除

            // コレクションへの追加にも追随
            lci.LoadCasesLevel1.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            lci.LoadCasesLevel2.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (LoadCase lc in e.NewItems)
                        lc.PropertyChanged += LoadCase_PropertyChanged_ForOption;
                if (e.OldItems != null)
                    foreach (LoadCase lc in e.OldItems)
                        lc.PropertyChanged -= LoadCase_PropertyChanged_ForOption;
                UpdateLoadCaseOption();
            };
            lci.LoadCombinations.CollectionChanged += (s, e) =>
            {
                // 組合せが UI に影響する場合に再構築
                UpdateLoadCombinationOption();
            };
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
        }
    }
}
