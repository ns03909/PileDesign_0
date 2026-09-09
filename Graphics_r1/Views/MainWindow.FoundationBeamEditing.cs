using AvalonDock.Layout;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using MenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Shapes;
using System.Windows.Threading;

using Serilog;

namespace PileDesign.Views
{
    /// <summary>
    /// MainWindow — 基礎梁のビジュアル編集。
    ///
    /// 画面上でクリックして、基礎梁の節点や要素を足す / 消すモード。
    /// プレビュー線を出し、押した場所から一番近い節点を拾い、杭頭にスナップさせる。
    ///
    /// <b>平面表示のときだけ働く。</b> 斜めから見た画面でクリック位置を平面座標に
    /// 直すと、奥行きが決まらず思わぬ場所に置かれる
    /// (<see cref="IsPlanView"/> で判定している)。
    ///
    /// <b>杭頭へのスナップは Z も合わせる。</b> 平面で合っていても Z が違うと、
    /// 梁がつながらない別の節点になる。
    ///
    /// 以前は <c>MainWindow.xaml.cs</c> (4,090 行) の中にあった。
    /// </summary>
    public partial class MainWindow
    {
        #region 基礎梁ビジュアル編集 - イベントハンドラ

        /// <summary>
        /// 基礎梁追加のプレビュー線を描画
        /// </summary>
        private void DrawFoundationBeamPreview(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            if (vm.TempStartNode == null) return;

            // 既存のプレビュー線を削除
            ClearFoundationBeamPreview();

            // 開始ノードの座標を解決
            var coords = vm.CurrentInputModel.GetNodeCoordinates(vm.TempStartNode.Type, vm.TempStartNode.Id);
            if (coords == null) return; // 座標が見つからない場合は何もしない

            // 開始ノードの画面座標を取得
            var startNodeLoc3D = new Point3D(coords.Value.X, coords.Value.Y, coords.Value.Z);
            var startScreenPos = vm.CanvasThreeDView.Transformation(startNodeLoc3D);

            // プレビュー線を作成（破線スタイル）
            var previewLine = new Line
            {
                X1 = startScreenPos.X,
                Y1 = startScreenPos.Y,
                X2 = mousePos.X,
                Y2 = mousePos.Y,
                Stroke = Brushes.Orange,
                StrokeThickness = 2.0,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Tag = "FoundationBeamPreview",
                IsHitTestVisible = false // マウスイベントに反応しない
            };

            Canvas3DLayout.Children.Add(previewLine);
        }

        /// <summary>
        /// プレビュー線をクリア
        /// </summary>
        private void ClearFoundationBeamPreview()
        {
            var existingPreview = Canvas3DLayout.Children.OfType<Line>()
                .FirstOrDefault(l => l.Tag?.ToString() == "FoundationBeamPreview");
            if (existingPreview != null)
            {
                Canvas3DLayout.Children.Remove(existingPreview);
            }
        }

        /// <summary>
        /// 基礎梁編集モードのマウスクリック処理
        /// </summary>
        /// <returns>処理された場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleFoundationBeamEditMode(MouseButtonEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 編集モードがNoneの場合は何もしない
            if (vm.CurrentEditMode == CanvasEditMode.None)
            {
                return false;
            }

            Point mousePos = e.GetPosition(Canvas3DLayout);

            switch (vm.CurrentEditMode)
            {
                case CanvasEditMode.AddNode:
                    HandleAddNode(mousePos);
                    break;

                case CanvasEditMode.AddElement:
                    HandleAddElement(mousePos);
                    break;

                case CanvasEditMode.Delete:
                    HandleDelete(mousePos);
                    break;
            }

            // 編集モード中は常にtrueを返して選択処理を抑制
            return true;
        }

