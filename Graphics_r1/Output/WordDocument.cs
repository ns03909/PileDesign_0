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

using Serilog;
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
               && inputModel.PileBodies.Any(pb => pb?.PileTop?.CaptainPile != null
                   && (pb.PileTopType?.Contains("キャプテンパイル工法") ?? false));

        // キャプリングパイル工法 有無判定ヘルパ
        private bool HasCapringPile()
            => inputModel?.PileBodies != null
               && inputModel.PileBodies.Any(pb => pb?.PileTop?.CapringPile != null
                   && (pb.PileTopType?.Contains("キャプリングパイル工法") ?? false));


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

            var sw = new System.Diagnostics.Stopwatch();
            void StartSection() => sw.Restart();
            void EndSection(string label) { sw.Stop(); Log.Information("[Docx]   {Section}: {Elapsed:N2}s", label, sw.Elapsed.TotalSeconds); }

            try
            {
                using var wordDocument = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
                MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();

                StartSection();
                BuildResultLookupCaches();
                EndSection("BuildResultLookupCaches");

                StartSection();
                EnsureHeadingStylesWithNumbering(mainPart);
                EndSection("EnsureHeadingStylesWithNumbering");

                Document doc = new();
                Body body = new();

                // モデル図をキャプチャ（UIスレッド上で実行）
                StartSection();
                byte[]? modelImageBytes = mainWindowViewModel?.CaptureIsometricModelImageBytes();
                EndSection("CaptureIsometricModelImage (UI)");

                StartSection();
                AddFrontMatter(mainPart, body, inputModel, modelImageBytes);
                EndSection("AddFrontMatter");

                StartSection();
                AddInputDataSection(mainPart, body, inputModel);
                EndSection("AddInputDataSection");

                StartSection();
                AddLoadCombinationAndFigureSection(mainPart, body, inputModel);
                EndSection("AddLoadCombinationAndFigureSection");

                // まとめて追加
                StartSection();
                doc.Append(body);
                mainPart.Document = doc;
                mainPart.Document.Save();
                EndSection("Document.Save (zip 書き出し)");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Word 出力中にエラー");
                throw;
            }

            Log.Debug("Word文書を出力しました。Word で開き、目次上をクリック → F9 でフィールド更新してください。");
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
            var sw = new System.Diagnostics.Stopwatch();
            void Time(string label, Action a) { sw.Restart(); a(); sw.Stop(); Log.Information("[Docx]     {Section}: {Elapsed:N2}s", label, sw.Elapsed.TotalSeconds); }

            AddText(body, $"杭検討プログラム ver {(System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString())}", "center");
            AddText(body, DateTime.Now.ToString("yyyy/MM/dd"), "center");

            if (mainWindowViewModel.DocxOutput.IncludeFundamental)
            {
                Time("Fundamental", () => {
                    AddHeader1(body, "基本設定", 1);
                    AddFundamentalTable(body, inputModel.FundamentalInput);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeLoadCondition)
            {
                Time("LoadCondition", () => {
                    AddHeader1(body, "荷重条件", 1);
                    AddText(body, "レベル1荷重");
                    AddLoadCaseTable(body, inputModel.LoadCasesInput.LoadCasesLevel1, inputModel.FundamentalInput);
                    AddText(body, "レベル2荷重");
                    AddLoadCaseTable(body, inputModel.LoadCasesInput.LoadCasesLevel2, inputModel.FundamentalInput);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludePileBodies)
            {
                Time("PileBodies", () => {
                    AddHeader1(body, "杭体", 1);
                    AddPileBodiesTables(body, inputModel.PileBodies);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludePileLayoutTable)
            {
                Time("PileLayout", () => {
                    AddHeader1(body, "杭配置", 1);
                    AddPileLayoutTables(body, inputModel.PileLayoutItems, inputModel.FundamentalInput);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludePileAxialLoad)
            {
                Time("PileAxialLoad", () => {
                    AddHeader1(body, "杭軸力", 1);
                    AddPileAxialLoadTables(body, inputModel.PileLayoutItems);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeIsFrontPile)
            {
                Time("IsFrontPile", () => {
                    AddHeader1(body, "前後方杭", 1);
                    AddIsFrontPileTables(body, inputModel.PileLayoutItems);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeDesignApproach)
            {
                Time("DesignApproach", () => {
                    AddHeader1(body, "検討方針", 1);
                    AddDesignApproachSection(body, inputModel);
                    AddLineBreak(body);
                });
            }

            // 計算条件・仮定 — 材料モデル化オプション等の仮定を明記。
            // 計算書レベル（簡易/詳細）に依らず出力する（仮定の記載は計算書の必須情報）。
            if (mainWindowViewModel.DocxOutput.IncludeAssumptions)
            {
                Time("Assumptions", () => AddCalculationAssumptionsSection(body, inputModel));
            }

            if (mainWindowViewModel.DocxOutput.IncludeGroundInformation)
            {
                Time("GroundInformation", () => {
                    AddHeader1(body, "地盤", 1);
                    AddGroundInfo(body, inputModel.GroundsInput, inputModel.FundamentalInput);
                    AddLineBreak(body);
                });
            }

            // 地盤グラフ (N値分布 / Cu / Vs / Es / FL) — 個別チェックされた項目のみ出力
            Time("GroundGraphsSection", () => AddGroundGraphsSection(mainPart, body));

            // 杭の図・諸元 (杭姿図 / 杭頭諸元 / 軸力制限) — 個別チェックされた項目のみ出力
            Time("PileDiagramsSection", () => AddPileDiagramsSection(mainPart, body));

            // 要素分割関連 (要素分割杭姿図 / 水平地盤反力 / 土圧合力ばね) — 個別チェックされた項目のみ出力
            Time("ElementDivisionSection", () => AddElementDivisionSection(mainPart, body));

            // 解析サマリーレポート (テキスト) — 水平解析完了済かつチェックされた場合のみ
            Time("AnalysisSummaryReportSection", () => AddAnalysisSummaryReportSection(body));

            if (mainWindowViewModel.DocxOutput.IncludeLiquefaction)
            {
                Time("Liquefaction", () => {
                    AddLiquefactionSection(body);
                    AddLineBreak(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeVertical)
            {
                Time("Vertical (支持力+沈下+杭モデル図)", () => {
                    if (mainWindowViewModel.IsVerticalAnalysisDone)
                    {
                        AddHeader1(body, "杭の支持力", 1);
                        AddPileResistanceDescription(body, inputModel.ElementDivision.SoilPiles, inputModel.FundamentalInput);
                        AddVerticalResistance(body, inputModel.ElementDivision.SoilPiles, inputModel.FundamentalInput);
                        AddLineBreak(body);

                        // Smart-MAGNUM 工法の杭がある場合のみ算定根拠表を追加
                        AddSmartMagnumBasisTable(body, inputModel.ElementDivision.SoilPiles);
                        AddHybridKneadingBasisTable(body, inputModel.ElementDivision.SoilPiles);

                        if (mainWindowViewModel.DocxOutput.CalculationReportLevel >= 2)
                        {
                            AddSectionVerticalResistance(body);
                            AddLineBreak(body);
                        }

                        if (mainWindowViewModel.DocxOutput.CalculationReportLevel >= 2)
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

                            // 沈下グラフは「単杭の沈下」節 (IncludeSettlement) で出力する。
                            // 旧実装はここでも AddSettlementGraph を呼び、同じ図表が二重出力されていた。
                        }
                    }
                    else
                    {
                        AddText(body, "（鉛直解析が未実施のため、支持力検討は省略されています）", "left");
                    }
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeHorizontal)
            {
                var swH = new System.Diagnostics.Stopwatch();
                void TimeH(string label, Action a) { swH.Restart(); a(); swH.Stop(); Log.Information("[Docx]       [Horizontal] {Section}: {Elapsed:N2}s", label, swH.Elapsed.TotalSeconds); }

                if (inputModel.EmbedmentInput is { EmbedmentLayersCount: > 0 })
                {
                    TimeH("Embedment (根入部)", () => {
                        AddHeader1(body, "根入部", 1);
                        // 根入れ層の入力データ表（数式説明ではない）のため、計算書レベルによらず出力する。
                        // 旧実装は 詳細(>=2) ゲート内にあり、簡易では空の「根入部」見出しだけが残っていた。
                        AddEmbedment(body, inputModel.EmbedmentInput, inputModel.FundamentalInput);
                        AddLineBreak(body);
                    });
                }
                if (mainWindowViewModel.DocxOutput.CalculationReportLevel >= 2)
                {
                    TimeH("GroundDisplacementSection", () => { AddGroundDisplacementSection(body); AddPageBreak(body); });
                    TimeH("SectionHorizontalResistance", () => { AddSectionHorizontalResistance(body); AddLineBreak(body); });
                }
                AddPageBreak(body);
                // H1 に昇格 (旧: H2 で直前の無関係な H1 の子として番号付けされていた)
                AddHeader1(body, "上部構造、基礎部への作用の組合せ", 1);
                AddText(body, "水平解析では、地盤変位と慣性力の同時作用を以下の係数の組で定義し、組合せごとに解析する。");
                AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                    [Tex(@"\alpha_{L}"), ": 地盤の水平変位に乗じる係数（αL 倍した地盤変位を地盤ばね外端に強制変位として与える）"]);
                AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                    [Tex(@"\beta_{U}"), ": 上部構造慣性力 ", Tex(@"\P_{s}"), " に乗じる係数"]);
                AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                    [Tex(@"\beta_{L}"), ": 基礎部慣性力 ", Tex(@"\P_{f}"), " に乗じる係数（杭頭位置に ", Tex(@"\beta_{U} P_{s} + \beta_{L} P_{f}"), " の水平力を作用させる）"]);
                TimeH("LoadCombinationTable", () => AddLoadCombinationTable(mainPart, body));

                if (mainWindowViewModel.IsHorizontalAnalysisDone
                    && anaModel?.HorizontalSoilSprings != null
                    && anaModel.HorizontalSoilSprings.Any(s => s.NodeI?.Name == "根入部節点"))
                {
                    // 見出しなしで表だけが並んでいたため H2 を付与
                    AddHeader2(body, "根入部反力の合計");
                    TimeH("HorizontalReactionSummaryTable L1", () => { AddLineBreak(body); AddHorizontalReactionSummaryTable(mainPart, body, 1); });
                    TimeH("HorizontalReactionSummaryTable L2", () => { AddLineBreak(body); AddHorizontalReactionSummaryTable(mainPart, body, 2); });
                }

                var soilPiles = inputModel.ElementDivision.SoilPiles;
                if (soilPiles is { Count: > 0 })
                {
                    var soilPile = soilPiles[0];
                    const double pileElevationH = 100;

                    TimeH("PileForceDiagram (horizontal)", () => {
                        AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "horizontal");
                        AddAutoFigureCaption(body, "水平抵抗解析杭モデル", "図");
                    });

                    if (mainWindowViewModel.IsHorizontalAnalysisDone)
                    {
                        TimeH("PileForceSummaryTable", () => AddPileForceSummaryTable(mainPart, body));
                        if (mainWindowViewModel.DocxOutput.IncludeHorizontal_NMinT)
                            TimeH("NMinT (N-M 図)", () => AddNMinT(mainPart, body));
                        if (mainWindowViewModel.DocxOutput.IncludeHorizontal_QNInT)
                            TimeH("QNInT (Q-N 図)", () => AddQNInT(mainPart, body));
                        if (mainWindowViewModel.DocxOutput.IncludeHorizontal_MPhi)
                            TimeH("MPhiCurves (M-φ)", () => AddMPhiCurves(mainPart, body));
                        if (mainWindowViewModel.DocxOutput.IncludeHorizontal_MTheta)
                            TimeH("MThetaCurves (M-θ)", () => AddMThetaCurves(mainPart, body));
                    }
                    else
                    {
                        AddText(body, "（水平解析が未実施のため、解析結果は省略されています）", "left");
                    }
                }

                if (mainWindowViewModel.DocxOutput.CalculationReportLevel >= 2)
                {
                    TimeH("SectionMemberCapacities", () => { AddSectionMemberCapacities(body); AddLineBreak(body); });
                }
            }
            if ((mainWindowViewModel.DocxOutput.IncludeHorizontal_Bending || mainWindowViewModel.DocxOutput.IncludeHorizontal_Shear)
                && mainWindowViewModel.IsHorizontalAnalysisDone && anaModel != null)
            {
                Time("AllPileStressDiagrams (M/Q ダイアグラム)", () => AddAllPileStressDiagrams(mainPart, body,
                    mainWindowViewModel.DocxOutput.IncludeHorizontal_Bending,
                    mainWindowViewModel.DocxOutput.IncludeHorizontal_Shear,
                    mainWindowViewModel.DocxOutput.IncludeHorizontal_StressLimitState));
            }
            // 杭伏図マップ群 — 見出しなしで図が唐突に並んでいたため、いずれかが ON のとき H1 を付与
            if (mainWindowViewModel.DocxOutput.IncludePileLocationMap
                || mainWindowViewModel.DocxOutput.IncludePileAxialLoadMap
                || mainWindowViewModel.DocxOutput.IncludeIsFrontMap
                || mainWindowViewModel.DocxOutput.IncludePileHeadMomentMap
                || mainWindowViewModel.DocxOutput.IncludePileHeadShearMap)
            {
                AddPageBreak(body);
                AddHeader1(body, "杭配置・応力マップ", 1);
            }
            if (mainWindowViewModel.DocxOutput.IncludePileLocationMap)
                Time("PileLocationMap", () => { AddPilingLayoutDiagramByMm(mainPart, body, 150, 200, GetPileBasicMark); AddAutoFigureCaption(body, "杭配置マップ", "図"); });
            if (mainWindowViewModel.DocxOutput.IncludePileAxialLoadMap)
                Time("PileAxialLoadMap", () => { AddPilingLayoutDiagramByMm(mainPart, body, 150, 200, GetPileAxialForceMark); AddAutoFigureCaption(body, "杭軸力マップ", "図"); });
            if (mainWindowViewModel.DocxOutput.IncludeIsFrontMap)
                Time("IsFrontMap", () => { AddPilingLayoutDiagramByMm(mainPart, body, 150, 200, GetPileIsFront); AddAutoFigureCaption(body, "杭前後方杭マップ", "図"); });
            if (mainWindowViewModel.DocxOutput.IncludePileHeadMomentMap)
                Time("PileHeadMomentMap", () => { AddPilingLayoutDiagramByMm(mainPart, body, 150, 200, GetPileTopBendingMomentMark); AddAutoFigureCaption(body, "杭頭モーメントマップ", "図"); });
            if (mainWindowViewModel.DocxOutput.IncludePileHeadShearMap)
                Time("PileHeadShearMap", () => { AddPilingLayoutDiagramByMm(mainPart, body, 150, 200, GetPileTopShearForceMark); AddAutoFigureCaption(body, "杭頭せん断力マップ", "図"); });
            if (mainWindowViewModel.DocxOutput.IncludeHorizontal_NGReport
                && mainWindowViewModel.IsHorizontalAnalysisDone
                && anaModel != null)
            {
                Time("HorizontalEvaluationReport (NG)", () => AddHorizontalEvaluationReport(body, factored: true));
            }
            if (mainWindowViewModel.DocxOutput.IncludeSettlement)
            {
                Time("Settlement (単杭の沈下)", () => {
                    if (mainWindowViewModel.IsVerticalAnalysisDone)
                    {
                        AddPageBreak(body);
                        AddHeader1(body, "単杭の沈下", 1);
                        AddSettlementGraph(mainPart, body);
                    }
                    else
                    {
                        AddText(body, "（鉛直解析が未実施のため、沈下結果は省略されています）", "left");
                    }
                });
            }
            if (mainWindowViewModel.DocxOutput.IncludeGroupPileSettlement)
            {
                Time("GroupPileSettlement (コンタ+杭沈下表)", () => {
                    // 見出しなしで直前セクションに紛れ込んでいたため H1 を付与
                    AddPageBreak(body);
                    AddHeader1(body, "群杭の沈下", 1);
                    AddGroupPileSettlementContourDiagram(mainPart, body);
                    AddPileSettlementTable(body);
                });
            }

            if (mainWindowViewModel.DocxOutput.IncludeVerticalBeamResults)
            {
                Time("VerticalBeamResults", () => {
                    if (mainWindowViewModel.IsVerticalBeamAnalysisDone && mainWindowViewModel.VerticalBeamCaseResults != null)
                    {
                        AddPageBreak(body);
                        AddHeader1(body, "基礎梁考慮鉛直解析結果", 1);
                        AddVerticalBeamResultTables(body);
                    }
                    else
                    {
                        AddText(body, "（基礎梁考慮鉛直解析が未実施のため、結果は省略されています）", "left");
                    }
                });
            }

            // 杭頭工法の説明（FT-Pile / キャプテンパイル / キャプリングパイル）
            // 数式を含むが、設計クライテリア（許容回転角 θa・θu 等）や適用範囲を含むため、
            // 計算書レベルによらず常に出力する（旧実装は 詳細(>=2) のみだった）。
            if (HasFTPile())
            {
                AddDescriptionFTPile(body);
                AddLineBreak(body);
            }

            if (HasCaptainPile())
            {
                AddDescriptionCaptainPile(body);
                AddLineBreak(body);
            }

            if (HasCapringPile())
            {
                AddDescriptionCapringPile(body);
                AddLineBreak(body);
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

            // 目次見出し (3 種類すべて同フォーマット: Bold + 中央 + 14pt)
            // プレースホルダ案内: 日本語 IME 環境では F9 = 全角英数字変換のため、
            // Ctrl+A → F9 だと選択テキストが再変換されてしまう。代わりに
            // 「目次上でクリック → F9」(または右クリック → フィールド更新) を案内する。
            const string updateHint = "（この行をクリック → F9 で更新 / 右クリック → 「フィールド更新」）";

            AppendTocTitle(body, "目次");
            AddTocField(body, $"TOC \\o \"1-{headingLevels}\" \\h \\z \\u", updateHint);
            AddLineBreak(body);

            // 図目次
            AppendTocTitle(body, "図目次");
            // TOC \c の識別子は SEQ 識別子と同じ（Latin）を使う。
            AddTocField(body, "TOC \\h \\z \\c \"Figure\"", updateHint);
            AddLineBreak(body);

            // 表目次
            AppendTocTitle(body, "表目次");
            AddTocField(body, "TOC \\h \\z \\c \"Table\"", updateHint);
        }

        // 目次タイトル段落を統一フォーマットで追加 (Bold + 中央 + 14pt)
        private static void AppendTocTitle(Body body, string title)
        {
            var para = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(
                    new RunProperties(new FontSize { Val = (14 * 2).ToString() }, CreateDefaultRunFonts(), new Bold()),
                    new Text(title))
            );
            body.Append(para);
        }

        /// <summary>TOCフィールドを1つ挿入するヘルパ</summary>
        /// <remarks>
        /// Dirty フラグは敢えて付与しない。Dirty=true を付けると Word が起動時に
        /// 「他のファイルを参照するフィールドが含まれます」というセキュリティ警告ダイアログを表示し
        /// 利用者を不安にさせるため。代わりに利用者が手動で TOC 上を Ctrl+A → F9 で更新する。
        /// fieldCode の前後にスペースを入れることで Word の field parser がトークン分解で失敗しないようにする。
        /// </remarks>
        private static void AddTocField(Body body, string fieldCode, string placeholder)
        {
            var tocPara = new Paragraph();
            tocPara.Append(new Run(new FieldChar
            {
                FieldCharType = FieldCharValues.Begin,
            }));
            var instrRun = new Run();
            // fieldCode は前後に空白を持たせる (例: " TOC \\o ... ") — Word の field parser 互換性
            instrRun.Append(new FieldCode($" {fieldCode.Trim()} ") { Space = SpaceProcessingModeValues.Preserve });
            tocPara.Append(instrRun);
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
            tocPara.Append(new Run(new Text(placeholder)));
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            body.Append(tocPara);
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
                    AddFoundationBeamInputTables(body, fbInput, inputModel);
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
        private static void AddFoundationBeamInputTables(Body body, FoundationBeamInput fbInput, InputModel inputModel)
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

                for (int matIdx = 0; matIdx < fbInput.Materials.Count; matIdx++)
                {
                    var mat = fbInput.Materials[matIdx];
                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{matIdx + 1}", "center", wNo, fs),
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

                for (int secIdx = 0; secIdx < fbInput.Sections.Count; secIdx++)
                {
                    var sec = fbInput.Sections[secIdx];
                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{secIdx + 1}", "center", wNo, fs),
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

                for (int beamIdx = 0; beamIdx < fbInput.Beams.Count; beamIdx++)
                {
                    var beam = fbInput.Beams[beamIdx];
                    // ノード参照の表示文字列を組み立て (Type+Id から表示 No を解決)
                    int nodeINo = inputModel?.GetNodeDisplayNo(beam.NodeI_Type, beam.NodeI_Id) ?? 0;
                    int nodeJNo = inputModel?.GetNodeDisplayNo(beam.NodeJ_Type, beam.NodeJ_Id) ?? 0;
                    string nodeIStr = GetBeamNodeDisplayString(beam.NodeI_Type, nodeINo, fbInput);
                    string nodeJStr = GetBeamNodeDisplayString(beam.NodeJ_Type, nodeJNo, fbInput);

                    TableRow row = new();
                    row.Append(
                        CreateTableCellWithWidth($"{beamIdx + 1}", "center", wNo, fs),
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
                    .Select(l => l.LevelIndex!.Value));

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
            if (panelCount == 0 || xsByPanelBySeries == null) return;
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
            double widthMm = 150, double heightMm = 150,
            // 各パネルに重ねる限界状態ステップライン (xs=limit values, ys=Z)。null なら描画なし。
            // limitXsByPanel[i] が null/空なら i 番パネルにラインを引かない。
            List<List<double>>? limitXsByPanel = null,
            List<List<double>>? limitYsByPanel = null,
            string? limitLegend = null)
        {
            byte[]? pngBytes = RenderPileElevResultToPngBytes(
                xsLists, ysLists, titles, xLabels, yLabels,
                widthMm, heightMm, limitXsByPanel, limitYsByPanel, limitLegend);
            if (pngBytes == null || pngBytes.Length == 0) return;
            WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
        }

        // 杭応力ダイアグラム (変位/せん断/曲げ) の Multiplot を PNG バイト列として返す。
        // Word body を一切触らないため、Parallel.For などで並列実行できる。
        public static byte[]? RenderPileElevResultToPngBytes(
            List<List<double>> xsLists, List<List<double>> ysLists,
            List<string> titles, List<string> xLabels, List<string> yLabels,
            double widthMm = 150, double heightMm = 150,
            List<List<double>>? limitXsByPanel = null,
            List<List<double>>? limitYsByPanel = null,
            string? limitLegend = null)
        {
            ScottPlot.Multiplot multiplot = new();

            int count = Math.Min(3, Math.Min(xsLists?.Count ?? 0, ysLists?.Count ?? 0));
            if (count == 0 || xsLists == null || ysLists == null) return null;
            multiplot.AddPlots(count);

            List<Plot> plots = [];
            for (int i = 0; i < count; i++)
            {
                plots.Add(multiplot.Subplots.GetPlot(i));

                double[] xsArray = [.. xsLists[i]];
                double[] ysArray = [.. ysLists[i]];
                var scatter = plots[i].Add.ScatterLine(xsArray, ysArray);
                var pileColor = scatter.LineStyle.Color;

                // 変位図（先頭パネル）に地盤変位を重ねるのは xsLists にcount+1個の要素があるときのみ
                if (i == 0 && xsLists.Count > count)
                {
                    scatter.LegendText = "杭変位";

                    double[] xsArrayS = [.. xsLists[count]];
                    if (xsArrayS.Length == ysArray.Length)
                    {
                        var soilScatter = plots[i].Add.ScatterLine(xsArrayS, ysArray);
                        // 杭変位と同じ色で破線にする
                        soilScatter.LineStyle.Color = pileColor;
                        soilScatter.LineStyle.Pattern = LinePattern.Dashed;
                        soilScatter.LegendText = "地盤変位";
                    }

                    plots[i].ShowLegend();
                }

                // 限界状態ステップラインを重ねる (せん断力/モーメントパネル用)
                // 正側のみ・破線・応力ラインと同色 (GraphViewModel.DrawPileForce と整合)
                if (limitXsByPanel != null && limitYsByPanel != null
                    && i < limitXsByPanel.Count && i < limitYsByPanel.Count
                    && limitXsByPanel[i] != null && limitYsByPanel[i] != null
                    && limitXsByPanel[i].Count > 0
                    && limitXsByPanel[i].Count == limitYsByPanel[i].Count)
                {
                    double[] limXs = [.. limitXsByPanel[i]];
                    double[] limYs = [.. limitYsByPanel[i]];
                    var limitScatter = plots[i].Add.ScatterLine(limXs, limYs);
                    limitScatter.LineStyle.Color = pileColor;
                    limitScatter.LineStyle.Pattern = LinePattern.Dashed;
                    limitScatter.LineStyle.Width = 1.5f;
                    limitScatter.MarkerStyle.IsVisible = false;
                    limitScatter.LegendText = limitLegend ?? "限界状態";
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

            int widthPx = MmToPx(widthMm, Dpi, 2.0);
            int heightPx = MmToPx(heightMm, Dpi, 2.0);
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            try
            {
                multiplot.SavePng(tempFile, widthPx, heightPx);
                return File.ReadAllBytes(tempFile);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
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
                Log.Warning(ex, "AddScottPlotGraphToBody: エラー");
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

                // 限界状態カーブの色: 安全限界=PaleRed(レベル2), 損傷限界=Green(レベル1), 使用限界=DeepBlue(常時)
                ScottPlot.Color[] lineColors = [
                    ScottPlot.Color.FromSKColor(NikkenSKColor.PaleRed),  // 安全限界(低減前)
                    ScottPlot.Color.FromSKColor(NikkenSKColor.PaleRed),  // 安全限界(低減後)
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

                // 散布点の色: レベル2=PaleRed, レベル1=Green, 常時=DeepBlue
                ScottPlot.Color[] scatterColors = [
                    ScottPlot.Color.FromSKColor(NikkenSKColor.PaleRed),
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

                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch (Exception delEx) { Log.Warning(delEx, "[WordDocument] tempFile delete failed"); }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AddNMinTScottPlotGraphToBody: エラー");
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
                Log.Warning(ex, "AddScottPlotGraphWithMultipleDataToBody: エラー");
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




        // 仕様メモ (将来の docx セクション本文に組み込む予定の説明文。現状未使用):
        //   a001: 基礎部材の強度と変形性能
        //   a002: 液状化危険度、地盤変形量と液状化程度の予測
        //   a003: 沈下
        //   a0031: 単杭の沈下 — 荷重伝達解析による荷重-沈下量関係の評価
        //   a0032: 群杭の沈下 — スタインブレナーの近似解 (多層地盤、基礎指針'19 5.3節 1(3)(iii))
        //   a004: 鉛直支持力および引抜き抵抗力
        //   a005: 水平抵抗 — 基礎指針'19 6.6節による
        //   b001: 場所打ちコンクリート杭の曲げモーメントと曲率の関係
        //   b002: 断面の平面保持仮定、鉄筋/コンクリート応力度-ひずみ度をモデル化
        //   b003: 曲げひび割れモーメント・曲率
        //   b004: 主筋降伏発生時のモーメント・曲率 (断面曲げ解析)
        //   b005: 安全限界曲げモーメント時の曲率



    }
}

