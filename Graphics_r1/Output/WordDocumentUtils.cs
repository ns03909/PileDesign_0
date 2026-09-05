using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PileDesign.Services;

namespace PileDesign.Output
{

    public static class WordDocumentUtils
    {
        /// <summary>
        /// FormattedText 用の PixelsPerDip を安全に返す。
        ///
        /// <c>Application.Current.MainWindow</c> はそれを開いたスレッドからしか触れない。
        /// 別スレッドから <c>VisualTreeHelper.GetDpi(...)</c> を呼ぶとスレッド親和性の違反となり、
        /// 例外ではなくプロセスごと落ちることがある。アクセスできないときは 1.0 を返す。
        /// </summary>
        private static double SafePixelsPerDip()
        {
            try
            {
                var w = Application.Current?.MainWindow;
                if (w != null && w.CheckAccess())
                    return VisualTreeHelper.GetDpi(w).PixelsPerDip;
            }
            catch { /* 取得できないときは既定値 */ }
            return 1.0;
        }


        public enum DiagramAlignment
        {
            Left,
            Center,
            Right
        }

        public static JustificationValues GetJustification(DiagramAlignment alignment)
        {
            return alignment switch
            {
                DiagramAlignment.Left => JustificationValues.Left,
                DiagramAlignment.Center => JustificationValues.Center,
                DiagramAlignment.Right => JustificationValues.Right,
                _ => JustificationValues.Center
            };
        }

        public static void AddImageToBodyByMm(
            MainDocumentPart mainPart,
            Body body,
            string imagePath,
            double widthMm,
            double heightMm,
            DiagramAlignment alignment = DiagramAlignment.Center // 追加
            )
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    Serilog.Log.Warning("[WordDocumentUtils] 画像ファイルが見つかりません（図をスキップ）: {ImagePath}", imagePath);
                    PileDesign.Services.MessageService.Show($"画像ファイルが見つかりません: {imagePath}");
                    return;
                }
                // mm→ピクセル変換
                //int widthPx = (int)Math.Round(widthMm * dpi / 25.4);
                //int heightPx = (int)Math.Round(heightMm * dpi / 25.4);

                // 1インチ = 914400 EMU
                // 1インチ = 25.4 mm、
                // 1 mm = 914400 / 25.4 = 36000 EMU

                // mm→EMU変換
                long widthEmu = (long)(widthMm * 36_000);// * 96 / dpi);
                long heightEmu = (long)(heightMm * 36_000);// * 96 / dpi);

                var imagePart = mainPart.AddImagePart(
                    imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        ? ImagePartType.Png
                        : ImagePartType.Jpeg
                );

                using (FileStream stream = new(imagePath, FileMode.Open, FileAccess.Read))
                {
                    imagePart.FeedData(stream);
                }

                string relationshipId = mainPart.GetIdOfPart(imagePart);

