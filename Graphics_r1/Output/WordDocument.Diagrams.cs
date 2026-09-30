using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using ScottPlot;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using Drawing = DocumentFormat.OpenXml.Wordprocessing.Drawing;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Int32Value = DocumentFormat.OpenXml.Int32Value;
using NumberingFormat = DocumentFormat.OpenXml.Wordprocessing.NumberingFormat;
using Point = System.Windows.Point;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;
using WpStyle = DocumentFormat.OpenXml.Wordprocessing.Style;

using Serilog;

namespace PileDesign.Output
{
    // WPF DrawingContext による図版生成: 荷重組合せ図・杭伏図・杭反力立面図・沈下コンター・ばね記号描画。物理分割 partial (純粋移動)。
    internal partial class WordDocument
    {
        public static Drawing CreateLoadCombinationDiagramDrawing(MainDocumentPart mainDocumentPart,
            double ps, double pf, double alphaL, double betaU, double betaL,
            int widthMm = 30, int heightMm = 30)
        {
            // 1. WPFで図形を描画し、BitmapSourceとして取得
            int widthPx = MmToPx(widthMm, Dpi, 2.0);
            int heightPx = MmToPx(heightMm, Dpi, 2.0);

            // スケール
            double scale = Math.Min(widthPx / 300.0, heightPx / 212.5);
            double centerX = widthPx * 2.0 / 3.0;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                // 円
                double ellipseW = 50 * scale, ellipseH = 50 * scale;
                dc.DrawEllipse(null, new Pen(NikkenBrush.SkyBlue, 1), new(centerX, ellipseH * 1.5), ellipseW, ellipseH);

                // ばね
                double springW = 5 * scale, springH = 20 * scale;
                dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1), new Rect(centerX - springW * 0.5, ellipseH * 2.5, springW, springH));

