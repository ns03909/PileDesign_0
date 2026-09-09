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
    /// MainWindow — 3D キャンバスのマウス操作。
    ///
    /// 押す・動かす・離す・ホイール。左は選択と矩形選択、右は回転と平行移動、
    /// ホイールは拡大縮小。右クリックのメニューもここで組み立てる。
    ///
    /// <b>回転は CompositionTarget.Rendering で回す。</b> マウスが動くたびに描き直すと
    /// 追いつかないので、動いた位置だけ控えておき、描画のタイミングでまとめて反映する。
    /// 回していない間は購読を外すこと (<see cref="UnhookRendering"/>)。
    /// 付けっぱなしにすると、何もしていなくても毎フレーム呼ばれ続ける。
    ///
    /// <b>右ドラッグは「回した」か「クリックした」かを距離で分ける。</b>
    /// 少しでも動いたらメニューを出さない、では出したいときに出ない。
    /// 閾値 (<c>RightClickDragThreshold</c>) を置いてある。
    ///
    /// 以前は <c>MainWindow.xaml.cs</c> (4,090 行) の中にあった。
    /// </summary>
    public partial class MainWindow
    {
        //// Mouse Event ////


        // マウスがプレスされたときの処理
        private void Canvas3DLayout_MouseDown(object sender, MouseButtonEventArgs e)
        {
            {
                if (e.MiddleButton == MouseButtonState.Pressed)
                {
                    IsMouseWheelPressed = true;
                    previousMousePosition = e.GetPosition(Canvas3DLayout);
                }
            }
        }

        // 置換: Canvas外へ出た/キャプチャロスト時の後始末（ドラッグフラグもクリア）
        private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        {
            IsMouseWheelPressed = false;
            IsRightButtonClicked = false;

            _isRotatingView = false;
            _rightDragged = false;
            UnhookRendering();
            isLightweightDrawing = false;

            // 沈下マップツールチップを非表示
            HideSettlementTooltip();

            if (e.LeftButton == MouseButtonState.Released)
            {
                Canvas3DLayout.ReleaseMouseCapture();
            }

            if (selectionRectangle != null)
            {
                endPoint = e.GetPosition(Canvas3DLayout);
                ConfirmSelection3D();
                Canvas3DLayout.Children.Remove(selectionRectangle);
                selectionRectangle = null;
            }
        }

        private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        {
            IsMouseWheelPressed = false;
            IsRightButtonClicked = false;

            _isRotatingView = false;
            _rightDragged = false;
            UnhookRendering();
            isLightweightDrawing = false;
        }

        //// 置換: Canvas外へ出た/キャプチャロスト時の後始末
        //private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    _isRotatingView = false;
        //    UnhookRendering();
        //    isLightweightDrawing = false;

        //    if (e.LeftButton == MouseButtonState.Released)
        //    {
        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }

        //    if (selectionRectangle != null)
        //    {
        //        endPoint = e.GetPosition(Canvas3DLayout);
        //        ConfirmSelection3D();
        //        Canvas3DLayout.Children.Remove(selectionRectangle);
        //        selectionRectangle = null;
        //    }
        //}


        //private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    _isRotatingView = false;
        //    UnhookRendering();
        //    isLightweightDrawing = false;
        //}

        //// マウスがCanvasの範囲外に出た時の処理
        //private void Canvas3DLayout_MouseLeave(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;

        //    if (e.LeftButton == MouseButtonState.Released)
        //    {
        //        // マウスキャプチャを解除
        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }

        //    // マウスがCanvasの範囲外に出た時の処理
        //    if (selectionRectangle != null)
        //    {
        //        // 選択範囲を確定する
        //        endPoint = e.GetPosition(Canvas3DLayout);
        //        ConfirmSelection3D();

        //        // SelectionRectangleを消す
        //        Canvas3DLayout.Children.Remove(selectionRectangle);
        //        selectionRectangle = null;
        //    }
        //}

        private int? FindNearestNodeIndex(Point pos)
        {
            var viewModel = _mainWindowViewModel;
            double minDist = 15.0; // ピクセル閾値
            int? nearestIndex = null;
            for (int i = 0; i < viewModel.CurrentInputModel.PileLayoutItems.Count; i++)
            {
                var node = viewModel.CurrentInputModel.PileLayoutItems[i];
                var screenPt = viewModel.CanvasThreeDView.Transformation(node.Point3D);
                double dist = (screenPt - pos).Length;
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestIndex = i;
                }
            }
            return nearestIndex;
        }
        // マウス左ボタンが押された時のメソッド
        private void Canvas3DLayout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // ダブルクリック: 選択中の杭/梁のプロパティ編集
            if (e.ClickCount == 2)
            {
                var viewModel = _mainWindowViewModel;
                var selectedPile = viewModel.CurrentInputModel.PileLayoutItems.FirstOrDefault(p => p.IsSelected);
                var selectedBeam = viewModel.CurrentInputModel.FoundationBeamInput?.Beams.FirstOrDefault(b => b.IsSelected);
                if (selectedPile != null)
                {
                    viewModel.EditAddPilesCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                else if (selectedBeam != null)
                {
                    viewModel.EditBeamElementsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // 基礎梁ビジュアル編集モードの処理
            if (HandleFoundationBeamEditMode(e))
            {
                return; // 編集モードで処理された場合は早期リターン
            }

            startPoint = e.GetPosition(Canvas3DLayout);

            IsRightButtonClicked = false;
            IsMouseWheelPressed = false;
            // マウスキャプチャを設定
            Canvas3DLayout.CaptureMouse();

            // Canvas にキーボードフォーカスを設定
            Canvas3DLayout.Focus();

            // Shiftキーが押されている場合の処理
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                // クリック位置の周辺に節点があるかチェック
                SelectNode3DIfNearby(startPoint, true);
            }
            // Shiftキーが押されていない場合の処理
            else
            {
                ClearCanvasSelection();

                // クリック位置の周辺に節点があるかチェック
                SelectNode3DIfNearby(startPoint, false);

                var elementToRemove = Canvas3DLayout.Children.OfType<Path>().FirstOrDefault(p => p.Name == "Selection");
                if (elementToRemove != null)
                {
                    Canvas3DLayout.Children.Remove(elementToRemove);
                }
            }

            // 左ボタンプレスの場合
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 選択窓
                startPoint = e.GetPosition(Canvas3DLayout);
                selectionRectangle = new Rectangle
                {
                    //Stroke = Brushes.Black,
                    StrokeThickness = 1,
                    Opacity = 0.3,
                    Fill = Brushes.LightBlue,
                    Stroke = Brushes.Black

                };

                Canvas.SetLeft(selectionRectangle, startPoint.X);
                Canvas.SetTop(selectionRectangle, startPoint.Y);

                // ★常に最前面にする
                Panel.SetZIndex(selectionRectangle, 10000);

                Canvas3DLayout.Children.Add(selectionRectangle);
            }
        }

        private const double DragThreshold = 5.0; // ドラッグとみなす移動距離の閾値


        // マウス左ボタンが離された時のメソッド
        private void Canvas3DLayout_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // マウスキャプチャを解除
            Canvas3DLayout.ReleaseMouseCapture();

            // 編集モード中は選択処理を抑制
            if (DataContext is MainWindowViewModel vm && vm.CurrentEditMode != CanvasEditMode.None)
            {
                Canvas3DLayout.Children.Remove(selectionRectangle);
                selectionRectangle = null;
                return;
            }

            // マウスの左ボタンが離された時の処理
            endPoint = e.GetPosition(Canvas3DLayout);

            // 移動距離を計算
            double distance = (endPoint - startPoint).Length;

            if (distance > DragThreshold)
            {
                // ドラッグとみなす処理
                ConfirmSelection3D();
            }
            else
            {
                // クリックとみなす処理
                //HandleClick();
            }

            // SelectionRectangleを消す
            Canvas3DLayout.Children.Remove(selectionRectangle);
            selectionRectangle = null;
        }


        // 追加フィールド（クラス内に追加）
        private Point _rightDragAnchorPoint;
        private double _anchorTht;
        private double _anchorPhi;
        private bool _isRotatingView = false;
        private bool _isRenderingHooked = false;
        private Point _latestMousePos;

        // ★ 追加: レンダリングキャッシュ用フィールド
        private double _lastRenderedTht = double.NaN;
        private double _lastRenderedPhi = double.NaN;

        // 回転感度（px -> degree）
        //private const double RotateDegPerPixelX = 0.50; // 横移動: θ
        //private const double RotateDegPerPixelY = 0.50; // 縦移動: φ
        private const double RotateDegPerPixelX = 0.35; // より細かい制御
        private const double RotateDegPerPixelY = 0.35;

        // 追加ヘルパー（クラス内に追加）
        private void HookRendering()
        {
            if (_isRenderingHooked) return;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _isRenderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_isRenderingHooked) return;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _isRenderingHooked = false;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_isRotatingView) return;

            var viewModel = (MainWindowViewModel)DataContext;
            var delta = _latestMousePos - _rightDragAnchorPoint;

            double newTht = _anchorTht - delta.X * RotateDegPerPixelX;
            double newPhi = _anchorPhi + delta.Y * RotateDegPerPixelY;

            // φの範囲制限
            newPhi = Math.Clamp(newPhi, -CanvasThreeDView.MaxPhiAngle, CanvasThreeDView.MaxPhiAngle);

            // 角度が変わっていない場合はスキップ（無駄な再描画を防止）
            if (Math.Abs(newTht - _lastRenderedTht) < 0.01 &&
                Math.Abs(newPhi - _lastRenderedPhi) < 0.01)
            {
                return;
            }

            _lastRenderedTht = newTht;
            _lastRenderedPhi = newPhi;

            // 軽量描画モードで更新
            isLightweightDrawing = true;
            try
            {
                viewModel.CanvasThreeDView.Tht = newTht;
                viewModel.CanvasThreeDView.Phi = newPhi;
                UpdateCanvas3D();
            }
            finally
            {
                isLightweightDrawing = false;
            }
        }
        //// φの過回転を軽く制限（必要に応じて調整）
        //newPhi = Math.Max(-89.9, Math.Min(89.9, newPhi));

        //    // 右ドラッグ中は軽量描画
        //    isLightweightDrawing = true;
        //    try
        //    {
        //        // セッター側で再描画が走らない場合も明示更新
        //        viewModel.CanvasThreeDView.Tht = newTht;
        //        viewModel.CanvasThreeDView.Phi = newPhi;
        //        UpdateCanvas3D();
        //    }
        //    finally
        //    {
        //        isLightweightDrawing = false;
        //    }
        //}

        private const double RotationThreshold = 0.5; //1ピクセルごとに回転
        private const double RotationAngle = 5.0; // 1度回転

        private DateTime lastUpdate = DateTime.Now;
        private readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(50); // 更新間隔10ミリ秒

        private bool _rightDragged = false;
        private Point _rightDownPoint;
        //private const double RightClickDragThreshold = 6.0; // px: 右クリックとドラッグの判定閾値
        private const double RightClickDragThreshold = 3.0; // より反応を良くする

        // マウス移動
        private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        {
            var viewModel = (MainWindowViewModel)DataContext;

            // ステータスバーにマウス座標を表示（スナップZ平面上の逆変換）
            var screenPos = e.GetPosition(Canvas3DLayout);
            if (!double.IsNaN(_lastSnappedZ))
            {
                var worldPos = viewModel.CanvasThreeDView.InverseTransformationAtZ(screenPos, _lastSnappedZ);
                viewModel.MouseCoordinateText = $"X={worldPos.X:F3}  Y={worldPos.Y:F3}  Z={worldPos.Z:F3}";
            }
            else
            {
                var worldPos = viewModel.CanvasThreeDView.InverseTransformation(screenPos);
                viewModel.MouseCoordinateText = $"X={worldPos.X:F3}  Y={worldPos.Y:F3}  Z={worldPos.Z:F3}";
            }

            // 基礎梁追加モード: プレビュー線を描画
            if (viewModel.CurrentEditMode == CanvasEditMode.AddElement && viewModel.TempStartNode != null)
            {
                DrawFoundationBeamPreview(e.GetPosition(Canvas3DLayout));
            }

            // Shift+右でのパン中は中ボタンパンと同様の処理
            if (_isPanningWithRight && e.RightButton == MouseButtonState.Pressed)
            {
                Point currentMousePosition = e.GetPosition(Canvas3DLayout);
                Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

                viewModel.CanvasThreeDView.ViewTransition = new Point(
                    viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
                    viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
                );

                previousMousePosition = currentMousePosition;

                UpdateWhileMouseAction();
                return;
            }

            // 既存の右ボタンドラッグ（回転）処理はそのまま（以下省略せず既存処理を維持）
            // 既存実装のまま続行...
            if (e.RightButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(Canvas3DLayout);

                // ドラッグ判定（しきい値超えでドラッグ開始）
                if (!_isRotatingView)
                {
                    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                    {
                        _rightDragged = true;
                        _isRotatingView = true;

                        // ★ 重要: 閾値を超えた位置を新しいアンカーにする
                        _rightDragAnchorPoint = pos;
                        _anchorTht = viewModel.CanvasThreeDView.Tht;
                        _anchorPhi = viewModel.CanvasThreeDView.Phi;

                        // レンダリングフック開始
                        HookRendering();
                        isLightweightDrawing = true;
                    }
                }
                //if (!_isRotatingView)
                //{
                //    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                //    {
                //        _rightDragged = true;
                //        _isRotatingView = true;

                //        // 回転開始時にフック・軽量描画ON
                //        HookRendering();
                //        isLightweightDrawing = true;
                //    }
                //}
                //{
                //    var viewModel = (MainWindowViewModel)DataContext;

                //    // 右ドラッグ中はイベントを合流させる（ここでは位置だけ記録）
                //    if (e.RightButton == MouseButtonState.Pressed)
                //    {
                //        var pos = e.GetPosition(Canvas3DLayout);

                // ドラッグ判定（しきい値超えでドラッグ開始）
                if (!_isRotatingView)
                {
                    if ((_rightDownPoint - pos).Length > RightClickDragThreshold)
                    {
                        _rightDragged = true;
                        _isRotatingView = true;

                        // 回転開始時にフック・軽量描画ON
                        HookRendering();
                        isLightweightDrawing = true;
                    }
                }

                if (_isRotatingView)
                {
                    _latestMousePos = pos; // Renderingで1フレームに1回更新
                    return;               // 重い再描画はここでしない
                }
            }

            // 以降は既存の処理（左ドラッグ・中ドラッグ・スロットリング等）
            if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
            lastUpdate = DateTime.Now;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // ...（既存の矩形選択更新処理）...
                Point currentPoint = e.GetPosition(Canvas3DLayout);
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);

                if (selectionRectangle != null)
                {
                    selectionRectangle.Width = width;
                    selectionRectangle.Height = height;

                    Canvas.SetLeft(selectionRectangle, x);
                    Canvas.SetTop(selectionRectangle, y);

                    if (currentPoint.X >= startPoint.X)
                    {
                        selectionRectangle.Fill = Brushes.LightBlue;
                        selectionRectangle.StrokeDashArray = null;
                        viewModel.IsCrossSelectionMode = false;
                    }
                    else
                    {
                        selectionRectangle.Fill = Brushes.LightGreen;
                        selectionRectangle.StrokeDashArray = [4, 2];
                        viewModel.IsCrossSelectionMode = true;
                    }
                }
            }

            if (IsMouseWheelPressed)
            {
                Point currentMousePosition = e.GetPosition(Canvas3DLayout);
                Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

                viewModel.CanvasThreeDView.ViewTransition = new Point(
                    viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
                    viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
                );

                previousMousePosition = currentMousePosition;

                UpdateWhileMouseAction();
            }

            // ツールチップとホバーハイライトの更新（ボタンが押されていない時のみ）
            if (e.LeftButton == MouseButtonState.Released &&
                e.RightButton == MouseButtonState.Released &&
                !IsMouseWheelPressed)
            {
                Point mousePos = e.GetPosition(Canvas3DLayout);
                // ホバーハイライト + スナップZ更新
                var snappedZ = UpdateHoverHighlight(mousePos);
                if (snappedZ.HasValue)
                    _lastSnappedZ = snappedZ.Value;
                // 沈下マップツールチップ
                UpdateSettlementTooltip(mousePos);
                // 応力図・変位図ツールチップ
                try
                {
                    UpdateBeamResultTooltip(mousePos);
                }
                catch
                {
                    // 例外時は無視（描画に影響しないように）
                }
            }
            else
            {
                ClearHoverHighlight();
                HideSettlementTooltip();
                try { HideBeamResultTooltip(); } catch (Exception ex) { Log.Warning(ex, "HideBeamResultTooltip"); }
            }
        }

        //// 置換: マウス移動
        //private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        //{
        //    var viewModel = (MainWindowViewModel)DataContext;

        //    // 右ドラッグ中はイベントを合流させる（ここでは位置だけ記録）
        //    if (e.RightButton == MouseButtonState.Pressed && _isRotatingView)
        //    {
        //        _latestMousePos = e.GetPosition(Canvas3DLayout);
        //        // 右ドラッグ時はここで重い再描画をしない
        //        return;
        //    }

        //    // 右ドラッグ以外は既存のスロットリングを適用
        //    if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
        //    lastUpdate = DateTime.Now;

        //    if (viewModel.IsElementAddMode)
        //    {
        //        UpdateEditingElement3D(e);
        //    }

        //    // 左ボタン: 矩形選択の更新（既存処理を維持）
        //    if (e.LeftButton == MouseButtonState.Pressed)
        //    {
        //        Point currentPoint = e.GetPosition(Canvas3DLayout);

        //        double x = Math.Min(startPoint.X, currentPoint.X);
        //        double y = Math.Min(startPoint.Y, currentPoint.Y);
        //        double width = Math.Abs(currentPoint.X - startPoint.X);
        //        double height = Math.Abs(currentPoint.Y - startPoint.Y);

        //        if (selectionRectangle != null)
        //        {
        //            selectionRectangle.Width = width;
        //            selectionRectangle.Height = height;

        //            Canvas.SetLeft(selectionRectangle, x);
        //            Canvas.SetTop(selectionRectangle, y);

        //            if (currentPoint.X >= startPoint.X)
        //            {
        //                selectionRectangle.Fill = Brushes.LightBlue;
        //                selectionRectangle.StrokeDashArray = null; // 実線
        //                viewModel.IsCrossSelectionMode = false;
        //            }
        //            else
        //            {
        //                selectionRectangle.Fill = Brushes.LightGreen;
        //                selectionRectangle.StrokeDashArray = new DoubleCollection { 4, 2 }; // 破線
        //                viewModel.IsCrossSelectionMode = true;
        //            }
        //        }
        //    }

        //    // 中ボタン: 平行移動（既存処理を維持）
        //    if (IsMouseWheelPressed)
        //    {
        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        viewModel.CanvasThreeDView.ViewTransition = new Point(
        //            viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
        //            viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
        //        );

        //        previousMousePosition = currentMousePosition;

        //        UpdateWhileMouseAction();
        //    }

        //    // 旧: 右ドラッグでの逐次回転処理は削除（Renderingでまとめて描画）
        //}

        //// マウスが移動した時のメソッド
        //private void Canvas3DLayout_MouseMove(object sender, MouseEventArgs e)
        //{
        //    // 一定の間隔でのみUIを更新
        //    if ((DateTime.Now - lastUpdate) < UpdateInterval) return;
        //    lastUpdate = DateTime.Now;

        //    MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
        //    IsRightButtonClicked = false;

        //    if (viewModel.IsElementAddMode)
        //    {
        //        UpdateEditingElement3D(e); // 編集中要素の更新
        //    }

        //    // 左ボタンが押されている場合の処理
        //    if (e.LeftButton == MouseButtonState.Pressed)
        //    {
        //        Point currentPoint = e.GetPosition(Canvas3DLayout);

        //        double x = Math.Min(startPoint.X, currentPoint.X);
        //        double y = Math.Min(startPoint.Y, currentPoint.Y);
        //        double width = Math.Abs(currentPoint.X - startPoint.X);
        //        double height = Math.Abs(currentPoint.Y - startPoint.Y);

        //        if (selectionRectangle != null)
        //        {
        //            selectionRectangle.Width = width;
        //            selectionRectangle.Height = height;

        //            Canvas.SetLeft(selectionRectangle, x);
        //            Canvas.SetTop(selectionRectangle, y);

        //            // 選択窓の色を動的に変更
        //            if (currentPoint.X >= startPoint.X)
        //            {
        //                selectionRectangle.Fill = Brushes.LightBlue;
        //                selectionRectangle.StrokeDashArray = null; // 実線
        //                viewModel.IsCrossSelectionMode = false;
        //            }
        //            else
        //            {
        //                selectionRectangle.Fill = Brushes.LightGreen;
        //                selectionRectangle.StrokeDashArray = [4, 2]; // 破線
        //                viewModel.IsCrossSelectionMode = true;
        //            }
        //        }
        //    }

        //    // ホイールが押されている場合の処理
        //    if (IsMouseWheelPressed)
        //    {

        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        // 水平方向の移動成分をViewTransition.Xに変換
        //        viewModel.CanvasThreeDView.ViewTransition = new Point(
        //            viewModel.CanvasThreeDView.ViewTransition.X + delta.X,
        //            viewModel.CanvasThreeDView.ViewTransition.Y + delta.Y
        //        );

        //        previousMousePosition = currentMousePosition;

        //        UpdateWhileMouseAction();
        //        // await Task.Run(() => UpdateWhileMouseAction()); // 非同期に実行
        //    }

        //    // 右ボタンが押されている場合の処理
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        Point currentMousePosition = e.GetPosition(Canvas3DLayout);
        //        Vector delta = Point.Subtract(currentMousePosition, previousMousePosition);

        //        bool rotated = false;

        //        // ここで軽量描画フラグを先に立てる
        //        isLightweightDrawing = true;
        //        try
        //        {
        //            // 左右方向の移動成分を左右回転θに変換
        //            if (Math.Abs(delta.X) >= RotationThreshold) // 回転速度の調整係数
        //        {
        //            viewModel.CanvasThreeDView.Tht -= Math.Sign(delta.X) * RotationAngle;
        //            previousMousePosition.X = currentMousePosition.X; // 更新
        //        }

        //        // 上下方向の移動成分を上下回転φに変換
        //        if (Math.Abs(delta.Y) >= RotationThreshold) // 回転速度の調整係数
        //        {
        //            viewModel.CanvasThreeDView.Phi += Math.Sign(delta.Y) * RotationAngle;
        //            previousMousePosition.Y = currentMousePosition.Y; // 更新
        //        }
        //            // setter 内の自動再描画が走るため必須ではないが、
        //            // スロットリングのタイミングで1回明示的に描画しておくと安定
        //            if (rotated)
        //            {
        //                UpdateCanvas3D();
        //            }
        //        }
        //        finally
        //        {
        //            isLightweightDrawing = false; // 元に戻す
        //        }
        //    }
        //}
        //UpdateWhileMouseAction();
        //        //await Task.Run(() => UpdateWhileMouseAction()); // 非同期に実行
        //    }
        //}

        bool IsRightButtonClicked { get; set; } = false;

        //// マウス右ボタンが押された時のメソッド
        //private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;
        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //    }
        //}

        // 追加フィールド（既存の追加フィールド群の近くに挿入）
        private bool _isPanningWithRight = false;

        // 置換: マウス右ボタン押下
        private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                // Shift + 右ボタン => 中ボタンと同様にパン（平行移動）
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    _isPanningWithRight = true;
                    IsMouseWheelPressed = true;
                    IsRightButtonClicked = false; // コンテキストメニュー抑止
                    previousMousePosition = e.GetPosition(Canvas3DLayout);

                    // マウスキャプチャして移動中の描画を許可
                    Canvas3DLayout.CaptureMouse();
                    return;
                }

                // 既存の回転開始処理（Shift無しの右ボタン）
                IsRightButtonClicked = true;

                var viewModel = (MainWindowViewModel)DataContext;

                previousMousePosition = e.GetPosition(Canvas3DLayout);
                _rightDownPoint = previousMousePosition;
                _rightDragAnchorPoint = previousMousePosition;

                _anchorTht = viewModel.CanvasThreeDView.Tht;
                _anchorPhi = viewModel.CanvasThreeDView.Phi;

                _latestMousePos = _rightDragAnchorPoint;
                _isRotatingView = false;        // ここではまだ回転開始しない（移動量が閾値超えたら開始）
                _rightDragged = false;

                Canvas3DLayout.CaptureMouse();
            }
        }
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;

        //        var viewModel = (MainWindowViewModel)DataContext;

        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //        _rightDownPoint = previousMousePosition;
        //        _rightDragAnchorPoint = previousMousePosition;

        //        _anchorTht = viewModel.CanvasThreeDView.Tht;
        //        _anchorPhi = viewModel.CanvasThreeDView.Phi;

        //        _latestMousePos = _rightDragAnchorPoint;
        //        _isRotatingView = false;        // ここではまだ回転開始しない（移動量が閾値超えたら開始）
        //        _rightDragged = false;

        //        Canvas3DLayout.CaptureMouse();
        //    }
        //}

        //// 置換: マウス右ボタン押下
        //private void Canvas3DLayout_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.RightButton == MouseButtonState.Pressed)
        //    {
        //        IsRightButtonClicked = true;

        //        var viewModel = (MainWindowViewModel)DataContext;

        //        previousMousePosition = e.GetPosition(Canvas3DLayout);
        //        _rightDragAnchorPoint = previousMousePosition;

        //        _anchorTht = viewModel.CanvasThreeDView.Tht;
        //        _anchorPhi = viewModel.CanvasThreeDView.Phi;

        //        _latestMousePos = _rightDragAnchorPoint;
        //        _isRotatingView = true;

        //        // 右ドラッグ中は1フレームに1回の更新
        //        HookRendering();

        //        // 軽量描画を有効化（重いラベル等を抑制）
        //        isLightweightDrawing = true;

        //        // マウスキャプチャ
        //        Canvas3DLayout.CaptureMouse();
        //    }
        //}

        //// 置換: マウス右ボタン解放
        //private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        IsRightButtonClicked = false;

        //        _isRotatingView = false;
        //        UnhookRendering();

        //        // 軽量描画を解除して最終状態をフル描画
        //        isLightweightDrawing = false;
        //        UpdateCanvas3D();

        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }
        //}
        // 置換: マウス右ボタン解放（回転の後始末＋ドラッグフラグのクリア）
        private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                if (_isPanningWithRight)
                {
                    // Shift+右でのパンを終了
                    _isPanningWithRight = false;
                    IsMouseWheelPressed = false;

                    // 最終更新
                    UpdateCanvas3D();

                    // 後始末
                    Canvas3DLayout.ReleaseMouseCapture();
                    IsRightButtonClicked = false;
                    _rightDragged = false;
                    return;
                }

                // 既存の回転終了処理
                _isRotatingView = false;
                UnhookRendering();

                isLightweightDrawing = false;
                UpdateCanvas3D();

                IsRightButtonClicked = false;
                _rightDragged = false;

                Canvas3DLayout.ReleaseMouseCapture();

                // レンダリングキャッシュをリセット
                _lastRenderedTht = double.NaN;
                _lastRenderedPhi = double.NaN;
            }
        }
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        _isRotatingView = false;
        //        UnhookRendering();

        //        isLightweightDrawing = false;
        //        UpdateCanvas3D();

        //        IsRightButtonClicked = false;
        //        _rightDragged = false;

        //        Canvas3DLayout.ReleaseMouseCapture();
        //    }
        //}

        // マウス右ボタンが離された時のメソッド
        //private void Canvas3DLayout_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        //{
        //    if (e.ChangedButton == MouseButton.Right)
        //    {
        //        IsRightButtonClicked = false;
        //    }
        //}

        // ズーム後のフル描画デバウンスタイマー
        private System.Windows.Threading.DispatcherTimer? _zoomFullRenderTimer;

        // マウスホイールイベント: 軽量描画 + デバウンスでフル描画
        private void Canvas3DLayout_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta != 0)
            {
                IsMouseWheelPressed = true;
            }

            IsRightButtonClicked = false;

            MainWindowViewModel viewModel = (MainWindowViewModel)DataContext;
            Point mousePosition = e.GetPosition(Canvas3DLayout);

            double scale = viewModel.CanvasThreeDView.Scale;
            double zoomFactor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Math.Max(0.1, Math.Min(scale * zoomFactor, 100));

            Point originalFocalPoint = viewModel.CanvasThreeDView.Transformation(viewModel.CanvasThreeDView.Ct);
            Point newFocalPoint = new(
                (originalFocalPoint.X - mousePosition.X) * zoomFactor + mousePosition.X,
                (originalFocalPoint.Y - mousePosition.Y) * zoomFactor + mousePosition.Y);

            Point originalViewPosition = viewModel.CanvasThreeDView.ViewTransition;
            Point newViewPosition = new(
                originalViewPosition.X + (newFocalPoint.X - originalFocalPoint.X),
                originalViewPosition.Y + (newFocalPoint.Y - originalFocalPoint.Y));

            viewModel.CanvasThreeDView.Scale = newScale;
            viewModel.CanvasThreeDView.ViewTransition = newViewPosition;

            IsMouseWheelPressed = false;

            // 軽量描画（ラベル・地盤・解析結果を省略）
            UpdateWhileMouseAction();
            viewModel.RaisePropertyChanged(nameof(viewModel.ZoomText));

            // 200ms後にフル描画を1回実行（連続ホイール操作ではリセット）
            if (_zoomFullRenderTimer == null)
            {
                _zoomFullRenderTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _zoomFullRenderTimer.Tick += (s, _) =>
                {
                    _zoomFullRenderTimer.Stop();
                    UpdateCanvas3D();
                };
            }
            _zoomFullRenderTimer.Stop();
            _zoomFullRenderTimer.Start();
        }

        // 置換: プレビューMouseUpでのメニュー表示判定
        private void Canvas3DLayout_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Released)
            {
                IsMouseWheelPressed = false;
            }

            // 右クリックメニューは「ドラッグしていない場合のみ」表示
            if (IsRightButtonClicked && !_rightDragged)
            {
                // 選択状態を判定
                bool hasSelectedNodes = false;
                bool hasSelectedBeams = false;

                if (DataContext is MainWindowViewModel vm)
                {
                    hasSelectedNodes = vm.CurrentInputModel?.PileLayoutItems?
                        .Any(p => p.IsSelected) ?? false;
                    if (!hasSelectedNodes)
                        hasSelectedNodes = vm.CurrentInputModel?.InputNodes?
                            .Any(n => n.IsSelected) ?? false;

                    hasSelectedBeams = vm.CurrentInputModel?.FoundationBeamInput?.Beams?
                        .Any(b => b.IsSelected) ?? false;
                }

                ContextMenu contextMenu;

                if (hasSelectedNodes && hasSelectedBeams)
                {
                    // 両方選択されている場合: 統合メニューを動的に構築
                    contextMenu = new ContextMenu();

                    // 杭節点メニュー項目
                    if (FindResource("NodeContextMenu") is ContextMenu nodeMenu)
                    {
                        foreach (var item in nodeMenu.Items)
                        {
                            if (item is MenuItem mi)
                            {
                                contextMenu.Items.Add(CloneMenuItemForContextMerge(mi));
                            }
                            else if (item is Separator)
                            {
                                contextMenu.Items.Add(new Separator());
                            }
                        }
                    }

                    contextMenu.Items.Add(new Separator());

                    // 梁要素メニュー項目（画像コピー/保存は杭側に含まれるので省略）
                    if (FindResource("BeamElementContextMenu") is ContextMenu beamMenu)
                    {
                        foreach (var item in beamMenu.Items)
                        {
                            if (item is MenuItem mi)
                            {
                                // 画像コピー/保存は重複するのでスキップ
                                var header = mi.Header?.ToString() ?? "";
                                if (header.Contains("画像")) continue;

                                contextMenu.Items.Add(CloneMenuItemForContextMerge(mi));
                            }
                        }
                    }
                }
                else if (hasSelectedBeams)
                {
                    contextMenu = FindResource("BeamElementContextMenu") as ContextMenu;
                }
                else
                {
                    contextMenu = FindResource("NodeContextMenu") as ContextMenu;
                }

                if (contextMenu != null)
                {
                    contextMenu.PlacementTarget = sender as UIElement;
                    contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                    contextMenu.IsOpen = true;
                }

                startPoint = e.GetPosition(Canvas3DLayout);
            }
            else
            {
                // 右クリック以外、または右ドラッグの場合は非表示
                Canvas3DLayout.ContextMenu = null;
                UpdateCanvas3D();
            }

            // 後始末
            IsRightButtonClicked = false;
            IsMouseWheelPressed = false;
            _rightDragged = false;
        }

        //// マウスホイールドラッグ完了時のメソッド マウスホイールが離された時の処理
        //private void Canvas3DLayout_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        //{
        /// <summary>
        /// ContextMenu を動的にマージ構築する際に MenuItem を「正しく」複製する。
        ///
        /// 注意: 元の MenuItem の Command プロパティは XAML で {Binding ...} 経由で指定されている場合、
        /// 元 ContextMenu が一度も Open されないと binding が評価されず mi.Command は null になる。
        /// したがって `new MenuItem { Command = mi.Command }` だと null コマンドの MenuItem が生成され、
        /// クリックしても無反応になる。
        ///
        /// 本ヘルパでは BindingOperations.GetBinding で元の Binding を取得して新 MenuItem に再設定し、
        /// PlacementTarget の DataContext から binding が評価されるようにする。
        /// </summary>
        private static MenuItem CloneMenuItemForContextMerge(MenuItem source)
        {
            var newItem = new MenuItem { Header = source.Header };

            // Command (Binding を保持)
            var cmdBinding = System.Windows.Data.BindingOperations.GetBinding(source, MenuItem.CommandProperty);
            if (cmdBinding != null) newItem.SetBinding(MenuItem.CommandProperty, cmdBinding);
            else if (source.Command != null) newItem.Command = source.Command;

            // CommandParameter (Binding を保持)
            var paramBinding = System.Windows.Data.BindingOperations.GetBinding(source, MenuItem.CommandParameterProperty);
            if (paramBinding != null) newItem.SetBinding(MenuItem.CommandParameterProperty, paramBinding);
            else if (source.CommandParameter != null) newItem.CommandParameter = source.CommandParameter;

            return newItem;
        }

        //    if (e.MiddleButton == MouseButtonState.Released)
        //    {
        //        IsMouseWheelPressed = false;
        //    }

        //    if (IsRightButtonClicked == true)
        //    {
        //        // マウス位置で ContextMenu を表示
        //        if (FindResource("NodeContextMenu") is ContextMenu contextMenu)
        //        {
        //            contextMenu.PlacementTarget = sender as UIElement;
        //            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        //            contextMenu.IsOpen = true;
        //        }
        //        else
        //        {
        //            // デバッグログ
        //            Console.WriteLine("ContextMenu is null");
        //        }
        //        startPoint = e.GetPosition(Canvas3DLayout);
        //        IsRightButtonClicked = false;
        //        IsMouseWheelPressed = false;
        //    }
        //    else
        //    {
        //        // 右クリック以外の場合は ContextMenu を非表示にする
        //        Canvas3DLayout.ContextMenu = null;
        //        UpdateCanvas3D();
        //    }
        //}

        //private void Canvas3DLayout_LostMouseCapture(object sender, MouseEventArgs e)
        //{
        //    IsMouseWheelPressed = false;
        //    IsRightButtonClicked = false;
        //}
    }
}
