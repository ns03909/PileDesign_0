using DocumentFormat.OpenXml.Packaging;
using PileDesign.Common;
using PileDesign.Models.InputData;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 要素分割関連 (要素分割杭姿図 / 水平地盤反力分布 / 土圧合力ばね分布) の docx 出力。
    // ElementDivisionViewModel.Draw*Graph は ElementDivisionWindow 起動中の wpfPlot
    // インスタンスに描画するため docx 出力からは呼べない。同じデータソース
    // (SoilPile.HorizontalSoilReactions / inputModel.ElementDivision.DoatsuGoryokuBane)
    // からプロットを再構築する。
    internal partial class WordDocument
    {
        private const double ElemDivPileShapeWidthMm = 100;
        private const double ElemDivPileShapeHeightMm = 130;
        // ShapeDrawer.DrawPileElevation の右側 Z ラベル切れ防止用マージン (PileDiagrams.cs と同じ)
        private const double ElemDivPileShapeRightLabelMarginMm = 5;
        private const double ElemDivGraphWidthMm = 120;
        private const double ElemDivGraphHeightMm = 150;

        private void AddElementDivisionSection(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.ElementDivision == null || mainWindowViewModel == null) return;

            bool any = mainWindowViewModel.IncludeElementDivisionPileShape
                    || mainWindowViewModel.IncludeHorizontalSoilReactionGraph
                    || mainWindowViewModel.IncludeDoatsuGoryokuBaneGraph;
            if (!any) return;

            AddHeader1(body, "要素分割", 1);

            if (mainWindowViewModel.IncludeElementDivisionPileShape)
                AddElementDivisionPileShapes(mainPart, body);

            if (mainWindowViewModel.IncludeHorizontalSoilReactionGraph)
                AddHorizontalSoilReactionGraphs(mainPart, body);

            if (mainWindowViewModel.IncludeDoatsuGoryokuBaneGraph)
                AddDoatsuGoryokuBaneGraph(mainPart, body);
        }

        // ---- 要素分割杭姿図 (各 SoilPile につき 1 図、ZDataItems を分割マーカーとして表示) ------

        private void AddElementDivisionPileShapes(MainDocumentPart mainPart, Body body)
        {
            // 列挙中の concurrent modification を防ぐためスナップショットを取る
            var soilPiles = inputModel.ElementDivision?.SoilPiles?.ToList();
            if (soilPiles == null || soilPiles.Count == 0) return;

            foreach (var soilPile in soilPiles)
            {
                if (soilPile?.PileBodySegments == null || soilPile.PileBodySegments.Count == 0) continue;
                int pileBodyIdx = soilPile.PileBodyNo - 1;
                if (pileBodyIdx < 0 || pileBodyIdx >= inputModel.PileBodies.Count) continue;
                var pileBody = inputModel.PileBodies[pileBodyIdx];

                try
                {
                    var pngBytes = RenderElementDivisionPileShape(soilPile, pileBody);
                    if (pngBytes == null || pngBytes.Length == 0) continue;
                    // 画像幅 = 杭描画幅 + Z ラベル右マージン
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes,
                        ElemDivPileShapeWidthMm + ElemDivPileShapeRightLabelMarginMm, ElemDivPileShapeHeightMm);

                    string caption = $"要素分割杭姿図 — 杭体 {soilPile.PileBodyNo} × 地盤 {soilPile.GroundNo}";
                    AddAutoFigureCaption(body, caption, "図");
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "要素分割杭姿図 (PileBodyNo={PB}, GroundNo={GN}) の docx 出力中にエラー",
                        soilPile.PileBodyNo, soilPile.GroundNo);
                }
            }
        }

        // ShapeDrawer.DrawPileElevation(isElementDivision: true) を新規 Canvas に描画して PNG 化。
        // 杭姿図 (PileDiagrams.cs) と基本同じ手順だが、zs = SoilPile.ZDataItems に分割点マーカーを表示する。
        private byte[] RenderElementDivisionPileShape(SoilPile soilPile, PileBodyInput pileBody)
        {
            // 右側 Z ラベル余白を Canvas 幅に含めることで、ラベルが bitmap 右端で切れないようにする
            double dipW = (ElemDivPileShapeWidthMm + ElemDivPileShapeRightLabelMarginMm) * 96.0 / 25.4;
            double dipH = ElemDivPileShapeHeightMm * 96.0 / 25.4;
            int pxW = (int)Math.Round(dipW * Layout.HiResScale);
            int pxH = (int)Math.Round(dipH * Layout.HiResScale);

            var canvas = new Canvas
            {
                Width = dipW,
                Height = dipH,
                Background = Brushes.White,
            };
            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            // ZDataItems を分割点 zs として渡す
            var zs = soilPile.ZDataItems?.Select(z => z.Z).ToList();

            ShapeDrawer.DrawPileElevation(
                canvas,
                soilPile.PileBodySegments,
                pileBody.PileToeDia,
                pileBody.InsituPileToeHeight,
                pileBody.InsituPileToeAngle,
                pileBody.PrecastConcretePileToeHeightRatio,
                pileBody.PileConstructionType,
                pileTopAltitude: soilPile.Z,
                groundInput: soilPile.GroundInput,
                isElementDivision: true,
                zs: zs);

            canvas.Measure(new Size(dipW, dipH));
            canvas.Arrange(new Rect(0, 0, dipW, dipH));
            canvas.UpdateLayout();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, dipW, dipH));
                var brush = new VisualBrush(canvas)
                {
                    Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, dipW, dipH));
            }

            var rtb = new RenderTargetBitmap(pxW, pxH, 96.0 * Layout.HiResScale, 96.0 * Layout.HiResScale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        // ---- 水平地盤反力分布グラフ (SoilPile ごとに 1 図) --------------------------------

        private void AddHorizontalSoilReactionGraphs(MainDocumentPart mainPart, Body body)
        {
            var soilPiles = inputModel.ElementDivision?.SoilPiles?.ToList();
            if (soilPiles == null || soilPiles.Count == 0) return;

            foreach (var soilPile in soilPiles)
            {
                var items = soilPile?.HorizontalSoilReactions?.ToList();
                if (items == null || items.Count == 0) continue;

                try
                {
                    AddHorizontalSoilReactionGraphForPile(mainPart, body, soilPile);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "水平地盤反力グラフ (PileBodyNo={PB}, GroundNo={GN}) の docx 出力中にエラー",
                        soilPile.PileBodyNo, soilPile.GroundNo);
                }
            }
        }

        private void AddHorizontalSoilReactionGraphForPile(MainDocumentPart mainPart, Body body, SoilPile soilPile)
        {
            var items = soilPile.HorizontalSoilReactions?.ToList();
            if (items == null || items.Count == 0) return;

            var plot = new Plot();
            // 各層を矩形 (x=0..Kh0, y=ZBtm..ZTop) で塗る
            double maxKh0 = 1.0;
            foreach (var item in items)
            {
                var rect = plot.Add.Rectangle(new CoordinateRect(0, item.Kh0, item.ZBtm, item.ZTop));
                rect.FillColor = ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue);
                rect.LineColor = new ScottPlot.Color(0, 0, 0, 255);
                rect.LineWidth = 1;
                if (item.Kh0 > maxKh0) maxKh0 = item.Kh0;
            }

            // 各層境界に Kh0 数値ラベル (ZTop / ZBtm 両端)
            foreach (var item in items)
            {
                PlotHelper.AddText(plot, $"{item.Kh0:N0}", item.Kh0, item.ZTop, maxKh0);
                PlotHelper.AddText(plot, $"{item.Kh0:N0}", item.Kh0, item.ZBtm, maxKh0);
            }

            // 基準線 (X=0 / Z=0)
            var grayColor = new ScottPlot.Color(128, 128, 128, 255);
            plot.Add.VerticalLine(0, 1, grayColor);
            plot.Add.HorizontalLine(0, 1, grayColor);

            string title = $"基準水平地盤反力係数分布 — 杭体 {soilPile.PileBodyNo} × 地盤 {soilPile.GroundNo}";
            plot.Axes.Title.Label.Text = title;
            plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);
            string xLabel = "基準水平地盤反力係数 kₕ₀ (kN/m³)";
            plot.Axes.Bottom.Label.Text = xLabel;
            plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);
            plot.Axes.Left.Label.Text = "Z (m)";
            plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect("Z");
            plot.Axes.AutoScale();
            plot.Axes.AutoScaleExpandX();
            plot.Axes.AutoScaleExpandY();
            plot.Axes.Bottom.Min = 0;

            SavePlotAndAddToBody(mainPart, body, plot,
                $"水平地盤反力 ({soilPile.PileBodyNo}-{soilPile.GroundNo})",
                ElemDivGraphWidthMm, ElemDivGraphHeightMm);
        }

        // ---- 土圧合力ばね分布グラフ (1 図、SoilEmbedment あり時のみ) ----------------------

        private void AddDoatsuGoryokuBaneGraph(MainDocumentPart mainPart, Body body)
        {
            var doatsuItems = inputModel.ElementDivision?.DoatsuGoryokuBane?.Items?.ToList();
            if (doatsuItems == null || doatsuItems.Count == 0) return;

            try
            {
                var plot = new Plot();
                var xs = new List<double>();
                var ys = new List<double>();

                for (int i = 0; i < doatsuItems.Count; i++)
                {
                    var item = doatsuItems[i];
                    if (i == 0)
                    {
                        xs.Add(0.0);
                        ys.Add(item.ZTop);
                    }
                    xs.Add((item.PpTop - item.P0Top) / 3);
                    xs.Add((item.PpBtm - item.P0Btm) / 3);
                    ys.Add(item.ZTop);
                    ys.Add(item.ZBtm);
                    if (i == doatsuItems.Count - 1)
                    {
                        xs.Add(0.0);
                        ys.Add(item.ZBtm);
                    }
                }

                if (xs.Count < 2) return;

                var scatter = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
                scatter.Color = ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue);
                scatter.LineWidth = 2;

                // 基準線 (X=0 / Z=0)
                var grayColor = new ScottPlot.Color(128, 128, 128, 255);
                plot.Add.VerticalLine(0, 1, grayColor);
                plot.Add.HorizontalLine(0, 1, grayColor);

                string title = "基準等相対変位時土圧分布";
                plot.Axes.Title.Label.Text = title;
                plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);
                string xLabel = "土圧 (pₚ−p₀)/3 (kN/m²)";
                plot.Axes.Bottom.Label.Text = xLabel;
                plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);
                plot.Axes.Left.Label.Text = "Z (m)";
                plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect("Z");
                plot.Axes.AutoScale();
                plot.Axes.AutoScaleExpandX();
                plot.Axes.AutoScaleExpandY();

                SavePlotAndAddToBody(mainPart, body, plot, "土圧合力ばね分布",
                    ElemDivGraphWidthMm, ElemDivGraphHeightMm);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "土圧合力ばねグラフの docx 出力中にエラー");
            }
        }

        // ---- 共通: Plot → PNG → docx 埋込 (要素分割専用、寸法可変) ----------------------

        private void SavePlotAndAddToBody(MainDocumentPart mainPart, Body body, Plot plot,
            string caption, double widthMm, double heightMm)
        {
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            int pxW = MmToPx(widthMm, Dpi, Layout.HiResScale);
            int pxH = MmToPx(heightMm, Dpi, Layout.HiResScale);
            plot.SavePng(tempFile, pxW, pxH);
            WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);
            try { File.Delete(tempFile); } catch { }
            AddAutoFigureCaption(body, caption, "図");
        }
    }
}
