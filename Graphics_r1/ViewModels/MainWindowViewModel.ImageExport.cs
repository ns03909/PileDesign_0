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
    /// MainWindowViewModel — モデル図の画像出力。
    ///
    /// 画面の 3D キャンバスを画像にして、ファイルに保存する / クリップボードへ写す /
    /// 計算書へ渡す。倍率を指定して、画面より細かい絵を作れる。
    ///
    /// <b>倍密度で描く図は、文字の大きさも倍率に追従させること。</b>
    /// px を直に書くと、紙面で実効 3.7pt になる。大きさは pt で持ち、
    /// 変換を通してから px にする。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>画像の倍率の上限。メニューは 1 倍・2 倍だが、コマンドに直接渡る値に備える。</summary>
        internal const double MaxImageScale = 8.0;

        /// <summary>画像 1 辺の画素数の上限 (RenderTargetBitmap が扱える大きさの目安)。</summary>
        internal const int MaxImageSide = 16384;

        /// <summary>画像の総画素数の上限 (1 画素 4 バイトなので約 200 MB)。</summary>
        internal const long MaxImagePixels = 50_000_000;

        /// <summary>
        /// 倍率の指定を読む。空なら 1 倍。正の有限の数で、上限 (<see cref="MaxImageScale"/>) 以下でなければ false と理由を返す。
        ///
        /// 以前は読めた数をそのまま画素数の計算に使っていたので、0・負数・極端に大きい値が渡ると、
        /// 0 画素の画像を作る例外や、メモリを使い切る大きさの画像を作ろうとした。
        /// </summary>
        internal static bool TryResolveImageScale(string? scaleParam, out double scale, out string? error)
        {
            scale = 1.0;
            error = null;
            if (string.IsNullOrEmpty(scaleParam)) return true;
            if (!PileDesign.Common.NumericText.TryParse(scaleParam, out double parsed) || !double.IsFinite(parsed) || parsed <= 0)
            {
                error = $"画像の倍率「{scaleParam}」は正の数ではありません。";
                return false;
            }
            if (parsed > MaxImageScale)
            {
                error = $"画像の倍率 {parsed} は大きすぎます (最大 {MaxImageScale} 倍)。";
                return false;
            }
            scale = parsed;
            return true;
        }

        /// <summary>
        /// 画像の画素数を決める。1 画素以上で、1 辺と総画素数が上限以下でなければ false と理由を返す。
        /// </summary>
        internal static bool TryImagePixelSize(double actualWidth, double actualHeight, double dpiX, double dpiY, double scale,
            out int width, out int height, out string? error)
        {
            width = height = 0;
            error = null;
            double w = actualWidth * dpiX / 96.0 * scale;
            double h = actualHeight * dpiY / 96.0 * scale;
            if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1)
            {
                error = "モデル図の表示領域の大きさが 0 のため、画像を作れません (ウィンドウを表示してからやり直してください)。";
                return false;
            }
            if (w > MaxImageSide || h > MaxImageSide || w * h > MaxImagePixels)
            {
                error = $"画像が大きすぎます ({w:N0}×{h:N0} 画素。1 辺 {MaxImageSide:N0}・計 {MaxImagePixels:N0} 画素まで)。倍率を下げてください。";
                return false;
            }
            width = (int)w;
            height = (int)h;
            return true;
        }

        /// <summary>
        /// モデル図 (Canvas3D) を、指定の倍率で白地の画像にする。倍率・大きさが不正なら理由を知らせて null。
        /// 保存とコピーで同じ手順を使う (以前は 2 か所に同じ処理の写しがあった)。
        /// </summary>
        private RenderTargetBitmap? RenderCanvasImage(string? scaleParam, string action)
        {
            if (Canvas3DLayout == null) return null;

            var dpiInfo = VisualTreeHelper.GetDpi(Canvas3DLayout);
            if (!TryResolveImageScale(scaleParam, out double scale, out string? error)
                || !TryImagePixelSize(Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight,
                        dpiInfo.PixelsPerInchX, dpiInfo.PixelsPerInchY, scale, out int width, out int height, out error))
            {
                PileDesign.Services.MessageService.Show(error!, action, MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            // Canvas を RenderTargetBitmap でキャプチャ（システムDPI考慮）
            var rtb = new RenderTargetBitmap(width, height,
                dpiInfo.PixelsPerInchX * scale, dpiInfo.PixelsPerInchY * scale, PixelFormats.Pbgra32);

            // 背景を白で描画してからCanvasを直接レンダリング
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null,
                    new Rect(0, 0, Canvas3DLayout.ActualWidth, Canvas3DLayout.ActualHeight));
            }
            rtb.Render(dv);
            rtb.Render(Canvas3DLayout);
            return rtb;
        }

        /// <summary>
        /// Canvas3D の画像を保存するコマンド
        /// </summary>
        [RelayCommand]
        private void ImageSave(string scaleParam)
        {
            if (Canvas3DLayout == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
                DefaultExt = ".png",
                FileName = "Canvas3D_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var rtb = RenderCanvasImage(scaleParam, "画像の保存");
                    if (rtb == null) return;

                    // エンコーダーを選択
                    BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLower() switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                        ".bmp" => new BmpBitmapEncoder(),
                        _ => new PngBitmapEncoder()
                    };

                    encoder.Frames.Add(BitmapFrame.Create(rtb));

                    // 一時ファイルに書き切ってから差し替える。以前は保存先を直接作り直していたので、
                    // 既存の画像を選んでエンコードや書き込みに失敗すると、前の画像が失われた
                    PileDesign.Services.FileOperationService.WriteAtomically(dialog.FileName, stream => encoder.Save(stream));

                    StatusMessage = $"画像を保存しました ({rtb.PixelWidth}x{rtb.PixelHeight}): {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    PileDesign.Services.MessageService.ShowError($"画像の保存に失敗しました", ex, "エラー");
                }
            }
        }

        /// <summary>
        /// Canvas3D の画像をクリップボードにコピーするコマンド
        /// </summary>
        [RelayCommand]
        private void ImageCopy(string scaleParam)
        {
            if (Canvas3DLayout == null) return;

            try
            {
                var rtb = RenderCanvasImage(scaleParam, "画像のコピー");
                if (rtb == null) return;

                // Clipboard.SetImage()はStringMetadata非対応で例外になる環境があるため
                // BitmapSourceを一切渡さず、生バイトストリームのみでクリップボードに設定
                var pngEnc = new PngBitmapEncoder();
                pngEnc.Frames.Add(BitmapFrame.Create(rtb));
                var pngStream = new System.IO.MemoryStream();
                pngEnc.Save(pngStream);

                var bmpEnc = new BmpBitmapEncoder();
                bmpEnc.Frames.Add(BitmapFrame.Create(rtb));
                var bmpStream = new System.IO.MemoryStream();
                bmpEnc.Save(bmpStream);
                // DIB = BMPからファイルヘッダ(14バイト)を除いたもの
                bmpStream.Position = 14;
                var dibBytes = new byte[bmpStream.Length - 14];
                bmpStream.Read(dibBytes, 0, dibBytes.Length);

                var dataObject = new DataObject();
                dataObject.SetData("PNG", pngStream, false);
                dataObject.SetData(DataFormats.Dib, new System.IO.MemoryStream(dibBytes), false);

                // 書き込めたかで知らせ方を分ける。以前は結果を見ずに「コピーしました」と出していたので、
                // 他のアプリがクリップボードを使っていて書き込めなくても成功と表示した
                StatusMessage = DescribeImageCopyResult(
                    Common.ClipboardHelper.TrySetDataObject(dataObject, true), rtb.PixelWidth, rtb.PixelHeight);
            }
            catch (Exception ex)
            {
                PileDesign.Services.MessageService.ShowError($"画像のコピーに失敗しました", ex, "エラー");
            }
        }

        /// <summary>画像のコピーの結果の文。書き込めなかったときは、その理由と対処を書く。</summary>
        internal static string DescribeImageCopyResult(bool copied, int width, int height)
            => copied
                ? $"画像をクリップボードにコピーしました ({width}x{height})"
                : "画像をクリップボードにコピーできませんでした。他のアプリがクリップボードを使っている可能性があります。少し待ってからやり直してください。";

        /// <summary>
        /// アイソメトリック表示でモデル全体（杭先端含む）をキャプチャし、PNGバイト配列を返す。
        /// Word出力用。キャプチャ後にカメラ状態は元に戻す。
        /// </summary>
        public byte[]? CaptureIsometricModelImageBytes()
        {
            if (Canvas3DLayout == null || CurrentInputModel == null || CurrentInputModel.PileLayoutItems.Count == 0)
                return null;

            // --- 1. 現在の状態を保存 ---
            var savedTht = CanvasThreeDView.Tht;
            var savedPhi = CanvasThreeDView.Phi;
            var savedScale = CanvasThreeDView.Scale;
            var savedViewTransition = CanvasThreeDView.ViewTransition;
            var savedCt = CanvasThreeDView.Ct;
            var savedDv0 = CanvasThreeDView.Dv0;
            var savedTickMark = IsTickMarkVisible;
            var savedAxes = IsXYZAxesVisible;

            try
            {
                // SetCt自動上書きをスキップするフラグをON
                IsCapturingForExport = true;

                // カーソル位置の目印 (結果ツールチップの赤い丸) を消す。
                // 再描画では消えないので、ここで明示的に消しておく。
                HideTransientOverlaysAction?.Invoke();

                // --- 2. 杭頭＋杭先端＋地盤範囲を含む全3D点を収集 ---
                var allPoints = new System.Collections.ObjectModel.ObservableCollection<Point3D>();
                foreach (var pile in CurrentInputModel.PileLayoutItems)
                {
                    allPoints.Add(pile.Point3D); // 杭頭

                    int idx = pile.PileBodyNo - 1;
                    if (idx >= 0 && CurrentInputModel.PileBodies != null && idx < CurrentInputModel.PileBodies.Count)
                    {
                        var pileBody = CurrentInputModel.PileBodies[idx];
                        if (pileBody.PileBodySegments != null && pileBody.PileBodySegments.Count > 0)
                        {
                            double totalLen = pileBody.PileBodySegments.Sum(s => s.SegmentLength);
                            allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, pile.Point3D.Z - totalLen));
                        }
                    }

                    int gIdx = pile.GroundNo - 1;
                    if (gIdx >= 0 && CurrentInputModel.GroundsInput != null && gIdx < CurrentInputModel.GroundsInput.Count)
                    {
                        var ground = CurrentInputModel.GroundsInput[gIdx];
                        allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, ground.GroundTopAltitude));
                        if (ground.GroundLayers != null && ground.GroundLayers.Count > 0)
                        {
                            double btmAlt = ground.GroundLayers[^1].BottomAltitude;
                            allPoints.Add(new Point3D(pile.Point3D.X, pile.Point3D.Y, btmAlt));
                        }
                    }
                }

                // --- 3. 装飾要素を設定（通り芯は残す） ---
                IsTickMarkVisible = false;
                IsXYZAxesVisible = false;

                // --- 4. 全点を中心にカメラ設定 ---
                CanvasThreeDView.SetCt(allPoints);
                CanvasThreeDView.ViewTransition = new Point(0, 0);

                // --- 5. アイソメ視点に設定（SetCtはスキップされる） ---
                CanvasThreeDView.Tht = -45;
                CanvasThreeDView.Phi = 45;
                Canvas3DLayout.UpdateLayout();
                Canvas3DLayout.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

                // --- 6. 実際のCanvasサイズを取得 ---
                double canvasW = Canvas3DLayout.ActualWidth;
                double canvasH = Canvas3DLayout.ActualHeight;
                if (canvasW <= 0 || canvasH <= 0) return null;

                // --- 7. 全点の2Dバウンディングボックスを計算 ---
                double xMax = double.MinValue, yMax = double.MinValue;
                double xMin = double.MaxValue, yMin = double.MaxValue;
                foreach (var pt3d in allPoints)
                {
                    Point pt2d = CanvasThreeDView.Transformation(pt3d);
                    if (pt2d.X > xMax) xMax = pt2d.X;
                    if (pt2d.Y > yMax) yMax = pt2d.Y;
                    if (pt2d.X < xMin) xMin = pt2d.X;
                    if (pt2d.Y < yMin) yMin = pt2d.Y;
                }
                double bbW = xMax - xMin;
                double bbH = yMax - yMin;
                if (bbW <= 0 || bbH <= 0) return null;

                // --- 8. スケールをフィットさせ、中央に配置 ---
                double gridMargin = GridSymbolZoneWidth * 2; // 通り芯符号用マージン
                double availW = canvasW - gridMargin * 2;
                double availH = canvasH - gridMargin * 2;
                double fitRatio = Math.Min(availW / bbW, availH / bbH);

                // 中央補正: スケール変更後のBB中心がCanvas中心に来るようVTを設定
                double bbCenterX = (xMin + xMax) / 2;
                double bbCenterY = (yMin + yMax) / 2;
                double orgX = canvasW / 2;
                double orgY = canvasH / 2;
                CanvasThreeDView.ViewTransition = new Point(
                    (orgX - bbCenterX) * fitRatio,
                    (orgY - bbCenterY) * fitRatio);
                CanvasThreeDView.Scale *= fitRatio; // re-render triggered

                Canvas3DLayout.UpdateLayout();
                Canvas3DLayout.Dispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

                // --- 9. コンテンツ領域をVisualBrushで切り出してキャプチャ ---
                // 最終的な2D BBを再計算（スケール・VT適用後）
                xMax = double.MinValue; yMax = double.MinValue;
                xMin = double.MaxValue; yMin = double.MaxValue;
                foreach (var pt3d in allPoints)
                {
                    Point pt2d = CanvasThreeDView.Transformation(pt3d);
                    if (pt2d.X > xMax) xMax = pt2d.X;
                    if (pt2d.Y > yMax) yMax = pt2d.Y;
                    if (pt2d.X < xMin) xMin = pt2d.X;
                    if (pt2d.Y < yMin) yMin = pt2d.Y;
                }

                // 通り芯符号用のマージンを追加
                double captureMargin = GridSymbolZoneWidth * 1.5;
                double cropX = Math.Max(0, xMin - captureMargin);
                double cropY = Math.Max(0, yMin - captureMargin);
                double cropR = Math.Min(canvasW, xMax + captureMargin);
                double cropB = Math.Min(canvasH, yMax + captureMargin);
                double cropW = cropR - cropX;
                double cropH = cropB - cropY;
                if (cropW <= 0 || cropH <= 0) return null;

                double capScale = 2.0;
                int outW = (int)(cropW * capScale);
                int outH = (int)(cropH * capScale);
                var rtb = new RenderTargetBitmap(outW, outH, 96 * capScale, 96 * capScale, PixelFormats.Pbgra32);

                // 白背景
                var bgVisual = new DrawingVisual();
                using (var dc = bgVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cropW, cropH));
                }
                rtb.Render(bgVisual);

                // VisualBrushでコンテンツ領域を切り出し
                var contentVisual = new DrawingVisual();
                using (var dc = contentVisual.RenderOpen())
                {
                    var vb = new VisualBrush(Canvas3DLayout)
                    {
                        Viewbox = new Rect(cropX, cropY, cropW, cropH),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Uniform
                    };
                    dc.DrawRectangle(vb, null, new Rect(0, 0, cropW, cropH));
                }
                rtb.Render(contentVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                return ms.ToArray();
            }
            finally
            {
                // --- フラグをOFF ---
                IsCapturingForExport = false;

                // --- 装飾復元 ---
                IsTickMarkVisible = savedTickMark;
                IsXYZAxesVisible = savedAxes;

                // --- カメラ復元 ---
                CanvasThreeDView.Dv0 = savedDv0;
                CanvasThreeDView.Ct = savedCt;
                CanvasThreeDView.ViewTransition = savedViewTransition;
                CanvasThreeDView.Scale = savedScale;
                CanvasThreeDView.Tht = savedTht;
                CanvasThreeDView.Phi = savedPhi;
                UpdateCanvas3DAction?.Invoke();
            }
        }
    }
}