                // 基礎
                double baseW = 70 * scale, baseH = 30 * scale;
                dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1), new Rect(centerX - baseW * 0.5, ellipseH * 2.5 + springH, baseW, baseH));

                // 杭
                double pileW = 9 * scale, pileH = 100 * scale, pileSpacing = 45 * scale;
                double pileY = ellipseH * 2.5 + springH + baseH;
                dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1), new Rect(centerX - pileSpacing * 0.5 - pileW * 0.5, pileY, pileW, pileH));
                dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1), new Rect(centerX + pileSpacing * 0.5 - pileW * 0.5, pileY, pileW, pileH));

                // 放物線

                double rect2W = baseW, rect2H = baseH, rect4H = pileH;
                double leftX = centerX - rect2W * 2;

                double parabolaStartX = leftX + rect2W * 1 * alphaL;
                double parabolaStartY = springH + ellipseH * 2.5;
                double parabolaControlX = parabolaStartX;
                double parabolaControlY = parabolaStartY + rect4H * 0.5;
                double parabolaEndX = leftX + rect2W * 0.5 * alphaL;
                double parabolaEndY = springH + ellipseH * 2.5 + rect2H + rect4H;

                // PathGeometryでベジェ曲線を作成
                var geometry = new PathGeometry();
                var figure = new PathFigure
                {
                    StartPoint = new Point(parabolaStartX, parabolaStartY),
                    IsClosed = false
                };
                figure.Segments.Add(new BezierSegment(
                    new Point(parabolaControlX, parabolaControlY),
                    new Point(parabolaControlX, parabolaControlY),
                    new Point(parabolaEndX, parabolaEndY),
                    true
                ));
                geometry.Figures.Add(figure);

                // 描画
                dc.DrawGeometry(null, new Pen(NikkenBrush.SkyBlue, 1), geometry);
                dc.DrawLine(new Pen(NikkenBrush.SkyBlue, 1), new(leftX, parabolaEndY), new(parabolaEndX, parabolaEndY));
                dc.DrawLine(new Pen(NikkenBrush.SkyBlue, 1), new(leftX, parabolaEndY), new(leftX, parabolaStartY));
                dc.DrawLine(new Pen(NikkenBrush.SkyBlue, 1), new(parabolaStartX, parabolaStartY), new(leftX, parabolaStartY));

                var typeface = new Typeface(Layout.FontName);
                var brush = Brushes.Black;
                var fontSize = 12;

                double denom = Math.Max(Math.Abs(ps) + Math.Abs(pf), 1e-6);
                double forceRatio = 125 / denom * scale;

                Point actionCenter0 = new(centerX - baseW * 0.5, ellipseH * 2.5 + springH + baseH * 0.5);
                DrawHorizontalArrow(dc, actionCenter0, betaL * pf * forceRatio, 2 * scale);

                double ppd = 1.0;
                try
                {
                    var app = System.Windows.Application.Current;
                    if (app?.MainWindow != null)
                        ppd = VisualTreeHelper.GetDpi(app.MainWindow).PixelsPerDip;
                }
                catch { ppd = 1.0; }

                var text2 = $"{betaL:F2}・Pf";
                var ft2 = new FormattedText(
                    text2,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, fontSize, brush, ppd
                    /*VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip*/)
                {
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                dc.DrawText(ft2, new Point(actionCenter0.X - 0.5 * Math.Abs(betaL) * pf * forceRatio, actionCenter0.Y + 5 * scale));

                Point actionCenter1 = new(actionCenter0.X - Math.Abs(betaL) * pf * forceRatio, actionCenter0.Y);
                DrawHorizontalArrow(dc, actionCenter1, betaU * ps * forceRatio, 2 * scale);

                var text1 = $"{betaU:F2}・Ps";
                var ft1 = new FormattedText(
                    text1,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, fontSize, brush, ppd
                    /*VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip*/)
                {
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                dc.DrawText(ft1, new Point(actionCenter1.X - 0.5 * Math.Abs(betaU) * ps * forceRatio, actionCenter1.Y - 5 * scale - ft1.Height));

                var text3 = $"{alphaL:F2}・D";
                var ft3 = new FormattedText(
                    text3,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface, fontSize, brush, ppd
                    /*VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip*/)
                {
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                dc.DrawText(ft3, new Point(parabolaEndX, (parabolaStartY + parabolaEndY) * 0.5 - ft3.Height));
            }

            // 2. RenderTargetBitmapで画像化
            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);

            // 3. PNGとしてメモリストリームに保存
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            byte[] imageBytes;
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                imageBytes = ms.ToArray();
            }

            // 4. OpenXML Drawing要素を生成
            // 関係の番号は足した順に振る (以前は乱数で作っていて、同じ結果から出しても計算書の中身が毎回変わった)
            var mainPart = mainDocumentPart;
            var imagePart = WordDrawingBuilder.AddImagePartInOrder(mainPart, ImagePartType.Png);
            using (var stream = new MemoryStream(imageBytes))
                imagePart.FeedData(stream);
            string imagePartId = mainPart.GetIdOfPart(imagePart);

            // 5. Drawing要素を返す
            // mm→EMU変換
            long widthEmu = MmToEmu(widthMm);
            long heightEmu = MmToEmu(heightMm);

            var drawing = new Drawing(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent
                    {
                        Cx = widthEmu, // EMU変換
                        Cy = heightEmu
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent
                    {
                        LeftEdge = 0L,
                        TopEdge = 0L,
                        RightEdge = 0L,
                        BottomEdge = 0L
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties
                    {
                        Id = (UInt32Value)1U,
                        Name = "LoadCombinationDiagram"
                    },
                    new DocumentFormat.OpenXml.Drawing.Graphic(
                        new DocumentFormat.OpenXml.Drawing.GraphicData(
                            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties
                                    {
                                        Id = (UInt32Value)0U,
                                        Name = "LoadCombinationDiagram.png"
                                    },
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()
                                ),
                                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                    new DocumentFormat.OpenXml.Drawing.Blip
                                    {
                                        Embed = imagePartId,
                                        CompressionState = DocumentFormat.OpenXml.Drawing.BlipCompressionValues.Print
                                    },
                                    new DocumentFormat.OpenXml.Drawing.Stretch(
                                        new DocumentFormat.OpenXml.Drawing.FillRectangle()
                                    )
                                ),
                                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                    new DocumentFormat.OpenXml.Drawing.Transform2D(
                                        new DocumentFormat.OpenXml.Drawing.Offset { X = 0L, Y = 0L },
                                        new DocumentFormat.OpenXml.Drawing.Extents
                                        {
                                            Cx = widthEmu,
                                            Cy = heightEmu
                                        }
                                    ),
                                    new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                                        new DocumentFormat.OpenXml.Drawing.AdjustValueList()
                                    )
                                    { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }
                                )
                            )
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                )
            );

            return drawing;
        }

        private static Paragraph GetParagraph(
            string text,
            string horAlign = "center",
            double fontSize = 10.5,
            string fontName = "ＭＳ ゴシック")
        {
            JustificationValues horizontalAlign = horAlign switch
            {
                "right" => JustificationValues.Right,
                "center" => JustificationValues.Center,
                _ => JustificationValues.Left
            };

            var paraProps = new ParagraphProperties(
                new Justification() { Val = horizontalAlign }
            );

            var runs = new List<OpenXmlElement>();
            int idx = 0;
            while (idx < text.Length)
            {
                // 改行（LF）に対応
                if (text[idx] == '\n')
                {
                    runs.Add(new Run(new Break()));
                    idx++;
                    continue;
                }

                if (text[idx..].StartsWith("<^"))
                {
                    int close = text.IndexOf('>', idx + 2);
                    if (close > idx + 2)
                    {
                        // 上付き文字（複数文字対応）
                        string supText = text[(idx + 2)..close];
                        var run = new Run(
                            new RunProperties(
                                new FontSize() { Val = (fontSize * 2).ToString() },
                                new RunFonts()
                                {
                                    Ascii = fontName,
                                    HighAnsi = fontName,
                                    EastAsia = fontName,
                                    ComplexScript = fontName
                                },
                                new VerticalTextAlignment() { Val = VerticalPositionValues.Superscript }
                            ),
                            new Text(supText) { Space = SpaceProcessingModeValues.Preserve }
                        );
                        runs.Add(run);
                        idx = close + 1;
                        continue;
                    }
                }
                if (text[idx..].StartsWith("<_"))
                {
                    int close = text.IndexOf('>', idx + 2);
                    if (close > idx + 2)
                    {
                        // 下付き文字（複数文字対応）
                        string subText = text[(idx + 2)..close];
                        var run = new Run(
                            new RunProperties(
                                new FontSize() { Val = (fontSize * 2).ToString() },
                                new RunFonts()
                                {
                                    Ascii = fontName,
                                    HighAnsi = fontName,
                                    EastAsia = fontName,
                                    ComplexScript = fontName
                                },
                                new VerticalTextAlignment() { Val = VerticalPositionValues.Subscript }
                            ),
                            new Text(subText) { Space = SpaceProcessingModeValues.Preserve }
                        );
                        runs.Add(run);
                        idx = close + 1;
                        continue;
                    }
                }

                // 通常文字
                {
                    int nextIdx = text.IndexOfAny(['<', '\n'], idx);
                    if (nextIdx == -1) nextIdx = text.Length;
                    // 未閉じの '<' は通常文字として扱うため、少なくとも1文字は進める
                    if (nextIdx == idx) nextIdx = idx + 1;
                    string normalText = text[idx..nextIdx];
                    if (!string.IsNullOrEmpty(normalText))
                    {
                        var run = new Run(
                            new RunProperties(
                                new FontSize() { Val = (fontSize * 2).ToString() },
                                new RunFonts()
                                {
                                    Ascii = fontName,
                                    HighAnsi = fontName,
                                    EastAsia = fontName,
                                    ComplexScript = fontName
                                }
                            ),
                            new Text(normalText) { Space = SpaceProcessingModeValues.Preserve }
                        );
                        runs.Add(run);
                    }
                    idx = nextIdx;
                }
            }

            var para = new Paragraph(paraProps);
            foreach (var run in runs)
                para.Append(run);

            return para;
        }

        // TableCellの縦揃えを指定する例
        private static void SetTableCellWithVerticalAlign(TableCell cell, Paragraph para, string verAlign = "center")
        {
            // TableCellPropertiesがなければ新規作成
            cell.TableCellProperties ??= new TableCellProperties();

            // 縦揃え値を変換
            TableVerticalAlignmentValues align = verAlign switch
            {
                "top" => TableVerticalAlignmentValues.Top,
                "bottom" => TableVerticalAlignmentValues.Bottom,
                "center" => TableVerticalAlignmentValues.Center,
                _ => TableVerticalAlignmentValues.Center
            };

            // TableCellVerticalAlignmentを追加
            cell.TableCellProperties.Append(new TableCellVerticalAlignment() { Val = align });

            // Paragraphを追加
            cell.Append(para);
        }

        // ダイヤグラム挿入メソッド
        private static void AddLoadCombinationDiagramByMm(
            MainDocumentPart mainDocumentPart,
            Body body,
            double ps,
            double pf,
            double alphaL,
            double betaU,
            double betaL,
            int widthMm = 30,
            int heightMm = 30
            )
        {
            // 旧: SaveLoadCombinationDiagramByMm(fileName, ...); WordDocumentUtils.AddImageToBodyByMm(...)
            // 新: DiagramRenderer で PNG bytes を作り、WordDrawingBuilder で body に挿入（ファイル不要）
            try
            {
                var pngBytes = DiagramRenderer.RenderLoadCombinationDiagramPng(ps, pf, alphaL, betaU, betaL, widthMm, heightMm);
                WordDrawingBuilder.AddPngBytesToBody(mainDocumentPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "荷重の組合せの図", ex);
            }
        }

        // 荷重コンビネーションイメージ挿入
        // 矢印描画
        private static void DrawHorizontalArrow(DrawingContext dc, Point point, double length, double barWidth = 5)
        {
            double triHeight = barWidth * 5;
            double triHalfWidth = barWidth * 3;
            Point upTriangle;
            Point dnTriangle;
            Point upInWidth;
            Point dnInWidth;
            Point upOutWidth;
            Point dnOutWidth;
            if (length > 0)
            {
                upTriangle = new(point.X - triHeight, point.Y + triHalfWidth);
                dnTriangle = new(point.X - triHeight, point.Y - triHalfWidth);
                upInWidth = new(point.X - triHeight, point.Y + barWidth);
                dnInWidth = new(point.X - triHeight, point.Y - barWidth);
                upOutWidth = new(point.X - length, point.Y + barWidth);
                dnOutWidth = new(point.X - length, point.Y - barWidth);
            }
            else // (length <= 0)
            {

                upTriangle = new(point.X + length + triHeight, point.Y + triHalfWidth);
                dnTriangle = new(point.X + length + triHeight, point.Y - triHalfWidth);
                upInWidth = new(point.X + length + triHeight, point.Y + barWidth);
                dnInWidth = new(point.X + length + triHeight, point.Y - barWidth);
                upOutWidth = new(point.X, point.Y + barWidth);
                dnOutWidth = new(point.X, point.Y - barWidth);
                point = new(point.X + length, point.Y);
            }
            // ポリラインの頂点をリストで定義
            var points = new List<Point>
            {
                upTriangle,
                upInWidth,
                upOutWidth,
                dnOutWidth,
                dnInWidth,
                dnTriangle
            };

            // PathGeometryでポリラインを作成
            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = point,
                IsClosed = true
            };
            figure.Segments.Add(new PolyLineSegment(points, true));
            geometry.Figures.Add(figure);

            // 描画
            dc.DrawGeometry(null, new Pen(NikkenBrush.SkyBlue, 1), geometry);
        }

        // 水平力検討用杭ダイヤグラム挿入メソッド
        private static void AddPileForceDiagramByMm(
            MainDocumentPart mainDocumentPart,
            Body body,
            double widthMm,
            double heightMm,
            SoilPile soilPile,
            string springType = "horizontal"
            )
        {
            try
            {
                // 図中の書体・線の太さは Layout の規約から渡す。DiagramRenderer 側で
                // px を直書きすると、倍密度 (HiResScale) の画像に極小の文字が焼き込まれる。
                var style = new PileDiagramStyle(
                    Layout.DiagramFontName,
                    Layout.DiagramFontSizePt,
                    Layout.DiagramSmallFontSizePt,
                    Layout.DiagramLineWidthThickPt,
                    Layout.DiagramLineWidthThinPt,
                    Layout.SpringZigzagMaxCount,
                    Layout.MinSpringLengthPx);
                var pngBytes = DiagramRenderer.RenderPileForceElevationPngBytes(
                    soilPile, springType, style, widthMm, heightMm,
                    dpi: Layout.BaseDpi, scale: Layout.HiResScale);
                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainDocumentPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "杭に作用する力の図", ex);
            }
        }
        //{
        //    string fileName = "temp.png";
        //    SavePileForceElevationDiagramByMm(fileName, widthMm, heightMm, soilPile, springType);
        //    WordDocumentUtils.AddImageToBodyByMm(mainDocumentPart, body, fileName, widthMm, heightMm);
        //    if (File.Exists(fileName)) { File.Delete(fileName); }
        //}

        // 杭伏図ダイヤグラム挿入メソッド
        private void AddPilingLayoutDiagramByMm(
            MainDocumentPart mainDocumentPart,
            Body body,
            double widthMm,
            double heightMm,
            Func<PileLayoutDataItem, string> markSelector
            )
        //{
        //    string fileName = "temp.png";
        //    SavePilingLayoutDiagramByMm(fileName, widthMm, heightMm, markSelector);
        //    WordDocumentUtils.AddImageToBodyByMm(mainDocumentPart, body, fileName, widthMm, heightMm);
        //    if (File.Exists(fileName)) { File.Delete(fileName); }
        //}
        {
            try
            {
                // 直径 (m) を決めるオプションセレクタ（安全に null チェック）
                double diameterSelector(PileLayoutDataItem pli)
                {
                    if (inputModel?.PileBodies == null || pli == null) return 1.0;
                    // 参照の切れた杭は径を仮に置かず、図ごと省いて理由 (杭番号・杭体番号) を書く
                    var pb = inputModel.RequirePileBody(pli);
                    var seg = pb?.PileBodySegments?.FirstOrDefault();
                    if (seg?.PileSection == null) return 1.0;
                    // 元実装では PileDiameter は mm 単位で扱っているため m に変換
                    return seg.PileSection.PileDiameter * 0.001;
                }

                var pngBytes = DiagramRenderer.RenderPilingLayoutPngBytes(
                    inputModel.PileLayoutItems,
                    markSelector,
diameterSelector,
                    widthMm,
                    heightMm,
                    dpi: Layout.BaseDpi,
                    scale: Layout.HiResScale
                );

                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainDocumentPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "杭配置図", ex);
            }
        }

        /// <summary>
        /// 群杭沈下コンタ図をWord文書に挿入
        /// </summary>
        private void AddGroupPileSettlementContourDiagram(
            MainDocumentPart mainDocumentPart,
            Body body,
            double widthMm = 150,
            double heightMm = 150)
        {
            try
            {
                var pgs = inputModel?.PileGroupSettlement;
                // 表示中のケースから読む。複製 (SettlementGridData) を読むと、
                // ケースを切り替えたのに計算書だけ古い図が出る。
                var settlementData = pgs?.ActiveSettlementGridData;
                if (settlementData == null || settlementData.Count == 0)
                    return;

                // 格子は表示中のケースの沈下値から引く (画面と同じ)。
                // 入力側の SettlementGridX/Y は解析時のスナップショットに移らないので、
                // 沈下だけ再実行した場合に計算書のコンタだけ古い軸で描かれてしまう。
                var gridXs = pgs?.ActiveGridX?.ToList();
                var gridYs = pgs?.ActiveGridY?.ToList();
                if (!PileDesign.Services.SettlementGridDataValidator.TryBuildGrid(
                    settlementData, gridXs ?? [], gridYs ?? [], out _, out var gridError))
                {
                    NoteOmitted(body, "群杭沈下の分布図",
                        new System.IO.InvalidDataException(gridError));
                    return;
                }

                // 杭位置リスト
                var pilePositions = inputModel?.PileLayoutItems?
                    .Select(p => (X: p.Point3D.X, Y: p.Point3D.Y))
                    .ToList();

                var pngBytes = DiagramRenderer.RenderSettlementContourDiagram(
                    settlementData,
                    gridXs,
                    gridYs,
                    pilePositions,
                    widthMm,
                    heightMm,
                    dpi: Layout.BaseDpi,
                    scale: Layout.HiResScale,
                    colorBandCount: 12
                );

                if (pngBytes != null && pngBytes.Length > 0)
                {
                    WordDrawingBuilder.AddPngBytesToBody(mainDocumentPart, body, pngBytes, widthMm, heightMm);
                    AddAutoFigureCaption(body,
                        "群杭沈下コンタ図" + PileDesign.Services.GroupSettlementCaseLabel.Describe(pgs?.ActiveRecord), "図");
                }
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "群杭沈下の分布図", ex);
            }
        }

        /// <summary>
        /// 各杭位置の沈下量一覧表をWord文書に挿入
        /// </summary>
        private void AddPileSettlementTable(Body body)
        {
            var pileLayoutItems = inputModel?.PileLayoutItems;
            if (pileLayoutItems == null || pileLayoutItems.Count == 0) return;

            AddLineBreak(body);
            AddAutoFigureCaption(body,
                "各杭位置の沈下量一覧" + PileDesign.Services.GroupSettlementCaseLabel.Describe(
                    inputModel?.PileGroupSettlement?.ActiveRecord), "表");

            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // ヘッダー行
            TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["No"], fontSize, "center"),
                CreateTableCell(["X", "[m]"], fontSize, "center"),
                CreateTableCell(["Y", "[m]"], fontSize, "center"),
                CreateTableCell(["単杭沈下量", "(常時)", "[mm]"], fontSize, "center"),
                CreateTableCell(["群杭沈下量", "[mm]"], fontSize, "center"),
                CreateTableCell(["合計沈下量", "[mm]"], fontSize, "center")
            );
            table.Append(headerRow);

            // データ行
            foreach (var pli in pileLayoutItems)
            {
                bool hasSingle = PileDesign.Services.SettlementDeformationAngleEvaluator.HasSinglePileSettlement(inputModel, pli);
                var groupValues = inputModel.PileGroupSettlement?.ActiveRecord?.PileSettlements_mm;
                double groupMm = 0;
                bool hasGroup = groupValues?.TryGetValue(pli.PileNo, out groupMm) == true;
                var values = PileDesign.Services.PileSettlementTableValues.Build(
                    hasSingle, pli.SinglePileSettlementVL, hasGroup, groupMm);
                static string Display(double? value) => value.HasValue ? value.Value.ToString("N3") : "—";

                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{pli.PileNo}"], fontSize, "center"));
                dataRow.Append(CreateTableCell([$"{pli.Point3D.X:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pli.Point3D.Y:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([Display(values.SingleMm)], fontSize, "right"));
                dataRow.Append(CreateTableCell([Display(values.GroupMm)], fontSize, "right"));
                dataRow.Append(CreateTableCell([Display(values.TotalMm)], fontSize, "right"));
                table.Append(dataRow);
            }

            body.Append(table);
            AddTableNote(body, "※ — は未計算または結果なし。単杭・群杭の両結果がある場合だけ合計を表示する。");

            // どの列を設計値として扱うかは基本設定で決まる。表に 3 列並ぶだけでは読み手が判断できない。
            bool includesGroup = inputModel.FundamentalInput?.SettlementDesignIncludesGroup ?? true;
            AddTableNote(body,
                "※ 沈下検討の対象は基本設定で「"
                + (inputModel.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下")
                + "」に設定されている。設計値は"
                + (includesGroup ? "合計沈下量" : "単杭沈下量")
                + "の列。杭頭変形角の検定もこの値の差で求めている。");

            // 旧形式から復元した群杭沈下量は、計算して得た値と区別して書く
            if (inputModel.PileGroupSettlement?.ActiveRecord?.IsConvergenceUnknown == true)
                AddTableNote(body, "※ " + PileDesign.Services.LegacySettlementMigration.UnknownConvergenceReason + "。");
        }

        /// <summary>
        /// 基礎梁考慮鉛直解析結果のテーブルを出力
        /// </summary>
        private void AddVerticalBeamResultTables(Body body)
        {
            var caseResults = _source.VerticalBeamCaseResults;
            if (caseResults == null || caseResults.Count == 0) return;

            double fontSize = 8;

            foreach (var caseResult in caseResults)
            {
                // 杭反力・沈下量テーブル
                if (caseResult.PileResults != null && caseResult.PileResults.Count > 0)
                {
                    AddLineBreak(body);
                    AddAutoFigureCaption(body, $"単杭沈下解析（基礎梁考慮） 杭反力・沈下量（{caseResult.LoadCaseName}）", "表");

                    Table pileTable = CreateTableWithBorders();
                    TableRow pileHeader = CreateHeaderRow(
                        CreateTableCell(["杭No"], fontSize, "center"),
                        CreateTableCell(["X", "[m]"], fontSize, "center"),
                        CreateTableCell(["Y", "[m]"], fontSize, "center"),
                        CreateTableCell(["入力荷重", "[kN]"], fontSize, "center"),
                        CreateTableCell(["反力", "[kN]"], fontSize, "center"),
                        CreateTableCell(["沈下量", "[mm]"], fontSize, "center")
                    );
                    pileTable.Append(pileHeader);

                    foreach (var pr in caseResult.PileResults)
                    {
                        TableRow row = new();
                        row.Append(CreateTableCell([$"{pr.PileNo}"], fontSize, "center"));
                        row.Append(CreateTableCell([$"{pr.X:N3}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{pr.Y:N3}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{pr.InputLoad_kN:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{pr.Reaction_kN:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{pr.Settlement_mm:N3}"], fontSize, "right"));
                        pileTable.Append(row);
                    }
                    body.Append(pileTable);
                }

                // 梁応力テーブル
                if (caseResult.BeamResults != null && caseResult.BeamResults.Count > 0)
                {
                    AddLineBreak(body);
                    AddAutoFigureCaption(body, $"単杭沈下解析（基礎梁考慮） 梁応力（{caseResult.LoadCaseName}）", "表");

                    Table beamTable = CreateTableWithBorders();
                    TableRow beamHeader = CreateHeaderRow(
                        CreateTableCell(["梁名"], fontSize, "center"),
                        CreateTableCell(["N<_i>", "[kN]"], fontSize, "center"),
                        CreateTableCell(["Q<_zi>", "[kN]"], fontSize, "center"),
                        CreateTableCell(["M<_yi>", "[kNm]"], fontSize, "center"),
                        CreateTableCell(["N<_j>", "[kN]"], fontSize, "center"),
                        CreateTableCell(["Q<_zj>", "[kN]"], fontSize, "center"),
                        CreateTableCell(["M<_yj>", "[kNm]"], fontSize, "center")
                    );
                    beamTable.Append(beamHeader);

                    foreach (var br in caseResult.BeamResults)
                    {
                        TableRow row = new();
                        row.Append(CreateTableCell([$"{br.BeamName}"], fontSize, "center"));
                        row.Append(CreateTableCell([$"{br.Ni:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{br.Qzi:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{br.Myi:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{br.Nj:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{br.Qzj:N1}"], fontSize, "right"));
                        row.Append(CreateTableCell([$"{br.Myj:N1}"], fontSize, "right"));
                        beamTable.Append(row);
                    }
                    body.Append(beamTable);
                }

                // 収束情報
                if (caseResult.StepResults != null && caseResult.StepResults.Count > 0)
                {
                    var lastStep = caseResult.StepResults[^1];
                    AddText(body,
                        $"収束状態: {(caseResult.IsConverged ? "収束" : "未収束")}　" +
                        $"ステップ数: {lastStep.Step}　反復回数: {lastStep.Iterations}　残差: {lastStep.Residual:E2}",
                        "left");
                }
            }
        }

        // 杭伏図ダイヤグラム挿入メソッド
        private void AddPilingLayoutPileTopMomentDiagramByMm(
            MainDocumentPart mainDocumentPart,
            Body body,
            double widthMm,
            double heightMm,
            LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction
            )
        {
            string fileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            SavePilingLayoutDiagramByMm(
                fileName,
                widthMm,
                heightMm,
                pileLayoutItem => GetPileTopMoment(pileLayoutItem, loadCase, loadCombination, isLiquefaction)
                );
            WordDocumentUtils.AddImageToBodyByMm(mainDocumentPart, body, fileName, widthMm, heightMm);
            if (File.Exists(fileName)) { File.Delete(fileName); }
        }

        private string GetPileTopMoment(PileLayoutDataItem pileLayoutItem, LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction)
        {
            string mark = $"{pileLayoutItem.No}";
            void AddPileTopMoments(ObservableCollection<LoadCase> loadCases)
            {
                if (pileLayoutItem.Beams == null || pileLayoutItem.Beams.Count == 0) return;
                var beam = pileLayoutItem.Beams[0];
                if (beam?.BeamResults == null) return;

                if (loadCases == null) return;
                int cnt = Math.Min(beam.BeamResults.Count, loadCases.Count);
                for (int i = 0; i < cnt; i++)
                {
                    var lc = loadCases[i];
                    if (lc?.IsApplicable != true) continue;

                    var res = GetBeamResultCached(beam, lc, loadCombination, isLiquefaction)?.CumulativeForce;
                    if (res == null) continue;

                    mark += $"\n{res.Myi:N0}";
                    mark += $"\n{res.Mzi:N0}";
                }
            }
            AddPileTopMoments(inputModel.LoadCasesInput.LoadCasesLevel1);
            AddPileTopMoments(inputModel.LoadCasesInput.LoadCasesLevel2);
            return mark;
        }
        //{
        //    string mark = $"{pileLayoutItem.No}";

        //    void AddPileTopMoments(ObservableCollection<LoadCase> loadCases)
        //    {
        //        var beam = pileLayoutItem.Beams[0];
        //        int cnt = Math.Min(beam.BeamResults.Count, loadCases.Count);
        //        for (int i = 0; i < cnt; i++)
        //        {
        //            var lc = loadCases[i];
        //            if (!lc.IsApplicable) continue;

        //            var res = beam.GetBeamResult(anaModel, lc, loadCombination, isLiquefaction).CumulativeForce;
        //            mark += $"\n{res.Myi:N0}";
        //            mark += $"\n{res.Mzi:N0}";
        //        }
        //    }

        //    AddPileTopMoments(inputModel.LoadCasesInput.LoadCasesLevel1);
        //    AddPileTopMoments(inputModel.LoadCasesInput.LoadCasesLevel2);

        //    return mark;
        //}



        // 図タイトル
        public static void AddFigureTitle(Body body, string title, int figureNumber, double fontSize = 10.5)
        {
            string caption = $"図{figureNumber} {title}";
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Center },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Caption" }
                }
            };
            Run run = new()
            {
                RunProperties = new RunProperties
                {
                    FontSize = new FontSize { Val = (fontSize * 2).ToString() }
                }
            };
            run.Append(new Text(caption));
            paragraph.Append(run);
            body.Append(paragraph);
        }

        // ある範囲内の、ある数の倍数のリストを返すメソッド（メモリ描画）
        static List<double> GetMultiplesInRange(double min, double max, double unit)
        {
            var result = new List<double>();
            // unitXの最初の倍数（minX以上）
            double start = Math.Ceiling(min / unit) * unit;
            for (double x = start; x <= max + 1e-8; x += unit) // 誤差対策で+1e-8
            {
                result.Add(Math.Round(x, 8)); // 丸め誤差対策
            }
            // 最大値がmaxより小さい場合、その値+unitを追加
            if (result.Count > 0 && result[^1] < max)
            {
                result.Add(Math.Round(result[^1] + unit, 8));
            }
            return result;
        }

        // 杭伏図への基本情報の追加メソッド
        private string GetPileBasicMark(PileLayoutDataItem pileLayoutItem)
        {
            string mark = $"{pileLayoutItem.No}\n" +
                        $"杭体No:{pileLayoutItem.PileBodyNo}\n" +
                        $"地盤No:{pileLayoutItem.GroundNo}\n" +
                        $"({pileLayoutItem.X:N2},{pileLayoutItem.Y:N2},{pileLayoutItem.Z:N2})\n" +
                        $"R/B:{pileLayoutItem.PileSpacingFactor:N2}\n" +
                        $"ξ:{pileLayoutItem.GroupPileFactor:N2}";
            return mark;
        }


        // 杭頂部の力学値（曲げモーメント・せん断力）を取得する汎用メソッド
        private string GetPileTopForceMark(
            PileLayoutDataItem pileLayoutItem,
            Func<BeamForce, double> valueSelector,
            string title = "")
        {
            string mark = $"{pileLayoutItem.No}";

            if (pileLayoutItem?.Beams == null || pileLayoutItem.Beams.Count == 0)
                return mark;

            var beam = pileLayoutItem.Beams[0];
            if (beam == null)
                return mark;

            var loadCasesInput = inputModel?.LoadCasesInput;
            if (loadCasesInput == null)
                return mark;

            var level1 = loadCasesInput.LoadCasesLevel1 ?? [];
            var level2 = loadCasesInput.LoadCasesLevel2 ?? [];
            var allCombinations = loadCasesInput.AllLoadCombinations;

            var combos = (allCombinations != null && allCombinations.Count > 0)
                ? [.. allCombinations.Cast<LoadCombination?>()]
                : new List<LoadCombination?>() { null };

            // 各レベルについて先頭4ケース（存在する分）を列挙し、各ケースで値を求める
            void AppendLevelCases(IEnumerable<LoadCase> loadCases, string levelLabel)
            {
                mark += $"\n{levelLabel}";
                var lcList = loadCases?.ToList() ?? [];
                int casesToShow = Math.Min(4, lcList.Count);

                for (int idx = 0; idx < casesToShow; idx++)
                {
                    var lc = lcList[idx];
                    if (lc == null || !lc.IsApplicable)
                    {
                        mark += $"\nケース{idx + 1}: -";
                        continue;
                    }

                    double maxLiq = double.NegativeInfinity;
                    double maxNonLiq = double.NegativeInfinity;

                    foreach (var comb in combos)
                    {
                        // 液状化あり / なし 両方チェックし、それぞれで最大を取る
                        try
                        {
                            var resL = GetBeamResultCached(beam, lc, comb, true)?.CumulativeForce;
                            if (resL != null)
                            {
                                double val = valueSelector(resL);
                                if (!double.IsNaN(val))
                                    maxLiq = Math.Max(maxLiq, val);
                            }
                        }
                        catch { /* 念のため無視 */ }

                        try
                        {
                            var resN = GetBeamResultCached(beam, lc, comb, false)?.CumulativeForce;
                            if (resN != null)
                            {
                                double val = valueSelector(resN);
                                if (!double.IsNaN(val))
                                    maxNonLiq = Math.Max(maxNonLiq, val);
                            }
                        }
                        catch { /* 念のため無視 */ }
                    }

                    // 表示値決定ルール
                    double? chosen = null;
                    if (!double.IsNegativeInfinity(maxLiq) && !double.IsNegativeInfinity(maxNonLiq))
                        chosen = Math.Max(maxLiq, maxNonLiq);
                    else if (!double.IsNegativeInfinity(maxLiq))
                        chosen = maxLiq;
                    else if (!double.IsNegativeInfinity(maxNonLiq))
                        chosen = maxNonLiq;

                    if (chosen.HasValue)
                        mark += $"\nケース{idx + 1}: {chosen.Value:N1}";
                    else
                        mark += $"\nケース{idx + 1}: -";
                }

                // 足りないケースがあれば "-" で埋める
                for (int idx = lcList.Count; idx < 4; idx++)
                {
                    mark += $"\nケース{idx + 1}: -";
                }
            }

            AppendLevelCases(level1, "レベル1");
            AppendLevelCases(level2, "レベル2");

            return mark;
        }

        // 杭伏図への曲げモーメント情報の追加メソッド
        private string GetPileTopBendingMomentMark(PileLayoutDataItem pileLayoutItem)
        {
            return GetPileTopForceMark(pileLayoutItem, force => force.Mi);
        }


        // 杭伏図への線打力情報の追加メソッド
        private string GetPileTopShearForceMark(PileLayoutDataItem pileLayoutItem)
        {
            return GetPileTopForceMark(pileLayoutItem, force => force.Fi);
        }


        // 杭伏図への軸力情報の追加メソッド
        private string GetPileAxialForceMark(PileLayoutDataItem pileLayoutItem)
        {
            string mark = $"{pileLayoutItem.No}\n" +
                          $"VL0:{pileLayoutItem.AxialForceVL0:N1}\n" +
                          $"VLadd:{pileLayoutItem.AxialForceVLAdditional:N1}\n" +
                          $"VL:{pileLayoutItem.AxialForceVL0 + pileLayoutItem.AxialForceVLAdditional:N1}";

            void AddLoads(ObservableCollection<LoadCase> loadCases, ObservableCollection<double> axialLoads)
            {
                if (loadCases == null || axialLoads == null) return;
                int cnt = Math.Min(loadCases.Count, axialLoads.Count);
                for (int i = 0; i < cnt; i++)
                {
                    var lc = loadCases[i];
                    if (lc?.IsApplicable != true) continue;
                    double axialForce = axialLoads[i];
                    mark += $"\n{lc.LoadName}:{axialForce:N1}";
                }
            }

            AddLoads(inputModel.LoadCasesInput.LoadCasesLevel1, pileLayoutItem.AxialForceLevel1s);
            AddLoads(inputModel.LoadCasesInput.LoadCasesLevel2, pileLayoutItem.AxialForceLevel2s);

            return mark;
        }

        // 前方杭後方杭情報の追加メソッド
        private string GetPileIsFront(PileLayoutDataItem pileLayoutItem)
        {
            string mark = $"{pileLayoutItem.No}";

            void AddIsFronts(ObservableCollection<LoadCase> loadCases)
            {
                if (loadCases == null || pileLayoutItem.IsFrontPiles == null) return;
                int cnt = Math.Min(pileLayoutItem.IsFrontPiles.Count, loadCases.Count);
                for (int i = 0; i < cnt; i++)
                {
                    var lc = loadCases[i];
                    if (lc?.IsApplicable != true) continue;
                    bool isFrontPile = pileLayoutItem.IsFrontPiles[i];
                    mark += $"\n{lc.LoadName}:{(isFrontPile ? "前方杭" : "後方杭")}";
                }
            }

            AddIsFronts(inputModel.LoadCasesInput.LoadCasesLevel1);
            AddIsFronts(inputModel.LoadCasesInput.LoadCasesLevel2);

            return mark;
        }



        /// <summary>
        /// ジグザグの斜辺長（最大振幅点と最小振幅点の間の距離）で指定するバージョン
        /// </summary>
        public static void DrawSpringZigzagBySegmentLength(
            DrawingContext dc,
            Point start,
            Point end,
            int zigzagCount = 8,
            double zigzagSegmentLength = 30, // 斜辺長
            Pen pen = null,
            double endSegmentLength = 20)
        {
            pen ??= new Pen(Brushes.Black, 2);

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = PileDesign.Common.StableNumerics.Norm(dx, dy);

            if (length < 2 * endSegmentLength || zigzagCount < 1)
            {
                dc.DrawLine(pen, start, end);
                return;
            }

            double ux = dx / length;
            double uy = dy / length;

            Point zigzagStart = new(start.X + ux * endSegmentLength, start.Y + uy * endSegmentLength);
            Point zigzagEnd = new(end.X - (ux * endSegmentLength), end.Y - (uy * endSegmentLength));

            dc.DrawLine(pen, start, zigzagStart);
            dc.DrawLine(pen, zigzagEnd, end);

            double zigzagLength = length - 2 * endSegmentLength;
            double step = zigzagLength / zigzagCount;

            // 振幅Aを斜辺長Lとstepから計算
            // L^2 = S^2 + (2A)^2 → A = sqrt((L^2 - S^2)/4)
            double amplitude = Math.Sqrt(Math.Max(zigzagSegmentLength * zigzagSegmentLength - step * step, 0)) / 2;

            double nx = -uy;
            double ny = ux;

            var points = new List<Point>
            {
                zigzagStart
            };

            for (int i = 1; i < zigzagCount; i++)
            {
                double t = i * step;
                double px = zigzagStart.X + ux * t;
                double py = zigzagStart.Y + uy * t;
                double offset = ((i % 2 == 0) ? -1 : 1) * amplitude;
                points.Add(new Point(px + nx * offset, py + ny * offset));
            }
            points.Add(zigzagEnd);

            for (int i = 0; i < points.Count - 1; i++)
            {
                dc.DrawLine(pen, points[i], points[i + 1]);
            }
        }

        /// <summary>
        /// ばねを模式的に表すジグザグのポリラインを描画します。
        /// 両端に直線部分（長さendSegmentLength）を設け、その間をジグザグにします。
        /// </summary>
        /// <param name="dc">DrawingContext</param>
        /// <param name="start">開始点</param>
        /// <param name="end">終了点</param>
        /// <param name="zigzagCount">ジグザグの数</param>
        /// <param name="amplitude">ジグザグの振幅（上下幅）</param>
        /// <param name="pen">線のペン</param>
        /// <param name="endSegmentLength">両端の直線部分の長さ（ピクセル等）</param>
        public static void DrawSpringZigzag(
            DrawingContext dc,
            Point start,
            Point end,
            int zigzagCount = 8,
            double amplitude = 10,
            Pen pen = null,
            double endSegmentLength = 20)
        {
            pen ??= new Pen(Brushes.Black, 2);

            // 線分の方向ベクトル
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = PileDesign.Common.StableNumerics.Norm(dx, dy);

            if (length < 2 * endSegmentLength || zigzagCount < 1)
            {
                // 全体が短すぎる場合は直線のみ描画
                dc.DrawLine(pen, start, end);
                return;
            }

            // 単位ベクトル
            double ux = dx / length;
            double uy = dy / length;

            // ジグザグ開始・終了点
            Point zigzagStart = new(start.X + ux * endSegmentLength, start.Y + uy * endSegmentLength);
            Point zigzagEnd = new(end.X - (ux * endSegmentLength), end.Y - (uy * endSegmentLength));

            // 両端の直線部分
            dc.DrawLine(pen, start, zigzagStart);
            dc.DrawLine(pen, zigzagEnd, end);

            // ジグザグ部分
            double zigzagLength = length - 2 * endSegmentLength;
            double step = zigzagLength / zigzagCount;

            // 垂直方向ベクトル（正規化）
            double nx = -uy;
            double ny = ux;

            var points = new List<Point>
            {
                zigzagStart
            };

            for (int i = 1; i < zigzagCount; i++)
            {
                double t = i * step;
                double px = zigzagStart.X + ux * t;
                double py = zigzagStart.Y + uy * t;
                double offset = ((i % 2 == 0) ? -1 : 1) * amplitude;
                points.Add(new Point(px + nx * offset, py + ny * offset));
            }
            points.Add(zigzagEnd);

            // ポリライン描画
            for (int i = 0; i < points.Count - 1; i++)
            {
                dc.DrawLine(pen, points[i], points[i + 1]);
            }
        }

        // 杭姿図を保存するメソッド
        // 杭配置図（杭伏図）を保存するメソッド
        public void SavePilingLayoutDiagramByMm(
            string filePath,
            double widthMm,
            double heightMm,
            Func<PileLayoutDataItem, string> markSelector, // 追加: テキスト生成関数
            int dpi = 192
        )
        {
            if (inputModel.PileLayoutItems == null || inputModel.PileLayoutItems.Count == 0)
                return;
            //int dpi = 96;
            double gridBandWidth = 10; // m
            double unitX = 5; // m
            double unitY = 5; // m
            double tickLength = 1; // m
            double tickWidth = 1; // m
            double pileWidth = 1; // m
            double symbolCircleDiaInPixel = 20; // 
            double symbolTextHeight = symbolCircleDiaInPixel * 0.5;

            // mm→ピクセル変換
            //int widthPx = (int)Math.Round(widthMm * dpi / 25.4);
            //int heightPx = (int)Math.Round(heightMm * dpi / 25.4);
            int widthPx = MmToPx(widthMm, dpi);
            int heightPx = MmToPx(heightMm, dpi);

            var maxX = double.MinValue;
            var minX = double.MaxValue;
            var maxY = double.MinValue;
            var minY = double.MaxValue;



            foreach (var pileLayoutItem in inputModel.PileLayoutItems)
            {
                var locX = pileLayoutItem.Point3D.X;
                var locY = pileLayoutItem.Point3D.Y;
                var dia = inputModel.RequirePileBody(pileLayoutItem).PileBodySegments[0].PileSection?.PileDiameter ?? 0;
                maxX = Math.Max(maxX, locX);
                minX = Math.Min(minX, locX);
                maxY = Math.Max(maxY, locY);
                minY = Math.Min(minY, locY);
            }

            double midX = (maxX + minX) * 0.5; // 中央X座標
            double midY = (maxY + minY) * 0.5; // 中央Y座標

            if (Math.Abs(maxX - minX) < 1e-9) { maxX += 1; minX -= 1; }
            if (Math.Abs(maxY - minY) < 1e-9) { maxY += 1; minY -= 1; }

            double scale = Math.Min(
                widthPx / (maxX - minX + 2 * gridBandWidth),
                heightPx / (maxY - minY + 2 * gridBandWidth)); // pixel / m

            double symbolCircleDia = symbolCircleDiaInPixel / scale; // m

            // ローカル関数で座標変換
            System.Windows.Point ToImagePoint(double x, double y)
            {
                double px = widthPx * 0.5 + (x - midX) * scale;
                double py = heightPx * 0.5 - (y - midY) * scale; // Y軸反転
                return new System.Windows.Point(px, py);
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                // 杭の描画
                foreach (var pileLayoutItem in inputModel.PileLayoutItems)
                {
                    var locX = pileLayoutItem.Point3D.X;
                    var locY = pileLayoutItem.Point3D.Y;
                    var pileBody = inputModel.RequirePileBody(pileLayoutItem);
                    var dia = (pileBody.PileBodySegments[0].PileSection?.PileDiameter ?? 0) * 0.001;
                    var toeDia = pileBody.PileToeDia * 0.001;
                    // 円
                    dc.DrawEllipse(null, new Pen(Brushes.Blue, pileWidth),
                        ToImagePoint(locX, locY),
                        dia * scale * 0.5, dia * scale * 0.5);

                    string mark = markSelector(pileLayoutItem); // ここで切り替え

                    // 左揃え
                    WordDocumentUtils.DrawTextWithAlignment(dc, mark,
                        ToImagePoint(locX + dia * 0.5, locY + dia * 0.5), 10, Brushes.Black, null, "left", "top");
                }

                var tickXs = GetMultiplesInRange(minX, maxX, unitX);
                var tickYs = GetMultiplesInRange(minY, maxY, unitY);

                // メモリの描画(上)
                foreach (var x in tickXs)
                {
                    dc.DrawLine(new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(x, maxY + gridBandWidth),
                        ToImagePoint(x, maxY + gridBandWidth - tickLength));
                    // 左揃え
                    WordDocumentUtils.DrawTextWithAlignment(dc, $"{x}",
                        ToImagePoint(x, maxY + gridBandWidth), 10, Brushes.Black, null, "left", "top");
                }

                // メモリの描画(左)
                foreach (var y in tickYs)
                {
                    dc.DrawLine(new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(-gridBandWidth, y),
                        ToImagePoint(-gridBandWidth + tickLength, y));
                    WordDocumentUtils.DrawTextWithAlignment(dc, $"{y}",
                        ToImagePoint(-gridBandWidth, y), 10, Brushes.Black, null, "left", "bottom");
                }

                // 一点鎖線: 長い線(6), 空白(2), 短い線(1), 空白(2) の繰り返し
                var dashStyle = new DashStyle([10, 2, 1, 2], 0); /// DashStyle([6, 2, 1, 2], 0);
                var dashedPen = new Pen(Brushes.Gray, tickWidth) { DashStyle = dashStyle };

                // gridXの描画 (下)
                foreach (var x in inputModel.GridXItems)
                {
                    dc.DrawEllipse(null, new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(x.Coord, -gridBandWidth + symbolCircleDia * 0.5),
                        symbolCircleDiaInPixel * 0.5, symbolCircleDiaInPixel * 0.5);
                    WordDocumentUtils.DrawTextWithAlignment(dc, $"{x.Name}",
                        ToImagePoint(x.Coord, -gridBandWidth + symbolCircleDia * 0.5),
                        symbolTextHeight, Brushes.Black, null, "center", "center");
                    dc.DrawEllipse(null, new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(x.Coord, -gridBandWidth + symbolCircleDia * 1.5),
                        symbolCircleDiaInPixel * 0.05, symbolCircleDiaInPixel * 0.05);
                    dc.DrawLine(dashedPen,
                        ToImagePoint(x.Coord, maxY + gridBandWidth - symbolCircleDia * 1.5),
                        ToImagePoint(x.Coord, -gridBandWidth + symbolCircleDia * 1.5));
                    // 寸法
                    if (x.Spacing > 0.01)
                    {
                        WordDocumentUtils.DrawTextWithAlignment(dc, $"{x.Spacing:N3}",
                            ToImagePoint(x.Coord - x.Spacing * 0.5, -gridBandWidth + symbolCircleDia * 1.5),
                            symbolTextHeight, Brushes.Black, null, "center", "bottom");
                        // 寸法線
                        dc.DrawLine(new Pen(Brushes.Gray, tickWidth),
                            ToImagePoint(x.Coord, -gridBandWidth + symbolCircleDia * 1.5),
                            ToImagePoint(x.Coord - x.Spacing, -gridBandWidth + symbolCircleDia * 1.5));
                    }
                }

                // gridYの描画(右)
                foreach (var y in inputModel.GridYItems)
                {
                    dc.DrawEllipse(null, new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 0.5, y.Coord),
                        symbolCircleDiaInPixel * 0.5, symbolCircleDiaInPixel * 0.5);
                    WordDocumentUtils.DrawTextWithAlignment(dc, $"{y.Name}",
                        ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 0.5, y.Coord),
                        symbolTextHeight, Brushes.Black, null, "center", "center");
                    dc.DrawEllipse(null, new Pen(Brushes.Gray, tickWidth),
                        ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 1.5, y.Coord),
                        symbolCircleDiaInPixel * 0.05, symbolCircleDiaInPixel * 0.05);
                    dc.DrawLine(dashedPen,
                        ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 1.5, y.Coord),
                        ToImagePoint(-gridBandWidth + symbolCircleDia * 1.5, y.Coord));
                    // 寸法
                    if (y.Spacing > 0.01)
                    {
                        WordDocumentUtils.DrawTextWithAlignment(dc, $"{y.Spacing:N3}",
                            ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 1.5, y.Coord - y.Spacing * 0.5),
                            symbolTextHeight, Brushes.Black, null, "center", "bottom", -90);
                        // 寸法線
                        dc.DrawLine(new Pen(Brushes.Gray, tickWidth),
                            ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 1.5, y.Coord),
                            ToImagePoint(maxX + gridBandWidth - symbolCircleDia * 1.5, y.Coord - y.Spacing));
                    }
                }
            }
            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            // bmpは、AddImageToBodyByMmによるWord貼りこみ時の不具合を避けるため、96dpi固定で作成する必要があります。

            bmp.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            // 書き終えてから差し替える (計算書に貼る一時ファイルだが、書き出しはすべてこの形にそろえる)
            PileDesign.Services.FileOperationService.WriteAtomically(filePath, fs => encoder.Save(fs));
        }

        // ダイアグラム保存
    }
}
