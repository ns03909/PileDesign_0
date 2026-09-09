using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PileDesign.Views.AutoIsFrontPilesWindow;
using static PileDesign.Views.EditPileLayoutWindow;
using static PileDesign.Views.MoveCopyWindow;
using Point = System.Windows.Point;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel — 通り心・矩形荷重・沈下地層の編集。
    ///
    /// 画面の表で足す・消す・選ぶ操作。3 つを 1 つのファイルにまとめてあるのは、
    /// どれも「表の行を足して番号を振り直す」という同じ形をしているため。
    ///
    /// <b>通り心で選ぶのは表示のためではない。</b> 通り心上の基礎梁節点や梁要素を
    /// まとめて選ぶ操作があり、選択距離 (<see cref="GridSelectionDistance"/>) の
    /// 範囲に入るものを拾う。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // 通り心選択対象距離 (m)
        private double _gridSelectionDistance = 0.1;
        public double GridSelectionDistance
        {
            get => _gridSelectionDistance;
            set => SetProperty(ref _gridSelectionDistance, value);
        }

        // GridX追加メソッド
        [RelayCommand]
        private void AddGridX()
        {
            // Undoポイントを追加（1回の追加を1ステップで戻せるようにする）
            TrySaveUndoSnapshotSafely();

            // 防波堤: null の場合はここで生成
            CurrentInputModel.GridXItems ??= [];
            AddGrid(CurrentInputModel.GridXItems, "X1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridXItems));
        }

        // GridY追加メソッド
        [RelayCommand]
        private void AddGridY()
        {
            TrySaveUndoSnapshotSafely();
            CurrentInputModel.GridYItems ??= [];
            AddGrid(CurrentInputModel.GridYItems, "Y1", 7.2);
            OnPropertyChanged(nameof(CurrentInputModel.GridYItems));
        }

        // Grid追加メソッド
        private void AddGrid(ObservableCollection<GridDataItem> collection, string name, double spacing)
        {
            collection.Add(new GridDataItem());
            if (collection.Count == 1)
                collection[^1].Name = name;
            // 複数のアイテムがある場合、前のアイテムの設定をコピー
            else if (collection.Count == 2)
            {
                collection[^1].Spacing = spacing;
                collection[^1].Name = StringTransformer.TransformLastCharacter(collection[^2].Name);
            }
            else if (collection.Count >= 3)
            {
                collection[^1].Spacing = collection[^2].Spacing;
                collection[^1].Name = StringTransformer.TransformLastCharacter(collection[^2].Name);
            }
            RecalculateGrid(collection);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        private void RecalculateGrid(Collection<GridDataItem> collection)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (i == 0)
                {
                    collection[i].Spacing = 0;
                    collection[i].SpacingForeground = Brushes.Gray;
                    collection[i].CoordForeground = Brushes.Black;
                }
                else
                {
                    collection[i].Coord = collection[i - 1].Coord + collection[i].Spacing;
                    collection[i].SpacingForeground = Brushes.Black;
                    collection[i].CoordForeground = Brushes.Gray;
                }
            }
            // 変更: デバウンス付きで更新
            RequestUpdateWindow();
        }

        // 矩形荷重追加メソッド
        [RelayCommand]
        private void AddRectLoad()
        {
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (!ConfirmAnalysisConditionChange("両方", "矩形荷重 (追加)")) return;

            // Undoポイントを追加
            TrySaveUndoSnapshotSafely();

            CurrentInputModel.PileGroupSettlement.RectLoads.Add(new RectLoad());

            // 個別十字系で手動追加された場合は「任意矩形」に切り替え
            SwitchToAnyRectIfCrossType();

            IsGroupPileSettlementAnalysisDone = false;
            RequestUpdateWindow();
        }

        /// <summary>
        /// 反復解析タブの矩形荷重リセット: 個別矩形 (杭ごとに 1 矩形) を初期生成する。
        /// 各矩形の DX/DY は荷重面等価径から (√π·r) で算出 (取得不可なら 2.0m)、
        /// QA は VL 軸力 (= AxialForceVL0 + AxialForceVLAdditional)、LinkedPileNo は pile.PileNo。
        /// 既存の編集内容は破棄するため確認ダイアログを表示する。
        /// </summary>
        [RelayCommand]
        private void ResetBeamAwareRectLoads()
        {
            var pgs = CurrentInputModel?.PileGroupSettlement;
            var piles = CurrentInputModel?.PileLayoutItems;
            if (pgs == null || piles == null || piles.Count == 0) return;

            int existingCount = pgs.RectLoads?.Count ?? 0;
            if (existingCount > 0)
            {
                var res = MessageService.Show(
                    $"現在の矩形荷重 ({existingCount} 件) を破棄し、各杭に対して個別矩形を再生成します。\n続行しますか?",
                    "矩形荷重リセット確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (res != MessageBoxResult.OK) return;
            }

            TrySaveUndoSnapshotSafely();

            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            var newList = new System.Collections.ObjectModel.ObservableCollection<RectLoad>();
            foreach (var pile in piles)
            {
                double radius = 0;
                if (soilPiles != null && pile.SoilPileAltNo - 1 >= 0 && pile.SoilPileAltNo - 1 < soilPiles.Count)
                    radius = soilPiles[pile.SoilPileAltNo - 1].GroupPileLoadDia * 0.5;
                double side = radius > 0 ? Math.Sqrt(Math.PI) * radius : 2.0;
                double half = side * 0.5;
                double qa = pile.AxialForceVL0 + pile.AxialForceVLAdditional;
                newList.Add(new RectLoad
                {
                    X1 = pile.Point3D.X - half,
                    X2 = pile.Point3D.X + half,
                    Y1 = pile.Point3D.Y - half,
                    Y2 = pile.Point3D.Y + half,
                    QA = qa,
                    LinkedPileNo = pile.PileNo,
                });
            }
            pgs.RectLoads = newList;

            // 反復解析タブ用に LoadingType を「個別矩形（基礎梁考慮）」へ確定
            pgs.LoadingType = "個別矩形（基礎梁考慮）";

            IsGroupPileSettlementAnalysisDone = false;
            RequestUpdateWindow();
            ShowToast($"矩形荷重をリセットしました ({newList.Count} 件)。");
        }

        // 自動生成による RectLoads 置換中のフラグ（ユーザ編集と区別するため）
        private bool _suppressRectLoadAutoSwitch;

        /// <summary>
        /// 「個別十字」「個別十字（基礎梁反力）」「個別矩形」「個別矩形（基礎梁考慮）」が選択されている場合、
        /// RectLoads を自動生成値で置き換える。
        /// 「個別矩形」系では既存矩形の DX/DY (寸法) は維持し、中心座標と荷重 QA のみ最新値で更新する。
        /// </summary>
        public void RebuildAutoCrossRectLoadsIfNeeded()
        {
            if (CurrentInputModel?.PileGroupSettlement == null) return;
            var lt = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (lt != "個別十字" && lt != "個別十字（基礎梁反力）"
             && lt != "個別矩形" && lt != "個別矩形（基礎梁考慮）") return;

            // 個別十字（基礎梁反力）のみ VB 解析の杭反力を使用するため必須。
            // 個別矩形（基礎梁考慮）は VB 解析を要求しない (将来の反復実装で内部解析する)。
            if (lt == "個別十字（基礎梁反力）"
                && (!IsVerticalBeamAnalysisDone || VerticalBeamCaseResults == null || VerticalBeamCaseResults.Count == 0))
            {
                return;
            }

            var piles = CurrentInputModel.PileLayoutItems;
            var soilPiles = CurrentInputModel.ElementDivision?.SoilPiles;
            if (piles == null || piles.Count == 0 || soilPiles == null || soilPiles.Count == 0) return;

            var generated = SettlementAnalysisService.BuildAutoCrossRectLoads(
                CurrentInputModel.PileGroupSettlement, piles, soilPiles, VerticalBeamCaseResults);

            _suppressRectLoadAutoSwitch = true;
            try
            {
                CurrentInputModel.PileGroupSettlement.RectLoads = new ObservableCollection<RectLoad>(generated);
                IsRectLoadFreshFromAutoGen = true; // 自動生成直後はクリーン状態
            }
            finally
            {
                _suppressRectLoadAutoSwitch = false;
            }
        }

        /// <summary>
        /// 現在の荷重タイプが個別十字系の場合、「任意矩形」に切り替える。
        /// ユーザが荷重データグリッドを手動で編集 (位置 X1/X2/Y1/Y2 や荷重 QA) したときに呼ぶ。
        /// 個別矩形は DX/DY のみ編集 OK (位置・QA は自動再生成で復元) なので、ここでは切替えない。
        /// </summary>
        public void SwitchToAnyRectIfCrossType()
        {
            if (_suppressRectLoadAutoSwitch) return;
            if (CurrentInputModel?.PileGroupSettlement == null) return;
            var lt = CurrentInputModel.PileGroupSettlement.LoadingType;
            if (lt == "個別十字" || lt == "個別十字（基礎梁反力）")
            {
                CurrentInputModel.PileGroupSettlement.LoadingType = "任意矩形";
            }
        }

        // 群杭沈下検討用検討用土層追加メソッド
        [RelayCommand]
        private void AddSettlementSoilLayer()
        {
            if (!ConfirmAnalysisConditionChange("両方", "土層 (追加)")) return;

            TrySaveUndoSnapshotSafely();

            double bottomAlt;
            double ek;
            double poissonsRatio;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;



            if (CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Count == 0)
            {
                bottomAlt = CurrentInputModel.PileGroupSettlement.LoadingPlaneAltitude - 10.0;
                ek = 100_000_000;
                poissonsRatio = 0.3;
            }
            else
            {
                bottomAlt = settlementSoilLayers[^1].BottomAltitude - 10.0;
                ek = settlementSoilLayers[^1].Ek;
                poissonsRatio = settlementSoilLayers[^1].PoissonsRatio;
            }

            CurrentInputModel.PileGroupSettlement.SettlementSoilLayers.Add(
                new SettlementSoilLayer()
                {
                    BottomAltitude = bottomAlt,
                    Ek = ek,
                    PoissonsRatio = poissonsRatio
                });

            UpdateSettlementSoilLayer(); // 更新

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 全土層削除メソッド
        [RelayCommand]
        private void DeleteAllSettlementSoilLayers()
        {
            var settlement = CurrentInputModel?.PileGroupSettlement;
            if (settlement == null)
                return;

            TrySaveUndoSnapshotSafely();

            // 土層コレクションをクリア
            settlement.SettlementSoilLayers?.Clear();

            // 解析に用いるグリッドデータをクリア
            try
            {
                // Clear() は複製の中身を読む。空を代入すれば読まずに済む
                // (この複製はいずれ「読み込めるが書き出さない」形にする)。
                settlement.SettlementGridData = [];
                settlement.SettlementGridX?.Clear();
                settlement.SettlementGridY?.Clear();
            }
            catch
            {
                // 念のため例外は無視（コレクションが null の可能性など）
            }


            // 解析フラグと表示フラグをリセット
            IsGroupPileSettlementAnalysisDone = false;
            IsGroupPileGridDeformationVisible = false;
            IsBubbleVisible = false;
            IsArrowVisible = false;

            // 必要ならプロパティ更新通知
            OnPropertyChanged(nameof(CurrentInputModel));

            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        // 群杭沈下検討用検討用土層削除メソッド
        [RelayCommand]
        private void DeleteSettlementSoilLayer(object sender)
        {
            if (!ConfirmAnalysisConditionChange("両方", "土層 (削除)")) return;

            DeleteCollectionItem(
                sender,
                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers,
                () => UpdateSettlementSoilLayer());
        }

        // 群杭沈下検討用検討用土層データグリッド更新メソッド
        private void UpdateSettlementSoilLayer()
        {
            // 厚さは「土層上端 (SoilLayersTopAltitude)」基準で算出
            double topAltitude = CurrentInputModel.PileGroupSettlement.SoilLayersTopAltitude;
            ObservableCollection<SettlementSoilLayer> settlementSoilLayers = CurrentInputModel.PileGroupSettlement.SettlementSoilLayers;
            for (int i = 0; i < settlementSoilLayers.Count; i++)
            {
                if (i == 0)
                    settlementSoilLayers[i].Thickness = topAltitude - settlementSoilLayers[i].BottomAltitude;
                else
                    settlementSoilLayers[i].Thickness = settlementSoilLayers[i - 1].BottomAltitude - settlementSoilLayers[i].BottomAltitude;
            }
        }

        public void DataGridGridX_CurrentCellChanged()
        {
            RecalculateGrid(CurrentInputModel.GridXItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        public void DataGridGridY_CurrentCellChanged()
        {
            RecalculateGrid(CurrentInputModel.GridYItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }

        [RelayCommand]
        private void DataGridGridX_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
                RecalculateGrid(CurrentInputModel.GridXItems);
        }
        [RelayCommand]
        private void DataGridGridY_OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) || e.Key == Key.Right || e.Key == Key.Left)
                RecalculateGrid(CurrentInputModel.GridYItems);
        }

        [RelayCommand]
        private void DeleteGridX(object sender)
        {
            // Undoポイント
            TrySaveUndoSnapshotSafely();

            DeleteGridItem(sender, CurrentInputModel.GridXItems);
            RecalculateGrid(CurrentInputModel.GridXItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }
        [RelayCommand]
        private void DeleteGridY(object sender)
        {
            // Undoポイント
            TrySaveUndoSnapshotSafely();

            DeleteGridItem(sender, CurrentInputModel.GridYItems);
            RecalculateGrid(CurrentInputModel.GridYItems);
            // 変更後（以下の箇所で適用）
            RequestUpdateWindow();
        }
        [RelayCommand]
        private void SelectGridX(object parameter)
        {
            if (parameter is not GridDataItem gridItem) return;
            double coord = gridItem.Coord;
            double tolerance = GridSelectionDistance;

            ClearAllSelections();

            // 通り上の杭配置を選択
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                if (Math.Abs(pile.X - coord) <= tolerance)
                    pile.IsSelected = true;
            }

            // 通り上の一般節点を選択
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General && Math.Abs(node.X - coord) <= tolerance)
                        node.IsSelected = true;
                }
            }

            // 通り上の基礎梁節点を選択
            SelectFoundationNodesOnGrid(c => c.X, coord, tolerance);

            // 両端が通り上にある基礎梁を選択
            SelectFoundationBeamsOnGrid(c => c.X, coord, tolerance);

            RequestUpdateWindow();
        }

        [RelayCommand]
        private void SelectGridY(object parameter)
        {
            if (parameter is not GridDataItem gridItem) return;
            double coord = gridItem.Coord;
            double tolerance = GridSelectionDistance;

            ClearAllSelections();

            // 通り上の杭配置を選択
            foreach (var pile in CurrentInputModel.PileLayoutItems)
            {
                if (Math.Abs(pile.Y - coord) <= tolerance)
                    pile.IsSelected = true;
            }

            // 通り上の一般節点を選択
            if (CurrentInputModel.InputNodes != null)
            {
                foreach (var node in CurrentInputModel.InputNodes)
                {
                    if (node.Type == NodeType.General && Math.Abs(node.Y - coord) <= tolerance)
                        node.IsSelected = true;
                }
            }

            // 通り上の基礎梁節点を選択
            SelectFoundationNodesOnGrid(c => c.Y, coord, tolerance);

            // 両端が通り上にある基礎梁を選択
            SelectFoundationBeamsOnGrid(c => c.Y, coord, tolerance);

            RequestUpdateWindow();
        }

        /// <summary>
        /// 通り上の基礎梁節点（FoundationNode）を選択
        /// </summary>
        private void SelectFoundationNodesOnGrid(Func<(double X, double Y, double Z), double> getCoord, double coord, double tolerance)
        {
            var fbNodes = CurrentInputModel.FoundationBeamInput?.Nodes;
            if (fbNodes == null) return;

            foreach (var fnode in fbNodes)
            {
                if (Math.Abs(getCoord((fnode.X, fnode.Y, 0)) - coord) <= tolerance)
                    fnode.IsSelected = true;
            }
        }

        /// <summary>
        /// 両端が通り上にある基礎梁を選択
        /// </summary>
        private void SelectFoundationBeamsOnGrid(Func<(double X, double Y, double Z), double> getCoord, double coord, double tolerance)
        {
            var fbBeams = CurrentInputModel.FoundationBeamInput?.Beams;
            if (fbBeams == null) return;

            foreach (var beam in fbBeams)
            {
                var coordsI = CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                var coordsJ = CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                if (!coordsI.HasValue || !coordsJ.HasValue) continue;

                bool iOnGrid = Math.Abs(getCoord(coordsI.Value) - coord) <= tolerance;
                bool jOnGrid = Math.Abs(getCoord(coordsJ.Value) - coord) <= tolerance;

                if (iOnGrid && jOnGrid)
                    beam.IsSelected = true;
            }
        }

        /// <summary>
        /// すべての選択状態をクリアするヘルパー
        /// </summary>
        private void ClearAllSelections()
        {
            foreach (var pile in CurrentInputModel.PileLayoutItems)
                pile.IsSelected = false;
            if (CurrentInputModel.InputNodes != null)
                foreach (var node in CurrentInputModel.InputNodes)
                    node.IsSelected = false;
            if (CurrentInputModel.FoundationBeamInput != null)
            {
                foreach (var node in CurrentInputModel.FoundationBeamInput.Nodes)
                    node.IsSelected = false;
                foreach (var beam in CurrentInputModel.FoundationBeamInput.Beams)
                    beam.IsSelected = false;
            }
        }

        private static void DeleteGridItem(object sender, ObservableCollection<GridDataItem> collection)
        {
            // sender が GridDataItem であることを確認
            if (sender is not GridDataItem itemToDelete) return;

            // コレクションから削除
            collection.Remove(itemToDelete);
        }

        [RelayCommand]
        private void DeleteRectLoad(object sender)
        {
            // 解析結果が保存されている場合は警告 (両ルートとも RectLoads を共有するため両方破棄)
            if (!ConfirmAnalysisConditionChange("両方", "矩形荷重 (削除)")) return;

            DeleteCollectionItem(sender, CurrentInputModel.PileGroupSettlement.RectLoads, immediate: true);
            // ユーザ手動削除時は個別十字系から「任意矩形」に切替
            SwitchToAnyRectIfCrossType();
        }
    }
}
