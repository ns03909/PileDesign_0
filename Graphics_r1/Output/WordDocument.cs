using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
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

namespace PileDesign.Output
{
    internal partial class WordDocument(InputModel _inputModel, AnaModel _anaModel, MainWindowViewModel _mainWindowViewModel)
    {
        // SEQ フィールドが Word の F9 更新に依存せず正しい番号を表示するよう、
        // コード側で番号を直接カウントして書き込む。
        private int _figureCounter = 0;
        private int _tableCounter = 0;

        private static class Layout
        {
            // ドキュメント全体の標準フォント
            public const string FontName = "ＭＳ ゴシック";

            // 段落/番号付きアウトライン（EnsureHeadingStylesWithNumbering 等）
            public const int OutlineIndentStepTwips = 420;
            public const int HangingIndentTwips = 420;
            public const int BulletIndentTwips = 360;

            // 図・グラフ標準寸法(mm)
            public const double FigureWidthMm = 150;
            public const double FigureHeightMm = 150;
            public const double LayoutDiagramWidthMm = 150;
            public const double LayoutDiagramHeightMm = 200;
            public const double PileElevationWidthMm = 150;
            public const double PileElevationHeightMm = 100;

            // 描画/画像出力
            public const double BaseDpi = 96.0;
            public const double HiResScale = 2.0;         // ScottPlot, 自前描画用倍密度
            public const double MinSpringLengthPx = 8.0;

            // 線種
            public const double LineWidthThick = 3.0;
            public const double LineWidthThin = 1.0;

            // 数値安定
            public const double EpsTiny = 1e-9;
            public const double EpsSmall = 1e-8;
            public const double DispSmallThreshold = 0.001;

            // スプリング/ジグザグ
            public const int SpringZigzagCount = 6;
            public const double SpringZigzagSegment = 18.0;

            // キャプション等
            public const double DefaultFontSizePt = 10.5;
        }

        private readonly MainWindowViewModel mainWindowViewModel = _mainWindowViewModel; // 追加

        private readonly InputModel inputModel = _inputModel;
        private readonly AnaModel anaModel = _anaModel;

        // FTPile有無判定ヘルパ
        private bool HasFTPile()
            => inputModel?.PileBodies != null
               && inputModel.PileBodies.Any(pb => pb?.PileTop?.FTPile != null);

        // FTPile有無判定ヘルパ
        private bool HasCaptainPile()
            => inputModel?.PileBodies != null
               && inputModel.PileBodies.Any(pb => pb?.PileTop?.CaptainPile != null);


        readonly double symbolDescTabPosition = 15; // mm
        // 共通ユーティリティ（単位換算）
        private const double Dpi = 96.0;
        private const double InchPerMm = 1.0 / 25.4;              // 1mm = 1/25.4 inch
        private const double TwipsPerInch = 1440.0;               // 1 inch = 1440 twips
        private const double EmuPerInch = 914400.0;               // 1 inch = 914400 EMU
        private static readonly double TwipsPerMm = TwipsPerInch * InchPerMm; // ≒56.692913
        private static readonly double EmuPerMm = EmuPerInch * InchPerMm;     // =36000

        // mm → px （任意 dpi / 任意スケール）
        private static int MmToPx(double mm, double dpi, double scale = 1.0)
            => (int)Math.Round(mm * dpi * scale * InchPerMm);

        // mm → twips （int）
        private static int MmToTwipsInt(double mm)
            => (int)Math.Round(mm * TwipsPerMm);

        // mm → twips （string）
        private static string MmToTwips(double mm)
            => MmToTwipsInt(mm).ToString();

        // mm → EMU
        private static long MmToEmu(double mm)
            => (long)Math.Round(mm * EmuPerMm);

        // インデント作成ヘルパー
        private static Indentation CreateIndentation(double leftIndentMm = 0, double firstLineIndentMm = 0, double hangingIndentMm = 0)
        {
            return new Indentation
            {
                Left = leftIndentMm != 0 ? MmToTwips(leftIndentMm) : null,
                FirstLine = firstLineIndentMm != 0 ? MmToTwips(firstLineIndentMm) : null,
                Hanging = hangingIndentMm != 0 ? MmToTwips(hangingIndentMm) : null
            };
        }

        /// <summary>標準フォントの RunFonts を生成</summary>
        private static RunFonts CreateDefaultRunFonts() => new()
        {
            Ascii = Layout.FontName,
            HighAnsi = Layout.FontName,
            EastAsia = Layout.FontName,
            ComplexScript = Layout.FontName
        };

        private static readonly Regex InlineMathRx = new(@"\$(.+?)\$", RegexOptions.Singleline | RegexOptions.Compiled);

        private static IEnumerable<OpenXmlElement> BuildInlineMixedRuns(string text, double fontSize)
        {
            int last = 0;
            foreach (Match m in InlineMathRx.Matches(text))
            {
                if (m.Index > last)
                    foreach (var r in ConvertStringToRunsWithSuperSub(text[last..m.Index], fontSize))
                        yield return r;
                yield return ParseTeXToOfficeMath(m.Groups[1].Value);
                last = m.Index + m.Length;
            }
            if (last < text.Length)
                foreach (var r in ConvertStringToRunsWithSuperSub(text[last..], fontSize))
                    yield return r;
        }

        // TeX パーサ自己テスト有効化フラグ
        private const bool RunTexParserSelfTest = false;

        // Word文書作成メソッド
        public void CreateWordDocument(InputModel inputModel, string fileName)
        {

            ArgumentNullException.ThrowIfNull(inputModel);
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is empty.", nameof(fileName));
            if (inputModel.ElementDivision == null)
                throw new InvalidOperationException("ElementDivision が未設定です。");

            try
            {
                using var wordDocument = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
                MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();

                EnsureHeadingStylesWithNumbering(mainPart);
                Document doc = new();
                Body body = new();

                // モデル図をキャプチャ（UIスレッド上で実行）
                byte[]? modelImageBytes = mainWindowViewModel?.CaptureIsometricModelImageBytes();

                AddFrontMatter(mainPart, body, inputModel, modelImageBytes);
                AddInputDataSection(mainPart, body, inputModel);


                AddLoadCombinationAndFigureSection(mainPart, body, inputModel);

                // まとめて追加
                doc.Append(body);
                mainPart.Document = doc;
                mainPart.Document.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Word 出力中にエラー: {ex.Message}");
                throw;
            }

            System.Diagnostics.Debug.WriteLine("Word文書を出力しました。開いて Ctrl+A → F9 でフィールド更新してください。");
        }


        // FrontMatter: タイトル・モデル図・目次・基本説明章
        private void AddFrontMatter(MainDocumentPart mainPart, Body body, InputModel model, byte[]? modelImageBytes = null)
        {
            AddText(body, $"杭検討プログラム ver {(System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString())}", "center");
            AddTitle(body, "基礎ぐいの検討書");

            // モデル図（アイソメトリック）
            if (modelImageBytes != null && modelImageBytes.Length > 0)
            {
                AddLineBreak(body);
                // PNGからピクセルサイズを読み取り、アスペクト比を維持して挿入
                // A4有効領域: 幅160mm(210-25*2), 高さ247mm(297-25*2)
                // タイトル(~18mm) + 副題(~6mm) + 改行(~5mm) + 図キャプション(~8mm) + 目次(~15mm) + 余白(~35mm) ≈ 87mm
                double maxWidthMm = 160;
                double maxHeightMm = 160; // 247 - 87 = 160mm（1ページに収まる上限）
                double imgWidthMm = maxWidthMm;
                double imgHeightMm = 100;
                using (var imgStream = new MemoryStream(modelImageBytes))
                {
                    var decoder = new PngBitmapDecoder(imgStream, BitmapCreateOptions.None, BitmapCacheOption.None);
                    if (decoder.Frames.Count > 0)
                    {
                        var frame = decoder.Frames[0];
                        double aspectRatio = (double)frame.PixelWidth / frame.PixelHeight;
                        imgHeightMm = imgWidthMm / aspectRatio;
                        // 高さが上限を超える場合、高さを制限して幅を縮小
                        if (imgHeightMm > maxHeightMm)
                        {
                            imgHeightMm = maxHeightMm;
                            imgWidthMm = maxHeightMm * aspectRatio;
                        }
                    }
                }
                WordDrawingBuilder.AddPngBytesToBody(mainPart, body, modelImageBytes, imgWidthMm, imgHeightMm);
                AddAutoFigureCaption(body, "モデル図（アイソメトリック）", "図");
            }

            // 目次
            AddTableOfContents(body, 3);
            AddPageBreak(body);

        }

        // 入力情報・表類
        private void AddInputDataSection(MainDocumentPart mainPart, Body body, InputModel inputModel)
        {
            AddText(body, $"杭検討プログラム ver {(System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString())}", "center");
            AddText(body, DateTime.Now.ToString("yyyy/MM/dd"), "center");

            AddHeader1(body, "基本設定", 1);

            AddFundamentalTable(body, inputModel.FundamentalInput); // 基本設定テーブル
            AddLineBreak(body);

            AddHeader1(body, "荷重条件", 1);
            AddText(body, "レベル1荷重");
            AddLoadCaseTable(body, inputModel.LoadCasesInput.LoadCasesLevel1);
            AddText(body, "レベル2荷重");
            AddLoadCaseTable(body, inputModel.LoadCasesInput.LoadCasesLevel2);
            AddLineBreak(body);

            AddHeader1(body, "杭体", 1);
            AddPileBodiesTables(body, inputModel.PileBodies);
            AddLineBreak(body);

            AddHeader1(body, "杭配置", 1);
            AddPileLayoutTables(body, inputModel.PileLayoutItems);
            AddLineBreak(body);

            AddHeader1(body, "杭軸力", 1);
            AddPileAxialLoadTables(body, inputModel.PileLayoutItems);
            AddLineBreak(body);

            AddHeader1(body, "前後方杭", 1);
            AddIsFrontPileTables(body, inputModel.PileLayoutItems);
            AddLineBreak(body);

            AddHeader1(body, "検討方針", 1);
            AddDesignApproachSection(body, inputModel);
            AddLineBreak(body);


            if (mainWindowViewModel.IncludeGroundInformation) // 地盤
            {
                AddHeader1(body, "地盤", 1);
                AddGroundInfo(body, inputModel.GroundsInput);
                AddLineBreak(body);
            }

            if (mainWindowViewModel.IncludeLiquefaction) // 液状化
            {
                // 液状化の検討
                AddLiquefactionSection(body);
                AddLineBreak(body);
            }

            if (mainWindowViewModel.IncludeVertical) // 鉛直解析
            {
                if (mainWindowViewModel.IsVerticalAnalysisDone)
                {
                    AddHeader1(body, "杭の支持力", 1);
                    AddPileResistanceDescription(body, inputModel.ElementDivision.SoilPiles);
                    AddVerticalResistance(body, inputModel.ElementDivision.SoilPiles);
                    AddLineBreak(body);

                    // 杭の支持力検討
                    if (mainWindowViewModel.CalculationReportLevel >= 2)
                    {
                        AddSectionVerticalResistance(body);
                        AddLineBreak(body);
                    }

                    // 杭の沈下検討
                    if (mainWindowViewModel.CalculationReportLevel >= 2)
                    {
                        AddSectionSettlement(body);
                        AddLineBreak(body);
                    }
                    var soilPiles = inputModel.ElementDivision.SoilPiles;
                    if (soilPiles is { Count: > 0 })
                    {
                        var soilPile = soilPiles[0];
                        const double pileElevationH = 100;

                        AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "vertical");
                        AddAutoFigureCaption(body, "沈下解析杭モデル", "図");

                        AddSettlementGraph(mainPart, body);
                    }
                }
                else
                {
                    AddText(body, "（鉛直解析が未実施のため、支持力検討は省略されています）", "left");
                }
            }

            if (mainWindowViewModel.IncludeHorizontal) // 水平解析
            {
                // 根入部
                if (inputModel.EmbedmentInput is { EmbedmentLayersCount: > 0 })
                {
                    AddHeader1(body, "根入部", 1);
                    if (mainWindowViewModel.CalculationReportLevel >= 2)
                    {
                        AddEmbedment(body, inputModel.EmbedmentInput);
                        AddLineBreak(body);
                    }
                }
                // 地盤の水平変位
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddGroundDisplacementSection(body);
                    AddPageBreak(body);
                }
                // 杭の水平抵抗
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddSectionHorizontalResistance(body);
                    AddLineBreak(body);
                }
                AddPageBreak(body);
                AddHeader1(body, "上部構造、基礎部への作用の組み合わせ", 2);
                AddLoadCombinationTable(mainPart, body);
                var soilPiles = inputModel.ElementDivision.SoilPiles;
                if (soilPiles is { Count: > 0 })
                {
                    var soilPile = soilPiles[0];
                    const double pileElevationW = 150;
                    const double pileElevationH = 100;

                    AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "horizontal");
                    AddAutoFigureCaption(body, "水平抵抗解析杭モデル", "図");

