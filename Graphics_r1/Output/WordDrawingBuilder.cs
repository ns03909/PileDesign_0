using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.IO;

namespace PileDesign.Output
{
    internal static class WordDrawingBuilder
    {
        // imageBytes: PNG bytes
        public static void AddPngBytesToBody(MainDocumentPart mainPart, Body body, byte[] imageBytes, double widthMm, double heightMm)
        {
            if (mainPart == null) throw new ArgumentNullException(nameof(mainPart));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (imageBytes == null || imageBytes.Length == 0) return;

            // Add image part and feed data
            var imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png);
            using (var ms = new MemoryStream(imageBytes))
            {
                imagePart.FeedData(ms);
            }
            string imagePartId = mainPart.GetIdOfPart(imagePart);

            // Create Drawing element (inline) - same structure as original CreateLoadCombinationDiagramDrawing used
            long cx = (long)Math.Round(widthMm * 36000.0); // EMU per mm = 36000
            long cy = (long)Math.Round(heightMm * 36000.0);

            var drawing = new DocumentFormat.OpenXml.Wordprocessing.Drawing(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = cx, Cy = cy },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties { Id = (UInt32Value)1U, Name = "Diagram" },
                    new DocumentFormat.OpenXml.Drawing.Graphic(
                        new DocumentFormat.OpenXml.Drawing.GraphicData(
                            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties { Id = (UInt32Value)0U, Name = "diagram.png" },
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()
                                ),
                                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                    new DocumentFormat.OpenXml.Drawing.Blip { Embed = imagePartId, CompressionState = DocumentFormat.OpenXml.Drawing.BlipCompressionValues.Print },
                                    new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())
                                ),
                                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                    new DocumentFormat.OpenXml.Drawing.Transform2D(
                                        new DocumentFormat.OpenXml.Drawing.Offset { X = 0L, Y = 0L },
                                        new DocumentFormat.OpenXml.Drawing.Extents { Cx = cx, Cy = cy }
                                    ),
                                    new DocumentFormat.OpenXml.Drawing.PresetGeometry(new DocumentFormat.OpenXml.Drawing.AdjustValueList()) { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }
                                )
                            )
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }
                    )
                )
            );

            // Append drawing as a Paragraph
            var para = new Paragraph(new Run(drawing));
            body.Append(para);
        }
    }
}