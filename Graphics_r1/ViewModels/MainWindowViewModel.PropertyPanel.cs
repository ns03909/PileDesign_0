using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// プロパティパネルに 1 行として並ぶ項目。
    ///
    /// 読み取り専用・数値入力・選択肢の 3 通りがあり、編集したときに何をするかは
    /// <see cref="CommitAction"/> が持つ。値を書き戻すときは
    /// <see cref="SetValueSilent"/> を使う（<see cref="CommitAction"/> を呼ばない）。
    /// 呼ぶと、書き戻し → 変更通知 → また書き戻し、と回る。
    /// </summary>
    public enum PropertyInputType { ReadOnly, Number, ComboBox }

    public class PropertyPanelItem(
        string name,
        string value,
        string unit = "",
        PropertyInputType inputType = PropertyInputType.ReadOnly,
        Action<PropertyPanelItem, string>? commitAction = null,
        IReadOnlyList<string>? options = null,
        string nameColor = null,
        string description = null) : INotifyPropertyChanged
    {
        public string Name { get; } = name;
        public string Unit { get; } = unit;
        public string NameColor { get; } = nameColor;
        public PropertyInputType InputType { get; } = inputType;
        public IReadOnlyList<string>? Options { get; } = options;

        /// <summary>項目ホバー時にツールチップとして表示する概要 (任意)。空ならツールチップなし。</summary>
        public string Description { get; } = description;
        public bool HasDescription => !string.IsNullOrEmpty(Description);

        /// <summary>Unit が非空かを返す (XAML の Unit ラベル可視性制御に使用)</summary>
        public bool HasUnit => !string.IsNullOrEmpty(Unit);

        private string _value = value;
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
        public Action<PropertyPanelItem, string>? CommitAction { get; } = commitAction;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// MainWindowViewModel — プロパティパネル。
    ///
    /// 画面で選んだもの（杭・梁・一般節点・基礎梁節点）の諸元を 1 列に並べ、
    /// 編集できるものは編集させる。複数選択のときは「合計」と「各値」を切り替える。
    ///
    /// 以前は <c>MainWindowViewModel.Constructor.cs</c> の中にあった。
    /// あのファイルは 4,542 行あり、名前のとおりのコンストラクタは最後の 9% だけで、
    /// 残りは寄せ集めだった。何がどこにあるか、開いてみるまで分からない状態だった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // プロパティパネル: 選択アイテムのプロパティ一覧
        public ObservableCollection<PropertyPanelItem> SelectedItemProperties { get; } = [];

        // プロパティパネル: 複数選択時の表示モード (true=合計、false=各値)
        [ObservableProperty]
        private bool _propertyDisplayIsTotal = true;

        partial void OnPropertyDisplayIsTotalChanged(bool value)
        {
            // 表示モード変更時にプロパティパネルを再構築
            UpdatePropertyPanel();
        }

        // プロパティパネル: 複数選択中かどうか (トグルボタン表示制御用)
        public bool IsMultipleSelection
        {
            get
            {
                var pileCount = CurrentInputModel?.PileLayoutItems?.Count(p => p.IsSelected) ?? 0;
                if (pileCount > 1) return true;
                var beamCount = CurrentInputModel?.FoundationBeamInput?.Beams?.Count(b => b.IsSelected) ?? 0;
                if (beamCount > 1) return true;
                var nodeCount = CurrentInputModel?.InputNodes?.Count(n => n.IsSelected && n.Type == Models.InputData.NodeType.General) ?? 0;
                if (nodeCount > 1) return true;
                return false;
            }
        }

        // プロパティパネル: 杭が 1 本以上選択されているか (軸力モードトグル表示制御用)
        public bool IsAnyPileSelected
            => CurrentInputModel?.PileLayoutItems?.Any(p => p.IsSelected) ?? false;

        // プロパティパネル: 選択中アイテムのPropertyChanged購読管理
        private INotifyPropertyChanged? _subscribedPropertyItem;

        public string SelectedItemHeader
        {
            get
            {
                var piles = CurrentInputModel?.PileLayoutItems?.Where(p => p.IsSelected).ToList();
                if (piles?.Count == 1) return $"杭 #{CurrentInputModel!.PileLayoutItems.IndexOf(piles[0]) + 1}";
                if (piles?.Count > 1) return $"杭 ×{piles.Count}";

                var beams = CurrentInputModel?.FoundationBeamInput?.Beams?.Where(b => b.IsSelected).ToList();
                if (beams?.Count == 1) return $"梁要素 #{CurrentInputModel!.FoundationBeamInput!.GetBeamNo(beams[0])}";
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
            OnPropertyChanged(nameof(IsMultipleSelection));
            OnPropertyChanged(nameof(IsAnyPileSelected));

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

            // <b>背景スレッドから来ることがある。</b>
            // 解析モデルの組立 (AnalysisModelling) は Task.Run で走り、その中で杭へ
            // FEM の参照 (PileTopRotationalSpring など) を書き込む。杭を選択していると
            // その通知がここへ届く。プロパティ一覧は UI にバインドされたコレクションなので、
            // 背景スレッドから触ると CollectionView が NotSupportedException を投げて
            // <b>解析ごと落ちる</b> (杭を選んでいるときだけ再現するので気付きにくい)。
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => OnSelectedItemPropertyChanged(sender, e)));
                return;
            }

            // プロパティ一覧を再構築
            SelectedItemProperties.Clear();
            OnPropertyChanged(nameof(SelectedItemHeader));

            if (sender is PileLayoutDataItem pile) BuildPileProperties(pile);
            else if (sender is FoundationBeam beam) BuildBeamProperties(beam);
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
            if (CurrentInputModel == null) return;
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
            SelectedItemProperties.Add(new("Z (接合節点)", $"{pile.Z:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.Z, v => pile.Z = v)));
            // 杭頭 Z は読み取り専用で参考表示 (= pile.Z - ΔZc)
            SelectedItemProperties.Add(new("Z (杭頭)", $"{pile.PileHeadZ:F3}", "m"));

            SelectedItemProperties.Add(new("杭体No", $"{pile.PileBodyNo}", "",
                PropertyInputType.ComboBox,
                MakeIntCommit(() => pile.PileBodyNo, v => pile.PileBodyNo = v),
                pileBodyOptions,
                description: CurrentInputModel.GetPileBodySummary(pile.PileBodyNo)));
            SelectedItemProperties.Add(new("地盤No", $"{pile.GroundNo}", "",
                PropertyInputType.ComboBox,
                MakeIntCommit(() => pile.GroundNo, v => pile.GroundNo = v),
                groundOptions,
                description: CurrentInputModel.GetGroundSummary(pile.GroundNo)));

            var pileLen = CalcPileLength(pile);
            if (pileLen.HasValue)
                SelectedItemProperties.Add(new("杭長", $"{pileLen.Value:F3}", "m"));

            SelectedItemProperties.Add(new("群杭係数 ξ", $"{pile.GroupPileFactor:F3}", "",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.GroupPileFactor, v => pile.GroupPileFactor = v)));
            SelectedItemProperties.Add(new("杭間隔比 R/B", $"{pile.PileSpacingFactor:F3}"));
            SelectedItemProperties.Add(new("ΔZc", $"{pile.FoundationBeamDeltaZc:F3}", "m",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.FoundationBeamDeltaZc, v => pile.FoundationBeamDeltaZc = v)));

            // 軸力 VL（VL0 を編集、表示は VL0 の値）— ディープブルー
            SelectedItemProperties.Add(new("軸力 VL", $"{pile.AxialForceVL0:F1}", "kN",
                PropertyInputType.Number,
                MakeDoubleCommit(() => pile.AxialForceVL0, v => pile.AxialForceVL0 = v, "F1"),
                nameColor: "#3271AD"));

            bool isVar = Common.AxialForceModeContext.IsVariationMode;

            // 軸力: レベル1 — 緑 (絶対 or 変動)
            for (int i = 0; i < pile.AxialForceLevel1s.Count; i++)
            {
                int idx = i;
                if (isVar)
                {
                    SelectedItemProperties.Add(new($"ΔN 1-{i + 1}", $"{pile.AxialForceVariationLevel1s[i]:F1}", "kN",
                        PropertyInputType.Number,
                        MakeDoubleCommit(
                            () => pile.AxialForceVariationLevel1s[idx],
                            v => pile.AxialForceVariationLevel1s[idx] = v, "F1"),
                        nameColor: "#238966"));
                }
                else
                {
                    SelectedItemProperties.Add(new($"軸力 1-{i + 1}", $"{pile.AxialForceLevel1s[i]:F1}", "kN",
                        PropertyInputType.Number,
                        MakeDoubleCommit(
                            () => pile.AxialForceLevel1s[idx],
                            v => pile.AxialForceLevel1s[idx] = v, "F1"),
                        nameColor: "#238966"));
                }
            }

            // 軸力: レベル2 — 桃赤 (絶対 or 変動)
            for (int i = 0; i < pile.AxialForceLevel2s.Count; i++)
            {
                int idx = i;
                if (isVar)
                {
                    SelectedItemProperties.Add(new($"ΔN 2-{i + 1}", $"{pile.AxialForceVariationLevel2s[i]:F1}", "kN",
                        PropertyInputType.Number,
                        MakeDoubleCommit(
                            () => pile.AxialForceVariationLevel2s[idx],
                            v => pile.AxialForceVariationLevel2s[idx] = v, "F1"),
                        nameColor: "#E95541"));
                }
                else
                {
                    SelectedItemProperties.Add(new($"軸力 2-{i + 1}", $"{pile.AxialForceLevel2s[i]:F1}", "kN",
                        PropertyInputType.Number,
                        MakeDoubleCommit(
                            () => pile.AxialForceLevel2s[idx],
                            v => pile.AxialForceLevel2s[idx] = v, "F1"),
                        nameColor: "#E95541"));
                }
            }
        }

        private void BuildBeamProperties(FoundationBeam beam)
        {
            if (CurrentInputModel == null) return;
            SelectedItemProperties.Add(new("要素No",    $"{CurrentInputModel.FoundationBeamInput.GetBeamNo(beam)}"));
            SelectedItemProperties.Add(new("I端節点No", CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeI_Type, beam.NodeI_Id)));
            SelectedItemProperties.Add(new("J端節点No", CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeJ_Type, beam.NodeJ_Id)));
            SelectedItemProperties.Add(new("材料No",    $"{beam.MaterialNo}"));
            SelectedItemProperties.Add(new("断面No",    $"{beam.SectionNo}"));
            SelectedItemProperties.Add(new("幅",        $"{beam.Width:F3}", "m"));
            SelectedItemProperties.Add(new("高さ",      $"{beam.Height:F3}", "m"));
            SelectedItemProperties.Add(new("ヤング率",   $"{beam.YoungModulus / 1000.0:N0}", "N/mm²"));
            SelectedItemProperties.Add(new("横弾性係数", $"{beam.ShearModulus / 1000.0:N0}", "N/mm²"));

            // 角度β のみ編集可能
            SelectedItemProperties.Add(new("角度β", $"{beam.AngleBeta:F1}", "°",
                PropertyInputType.Number,
                MakeDoubleCommit(() => beam.AngleBeta, v => beam.AngleBeta = v, "F1")));

            var len = CalcBeamLength(beam);
            if (len.HasValue)
                SelectedItemProperties.Add(new("部材長", $"{len.Value:F3}", "m"));
        }

        private double? CalcBeamLength(FoundationBeam beam)
        {
            if (beam.NodeI_Id == Guid.Empty || beam.NodeJ_Id == Guid.Empty) return null;
            if (CurrentInputModel == null) return null;
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
            if (idx < 0 || CurrentInputModel?.PileBodies == null || idx >= CurrentInputModel.PileBodies.Count)
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
            SelectedItemProperties.Add(new("X", $"{fNode.X:F3}", "m"));
            SelectedItemProperties.Add(new("Y", $"{fNode.Y:F3}", "m"));
            SelectedItemProperties.Add(new("Z", $"{fNode.Z:F3}", "m"));
        }

        // -------------------------------------------------------
        // 複数選択 Build メソッド
        // -------------------------------------------------------

        private static string CommonOrVarious<T>(IEnumerable<T> values)
        {
            var distinct = values.Distinct().ToList();
            return distinct.Count == 1 ? $"{distinct[0]}" : "(various)";
        }

        private static string CommonDoubleOrVarious(IEnumerable<double> values, string format = "F3", double scale = 1.0)
        {
            var distinct = values.Select(v => Math.Round(v, 6)).Distinct().ToList();
            return distinct.Count == 1 ? (distinct[0] * scale).ToString(format) : "(various)";
        }

        private void BuildMultiPileProperties(List<PileLayoutDataItem> piles)
        {
            if (CurrentInputModel == null) return;
            var pileBodyOptions = CurrentInputModel.PileBodiesCountList.Select(x => x.ToString()).ToList();
            var groundOptions   = CurrentInputModel.GroundsInputCountList.Select(x => x.ToString()).ToList();

            SelectedItemProperties.Add(new("選択数", $"{piles.Count} 本"));

            // 杭長 (total/each 切替、読み取り専用)
            double totalPileLen = 0; int countValidLen = 0;
            var pileLengths = new List<double>();
            foreach (var p in piles) { var l = CalcPileLength(p); if (l.HasValue) { totalPileLen += l.Value; countValidLen++; pileLengths.Add(l.Value); } }
            if (countValidLen > 0)
            {
                if (PropertyDisplayIsTotal)
                    SelectedItemProperties.Add(new("杭長 (total)", $"{totalPileLen:F3}", "m"));
                else
                    SelectedItemProperties.Add(new("杭長 (each)", CommonDoubleOrVarious(pileLengths), "m"));
            }

            // 杭体No（ComboBox: 同一値なら選択可、様々なら空欄）
            var commonPileBodyNo = piles.Select(p => p.PileBodyNo).Distinct().ToList();
            // 同一値ならその杭体のサマリーをツールチップに、混在なら使用中の杭体 No 一覧を簡潔に
            string pileBodyDesc;
            if (commonPileBodyNo.Count == 1)
                pileBodyDesc = CurrentInputModel.GetPileBodySummary(commonPileBodyNo[0]);
            else
                pileBodyDesc = $"選択杭で混在: 杭体 No.{string.Join(", No.", commonPileBodyNo.OrderBy(n => n))}";
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
                }, pileBodyOptions,
                description: pileBodyDesc));

            // 地盤No（ComboBox）
            var commonGroundNo = piles.Select(p => p.GroundNo).Distinct().ToList();
            string groundDesc;
            if (commonGroundNo.Count == 1)
                groundDesc = CurrentInputModel.GetGroundSummary(commonGroundNo[0]);
            else
                groundDesc = $"選択杭で混在: 地盤 No.{string.Join(", No.", commonGroundNo.OrderBy(n => n))}";
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
                }, groundOptions,
                description: groundDesc));

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

            // 杭間隔比（読み取り専用）
            SelectedItemProperties.Add(new("杭間隔比 R/B", CommonDoubleOrVarious(piles.Select(p => p.PileSpacingFactor))));

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

            // 軸力 VL (total/each 切替、読み取り専用) — ディープブルー
            // 単位 kN は別パラメータで渡し、XAML 側で Foreground=#888 の軽量ラベルとして描画させる
            if (PropertyDisplayIsTotal)
                SelectedItemProperties.Add(new("軸力 VL (total)", $"{piles.Sum(p => p.AxialForceVL):F1}", "kN", nameColor: "#3271AD"));
            else
                SelectedItemProperties.Add(new("軸力 VL (each)", CommonDoubleOrVarious(piles.Select(p => p.AxialForceVL), "F1"), "kN", nameColor: "#3271AD"));

            bool isVar = Common.AxialForceModeContext.IsVariationMode;
            string l1Prefix = isVar ? "ΔN" : "軸力";
            string l2Prefix = isVar ? "ΔN" : "軸力";

            // レベル1軸力 — 緑 (total: 合計・読み取り専用、each: 各値・編集可)
            int level1Count = piles.Min(p => p.AxialForceLevel1s.Count);
            for (int i = 0; i < level1Count; i++)
            {
                int idx = i;
                // 絶対モードでは AxialForceLevel1s、変動モードでは AxialForceVariationLevel1s を読み書き
                System.Func<PileLayoutDataItem, double> getter = isVar
                    ? (p => p.AxialForceVariationLevel1s[idx])
                    : (p => p.AxialForceLevel1s[idx]);
                System.Action<PileLayoutDataItem, double> setter = isVar
                    ? ((p, v) => p.AxialForceVariationLevel1s[idx] = v)
                    : ((p, v) => p.AxialForceLevel1s[idx] = v);

                if (PropertyDisplayIsTotal)
                {
                    SelectedItemProperties.Add(new($"{l1Prefix} 1-{i + 1} (total)",
                        $"{piles.Sum(p => getter(p)):F1}", "kN",
                        nameColor: "#238966"));
                }
                else
                {
                    SelectedItemProperties.Add(new($"{l1Prefix} 1-{i + 1} (each)",
                        CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1"), "kN",
                        PropertyInputType.Number,
                        (item, rawValue) =>
                        {
                            if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1")); return; }
                            if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1")); return; }
                            SaveUndoState();
                            foreach (var p in piles) setter(p, newVal);
                            RequestUpdateWindow();
                        },
                        nameColor: "#238966"));
                }
            }

            // レベル2軸力 — 桃赤 (total: 合計・読み取り専用、each: 各値・編集可)
            int level2Count = piles.Min(p => p.AxialForceLevel2s.Count);
            for (int i = 0; i < level2Count; i++)
            {
                int idx = i;
                System.Func<PileLayoutDataItem, double> getter = isVar
                    ? (p => p.AxialForceVariationLevel2s[idx])
                    : (p => p.AxialForceLevel2s[idx]);
                System.Action<PileLayoutDataItem, double> setter = isVar
                    ? ((p, v) => p.AxialForceVariationLevel2s[idx] = v)
                    : ((p, v) => p.AxialForceLevel2s[idx] = v);

                if (PropertyDisplayIsTotal)
                {
                    SelectedItemProperties.Add(new($"{l2Prefix} 2-{i + 1} (total)",
                        $"{piles.Sum(p => getter(p)):F1}", "kN",
                        nameColor: "#E95541"));
                }
                else
                {
                    SelectedItemProperties.Add(new($"{l2Prefix} 2-{i + 1} (each)",
                        CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1"), "kN",
                        PropertyInputType.Number,
                        (item, rawValue) =>
                        {
                            if (!double.TryParse(rawValue, out var newVal)) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1")); return; }
                            if (!CheckAndResetAnalysisResults()) { item.SetValueSilent(CommonDoubleOrVarious(piles.Select(p => getter(p)), "F1")); return; }
                            SaveUndoState();
                            foreach (var p in piles) setter(p, newVal);
                            RequestUpdateWindow();
                        },
                        nameColor: "#E95541"));
                }
            }
        }

        private void BuildMultiBeamProperties(List<FoundationBeam> beams)
        {
            SelectedItemProperties.Add(new("選択数",    $"{beams.Count} 本"));
            SelectedItemProperties.Add(new("材料No",    CommonOrVarious(beams.Select(b => b.MaterialNo))));
            SelectedItemProperties.Add(new("断面No",    CommonOrVarious(beams.Select(b => b.SectionNo))));
            SelectedItemProperties.Add(new("幅",        CommonDoubleOrVarious(beams.Select(b => b.Width)), "m"));
            SelectedItemProperties.Add(new("高さ",      CommonDoubleOrVarious(beams.Select(b => b.Height)), "m"));
            SelectedItemProperties.Add(new("ヤング率",   CommonDoubleOrVarious(beams.Select(b => b.YoungModulus), "N0", 0.001), "N/mm²"));
            SelectedItemProperties.Add(new("横弾性係数", CommonDoubleOrVarious(beams.Select(b => b.ShearModulus), "N0", 0.001), "N/mm²"));
            SelectedItemProperties.Add(new("角度β",     CommonDoubleOrVarious(beams.Select(b => b.AngleBeta), "F1"), "°"));

            double totalLen = 0; int countValid = 0;
            foreach (var b in beams) { var l = CalcBeamLength(b); if (l.HasValue) { totalLen += l.Value; countValid++; } }
            if (countValid > 0)
                SelectedItemProperties.Add(new("部材長 (total)", $"{totalLen:F3}", "m"));
        }

        private void BuildMultiInputNodeProperties(List<InputNode> nodes)
        {
            SelectedItemProperties.Add(new("選択数", $"{nodes.Count} 個"));
            SelectedItemProperties.Add(new("タイプ", CommonOrVarious(nodes.Select(n => n.Type))));
        }
    }
}