                    if (mainWindowViewModel?.IsHorizontalAnalysisDone == true)
                    {
                        AddPileForceSummaryTable(mainPart, body);
                        AddNMinT(mainPart, body);
                        if (mainWindowViewModel.IncludeHorizontal_QNInT)
                            AddQNInT(mainPart, body);
                        if (mainWindowViewModel.IncludeHorizontal_MPhi)
                            AddMPhiCurves(mainPart, body);
                        if (mainWindowViewModel.IncludeHorizontal_MTheta)
                            AddMThetaCurves(mainPart, body);
                    }
                    else
                    {
                        AddText(body, "（水平解析が未実施のため、解析結果は省略されています）", "left");
                    }
                }

                // 基礎部材の強度と変形性能
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddSectionMemberCapacities(body);
                    AddLineBreak(body);
                }

            }
            // 全杭の応力ダイアグラム出力（曲げモーメント・せん断力）
            if ((mainWindowViewModel.IncludeHorizontal_Bending || mainWindowViewModel.IncludeHorizontal_Shear)
                && mainWindowViewModel?.IsHorizontalAnalysisDone == true && anaModel != null)
            {
                AddAllPileStressDiagrams(mainPart, body,
                    mainWindowViewModel.IncludeHorizontal_Bending,
                    mainWindowViewModel.IncludeHorizontal_Shear);
            }
            if (mainWindowViewModel.IncludePileLocationMap) // 杭配置マップ
            {
                double layoutW = 150; double layoutH = 200;
                AddPilingLayoutDiagramByMm(mainPart, body, widthMm: layoutW, heightMm: layoutH, GetPileBasicMark);
                AddAutoFigureCaption(body, "杭配置マップ", "図");
            }
            if (mainWindowViewModel.IncludePileAxialLoadMap) // 杭軸力マップ
            {
                double layoutW = 150; double layoutH = 200;
                AddPilingLayoutDiagramByMm(mainPart, body, widthMm: layoutW, heightMm: layoutH, GetPileAxialForceMark);
                AddAutoFigureCaption(body, "杭軸力マップ", "図");
            }
            if (mainWindowViewModel.IncludeIsFrontMap)  // 杭前後方杭マップ
            {
                double layoutW = 150; double layoutH = 200;
                AddPilingLayoutDiagramByMm(mainPart, body, widthMm: layoutW, heightMm: layoutH, GetPileIsFront);
                AddAutoFigureCaption(body, "杭前後方杭マップ", "図");
            }
            if (mainWindowViewModel.IncludePileHeadMomentMap)  // 杭頭モーメントマップ
            {
                double layoutW = 150; double layoutH = 200;
                AddPilingLayoutDiagramByMm(mainPart, body, widthMm: layoutW, heightMm: layoutH, GetPileTopBendingMomentMark);
                AddAutoFigureCaption(body, "杭頭モーメントマップ", "図");
            }
            if (mainWindowViewModel.IncludePileHeadShearMap)  // 杭頭せん断力マップ
            {
                double layoutW = 150; double layoutH = 200;
                AddPilingLayoutDiagramByMm(mainPart, body, widthMm: layoutW, heightMm: layoutH, GetPileTopShearForceMark);
                AddAutoFigureCaption(body, "杭頭せん断力マップ", "図");
            }
            if (mainWindowViewModel.IncludeSettlement) // 沈下
            {
                if (mainWindowViewModel.IsVerticalAnalysisDone)
                {
                    AddPageBreak(body);
                    AddHeader1(body, "単杭の沈下", 2);
                    AddSettlementGraph(mainPart, body);
                }
                else
                {
                    AddText(body, "（鉛直解析が未実施のため、沈下結果は省略されています）", "left");
                }
            }
            if (mainWindowViewModel.IncludeLoadSettlementCurve) // 沈下曲線
            {
                if (mainWindowViewModel.IsVerticalAnalysisDone)
                {
                    AddPageBreak(body);
                    AddHeader1(body, "荷重-沈下曲線", 2);
                    AddSettlementGraph(mainPart, body);
                }
                else
                {
                    AddText(body, "（鉛直解析が未実施のため、荷重-沈下曲線は省略されています）", "left");
                }
            }

            if (mainWindowViewModel.IncludeGroupPileSettlement) // 群杭沈下
            {
                AddGroupPileSettlementContourDiagram(mainPart, body);
                AddPileSettlementTable(body);
            }

            if (mainWindowViewModel.IncludeVerticalBeamResults) // 基礎梁考慮鉛直解析結果
            {
                if (mainWindowViewModel.IsVerticalBeamAnalysisDone && mainWindowViewModel.VerticalBeamCaseResults != null)
                {
                    AddPageBreak(body);
                    AddHeader1(body, "基礎梁考慮鉛直解析結果", 2);
                    AddVerticalBeamResultTables(body);
                }
                else
                {
                    AddText(body, "（基礎梁考慮鉛直解析が未実施のため、結果は省略されています）", "left");
                }
            }

            // FT-Pile構法
            if (HasFTPile())
            {
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddDescriptionFTPile(body);
                    AddLineBreak(body);
                }
            }

            // キャプテンパイル工法
            if (HasCaptainPile())
            {
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddDescriptionCaptainPile(body);
                    AddLineBreak(body);
                }
            }
        }

        // 荷重組合せ + 図・グラフ類
        private void AddLoadCombinationAndFigureSection(MainDocumentPart mainPart, Body body, InputModel model)
        {

            AddPileDescription(mainPart, body);

            //var soilPiles = model.ElementDivision.SoilPiles;
            //if (soilPiles is { Count: > 0 })
            //{
            //    var soilPile = soilPiles[0];
            //    const double pileElevationW = 150;
            //    const double pileElevationH = 100;

            //    AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "horizontal");
            //    AddAutoFigureCaption(body, "水平抵抗解析杭モデル", "図");

            //    AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "vertical");
            //    AddAutoFigureCaption(body, "沈下解析杭モデル", "図");

            //    AddSettlementGraph(mainPart, body);
            //    AddPileForceSummaryTable(mainPart, body);
            //    AddNMinT(mainPart, body);
            //}
        }

        // 目次を挿入するヘルパ（OpenXML のフィールドで TOC を作る）
        public static void AddTableOfContents(Body body, int headingLevels = 3)
        {
            if (body == null) return;

            // 目次見出し
            var titlePara = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new FontSize { Val = (14 * 2).ToString() }, CreateDefaultRunFonts())) { }
            );
            titlePara.Append(new Run(new Text("目次")));
            body.Append(titlePara);

            // 本文目次 TOC フィールド
            AddTocField(body, $"TOC \\o \"1-{headingLevels}\" \\h \\z \\u",
                "目次を更新するには、Wordでフィールドを更新してください。 (選択: __Ctrl+A__, 更新: __F9__)");

            AddLineBreak(body);

            // 図目次
            var figTitlePara = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Left }),
                new Run(new RunProperties(new FontSize { Val = (12 * 2).ToString() }, CreateDefaultRunFonts(), new Bold()),
                    new Text("図目次"))
            );
            body.Append(figTitlePara);
            // TOC \c の識別子は SEQ 識別子と同じ（Latin）を使う。
            AddTocField(body, "TOC \\h \\z \\c \"Figure\"", "（Ctrl+A → F9 で更新）");

            AddLineBreak(body);

            // 表目次
            var tblTitlePara = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Left }),
                new Run(new RunProperties(new FontSize { Val = (12 * 2).ToString() }, CreateDefaultRunFonts(), new Bold()),
                    new Text("表目次"))
            );
            body.Append(tblTitlePara);
            AddTocField(body, "TOC \\h \\z \\c \"Table\"", "（Ctrl+A → F9 で更新）");
        }

        /// <summary>TOCフィールドを1つ挿入するヘルパ</summary>
        private static void AddTocField(Body body, string fieldCode, string placeholder)
        {
            var tocPara = new Paragraph();
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));
            var instrRun = new Run();
            instrRun.Append(new FieldCode(fieldCode) { Space = SpaceProcessingModeValues.Preserve });
            tocPara.Append(instrRun);
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
            tocPara.Append(new Run(new Text(placeholder)));
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            body.Append(tocPara);
        }

        // 追加ヘルパ: TeX 文字列から OfficeMath を作る短縮ラッパ
        public static DocumentFormat.OpenXml.Math.OfficeMath Tex(string tex)
        {
            return ParseTeXToOfficeMath(tex ?? string.Empty);
        }

        // 追加ヘルパ: 単一 TeX 式を含む段落を追加する簡易メソッド（旧 AddInlineMathParagraph と同様の振る舞い）
        public static void AddInlineTeXParagraph(
            Body body,
            string tex,
            double fontSize = 10.5,
            int leftIndentMm = 0,
            int firstLineIndentMm = 0,
            int hangingIndentMm = 0)
        {
            if (body == null) return;
            var math = ParseTeXToOfficeMath(tex ?? string.Empty);
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = CreateIndentation(leftIndentMm, firstLineIndentMm, hangingIndentMm)
                }
            };
            paragraph.Append(math);
            body.Append(paragraph);
        }

        // 追加: 簡易 TeX -> OfficeMath パーサ（WordDocument クラス内の任意の場所に追加）
        public static DocumentFormat.OpenXml.Math.OfficeMath ParseTeXToOfficeMath(string tex)
        {
            if (string.IsNullOrWhiteSpace(tex))
                return GetCombinedRunToMath([GetRun("")]);

            // $...$ があれば取り除く
            if (tex.Length >= 2 && tex[0] == '$' && tex[^1] == '$')
                tex = tex[1..^1];

            int pos = 0;
            int len = tex.Length;

            void SkipSpaces()
            {
                while (pos < len && char.IsWhiteSpace(tex[pos])) pos++;
            }

            char Peek() => pos < len ? tex[pos] : '\0';

            string ReadWhileLetter()
            {
                int s = pos;
                while (pos < len && char.IsLetter(tex[pos])) pos++;
                return tex[s..pos];
            }

            DocumentFormat.OpenXml.Math.Run CombineRunsToRun(List<DocumentFormat.OpenXml.Math.Run> runs)
            {
                if (runs == null || runs.Count == 0) return GetRun("");
                if (runs.Count == 1) return runs[0];
                return GetCombinedRun(runs);
            }

            string MapTexCmd(string cmd) => cmd switch
            {
                "alpha" => "α",
                "beta" => "β",
                "gamma" => "γ",
                "delta" => "δ",
                "eta" => "η",
                "Delta" => "Δ",
                "theta" => "θ",
                "phi" => "φ",
                "pi" => "π",
                "psi" => "ψ",
                "xi" => "ξ",
                "kappa" => "κ",
                "zeta" => "ζ",
                "sigma" => "σ",
                "mu" => "μ",
                "nu" => "ν",
                "lambda" => "λ",
                "epsilon" => "ε",
                "varepsilon" => "ϵ",
                "tau" => "τ",
                "times" => "×",
                "cdot" => "·",
                // degree symbol for \circ
                "circ" => "°",
                // TeX の空白コマンドを Unicode 空白へマップ
                //   -> thin space (U+2009)
                // \:  -> medium/figure space (U+2005)
                // \;  -> em space (U+2003)
                // \!  -> negative thin (扱いづらいので空文字にする)
                "," => "\u2009",
                ":" => "\u2005",
                ";" => "\u2003",
                "!" => "",
                // 不等号コマンド
                "ge" => "≥",
                "geq" => "≥",
                "le" => "≤",
                "leq" => "≤",
                _ => cmd
            };

            // 匹配用ペアを返す（left の開始文字に対する期待終了文字）
            char MatchingRight(char left)
            {
                return left switch
                {
                    '(' => ')',
                    '[' => ']',
                    '{' => '}',
                    '<' => '>',
                    '|' => '|',
                    _ => left
                };
            }

            // Parse a sequence until a terminating char (endChar == '\0' means EOF).
            // expectedRightChar を渡すと、"\right<ch>" を見つけたらそこで終了する挙動を行う（\left...\right...用）
            List<DocumentFormat.OpenXml.Math.Run> ParseSequence(char endChar, char expectedRightChar = '\0')
            {
                var runs = new List<DocumentFormat.OpenXml.Math.Run>();
                while (pos < len && (endChar == '\0' || tex[pos] != endChar))
                {
                    SkipSpaces();
                    if (pos >= len || (endChar != '\0' && tex[pos] == endChar)) break;

                    // For \rightX termination when expectedRightChar is given:
                    if (expectedRightChar != '\0' && pos < len && tex[pos] == '\\')
                    {
                        // match \right (case-insensitive) at current position
                        var remaining = tex[pos..];
                        var m = System.Text.RegularExpressions.Regex.Match(remaining, @"^\\right\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            int checkPos = pos + m.Length;
                            // skip optional spaces after \right
                            while (checkPos < len && char.IsWhiteSpace(tex[checkPos])) checkPos++;

                            if (checkPos < len)
                            {
                                // direct single-char delimiter: \right}
                                if (tex[checkPos] == expectedRightChar)
                                {
                                    pos = checkPos + 1; // consume delimiter
                                    break;
                                }

                                // escaped delimiter: \right\}
                                if (tex[checkPos] == '\\' && checkPos + 1 < len && tex[checkPos + 1] == expectedRightChar)
                                {
                                    pos = checkPos + 2; // consume backslash and delimiter
                                    break;
                                }

                                // invisible delimiter \right.
                                if (tex[checkPos] == '.')
                                {
                                    pos = checkPos + 1;
                                    break;
                                }

                                // named delimiter like \rbrace, \rangle, \vert, \rvert
                                if (tex[checkPos] == '\\')
                                {
                                    int namePos = checkPos + 1;
                                    int nameStart = namePos;
                                    while (namePos < len && char.IsLetter(tex[namePos])) namePos++;
                                    if (namePos > nameStart)
                                    {
                                        string name = tex[nameStart..namePos];
                                        var mapping = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
                                        {
                                            ["rbrace"] = '}',
                                            ["lbrace"] = '{',
                                            ["rangle"] = '>',
                                            ["lang"] = '<',
                                            ["vert"] = '|',
                                            ["rvert"] = '|',
                                        };
                                        if (mapping.TryGetValue(name, out char mapped) && mapped == expectedRightChar)
                                        {
                                            pos = namePos; // consume the name token
                                            break;
                                        }
                                    }
                                }

                                // If none matched, fall through and continue parsing normally.
                            }
                        }
                    }

                    // parse base atom
                    DocumentFormat.OpenXml.Math.Run baseRun;
                    if (tex[pos] == '{')
                    {
                        pos++; // consume '{'
                        var inner = ParseSequence('}', '\0');
                        if (pos < len && tex[pos] == '}') pos++;
                        baseRun = CombineRunsToRun(inner);
                    }
                    else if (tex[pos] == '\\')
                    {
                        pos++; // consume '\'
                        string cmd = ReadWhileLetter();

                        if (string.Equals(cmd, "frac", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cmd, "dfrac", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(cmd, "tfrac", StringComparison.OrdinalIgnoreCase))
                        {
                            // expect {num}{den}
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var numList = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var denList = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetFraction(CombineRunsToRun(numList), CombineRunsToRun(denList));
                        }
                        else if (string.Equals(cmd, "sqrt", StringComparison.OrdinalIgnoreCase))
                        {
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var inner = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetRadicalRun(CombineRunsToRun(inner));
                        }
                        else if (string.Equals(cmd, "overline", StringComparison.OrdinalIgnoreCase))
                        {
                            // \overline{...}
                            SkipSpaces();
                            if (Peek() == '{') { pos++; }
                            var inner = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            baseRun = GetTopBarredRun(CombineRunsToRun(inner));
                        }
                        else if (string.Equals(cmd, "left", StringComparison.OrdinalIgnoreCase))
                        {
                            // \left<delim> ... \right<matching>
                            SkipSpaces();
                            if (pos >= len) { baseRun = GetRun(""); }
                            else
                            {
                                char beginChar = tex[pos];
                                pos++;
                                char expected = MatchingRight(beginChar);
                                // parse until \right<expected>
                                var inner = ParseSequence('\0', expected);
                                // create delimitered run (beginChar, expected)
                                baseRun = GetDelimiteredRun(CombineRunsToRun(inner), beginChar.ToString(), expected.ToString());
                            }
                        }
                        else if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(cmd, "int", StringComparison.OrdinalIgnoreCase))
                        {
                            // parse optional sub/sup in either order for \sum and \int
                            SkipSpaces();
                            DocumentFormat.OpenXml.Math.Run subRun = GetRun("");
                            DocumentFormat.OpenXml.Math.Run superRun = GetRun("");

                            // allow up to two markers (_ or ^) immediately following
                            for (int k = 0; k < 2; k++)
                            {
                                if (pos < len && (tex[pos] == '_' || tex[pos] == '^'))
                                {
                                    char op = tex[pos++];
                                    SkipSpaces();
                                    DocumentFormat.OpenXml.Math.Run argRun;
                                    if (pos < len && tex[pos] == '{')
                                    {
                                        pos++;
                                        var seq = ParseSequence('}');
                                        if (pos < len && tex[pos] == '}') pos++;
                                        argRun = CombineRunsToRun(seq);
                                    }
                                    else if (pos < len && tex[pos] == '\\')
                                    {
                                        pos++;
                                        string cmd2 = ReadWhileLetter();
                                        argRun = GetRun(MapTexCmd(cmd2));
                                    }
                                    else if (pos < len)
                                    {
                                        argRun = GetRun(tex[pos].ToString());
                                        pos++;
                                    }
                                    else
                                    {
                                        argRun = GetRun("");
                                    }

                                    if (op == '_') subRun = argRun;
                                    else superRun = argRun;

                                    SkipSpaces();
                                    continue;
                                }
                                break;
                            }

                            // After optional sub/sup, if a braced body follows treat that as the base (本文)
                            SkipSpaces();
                            if (pos < len && tex[pos] == '{')
                            {
                                pos++; // consume '{'
                                var bodySeq = ParseSequence('}');
                                if (pos < len && tex[pos] == '}') pos++;
                                // base is the body content
                                if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase))
                                    baseRun = GetSummationRun(CombineRunsToRun(bodySeq), subRun, superRun);
                                else
                                    baseRun = GetIntegralRun(CombineRunsToRun(bodySeq), subRun, superRun);
                            }
                            else
                            {
                                // no explicit braced body -> create nary with empty base (symbol only)
                                if (string.Equals(cmd, "sum", StringComparison.OrdinalIgnoreCase))
                                    baseRun = GetSummationRun(GetRun(""), subRun, superRun);
                                else
                                    baseRun = GetIntegralRun(GetRun(""), subRun, superRun);
                            }
                        }
                        else
                        {
                            // greek or plain command -> map to character(s)
                            baseRun = GetRun(MapTexCmd(cmd));
                        }
                    }
                    else
                    {
                        // normal characters until special char
                        int s = pos;
                        while (pos < len && !"{}^_\\$".Contains(tex[pos]))
                            pos++;
                        string txt = tex[s..pos];
                        baseRun = GetRun(txt);
                    }

                    SkipSpaces();
                    // handle ^ and _ possibly repeated
                    while (pos < len && (tex[pos] == '^' || tex[pos] == '_'))
                    {
                        char op = tex[pos++];
                        SkipSpaces();
                        DocumentFormat.OpenXml.Math.Run subSuperRun;
                        if (pos < len && tex[pos] == '{')
                        {
                            pos++;
                            var seq = ParseSequence('}');
                            if (pos < len && tex[pos] == '}') pos++;
                            subSuperRun = CombineRunsToRun(seq);
                        }
                        else if (pos < len && tex[pos] == '\\')
                        {
                            pos++;
                            string cmd = ReadWhileLetter();
                            subSuperRun = GetRun(MapTexCmd(cmd));
                        }
                        else
                        {
                            // single char
                            if (pos < len)
                            {
                                subSuperRun = GetRun(tex[pos].ToString());
                                pos++;
                            }
                            else
                            {
                                subSuperRun = GetRun("");
                            }
                        }

                        if (op == '^')
                            baseRun = GetSuperscript(baseRun, subSuperRun);
                        else
                            baseRun = GetSubscript(baseRun, subSuperRun);

                        SkipSpaces();
                    }

                    runs.Add(baseRun);
                }
                return runs;
            }

            var runsList = ParseSequence('\0');
            var combined = CombineRunsToRun(runsList);
            return GetCombinedRunToMath([combined]);
        }
        //public static DocumentFormat.OpenXml.Math.OfficeMath ParseTeXToOfficeMath(string tex)
        //{
        //    if (string.IsNullOrWhiteSpace(tex))
        //        return GetCombinedRunToMath([GetRun("")]);

        //    // $...$ があれば取り除く
        //    if (tex.Length >= 2 && tex[0] == '$' && tex[^1] == '$')
        //        tex = tex.Substring(1, tex.Length - 2);

        //    int pos = 0;
        //    int len = tex.Length;

        //    void SkipSpaces()
        //    {
        //        while (pos < len && char.IsWhiteSpace(tex[pos])) pos++;
        //    }

        //    char Peek() => pos < len ? tex[pos] : '\0';

        //    string ReadWhileLetter()
        //    {
        //        int s = pos;
        //        while (pos < len && char.IsLetter(tex[pos])) pos++;
        //        return tex.Substring(s, pos - s);
        //    }

        //    DocumentFormat.OpenXml.Math.Run CombineRunsToRun(List<DocumentFormat.OpenXml.Math.Run> runs)
        //    {
        //        if (runs == null || runs.Count == 0) return GetRun("");
        //        if (runs.Count == 1) return runs[0];
        //        return GetCombinedRun(runs);
        //    }

        //    string MapTexCmd(string cmd) => cmd switch
        //    {
        //        "alpha" => "α",
        //        "beta" => "β",
        //        "gamma" => "γ",
        //        "delta" => "δ",
        //        "theta" => "θ",
        //        "phi" => "φ",
        //        "pi" => "π",
        //        "sum" => "∑",
        //        "int" => "∫",
        //        _ => cmd
        //    };

        //    // Parse a sequence until a terminating char (endChar == '\0' means EOF)
        //    List<DocumentFormat.OpenXml.Math.Run> ParseSequence(char endChar)
        //    {
        //        var runs = new List<DocumentFormat.OpenXml.Math.Run>();
        //        while (pos < len && (endChar == '\0' || tex[pos] != endChar))
        //        {
        //            SkipSpaces();
        //            if (pos >= len || (endChar != '\0' && tex[pos] == endChar)) break;

        //            // parse base atom
        //            DocumentFormat.OpenXml.Math.Run baseRun;
        //            if (tex[pos] == '{')
        //            {
        //                pos++; // consume '{'
        //                var inner = ParseSequence('}');
        //                if (pos < len && tex[pos] == '}') pos++;
        //                baseRun = CombineRunsToRun(inner);
        //            }
        //            else if (tex[pos] == '\\')
        //            {
        //                pos++; // consume '\'
        //                string cmd = ReadWhileLetter();
        //                if (string.Equals(cmd, "frac", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    // expect {num}{den}
        //                    // numerator
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var numList = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    // denominator
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var denList = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    baseRun = GetFraction(CombineRunsToRun(numList), CombineRunsToRun(denList));
        //                }
        //                else if (string.Equals(cmd, "sqrt", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    SkipSpaces();
        //                    if (Peek() == '{') { pos++; }
        //                    var inner = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    baseRun = GetRadicalRun(CombineRunsToRun(inner));
        //                }
        //                else
        //                {
        //                    // greek or plain command
        //                    baseRun = GetRun(MapTexCmd(cmd));
        //                }
        //            }
        //            else
        //            {
        //                // normal characters until special char
        //                int s = pos;
        //                while (pos < len && !"{}^_\\$".Contains(tex[pos]))
        //                    pos++;
        //                string txt = tex[s..pos];
        //                baseRun = GetRun(txt);
        //            }

        //            SkipSpaces();
        //            // handle ^ and _ possibly repeated
        //            while (pos < len && (tex[pos] == '^' || tex[pos] == '_'))
        //            {
        //                char op = tex[pos++];
        //                SkipSpaces();
        //                DocumentFormat.OpenXml.Math.Run subSuperRun;
        //                if (pos < len && tex[pos] == '{')
        //                {
        //                    pos++;
        //                    var seq = ParseSequence('}');
        //                    if (pos < len && tex[pos] == '}') pos++;
        //                    subSuperRun = CombineRunsToRun(seq);
        //                }
        //                else if (pos < len && tex[pos] == '\\')
        //                {
        //                    pos++;
        //                    string cmd = ReadWhileLetter();
        //                    subSuperRun = GetRun(MapTexCmd(cmd));
        //                }
        //                else
        //                {
        //                    // single char
        //                    if (pos < len)
        //                    {
        //                        subSuperRun = GetRun(tex[pos].ToString());
        //                        pos++;
        //                    }
        //                    else
        //                    {
        //                        subSuperRun = GetRun("");
        //                    }
        //                }

        //                if (op == '^')
        //                    baseRun = GetSuperscript(baseRun, subSuperRun);
        //                else
        //                    baseRun = GetSubscript(baseRun, subSuperRun);

        //                SkipSpaces();
        //            }

        //            runs.Add(baseRun);
        //        }
        //        return runs;
        //    }

        //    var runsList = ParseSequence('\0');
        //    var combined = CombineRunsToRun(runsList);
        //    return GetCombinedRunToMath([combined]);
        //}

        // 修正: CreateTableCell 内の string 処理を拡張して $...$ を TeX として変換できるようにする
        private static TableCell CreateTableCell(
            List<object> contents,
            double fontSize = 8,
            string alignment = "center",
            string verticalAlignment = "center",
            bool bold = false
        )
        {
            var cell = new TableCell();

            var cellProperties = new TableCellProperties();
            cellProperties.Append(new TableCellVerticalAlignment
            {
                Val = verticalAlignment.ToLower() switch
                {
                    "top" => TableVerticalAlignmentValues.Top,
                    "bottom" => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Center
                }
            });
            cell.Append(cellProperties);

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification
                    {
                        Val = alignment.ToLower() switch
                        {
                            "center" or "centre" => JustificationValues.Center,
                            "right" => JustificationValues.Right,
                            _ => JustificationValues.Left
                        }
                    }
                }
            };

            if (contents != null)
            {
                for (int i = 0; i < contents.Count; i++)
                {
                    var item = contents[i];

                    switch (item)
                    {
                        case string s:
                            foreach (var e in BuildInlineMixedRuns(s, fontSize))
                            {
                                if (bold && e is Run r) { r.RunProperties ??= new RunProperties(); r.RunProperties.Bold ??= new Bold(); }
                                paragraph.Append(e);
                            }
                            break;

                        case Run run:
                            if (bold) { run.RunProperties ??= new RunProperties(); run.RunProperties.Bold ??= new Bold(); }
                            paragraph.Append(run);
                            break;

                        case DocumentFormat.OpenXml.Math.OfficeMath math:
                            paragraph.Append(math);
                            break;

                        case null:
                            break;

                        default:
                            var txt = item.ToString();
                            if (!string.IsNullOrEmpty(txt))
                            {
                                foreach (var e in BuildInlineMixedRuns(txt, fontSize))
                                {
                                    if (bold && e is Run r2) { r2.RunProperties ??= new RunProperties(); r2.RunProperties.Bold ??= new Bold(); }
                                    paragraph.Append(e);
                                }
                            }
                            break;
                    }

                    if (i < contents.Count - 1)
                        paragraph.Append(new Run(new Break()));
                }
            }

            cell.Append(paragraph);
            return cell;
        }

        // 修正: AddInlineMathParagraph を更新し、parts 内の文字列に含まれる $...$ を TeX として自動変換するようにする
        public static void AddInlineMathParagraph(
            Body body,
            object[] parts,
            double fontSize = 10.5,
            int leftIndentMm = 0,
            int firstLineIndentMm = 0,
            int hangingIndentMm = 0
        )
        {
            if (body == null) return;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = CreateIndentation(leftIndentMm, firstLineIndentMm, hangingIndentMm)
                }
            };

            if (parts == null || parts.Length == 0)
            {
                body.Append(paragraph);
                return;
            }

            foreach (var part in parts)
            {
                switch (part)
                {
                    case null:
                        continue;

                    case string s:
                        {
                            // 改行分割し各行を BuildInlineMixedRuns で処理
                            var lines = s.Replace("\r\n", "\n").Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                foreach (var e in BuildInlineMixedRuns(lines[i], fontSize))
                                    paragraph.Append(e);

                                if (i < lines.Length - 1)
                                    paragraph.Append(new Run(new Break()));
                            }
                            break;
                        }

                    case Run run:
                        paragraph.Append(run);
                        break;

                    case DocumentFormat.OpenXml.Math.OfficeMath math:
                        paragraph.Append(math);
                        break;

                    default:
                        {
                            var txt = part.ToString();
                            if (string.IsNullOrEmpty(txt)) break;
                            var lines = txt.Replace("\r\n", "\n").Split('\n');
                            for (int i = 0; i < lines.Length; i++)
                            {
                                foreach (var e in BuildInlineMixedRuns(lines[i], fontSize))
                                    paragraph.Append(e);

                                if (i < lines.Length - 1)
                                    paragraph.Append(new Run(new Break()));
                            }
                            break;
                        }
                }
            }

            body.Append(paragraph);
        }


        // 検討方針: 接続モード説明と一般梁要素情報
        private void AddDesignApproachSection(Body body, InputModel inputModel)
        {
            var fbInput = inputModel.FoundationBeamInput;
            var mode = fbInput?.ConnectionMode ?? FoundationBeamConnectionMode.RigidBody;

            // 接続モードの説明
            AddHeader2(body, "杭頭接続仮定");

            if (mode == FoundationBeamConnectionMode.RigidBody)
            {
                AddInlineMathParagraph(body, ["杭頭の接続は剛体連結とする。"]);
                AddInlineMathParagraph(body, ["剛体連結では、すべての杭頭節点を代表点（荷重作用点）に対して" +
                    "全6自由度（$U_x, U_y, U_z, R_x, R_y, R_z$）で剛体拘束する。"]);

                // 拘束自由度の表
                AddConnectionModeTable(body, isRigidBody: true);
            }
            else
            {
                AddInlineMathParagraph(body, ["杭頭の接続は剛床連結とする。"]);
                AddInlineMathParagraph(body, ["剛床連結では、水平面内の変位（$U_x, U_y$）および鉛直軸まわりの回転（$R_z$）のみを" +
                    "剛体拘束し、鉛直変位（$U_z$）および水平軸まわりの回転（$R_x, R_y$）は自由とする。"]);
                AddInlineMathParagraph(body, ["自由とした自由度の剛性は、一般梁要素（基礎梁）の曲げ剛性およびせん断剛性により負担する。" +
                    "不均等な鉛直荷重の分配や、基礎梁のたわみによる各杭の沈下差を評価できる。"]);

                // 拘束自由度の表
                AddConnectionModeTable(body, isRigidBody: false);

                // 一般梁要素の入力データ
                if (fbInput != null)
                {
                    AddHeader2(body, "一般梁要素");
                    AddFoundationBeamInputTables(body, fbInput);
                }
            }
        }

        // 接続モードの拘束自由度テーブル
        private static void AddConnectionModeTable(Body body, bool isRigidBody)
        {
            double fs = 8.0;
            int w0 = 2500;
            int w1 = 1200;
            Table table = CreateTableWithBordersAndWidths(w0, w1, w1, w1, w1, w1, w1);

            // ヘッダ行
            TableRow headerRow = CreateHeaderRow(
                CreateTableCellWithWidth("", "center", w0, fs),
                CreateTableCellWithWidth("Ux", "center", w1, fs),
                CreateTableCellWithWidth("Uy", "center", w1, fs),
                CreateTableCellWithWidth("Uz", "center", w1, fs),
                CreateTableCellWithWidth("Rx", "center", w1, fs),
                CreateTableCellWithWidth("Ry", "center", w1, fs),
                CreateTableCellWithWidth("Rz", "center", w1, fs)
            );
            table.Append(headerRow);

            // データ行
            TableRow dataRow = new();
            dataRow.Append(
                CreateTableCellWithWidth("拘束", "center", w0, fs),
                CreateTableCellWithWidth("○", "center", w1, fs),
                CreateTableCellWithWidth("○", "center", w1, fs),
                CreateTableCellWithWidth(isRigidBody ? "○" : "－", "center", w1, fs),
                CreateTableCellWithWidth(isRigidBody ? "○" : "－", "center", w1, fs),
                CreateTableCellWithWidth(isRigidBody ? "○" : "－", "center", w1, fs),
                CreateTableCellWithWidth("○", "center", w1, fs)
            );
            table.Append(dataRow);

            // 剛性負担行（剛床連結のみ）
            if (!isRigidBody)
            {
                TableRow stiffnessRow = new();
                stiffnessRow.Append(
                    CreateTableCellWithWidth("剛性負担", "center", w0, fs),
                    CreateTableCellWithWidth("剛床", "center", w1, fs),
                    CreateTableCellWithWidth("剛床", "center", w1, fs),
                    CreateTableCellWithWidth("梁要素", "center", w1, fs),
                    CreateTableCellWithWidth("梁要素", "center", w1, fs),
                    CreateTableCellWithWidth("梁要素", "center", w1, fs),
                    CreateTableCellWithWidth("剛床", "center", w1, fs)
                );
                table.Append(stiffnessRow);
            }

            body.Append(table);
        }

        // 一般梁要素の入力データテーブル群
        private static void AddFoundationBeamInputTables(Body body, FoundationBeamInput fbInput)
        {
            double fs = 8.0;

            // 材料テーブル
            if (fbInput.Materials != null && fbInput.Materials.Count > 0)
            {
                AddText(body, "材料");
                int wNo = 1200, wName = 3000, wE = 3000, wG = 3000, wNu = 2000;
                Table matTable = CreateTableWithBordersAndWidths(wNo, wName, wE, wG, wNu);

                TableRow matHeader = CreateHeaderRow(
                    CreateTableCellWithWidth("No", "center", wNo, fs),
                    CreateTableCellWithWidth("名称", "center", wName, fs),
                    CreateTableCellWithWidth("E (kN/m²)", "center", wE, fs),
                    CreateTableCellWithWidth("G (kN/m²)", "center", wG, fs),
                    CreateTableCellWithWidth("ν", "center", wNu, fs)
                );
                matTable.Append(matHeader);

                foreach (var mat in fbInput.Materials)
                {
                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{mat.No}", "center", wNo, fs),
                        CreateTableCellWithWidth(mat.Name ?? "", "left", wName, fs),
                        CreateTableCellWithWidth($"{mat.YoungModulus:E2}", "right", wE, fs),
                        CreateTableCellWithWidth($"{mat.ShearModulus:E2}", "right", wG, fs),
                        CreateTableCellWithWidth($"{mat.PoissonRatio:N2}", "right", wNu, fs)
                    );
                    matTable.Append(row);
                }
                body.Append(matTable);
            }

            // 断面テーブル
            if (fbInput.Sections != null && fbInput.Sections.Count > 0)
            {
                AddText(body, "断面");
                int wNo = 800, wName = 1800, wB = 1400, wH = 1400, wA = 1800, wIy = 2200, wIz = 2200;
                Table secTable = CreateTableWithBordersAndWidths(wNo, wName, wB, wH, wA, wIy, wIz);

                TableRow secHeader = CreateHeaderRow(
                    CreateTableCellWithWidth("No", "center", wNo, fs),
                    CreateTableCellWithWidth("名称", "center", wName, fs),
                    CreateTableCellWithWidth("幅 [m]", "center", wB, fs),
                    CreateTableCellWithWidth("高さ [m]", "center", wH, fs),
                    CreateTableCellWithWidth("A (m²)", "center", wA, fs),
                    CreateTableCellWithWidth("Iy (m⁴)", "center", wIy, fs),
                    CreateTableCellWithWidth("Iz (m⁴)", "center", wIz, fs)
                );
                secTable.Append(secHeader);

                foreach (var sec in fbInput.Sections)
                {
                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{sec.No}", "center", wNo, fs),
                        CreateTableCellWithWidth(sec.Name ?? "", "left", wName, fs),
                        CreateTableCellWithWidth($"{sec.Width:N3}", "right", wB, fs),
                        CreateTableCellWithWidth($"{sec.Height:N3}", "right", wH, fs),
                        CreateTableCellWithWidth($"{sec.Area:N4}", "right", wA, fs),
                        CreateTableCellWithWidth($"{sec.MomentOfInertiaYY:N4}", "right", wIy, fs),
                        CreateTableCellWithWidth($"{sec.MomentOfInertiaZZ:N4}", "right", wIz, fs)
                    );
                    secTable.Append(row);
                }
                body.Append(secTable);
            }

            // 梁要素テーブル
            if (fbInput.Beams != null && fbInput.Beams.Count > 0)
            {
                AddText(body, "梁要素");
                int wNo = 1200, wMat = 2000, wSec = 2000, wNI = 3000, wNJ = 3000;
                Table beamTable = CreateTableWithBordersAndWidths(wNo, wMat, wSec, wNI, wNJ);

                TableRow beamHeader = CreateHeaderRow(
                    CreateTableCellWithWidth("No", "center", wNo, fs),
                    CreateTableCellWithWidth("材料No", "center", wMat, fs),
                    CreateTableCellWithWidth("断面No", "center", wSec, fs),
                    CreateTableCellWithWidth("I端", "center", wNI, fs),
                    CreateTableCellWithWidth("J端", "center", wNJ, fs)
                );
                beamTable.Append(beamHeader);

                foreach (var beam in fbInput.Beams)
                {
                    // ノード参照の表示文字列を組み立て
                    string nodeIStr = GetBeamNodeDisplayString(beam.NodeI_Type, beam.NodeI_No, fbInput);
                    string nodeJStr = GetBeamNodeDisplayString(beam.NodeJ_Type, beam.NodeJ_No, fbInput);

                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{beam.No}", "center", wNo, fs),
                        CreateTableCellWithWidth($"{beam.MaterialNo}", "center", wMat, fs),
                        CreateTableCellWithWidth($"{beam.SectionNo}", "center", wSec, fs),
                        CreateTableCellWithWidth(nodeIStr, "left", wNI, fs),
                        CreateTableCellWithWidth(nodeJStr, "left", wNJ, fs)
                    );
                    beamTable.Append(row);
                }
                body.Append(beamTable);
            }
        }

        // 梁要素のノード参照表示文字列（Word出力用）
        private static string GetBeamNodeDisplayString(NodeReferenceType type, int nodeNo, FoundationBeamInput fbInput)
        {
            return type switch
            {
                NodeReferenceType.PileLayout => $"杭 No.{nodeNo}",
                NodeReferenceType.GeneralNode => $"一般節点 No.{nodeNo}",
                NodeReferenceType.FoundationNode => $"専用節点 No.{nodeNo}",
                _ => $"No.{nodeNo}"
            };
        }

        // 杭明細を追加
        private void AddPileDescription(MainDocumentPart mainDocumentPart, Body body)
        {
            List<string> selectedPileBodies = [];
            List<int> selectedSegment = [];
            List<double> selectedSegmentTop = [];
            List<double> selectedSegmentBtm = [];

            List<string> sectionTypes = [];

            List<string> pileDias = [];
            List<string> mainBars = [];
            List<string> mainBarPCD = [];
            List<string> steelPipes = [];
            List<string> hoops = [];
            List<string> covers = [];

            List<string> pipeDescription = [];
            List<string> concreteFcDescription = [];
            List<string> concreteEcDescription = [];
            List<string> concreteGammaDescription = [];
            List<string> concreteGsiDescription = [];
            List<string> hoopDescription = [];
            List<string> mainBarDescription = [];

            // 杭検討結果まとめ一覧
            for (int selectedPileBodyNo = 1; selectedPileBodyNo <= inputModel.PileBodies.Count; selectedPileBodyNo++)
            {
                var pileBody = inputModel.PileBodies[selectedPileBodyNo - 1];

                if (pileBody.PileConstructionType == "場所打ちコンクリート杭")
                {
                    for (int selectedSegmentNo = 1; selectedSegmentNo <= pileBody.PileBodySegments.Count; selectedSegmentNo++)
                    {
                        var pileSection = pileBody.PileBodySegments[selectedSegmentNo - 1].PileSection;

                        selectedPileBodies.Add(pileBody.PileBodyRef);
                        selectedSegment.Add(selectedSegmentNo);
                        var segment = pileBody.PileBodySegments[selectedSegmentNo - 1];
                        selectedSegmentBtm.Add(segment.SegmentDepth);
                        selectedSegmentTop.Add(segment.SegmentDepth - segment.SegmentLength);

                        sectionTypes.Add(segment.PileSection.PileSectionType);

                        // 杭の詳細情報を取得
                        pileDias.Add($"{pileSection.PileDiameter:N0}");

                        if (Math.Abs(pileSection.PipeDia) < 0.0001 || Math.Abs(pileSection.PipeTs) < 0.001)
                        {
                            pipeDescription.Add(string.Empty);
                        }
                        else
                        {
                            pipeDescription.Add($"{pileSection.PipeDia:N0}-{pileSection.PipeTs:N0}({pileSection.PipeGrade})");
                        }

                        concreteFcDescription.Add($"{pileSection.ConcreteFc:N0}");
                        concreteEcDescription.Add($"{pileSection.ConcreteE:N0}");
                        concreteGammaDescription.Add($"{pileSection.ConcreteGamma:N1}");
                        concreteGsiDescription.Add($"{pileSection.ConcreteGsi:N2}");
                        hoopDescription.Add($"{pileSection.HoopSize}-{pileSection.HoopSpacing}({pileSection.HoopSpec})");
                        mainBarDescription.Add($"{pileSection.MainBarNum}-{pileSection.MainBarSize}({pileSection.MainBarSpec})");
                    }
                }

                else if (pileBody.PileConstructionType == "埋込み杭（プレボーリング）" ||
                    pileBody.PileConstructionType == "埋込み杭（中掘り）" ||
                    pileBody.PileConstructionType == "打込み杭" ||
                    pileBody.PileConstructionType == "回転貫入杭")
                {
                    // 杭の詳細情報を取得
                    //pileDias.Add(pileBody.PileDiameter);
                    //mainBars.Add(pileBody.MainBar);
                    //mainBarPCD.Add(pileBody.MainBarPCD);
                    //steelPipes.Add(pileBody.SteelPipe);
                    //hoops.Add(pileBody.Hoop);
                    //covers.Add(pileBody.Cover);
                }


            }

            //    {
            //        AddLineBreak(body);
            //        AddAutoFigureCaption(body, $"場所打ちコンクリート杭明細", "表");
            //        // 1. Table, TableRow, TableCellを作成
            //        var table = new Table();

            //        // 黒線の罫線プロパティを追加
            //        var borders = new TableBorders(
            //            new TopBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
            //            new BottomBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
            //            new LeftBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
            //            new RightBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
            //            new InsideHorizontalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
            //            new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 }
            //        );
            //        table.AppendChild(new TableProperties(borders));

            //        // テーブルの行を作成
            //        for (int rowIdx = 1; rowIdx <= selectedPileBodies.Count + 1; rowIdx++)
            //        {
            //            TableRow row = new();

            //            for (int colIdx = 1; colIdx <= 9; colIdx++)
            //            {
            //                TableCell cell = new();

            //                if (rowIdx == 1)
            //                {
            //                    if (colIdx == 1)
            //                    {
            //                        var para = GetParagraph("杭符号", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 2)
            //                    {
            //                        var para = GetParagraph("区間No", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 3)
            //                    {
            //                        var para = GetParagraph("上端深さ\n[m]", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 4)
            //                    {
            //                        var para = GetParagraph("下端深さ\n[m]", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 5)
            //                    {
            //                        var para = GetParagraph("杭断面タイプ", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 6)
            //                    {
            //                        var para = GetParagraph("杭径\n[mm]", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 7)
            //                    {
            //                        var para = GetParagraph("鋼管", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 8)
            //                    {
            //                        var para = GetParagraph("コンクリート\nFc|E|γ|ξ", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 9)
            //                    {
            //                        var para = GetParagraph("主筋", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 10)
            //                    {
            //                        var para = GetParagraph("フープ筋", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                }
            //                else
            //                {
            //                    int i = rowIdx - 2;

            //                    if (colIdx == 1)
            //                    {
            //                        var para = GetParagraph($"{selectedPileBodies[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 2)
            //                    {
            //                        var para = GetParagraph($"{selectedSegment[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 3)
            //                    {
            //                        var para = GetParagraph($"{selectedSegmentTop[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 4)
            //                    {
            //                        var para = GetParagraph($"{selectedSegmentBtm[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 5)
            //                    {
            //                        var para = GetParagraph($"{sectionTypes[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 6)
            //                    {
            //                        var para = GetParagraph($"{pileDias[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 7)
            //                    {
            //                        var para = GetParagraph($"{pipeDescription[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 8)
            //                    {
            //                        var para = GetParagraph($"{concreteFcDescription[i]}|{concreteEcDescription[i]}|{concreteGammaDescription[i]}|{concreteGsiDescription[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 9)
            //                    {
            //                        var para = GetParagraph($"{mainBarDescription[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 10)
            //                    {
            //                        var para = GetParagraph($"{hoopDescription[i]}", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    //cell.Append(new Paragraph(new Run(new Text("")))); // 空セル
            //                }

            //                row.Append(cell);
            //            }
            //            table.Append(row);
            //        }
            //        // 8. bodyにTableを追加
            //        body.Append(table);
            //    }
            //}
            {
                AddLineBreak(body);
                AddAutoFigureCaption(body, $"杭体明細", "表");
                //var table = new Table();
                var table = BuildPileDescriptionTable(inputModel);
                //var borders = new TableBorders(
                //    new TopBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                //    new BottomBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                //    new LeftBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                //    new RightBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                //    new InsideHorizontalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                //    new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 }
                //);
                //table.AppendChild(new TableProperties(borders));

                //for (int rowIdx = 1; rowIdx <= selectedPileBodies.Count + 1; rowIdx++)
                //{
                //    TableRow row = new();

                //    for (int colIdx = 1; colIdx <= 10; colIdx++)
                //    {
                //        TableCell cell = new();
                //        if (rowIdx == 1)
                //        {
                //            switch (colIdx)
                //            {
                //                case 1: SetTableCellWithVerticalAlign(cell, GetParagraph("杭符号", "center", 8), "center"); break;
                //                case 2: SetTableCellWithVerticalAlign(cell, GetParagraph("区間No", "center", 8), "center"); break;
                //                case 3: SetTableCellWithVerticalAlign(cell, GetParagraph("上端深さ\n[m]", "center", 8), "center"); break;
                //                case 4: SetTableCellWithVerticalAlign(cell, GetParagraph("下端深さ\n[m]", "center", 8), "center"); break;
                //                case 5: SetTableCellWithVerticalAlign(cell, GetParagraph("杭断面タイプ", "center", 8), "center"); break;
                //                case 6: SetTableCellWithVerticalAlign(cell, GetParagraph("杭径\n[mm]", "center", 8), "center"); break;
                //                case 7: SetTableCellWithVerticalAlign(cell, GetParagraph("鋼管", "center", 8), "center"); break;
                //                case 8: SetTableCellWithVerticalAlign(cell, GetParagraph("コンクリート\nFc|E|γ|ξ", "center", 8), "center"); break;
                //                case 9: SetTableCellWithVerticalAlign(cell, GetParagraph("主筋", "center", 8), "center"); break;
                //                case 10: SetTableCellWithVerticalAlign(cell, GetParagraph("フープ筋", "center", 8), "center"); break;
                //            }
                //        }
                //        else
                //        {
                //            int i = rowIdx - 2;
                //            switch (colIdx)
                //            {
                //                case 1: SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedPileBodies[i]}", "center", 8), "center"); break;
                //                case 2: SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegment[i]}", "center", 8), "center"); break;
                //                case 3: SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentTop[i]}", "center", 8), "center"); break;
                //                case 4: SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentBtm[i]}", "center", 8), "center"); break;
                //                case 5: SetTableCellWithVerticalAlign(cell, GetParagraph($"{sectionTypes[i]}", "center", 8), "center"); break;
                //                case 6: SetTableCellWithVerticalAlign(cell, GetParagraph($"{pileDias[i]}", "center", 8), "center"); break;
                //                case 7: SetTableCellWithVerticalAlign(cell, GetParagraph($"{pipeDescription[i]}", "center", 8), "center"); break;
                //                case 8: SetTableCellWithVerticalAlign(cell, GetParagraph($"{concreteFcDescription[i]}|{concreteEcDescription[i]}|{concreteGammaDescription[i]}|{concreteGsiDescription[i]}", "center", 8), "center"); break;
                //                case 9: SetTableCellWithVerticalAlign(cell, GetParagraph($"{mainBarDescription[i]}", "center", 8), "center"); break;
                //                case 10: SetTableCellWithVerticalAlign(cell, GetParagraph($"{hoopDescription[i]}", "center", 8), "center"); break;
                //            }
                //        }
                //        row.Append(cell);
                //    }

                //    // 追加: 1行目を繰返しヘッダー行に設定
                //    if (rowIdx == 1)
                //    {
                //        var trPr = row.GetFirstChild<TableRowProperties>();
                //        if (trPr == null)
                //        {
                //            trPr = new TableRowProperties();
                //            row.PrependChild(trPr);
                //        }
                //        if (!trPr.Elements<TableHeader>().Any())
                //            trPr.Append(new TableHeader());
                //    }

                //    table.Append(row);
                //}

                body.Append(table);
            }
        }

        // Build only the 場所打ちコンクリート杭明細 table and return it.
        // ここは段階的抽出の第一歩（元の AddPileDescription の一部を移植）。
        private static Table BuildPileDescriptionTable(InputModel inputModel)
        {
            // defensive
            ArgumentNullException.ThrowIfNull(inputModel);

            double fontSize = 8.0;

            // 列幅は既存 CreateTableWithBordersAndWidths を使う
            int[] widths = GetEqualColumnWidths(10);
            Table table = CreateTableWithBordersAndWidths(widths);

            // ヘッダー行
            var header = CreateHeaderRow(
                CreateTableCell(["杭符号"], fontSize, "center"),
                CreateTableCell(["区間No"], fontSize, "center"),
                CreateTableCell(["上端深さ\n[m]"], fontSize, "center"),
                CreateTableCell(["下端深さ\n[m]"], fontSize, "center"),
                CreateTableCell(["杭断面タイプ"], fontSize, "center"),
                CreateTableCell(["杭径\n[mm]"], fontSize, "center"),
                CreateTableCell(["鋼管"], fontSize, "center"),
                CreateTableCell(["コンクリート\nF<_c>|E|γ|ξ"], fontSize, "center"),
                CreateTableCell(["主筋"], fontSize, "center"),
                CreateTableCell(["フープ筋"], fontSize, "center")
            );
            table.Append(header);

            // データ行（安全にnullチェック）
            if (inputModel.PileBodies != null)
            {
                for (int p = 0; p < inputModel.PileBodies.Count; p++)
                {
                    var pileBody = inputModel.PileBodies[p];
                    if (pileBody == null) continue;

                    // 杭体内の各区間を列挙して行を追加（元のロジックに合わせる）
                    for (int segIdx = 0; segIdx < pileBody.PileBodySegments.Count; segIdx++)
                    {
                        var seg = pileBody.PileBodySegments[segIdx];
                        var section = seg?.PileSection;

                        var row = new TableRow();
                        row.Append(CreateTableCell([pileBody.PileBodyRef], fontSize, "center"));
                        row.Append(CreateTableCell([(segIdx + 1).ToString()], fontSize, "center"));
                        row.Append(CreateTableCell([$"{seg.SegmentDepth - seg.SegmentLength:N3}"], fontSize, "center"));
                        row.Append(CreateTableCell([$"{seg.SegmentDepth:N3}"], fontSize, "center"));
                        row.Append(CreateTableCell([section?.PileSectionType ?? string.Empty], fontSize, "center"));
                        row.Append(CreateTableCell([section != null ? $"{section.PileDiameter:N0}" : string.Empty], fontSize, "center"));

                        // 鋼管/コンクリート/主筋/フープ筋 は null 安全に
                        string pipeDesc = (section != null && Math.Abs(section.PipeDia) > 1e-6 && Math.Abs(section.PipeTs) > 1e-6)
                            ? $"{section.PipeDia:N0}-{section.PipeTs:N0}\n({section.PipeGrade})"
                            : string.Empty;
                        row.Append(CreateTableCell([pipeDesc], fontSize, "center"));

                        string concreteDesc = section != null
                            ? $"{section.ConcreteFc:N0}|{section.ConcreteE:N0}|{section.ConcreteGamma:N1}|{section.ConcreteGsi:N2}"
                            : string.Empty;
                        row.Append(CreateTableCell([concreteDesc], fontSize, "center"));

                        // 主筋: 0-0の場合は表示しない、それ以外は改行を入れる
                        string mainBarDesc = string.Empty;
                        if (section != null && section.MainBarNum > 0)
                        {
                            mainBarDesc = $"{section.MainBarNum}-{section.MainBarSize}\n({section.MainBarSpec})";
                        }
                        row.Append(CreateTableCell([mainBarDesc], fontSize, "center"));

                        // フープ筋: 改行を入れる
                        string hoopDesc = section != null ? $"{section.HoopSize}-{section.HoopSpacing}\n({section.HoopSpec})" : string.Empty;
                        row.Append(CreateTableCell([hoopDesc], fontSize, "center"));

                        table.Append(row);
                    }
                }
            }

            return table;
        }

        // 杭検討結果まとめ表を追加
        private void AddPileForceSummaryTable(MainDocumentPart mainDocumentPart, Body body)
        {
            List<string> selectedPileBodies = [];
            List<int> selectedSegment = [];
            List<double> selectedSegmentTop = [];
            List<double> selectedSegmentBtm = [];
            List<List<double>> Qmaxs = [];
            List<List<double>> Mmaxs = [];
            List<List<double>> NMaxs = [];
            List<List<double>> NMins = [];
            List<List<double>> Dmaxs = [];

            // 杭検討結果まとめ一覧
            for (int selectedPileBodyNo = 1; selectedPileBodyNo <= inputModel.PileBodies.Count; selectedPileBodyNo++)
            {
                var pileBody = inputModel.PileBodies[selectedPileBodyNo - 1];
                for (int selectedSegmentNo = 1; selectedSegmentNo <= pileBody.PileBodySegments.Count; selectedSegmentNo++)
                {
                    selectedPileBodies.Add(pileBody.PileBodyRef);
                    selectedSegment.Add(selectedSegmentNo);
                    var segment = pileBody.PileBodySegments[selectedSegmentNo - 1];
                    selectedSegmentBtm.Add(segment.SegmentDepth);
                    selectedSegmentTop.Add(segment.SegmentDepth - segment.SegmentLength);
                    Qmaxs.Add([double.MinValue, double.MinValue]);
                    Mmaxs.Add([double.MinValue, double.MinValue]);
                    NMaxs.Add([double.MinValue, double.MinValue]);
                    NMins.Add([double.MaxValue, double.MaxValue]);
                    Dmaxs.Add([double.MinValue, double.MinValue]);

                    //foreach (PileLayoutDataItem pileLayoutDataItem in inputModel.PileLayoutItems)
                    //{
                    //    if (pileLayoutDataItem.PileBodyNo != selectedPileBodyNo) continue;

                    //    NMaxs[^1][0] = Math.Max(NMaxs[^1][0], pileLayoutDataItem.AxialForceLevel1s.Max());
                    //    NMins[^1][0] = Math.Min(NMins[^1][0], pileLayoutDataItem.AxialForceLevel1s.Min());
                    //    NMaxs[^1][1] = Math.Max(NMaxs[^1][1], pileLayoutDataItem.AxialForceLevel2s.Max());
                    //    NMins[^1][1] = Math.Min(NMins[^1][1], pileLayoutDataItem.AxialForceLevel2s.Min());

                    //    foreach (LoadCase loadCase in inputModel.LoadCasesInput.AllSeismicLoadCases)
                    //    {
                    //        var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                    //        foreach (LoadCombination loadCombination in inputModel.LoadCasesInput.AllLoadCombinations)
                    //        {
                    //            foreach (var isLiquefaction in new[] { true, false })
                    //            {
                    //                // PileBodySegmentループ
                    //                for (int i = 0; i < inputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1].PileBodySegments.Count; i++)
                    //                {
                    //                    var pileBodySegment = inputModel.ElementDivision.SoilPiles[pileLayoutDataItem.SoilPileAltNo - 1].PileBodySegments[i];
                    //                    if (pileBodySegment.No != selectedSegmentNo) continue;

                    //                    var momentInPile = pileLayoutDataItem.Beams[i].GetBeamResult(
                    //                    anaModel, loadCase, loadCombination, isLiquefaction).CumulativeForce.MabsMax;

                    //                    var shearInPile = pileLayoutDataItem.Beams[i].GetBeamResult(
                    //                    anaModel, loadCase, loadCombination, isLiquefaction).CumulativeForce.FabsMax;

                    //                    var dispInPile = Math.Max(
                    //                        pileLayoutDataItem.Beams[i].NodeI.GetNodeResult(
                    //                        anaModel, loadCase, loadCombination, isLiquefaction).CumulativeDisp.Uh,
                    //                        pileLayoutDataItem.Beams[i].NodeJ.GetNodeResult(
                    //                        anaModel, loadCase, loadCombination, isLiquefaction).CumulativeDisp.Uh);

                    //                    int k = loadCase.Level - 1;

                    //                    Qmaxs[^1][k] = Math.Max(Qmaxs[^1][k], shearInPile);
                    //                    Mmaxs[^1][k] = Math.Max(Mmaxs[^1][k], momentInPile);
                    //                    Dmaxs[^1][k] = Math.Max(Dmaxs[^1][k], dispInPile);
                    //                }
                    //            }
                    //        }
                    //    }
                    //}
                    foreach (PileLayoutDataItem pileLayoutDataItem in inputModel.PileLayoutItems)
                    {
                        if (pileLayoutDataItem == null) continue;
                        if (pileLayoutDataItem.PileBodyNo != selectedPileBodyNo) continue;

                        // Safe update for NMax/NMin lists (guard against null or empty axial force lists)
                        if (pileLayoutDataItem.AxialForceLevel1s != null && pileLayoutDataItem.AxialForceLevel1s.Count > 0)
                        {
                            NMaxs[^1][0] = Math.Max(NMaxs[^1][0], pileLayoutDataItem.AxialForceLevel1s.Max());
                            NMins[^1][0] = Math.Min(NMins[^1][0], pileLayoutDataItem.AxialForceLevel1s.Min());
                        }
                        if (pileLayoutDataItem.AxialForceLevel2s != null && pileLayoutDataItem.AxialForceLevel2s.Count > 0)
                        {
                            NMaxs[^1][1] = Math.Max(NMaxs[^1][1], pileLayoutDataItem.AxialForceLevel2s.Max());
                            NMins[^1][1] = Math.Min(NMins[^1][1], pileLayoutDataItem.AxialForceLevel2s.Min());
                        }

                        foreach (LoadCase loadCase in inputModel.LoadCasesInput.AllSeismicLoadCases)
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in inputModel.LoadCasesInput.AllLoadCombinations)
                            {
                                // 液状化パターン（ユーザー選択に基づく）
                                var liqPatterns2 = new List<bool>();
                                if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatterns2.Add(true);
                                if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatterns2.Add(false);

                                foreach (var isLiquefaction in liqPatterns2)
                                {
                                    // PileBodySegmentループ（安全に null チェック）
                                    var soilPiles = inputModel.ElementDivision?.SoilPiles;
                                    if (soilPiles == null) continue;
                                    int soilIndex = pileLayoutDataItem.SoilPileAltNo - 1;
                                    if (soilIndex < 0 || soilIndex >= soilPiles.Count) continue;

                                    var soilPile = soilPiles[soilIndex];
                                    if (soilPile?.PileBodySegments == null) continue;

                                    for (int i = 0; i < soilPile.PileBodySegments.Count; i++)
                                    {
                                        var pileBodySegment = soilPile.PileBodySegments[i];
                                        if (pileBodySegment == null) continue;
                                        if (pileBodySegment.No != selectedSegmentNo) continue;

                                        // safety: beams list must exist and contain index i
                                        if (pileLayoutDataItem.Beams == null || i < 0 || i >= pileLayoutDataItem.Beams.Count) continue;
                                        var beam = pileLayoutDataItem.Beams[i];
                                        if (beam == null) continue;

                                        // GetBeamResult may return null or have null subproperties -> guard
                                        var beamResult = beam.GetBeamResult(anaModel, loadCase, loadCombination, isLiquefaction);
                                        var cumForce = beamResult?.CumulativeForce;
                                        if (cumForce == null) continue;

                                        double momentInPile = cumForce.MabsMax;
                                        double shearInPile = cumForce.FabsMax;

                                        // Node results may be missing -> fallback to 0.0
                                        double uhI = 0.0, uhJ = 0.0;
                                        try
                                        {
                                            var nodeIResult = beam.NodeI?.GetNodeResult(anaModel, loadCase, loadCombination, isLiquefaction);
                                            var nodeJResult = beam.NodeJ?.GetNodeResult(anaModel, loadCase, loadCombination, isLiquefaction);
                                            uhI = nodeIResult?.CumulativeDisp?.Uh ?? 0.0;
                                            uhJ = nodeJResult?.CumulativeDisp?.Uh ?? 0.0;
                                        }
                                        catch
                                        {
                                            uhI = 0.0; uhJ = 0.0;
                                        }

                                        double dispInPile = Math.Max(uhI, uhJ);

                                        int k = loadCase.Level - 1;
                                        if (k < 0 || k > 1) continue; // ensure valid index for level (expects 1 or 2)

                                        // Ensure per-segment lists have been initialized; they were created earlier with two elements
                                        Qmaxs[^1][k] = Math.Max(Qmaxs[^1][k], shearInPile);
                                        Mmaxs[^1][k] = Math.Max(Mmaxs[^1][k], momentInPile);
                                        Dmaxs[^1][k] = Math.Max(Dmaxs[^1][k], dispInPile);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // レベルごとに解析結果が存在するか判定（実際にデータが収集されたかで判定）
            bool hasLevel1Results = (inputModel.LoadCasesInput?.LoadCasesLevel1?.Any(x => x.IsApplicable) ?? false)
                && (Qmaxs.Any(q => q[0] > double.MinValue) || Mmaxs.Any(m => m[0] > double.MinValue));
            bool hasLevel2Results = (inputModel.LoadCasesInput?.LoadCasesLevel2?.Any(x => x.IsApplicable) ?? false)
                && (Qmaxs.Any(q => q[1] > double.MinValue) || Mmaxs.Any(m => m[1] > double.MinValue));

            BuildAnalysisResultSummaryTable(
            body,
            selectedPileBodies,
            selectedSegment,
            selectedSegmentTop,
            selectedSegmentBtm,
            Qmaxs,
            Mmaxs,
            NMaxs,
            NMins,
            Dmaxs,
            hasLevel1Results,
            hasLevel2Results
            );

        }

        private void BuildAnalysisResultSummaryTable(
            Body body,
            List<string> selectedPileBodies,
            List<int> selectedSegment,
            List<double> selectedSegmentTop,
            List<double> selectedSegmentBtm,

            List<List<double>> Qmaxs,
            List<List<double>> Mmaxs,
            List<List<double>> NMaxs,
            List<List<double>> NMins,
            List<List<double>> Dmaxs,
            bool hasLevel1Results,
            bool hasLevel2Results
            )
        {
            for (int k = 0; k < 2; k++)
            {
                // 該当レベルの解析結果がない場合はスキップ
                if (k == 0 && !hasLevel1Results) continue;
                if (k == 1 && !hasLevel2Results) continue;

                AddLineBreak(body);
                AddAutoFigureCaption(body, $"杭検討結果まとめ一覧（レベル{k + 1}地震）", "表");

                var table = new Table();
                var tableProps = new TableProperties(
                    // 表の幅を100%（紙面いっぱい）に設定
                    new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                    // 列幅を内容に応じて自動調整
                    new TableLayout { Type = TableLayoutValues.Autofit },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                        new BottomBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                        new LeftBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                        new RightBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 }
                    )
                );
                table.AppendChild(tableProps);

                for (int rowIdx = 1; rowIdx <= selectedPileBodies.Count + 1; rowIdx++)
                {
                    TableRow row = new();

                    for (int colIdx = 1; colIdx <= 9; colIdx++)
                    {
                        TableCell cell = new();

                        if (rowIdx == 1)
                        {
                            if (colIdx == 1) SetTableCellWithVerticalAlign(cell, GetParagraph("杭符号", "center", 8), "center");
                            else if (colIdx == 2) SetTableCellWithVerticalAlign(cell, GetParagraph("区間No", "center", 8), "center");
                            else if (colIdx == 3) SetTableCellWithVerticalAlign(cell, GetParagraph("上端深さ\n[m]", "center", 8), "center");
                            else if (colIdx == 4) SetTableCellWithVerticalAlign(cell, GetParagraph("下端深さ\n[m]", "center", 8), "center");
                            else if (colIdx == 5) SetTableCellWithVerticalAlign(cell, GetParagraph("D<_max>\n[m]", "center", 8), "center");
                            else if (colIdx == 6) SetTableCellWithVerticalAlign(cell, GetParagraph("Q<_max>\n[kN]", "center", 8), "center");
                            else if (colIdx == 7) SetTableCellWithVerticalAlign(cell, GetParagraph("M<_max>\n[kNm]", "center", 8), "center");
                            else if (colIdx == 8) SetTableCellWithVerticalAlign(cell, GetParagraph("N<_Max>\n[kN]", "center", 8), "center");
                            else if (colIdx == 9) SetTableCellWithVerticalAlign(cell, GetParagraph("N<_Min>\n[kN]", "center", 8), "center");
                        }
                        else
                        {
                            int i = rowIdx - 2;
                            if (colIdx == 1) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedPileBodies[i]}", "center", 8), "center");
                            else if (colIdx == 2) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegment[i]}", "center", 8), "center");
                            else if (colIdx == 3) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentTop[i]}", "center", 8), "center");
                            else if (colIdx == 4) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentBtm[i]}", "center", 8), "center");
                            else if (colIdx == 5) SetTableCellWithVerticalAlign(cell, GetParagraph($"{Dmaxs[i][k]:N3}", "center", 8), "center");
                            else if (colIdx == 6) SetTableCellWithVerticalAlign(cell, GetParagraph($"{Qmaxs[i][k]:N1}", "center", 8), "center");
                            else if (colIdx == 7) SetTableCellWithVerticalAlign(cell, GetParagraph($"{Mmaxs[i][k]:N1}", "center", 8), "center");
                            else if (colIdx == 8) SetTableCellWithVerticalAlign(cell, GetParagraph($"{NMaxs[i][k]:N1}", "center", 8), "center");
                            else if (colIdx == 9) SetTableCellWithVerticalAlign(cell, GetParagraph($"{NMins[i][k]:N1}", "center", 8), "center");
                        }

                        row.Append(cell);
                    }

                    // 1行目を繰返しヘッダー行に設定
                    if (rowIdx == 1)
                    {
                        var trPr = row.GetFirstChild<TableRowProperties>();
                        if (trPr == null)
                        {
                            trPr = new TableRowProperties();
                            row.PrependChild(trPr);
                        }
                        if (!trPr.Elements<TableHeader>().Any())
                            trPr.Append(new TableHeader());
                    }

                    table.Append(row);
                }

                body.Append(table);
            }
        }

        private void AddLoadCombinationTable(MainDocumentPart mainDocumentPart, Body body)
        {
            // 1. Table, TableRow, TableCellを作成
            var table = new Table();

            // 黒線の罫線プロパティを追加
            var borders = new TableBorders(
                new TopBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                new RightBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 }
            );
            table.AppendChild(new TableProperties(borders));

            var loadCasesInput = inputModel.LoadCasesInput;
            if (loadCasesInput?.LoadCombinations == null || loadCasesInput.LoadCombinations.Count == 0)
                return;
            int loadCombinationCount = loadCasesInput.LoadCombinations.Count;

            // テーブルの行を作成
            for (int rowIdx = 1; rowIdx <= 11; rowIdx++)
            {
                TableRow row = new();

                for (int colIdx = 1; colIdx <= loadCombinationCount + 1; colIdx++)
                {
                    TableCell cell = new();

                    if (rowIdx == 1)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("作用の組み合わせ", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            var para = GetParagraph($"{colIdx - 1}", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }
                    // 2行目かつ2～5列目にDrawingを挿入
                    else if (rowIdx == 2)
                    {
                        if (colIdx == 1)
                        {
                            cell.Append(new Paragraph(new Run(new Text(""))));
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            int loadCombinationNo = colIdx - 2;

                            // フォールバック付きで ps/pf を決定
                            double ps = 0, pf = 0;
                            if (loadCasesInput.LoadCasesLevel2 != null && loadCasesInput.LoadCasesLevel2.Count > 0)
                            {
                                ps = loadCasesInput.LoadCasesLevel2[0].UpperMassForce;
                                pf = loadCasesInput.LoadCasesLevel2[0].FoundationMassForce;
                            }
                            else if (loadCasesInput.LoadCasesLevel1 != null && loadCasesInput.LoadCasesLevel1.Count > 0)
                            {
                                ps = loadCasesInput.LoadCasesLevel1[0].UpperMassForce;
                                pf = loadCasesInput.LoadCasesLevel1[0].FoundationMassForce;
                            }

                            // 列定義に合わせて LoadCombinations を使用
                            var comb = loadCasesInput.LoadCombinations[loadCombinationNo];
                            double alphaL = comb.Alpha1;
                            double betaU = comb.Beta1;
                            double betaL = comb.Beta2;

                            Drawing drawing = CreateLoadCombinationDiagramDrawing(mainDocumentPart, ps, pf, alphaL, betaU, betaL);
                            Paragraph para = new(drawing.CloneNode(true));
                            cell.Append(para);
                        }
                    }

                    else if (rowIdx == 3)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("α<_L>", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            int loadCombinationNo = colIdx - 2;
                            double alpha1 = loadCasesInput.LoadCombinations[loadCombinationNo].Alpha1;
                            var para = GetParagraph($"{alpha1:N2}", "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }

                    else if (rowIdx == 4)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("β<_U>", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            int loadCombinationNo = colIdx - 2;
                            double beta1 = loadCasesInput.LoadCombinations[loadCombinationNo].Beta1;
                            var para = GetParagraph($"{beta1:N2}", "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }

                    else if (rowIdx == 5)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("β<_L>", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            int loadCombinationNo = colIdx - 2;
                            double beta2 = loadCasesInput.LoadCombinations[loadCombinationNo].Beta2;
                            var para = GetParagraph($"{beta2:N2}", "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }

                    else if (rowIdx == 6)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("レベル1\n上部構造\n慣性力\nP<_s> [kN]", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            string psText = string.Empty;
                            if (loadCasesInput.LoadCasesLevel1 != null)
                            {
                                for (int i = 0; i < loadCasesInput.LoadCasesLevel1.Count; i++)
                                {
                                    var lc = loadCasesInput.LoadCasesLevel1[i];
                                    psText += $"{lc.LoadName}: {lc.UpperMassForce:N1}";
                                    psText += i < loadCasesInput.LoadCasesLevel1.Count - 1 ? "\n" : string.Empty;
                                }
                            }
                            var para = GetParagraph(psText, "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }
                    else if (rowIdx == 7)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("レベル1\n基礎部\n慣性力\nP<_f> [kN]", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            string pfText = string.Empty;
                            if (loadCasesInput.LoadCasesLevel1 != null)
                            {
                                for (int i = 0; i < loadCasesInput.LoadCasesLevel1.Count; i++)
                                {
                                    var lc = loadCasesInput.LoadCasesLevel1[i];
                                    pfText += $"{lc.LoadName}: {lc.FoundationMassForce:N1}";
                                    pfText += i < loadCasesInput.LoadCasesLevel1.Count - 1 ? "\n" : string.Empty;
                                }
                            }
                            var para = GetParagraph(pfText, "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }
                    else if (rowIdx == 8)
                    {
                        if (colIdx == 1)
                        {
                            var para = GetParagraph("レベル1\nβ<_U>・P<_s>＋β<_L>・P<_f>\n[kN]", "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            string pText = string.Empty;
                            if (loadCasesInput.LoadCasesLevel1 != null)
                            {
                                for (int i = 0; i < loadCasesInput.LoadCasesLevel1.Count; i++)
                                {
                                    var lc = loadCasesInput.LoadCasesLevel1[i];
                                    int loadCombinationNo = colIdx - 2;
                                    double force = lc.UpperMassForce * loadCasesInput.LoadCombinations[loadCombinationNo].Beta1
                                                 + lc.FoundationMassForce * loadCasesInput.LoadCombinations[loadCombinationNo].Beta2;
                                    pText += $"{lc.LoadName}: {force:N1}";
                                    pText += i < loadCasesInput.LoadCasesLevel1.Count - 1 ? "\n" : string.Empty;
                                }
                            }
                            var para = GetParagraph(pText, "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }
                    else if (rowIdx == 9 || rowIdx == 10 || rowIdx == 11)
                    {
                        // 同様に L2 側も null 安全化
                        bool isRow9 = rowIdx == 9;
                        bool isRow10 = rowIdx == 10;
                        bool isRow11 = rowIdx == 11;

                        if (colIdx == 1)
                        {
                            string label = isRow9 ? "レベル2\n上部構造\n慣性力\nP<_s> [kN]"
                                        : isRow10 ? "レベル2\n基礎部\n慣性力\nP<_f> [kN]"
                                        : "レベル2\nβ<_U>・P<_s>＋β<_L>・P<_f>\n[kN]";
                            var para = GetParagraph(label, "center", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                        else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                        {
                            string text = string.Empty;
                            if (loadCasesInput.LoadCasesLevel2 != null)
                            {
                                for (int i = 0; i < loadCasesInput.LoadCasesLevel2.Count; i++)
                                {
                                    var lc = loadCasesInput.LoadCasesLevel2[i];
                                    text += $"{lc.LoadName}: ";
                                    if (isRow9)
                                        text += $"{lc.UpperMassForce:N1}";
                                    else if (isRow10)
                                        text += $"{lc.FoundationMassForce:N1}";
                                    else
                                    {
                                        int loadCombinationNo = colIdx - 2;
                                        double force = lc.UpperMassForce * loadCasesInput.LoadCombinations[loadCombinationNo].Beta1
                                                     + lc.FoundationMassForce * loadCasesInput.LoadCombinations[loadCombinationNo].Beta2;
                                        text += $"{force:N1}";
                                    }
                                    text += i < loadCasesInput.LoadCasesLevel2.Count - 1 ? "\n" : string.Empty;
                                }
                            }
                            var para = GetParagraph(text, "right", 8);
                            SetTableCellWithVerticalAlign(cell, para, "center");
                        }
                    }
                    //else if (rowIdx == 10)
                    //{
                    //    if (colIdx == 1)
                    //    {
                    //        var para = GetParagraph("レベル2\n基礎部\n慣性力\nP<_f>", "center", 8);
                    //        SetTableCellWithVerticalAlign(cell, para, "center");
                    //    }

                    //    else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                    //    {
                    //        string pf = string.Empty;
                    //        for (int i = 0; i < loadCasesInput.LoadCasesLevel2.Count; i++)
                    //        {
                    //            pf += $"{loadCasesInput.LoadCasesLevel2[i].LoadName}: ";
                    //            pf += $"{loadCasesInput.LoadCasesLevel2[i].FoundationMassForce:N1}";
                    //            pf += i < loadCasesInput.LoadCasesLevel2.Count - 1 ? "\n" : string.Empty;
                    //        }
                    //        var para = GetParagraph(pf, "right", 8);
                    //        SetTableCellWithVerticalAlign(cell, para, "center");
                    //    }
                    //}

                    //else if (rowIdx == 11)
                    //{
                    //    if (colIdx == 1)
                    //    {
                    //        var para = GetParagraph("レベル2\nβ<_U>・P<_s>＋β<_L>・P<_f>", "center", 8);
                    //        SetTableCellWithVerticalAlign(cell, para, "center");
                    //    }
                    //    else if (colIdx >= 2 && (colIdx - 2) < loadCasesInput.LoadCombinations.Count)
                    //    {
                    //        string p = string.Empty;
                    //        for (int i = 0; i < loadCasesInput.LoadCasesLevel2.Count; i++)
                    //        {
                    //            p += $"{loadCasesInput.LoadCasesLevel2[i].LoadName}: ";
                    //            int loadCombinationNo = colIdx - 2;
                    //            double force =
                    //                loadCasesInput.LoadCasesLevel2[i].UpperMassForce *
                    //                loadCasesInput.LoadCombinations[loadCombinationNo].Beta1 +
                    //                loadCasesInput.LoadCasesLevel2[i].FoundationMassForce *
                    //                loadCasesInput.LoadCombinations[loadCombinationNo].Beta2;
                    //            p += $"{force:N1}";
                    //            p += i < loadCasesInput.LoadCasesLevel2.Count - 1 ? "\n" : string.Empty;

                    //        }
                    //        var para = GetParagraph(p, "right", 8);
                    //        SetTableCellWithVerticalAlign(cell, para, "center");
                    //    }
                    //}
                    else
                    {
                        cell.Append(new Paragraph(new Run(new Text("")))); // 空セル
                    }

                    row.Append(cell);
                }
                table.Append(row);
            }
            // 8. bodyにTableを追加
            body.Append(table);
        }


        // 番号付きヘッダースタイル
        public static void EnsureHeadingStylesWithNumbering(MainDocumentPart mainPart)
        {
            if (mainPart == null) return;

            // StylesPart
            var stylesPart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles ??= new Styles();
            var styles = stylesPart.Styles;

            // NumberingPart
            var numberingPart = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering ??= new Numbering();
            var numbering = numberingPart.Numbering;

            const int targetAbstractNumId = 1;
            const int targetNumberId = 1;

            // AbstractNum 取得/作成
            var abstractNum = numbering.Elements<AbstractNum>()
                .FirstOrDefault(n => n.AbstractNumberId != null && n.AbstractNumberId.Value == targetAbstractNumId);

            if (abstractNum == null)
            {
                abstractNum = new AbstractNum
                {
                    AbstractNumberId = new Int32Value(targetAbstractNumId)
                };

                for (int lvl = 0; lvl <= 5; lvl++)
                {
                    string levelText = string.Join(".", Enumerable.Range(1, lvl + 1).Select(i => $"%{i}")) + ".";
                    var level = new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.Decimal },
                        new LevelText { Val = levelText }
                    )
                    {
                        LevelIndex = new Int32Value(lvl)
                    };

                    // インデント（必要に応じ調整）
                    level.Append(new PreviousParagraphProperties(
                        new Indentation
                        {
                            Left = (lvl * 420).ToString(),
                            Hanging = "420"
                        }));

                    abstractNum.Append(level);
                }

                numbering.Append(abstractNum);
            }
            else
            {
                // 不足レベル追加
                var existing = new HashSet<int>(abstractNum.Elements<Level>()
                    .Where(l => l.LevelIndex != null)
                    .Select(l => l.LevelIndex.Value));

                for (int lvl = 0; lvl <= 5; lvl++)
                {
                    if (existing.Contains(lvl)) continue;

                    string levelText = string.Join(".", Enumerable.Range(1, lvl + 1).Select(i => $"%{i}")) + ".";
                    var level = new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.Decimal },
                        new LevelText { Val = levelText }
                    )
                    {
                        LevelIndex = new Int32Value(lvl)
                    };

                    level.Append(new PreviousParagraphProperties(
                        new Indentation
                        {
                            Left = (lvl * 420).ToString(),
                            Hanging = "420"
                        }));

                    abstractNum.Append(level);
                }
            }

            // NumberingInstance
            if (!numbering.Elements<NumberingInstance>()
                .Any(i => i.NumberID != null && i.NumberID.Value == targetNumberId))
            {
                numbering.Append(new NumberingInstance(
                    new AbstractNumId { Val = new Int32Value(targetAbstractNumId) })
                {
                    NumberID = new Int32Value(targetNumberId)
                });
            }

            // 共通 RunProperties
            var commonRunProps = new RunProperties(CreateDefaultRunFonts());

            // Heading1～Heading6 スタイル
            for (int h = 1; h <= 6; h++)
            {
                string styleId = $"Heading{h}";
                if (styles.Elements<WpStyle>().Any(s => s.StyleId != null && s.StyleId.Value == styleId))
                    continue;

                int outline = h - 1;

                var style = new WpStyle(
                    new StyleName { Val = $"見出し {h}" },
                    new BasedOn { Val = "Normal" },
                    new NextParagraphStyle { Val = "Normal" },
                    new UIPriority { Val = new Int32Value(9 + h) },
                    new PrimaryStyle(),
                    new StyleRunProperties(commonRunProps.CloneNode(true))
                )
                {
                    Type = StyleValues.Paragraph,
                    StyleId = styleId
                };

                var pPr = new StyleParagraphProperties(
                    new NumberingProperties(
                        new NumberingLevelReference { Val = new Int32Value(outline) },
                        new NumberingId { Val = new Int32Value(targetNumberId) }
                    ),
                    new OutlineLevel { Val = new Int32Value(outline) }
                );

                pPr.Append(new Indentation
                {
                    Left = (outline * 420).ToString(),
                    Hanging = "420"
                });

                style.Append(pPr);
                styles.Append(style);
            }

            stylesPart.Styles.Save();
            numberingPart.Numbering.Save();
        }

        // 汎用番号付きリスト(レベル0)の Numbering 定義を用意
        public static void EnsureListNumberingSafe(MainDocumentPart mainPart, int abstractNumId = 90, int numberId = 90)
        {
            if (mainPart == null) return;

            var numberingPart = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering ??= new Numbering();

            // 既存定義があれば何もしない（再生成しない）
            if (numberingPart.Numbering.Elements<NumberingInstance>()
                .Any(n => n.NumberID?.Value == numberId))
                return;

            // AbstractNum が未定義なら追加
            var abstractNum = numberingPart.Numbering.Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
            if (abstractNum == null)
            {
                abstractNum = new AbstractNum(
                    new Nsid { Val = Guid.NewGuid().ToString("N")[..8] },
                    new MultiLevelType { Val = MultiLevelValues.SingleLevel },
                    new TemplateCode { Val = "0409001D" },
                    new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.Decimal },
                        new LevelText { Val = "%1." },
                        new LevelJustification { Val = LevelJustificationValues.Left },
                        new PreviousParagraphProperties(
                            new Indentation { Left = "360", Hanging = "360" }
                        )
                    )
                    { LevelIndex = 0 }
                )
                {
                    AbstractNumberId = abstractNumId
                };
                numberingPart.Numbering.Append(abstractNum);
            }

            // NumberingInstance を追加
            numberingPart.Numbering.Append(
                new NumberingInstance(
                    new AbstractNumId { Val = abstractNumId }
                )
                {
                    NumberID = numberId
                });

            numberingPart.Numbering.Save();
        }


        // 箇条書き(Bullet)用 Numbering 定義を安全生成
        private static void EnsureBulletListNumberingSafe(
            MainDocumentPart mainPart,
            int abstractNumId = 200,
            int numberId = 200,
            string bullet = "•")
        {
            if (mainPart == null) return;

            var numberingPart = mainPart.NumberingDefinitionsPart ?? mainPart.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering ??= new Numbering();

            // 既に同じ numberId が存在すれば終了
            if (numberingPart.Numbering.Elements<NumberingInstance>().Any(n => n.NumberID?.Value == numberId))
                return;

            // AbstractNum 存在チェック
            var abstractNum = numberingPart.Numbering.Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);

            if (abstractNum == null)
            {
                // 箇条書きレベル0のみ（必要になれば拡張）
                abstractNum = new AbstractNum
                {
                    AbstractNumberId = abstractNumId
                };

                var level = new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Bullet },
                    new LevelText { Val = bullet }, // 実際に表示される記号
                    new LevelJustification { Val = LevelJustificationValues.Left }
                )
                {
                    LevelIndex = 0
                };

                // 記号フォント（一般的な黒丸は "Symbol" でなくても表示されるが安全側で設定）
                level.Append(new NumberingSymbolRunProperties(
                    new RunFonts
                    {
                        Ascii = "Symbol",
                        HighAnsi = "Symbol",
                        EastAsia = "ＭＳ ゴシック",
                        Hint = FontTypeHintValues.Default
                    })
                );

                level.Append(new PreviousParagraphProperties(
                    new Indentation { Left = "360", Hanging = "360" }
                ));

                abstractNum.Append(level);
                numberingPart.Numbering.Append(abstractNum);
            }

            // NumberingInstance 追加
            numberingPart.Numbering.Append(
                new NumberingInstance(
                    new AbstractNumId { Val = abstractNumId }
                )
                {
                    NumberID = numberId
                });

            numberingPart.Numbering.Save();
        }


        // 図表番号付きタイトルの挿入メソッド
        public void AddAutoFigureCaption(Body body, string captionText, string label = "図", double fontSize = 10.5)
        {
            // コード側で番号をインクリメント（SEQ の F9 更新に依存しない）
            int number;
            if (label == "図") number = ++_figureCounter;
            else if (label == "表") number = ++_tableCounter;
            else number = 1;
            // ラベル（例: "図", "表", "Figure", "Table"）を追加
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Center },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Caption" }
                }
            };

            // SEQ 識別子は日本語識別子だと Word で解釈されない環境があるため、
            // 可視ラベルとは分離して Latin 識別子を使う（TOC \c と合わせる）。
            string seqIdentifier = label switch
            {
                "図" => "Figure",
                "表" => "Table",
                _ => label
            };

            FontSize fontSizeVal() => new FontSize { Val = (fontSize * 2).ToString() };

            // ラベル Run
            var labelRun = new Run(new RunProperties(fontSizeVal()));
            labelRun.Append(new Text($"{label} ") { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(labelRun);

            // SEQ フィールド: Begin / InstrText / Separate / 結果 / End を個別 Run で構成。
            // 各 Run の WithRunProperties で同じフォントサイズを維持。
            var beginRun = new Run(new RunProperties(fontSizeVal()));
            beginRun.Append(new FieldChar { FieldCharType = FieldCharValues.Begin });
            paragraph.Append(beginRun);

            var instrRun = new Run(new RunProperties(fontSizeVal()));
            instrRun.Append(new FieldCode($" SEQ {seqIdentifier} \\* ARABIC ")
            { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(instrRun);

            var separateRun = new Run(new RunProperties(fontSizeVal()));
            separateRun.Append(new FieldChar { FieldCharType = FieldCharValues.Separate });
            paragraph.Append(separateRun);

            var valueRun = new Run(new RunProperties(fontSizeVal()));
            valueRun.Append(new Text(number.ToString())); // コード側で計算した番号を書き込む
            paragraph.Append(valueRun);

            var endRun = new Run(new RunProperties(fontSizeVal()));
            endRun.Append(new FieldChar { FieldCharType = FieldCharValues.End });
            paragraph.Append(endRun);

            // キャプション本文
            var captionRun = new Run(new RunProperties(fontSizeVal()));
            captionRun.Append(new Text($" {captionText}") { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(captionRun);

            body.Append(paragraph);
        }

        // スコットプロット挿入メソッド
        /// <summary>
        /// 杭地盤セット単位で複数杭の系列を 1 図にオーバーレイするバージョン。
        /// xsByPanelBySeries[panel][series] = x データ配列、ys は全系列共通の Z 軸、
        /// soilSeries は 0 番パネル（変位）に破線で重ねる地盤変位（杭ごと）。
        /// </summary>
        public static void AddPileElevResultMultiToBody(
            MainDocumentPart mainPart, Body body,
            List<List<List<double>>> xsByPanelBySeries,
            List<List<double>> ysPerSeries,
            List<string> legends,
            List<List<double>> soilDispsPerSeries,
            List<string> titles, List<string> xLabels, List<string> yLabels,
            double widthMm = 150, double heightMm = 100)
        {
            ScottPlot.Multiplot multiplot = new();
            int panelCount = xsByPanelBySeries?.Count ?? 0;
            if (panelCount == 0) return;
            multiplot.AddPlots(panelCount);

            for (int p = 0; p < panelCount; p++)
            {
                var plot = multiplot.Subplots.GetPlot(p);
                var seriesList = xsByPanelBySeries[p];
                int seriesCount = seriesList.Count;

                for (int s = 0; s < seriesCount; s++)
                {
                    double[] xs = [.. seriesList[s]];
                    double[] ys = [.. (s < ysPerSeries.Count ? ysPerSeries[s] : ysPerSeries[^1])];
                    var scatter = plot.Add.ScatterLine(xs, ys);
                    if (legends != null && s < legends.Count)
                        scatter.LegendText = legends[s];

                    // 変位パネル（0 番）: 対応する地盤変位を同色破線で重ねる
                    if (p == 0 && soilDispsPerSeries != null && s < soilDispsPerSeries.Count)
                    {
                        double[] soilXs = [.. soilDispsPerSeries[s]];
                        if (soilXs.Length == ys.Length)
                        {
                            var soilScatter = plot.Add.ScatterLine(soilXs, ys);
                            soilScatter.LineStyle.Color = scatter.LineStyle.Color;
                            soilScatter.LineStyle.Pattern = LinePattern.Dashed;
                        }
                    }
                }

                if (seriesCount > 1 || p == 0) plot.ShowLegend();

                var grayColor = new ScottPlot.Color(128, 128, 128, 255);
                plot.Add.VerticalLine(0, 1, grayColor);
                plot.Add.HorizontalLine(0, 1, grayColor);

                plot.Axes.Title.Label.Text = titles[p] ?? "";
                plot.Axes.Bottom.Label.Text = xLabels[p] ?? "";
                plot.Axes.Left.Label.Text = yLabels[p] ?? "";
                plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(titles[p] ?? "メイリオ");
                plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabels[p] ?? "メイリオ");
                plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabels[p] ?? "メイリオ");
                plot.Legend.FontName = ScottPlot.Fonts.Detect("凡例");
            }

            multiplot.Layout = new ScottPlot.MultiplotLayouts.Columns();

            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            int widthPx = MmToPx(widthMm, Dpi, 2.0);
            int heightPx = MmToPx(heightMm, Dpi, 2.0);
            multiplot.SavePng(tempFile, widthPx, heightPx);
            WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        public static void AddPileElevResultToBody(
            MainDocumentPart mainPart, Body body,
            List<List<double>> xsLists, List<List<double>> ysLists,
            List<string> titles, List<string> xLabels, List<string> yLabels,
            double widthMm = 150, double heightMm = 150)
        {
            ScottPlot.Multiplot multiplot = new();

            int count = Math.Min(3, Math.Min(xsLists?.Count ?? 0, ysLists?.Count ?? 0));
            multiplot.AddPlots(count);

            List<Plot> plots = [];
            for (int i = 0; i < count; i++)
            {
                plots.Add(multiplot.Subplots.GetPlot(i));

                double[] xsArray = [.. xsLists[i]];
                double[] ysArray = [.. ysLists[i]];
                var scatter = plots[i].Add.ScatterLine(xsArray, ysArray);

                // 変位図（先頭パネル）に地盤変位を重ねるのは xsLists にcount+1個の要素があるときのみ
                if (i == 0 && xsLists.Count > count)
                {
                    scatter.LegendText = "杭変位";

                    double[] xsArrayS = [.. xsLists[count]];
                    if (xsArrayS.Length == ysArray.Length)
                    {
                        var soilScatter = plots[i].Add.ScatterLine(xsArrayS, ysArray);
                        // 杭変位と同じ色で破線にする
                        var pileColor = scatter.LineStyle.Color;
                        soilScatter.LineStyle.Color = pileColor;
                        soilScatter.LineStyle.Pattern = LinePattern.Dashed;
                        soilScatter.LegendText = "地盤変位";
                    }

                    plots[i].ShowLegend();
                }

                // X=0, Y=0 の補助線
                var grayColor = new ScottPlot.Color(128, 128, 128, 255);
                plots[i].Add.VerticalLine(0, 1, grayColor);
                plots[i].Add.HorizontalLine(0, 1, grayColor);

                plots[i].Axes.Title.Label.Text = titles[i];
                plots[i].Axes.Bottom.Label.Text = xLabels[i];
                plots[i].Axes.Left.Label.Text = yLabels[i];
                plots[i].Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(titles[i] ?? "メイリオ");
                plots[i].Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabels[i] ?? "メイリオ");
                plots[i].Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabels[i] ?? "メイリオ");
                plots[i].Legend.FontName = ScottPlot.Fonts.Detect("凡例");
            }

            // apply a custom layout
            multiplot.Layout = new ScottPlot.MultiplotLayouts.Columns();

            // 2. 一時画像ファイルとして保存
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            //int widthPx = (int)(widthMm / 25.4 * 96 * 2);
            int widthPx = MmToPx(widthMm, Dpi, 2.0);
            //int heightPx = (int)(heightMm / 25.4 * 96 * 2);
            int heightPx = MmToPx(heightMm, Dpi, 2.0);
            //wpf.Plot.SavePng(tempFile, widthPx, heightPx);
            multiplot.SavePng(tempFile, widthPx, heightPx);

            // 3. Word文書のbodyに画像挿入
            WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);

            // 4. 一時ファイル削除
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }

        // スコットプロット挿入メソッド
        public static void AddScottPlotGraphToBody(
            MainDocumentPart mainPart, Body body,
            List<double> xsList, List<double> ysList,
            string title, string xLabel, string yLabel,
            double widthMm = 150, double heightMm = 150)
        {
            try
            {
                var pngBytes = DiagramRenderer.RenderScottPlotToPngBytes(wpf =>
                {
                    double[] xs = xsList?.ToArray() ?? [];
                    double[] ys = ysList?.ToArray() ?? [];
                    if (xs.Length > 0 && ys.Length > 0)
                        wpf.Plot.Add.ScatterLine(xs, ys);

                    // X=0, Y=0 の補助線
                    var grayColor = new ScottPlot.Color(128, 128, 128, 255);
                    wpf.Plot.Add.VerticalLine(0, 1, grayColor);
                    wpf.Plot.Add.HorizontalLine(0, 1, grayColor);

                    wpf.Plot.Axes.Title.Label.Text = title ?? string.Empty;
                    wpf.Plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
                    wpf.Plot.Axes.Left.Label.Text = yLabel ?? string.Empty;

                    // ScottPlot.Fonts.Detect() を使用して日本語対応フォントを検出
                    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title ?? "メイリオ");
                    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel ?? "メイリオ");
                    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel ?? "メイリオ");
                }, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);

                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddScottPlotGraphToBody: エラー: {ex.Message}");
            }
        }

        public static void AddNMinTScottPlotGraphToBody(
    MainDocumentPart mainPart, Body body,
    List<List<double>> xsLineLists, List<List<double>> ysLineLists, List<string> lineLegends,
    List<List<double>> xsScatterLists, List<List<double>> ysScatterLists, List<string> scatterLegends,
    string title, string xLabel, string yLabel,
    double widthMm = 150, double heightMm = 150, bool showLegend = true)
        {
            try
            {
                // 日本語フォント候補から利用可能なものを選択（環境依存）
                string[] candidates = ["ＭＳ ゴシック", "MS Gothic", "Meiryo", "Yu Gothic UI", "Yu Gothic"];
                string useFont = candidates.FirstOrDefault(fn =>
                {
                    try
                    {
                        return System.Windows.Media.Fonts.SystemFontFamilies.Any(f => f.Source?.IndexOf(fn, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    catch
                    {
                        return false;
                    }
                }) ?? "Meiryo";

                // Multiplot を使ってプロットを構築（WPF 固有の振る舞いの違いを避け、保存時に安定）
                var multiplot = new ScottPlot.Multiplot();
                multiplot.AddPlots(1);
                var plot = multiplot.Subplots.GetPlot(0);

                // 限界状態カーブの色: 安全限界=Red(レベル2), 損傷限界=Green(レベル1), 使用限界=DeepBlue(常時)
                ScottPlot.Color[] lineColors = [
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Red),      // 安全限界(低減前)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Red),      // 安全限界(低減後)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Green),    // 損傷限界(低減前)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Green),    // 損傷限界(低減後)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.DeepBlue), // 使用限界(低減前)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.DeepBlue), // 使用限界(低減後)
                ];

                // 線データの追加
                for (int i = 0; i < xsLineLists.Count; i++)
                {
                    var xs = xsLineLists[i]?.ToArray() ?? [];
                    var ys = ysLineLists[i]?.ToArray() ?? [];
                    if (xs.Length > 0 && ys.Length > 0)
                    {
                        var pl = plot.Add.ScatterLine(xs, ys);
                        pl.LegendText = i < lineLegends.Count ? lineLegends[i] : null;
                        pl.LinePattern = i % 2 == 0 ? LinePattern.Dashed : LinePattern.Solid;
                        pl.LineWidth = i % 2 == 0 ? 1 : 2;
                        pl.MarkerSize = 0;
                        if (i < lineColors.Length)
                            pl.LineColor = lineColors[i];
                    }
                }

                // 散布点の色: レベル2=Red, レベル1=Green, 常時=DeepBlue
                ScottPlot.Color[] scatterColors = [
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Red),
                    ScottPlot.Color.FromSKColor(NikkenSKColor.Green),
                    ScottPlot.Color.FromSKColor(NikkenSKColor.DeepBlue),
                ];

                // 散布データの追加
                for (int i = 0; i < xsScatterLists.Count; i++)
                {
                    var xs = xsScatterLists[i]?.ToArray() ?? [];
                    var ys = ysScatterLists[i]?.ToArray() ?? [];
                    if (xs.Length > 0 && ys.Length > 0)
                    {
                        var sc = plot.Add.Scatter(xs, ys);
                        sc.MarkerSize = 6;
                        sc.LineWidth = 0;
                        sc.LegendText = i < scatterLegends.Count ? scatterLegends[i] : null;
                        if (i < scatterColors.Length)
                            sc.MarkerColor = scatterColors[i];
                    }
                }

                // X=0, Y=0 の補助線
                var grayColor = new ScottPlot.Color(128, 128, 128, 255);
                plot.Add.VerticalLine(0, 1, grayColor);
                plot.Add.HorizontalLine(0, 1, grayColor);

                // タイトル / 軸ラベル / 凡例を明示設定（フォント指定）
                try
                {
                    plot.Axes.Title.Label.Text = title ?? string.Empty;
                    plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
                    plot.Axes.Left.Label.Text = yLabel ?? string.Empty;

                    plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title ?? "メイリオ");
                    plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel ?? "メイリオ");
                    plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel ?? "メイリオ");
                    plot.Legend.FontName = ScottPlot.Fonts.Detect("凡例");
                }
                catch { /* 安全に無視 */ }

                if (showLegend) plot.ShowLegend();
                plot.Axes.AutoScale();

                // 一時ファイルへ保存して Word に貼り込み（既存コードと合わせる）
                string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
                int widthPx = MmToPx(widthMm, Layout.BaseDpi, Layout.HiResScale);
                int heightPx = MmToPx(heightMm, Layout.BaseDpi, Layout.HiResScale);

                // save
                multiplot.SavePng(tempFile, widthPx, heightPx);

                // Word 挿入
                WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);

                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddNMinTScottPlotGraphToBody: エラー: {ex.Message}");
            }
        }

        // 複数データスコットプロット挿入メソッド
        public static void AddScottPlotGraphWithMultipleDataToBody(
            MainDocumentPart mainPart, Body body,
            List<List<double>> xsLists, List<List<double>> ysLists, List<string> legends,
            string title, string xLabel, string yLabel,
            double widthMm = 150, double heightMm = 150)
        //{
        //    // 1. ScottPlotでグラフ作成
        //    WpfPlot wpf = new();
        //    if (xsLists.Count != ysLists.Count || xsLists.Count != legends.Count)
        //    {
        //        return;
        //    }

        //    for (int i = 0; i < xsLists.Count; i++)
        //    {
        //        var xsList = xsLists[i];
        //        var ysList = ysLists[i];
        //        var legend = legends[i];

        //        double[] xsArray = [.. xsList];
        //        double[] ysArray = [.. ysList];
        //        var scatterTemp = wpf.Plot.Add.ScatterLine(xsArray, ysArray);
        //        scatterTemp.LegendText = legend;
        //    }

        //    //string title = "ScottPlotサンプルグラフ";
        //    wpf.Plot.Axes.Title.Label.Text = title;
        //    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);

        //    //string xLabel = "X軸";
        //    wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);

        //    //string yLabel = "Y軸";
        //    wpf.Plot.Axes.Left.Label.Text = yLabel;
        //    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel);

        //    // Legend
        //    wpf.Plot.Legend.FontName = ScottPlot.Fonts.Detect(yLabel);

        //    // 2. 一時画像ファイルとして保存
        //    string tempFile = Path.GetTempFileName() + ".png";
        //    //int widthPx = (int)(widthMm / 25.4 * 96 * 2);
        //    //int heightPx = (int)(heightMm / 25.4 * 96 * 2);
        //    int widthPx = MmToPx(widthMm, Dpi, 2.0);
        //    int heightPx = MmToPx(heightMm, Dpi, 2.0);
        //    wpf.Plot.SavePng(tempFile, widthPx, heightPx);

        //    // 3. Word文書のbodyに画像挿入
        //    WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);

        //    // 4. 一時ファイル削除
        //    if (File.Exists(tempFile)) File.Delete(tempFile);
        //}
        {
            try
            {
                var pngBytes = DiagramRenderer.RenderScottPlotToPngBytes(wpf =>
                {
                    for (int i = 0; i < xsLists.Count; i++)
                    {
                        var xs = xsLists[i]?.ToArray() ?? [];
                        var ys = ysLists[i]?.ToArray() ?? [];
                        if (xs.Length > 0 && ys.Length > 0)
                        {
                            var pl = wpf.Plot.Add.ScatterLine(xs, ys);
                            pl.LegendText = i < legends.Count ? legends[i] : null;
                        }
                    }

                    wpf.Plot.Axes.Title.Label.Text = title ?? string.Empty;
                    wpf.Plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
                    wpf.Plot.Axes.Left.Label.Text = yLabel ?? string.Empty;

                    // ScottPlot.Fonts.Detect() を使用して日本語対応フォントを検出
                    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title ?? "メイリオ");
                    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel ?? "メイリオ");
                    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel ?? "メイリオ");
                    wpf.Plot.Legend.FontName = ScottPlot.Fonts.Detect("凡例");

                    wpf.Plot.ShowLegend();
                    wpf.Plot.Axes.AutoScale();
                }, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);

                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddScottPlotGraphWithMultipleDataToBody: エラー: {ex.Message}");
            }
        }

        // 改行メソッド
        public static void AddLineBreak(Body body)
        {
            body.Append(new Paragraph());
        }

        // 改ページメソッド
        public static void AddPageBreak(Body body)
        {
            var paragraph = new Paragraph(
                new Run(
                    new Break() { Type = BreakValues.Page }
                )
            );
            body.Append(paragraph);
        }


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
            string imagePartId = "rId" + Guid.NewGuid().ToString("N");
            var mainPart = mainDocumentPart;
            var imagePart = mainPart.AddImagePart(ImagePartType.Png, imagePartId);
            using (var stream = new MemoryStream(imageBytes))
                imagePart.FeedData(stream);

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
                System.Diagnostics.Debug.WriteLine($"図の作成でエラー: {ex.Message}");
                // フォールバック: 何もしないか、プレースホルダを追加する
            }
        }

        // 荷重コンビネーションイメージ挿入
        public static void SaveLoadCombinationDiagramByMm(
            string filePath,
            double ps,
            double pf,
            double alphaL,
            double betaU,
            double betaL,
            int widthMm = 30,
            int heightMm = 30,
            float dpi = 192)
        {
            // mm→px変換
            //int widthPx = (int)Math.Round(widthMm * dpi / 25.4);
            //int heightPx = (int)Math.Round(heightMm * dpi / 25.4);
            int widthPx = MmToPx(widthMm, dpi);
            int heightPx = MmToPx(heightMm, dpi);

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
                    typeface,
                    fontSize,
                    brush, ppd
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
                    typeface,
                    fontSize,
                    brush, ppd
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
                    typeface,
                    fontSize,
                    brush, ppd
                    /*VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip*/)
                {
                    TextAlignment = System.Windows.TextAlignment.Center
                };
                dc.DrawText(ft3, new Point(parabolaEndX, (parabolaStartY + parabolaEndY) * 0.5 - ft3.Height));
            }
            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            // bmpは、AddImageToBodyByMmによるWord貼りこみ時の不具合を避けるため、96dpi固定で作成する必要があります。

            bmp.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(filePath, FileMode.Create);
            encoder.Save(fs);
        }

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
                var pngBytes = DiagramRenderer.RenderPileForceElevationPngBytes(soilPile, springType, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);
                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainDocumentPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddPileForceDiagramByMm: 図作成エラー: {ex.Message}");
                // 必要ならプレースホルダ段落を追加
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
                    if (inputModel?.PileBodies == null) return 1.0;
                    int bodyNo = pli?.PileBodyNo ?? 0;
                    if (bodyNo <= 0 || bodyNo > inputModel.PileBodies.Count) return 1.0;
                    var pb = inputModel.PileBodies[bodyNo - 1];
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
                System.Diagnostics.Debug.WriteLine($"AddPilingLayoutDiagramByMm: 図作成エラー: {ex.Message}");
                // 必要に応じプレースホルダ段落を追加するなどのフォールバック処理を入れてください
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
                var settlementData = pgs?.SettlementGridData;
                if (settlementData == null || settlementData.Count == 0)
                    return;

                var gridXs = pgs?.SettlementGridX?.ToList();
                var gridYs = pgs?.SettlementGridY?.ToList();

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
                    AddAutoFigureCaption(body, "群杭沈下コンタ図", "図");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddGroupPileSettlementContourDiagram: 図作成エラー: {ex.Message}");
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
            AddAutoFigureCaption(body, "各杭位置の沈下量一覧", "表");

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
            int no = 0;
            foreach (var pli in pileLayoutItems)
            {
                no++;
                double singleSettle = pli.SinglePileSettlementVL;
                double groupSettle = pli.GroupPileSettlement;
                double totalSettle = singleSettle + groupSettle;

                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{no}"], fontSize, "center"));
                dataRow.Append(CreateTableCell([$"{pli.Point3D.X:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pli.Point3D.Y:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{singleSettle:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groupSettle:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{totalSettle:N3}"], fontSize, "right"));
                table.Append(dataRow);
            }

            body.Append(table);
        }

        /// <summary>
        /// 基礎梁考慮鉛直解析結果のテーブルを出力
        /// </summary>
        private void AddVerticalBeamResultTables(Body body)
        {
            var caseResults = mainWindowViewModel.VerticalBeamCaseResults;
            if (caseResults == null || caseResults.Count == 0) return;

            double fontSize = 8;

            foreach (var caseResult in caseResults)
            {
                // 杭反力・沈下量テーブル
                if (caseResult.PileResults != null && caseResult.PileResults.Count > 0)
                {
                    AddLineBreak(body);
                    AddAutoFigureCaption(body, $"基礎梁考慮鉛直解析 杭反力・沈下量（{caseResult.LoadCaseName}）", "表");

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
                    AddAutoFigureCaption(body, $"基礎梁考慮鉛直解析 梁応力（{caseResult.LoadCaseName}）", "表");

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

                int cnt = Math.Min(beam.BeamResults.Count, loadCases?.Count ?? 0);
                for (int i = 0; i < cnt; i++)
                {
                    var lc = loadCases[i];
                    if (lc?.IsApplicable != true) continue;

                    var res = beam.GetBeamResult(anaModel, lc, loadCombination, isLiquefaction)?.CumulativeForce;
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
                            var resL = beam.GetBeamResult(anaModel, lc, comb, true)?.CumulativeForce;
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
                            var resN = beam.GetBeamResult(anaModel, lc, comb, false)?.CumulativeForce;
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
                int cnt = Math.Min(loadCases?.Count ?? 0, axialLoads?.Count ?? 0);
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
                int cnt = Math.Min(pileLayoutItem.IsFrontPiles?.Count ?? 0, loadCases?.Count ?? 0);
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
            double length = Math.Sqrt(dx * dx + dy * dy);

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
            double length = Math.Sqrt(dx * dx + dy * dy);

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
        public void SavePileForceElevationDiagramByMm(
            string filePath,
            double widthMm,
            double heightMm,
            SoilPile soilPile,
            string springType = "horizontal",
            int dpi = 192
        )
        {
            ArgumentNullException.ThrowIfNull(soilPile);
            if (soilPile.PileBodyInput == null) return; // 必要なら例外化
            if (soilPile.PileBodySegments == null || soilPile.PileBodySegments.Count == 0)
            {
                // 空データ: 何も描かず終了（必要ならプレースホルダ出力）
                return;
            }

            var segments = soilPile.PileBodySegments;
            // 安全な最大径算出
            double maxSegDia = segments.Count > 0
                ? segments.Max(s => (double)(s?.PileSection?.PileDiameter ?? 0))
                : 0.0;
            double toeDia = soilPile.PileBodyInput.PileToeDia;
            double diaMax = Math.Max(toeDia, maxSegDia) * 0.001;

            double pileDepth = segments[^1].SegmentDepth;

            // 定数
            //const double lineWidth = 2;
            const double lineWidthThick = 3;
            const double lineWidthThin = 1;
            const double nodeRadius = 5;
            const double topMargin = 1; // m
            const double btmMargin = 5; // m
            const double horMargin = 1; // m

            PileBodyInput pileBody = inputModel.PileBodies[soilPile.PileBodyNo - 1];

            // mm→px変換
            //int widthPx = (int)Math.Round(widthMm * dpi / 25.4);
            //int heightPx = (int)Math.Round(heightMm * dpi / 25.4);
            int widthPx = MmToPx(widthMm, dpi);
            int heightPx = MmToPx(heightMm, dpi);

            // 最大径取得
            //double diaMax = Math.Max(soilPile.PileBodyInput.PileToeDia * 0.001,
            //    soilPile.PileBodySegments.Max(seg => seg.PileSection.PileDiameter * 0.001));
            //double pileDepth = soilPile.PileBodySegments[^1].SegmentDepth;

            //// 描画範囲
            //double minX = -diaMax * 0.5, maxX = diaMax * 0.5;
            //double minY = -pileDepth, maxY = 0;
            //double midX = (maxX + minX) * 0.5;
            ////double midY = (maxY + minY) * 0.5;
            //double midY = (topMargin - maxY + minY - btmMargin) * 0.5;

            //// スケール
            //double scale = Math.Min(
            //    widthPx / (maxX - minX + 2 * horMargin),
            //    heightPx / (maxY - minY + topMargin + btmMargin)
            //);

            //// 座標変換
            //Point ToImagePoint(double x, double y) =>
            //    new(widthPx * 0.5 + (x - midX) * scale, heightPx * 0.5 - (y - midY) * scale);

            //var dv = new DrawingVisual();
            //using (var dc = dv.RenderOpen())
            //{
            //    // 背景
            //    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

            //    // 杭描画
            //    foreach (var segment in soilPile.PileBodySegments)
            //    {
            //        double dia = segment.PileSection.PileDiameter * 0.001;
            //        double topY = -(segment.SegmentDepth - segment.SegmentLength);
            //        double bottomY = -segment.SegmentDepth;
            //        Point topLeft = ToImagePoint(midX - dia * 0.5, topY);
            //        double dx = dia * scale, dy = segment.SegmentLength * scale;
            //        Point upperNode = ToImagePoint(0, topY);
            //        Point bottomNode = ToImagePoint(0, bottomY);

            //        dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1), new Rect(topLeft.X, topLeft.Y, dx, dy));
            //        dc.DrawLine(new Pen(Brushes.Black, lineWidthThick), bottomNode, upperNode);
            //        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), upperNode, nodeRadius, nodeRadius);
            //    }

            //    if (soilPile.PileBodySegments.Count > 0)
            //    {
            //        int lastIndex = soilPile.PileBodySegments.Count - 1;
            //        if (soilPile.PileBodySegments[lastIndex]?.PileSection?.PileDiameter != null)
            //        {
            //            double _bottomSegmentDia = soilPile.PileBodySegments[lastIndex].PileSection.PileDiameter / 1000.0;
            //            double _pileToeDia = pileBody.PileToeDia / 1000.0;
            //            if (pileBody.PileConstructionType == "場所打ちコンクリート杭" && _pileToeDia > _bottomSegmentDia)
            //            {
            //                var pileToeAngle = pileBody.InsituPileToeAngle;
            //                var pileToeHeight = pileBody.InsituPileToeHeight * 0.001;

            //                // ポリゴン描画
            //                Point[] points =
            //                [
            //                    ToImagePoint(- _bottomSegmentDia * 0.5, (-pileDepth + pileToeHeight + ((_pileToeDia - _bottomSegmentDia) * 0.5 * Math.Tan((90-pileToeAngle) * Math.PI / 180)))),
            //                    ToImagePoint(- _pileToeDia * 0.5, (-pileDepth + pileToeHeight)),
            //                    ToImagePoint(- _pileToeDia * 0.5, -pileDepth),
            //                    ToImagePoint(+ _pileToeDia * 0.5, -pileDepth),
            //                    ToImagePoint(+ _pileToeDia * 0.5, (-pileDepth + pileToeHeight)),
            //                    ToImagePoint(+ _bottomSegmentDia * 0.5, (-pileDepth + pileToeHeight + ((_pileToeDia - _bottomSegmentDia) * 0.5 * Math.Tan((90-pileToeAngle) * Math.PI / 180))))
            //                ];
            //                StreamGeometry geometry = new();
            //                using (var ctx = geometry.Open())
            //                {
            //                    ctx.BeginFigure(points[0], true, true);
            //                    ctx.PolyLineTo([.. points.Skip(1)], true, true);
            //                }
            //                geometry.Freeze();
            //                dc.DrawGeometry(null, new Pen(NikkenBrush.SkyBlue, 1), geometry);

            //                // 横線
            //                dc.DrawLine(
            //                    new Pen(NikkenBrush.SkyBlue, 1),
            //                    ToImagePoint(+_pileToeDia * 0.5, (-pileDepth + pileToeHeight)),
            //                    ToImagePoint(-_pileToeDia * 0.5, (-pileDepth + pileToeHeight))
            //                );

            //                // 破線
            //                double _height = (_pileToeDia - _bottomSegmentDia) * 0.5 * Math.Tan(78 * Math.PI / 180);
            //                for (int i = -1; i < 2; i += 2)
            //                {
            //                    Pen dashedPen = new(NikkenBrush.SkyBlue, 1) { DashStyle = new DashStyle([2], 0) };
            //                    dc.DrawLine(
            //                        dashedPen,
            //                        ToImagePoint(_bottomSegmentDia * 0.5 * i, (-pileDepth + pileToeHeight + _height)),
            //                        ToImagePoint(_bottomSegmentDia * 0.5 * i, (-pileDepth))
            //                    );
            //                }
            //            }

            //            if ((pileBody.PileConstructionType == "埋込み杭（プレボーリング）" && _pileToeDia > _bottomSegmentDia) ||
            //                (pileBody.PileConstructionType == "埋込み杭（中掘り）" && _pileToeDia > _bottomSegmentDia))
            //            {
            //                var toeHeightRatio = pileBody.PrecastConcretePileToeHeightRatio;
            //                // ポリゴン描画
            //                double _height = _pileToeDia * toeHeightRatio;
            //                Point[] points =
            //                [
            //                    ToImagePoint( - _pileToeDia * 0.5, (-pileDepth + _height)),
            //                    ToImagePoint( - _pileToeDia * 0.5, -pileDepth),
            //                    ToImagePoint( _pileToeDia * 0.5, -pileDepth),
            //                    ToImagePoint( _pileToeDia * 0.5, (-pileDepth + _height))
            //                ];
            //                StreamGeometry geometry = new();
            //                using (var ctx = geometry.Open())
            //                {
            //                    ctx.BeginFigure(points[0], true, true);
            //                    ctx.PolyLineTo([.. points.Skip(1)], true, true);
            //                }
            //                geometry.Freeze();
            //                dc.DrawGeometry(null, new Pen(NikkenBrush.SkyBlue, 1), geometry);

            //                // 破線
            //                for (int i = -1; i < 2; i += 2)
            //                {
            //                    Pen dashedPen = new(NikkenBrush.SkyBlue, 1) { DashStyle = new DashStyle([2], 0) };
            //                    dc.DrawLine(
            //                        dashedPen,
            //                        ToImagePoint(_bottomSegmentDia * 0.5 * i, (-pileDepth - _height)),
            //                        ToImagePoint(_bottomSegmentDia * 0.5 * i, (-pileDepth))
            //                    );
            //                }
            //            }
            //        }
            //    }

            //    // 杭先端節点
            //    Point toeNode = ToImagePoint(0, -pileDepth);
            //    dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), toeNode, nodeRadius, nodeRadius);


            //    // 地盤層
            //    var groundLayers = soilPile.GroundInput.GroundLayers;
            //    double groundTop = soilPile.GroundInput.GroundTopAltitude - soilPile.Z;
            //    double groundTopPx = -groundTop * scale + ToImagePoint(0, 0).Y;
            //    if (0 <= groundTopPx && groundTopPx <= heightPx)
            //        dc.DrawLine(new Pen(Brushes.Black, lineWidthThin), new(0, groundTopPx), new(widthPx, groundTopPx));
            //    foreach (var layer in groundLayers)
            //    {
            //        double yPx = -(layer.BottomAltitude - soilPile.Z) * scale + ToImagePoint(0, 0).Y;
            //        if (0 <= yPx && yPx <= heightPx)
            //            dc.DrawLine(new Pen(Brushes.Black, lineWidthThin), new(0, yPx), new(widthPx, yPx));

            //        // text
            //        double textyPx = yPx - layer.LayerThickness * 0.5 * scale;
            //        Point textPoint = new(0, textyPx);
            //        var typeface = new Typeface(Layout.FontName);
            //        var brush = Brushes.Black;
            //        var fontSize = 16;
            //        var text = $"{layer.Name}, Es = {layer.Es:N0}kN/m2";
            //        var ft = new FormattedText(
            //            text,
            //            System.Globalization.CultureInfo.CurrentCulture,
            //            FlowDirection.LeftToRight,
            //            typeface,
            //            fontSize,
            //            brush,
            //            VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip);
            //        dc.DrawText(ft, textPoint);
            //    }

            //    // N値
            //    double leftX = widthPx * 0.75, rightX = widthPx * 0.90, nPointDia = 5, maxN = 60;
            //    double nScale = (rightX - leftX) / maxN;
            //    for (int i = 0; i <= maxN; i += 10)
            //    {
            //        double x = leftX + i * nScale;
            //        dc.DrawLine(new Pen(Brushes.Black, lineWidthThin), new(x, groundTopPx), new(x, heightPx));
            //    }

            //    List<Point> nValuePoints = [];
            //    foreach (var mass in soilPile.GroundInput.GroundMassesData)
            //    {
            //        double depth = mass.AltitudeDepth - soilPile.Z;
            //        double nValue = mass.NValue;
            //        Point nValuePoint = new(leftX + nValue * nScale, -depth * scale + ToImagePoint(0, 0).Y);
            //        nValuePoints.Add(nValuePoint);
            //        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), nValuePoint, nPointDia * 0.5, nPointDia * 0.5);

            //        var typeface = new Typeface(Layout.FontName);
            //        var brush = Brushes.Black;
            //        var fontSize = 16;
            //        var text = $"{mass.NValue}";
            //        var ft = new FormattedText(
            //            text,
            //            System.Globalization.CultureInfo.CurrentCulture,
            //            FlowDirection.LeftToRight,
            //            typeface,
            //            fontSize,
            //            brush,
            //            VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).PixelsPerDip);
            //        dc.DrawText(ft, nValuePoint);

            //    }

            //    // ポリライン描画
            //    for (int i = 0; i < nValuePoints.Count - 1; i++)
            //    {
            //        dc.DrawLine(new Pen(Brushes.Black, lineWidthThin), nValuePoints[i], nValuePoints[i + 1]);
            //    }

            //    if (springType == "horizontal")
            //    {
            //        // ローラー支承三角形
            //        double triHeight = 15, triWidth = 20;
            //        var triPts = new[]
            //        {
            //        toeNode,
            //        toeNode + new Vector(-triWidth * 0.5, triHeight),
            //        toeNode + new Vector(triWidth * 0.5, triHeight),
            //        toeNode
            //    };
            //        for (int i = 0; i < triPts.Length - 1; i++)
            //            dc.DrawLine(new Pen(Brushes.Black, lineWidthThick), triPts[i], triPts[i + 1]);

            //        // ローラー支承コロ
            //        double rollerRadius = triWidth * 0.25;
            //        Point leftRoller = toeNode + new Vector(-rollerRadius, triHeight + rollerRadius);
            //        Point rightRoller = toeNode + new Vector(rollerRadius, triHeight + rollerRadius);
            //        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), leftRoller, rollerRadius, rollerRadius);
            //        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), rightRoller, rollerRadius, rollerRadius);
            //        dc.DrawLine(new Pen(Brushes.Black, lineWidthThick),
            //        leftRoller + new Vector(-rollerRadius * 3, rollerRadius),
            //        rightRoller + new Vector(rollerRadius * 3, rollerRadius));


            //        // 地盤変位
            //        double dispBaseX = 0.35 * widthPx;
            //        double maxDisp = soilPile.ZDataItems.Max(z => Math.Max(Math.Max(z.GroundDisp1, z.GroundDisp2), Math.Max(z.GroundDisp1L, z.GroundDisp2L)));
            //        double dispRatio = maxDisp > 0 ? 50 / maxDisp : 1;

            //        void DrawDispLine(IEnumerable<PileZDataItem> zs, Func<PileZDataItem, double> dispSelector, Brush brush)
            //        {
            //            var pts = zs.Select(z => new Point(dispBaseX + dispSelector(z) * dispRatio, ToImagePoint(0, z.Z).Y)).ToList();
            //            for (int i = 0; i < pts.Count - 1; i++)
            //                dc.DrawLine(new Pen(brush, lineWidthThick), pts[i], pts[i + 1]);
            //        }

            //        DrawDispLine(soilPile.ZDataItems, z => z.GroundDisp1, Brushes.Khaki);
            //        DrawDispLine(soilPile.ZDataItems, z => z.GroundDisp2, Brushes.DarkKhaki);
            //        DrawDispLine(soilPile.ZDataItems, z => z.GroundDisp1L, Brushes.SlateBlue);
            //        DrawDispLine(soilPile.ZDataItems, z => z.GroundDisp2L, Brushes.DarkSlateGray);

            //        // ジグザグばね
            //        foreach (var z in soilPile.ZDataItems)
            //        {
            //            Point ptDisp2L = new(dispBaseX + z.GroundDisp2L * dispRatio, ToImagePoint(0, z.Z).Y);
            //            dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick), ptDisp2L, nodeRadius, nodeRadius);
            //            Point ptPile = new(widthPx * 0.5, ToImagePoint(0, z.Z).Y);
            //            DrawSpringZigzagBySegmentLength(dc, ptDisp2L, ptPile, 10, 36, new Pen(Brushes.DarkGray, 2));
            //        }

            //    }
            //    else if (springType == "vertical")
            //    {
            //        double armlength = diaMax;
            //        double xPx = widthPx * 0.5;
            //        double xLeftPx = widthPx * 0.5 - diaMax * 0.5 * scale;
            //        double xRightPx = widthPx * 0.5 + diaMax * 0.5 * scale;
            //        double springLengthPx = 0.5 * scale;

            //        // ジグザグばね
            //        foreach (var z in soilPile.ZDataItems)
            //        {
            //            double yPx = ToImagePoint(0, z.Z).Y;
            //            dc.DrawLine(new Pen(Brushes.Black, lineWidthThick), new(xLeftPx, yPx), new(xRightPx, yPx));
            //            Point ptPile = new(xPx, yPx);
            //            DrawSpringZigzagBySegmentLength(
            //                dc, new(xLeftPx, yPx), new(xLeftPx, yPx + springLengthPx), 6, 18, new Pen(Brushes.DarkGray, 2), 0.1 * scale);
            //            DrawSpringZigzagBySegmentLength(
            //                dc, new(xRightPx, yPx), new(xRightPx, yPx + springLengthPx), 6, 18, new Pen(Brushes.DarkGray, 2), 0.1 * scale);
            //        }
            //        double yBottomPx = ToImagePoint(0, soilPile.ZDataItems[^1].Z).Y;
            //        Point pointBottomPx = new(widthPx * 0.5, yBottomPx);
            //        Point pointSpringBottomPx = new(widthPx * 0.5, yBottomPx + springLengthPx);
            //        DrawSpringZigzagBySegmentLength(
            //            dc, pointBottomPx, pointSpringBottomPx, 6, 18, new Pen(Brushes.DarkGray, 2), 0.1 * scale);
            ////    }
            //}

            //// 画像保存
            //var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            //bmp.Render(dv);
            //var encoder = new PngBitmapEncoder();
            //encoder.Frames.Add(BitmapFrame.Create(bmp));
            //using var fs = new FileStream(filePath, FileMode.Create);
            //encoder.Save(fs);

            double minX = -diaMax * 0.5, maxX = diaMax * 0.5;
            double minY = -pileDepth, maxY = 0;
            double midX = (maxX + minX) * 0.5;
            double midY = (topMargin - maxY + minY - btmMargin) * 0.5;

            double scale = Math.Min(
                widthPx / (maxX - minX + 2 * horMargin),
                heightPx / (maxY - minY + topMargin + btmMargin));

            Point ToImagePoint(double x, double y) =>
                new(widthPx * 0.5 + (x - midX) * scale,
                    heightPx * 0.5 - (y - midY) * scale);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, widthPx, heightPx));

                foreach (var segment in segments)
                {
                    double dia = (segment.PileSection?.PileDiameter ?? 0) * 0.001;
                    double topY = -(segment.SegmentDepth - segment.SegmentLength);
                    double bottomY = -segment.SegmentDepth;
                    var topLeft = ToImagePoint(midX - dia * 0.5, topY);
                    double dx = dia * scale;
                    double dy = segment.SegmentLength * scale;
                    var upperNode = ToImagePoint(0, topY);
                    var bottomNode = ToImagePoint(0, bottomY);

                    dc.DrawRectangle(null, new Pen(NikkenBrush.SkyBlue, 1),
                        new Rect(topLeft.X, topLeft.Y, dx, dy));
                    dc.DrawLine(new Pen(Brushes.Black, lineWidthThick), bottomNode, upperNode);
                    dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick),
                        upperNode, nodeRadius, nodeRadius);
                }

                // 先端節点
                var toeNode = ToImagePoint(0, -pileDepth);
                dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick),
                    toeNode, nodeRadius, nodeRadius);

                // 地盤層描画（null安全）
                var groundLayers = soilPile.GroundInput?.GroundLayers;
                if (groundLayers != null)
                {
                    double groundTop = soilPile.GroundInput.GroundTopAltitude - soilPile.Z;
                    double groundTopPx = -groundTop * scale + ToImagePoint(0, 0).Y;
                    if (0 <= groundTopPx && groundTopPx <= heightPx)
                        dc.DrawLine(new Pen(Brushes.Black, lineWidthThin),
                            new Point(0, groundTopPx),
                            new Point(widthPx, groundTopPx));

                    foreach (var layer in groundLayers)
                    {
                        double yPx = -(layer.BottomAltitude - soilPile.Z) * scale + ToImagePoint(0, 0).Y;
                        if (0 <= yPx && yPx <= heightPx)
                            dc.DrawLine(new Pen(Brushes.Black, lineWidthThin),
                                new Point(0, yPx), new Point(widthPx, yPx));
                    }
                }

                // N値プロット（安全化）
                var masses = soilPile.GroundInput?.GroundMassesData;
                if (masses != null && masses.Count > 0)
                {
                    double leftX = widthPx * 0.75, rightX = widthPx * 0.90;
                    double nPointDia = 5, maxN = 60;
                    double nScale = (rightX - leftX) / maxN;
                    var nPts = new List<Point>();

                    foreach (var m in masses)
                    {
                        double depth = m.AltitudeDepth - soilPile.Z;
                        var pt = new Point(leftX + m.NValue * nScale,
                            -depth * scale + ToImagePoint(0, 0).Y);
                        nPts.Add(pt);
                        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, lineWidthThick),
                            pt, nPointDia * 0.5, nPointDia * 0.5);
                    }
                    for (int i = 0; i < nPts.Count - 1; i++)
                        dc.DrawLine(new Pen(Brushes.Black, lineWidthThin), nPts[i], nPts[i + 1]);
                }

                // 変位系（ZDataItems が空ならスキップ）
                var zItems = soilPile.ZDataItems;
                if (zItems != null && zItems.Count > 0)
                {
                    if (springType == "horizontal")
                    {
                        double dispBaseX = 0.35 * widthPx;
                        double maxDisp = zItems.Max(z =>
                            Math.Max(Math.Max(z.GroundDisp1, z.GroundDisp2),
                                     Math.Max(z.GroundDisp1L, z.GroundDisp2L)));
                        double dispRatio = maxDisp > 0 ? 50 / maxDisp : 1;

                        void DrawDisp(Func<PileZDataItem, double> sel, Brush brush)
                        {
                            for (int i = 0; i < zItems.Count - 1; i++)
                            {
                                var p1 = new Point(dispBaseX + sel(zItems[i]) * dispRatio, ToImagePoint(0, zItems[i].Z).Y);
                                var p2 = new Point(dispBaseX + sel(zItems[i + 1]) * dispRatio, ToImagePoint(0, zItems[i + 1].Z).Y);
                                dc.DrawLine(new Pen(brush, lineWidthThick), p1, p2);
                            }
                        }
                        DrawDisp(z => z.GroundDisp1, Brushes.Khaki);
                        DrawDisp(z => z.GroundDisp2, Brushes.DarkKhaki);
                        DrawDisp(z => z.GroundDisp1L, Brushes.SlateBlue);
                        DrawDisp(z => z.GroundDisp2L, Brushes.DarkSlateGray);
                    }
                    else if (springType == "vertical")
                    {
                        double xCenter = widthPx * 0.5;
                        double halfWidthPx = diaMax * 0.5 * scale;
                        double xLeftPx = xCenter - halfWidthPx;
                        double xRightPx = xCenter + halfWidthPx;
                        double springLengthPx = Math.Max(0.5 * scale, 8.0);
                        Pen linePen = new(Brushes.Black, lineWidthThick);
                        Pen springPen = new(Brushes.DarkGray, 2);
                        foreach (var z in zItems)
                        {
                            double yPx = ToImagePoint(0, z.Z).Y;
                            dc.DrawLine(linePen, new Point(xLeftPx, yPx), new Point(xRightPx, yPx));
                            DrawSpringZigzagBySegmentLength(dc, new Point(xLeftPx, yPx), new Point(xLeftPx, yPx + springLengthPx), 6, 18, springPen, 0.1 * scale);
                            DrawSpringZigzagBySegmentLength(dc, new Point(xRightPx, yPx), new Point(xRightPx, yPx + springLengthPx), 6, 18, springPen, 0.1 * scale);
                        }
                        double yBottomPx = ToImagePoint(0, zItems[^1].Z).Y;
                        DrawSpringZigzagBySegmentLength(
                            dc,
                            new Point(xCenter, yBottomPx),
                            new Point(xCenter, yBottomPx + springLengthPx),
                            6, 18, springPen, 0.1 * scale);
                    }
                }
            }

            var bmp = new RenderTargetBitmap(widthPx, heightPx, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(filePath, FileMode.Create);
            encoder.Save(fs);
        }

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
                var dia = inputModel.PileBodies[pileLayoutItem.PileBodyNo - 1].PileBodySegments[0].PileSection?.PileDiameter ?? 0;
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
                    var pileBody = inputModel.PileBodies[pileLayoutItem.PileBodyNo - 1];
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
            using var fs = new FileStream(filePath, FileMode.Create);
            encoder.Save(fs);
        }

        // タイトルを追加するメソッド
        public static void AddTitle(Body body, string titleText, double fontSize = 16)
        {
            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Center },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Title" }
                }
            };

            Run run = new()
            {
                RunProperties = new RunProperties
                {
                    FontSize = new FontSize { Val = (fontSize * 2).ToString() } // (1ポイント = 2半角文字)
                }
            };

            Text text = new(titleText);
            run.Append(text);
            paragraph.Append(run);
            body.Append(paragraph);
        }

        // 章タイトルを追加するメソッド（$...$ 内を TeX 数式として処理できるように拡張）
        public static void AddHeader1(Body body, string header1Title, int outlineLevel = 0, double fontSize = 12)
        {
            if (body == null) return;

            int headingLevel = Math.Clamp(outlineLevel <= 0 ? 1 : outlineLevel, 1, 9);
            string styleId = $"Heading{headingLevel}";
            int outline = headingLevel - 1;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = styleId },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header1Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }

        // 見出し2（$...$ を TeX として処理・<^x>/< _x> の簡易上付下付も維持）
        public static void AddHeader2(Body body, string header2Title, int outlineLevel = 1, double fontSize = 12)
        {
            if (body == null) return;

            int outline = Math.Max(0, outlineLevel);
            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Heading2" },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header2Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }

        // 見出し3（$...$ を TeX として処理・<^x>/< _x> の簡易上付下付も維持）
        public static void AddHeader3(Body body, string header3Title, int outlineLevel = 1, double fontSize = 12)
        {
            if (body == null) return;

            int outline = Math.Max(0, outlineLevel);
            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = new Justification { Val = JustificationValues.Left },
                    ParagraphStyleId = new ParagraphStyleId { Val = "Heading3" },
                    OutlineLevel = new OutlineLevel { Val = new Int32Value(outline) }
                }
            };

            string text = header3Title ?? string.Empty;

            if (text.Length != 0)
            {
                foreach (var e in BuildInlineMixedRuns(text, fontSize))
                    paragraph.Append(e);
            }

            body.Append(paragraph);
        }


        // 上付き、下付き文字
        // サポートする記法:
        //   <^...> または ^{...} : 上付き文字
        //   <_...> または _{...} : 下付き文字
        public static List<Run> ConvertStringToRunsWithSuperSub(string text, double fontSize = 10.5)
        {
            var runs = new List<Run>();
            int pos = 0;
            // 既存の <^...>, <_...> に加え、LaTeX形式の ^{...}, _{...} もサポート
            var pattern = @"\<\^(.*?)\>|<_(.*?)\>|\^{(.*?)}|_{(.*?)}";

            var matches = Regex.Matches(text, pattern);

            foreach (Match match in matches)
            {
                // 通常文字
                if (match.Index > pos)
                {
                    string normalText = text[pos..match.Index];
                    runs.Add(new Run(
                        new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() }, RunFonts = CreateDefaultRunFonts() },
                        new Text(normalText)
                    ));
                }

                if (match.Groups[1].Success || match.Groups[3].Success) // 上付き (<^...> or ^{...})
                {
                    string superText = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
                            RunFonts = CreateDefaultRunFonts(),
                            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }
                        },
                        new Text(superText)
                    ));
                }
                else if (match.Groups[2].Success || match.Groups[4].Success) // 下付き (<_...> or _{...})
                {
                    string subText = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
                            RunFonts = CreateDefaultRunFonts(),
                            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Subscript }
                        },
                        new Text(subText)
                    ));
                }

                pos = match.Index + match.Length;
            }

            // 残りの通常文字
            if (pos < text.Length)
            {
                string normalText = text[pos..];
                runs.Add(new Run(
                    new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() }, RunFonts = CreateDefaultRunFonts() },
                    new Text(normalText)
                ));
            }

            return runs;
        }

        // テキストを追加するメソッド
        public static void AddText(Body body, string textContent, string alignment = "left", double fontSize = 10.5)
        {
            if (body == null) return;

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Justification = alignment.ToLower() switch
                    {
                        "center" => new Justification { Val = JustificationValues.Center },
                        "right" => new Justification { Val = JustificationValues.Right },
                        _ => new Justification { Val = JustificationValues.Left },
                    }
                }
            };

            if (string.IsNullOrEmpty(textContent))
            {
                body.Append(paragraph); // 空行
                return;
            }

            // 改行対応：行ごとに BuildInlineMixedRuns を適用し、行間に Break を挿入
            string[] lines = textContent.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var element in BuildInlineMixedRuns(lines[i], fontSize))
                    paragraph.Append(element);

                if (i < lines.Length - 1)
                    paragraph.Append(new Run(new Break()));
            }

            body.Append(paragraph);
        }

        // テーブルに枠線を追加するメソッド
        // 表は紙面幅いっぱい（100%）に設定し、列幅は内容に応じて自動調整
        private static Table CreateTableWithBorders()
        {
            Table table = new();
            TableProperties props = new(
                // 表の幅を100%（紙面いっぱい）に設定
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                // 列幅を内容に応じて自動調整
                new TableLayout { Type = TableLayoutValues.Autofit },
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                )
            );
            table.AppendChild(props);
            return table;
        }

        private static Table CreateTableWithBordersAndWidths(params int[] columnWidths)
        // 列幅の比率を維持しながら、表は紙面幅いっぱいに設定
        {
            Table table = new();
            TableProperties props = new(
                // 表の幅を100%（紙面いっぱい）に設定
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                )
            );
            table.AppendChild(props);

            TableGrid tableGrid = new();
            foreach (int width in columnWidths)
            {
                tableGrid.Append(new GridColumn { Width = width.ToString() });
            }
            table.AppendChild(tableGrid);

            return table;
        }

        // テーブルセル
        private static TableCell CreateTableCellWithWidth(string text, string alignment = "left", int width = 0, double fontSize = 8)
        {
            TableCell tableCell = new();
            TableCellProperties tableCellProperties = new();
            TableCellVerticalAlignment tableCellVerticalAlignment = new() { Val = TableVerticalAlignmentValues.Center };
            tableCellProperties.Append(tableCellVerticalAlignment);

            if (width > 0)
            {
                TableCellWidth tableCellWidth = new() { Type = TableWidthUnitValues.Dxa, Width = width.ToString() };
                tableCellProperties.Append(tableCellWidth);
            }

            Paragraph paragraph = new()
            {
                ParagraphProperties = new ParagraphProperties()
            };

            paragraph.ParagraphProperties.Justification = alignment.ToLower() switch
            {
                "center" => new Justification { Val = JustificationValues.Center },
                "right" => new Justification { Val = JustificationValues.Right },
                "left" => new Justification { Val = JustificationValues.Left },
                _ => new Justification { Val = JustificationValues.Left },
            };

            Run run = new()
            {
                RunProperties = new RunProperties
                {
                    FontSize = new FontSize { Val = (fontSize * 2).ToString() } // フォントサイズを設定
                }
            };

            // テキストを改行位置で分割し、各行を追加
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    run.Append(new Break());
                }
                run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            }

            paragraph.Append(run);
            tableCell.Append(tableCellProperties);
            tableCell.Append(paragraph);

            return tableCell;
        }

        // テーブルヘッダー行設定のためのメソッド
        private static TableRow CreateHeaderRow(params TableCell[] cells)
        {
            TableRow headerRow = new();
            foreach (var cell in cells)
            {
                headerRow.Append(cell);
            }

            // ヘッダー行として設定
            TableRowProperties rowProperties = new();
            rowProperties.Append(new TableHeader());
            headerRow.Append(rowProperties);

            return headerRow;
        }

        private static void SetColumnWidth(Table table, int columnIndex, int width)
        {
            try
            {
                TableGrid tableGrid = table.GetFirstChild<TableGrid>();
                if (tableGrid == null)
                {
                    tableGrid = new TableGrid();
                    for (int i = 0; i < columnIndex + 1; i++)
                    {
                        tableGrid.Append(new GridColumn());
                    }
                    table.AppendChild(tableGrid);
                }

                if (tableGrid.ChildElements.Count <= columnIndex)
                {
                    for (int i = tableGrid.ChildElements.Count; i <= columnIndex; i++)
                    {
                        tableGrid.Append(new GridColumn());
                    }
                }

                GridColumn gridColumn = (GridColumn)tableGrid.ChildElements[columnIndex];
                gridColumn.Width = width.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetColumnWidthでエラーが発生しました: {ex.Message}");
            }
        }

        // ダイアグラム保存
        public static void SaveSimpleDiagram(string filePath, int width = 2480, int height = 3508)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 背景
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                // 線
                dc.DrawLine(new Pen(Brushes.Black, 2), new System.Windows.Point(20, 20), new System.Windows.Point(380, 280));
                // 円
                dc.DrawEllipse(null, new Pen(Brushes.Blue, 2), new System.Windows.Point(200, 150), 60, 60);
                // 矩形
                dc.DrawRectangle(null, new Pen(Brushes.Red, 2), new Rect(100, 50, 200, 100));
            }

            var bmp = new RenderTargetBitmap(width, height, 300, 300, PixelFormats.Pbgra32);
            bmp.Render(dv);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = new FileStream(filePath, FileMode.Create);
            encoder.Save(fs);
        }


        readonly string a001 = "基礎部材の強度と変形性能";
        readonly string a002 = "液状化危険度、地盤変形量と液状化程度の予測";
        readonly string a003 = "沈下";

        readonly string a0031 = "単杭の沈下：荷重伝達解析による荷重-沈下量関係の評価（「基礎指針'19」 6.3節、1(2)）を行う。";
        readonly string a0032 = "群杭の沈下:杭ごとに等価荷重面を設定し、杭先端以深の地盤の圧縮量（沈下量）を直接基礎と" +
            "同じくスタインブレナーの近似解（多層地盤の場合（「基礎指針'19」 5.3節、1(3)(iii)））を用いて求める。";
        readonly string a004 = "鉛直支持力および引抜き抵抗力：";
        readonly string a005 = "水平抵抗：「基礎指針'19」6.6節による。";


        readonly string b001 = "場所打ちコンクリート杭の曲げモーメントと曲率の関係";

        readonly string b002 = "断面の平面保持を仮定して、鉄筋とコンクリートの応力度-ひずみ度関係をモデル化し、断面の曲げ解析を行って、M-φ関係を計算する。" +
            "鉄筋の応力度-ひずみ度関係は、規格降伏店を用いたバイリニアとする。コンクリートの応力度-ひずみ度関係にはe関数法を用いる";
        readonly string b003 = "a.曲げひび割れモーメントおよび曲げひび割れ時の曲率##は以下による。";
        readonly string b004 = "b.杭の主筋降伏発生時の曲げモーメント##とその時の曲率##は、断面の曲げ解析による。" +
            "ただし、最外縁の杭主筋が引張降伏するとき（杭の主筋降伏発生時）の曲げモーメントと曲率とする。";
        readonly string b005 = "c.安全限界曲げモーメント時の曲率";



    }
}