        /// <summary>
        /// ノード追加処理
        /// </summary>
        /// <returns>常にtrue（必ず処理を行う）</returns>
        private bool HandleAddNode(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 基礎梁入力データの初期化
            if (vm.CurrentInputModel.FoundationBeamInput == null)
            {
                vm.CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;

            // 一点目の場合は(0, 0, ΔZc=1.0)に固定
            if (nodes.Count == 0)
            {
                var firstNode = new FoundationNode
                {
                    No = 1,
                    X = 0,
                    Y = 0,
                    Z = 1.0, // デフォルトΔZc
                    Name = "Node-1"
                };
                nodes.Add(firstNode);
                vm.RequestUpdateWindow();
                return true;
            }

            // 二点目以降: マウス位置→3D座標変換
            Point3D? rawPos = GetXYFromMousePosition(mousePos);
            if (rawPos == null) return true; // 座標変換に失敗しても処理したとみなす

            // 杭位置にスナップ
            Point3D finalPos = ApplyPileSnap(rawPos.Value);

            // スナップした杭を見つけて、ΔZcを適用
            var piles = vm.CurrentInputModel.PileLayoutItems;
            double finalZ = finalPos.Z; // デフォルトはrawPosのZ

            foreach (var pile in piles)
            {
                double dx = pile.X - finalPos.X;
                double dy = pile.Y - finalPos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                // スナップした杭が見つかった場合（XY座標が一致）
                if (dist < 0.01) // 1cm以内なら一致とみなす
                {
                    // v2 セマンティクス: pile.Z は接合節点 Z (FoundationNode の Z はそのまま)
                    finalZ = pile.Z;
                    break;
                }
            }

            // 新しいノードを作成
            var newNode = new FoundationNode
            {
                No = nodes.Count + 1,
                X = finalPos.X,
                Y = finalPos.Y,
                Z = finalZ,
                Name = $"Node-{nodes.Count + 1}"
            };

            nodes.Add(newNode);

            // Undo登録（今後実装）
            // _undoManager.RegisterAction(new AddNodeAction(newNode));

            // 3D更新
            vm.RequestUpdateWindow();
            return true;
        }

        /// <summary>
        /// 要素追加処理（2クリック方式）
        /// </summary>
        /// <returns>処理した場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleAddElement(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 基礎梁入力データの初期化
            if (vm.CurrentInputModel.FoundationBeamInput == null)
            {
                vm.CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;

            // ヒットテストして節点参照を取得
            NodeReference? hitRef = null;
            string hitNodeName = "";

            // まず既存の基礎梁ノードをヒットテスト
            FoundationNode? hitFoundationNode = HitTestNode(mousePos);
            if (hitFoundationNode != null)
            {
                hitRef = new NodeReference(NodeReferenceType.FoundationNode, hitFoundationNode.Id);
                hitNodeName = hitFoundationNode.Name;
            }

            // 既存ノードがない場合、InputNode（一般節点）をヒットテスト
            if (hitRef == null)
            {
                var hitInputNode = HitTestInputNode(mousePos);
                if (hitInputNode != null)
                {
                    // Pile型のInputNodeはクリック不可（無視）
                    if (hitInputNode.Type == NodeType.Pile)
                    {
                        return false; // Pile型は無視するが、処理していないのでfalse
                    }

                    // General型のInputNodeを直接参照
                    hitRef = new NodeReference(NodeReferenceType.GeneralNode, hitInputNode.UniqueId);
                    hitNodeName = $"一般節点-{hitInputNode.No}";
                }
            }

            // 既存ノードがない場合、接合節点をヒットテスト
            if (hitRef == null && vm.IsConnectionNodeVisible)
            {
                var hitPile = HitTestConnectionNode(mousePos);
                if (hitPile != null)
                {
                    // 杭配置を直接参照
                    hitRef = new NodeReference(NodeReferenceType.PileLayout, hitPile.UniqueId);
                    hitNodeName = $"杭配置-{hitPile.PileNo}";
                }
            }

            if (hitRef == null)
            {
                // ノードがクリックされていない場合は何もしない
                return false;
            }

            if (vm.TempStartNode == null)
            {
                // 1回目のクリック: 開始ノードを記録
                vm.TempStartNode = hitRef;
                vm.StatusMessage = $"開始ノード: {hitNodeName} → 終了ノードをクリック";
            }
            else
            {
                // 2回目のクリック: 要素を作成
                if (hitRef.Type == vm.TempStartNode.Type && hitRef.Id == vm.TempStartNode.Id)
                {
                    MessageService.Show("同じノードは接続できません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    vm.TempStartNode = null;
                    vm.StatusMessage = string.Empty;
                    ClearFoundationBeamPreview(); // プレビュー線をクリア
                    return true; // 処理したのでtrue
                }

                var beams = vm.CurrentInputModel.FoundationBeamInput.Beams;

                // デフォルトの材料・断面番号 (1-based 位置インデックス)
                int defaultMaterialNo = vm.CurrentInputModel.FoundationBeamInput.Materials.Count > 0 ? 1 : 1;
                int defaultSectionNo = vm.CurrentInputModel.FoundationBeamInput.Sections.Count > 0 ? 1 : 1;

                var newBeam = new FoundationBeam
                {
                    // No プロパティ廃止 (位置 = ID)
                    // 新方式: Type + Guid で参照
                    NodeI_Type = vm.TempStartNode.Type,
                    NodeI_Id = vm.TempStartNode.Id,
                    NodeJ_Type = hitRef.Type,
                    NodeJ_Id = hitRef.Id,
                    // 材料・断面番号
                    MaterialNo = defaultMaterialNo,
                    SectionNo = defaultSectionNo,
                    SectionName = $"Beam-{beams.Count + 1}",
                    Width = 0.5,
                    Height = 0.8,
                    YoungModulus = 2.5e7,
                    ShearModulus = 1.04e7
                };

                beams.Add(newBeam);

                // 参照先 (MaterialNo / SectionNo) のデフォルトを保証
                vm.CurrentInputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();

                // リセット
                vm.TempStartNode = null;
                vm.StatusMessage = string.Empty;
                ClearFoundationBeamPreview(); // プレビュー線をクリア

                // Undo登録（今後実装）

                // 3D更新
                vm.RequestUpdateWindow();
            }

            return true; // 処理したのでtrue
        }

        /// <summary>
        /// 削除処理
        /// </summary>
        /// <returns>削除した場合はtrue、何もしなかった場合はfalse</returns>
        private bool HandleDelete(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // ノードをヒットテスト
            FoundationNode? hitNode = HitTestNode(mousePos);

            if (hitNode == null) return false;

            // 確認ダイアログ
            var result = MessageService.Show(
                $"ノード '{hitNode.Name}' を削除しますか？\n接続されている要素も削除されます。",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return false;

            var nodes = vm.CurrentInputModel.FoundationBeamInput.Nodes;
            var beams = vm.CurrentInputModel.FoundationBeamInput.Beams;

            // 接続されている要素を削除（カスケード削除）
            var beamsToRemove = beams.Where(b =>
                (b.NodeI_Type == NodeReferenceType.FoundationNode && b.NodeI_Id == hitNode.Id) ||
                (b.NodeJ_Type == NodeReferenceType.FoundationNode && b.NodeJ_Id == hitNode.Id)).ToList();
            foreach (var beam in beamsToRemove)
            {
                beams.Remove(beam);
            }

            // ノードを削除
            nodes.Remove(hitNode);

            // 節点 No は振り直し (FoundationNode.No は廃止対象外)。
            // 梁要素 No は廃止 (位置 = ID)
            for (int i = 0; i < nodes.Count; i++)
            {
                nodes[i].No = i + 1;
            }

            // Undo登録（今後実装）

            // 3D更新
            vm.RequestUpdateWindow();
            return true;
        }

        #endregion

        #region 基礎梁ビジュアル編集 - 座標変換

        /// <summary>
        /// 現在のビューが平面図（Phi≒90°）かどうかを判定
        /// </summary>
        private bool IsPlanView()
        {
            var vm = (MainWindowViewModel)DataContext;
            // Phi=90°±5°を平面図とみなす
            return Math.Abs(vm.CanvasThreeDView.Phi - 90.0) < 5.0;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）から3D座標（XY）を取得（平面図専用、Z固定）
        /// </summary>
        /// <param name="mousePos">Canvas上のマウス位置</param>
        /// <returns>3D座標（Z=DefaultFoundationBeamZ）</returns>
        private Point3D? GetXYFromMousePosition(Point mousePos)
        {
            var vm = (MainWindowViewModel)DataContext;

            // 既存のInverseTransformation()を使用
            Point3D worldPos = vm.CanvasThreeDView.InverseTransformation(mousePos);

            // Z座標は常にDefaultFoundationBeamZを使用（どのビューでも）
            // 杭位置にスナップする場合は、HandleAddNode内でΔZcが適用される
            return new Point3D(worldPos.X, worldPos.Y, vm.DefaultFoundationBeamZ);
        }

        /// <summary>
        /// 杭位置に自動スナップ（tolerance以内の杭があればその位置に補正）
        /// </summary>
        /// <param name="rawPos">生の3D座標</param>
        /// <param name="tolerance">スナップ許容距離（m）</param>
        /// <returns>スナップ後の3D座標</returns>
        private Point3D ApplyPileSnap(Point3D rawPos, double tolerance = 0.5)
        {
            var vm = (MainWindowViewModel)DataContext;
            var piles = vm.CurrentInputModel.PileLayoutItems;

            // 最も近い杭を検索
            double minDist = double.MaxValue;
            Point3D? snapPos = null;

            foreach (var pile in piles)
            {
                double dx = pile.X - rawPos.X;
                double dy = pile.Y - rawPos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist < minDist && dist < tolerance)
                {
                    minDist = dist;
                    snapPos = new Point3D(pile.X, pile.Y, rawPos.Z);
                }
            }

            return snapPos ?? rawPos;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）で基礎梁ノードをヒットテスト
        /// </summary>
        /// <param name="mousePos">Canvas上のマウス位置</param>
        /// <param name="hitRadius">ヒット半径（ピクセル）</param>
        /// <returns>ヒットしたノード（なければnull）</returns>
        private FoundationNode? HitTestNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var nodes = vm.CurrentInputModel.FoundationBeamInput?.Nodes;

            if (nodes == null || nodes.Count == 0) return null;

            // 各ノードの画面座標を計算してヒットテスト
            foreach (var node in nodes)
            {
                var nodeLoc3D = new Point3D(node.X, node.Y, node.Z);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）で接合節点（杭頭+ΔZc）をヒットテスト
        /// </summary>
        /// <returns>ヒットした杭のPileLayoutDataItem（なければnull）</returns>
        private PileLayoutDataItem? HitTestConnectionNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var piles = vm.CurrentInputModel?.PileLayoutItems;

            if (piles == null || piles.Count == 0) return null;

            // 各杭の接合節点位置でヒットテスト (v2 セマンティクス: pile.Z は接合節点 Z)
            foreach (var pile in piles)
            {
                double connectionZ = pile.Z;
                var nodeLoc3D = new Point3D(pile.X, pile.Y, connectionZ);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return pile;
                }
            }

            return null;
        }

        /// <summary>
        /// マウス位置（ピクセル座標）でInputNode（一般節点）をヒットテスト
        /// </summary>
        /// <returns>ヒットしたInputNode（なければnull）</returns>
        private InputNode? HitTestInputNode(Point mousePos, double hitRadius = 10.0)
        {
            var vm = (MainWindowViewModel)DataContext;
            var nodes = vm.CurrentInputModel?.InputNodes;

            if (nodes == null || nodes.Count == 0) return null;

            // 各InputNodeの画面座標を計算してヒットテスト
            foreach (var node in nodes)
            {
                if (!node.IsVisible) continue;

                var nodeLoc3D = new Point3D(node.X, node.Y, node.Z);
                var screenPos = vm.CanvasThreeDView.Transformation(nodeLoc3D);

                double dx = screenPos.X - mousePos.X;
                double dy = screenPos.Y - mousePos.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitRadius)
                {
                    return node;
                }
            }

            return null;
        }

        #endregion
    }
}
