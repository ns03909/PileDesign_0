using AvalonDock.Layout;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Media;

namespace PileDesign.Views
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    /// 
    public partial class MainWindow : RibbonWindow
    {
        // ===== 静的フローズンブラシキャッシュ（描画ループ内での毎フレーム生成を回避） =====
        private static readonly SolidColorBrush BrushMinimapStroke = Freeze(new SolidColorBrush(Color.FromArgb(160, 100, 100, 100)));
        private static readonly SolidColorBrush BrushMinimapFill = Freeze(new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)));
        private static readonly SolidColorBrush BrushCubeFill = Freeze(new SolidColorBrush(Color.FromArgb(55, 100, 140, 200)));
        private static readonly SolidColorBrush BrushCubeStroke = Freeze(new SolidColorBrush(Color.FromArgb(160, 80, 120, 170)));
        private static readonly SolidColorBrush BrushCubeArrow = Freeze(new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)));
        private static readonly SolidColorBrush BrushCubeHighlightBlue = Freeze(new SolidColorBrush(Color.FromArgb(60, 98, 176, 226)));
        private static readonly SolidColorBrush BrushCubeHighlightGreen = Freeze(new SolidColorBrush(Color.FromArgb(90, 144, 238, 144)));
        private static readonly SolidColorBrush BrushSemiTransparentGray = Freeze(new SolidColorBrush(Color.FromArgb(160, 100, 100, 100)));
        private static readonly SolidColorBrush BrushDarkBackground = Freeze(new SolidColorBrush(Color.FromArgb(230, 50, 50, 50)));
        private static readonly SolidColorBrush BrushErrorFill = Freeze(new SolidColorBrush(Color.FromArgb(200, 255, 100, 100)));

        private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

        // 追加: ビュー操作中フラグ
        private bool _isViewInteracting = false;


        // 追加: 操作開始/終了ヘルパ
        private void BeginViewInteraction()
        {
            _isViewInteracting = true;
            isLightweightDrawing = true; // 操作中は軽量描画
            // 既存のラベル画像を即時消す
            if (_textLayerImage != null) _textLayerImage.Source = null;
            TextBlockInfos.Clear();
            // EdgeMode.Aliased: 操作中はアンチエイリアスを切ってラスタライズ高速化
            // (回転/ズーム時の描画 fps 向上、操作終了で Unspecified に戻して鮮明化)
            if (Canvas3DLayout != null)
                System.Windows.Media.RenderOptions.SetEdgeMode(Canvas3DLayout, System.Windows.Media.EdgeMode.Aliased);
        }

        private void EndViewInteraction()
        {
            _isViewInteracting = false;
            isLightweightDrawing = false;
            // 通常モードに戻して滑らかに描画
            if (Canvas3DLayout != null)
                System.Windows.Media.RenderOptions.SetEdgeMode(Canvas3DLayout, System.Windows.Media.EdgeMode.Unspecified);
            RequestUpdateCanvas3D(); // 最終状態をフル描画
        }

        private int _renderBatchDepth = 0;
        private bool _renderPending = false;
        private bool _isRendering = false;
        private System.Windows.Threading.DispatcherTimer? _renderTimer;

        private bool isLightweightDrawing = false;

        private void ClearCanvasDrawingLayerPathsOnly()
        {
            if (Canvas3DLayout == null) return;
            for (int i = Canvas3DLayout.Children.Count - 1; i >= 0; i--)
            {
                if (Canvas3DLayout.Children[i] is System.Windows.Shapes.Path)
                {
                    Canvas3DLayout.Children.RemoveAt(i);
                }
            }
        }

        // レンダリングの遅延要求（16msデバウンス）
        private void RequestUpdateCanvas3D()
        {
            if (_renderBatchDepth > 0)
            {
                _renderPending = true;
                return;
            }
            if (_renderTimer == null)
            {
                _renderTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _renderTimer.Tick += OnRenderTimerTick;
            }
            _renderTimer.Stop();
            _renderTimer.Start();
        }

        private void OnRenderTimerTick(object? sender, EventArgs e)
        {
            _renderTimer?.Stop();
            // 直近の大量更新を避けるため、ここで1回だけ実描画
            UpdateCanvas3D();
        }

        // バッチ制御（using で呼ぶ）
        private void BeginRenderBatch() => _renderBatchDepth++;
        private void EndRenderBatch()
        {
            if (_renderBatchDepth > 0) _renderBatchDepth--;
            if (_renderBatchDepth == 0 && _renderPending)
            {
                _renderPending = false;
                // バッチ終了時に1回だけ描画
                UpdateCanvas3D();
            }
        }

        // using(var _ = DeferRender()) { ... } で呼ぶためのスコープ
        private sealed class RenderScope : IDisposable
        {
            private readonly MainWindow _owner;
            private readonly bool _restoreLightweight;
            public RenderScope(MainWindow owner, bool forceLightweight)
            {
                _owner = owner;
                _owner.BeginRenderBatch();
                if (forceLightweight)
                {
                    _restoreLightweight = _owner.isLightweightDrawing == false;
                    _owner.isLightweightDrawing = true; // 大量更新中は軽量描画
                }
            }
            public void Dispose()
            {
                // 軽量描画を元に戻す
                if (_restoreLightweight) _owner.isLightweightDrawing = false;
                _owner.EndRenderBatch();
            }
        }

        // 公開ヘルパ（軽量描画ONでの一括更新）
        private IDisposable DeferRender(bool forceLightweight = true) => new RenderScope(this, forceLightweight);


        // MainWindow 内に追加（コマンドやメニューから呼び出し）
        private void DeleteSelectedPiles_Batched()
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var targets = vm.CurrentInputModel.PileLayoutItems
                .Where(p => p.IsVisible && p.IsSelected).ToList();
            if (targets.Count == 0) return;

            using (DeferRender(forceLightweight: true))
            {
                // DataGrid の選択同期や描画は抑止されたまま
                foreach (var p in targets)
                    vm.CurrentInputModel.PileLayoutItems.Remove(p);

                // 付随データのクリーンアップ等があればここでまとめて実施
                vm.CanvasGeometry.Clear();
                TextBlockInfos.Clear();
            }
        }

        public void UpdateCanvas3D()
        {
            // すでに描画中なら、終了後に1回だけ描画するようフラグを立てて戻る
            if (_isRendering)
            {
                _renderPending = true;
                return;
            }
            _isRendering = true;
            try
            {
                // バッチ中は保留…
                if (_renderBatchDepth > 0) { _renderPending = true; return; }

                ClearCanvasDrawingLayerPathsOnly();

                // 操作中/軽量描画中はテキストを消しておく（前フレームの残骸防止）
                if (_isViewInteracting || isLightweightDrawing)
                {
                    if (_textLayerImage != null) _textLayerImage.Source = null;
                    TextBlockInfos.Clear();
                }

                if (DataContext is not MainWindowViewModel viewModel) return;

                viewModel.CanvasGeometry.Clear();
                TextBlockInfos.Clear();

                if (isLightweightDrawing)
                {
                    // 軽量描画（ラベルなし）
                    UpdateNodes3D();
                    UpdateInputNodes3D(); // 一般節点も描画
                    UpdateConnectingNodes3D(); // 接続用節点・剛体連結線
                    if (viewModel.IsFoundationBeamVisible) UpdateFoundationBeams3D(); // 基礎梁・一般梁要素も描画
                    UpdateSelectedNodesAndElements3D(); // 選択要素も描画
                    if (viewModel.IsXYZAxesVisible) UpdateAxes3D();
                    UpdateCanvasCube();
                    viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);
                    return;
                }

                ColorBarCanvas?.Children.Clear();

                UpdateNodes3D(); // 節点描画の更新

                UpdateCanvasCube(); // XYZキューブの更新

                UpdateSelectedNodesAndElements3D(); // 選択節点描画の更新

                if (viewModel.IsEmbedmentBoxVisible)
                {
                    UpdateEmbedment3D(); // 根入部描画の更新
                }
                else
                {
                    // 根入部非表示: 他メソッド(CreateBoxGeometry等)が書き込んだジオメトリをクリア
                    viewModel.CanvasGeometry.PathGeoEmbedmenSides.Figures.Clear();
                    viewModel.CanvasGeometry.PathGeoDividedEmbedmenSides.Figures.Clear();
                    viewModel.CanvasGeometry.PathGeoEmbedmentDiagonals.Figures.Clear();
                    viewModel.CanvasGeometry.PathGeoDividedEmbedmentDiagonals.Figures.Clear();
                    viewModel.CanvasGeometry.PathGeoEmbedmentNodes.Figures.Clear();
                    viewModel.CanvasGeometry.PathGeoEmbedmentRigidConnections.Figures.Clear();
                }

                if (viewModel.IsXYZAxesVisible) UpdateAxes3D(); // XYZ軸の更新

                if (viewModel.IsGroundVisible) UpdateGround3D(); // 杭周地盤描画の更新

                if (viewModel.IsNValueVisible) UpdateGroundMassValue3D("NValue", 10, 60); // N値描画の更新

                if (viewModel.IsVS0Visible) UpdateGroundMassValue3D("VS0", 100, 400); // VS0描画の更新

                if (viewModel.IsFcVisible) UpdateGroundMassValue3D("Fc", 20, 100); // Fc描画の更新

                if (viewModel.IsDensityVisible) UpdateGroundLayerValue3D("density", 5, 25); // 密度描画の更新

                if (viewModel.IsCohesiveVisible) UpdateGroundLayerValue3D("cohesive", 50, 200); // 粘着力描画の更新

                if (viewModel.IsVsVisible) UpdateGroundLayerValue3D("Vs", 100, 500); // Vs描画の更新

                if (viewModel.IsEsVisible) UpdateGroundLayerValue3D("Es", 10000, 50000); // Es描画の更新

                if (viewModel.IsSettlementLoadVisible) UpdateSettlementLoad3D(); // 荷重面描画の更新

                UpdateConnectingNodes3D(); // 接続用節点・剛体連結線描画の更新

                if (viewModel.IsFoundationBeamVisible) UpdateFoundationBeams3D(); // 基礎梁・一般梁要素描画の更新

                UpdateInputNodes3D(); // 一般節点描画の更新

                if ((MainWindowViewModel)DataContext == null) return;

                // 平面図の場合
                if (Math.Abs(viewModel.CanvasThreeDView.Phi - 90.0) < 0.5 && !viewModel.CanvasThreeDView.IsPerspective)
                {
                    if (viewModel.IsTickMarkVisible) UpdateTickMarks3DPlan();  // 目盛りの更新
                    if (viewModel.IsGridLineVisible) UpdateGridLines3DPlan(); // 通り心の更新

                    if (Math.Abs(viewModel.CanvasThreeDView.Tht + 90) < 0.5) // XY（平面）の場合
                    {
                        if (viewModel.IsGridLineVisible) UpdateDimensionLines3DPlan();
                    }
                }

                // 側面図の場合
                else if (Math.Abs(viewModel.CanvasThreeDView.Phi) < 0.5 && viewModel.CanvasThreeDView.IsPerspective == false)
                {

                    if (viewModel.IsSettlementGroundVisible) UpdateSettlementGround3D(); // 側面図用沈下描画の更新
                    UpdateTickMarks3DElevation(); // 目盛りの更新

                    if (Math.Abs(viewModel.CanvasThreeDView.Tht) < 0.5) // YZ（右側面）の場合
                    {
                        if (viewModel.IsTickMarkVisible) UpdateTickMarks3DYofYZ();
                        if (viewModel.IsGridLineVisible) UpdateGridLinesAndDimensionsYforYZ(); // 通り心の更新
                    }

                    if (Math.Abs(viewModel.CanvasThreeDView.Tht + 90) < 0.5) // XZ（正面）の場合
                    {
                        if (viewModel.IsTickMarkVisible) UpdateTickMarks3DXofXZ();
                        if (viewModel.IsGridLineVisible) UpdateGridLinesAndDimensionsXforXZ(); // 通り心の更新
                    }
                }

                else
                {
                    if (viewModel.IsGridLineVisible) UpdateGridLines3D(); // 通り心の更新
                }

                // 杭節点変位と地盤変位の共通スケールを事前計算（両者を同じスケールで描画するため）
                PrepareSharedDisplacementScale();

                if (viewModel.IsForcedDisplacementVisible) UpdateForcedDisplacement3D(); // 3D地盤変位更新メソッド

                // 剛床の描画
                if (viewModel.IsRigidFloorVisible) UpdateRigidFloor3D();

                // 群杭沈下グリッドの描画
                if (viewModel.IsGroupPileGridVisible) UpdateGroupPileGrid3D();

                // 全てのパスを描画（MainCanvasGeometryのPathGeometryをCanvasに追加）
                viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);

                // === 以下はDrawAllPathsの後に実行（ColorBaredGeometryのPathが削除されないようにする） ===

                // 変形後沈下グリッドの描画（コンター図）
                if (viewModel.IsGroupPileGridDeformationVisible) UpdateSettlementGridDeformation();

                // 軸力・慣性力の描画
                if (viewModel.IsMassLoadingVisible || viewModel.IsAxialLoadingVisible) UpdateLoading3D();

                // 解析結果の描画
                if (viewModel.IsAnalysisResultVisible) UpdateAnalysisResult3D();

                // 変形後形状の描画（解析結果表示OFFでも独立して動作）
                if (viewModel.IsDeformedElementVisible && !viewModel.IsAnalysisResultVisible)
                    UpdateDeformedElementsStandalone();

                // 追加: 沈下系のバブル/矢印は最後に描いて最前面に
                string effContent = viewModel.EffectiveSettlementContent;
                if (_pendingSettlementPoints != null &&
                    _pendingSettlementValues != null &&
                    (effContent == "沈下" ||
                     effContent == "基礎梁考慮沈下" ||
                     effContent == "基礎梁考慮+群杭沈下" ||
                     effContent == "基礎梁考慮反力（地盤）" ||
                     effContent == "基礎梁考慮反力（杭頭集約）" ||
                     effContent == "単杭反力（地盤）" ||
                     effContent == "単杭反力（杭頭集約）"))
                {
                    DrawBubbleAndArrow(
                        _pendingSettlementPoints,
                        _pendingSettlementValues,
                        _pendingSettlementTitle ?? "沈下",
                        _pendingSettlementUnit ?? "mm");

                    _pendingSettlementPoints = null;
                    _pendingSettlementValues = null;
                    _pendingSettlementTitle = null;
                    _pendingSettlementUnit = null;
                }
                // 最後のテキストレンダリングは、操作中はスキップ
                if (!_isViewInteracting)
                {
                    RenderTextBlocksWithDrawingVisual();
                }

                // ミニマップの更新（AvalonDockパネルが表示中の場合のみ）
                if (minimapAnchorable?.IsVisible == true) UpdateMinimap();
            }
            finally
            {
                _isRendering = false;
                // 途中で保留されたリクエストがあれば1回だけ再描画を予約
                if (_renderPending && _renderBatchDepth == 0)
                {
                    _renderPending = false;
                    RequestUpdateCanvas3D();
                }
            }
        }

        /// <summary>
        /// ミニマップを更新：全モデル点を縮小描画し、現在のビューポート範囲を矩形で表示
        /// </summary>
        private void UpdateMinimap()
        {
            if (MinimapCanvas == null) return;
            if (DataContext is not MainWindowViewModel viewModel) return;

            MinimapCanvas.Children.Clear();

            double mmW = MinimapCanvas.ActualWidth;
            double mmH = MinimapCanvas.ActualHeight;
            if (mmW < 10 || mmH < 10) return; // サイズ未確定時はスキップ
            const double margin = 6;

            // 全モデル点を2D変換してバウンディングボックスを計算
            var allPoints = new System.Collections.Generic.List<Point>();

            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                allPoints.Add(viewModel.CanvasThreeDView.Transformation(pile.Point3D));
            }

            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    allPoints.Add(viewModel.CanvasThreeDView.Transformation(
                        new System.Windows.Media.Media3D.Point3D(node.X, node.Y, node.Z)));
                }
            }

            if (viewModel.CurrentInputModel.FoundationBeamInput?.Nodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.FoundationBeamInput.Nodes)
                {
                    allPoints.Add(viewModel.CanvasThreeDView.Transformation(
                        new System.Windows.Media.Media3D.Point3D(node.X, node.Y, node.Z)));
                }
            }

            if (allPoints.Count < 1) return;

            // バウンディングボックス
            double minX = allPoints.Min(p => p.X);
            double maxX = allPoints.Max(p => p.X);
            double minY = allPoints.Min(p => p.Y);
            double maxY = allPoints.Max(p => p.Y);

            // ビューポート範囲も含めてバウンディングボックスを拡張
            minX = Math.Min(minX, 0);
            maxX = Math.Max(maxX, Canvas3DWidth);
            minY = Math.Min(minY, 0);
            maxY = Math.Max(maxY, Canvas3DHeight);

            double rangeX = maxX - minX;
            double rangeY = maxY - minY;
            if (rangeX < 1) rangeX = 1;
            if (rangeY < 1) rangeY = 1;

            // ミニマップへのマッピング関数
            double scaleX = (mmW - margin * 2) / rangeX;
            double scaleY = (mmH - margin * 2) / rangeY;
            double mmScale = Math.Min(scaleX, scaleY);

            double offsetX = margin + ((mmW - margin * 2) - rangeX * mmScale) * 0.5;
            double offsetY = margin + ((mmH - margin * 2) - rangeY * mmScale) * 0.5;

            Point ToMinimap(Point canvasPt)
            {
                return new Point(
                    (canvasPt.X - minX) * mmScale + offsetX,
                    (canvasPt.Y - minY) * mmScale + offsetY);
            }

            // ビューポート矩形を描画
            var vpTopLeft = ToMinimap(new Point(0, 0));
            var vpBottomRight = ToMinimap(new Point(Canvas3DWidth, Canvas3DHeight));
            var vpRect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, vpBottomRight.X - vpTopLeft.X),
                Height = Math.Max(1, vpBottomRight.Y - vpTopLeft.Y),
                Stroke = BrushMinimapStroke,
                StrokeThickness = 1,
                Fill = BrushMinimapFill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(vpRect, vpTopLeft.X);
            Canvas.SetTop(vpRect, vpTopLeft.Y);
            MinimapCanvas.Children.Add(vpRect);

            // 要素（Elements）を線として描画
            foreach (var element in viewModel.CurrentInputModel.Elements)
            {
                var p0 = ToMinimap(viewModel.CanvasThreeDView.Transformation(element.Nodes[0].Point3D));
                var p1 = ToMinimap(viewModel.CanvasThreeDView.Transformation(element.Nodes[1].Point3D));
                var line = new System.Windows.Shapes.Line
                {
                    X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                    Stroke = element.IsSelected ? Brushes.Red : Brushes.Goldenrod,
                    StrokeThickness = element.IsSelected ? 1.0 : 0.5,
                    IsHitTestVisible = false
                };
                MinimapCanvas.Children.Add(line);
            }

            // 基礎梁要素を線として描画（Guidベース座標解決）
            if (viewModel.IsFoundationBeamVisible && viewModel.CurrentInputModel.FoundationBeamInput?.Beams != null)
            {
                var fbInput = viewModel.CurrentInputModel.FoundationBeamInput;
                var nodeDict = fbInput.Nodes.ToDictionary(n => n.No, n => n);
                foreach (var beam in fbInput.Beams)
                {
                    if (!beam.IsVisible) continue;

                    System.Windows.Media.Media3D.Point3D? loc0 = null, loc1 = null;
                    if (beam.NodeI_Id != Guid.Empty)
                    {
                        var c = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeI_Type, beam.NodeI_Id);
                        if (c.HasValue) loc0 = new(c.Value.X, c.Value.Y, c.Value.Z);
                    }
                    else if (beam.NodeI_No > 0 && nodeDict.TryGetValue(beam.NodeI_No, out var nI))
                        loc0 = new(nI.X, nI.Y, nI.Z);

                    if (beam.NodeJ_Id != Guid.Empty)
                    {
                        var c = viewModel.CurrentInputModel.GetNodeCoordinates(beam.NodeJ_Type, beam.NodeJ_Id);
                        if (c.HasValue) loc1 = new(c.Value.X, c.Value.Y, c.Value.Z);
                    }
                    else if (beam.NodeJ_No > 0 && nodeDict.TryGetValue(beam.NodeJ_No, out var nJ))
                        loc1 = new(nJ.X, nJ.Y, nJ.Z);

                    if (!loc0.HasValue || !loc1.HasValue) continue;

                    var p0 = ToMinimap(viewModel.CanvasThreeDView.Transformation(loc0.Value));
                    var p1 = ToMinimap(viewModel.CanvasThreeDView.Transformation(loc1.Value));
                    MinimapCanvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                        Stroke = beam.IsSelected ? Brushes.Red : Brushes.DarkOrange,
                        StrokeThickness = beam.IsSelected ? 1.0 : 0.5,
                        IsHitTestVisible = false
                    });
                }
            }

            // 杭節点をドットとして描画
            foreach (var pile in viewModel.CurrentInputModel.PileLayoutItems)
            {
                var mp = ToMinimap(viewModel.CanvasThreeDView.Transformation(pile.Point3D));
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 3, Height = 3,
                    Fill = pile.IsSelected ? Brushes.Red : Brushes.DimGray,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, mp.X - 1.5);
                Canvas.SetTop(dot, mp.Y - 1.5);
                MinimapCanvas.Children.Add(dot);
            }

            // 一般節点をドットとして描画
            if (viewModel.CurrentInputModel.InputNodes != null)
            {
                foreach (var node in viewModel.CurrentInputModel.InputNodes)
                {
                    if (!node.IsVisible) continue;
                    var mp = ToMinimap(viewModel.CanvasThreeDView.Transformation(
                        new System.Windows.Media.Media3D.Point3D(node.X, node.Y, node.Z)));
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 3, Height = 3,
                        Fill = node.IsSelected ? Brushes.Red : Brushes.SteelBlue,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, mp.X - 1.5);
                    Canvas.SetTop(dot, mp.Y - 1.5);
                    MinimapCanvas.Children.Add(dot);
                }
            }

            // ミニマップのスケール情報を保存（クリックナビゲーション用）
            _minimapMinX = minX;
            _minimapMinY = minY;
            _minimapScale = mmScale;
            _minimapOffsetX = offsetX;
            _minimapOffsetY = offsetY;
        }

        // ミニマップのマッピング用パラメータ
        private double _minimapMinX, _minimapMinY, _minimapScale, _minimapOffsetX, _minimapOffsetY;
        private Point? _minimapDragLastPos;
        private const double MinimapDragSensitivity = 0.4; // ドラッグ時の減衰係数（1.0で viewport 追従、< 1.0 で緩やかに）

        /// <summary>
        /// ミニマップクリックでビュー中心を移動（MouseDown / ドラッグ中の MouseMove から共用）
        /// </summary>
        private void MoveViewToMinimapPoint(Point clickPos)
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (_minimapScale <= 0) return;

            // ミニマップ座標→メインキャンバス座標に逆変換
            double canvasX = (clickPos.X - _minimapOffsetX) / _minimapScale + _minimapMinX;
            double canvasY = (clickPos.Y - _minimapOffsetY) / _minimapScale + _minimapMinY;

            // ビューの中心をクリック位置に移動（ViewTransitionを調整）
            double centerX = Canvas3DWidth * 0.5;
            double centerY = Canvas3DHeight * 0.5;

            viewModel.CanvasThreeDView.ViewTransition = new Point(
                viewModel.CanvasThreeDView.ViewTransition.X + (centerX - canvasX),
                viewModel.CanvasThreeDView.ViewTransition.Y + (centerY - canvasY));

            UpdateCanvas3D();
        }

        /// <summary>
        /// ミニマップクリックでビューを移動（ドラッグ開始点）。
        /// </summary>
        private void MinimapCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(MinimapCanvas);
            MoveViewToMinimapPoint(pos);
            _minimapDragLastPos = pos;
            MinimapCanvas.CaptureMouse();
            e.Handled = true;
        }

        /// <summary>
        /// ミニマップ上を左ボタン押下のままドラッグするとビューを追従移動（減衰付き相対パン）。
        /// </summary>
        private void MinimapCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            if (!MinimapCanvas.IsMouseCaptured) return;
            if (_minimapDragLastPos is not Point last) return;
            if (DataContext is not MainWindowViewModel viewModel) return;
            if (_minimapScale <= 0) return;

            var cur = e.GetPosition(MinimapCanvas);
            double dx = cur.X - last.X;
            double dy = cur.Y - last.Y;

            // ミニマップ上の移動量をキャンバス座標系に変換し、減衰を掛ける
            double shiftX = -(dx / _minimapScale) * MinimapDragSensitivity;
            double shiftY = -(dy / _minimapScale) * MinimapDragSensitivity;

            viewModel.CanvasThreeDView.ViewTransition = new Point(
                viewModel.CanvasThreeDView.ViewTransition.X + shiftX,
                viewModel.CanvasThreeDView.ViewTransition.Y + shiftY);

            _minimapDragLastPos = cur;
            UpdateCanvas3D();
        }

        private void MinimapCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (MinimapCanvas.IsMouseCaptured) MinimapCanvas.ReleaseMouseCapture();
            _minimapDragLastPos = null;
        }

        /// <summary>
        /// ミニマップのサイズ変更時にDockHeightを幅の0.8倍に維持して再描画
        /// </summary>
        private void MinimapCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged && minimapAnchorable?.Parent is LayoutAnchorablePane pane)
            {
                double targetHeight = e.NewSize.Width * 0.8;
                if (targetHeight > 10)
                    pane.DockHeight = new GridLength(targetHeight);
            }

            if (minimapAnchorable?.IsVisible == true)
                UpdateMinimap();
        }

        private GridLength _savedLeftTabsDockWidth = new GridLength(1, GridUnitType.Star);

        /// <summary>
        /// データタブ（一般節点～群杭荷重）の表示/非表示トグル
        /// </summary>
        private void ToggleLeftTabs_Click(object sender, RoutedEventArgs e)
        {
            var rootPanel = dockingManager.Layout.RootPanel;
            if (rootPanel.Children.Contains(leftTabsGroup))
            {
                _savedLeftTabsDockWidth = leftTabsGroup.DockWidth;
                rootPanel.Children.Remove(leftTabsGroup);
            }
            else
            {
                leftTabsGroup.DockWidth = _savedLeftTabsDockWidth;
                // LayoutAnchorablePane(入力データ)の直後に挿入
                var insertIndex = Math.Min(1, rootPanel.Children.Count);
                rootPanel.Children.Insert(insertIndex, leftTabsGroup);
            }
        }

        /// <summary>
        /// 入力データパネルの表示/非表示トグル
        /// </summary>
        private void ToggleInputDataPanel_Click(object sender, RoutedEventArgs e)
        {
            if (inputDataAnchorable.IsVisible)
                inputDataAnchorable.Hide();
            else
            {
                inputDataAnchorable.Show();
                inputDataAnchorable.IsActive = true;
            }
        }

        /// <summary>
        /// プロパティパネルの表示/非表示トグル
        /// </summary>
        private void TogglePropertiesPanel_Click(object sender, RoutedEventArgs e)
        {
            if (propertiesAnchorable.IsVisible)
                propertiesAnchorable.Hide();
            else
            {
                propertiesAnchorable.Show();
                propertiesAnchorable.IsActive = true;
            }
        }

        /// <summary>
        /// ミニマップパネルの表示/非表示トグル
        /// </summary>
        private void ToggleMinimapPanel_Click(object sender, RoutedEventArgs e)
        {
            if (minimapAnchorable.IsVisible)
                minimapAnchorable.Hide();
            else
            {
                minimapAnchorable.Show();
                minimapAnchorable.IsActive = true;
                UpdateMinimap();
            }
        }

        /// <summary>
        /// パネルのIsVisibleChanged購読を初期化（パネルXボタンとトグルボタンの同期用）
        /// </summary>
        internal void InitializePanelToggleSync()
        {
            inputDataAnchorable.IsVisibleChanged += (s, e) =>
                InputDataPanelToggle.IsChecked = inputDataAnchorable.IsVisible;
            propertiesAnchorable.IsVisibleChanged += (s, e) =>
                PropertiesPanelToggle.IsChecked = propertiesAnchorable.IsVisible;
            minimapAnchorable.IsVisibleChanged += (s, e) =>
                MinimapPanelToggle.IsChecked = minimapAnchorable.IsVisible;
        }
    }
}