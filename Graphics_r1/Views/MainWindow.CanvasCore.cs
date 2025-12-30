using MathNet.Numerics.LinearAlgebra;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Element = PileDesign.Models.InputData.Element;
using Node = PileDesign.FEM.Node;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using TransformGroup = System.Windows.Media.TransformGroup;

namespace PileDesign.Views
{
    /// <summary>
    /// 根入れ部のコードビハインド
    /// </summary>
    /// 




    public partial class MainWindow : Window
    {
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
        }

        private void EndViewInteraction()
        {
            _isViewInteracting = false;
            isLightweightDrawing = false;
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
                    if (viewModel.IsXYZAxesVisible) UpdateAxes3D();
                    UpdateCanvasCube();
                    viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);
                    return;
                }

                ColorBarCanvas?.Children.Clear();

                UpdateNodes3D(); // 節点描画の更新

                UpdateCanvasCube(); // XYZキューブの更新

                UpdateSelectedNodesAndElements3D(); // 選択節点描画の更新

                if (viewModel.IsEmbedmentBoxVisible) UpdateEmbedment3D(); // 根入部描画の更新

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

                if (viewModel.IsElementVisible) UpdateGeneralElement3D(); // 要素描画の更新

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

                if (viewModel.IsForcedDisplacementVisible) UpdateForcedDisplacement3D(); // 3D地盤変位更新メソッド

                // 剛床の描画
                if (viewModel.IsRigidFloorVisible) UpdateRigidFloor3D();

                // 群杭沈下グリッドの描画
                if (viewModel.IsGroupPileGridVisible) UpdateGroupPileGrid3D();

                // 変形後沈下グリッドの描画
                if (viewModel.IsGroupPileGridDeformationVisible) UpdateSettlementGridDeformation(); // 群杭沈下地盤変位の描画

                // 全てのパスを描画（MainCanvasGeometryのPathGeometryをCanvasに追加）
                viewModel.CanvasGeometry.DrawAllPaths(Canvas3DLayout, viewModel.PileStrokeThickness, viewModel.SoilStrokeThickness);

                // === 以下はDrawAllPathsの後に実行（ColorBaredGeometryのPathが削除されないようにする） ===

                // 軸力・慣性力の描画
                if (viewModel.IsMassLoadingVisible || viewModel.IsAxialLoadingVisible) UpdateLoading3D();

                // 解析結果の描画
                if (viewModel.IsAnalysisResultVisible) UpdateAnalysisResult3D();

                // 追加: 「沈下」のバブル/矢印は最後に描いて最前面に
                if (_pendingSettlementPoints != null &&
                    _pendingSettlementValues != null &&
                    viewModel.AnalysisResultContent == "沈下")
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
    }
}