                var element = new DocumentFormat.OpenXml.Wordprocessing.Drawing(
                    new Inline(
                        new Extent() { Cx = widthEmu, Cy = heightEmu },
                        new EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                        new DocProperties() { Id = (UInt32Value)1U, Name = System.IO.Path.GetFileName(imagePath) },
                        new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(new GraphicFrameLocks() { NoChangeAspect = true }),
                        new Graphic(
                            new GraphicData(
                                new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties() { Id = (UInt32Value)0U, Name = System.IO.Path.GetFileName(imagePath) },
                                        new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()
                                    ),
                                    new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                        new DocumentFormat.OpenXml.Drawing.Blip() { Embed = relationshipId, CompressionState = DocumentFormat.OpenXml.Drawing.BlipCompressionValues.Print },
                                        new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())
                                    ),
                                    new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                        new DocumentFormat.OpenXml.Drawing.Transform2D(
                                            new DocumentFormat.OpenXml.Drawing.Offset() { X = 0L, Y = 0L },
                                            new DocumentFormat.OpenXml.Drawing.Extents() { Cx = widthEmu, Cy = heightEmu }
                                        ),
                                        new DocumentFormat.OpenXml.Drawing.PresetGeometry(new DocumentFormat.OpenXml.Drawing.AdjustValueList()) { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }
                                    )
                                )
                            )
                            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                        )
                    )
                );

                // 配置指定付きの段落で画像を追加
                var paragraph = new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.Justification() { Val = GetJustification(alignment) }
                    ),
                    new DocumentFormat.OpenXml.Wordprocessing.Run(element)
                );

                body.AppendChild(paragraph);
            }
            catch (Exception ex)
            {
                // 図 1 枚の失敗で計算書全体を止めない。詳細はログに残す
                // (以前は例外オブジェクト全体をスタックトレース込みでダイアログに出していた)。
                Serilog.Log.Warning(ex, "[WordDocumentUtils] 画像挿入に失敗（図をスキップ）: {ImagePath}", imagePath);
                PileDesign.Services.MessageService.Show(
                    "図の挿入に失敗したため、その図を省いて出力を続けます。\n"
                    + $"詳細はログ ({PileDesign.Common.Logging.AppLog.LogDirectory}) を確認してください。",
                    "計算書出力", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// DrawingContextにテキストを描画（中央・右揃え、複数行対応）
        /// </summary>
        /// <param name="dc">DrawingContext</param>
        /// <param name="text">描画するテキスト（\nで複数行可）</param>
        /// <param name="origin">基準位置（左上）</param>
        /// <param name="fontSize">フォントサイズ</param>
        /// <param name="brush">文字色</param>
        /// <param name="typeface">フォント</param>
        /// <param name="alignment">"left" "center" "right"</param>
        //public static void DrawTextWithAlignment_origin(
        //    DrawingContext dc,
        //    string text,
        //    System.Windows.Point origin,
        //    double fontSize = 16,
        //    Brush? brush = null,
        //    Typeface? typeface = null,
        //    string alignment = "left")
        //{
        //    brush ??= Brushes.Black;
        //    typeface ??= new Typeface("Meiryo");

        //    var lines = text.Replace("\r\n", "\n").Split('\n');
        //    double y = origin.Y;

        //    foreach (var line in lines)
        //    {
        //        var formattedText = new FormattedText(
        //            line,
        //            System.Globalization.CultureInfo.CurrentCulture,
        //            FlowDirection.LeftToRight,
        //            typeface,
        //            fontSize,
        //            brush,
        //            VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip
        //        );

        //        double x = origin.X;
        //        if (alignment.ToLower() == "center")
        //        {
        //            x -= formattedText.Width / 2;
        //        }
        //        else if (alignment.ToLower() == "right")
        //        {
        //            x -= formattedText.Width;
        //        }

        //        dc.DrawText(formattedText, new System.Windows.Point(x, y));
        //        y += formattedText.Height;
        //    }
        //}

        /// <summary>
        /// DrawingContextにテキストを描画（中央・右揃え、複数行対応）
        /// </summary>
        /// <param name="dc">DrawingContext</param>
        /// <param name="text">描画するテキスト（\nで複数行可）</param>
        /// <param name="origin">基準位置（左上）</param>
        /// <param name="fontSize">フォントサイズ</param>
        /// <param name="brush">文字色</param>
        /// <param name="typeface">フォント</param>
        /// <param name="alignment">"left" "center" "right"</param>
        public static void DrawTextWithAlignment(
            DrawingContext dc,
            string text,
            System.Windows.Point origin,
            double fontSize = 16,
            Brush? brush = null,
            Typeface? typeface = null,
            string alignment = "left",
            string verticalAlignment = "top",
            double angle = 0 // ← 追加：回転角度（度、時計回り）
        )
        {
            string rotateCenterH = alignment; // "left"|"center"|"right"|"origin"
            string rotateCenterV = verticalAlignment;  // "top"|"center"|"bottom"|"origin"


            brush ??= Brushes.Black;
            typeface ??= new Typeface("Meiryo");

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var formattedLines = new List<FormattedText>();
            double totalHeight = 0;

            // 各行のFormattedTextと高さ合計を取得
            foreach (var line in lines)
            {
                var ft = new FormattedText(
                    line,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush,
                    SafePixelsPerDip()
                );
                formattedLines.Add(ft);
                totalHeight += ft.Height;
            }

            double y = origin.Y;
            if (string.Equals(verticalAlignment, "center"))
                y -= totalHeight / 2;
            else if (string.Equals(verticalAlignment, "bottom"))
                y -= totalHeight;

            foreach (var ft in formattedLines)
            {
                double x = origin.X;
                if (string.Equals(alignment, "center"))
                    x -= ft.Width / 2;
                else if (string.Equals(alignment, "right"))
                    x -= ft.Width;

                // 回転中心の計算
                double cx = x, cy = y;
                if (string.Equals(rotateCenterH, "center"))
                    cx = x + ft.Width / 2;
                else if (string.Equals(rotateCenterH, "right"))
                    cx = x + ft.Width;
                // "left"または"origin"はxのまま

                if (string.Equals(rotateCenterV, "center"))
                    cy = y + ft.Height / 2;
                else if (string.Equals(rotateCenterV, "bottom"))
                    cy = y + ft.Height;
                // "top"または"origin"はyのまま

                if (Math.Abs(angle) > 1e-6)
                {
                    dc.PushTransform(new RotateTransform(angle, cx, cy));
                    dc.DrawText(ft, new System.Windows.Point(x, y));
                    dc.Pop();
                }
                else
                {
                    dc.DrawText(ft, new System.Windows.Point(x, y));
                }
                y += ft.Height;
            }
        }




        //// Y座標の調整
        //double y = origin.Y;
        //    if (verticalAlignment.ToLower() == "center")
        //    {
        //        y -= totalHeight / 2;
        //    }
        //    else if (verticalAlignment.ToLower() == "bottom")
        //    {
        //        y -= totalHeight;
        //    }
        //    // "top" の場合はそのまま

        //    // 各行を描画
        //    foreach (var ft in formattedLines)
        //    {
        //        double x = origin.X;
        //        if (alignment.ToLower() == "center")
        //        {
        //            x -= ft.Width / 2;
        //        }
        //        else if (alignment.ToLower() == "right")
        //        {
        //            x -= ft.Width;
        //        }

        //        if (Math.Abs(angle) > 1e-6)
        //        {
        //            // (x, y) を中心に回転
        //            dc.PushTransform(new RotateTransform(angle, x, y));
        //            dc.DrawText(ft, new System.Windows.Point(x, y));
        //            dc.Pop();
        //        }
        //        else
        //        {
        //            dc.DrawText(ft, new System.Windows.Point(x, y));
        //        }
        //        y += ft.Height;
        //        //dc.DrawText(ft, new System.Windows.Point(x, y));
        //        //y += ft.Height;
        //    }
        //}
    }
}
