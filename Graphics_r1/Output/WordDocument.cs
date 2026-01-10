using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.EMMA;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LiveChartsCore.Measure;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using ScottPlot;
using ScottPlot.Colormaps;
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
    internal class WordDocument
    {
        private static class Layout
        {
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

        private readonly MainWindowViewModel mainWindowViewModel; // 追加

        private readonly InputModel inputModel;
        private readonly AnaModel anaModel;

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

        // 従来互換（string）
        private static string MmToTwips(double mm)
            => MmToTwipsInt(mm).ToString();

        // mm → EMU
        private static long MmToEmu(double mm)
            => (long)Math.Round(mm * EmuPerMm);

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

        // コンストラクタ
        public WordDocument(InputModel _inputModel, AnaModel _anaModel, MainWindowViewModel _mainWindowViewModel)
        {
            this.inputModel = _inputModel;
            this.anaModel = _anaModel;
            mainWindowViewModel = _mainWindowViewModel;
        }

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

                AddFrontMatter(mainPart, body, inputModel);
                AddInputDataSection(mainPart, body, inputModel);


                AddLoadCombinationAndFigureSection(mainPart, body, inputModel);

                // まとめて追加
                doc.Append(body);
                mainPart.Document = doc;
                mainPart.Document.Save();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Word 出力中にエラー: {ex.Message}");
                throw;
            }

            Console.WriteLine("Word文書を出力しました。開いて Ctrl+A → F9 でフィールド更新してください。");
        }


        // FrontMatter: タイトル・目次・基本説明章
        private void AddFrontMatter(MainDocumentPart mainPart, Body body, InputModel model)
        {
            AddText(body, "杭検討プログラム ver プレプロト", "center");
            AddTitle(body, "基礎ぐいの検討書");

            // 目次
            AddTableOfContents(body, 3);
            AddPageBreak(body);

        }

        // 入力情報・表類
        private void AddInputDataSection(MainDocumentPart mainPart, Body body, InputModel inputModel)
        {
            AddText(body, "杭検討プログラム ver プレプロト", "center");
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
                    const double pileElevationW = 150;
                    const double pileElevationH = 100;

                    AddPileForceDiagramByMm(mainPart, body, widthMm: 150, heightMm: pileElevationH, soilPile, "vertical");
                    AddAutoFigureCaption(body, "沈下解析杭モデル", "図");

                    AddSettlementGraph(mainPart, body);
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

                    AddPileForceSummaryTable(mainPart, body);
                    AddNMINT(mainPart, body);
                }

                // 基礎部材の強度と変形性能
                if (mainWindowViewModel.CalculationReportLevel >= 2)
                {
                    AddSectionMemberCapacities(body);
                    AddLineBreak(body);
                }

            }
            if (mainWindowViewModel.IncludeHorizontal_Bending) // 曲げモーメント
            { }
            if (mainWindowViewModel.IncludeHorizontal_Shear) // せん断力
            { }
            if (mainWindowViewModel.IncludeHorizontal_NMINT) // NMINT
            { }
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
            { }
            if (mainWindowViewModel.IncludeLoadSettlementCurve) // 沈下曲線
            { }

            if (mainWindowViewModel.IncludeGroupPileSettlement) // 群杭沈下
            { }

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
            //    AddNMINT(mainPart, body);
            //}
        }

        // 目次を挿入するヘルパ（OpenXML のフィールドで TOC を作る）
        public static void AddTableOfContents(Body body, int headingLevels = 3)
        {
            if (body == null) return;

            // 目次見出し
            var titlePara = new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new RunProperties(new FontSize { Val = (14 * 2).ToString() })) { }
            );
            titlePara.Append(new Run(new Text("目次")));
            body.Append(titlePara);

            // TOC フィールド（Begin）
            var tocPara = new Paragraph();

            // フィールド開始
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }));

            // フィールドコード（TOC \o "1-3" \h \z \u）
            string fieldCode = $"TOC \\o \"1-{headingLevels}\" \\h \\z \\u";
            var instrRun = new Run();
            instrRun.Append(new FieldCode(fieldCode) { Space = SpaceProcessingModeValues.Preserve });
            tocPara.Append(instrRun);

            // フィールド区切り（Separate）
            tocPara.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));

            // 初期プレースホルダ（Wordで更新される）
            tocPara.Append(new Run(new Text("目次を更新するには、Wordでフィールドを更新してください。 (選択: __Ctrl+A__, 更新: __F9__)")));

            // フィールド終端（End）
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
                    Indentation = new Indentation
                    {
                        //Left = (leftIndentMm != 0) ? ((leftIndentMm * 56.7).ToString()) : null,
                        //FirstLine = (firstLineIndentMm != 0) ? ((firstLineIndentMm * 56.7).ToString()) : null,
                        //Hanging = (hangingIndentMm != 0) ? ((hangingIndentMm * 56.7).ToString()) : null
                        Left = leftIndentMm != 0 ? MmToTwips(leftIndentMm) : null,
                        FirstLine = firstLineIndentMm != 0 ? MmToTwips(firstLineIndentMm) : null,
                        Hanging = hangingIndentMm != 0 ? MmToTwips(hangingIndentMm) : null
                    }
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
                tex = tex.Substring(1, tex.Length - 2);

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
                return tex.Substring(s, pos - s);
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
                        var remaining = tex.Substring(pos);
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
                                        string name = tex.Substring(nameStart, namePos - nameStart);
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
                        string txt = tex.Substring(s, pos - s);
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
            string verticalAlignment = "center"
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
                                paragraph.Append(e);
                            break;

                        case Run run:
                            paragraph.Append(run);
                            break;

                        case DocumentFormat.OpenXml.Math.OfficeMath math:
                            paragraph.Append(math);
                            break;

                        case null:
                            // 何もしない
                            break;

                        default:
                            // 数値等その他 → ToString() して共通処理
                            var txt = item.ToString();
                            if (!string.IsNullOrEmpty(txt))
                            {
                                foreach (var e in BuildInlineMixedRuns(txt, fontSize))
                                    paragraph.Append(e);
                            }
                            break;
                    }

                    // 複数要素がある場合のみ改行
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
                    Indentation = new Indentation
                    {
                        //Left = (leftIndentMm != 0) ? (leftIndentMm * 56.7).ToString() : null,
                        //FirstLine = (firstLineIndentMm != 0) ? (firstLineIndentMm * 56.7).ToString() : null,
                        //Hanging = (hangingIndentMm != 0) ? (hangingIndentMm * 56.7).ToString() : null
                        Left = leftIndentMm != 0 ? MmToTwips(leftIndentMm) : null,
                        FirstLine = firstLineIndentMm != 0 ? MmToTwips(firstLineIndentMm) : null,
                        Hanging = hangingIndentMm != 0 ? MmToTwips(hangingIndentMm) : null
                    }
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

        private void AddNMINT(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.PileBodies == null || inputModel.PileBodies.Count == 0)
                return;

            foreach (var pileBody in inputModel.PileBodies)
            {
                if (pileBody?.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                    continue;

                // レベル別「最大曲げ」時の縦断図出力用トラッカー（後段で使用）
                List<double> maxMomentsInPileBody = [double.MinValue, double.MinValue];
                List<PileLayoutDataItem?> pileLayoutDataItemsWithMaxMoment = [null, null];
                List<LoadCase?> loadCasesWithMaxMoment = [null, null];
                List<LoadCombination?> loadCombinationsWithMaxMoment = [null, null];
                List<bool> isLiquefactionsWithMaxMoment = [false, false];

                for (int j = 0; j < pileBody.PileBodySegments.Count; j++)
                {
                    var segment = pileBody.PileBodySegments[j];
                    if (segment?.PileSection == null) continue;
                    int pileBodySegmentNo = segment.No;
                    var pileSection = segment.PileSection;

                    // ライン（耐力曲線）を kN/kNm に正規化
                    List<List<double>> lineListsX = [];
                    List<List<double>> lineListsY = [];
                    List<string> lineListsLegend = [];

                    static List<double> ToKilo(IEnumerable<double>? src) => src?.Select(v => v / 1e3).ToList() ?? [];
                    static List<double> ToKiloNm(IEnumerable<double>? src) => src?.Select(v => v / 1e6).ToList() ?? [];

                    try
                    {
                        var nmUUlt = pileSection.UnfactoredUltimateNM;
                        lineListsX.Add(ToKilo(nmUUlt.N));
                        lineListsY.Add(ToKiloNm(nmUUlt.M));
                        lineListsLegend.Add("低減前安全限界");

                        var nmFUlt = pileSection.FactoredUltimateNM;
                        lineListsX.Add(ToKilo(nmFUlt.N));
                        lineListsY.Add(ToKiloNm(nmFUlt.M));
                        lineListsLegend.Add("低減後安全限界");

                        var nmUDmg = pileSection.UnfactoredDamageNM;
                        lineListsX.Add(ToKilo(nmUDmg.N));
                        lineListsY.Add(ToKiloNm(nmUDmg.M));
                        lineListsLegend.Add("低減前損傷限界");

                        var nmFDmg = pileSection.FactoredDamageNM;
                        lineListsX.Add(ToKilo(nmFDmg.N));
                        lineListsY.Add(ToKiloNm(nmFDmg.M));
                        lineListsLegend.Add("低減後損傷限界");

                        var nmUSvc = pileSection.UnfactoredServiceNM;
                        lineListsX.Add(ToKilo(nmUSvc.N));
                        lineListsY.Add(ToKiloNm(nmUSvc.M));
                        lineListsLegend.Add("低減前使用限界");

                        var nmFSvc = pileSection.FactoredServiceNM;
                        lineListsX.Add(ToKilo(nmFSvc.N));
                        lineListsY.Add(ToKiloNm(nmFSvc.M));
                        lineListsLegend.Add("低減後使用限界");
                    }
                    catch
                    {
                        // 片側が null でも落ちないように防御
                    }

                    // 散布点
                    List<double> axialForceResultsVL = [];
                    List<double> momentResultsVL = [];

                    List<double> axialForceResultsLevel1 = [];
                    List<double> momentResultsLevel1 = [];

                    List<double> axialForceResultsLevel2 = [];
                    List<double> momentResultsLevel2 = [];

                    // 解析済みの場合のみ散布点を作成
                    if (mainWindowViewModel?.IsHorizontalAnalysisDone == true &&
                        inputModel.PileLayoutItems != null &&
                        inputModel.LoadCasesInput != null)
                    {
                        var allSeismicLoadCases = inputModel.LoadCasesInput.AllSeismicLoadCases ?? [];
                        var allLoadCombinations = inputModel.LoadCasesInput.AllLoadCombinations ?? [];

                        foreach (var pli in inputModel.PileLayoutItems)
                        {
                            if (pli == null) continue;

                            // 常時（VL）は同一杭体のみ採用。モーメントは 0 扱い
                            try
                            {
                                int pbIdx = pli.PileBodyNo - 1;
                                if (pbIdx >= 0 && pbIdx < inputModel.PileBodies.Count &&
                                    inputModel.PileBodies[pbIdx] == pileBody)
                                {
                                    axialForceResultsVL.Add(pli.AxialForceVL0 + pli.AxialForceVLAdditional);
                                    momentResultsVL.Add(0.0);
                                }
                            }
                            catch { /* ignore */ }

                            foreach (var loadCase in allSeismicLoadCases)
                            {
                                if (loadCase == null || !loadCase.IsApplicable) continue;
                                double axialForce = pli.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                                foreach (var loadCombination in allLoadCombinations)
                                {
                                    foreach (var isLiquefaction in new[] { true, false })
                                    {
                                        // 対象の soilPile をアイテムの代替番号から取得
                                        var soilPiles = inputModel.ElementDivision?.SoilPiles;
                                        int soilIndex = pli.SoilPileAltNo - 1;
                                        if (soilPiles == null || soilIndex < 0 || soilIndex >= soilPiles.Count)
                                            continue;

                                        var soilPileForItem = soilPiles[soilIndex];
                                        if (soilPileForItem?.PileBodySegments == null ||
                                            pli.Beams == null || pli.Beams.Count == 0)
                                            continue;

                                        // この区間Noに一致するビーム群の中から、当該ケースの最大曲げを抽出
                                        double maxMomentInPile = double.MinValue;
                                        for (int iSeg = 0; iSeg < soilPileForItem.PileBodySegments.Count; iSeg++)
                                        {
                                            var segI = soilPileForItem.PileBodySegments[iSeg];
                                            if (segI == null || segI.No != pileBodySegmentNo) continue;

                                            if (iSeg < 0 || iSeg >= pli.Beams.Count) continue;
                                            var beam = pli.Beams[iSeg];
                                            if (beam == null) continue;

                                            var cum = beam.GetBeamResult(anaModel, loadCase, loadCombination, isLiquefaction)?.CumulativeForce;
                                            if (cum != null)
                                                maxMomentInPile = Math.Max(maxMomentInPile, cum.MabsMax);
                                        }

                                        // 区間ループ後に1回だけ散布点を追加（重複防止）
                                        if (maxMomentInPile == double.MinValue) maxMomentInPile = 0.0;

                                        if (loadCase.Level == 1)
                                        {
                                            axialForceResultsLevel1.Add(axialForce);
                                            momentResultsLevel1.Add(maxMomentInPile);
                                        }
                                        else if (loadCase.Level == 2)
                                        {
                                            axialForceResultsLevel2.Add(axialForce);
                                            momentResultsLevel2.Add(maxMomentInPile);
                                        }

                                        // レベル別の「杭全体の最大」トラッキング（後段の縦断図に使用）
                                        for (int lvl = 0; lvl < 2; lvl++)
                                        {
                                            if (loadCase.Level != (lvl + 1)) continue;
                                            if (maxMomentInPile > maxMomentsInPileBody[lvl])
                                            {
                                                maxMomentsInPileBody[lvl] = maxMomentInPile;
                                                pileLayoutDataItemsWithMaxMoment[lvl] = pli;
                                                loadCasesWithMaxMoment[lvl] = loadCase;
                                                loadCombinationsWithMaxMoment[lvl] = loadCombination;
                                                isLiquefactionsWithMaxMoment[lvl] = isLiquefaction;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // グラフ出力（ライン＋散布）
                    List<List<double>> xsLists = [axialForceResultsLevel2, axialForceResultsLevel1, axialForceResultsVL];
                    List<List<double>> ysLists = [momentResultsLevel2, momentResultsLevel1, momentResultsVL];
                    List<string> legends = ["レベル2", "レベル1", "常時"];

                    AddNMINTScottPlotGraphToBody(
                        mainPart,
                        body,
                        lineListsX, lineListsY, lineListsLegend,
                        xsLists, ysLists, legends,
                        "", "軸力(kN)", "曲げモーメント(kNm)",
                        150, 150);

                    AddAutoFigureCaption(body,
                        $"NMINT　杭体符号:{pileBody.PileBodyRef} | 杭区間番号: {segment.No}",
                        "図");
                }

                // 縦断図（各レベルで最大曲げモーメントのケースを可視化）
                if (mainWindowViewModel?.IsHorizontalAnalysisDone == true)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var pli = pileLayoutDataItemsWithMaxMoment[k];
                        var lc = loadCasesWithMaxMoment[k];
                        var comb = loadCombinationsWithMaxMoment[k];
                        var isLiq = isLiquefactionsWithMaxMoment[k];

                        if (pli == null || lc == null || comb == null) continue;
                        if (pli.PileNodes == null || pli.SoilNodes == null || pli.Beams == null) continue;

                        List<double> moments = [];
                        List<double> shears = [];
                        List<double> disps = [];
                        List<double> zs = [];
                        List<double> soilDisps = [];

                        // Z 軸（節点列）と地盤変位
                        for (int i = 0; i < pli.PileNodes.Count; i++)
                        {
                            double z = -pli.Z + pli.PileNodes[i].Coord.Z;
                            zs.Add(z);
                            if (i != 0 && i != pli.PileNodes.Count - 1)
                                zs.Add(z);

                            double soilUh = 0.0;
                            try
                            {
                                soilUh = pli.SoilNodes[i]
                                    .GetNodeResult(anaModel, lc, comb, isLiq)
                                    ?.CumulativeDisp?.Uh ?? 0.0;
                            }
                            catch { soilUh = 0.0; }

                            soilDisps.Add(soilUh);
                            if (i != 0 && i != pli.PileNodes.Count - 1)
                                soilDisps.Add(soilUh);
                        }

                        // 要素力・変位
                        for (int i = 0; i < pli.Beams.Count; i++)
                        {
                            var res = pli.Beams[i].GetBeamResult(anaModel, lc, comb, isLiq)?.CumulativeForce;
                            moments.Add(res?.Mi ?? 0.0);
                            moments.Add(res?.Mj ?? 0.0);

                            shears.Add(res?.Fi ?? 0.0);
                            shears.Add(res?.Fj ?? 0.0);

                            double uhI = 0.0, uhJ = 0.0;
                            try
                            {
                                uhI = pli.Beams[i].NodeI.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                uhJ = pli.Beams[i].NodeJ.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                            }
                            catch { }
                            disps.Add(uhI);
                            disps.Add(uhJ);
                        }

                        List<string> titles = ["", "", ""];
                        List<string> xLabels = ["せん断力(kN)", "曲げモーメント(kNm)", "変位(m)"];
                        List<string> yLabels = ["Z(m)", "Z(m)", "Z(m)"];

                        AddPileElevResultToBody(
                            mainPart, body,
                            [shears, moments, disps, soilDisps], [zs, zs, zs],
                            titles, xLabels, yLabels,
                            150, 100);

                        AddAutoFigureCaption(
                            body,
                            $"杭番号:{pli.No} | 荷重ケース:{lc.LoadName} | 荷重組合せ:{comb.Name} | 液状化: {isLiq}",
                            "図");
                    }
                }
            }
        }

        // 荷重沈下関係グラフ挿入メソッド
        private void AddSettlementGraph(MainDocumentPart mainPart, Body body)
        {
            if (inputModel.ElementDivision.SoilPiles == null) return;
            foreach (var soilPile in inputModel.ElementDivision.SoilPiles)
            {
                if (soilPile.PileCircumVerticals != null) continue;

                var loadDisplacements = soilPile.LoadDisplacements;
                List<double> d00s = [];
                List<double> pileTopLoads = [];
                List<double> rzToes = [];
                List<double> rzCircums = [];
                List<double> weights = [];

                foreach (var loadDisplacement in loadDisplacements)
                {
                    d00s.Add(loadDisplacement.DD0s);
                    pileTopLoads.Add(loadDisplacement.PileTopLoad);
                    rzToes.Add(loadDisplacement.RzToe);
                    rzCircums.Add(loadDisplacement.RzCircum);
                    weights.Add(loadDisplacement.Weight);
                }

                List<List<double>> xsLists = [d00s, d00s, d00s, d00s];
                List<List<double>> ysLists = [pileTopLoads, rzToes, rzCircums, weights];
                List<string> legends = ["杭頭荷重", "杭先端支持力", "杭周面抵抗力", "杭自重"];
                AddScottPlotGraphWithMultipleDataToBody(
                    mainPart, body,
                    xsLists, ysLists, legends,
                    "", "杭頭沈下量(mm)", "荷重(kN)",
                    150, 150);

                string pileRef = soilPile.PileBodyInput.PileBodyRef;
                string soilRef = soilPile.GroundInput.GroundRef;
                double alt = soilPile.Z;
                AddAutoFigureCaption(body, $"杭体:{pileRef}|地盤:{soilRef}|杭頭Z{alt:N1}m：荷重沈下関係グラフ", "図");
            }
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
            //                        var para = GetParagraph("上端深さ\n(m)", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 4)
            //                    {
            //                        var para = GetParagraph("下端深さ\n(m)", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 5)
            //                    {
            //                        var para = GetParagraph("杭断面タイプ", "center", 8);
            //                        SetTableCellWithVerticalAlign(cell, para, "center");
            //                    }
            //                    else if (colIdx == 6)
            //                    {
            //                        var para = GetParagraph("杭径\n(mm)", "center", 8);
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
                AddAutoFigureCaption(body, $"場所打ちコンクリート杭明細", "表");
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
                //                case 3: SetTableCellWithVerticalAlign(cell, GetParagraph("上端深さ\n(m)", "center", 8), "center"); break;
                //                case 4: SetTableCellWithVerticalAlign(cell, GetParagraph("下端深さ\n(m)", "center", 8), "center"); break;
                //                case 5: SetTableCellWithVerticalAlign(cell, GetParagraph("杭断面タイプ", "center", 8), "center"); break;
                //                case 6: SetTableCellWithVerticalAlign(cell, GetParagraph("杭径\n(mm)", "center", 8), "center"); break;
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
                CreateTableCell(["上端深さ\n(m)"], fontSize, "center"),
                CreateTableCell(["下端深さ\n(m)"], fontSize, "center"),
                CreateTableCell(["杭断面タイプ"], fontSize, "center"),
                CreateTableCell(["杭径\n(mm)"], fontSize, "center"),
                CreateTableCell(["鋼管"], fontSize, "center"),
                CreateTableCell(["コンクリート\nFc|E|γ|ξ"], fontSize, "center"),
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
                            ? $"{section.PipeDia:N0}-{section.PipeTs:N0}({section.PipeGrade})"
                            : string.Empty;
                        row.Append(CreateTableCell([pipeDesc], fontSize, "center"));

                        string concreteDesc = section != null
                            ? $"{section.ConcreteFc:N0}|{section.ConcreteE:N0}|{section.ConcreteGamma:N1}|{section.ConcreteGsi:N2}"
                            : string.Empty;
                        row.Append(CreateTableCell([concreteDesc], fontSize, "center"));

                        string mainBarDesc = section != null ? $"{section.MainBarNum}-{section.MainBarSize}({section.MainBarSpec})" : string.Empty;
                        row.Append(CreateTableCell([mainBarDesc], fontSize, "center"));

                        string hoopDesc = section != null ? $"{section.HoopSize}-{section.HoopSpacing}({section.HoopSpec})" : string.Empty;
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
            List<List<double>> Nmaxs = [];
            List<List<double>> Nmins = [];
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
                    Nmaxs.Add([double.MinValue, double.MinValue]);
                    Nmins.Add([double.MaxValue, double.MaxValue]);
                    Dmaxs.Add([double.MinValue, double.MinValue]);

                    //foreach (PileLayoutDataItem pileLayoutDataItem in inputModel.PileLayoutItems)
                    //{
                    //    if (pileLayoutDataItem.PileBodyNo != selectedPileBodyNo) continue;

                    //    Nmaxs[^1][0] = Math.Max(Nmaxs[^1][0], pileLayoutDataItem.AxialForceLevel1s.Max());
                    //    Nmins[^1][0] = Math.Min(Nmins[^1][0], pileLayoutDataItem.AxialForceLevel1s.Min());
                    //    Nmaxs[^1][1] = Math.Max(Nmaxs[^1][1], pileLayoutDataItem.AxialForceLevel2s.Max());
                    //    Nmins[^1][1] = Math.Min(Nmins[^1][1], pileLayoutDataItem.AxialForceLevel2s.Min());

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

                        // Safe update for Nmax/Nmin lists (guard against null or empty axial force lists)
                        if (pileLayoutDataItem.AxialForceLevel1s != null && pileLayoutDataItem.AxialForceLevel1s.Count > 0)
                        {
                            Nmaxs[^1][0] = Math.Max(Nmaxs[^1][0], pileLayoutDataItem.AxialForceLevel1s.Max());
                            Nmins[^1][0] = Math.Min(Nmins[^1][0], pileLayoutDataItem.AxialForceLevel1s.Min());
                        }
                        if (pileLayoutDataItem.AxialForceLevel2s != null && pileLayoutDataItem.AxialForceLevel2s.Count > 0)
                        {
                            Nmaxs[^1][1] = Math.Max(Nmaxs[^1][1], pileLayoutDataItem.AxialForceLevel2s.Max());
                            Nmins[^1][1] = Math.Min(Nmins[^1][1], pileLayoutDataItem.AxialForceLevel2s.Min());
                        }

                        foreach (LoadCase loadCase in inputModel.LoadCasesInput.AllSeismicLoadCases)
                        {
                            var axialForce = pileLayoutDataItem.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                            foreach (LoadCombination loadCombination in inputModel.LoadCasesInput.AllLoadCombinations)
                            {
                                foreach (var isLiquefaction in new[] { true, false })
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

            BuildAnalysisResultSummaryTable(
            body,
            selectedPileBodies,
            selectedSegment,
            selectedSegmentTop,
            selectedSegmentBtm,
            Qmaxs,
            Mmaxs,
            Nmaxs,
            Nmins,
            Dmaxs
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
            List<List<double>> Nmaxs,
            List<List<double>> Nmins,
            List<List<double>> Dmaxs
            )
        {
            for (int k = 0; k < 2; k++)
            {
                AddLineBreak(body);
                AddAutoFigureCaption(body, $"杭検討結果まとめ一覧（レベル{k + 1}地震）", "表");

                var table = new Table();
                var borders = new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Color = "000000", Size = 4 }
                );
                table.AppendChild(new TableProperties(borders));

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
                            else if (colIdx == 3) SetTableCellWithVerticalAlign(cell, GetParagraph("上端深さ\n(m)", "center", 8), "center");
                            else if (colIdx == 4) SetTableCellWithVerticalAlign(cell, GetParagraph("下端深さ\n(m)", "center", 8), "center");
                            else if (colIdx == 5) SetTableCellWithVerticalAlign(cell, GetParagraph("Dmax\n(m)", "center", 8), "center");
                            else if (colIdx == 6) SetTableCellWithVerticalAlign(cell, GetParagraph("Qmax\n(kN)", "center", 8), "center");
                            else if (colIdx == 7) SetTableCellWithVerticalAlign(cell, GetParagraph("Mmax\n(kNm)", "center", 8), "center");
                            else if (colIdx == 8) SetTableCellWithVerticalAlign(cell, GetParagraph("Nmax\n(kN)", "center", 8), "center");
                            else if (colIdx == 9) SetTableCellWithVerticalAlign(cell, GetParagraph("Nmin\n(kN)", "center", 8), "center");
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
                            else if (colIdx == 8) SetTableCellWithVerticalAlign(cell, GetParagraph($"{Nmaxs[i][k]:N1}", "center", 8), "center");
                            else if (colIdx == 9) SetTableCellWithVerticalAlign(cell, GetParagraph($"{Nmins[i][k]:N1}", "center", 8), "center");
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
                            var para = GetParagraph("レベル1\n上部構造\n慣性力\nP<_s>", "center", 8);
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
                            var para = GetParagraph("レベル1\n基礎部\n慣性力\nP<_f>", "center", 8);
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
                            var para = GetParagraph("レベル1\nβ<_U>・P<_s>＋β<_L>・P<_f>", "center", 8);
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
                            string label = isRow9 ? "レベル2\n上部構造\n慣性力\nP<_s>"
                                        : isRow10 ? "レベル2\n基礎部\n慣性力\nP<_f>"
                                        : "レベル2\nβ<_U>・P<_s>＋β<_L>・P<_f>";
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
            var commonRunProps = new RunProperties(
                new RunFonts
                {
                    Ascii = "ＭＳ ゴシック",
                    HighAnsi = "ＭＳ ゴシック",
                    EastAsia = "ＭＳ ゴシック"
                });

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
        public static void AddAutoFigureCaption(Body body, string captionText, string label = "図", double fontSize = 10.5)
        {
            // ラベル（例: "図", "表", "Figure", "Table"）を追加
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

            //// SEQフィールド（自動図表番号）
            //run.Append(new FieldChar { FieldCharType = FieldCharValues.Begin });
            //run.Append(new FieldCode(" SEQ Figure \\* ARABIC "));
            //run.Append(new FieldChar { FieldCharType = FieldCharValues.Separate });
            //run.Append(new Text("1")); // 初期値（Wordで更新される）
            //run.Append(new FieldChar { FieldCharType = FieldCharValues.End });

            //run.Append(new Text($" {captionText}"));

            //paragraph.Append(run);
            //body.Append(paragraph);

            // ラベルを追加
            run.Append(new Text($"{label}"));

            // SEQフィールドの識別子もラベルに合わせる
            run.Append(new FieldChar { FieldCharType = FieldCharValues.Begin });
            run.Append(new FieldCode($" SEQ {label} \\* ARABIC "));
            run.Append(new FieldChar { FieldCharType = FieldCharValues.Separate });
            run.Append(new Text("1")); // 初期値（Wordで更新される）
            run.Append(new FieldChar { FieldCharType = FieldCharValues.End });

            run.Append(new Text($" {captionText}"));

            paragraph.Append(run);
            body.Append(paragraph);
        }

        // スコットプロット挿入メソッド
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
                plots[i].Add.ScatterLine(xsArray, ysArray);

                // 変位図に地盤変位を重ねるのは xsLists[3] があるときのみ
                if (i == 2 && xsLists.Count >= 4)
                {
                    double[] xsArrayS = [.. xsLists[3]];
                    if (xsArrayS.Length == ysArray.Length)
                        plots[i].Add.ScatterLine(xsArrayS, ysArray);
                }

                plots[i].Axes.Title.Label.Text = titles[i];
                plots[i].Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(titles[i]);
                plots[i].Axes.Bottom.Label.Text = xLabels[i];
                plots[i].Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabels[i]);
                plots[i].Axes.Left.Label.Text = yLabels[i];
                plots[i].Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabels[i]);
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

                    wpf.Plot.Axes.Title.Label.Text = title ?? string.Empty;
                    wpf.Plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
                    wpf.Plot.Axes.Left.Label.Text = yLabel ?? string.Empty;
                }, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);

                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AddScottPlotGraphToBody: エラー: {ex.Message}");
            }
        }
        //{
        //    // 1. ScottPlotでグラフ作成
        //    WpfPlot wpf = new();
        //    double[] xsArray = [.. xsList];
        //    double[] ysArray = [.. ysList];
        //    wpf.Plot.Add.ScatterLine(xsArray, ysArray);

        //    //string title = "ScottPlotサンプルグラフ";
        //    wpf.Plot.Axes.Title.Label.Text = title;
        //    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);

        //    //string xLabel = "X軸";
        //    wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        //    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);

        //    //string yLabel = "Y軸";
        //    wpf.Plot.Axes.Left.Label.Text = yLabel;
        //    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel);

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

        // 複数データスコットプロット挿入メソッド
        //public static void AddNMINTScottPlotGraphToBody(
        //    MainDocumentPart mainPart, Body body,
        //    List<List<double>> xsLineLists, List<List<double>> ysLineLists, List<string> lineLegends,
        //    List<List<double>> xsScatterLists, List<List<double>> ysScatterLists, List<string> scatterLegends,
        //    string title, string xLabel, string yLabel,
        //    double widthMm = 150, double heightMm = 150)
        ////{
        ////    List<ScottPlot.Color> colors = [
        ////        ScottPlot.Color.FromSKColor(NikkenSKColor.DeepBlue),
        ////        ScottPlot.Color.FromSKColor(NikkenSKColor.SkyBlue),
        ////        ScottPlot.Color.FromSKColor(NikkenSKColor.Yellow),
        ////        ];

        ////    // 1. ScottPlotでグラフ作成
        ////    WpfPlot wpf = new();
        ////    if (xsLineLists.Count != ysLineLists.Count || xsLineLists.Count != lineLegends.Count ||
        ////        xsScatterLists.Count != ysScatterLists.Count || xsScatterLists.Count != scatterLegends.Count
        ////        )
        ////    {
        ////        return;
        ////    }

        ////    for (int i = 0; i < xsLineLists.Count; i++)
        ////    {
        ////        if (xsLineLists.Count == 0) continue;
        ////        var xsList = xsLineLists[i];
        ////        var ysList = ysLineLists[i];
        ////        var legend = lineLegends[i];

        ////        double[] xsArray = [.. xsList];
        ////        double[] ysArray = [.. ysList];
        ////        var scatterLineTemp = wpf.Plot.Add.ScatterLine(xsArray, ysArray);
        ////        scatterLineTemp.LegendText = legend;

        ////        scatterLineTemp.LinePattern = i % 2 == 0 ? LinePattern.Dashed : LinePattern.Solid;
        ////        scatterLineTemp.LineWidth = i % 2 == 0 ? 1 : 2;
        ////        scatterLineTemp.MarkerSize = 0;
        ////        scatterLineTemp.LineColor = i < 2 ? colors[0] : i < 4 ? colors[1] : colors[2];
        ////    }

        ////    for (int i = 0; i < xsScatterLists.Count; i++)
        ////    {
        ////        var xsList = xsScatterLists[i];
        ////        var ysList = ysScatterLists[i];
        ////        var legend = scatterLegends[i];

        ////        double[] xsArray = [.. xsList];
        ////        double[] ysArray = [.. ysList];
        ////        var scatterTemp = wpf.Plot.Add.Scatter(xsArray, ysArray);
        ////        scatterTemp.LineWidth = 0;
        ////        scatterTemp.MarkerSize = 10;
        ////        scatterTemp.MarkerColor = colors[i];
        ////    }

        ////    //string title = "ScottPlotサンプルグラフ";
        ////    wpf.Plot.Axes.Title.Label.Text = title;
        ////    wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);

        ////    //string xLabel = "X軸";
        ////    wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        ////    wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);

        ////    //string yLabel = "Y軸";
        ////    wpf.Plot.Axes.Left.Label.Text = yLabel;
        ////    wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel);

        ////    // Legend
        ////    wpf.Plot.Legend.FontName = ScottPlot.Fonts.Detect(yLabel);
        ////    wpf.Plot.ShowLegend(Alignment.UpperRight); // 右上に凡例

        ////    wpf.Plot.Axes.AutoScale();
        ////    wpf.Plot.Axes.AutoScaleExpandX();
        ////    wpf.Plot.Axes.AutoScaleExpandY();
        ////    wpf.Plot.Axes.Left.Min = 0.0;

        ////    // 2. 一時画像ファイルとして保存
        ////    string tempFile = Path.GetTempFileName() + ".png";
        ////    //int widthPx = (int)(widthMm / 25.4 * 96 * 2);
        ////    //int heightPx = (int)(heightMm / 25.4 * 96 * 2);
        ////    int widthPx = MmToPx(widthMm, Dpi, 2.0);
        ////    int heightPx = MmToPx(heightMm, Dpi, 2.0);
        ////    wpf.Plot.SavePng(tempFile, widthPx, heightPx);

        ////    // 3. Word文書のbodyに画像挿入
        ////    WordDocumentUtils.AddImageToBodyByMm(mainPart, body, tempFile, widthMm, heightMm);

        ////    // 4. 一時ファイル削除
        ////    if (File.Exists(tempFile)) File.Delete(tempFile);
        ////}
        //{
        //    try
        //    {
        //        var pngBytes = DiagramRenderer.RenderScottPlotToPngBytes(wpf =>
        //        {
        //            // lines
        //            for (int i = 0; i < xsLineLists.Count; i++)
        //            {
        //                var xs = xsLineLists[i]?.ToArray() ?? Array.Empty<double>();
        //                var ys = ysLineLists[i]?.ToArray() ?? Array.Empty<double>();
        //                if (xs.Length > 0 && ys.Length > 0)
        //                {
        //                    var pl = wpf.Plot.Add.ScatterLine(xs, ys);
        //                    pl.LegendText = i < lineLegends.Count ? lineLegends[i] : null;
        //                    pl.LinePattern = i % 2 == 0 ? LinePattern.Dashed : LinePattern.Solid;
        //                    pl.LineWidth = i % 2 == 0 ? 1 : 2;
        //                    pl.MarkerSize = 0;
        //                }
        //            }

        //            // scatters
        //            for (int i = 0; i < xsScatterLists.Count; i++)
        //            {
        //                var xs = xsScatterLists[i]?.ToArray() ?? Array.Empty<double>();
        //                var ys = ysScatterLists[i]?.ToArray() ?? Array.Empty<double>();
        //                if (xs.Length > 0 && ys.Length > 0)
        //                {
        //                    var sc = wpf.Plot.Add.Scatter(xs, ys);
        //                    sc.MarkerSize = 6;
        //                    sc.LineWidth = 0;
        //                    sc.LegendText = i < scatterLegends.Count ? scatterLegends[i] : null;
        //                }
        //            }

        //            //string title = "ScottPlotサンプルグラフ";
        //            wpf.Plot.Axes.Title.Label.Text = title;
        //            wpf.Plot.Axes.Title.Label.FontName = ScottPlot.Fonts.Detect(title);

        //            //string xLabel = "X軸";
        //            wpf.Plot.Axes.Bottom.Label.Text = xLabel;
        //            wpf.Plot.Axes.Bottom.Label.FontName = ScottPlot.Fonts.Detect(xLabel);

        //            //string yLabel = "Y軸";
        //            wpf.Plot.Axes.Left.Label.Text = yLabel;
        //            wpf.Plot.Axes.Left.Label.FontName = ScottPlot.Fonts.Detect(yLabel);

        //            // Legend
        //            wpf.Plot.Legend.FontName = ScottPlot.Fonts.Detect(yLabel);
        //            wpf.Plot.ShowLegend(Alignment.UpperRight); // 右上に凡例

        //            wpf.Plot.Axes.Title.Label.Text = title ?? string.Empty;
        //            wpf.Plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
        //            wpf.Plot.Axes.Left.Label.Text = yLabel ?? string.Empty;
        //            wpf.Plot.ShowLegend();
        //            wpf.Plot.Axes.AutoScale();
        //        }, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);

        //        if (pngBytes != null && pngBytes.Length > 0)
        //            WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"AddNMINTScottPlotGraphToBody: エラー: {ex.Message}");
        //    }
        //}
        public static void AddNMINTScottPlotGraphToBody(
    MainDocumentPart mainPart, Body body,
    List<List<double>> xsLineLists, List<List<double>> ysLineLists, List<string> lineLegends,
    List<List<double>> xsScatterLists, List<List<double>> ysScatterLists, List<string> scatterLegends,
    string title, string xLabel, string yLabel,
    double widthMm = 150, double heightMm = 150)
        {
            try
            {
                // 日本語フォント候補から利用可能なものを選択（環境依存）
                string[] candidates = ["Meiryo", "Yu Gothic UI", "Yu Gothic", "ＭＳ ゴシック", "MS Gothic"];
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
                    }
                }

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
                    }
                }

                // タイトル / 軸ラベル / 凡例を明示設定（フォント指定）
                try
                {
                    plot.Axes.Title.Label.Text = title ?? string.Empty;
                    plot.Axes.Bottom.Label.Text = xLabel ?? string.Empty;
                    plot.Axes.Left.Label.Text = yLabel ?? string.Empty;

                    // 主要なフォントプロパティに明示的指定（API 互換性を考慮して例外を無視）
                    plot.Axes.Title.Label.FontName = useFont;
                    plot.Axes.Bottom.Label.FontName = useFont;
                    plot.Axes.Left.Label.FontName = useFont;
                    plot.Legend.FontName = useFont;
                }
                catch { /* 安全に無視 */ }

                plot.ShowLegend();
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
                Console.WriteLine($"AddNMINTScottPlotGraphToBody: エラー: {ex.Message}");
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
                    wpf.Plot.ShowLegend();
                    wpf.Plot.Axes.AutoScale();
                }, widthMm, heightMm, dpi: Layout.BaseDpi, scale: Layout.HiResScale);

                if (pngBytes != null && pngBytes.Length > 0)
                    WordDrawingBuilder.AddPngBytesToBody(mainPart, body, pngBytes, widthMm, heightMm);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AddScottPlotGraphWithMultipleDataToBody: エラー: {ex.Message}");
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

                var typeface = new Typeface("Meiryo");
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

                if (text[idx..].StartsWith("<^") && idx + 3 < text.Length && text[idx + 3] == '>')
                {
                    // 上付き文字
                    char supChar = text[idx + 2];
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
                        new Text(supChar.ToString())
                    );
                    runs.Add(run);
                    idx += 4;
                }
                else if (text[idx..].StartsWith("<_") && idx + 3 < text.Length && text[idx + 3] == '>')
                {
                    // 下付き文字
                    char subChar = text[idx + 2];
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
                        new Text(subChar.ToString())
                    );
                    runs.Add(run);
                    idx += 4;
                }
                else
                {
                    // 通常文字
                    int nextIdx = text.IndexOfAny(['<', '\n'], idx);
                    if (nextIdx == -1) nextIdx = text.Length;
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
                            new Text(normalText)
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
                Console.WriteLine($"図の作成でエラー: {ex.Message}");
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

                var typeface = new Typeface("Meiryo");
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
                Console.WriteLine($"AddPileForceDiagramByMm: 図作成エラー: {ex.Message}");
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
                Func<PileLayoutDataItem, double> diameterSelector = pli =>
                {
                    if (inputModel?.PileBodies == null) return 1.0;
                    int bodyNo = pli?.PileBodyNo ?? 0;
                    if (bodyNo <= 0 || bodyNo > inputModel.PileBodies.Count) return 1.0;
                    var pb = inputModel.PileBodies[bodyNo - 1];
                    var seg = pb?.PileBodySegments?.FirstOrDefault();
                    if (seg?.PileSection == null) return 1.0;
                    // 元実装では PileDiameter は mm 単位で扱っているため m に変換
                    return seg.PileSection.PileDiameter * 0.001;
                };

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
                Console.WriteLine($"AddPilingLayoutDiagramByMm: 図作成エラー: {ex.Message}");
                // 必要に応じプレースホルダ段落を追加するなどのフォールバック処理を入れてください
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


        // 杭伏図への曲げモーメント情報の追加メソッド
        private string GetPileTopBendingMomentMark(PileLayoutDataItem pileLayoutItem)
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
                ? new List<LoadCombination?>(allCombinations.Cast<LoadCombination?>())
                : new List<LoadCombination?>() { null };

            // 各レベルについて先頭4ケース（存在する分）を列挙し、各ケースで Mhi を求める。
            // 各ケースは全 LoadCombination, 液状化フラグ(true/false) を走査して最大値を採用。
            void AppendLevelCases(IEnumerable<LoadCase> loadCases, string levelLabel)
            {
                mark += $"\n{levelLabel}";
                var lcList = loadCases?.ToList() ?? new List<LoadCase>();
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
                            if (resL != null && !double.IsNaN(resL.Mi))
                                maxLiq = Math.Max(maxLiq, resL.Mi);
                        }
                        catch { /* 念のため無視 */ }

                        try
                        {
                            var resN = beam.GetBeamResult(anaModel, lc, comb, false)?.CumulativeForce;
                            if (resN != null && !double.IsNaN(resN.Mi))
                                maxNonLiq = Math.Max(maxNonLiq, resN.Mi);
                        }
                        catch { /* 念のため無視 */ }
                    }

                    // 表示値決定ルール：
                    // - 両方存在すれば大きい方を表示
                    // - 片方だけあればその値を表示
                    // - 無ければ "-" を表示
                    double? chosen = null;
                    if (!double.IsNegativeInfinity(maxLiq) && !double.IsNegativeInfinity(maxNonLiq))
                        chosen = Math.Max(maxLiq, maxNonLiq);
                    else if (!double.IsNegativeInfinity(maxLiq))
                        chosen = maxLiq;
                    else if (!double.IsNegativeInfinity(maxNonLiq))
                        chosen = maxNonLiq;

                    if (chosen.HasValue)
                        mark += $"\nケース{idx + 1}: {chosen.Value:N1}"; // 単位（kNm 等）は必要なら付与
                    else
                        mark += $"\nケース{idx + 1}: -";
                }

                // 足りないケースがあれば "-" で埋める（常に4行表示したい場合）
                for (int idx = lcList.Count; idx < 4; idx++)
                {
                    mark += $"\nケース{idx + 1}: -";
                }
            }

            AppendLevelCases(level1, "レベル1");
            AppendLevelCases(level2, "レベル2");

            return mark;
        }


        // 杭伏図への線打力情報の追加メソッド
        private string GetPileTopShearForceMark(PileLayoutDataItem pileLayoutItem)
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

            var level1 = loadCasesInput.LoadCasesLevel1 ?? new System.Collections.ObjectModel.ObservableCollection<LoadCase>();
            var level2 = loadCasesInput.LoadCasesLevel2 ?? new System.Collections.ObjectModel.ObservableCollection<LoadCase>();
            var allCombinations = loadCasesInput.AllLoadCombinations;

            var combos = (allCombinations != null && allCombinations.Count > 0)
                ? new List<LoadCombination?>(allCombinations.Cast<LoadCombination?>())
                : new List<LoadCombination?>() { null };

            // 各レベルについて先頭4ケース（存在する分）を列挙し、各ケースで Mhi を求める。
            // 各ケースは全 LoadCombination, 液状化フラグ(true/false) を走査して最大値を採用。
            void AppendLevelCases(IEnumerable<LoadCase> loadCases, string levelLabel)
            {
                mark += $"\n{levelLabel}";
                var lcList = loadCases?.ToList() ?? new List<LoadCase>();
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
                            if (resL != null && !double.IsNaN(resL.Fi))
                                maxLiq = Math.Max(maxLiq, resL.Fi);
                        }
                        catch { /* 念のため無視 */ }

                        try
                        {
                            var resN = beam.GetBeamResult(anaModel, lc, comb, false)?.CumulativeForce;
                            if (resN != null && !double.IsNaN(resN.Fi))
                                maxNonLiq = Math.Max(maxNonLiq, resN.Fi);
                        }
                        catch { /* 念のため無視 */ }
                    }

                    // 表示値決定ルール：
                    // - 両方存在すれば大きい方を表示
                    // - 片方だけあればその値を表示
                    // - 無ければ "-" を表示
                    double? chosen = null;
                    if (!double.IsNegativeInfinity(maxLiq) && !double.IsNegativeInfinity(maxNonLiq))
                        chosen = Math.Max(maxLiq, maxNonLiq);
                    else if (!double.IsNegativeInfinity(maxLiq))
                        chosen = maxLiq;
                    else if (!double.IsNegativeInfinity(maxNonLiq))
                        chosen = maxNonLiq;

                    if (chosen.HasValue)
                        mark += $"\nケース{idx + 1}: {chosen.Value:N1}"; // 単位（kN 等）は必要なら付与
                    else
                        mark += $"\nケース{idx + 1}: -";
                }

                // 足りないケースがあれば "-" で埋める（常に4行表示したい場合）
                for (int idx = lcList.Count; idx < 4; idx++)
                {
                    mark += $"\nケース{idx + 1}: -";
                }
            }

            AppendLevelCases(level1, "レベル1");
            AppendLevelCases(level2, "レベル2");

            return mark;
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
            //        var typeface = new Typeface("Meiryo");
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

            //        var typeface = new Typeface("Meiryo");
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
                var dia = inputModel.PileBodies[pileLayoutItem.PileBodyNo - 1].PileBodySegments[0].PileSection.PileDiameter;
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
                    var dia = pileBody.PileBodySegments[0].PileSection.PileDiameter * 0.001;
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
        public static List<Run> ConvertStringToRunsWithSuperSub(string text, double fontSize = 10.5)
        {
            var runs = new List<Run>();
            int pos = 0;
            var pattern = @"\<\^(.*?)>|<_(.*?)>";

            var matches = Regex.Matches(text, pattern);

            foreach (Match match in matches)
            {
                // 通常文字
                if (match.Index > pos)
                {
                    string normalText = text[pos..match.Index];
                    runs.Add(new Run(
                        new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() } },
                        new Text(normalText)
                    ));
                }

                if (match.Groups[1].Success) // 上付き
                {
                    string superText = match.Groups[1].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
                            VerticalTextAlignment = new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }
                        },
                        new Text(superText)
                    ));
                }
                else if (match.Groups[2].Success) // 下付き
                {
                    string subText = match.Groups[2].Value;
                    runs.Add(new Run(
                        new RunProperties
                        {
                            FontSize = new FontSize { Val = (fontSize * 2).ToString() },
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
                    new RunProperties { FontSize = new FontSize { Val = (fontSize * 2).ToString() } },
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
        private static Table CreateTableWithBorders()
        {
            Table table = new();
            TableProperties props = new(
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
        // 列幅の合計が8,500前後になるように指定する
        {
            Table table = new();
            TableProperties props = new(
                new TableBorders(
                    //new TableWidth { Width = "5000", Type = TableWidthUnitValues.Dxa }, // 表全体の幅
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

        //private static TableCell CreateTableCell(
        //    List<object> contents,
        //    double fontSize = 8,
        //    string alignment = "center",
        //    string verticalAlignment = "center"
        //)
        //{
        //    var cell = new TableCell();

        //    var cellProperties = new TableCellProperties();
        //    cellProperties.Append(new TableCellVerticalAlignment
        //    {
        //        Val = verticalAlignment.ToLower() switch
        //        {
        //            "top" => TableVerticalAlignmentValues.Top,
        //            "bottom" => TableVerticalAlignmentValues.Bottom,
        //            _ => TableVerticalAlignmentValues.Center
        //        }
        //    });
        //    cell.Append(cellProperties);

        //    var paragraph = new Paragraph
        //    {
        //        ParagraphProperties = new ParagraphProperties
        //        {
        //            Justification = new Justification
        //            {
        //                //Val = JustificationValues.Center // 中央揃え

        //                Val = alignment.ToLower() switch
        //                {
        //                    "center" or "centre" => JustificationValues.Center,
        //                    "right" => JustificationValues.Right,
        //                    _ => JustificationValues.Left
        //                }
        //            }
        //        }
        //    };

        //    for (int i = 0; i < contents.Count; i++)
        //    {
        //        var item = contents[i];
        //        if (item is string str)
        //        {
        //            var runs = ConvertStringToRunsWithSuperSub(str, fontSize);
        //            foreach (var run in runs)
        //                paragraph.Append(run);
        //        }
        //        else if (item is Run run)
        //        {
        //            paragraph.Append(run);
        //        }
        //        else if (item is DocumentFormat.OpenXml.Math.OfficeMath math)
        //        {
        //            paragraph.Append(math);
        //        }

        //        if (i < contents.Count - 1)
        //            paragraph.Append(new Run(new Break()));
        //    }

        //    cell.Append(paragraph);
        //    return cell;
        //}

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
                Console.WriteLine($"SetColumnWidthでエラーが発生しました: {ex.Message}");
            }
        }

        // 基本設定の表を追加するメソッド
        public static void AddFundamentalTable(Body body, FundamentalInput fundamentalInput)
        {
            int width0 = 3_000;
            int width1 = 10_000;
            try
            {
                //Table table = CreateTableWithBorders();
                Table table = CreateTableWithBordersAndWidths(width0, width1);

                // 1行目: 
                TableRow headerRow = CreateHeaderRow(
                    CreateTableCellWithWidth("プロジェクト番号", "left", width0),
                    CreateTableCellWithWidth(fundamentalInput.ProjectNo, "left", width1)
                );
                table.Append(headerRow);

                // 2行目: 
                TableRow row1 = new();
                row1.Append(
                    CreateTableCellWithWidth("プロジェクト名", "left", width0),
                    CreateTableCellWithWidth(fundamentalInput.ProjectName, "left", width1)
                );
                table.Append(row1);
                // 3行目: 
                TableRow row2 = new();
                row2.Append(
                    CreateTableCellWithWidth("Z軸符号", "left", width0),
                    CreateTableCellWithWidth($"{fundamentalInput.RefLevel:N3}", "left", width1)
                );
                table.Append(row2);

                // 列幅を設定
                //SetColumnWidth(table, 0, 20); // 1列目の幅
                //SetColumnWidth(table, 1, 60); // 2列目の幅

                body.Append(table);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AddFundamentalTableでエラーが発生しました: {ex.Message}");
            }
        }

        // 荷重条件の表を追加するメソッド
        private static void AddLoadCaseTable(Body body, ObservableCollection<LoadCase> loadCases)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["No."], fontSize, "center"),
            CreateTableCell(["荷重名"], fontSize, "center"),
            CreateTableCell(["荷重", "角度", "[度]"], fontSize, "center"),
            CreateTableCell(["地盤", "非線形性"], fontSize, "center"),
            CreateTableCell(["杭体", "非線形性"], fontSize, "center"),
            CreateTableCell(["慣性力", "位置", "X座標", "[m]"], fontSize, "center"),
            CreateTableCell(["慣性力", "位置", "Y座標", "[m]"], fontSize, "center"),
            CreateTableCell(["慣性力", "位置", "Z座標", "[m]"], fontSize, "center"),
            CreateTableCell(["上部構造", "慣性力", "[kN]"], fontSize, "center"),
            CreateTableCell(["基礎部", "慣性力", "[kN]"], fontSize, "center")
            );
            table.Append(headerRow);

            // データ行を追加
            int no = 0;
            foreach (LoadCase loadCase in loadCases)
            {
                no += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([no.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([loadCase.LoadName], fontSize, "right"));
                dataRow.Append(CreateTableCell([loadCase.LoadAngle.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([loadCase.IsSoilNonLinear.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([loadCase.IsPileNonLinear.ToString()], fontSize, "right"));

                // 位置X/Y/Z（[m]）
                dataRow.Append(CreateTableCell([$"{loadCase.ForceActionPointX:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{loadCase.ForceActionPointY:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{loadCase.ForceActionPointAltitude:N3}"], fontSize, "right"));

                // 上部・基礎の慣性力（[kN]）
                dataRow.Append(CreateTableCell([$"{loadCase.UpperMassForce:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{loadCase.FoundationMassForce:N0}"], fontSize, "right"));

                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 
        public static void AddGroundInfo(Body body, ObservableCollection<GroundInput> grounds)
        {
            double fontSize = 8.0;
            if (grounds == null || grounds.Count == 0) return;
            for (int i = 0; i < grounds.Count; i++)
            {
                GroundInput ground = grounds[i];
                AddHeader1(body, $"地盤情報 {i + 1}: {ground.GroundRef}", 2);


                Table table = CreateTableWithBorders();

                // 1行目: 表題
                TableRow headerRow = CreateHeaderRow(
                    CreateTableCell(["項目"], fontSize, "center"),
                    CreateTableCell(["深度[m]"], fontSize, "center"),
                    CreateTableCell(["Z[m]"], fontSize, "center")
                );
                table.Append(headerRow);

                // 2行目: データ
                TableRow row1 = new();
                row1.Append(
                    CreateTableCell(["孔口レベル"], fontSize, "center"),
                    CreateTableCell([$"{ground.GLDepth}"], fontSize, "right"),
                    CreateTableCell([$"{ground.GroundTopAltitude}"], fontSize, "right")
                );
                table.Append(row1);

                // 3行目: データ
                TableRow row2 = new();
                row2.Append(
                    CreateTableCell(["地下水位"], fontSize, "center"),
                    CreateTableCell([$"{ground.GroundWaterGLDepth}"], fontSize, "right"),
                    CreateTableCell([$"{ground.GroundWaterTableAltitude}"], fontSize, "right")
                );
                table.Append(row2);

                // 4行目: データ
                TableRow row3 = new();
                row3.Append(
                    CreateTableCell(["地中応力検討用レベル"], fontSize, "center"),
                    CreateTableCell([$"{ground.StressGLDepth}"], fontSize, "right"),
                    CreateTableCell([$"{ground.StressAltitude}"], fontSize, "right")
                );
                table.Append(row3);

                body.Append(table);

                body.Append(new Paragraph());

                AddGroundLayerTable(body, ground.GroundLayers);

                body.Append(new Paragraph());

                AddGroundMassTable(body, ground.GroundMassesData);

                body.Append(new Paragraph());

                AddLiquefactionInfo(body, ground);

                body.Append(new Paragraph());

                AddGroundDisplacementInfo(body, ground);
            }
        }

        // 地盤情報の表を追加するメソッド
        public static void AddGroundLayerTable(Body body, ObservableCollection<GroundLayerInput> groundLayers)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["土層", "番号"], fontSize, "center"),
            CreateTableCell(["層厚", "[m]"], fontSize, "center"),
            CreateTableCell(["下端", "深度", "[m]"], fontSize, "center"),
            CreateTableCell(["下端", "Z", "[m]"], fontSize, "center"),
            CreateTableCell(["土層", "分類"], fontSize, "center"),
            CreateTableCell(["単位", "体積", "重量", "[kN/m<^3>]"], fontSize, "center"),
            CreateTableCell(["年代", "分類"], fontSize, "center"),
            CreateTableCell(["押込側", "周面", "抵抗"], fontSize, "center"),
            CreateTableCell(["引抜側", "周面", "抵抗"], fontSize, "center"),
            CreateTableCell(["工学的", "基盤"], fontSize, "center")
            );

            table.Append(headerRow);

            // データ行を追加
            int no = 0;
            foreach (GroundLayerInput groundLayer in groundLayers)
            {
                no += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([no.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.LayerThickness:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.BottomGLDepth:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.BottomAltitude:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.GranularityClass}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.Density:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.AgeCategory}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.IsPositiveCircumResistance}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.IsNegativeCircumResistance}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.IsEngineeringBedrock}"], fontSize, "right"));

                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 地盤情報の表を追加するメソッド
        public static void AddGroundMassTable(Body body, ObservableCollection<GroundMassDataInput> groundMasses)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["土", "質点", "番号"], fontSize, "center"),
            CreateTableCell(["間隔", "[m]"], fontSize, "center"),
            CreateTableCell(["深度", "[m]"], fontSize, "center"),
            CreateTableCell(["Z", "[m]"], fontSize, "center"),
            CreateTableCell(["粒度", "分類"], fontSize, "center"),
            CreateTableCell(["土層", "番号"], fontSize, "center"),
            CreateTableCell(["単位", "体積", "重量", "[kN/m<^3>]"], fontSize, "center"),
            CreateTableCell(["年代", "分類"], fontSize, "center"),
            CreateTableCell(["N値"], fontSize, "center"),
            CreateTableCell(["質点", "質量", "[ton/m<^2>]"], fontSize, "center")
            );

            table.Append(headerRow);

            int no = 0;
            foreach (GroundMassDataInput groundMass in groundMasses)
            {
                no += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([no.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.Spacing:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.GLDepth:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.AltitudeDepth:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.GranularityClass}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.LayerNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.Density:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.AgeCategory}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.NValue}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundMass.Mass:N2}"], fontSize, "right"));

                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 液状化情報
        public static void AddLiquefactionInfo(Body body, GroundInput groundInput)
        {
            double fontSize = 8.0;
            ObservableCollection<GroundMassDataInput> groundMasses = groundInput.GroundMassesData;

            AddHeader1(body, "液状化の検討", 3);

            for (int i = 0; i < 2; i++)
            {
                AddText(body, $"液状化レベル{i + 1}");

                Table table0 = CreateTableWithBorders();

                table0.Append(new TableRow(
                    CreateTableCell(["地表面加速度"], fontSize, "center"),
                    CreateTableCell([i == 0 ?$"{groundInput.GroundAcceleration1}[m/s]" :
                                             $"{groundInput.GroundAcceleration2}[m/s]"], fontSize, "right")
                ));

                body.Append(table0);
                body.Append(new Paragraph());

                Table table = CreateTableWithBorders();

                static DocumentFormat.OpenXml.Math.OfficeMath mathDeltaNf() =>
                    GetCombinedRunToMath([
                        GetRun("Δ"),
                    GetSubscript(GetRun("N"), GetRun("f"))
                ]);

                // 1行目: 表題
                TableRow headerRow = CreateHeaderRow(
                //headerRow.Append(CreateTableCell("土","質点","番号", "center"));
                CreateTableCell(["深度", "[m]"], fontSize, "center"),
                //headerRow.Append(CreateTableCell("Z","[m]", "center"));
                //headerRow.Append(CreateTableCell("土層","番号", "center"));
                //headerRow.Append(CreateTableCell("年代","分類", "center"));
                CreateTableCell(["N値"], fontSize, "center"),
                CreateTableCell(["細粒分", "含有率", "F<_c>"], fontSize, "center"),
                CreateTableCell(["液状化", "判定", "対象"], fontSize, "center"),
                CreateTableCell(["全", "応力", "σ<_z>", "[kN/m<^2>]"], fontSize, "center"),
                CreateTableCell(["有効", "応力", "σ<_z>'", "[kN/m<^2>]"], fontSize, "center"),
                CreateTableCell(["低減", "係数", "r<_d>"], fontSize, "center"),
                CreateTableCell(["換算", "N値", "N<_1>"], fontSize, "center"),
                CreateTableCell(["N値", "増分", "⊿N<_f>"], fontSize, "center"),
                CreateTableCell(["補正", "N値", "N<_a>"], fontSize, "center"),
                CreateTableCell(["液状化", "抵抗比", "τ<_L>/σ<_z>'"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "繰返し", "せん断", "応力度", "τ<_d>/σ<_z>'"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "液状化", "安全率", "F<_L>"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "地盤", "反力", "係数", "低減率", "β<_L>"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "繰返し", "せん断", "ひずみ", "γ<_cy>", "[%]"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "液状化", "水平", "変位", "∑γ<_cy>H", "[mm]"], fontSize, "center")
                    );

                table.Append(headerRow);

                int no = 0;
                foreach (GroundMassDataInput groundMass in groundMasses)
                {
                    no += 1;
                    TableRow dataRow = new();
                    //dataRow.Append(CreateTableCell(no.ToString(), "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.GLDepth:N3}"], fontSize, "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.AltitudeDepth:N3}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.GranularityClass}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.LayerNo}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.AgeCategory}", "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.NValue:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.Fc:N0}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.IsLiquefactionLayer}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.SigmaZ:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.SigmaZPrime:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.RD:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.N1:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.DeltaNf:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.NL:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.TauLonSigmaZPrime:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.TauDonSigmaZPrime[i]:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.FL[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.BetaL[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.GammaCy[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.SigmaGammaCyH[i]:N2}"], fontSize, "right"));

                    table.Append(dataRow);
                }
                body.Append(table);
                AddText(body, "");
            }
        }

        // 地盤変位
        public static void AddGroundDisplacementInfo(Body body, GroundInput groundInput)
        {
            double fontSize = 8.0;
            ObservableCollection<GroundMassDataInput> groundMasses = groundInput.GroundMassesData;

            AddHeader1(body, "地盤変位の検討", 3);

            Table table0 = CreateTableWithBorders();

            table0.Append(
                new TableRow(
                    CreateTableCell(["地盤変位算定法"], fontSize, "center"),
                    CreateTableCell([$"{groundInput.CalculationMethod}"], fontSize, "right")
                ),
                new TableRow(
                    CreateTableCell(["表層の土層"], fontSize, "center"),
                    CreateTableCell([$"{groundInput.ShallowSoilType}"], fontSize, "right")
                ),
                new TableRow(
                    CreateTableCell(["工学的基盤の単位体積重量[kN/m<^3>]"], fontSize, "center"),
                    CreateTableCell([$"{groundInput.BedrockDensity:N1}"], fontSize, "right")
                ),
                new TableRow(
                    CreateTableCell(["工学的基盤せん断波速度[m/s]"], fontSize, "center"),
                    CreateTableCell([$"{groundInput.BedrockShearWaveVelocity:N1}"], fontSize, "right")
                ));


            body.Append(table0);
            body.Append(new Paragraph());

            for (int i = 0; i < 2; i++)
            {
                AddText(body, $"レベル{i + 1}地震");


                Table table = CreateTableWithBorders();

                // 1行目: 表題
                TableRow headerRow = CreateHeaderRow(
                //headerRow.Append(CreateTableCell("土\n質点\n番号", "center"));
                CreateTableCell(["深度", "[m]"], fontSize, "center"),
                //headerRow.Append(CreateTableCell("Z","[m]", "center"));
                //headerRow.Append(CreateTableCell("土層","番号", "center"));
                //headerRow.Append(CreateTableCell("年代","分類", "center"));
                CreateTableCell(["N値"], fontSize, "center"),
                CreateTableCell(["工学的", "基盤"], fontSize, "center"),
                CreateTableCell(["初期", "S波", "速度", "[m/s]"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "等価", "S波", "速度", "[m/s]"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "等価", "せん断", "ばね剛性", "k[kN/m]"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "仮の", "無次元化", "水平変位", "u"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "調整後", "無次元化", "水平変位", "u<^*>"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "地盤の", "水平変位", "D<_max> u<^*>", "[mm]"], fontSize, "center"),
                CreateTableCell([$"レベル{i + 1}", "地盤の", "水平変位", "D<_max> u<^*>", "+∑γcyH", "[mm]"], fontSize, "center")
                );

                table.Append(headerRow);

                int no = 0;
                foreach (GroundMassDataInput groundMass in groundMasses)
                {
                    no += 1;
                    TableRow dataRow = new();
                    //dataRow.Append(CreateTableCell(no.ToString(), "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.GLDepth:N3}"], fontSize, "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.AltitudeDepth:N3}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.GranularityClass}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.LayerNo}", "right"));
                    //dataRow.Append(CreateTableCell($"{groundMass.AgeCategory}", "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.NValue:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.IsEngineeringBedrock}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.VS0:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.VSE[i]:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.K[i]:N0}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.U[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.UStar[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.DmaxUStar[i]:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.DmaxUStarSigmaGammaCyH[i]:N2}"], fontSize, "right"));

                    table.Append(dataRow);

                    if (groundMass.IsEngineeringBedrock == true)
                        break;
                }
                body.Append(table);
            }
        }

        // 荷重条件の表を追加するメソッド
        public static void AddLoadCasesTable(Body body, LoadCasesInput loadCasesInput)
        {
            // レベル1の表を追加
            AddLoadCaseTable(body, loadCasesInput.LoadCasesLevel1);
            AddText(body, "");
            // レベル2の表を追加
            AddLoadCaseTable(body, loadCasesInput.LoadCasesLevel2);

        }

        // 杭体の表を追加するメソッド
        public static void AddPileBodiesTables(Body body, ObservableCollection<PileBodyInput> pileBodies)
        {
            foreach (PileBodyInput pileBodyInput in pileBodies)
            {
                AddPileBodySegmentTable(body, pileBodyInput);
            }
        }

        // 杭先端沈下の表を追加するメソッド
        public static void AddPileTipTable(Body body, PileBodyInput pileBodyInput)
        {
            Table table = CreateTableWithBorders();
            double fontSize = 8;
            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["杭体番号"], fontSize, "center"),
            CreateTableCell(["杭先端沈下α"], fontSize, "center"),
            CreateTableCell(["杭先端沈下n"], fontSize, "center")
            );

            table.Append(headerRow);

            // 2行目: データ
            TableRow dataRow = new();
            dataRow.Append(CreateTableCell([pileBodyInput.PileBodyRef], fontSize, "left"));
            dataRow.Append(CreateTableCell([pileBodyInput.SettleAlpha.ToString()], fontSize, "left"));
            dataRow.Append(CreateTableCell([pileBodyInput.SettleN.ToString()], fontSize, "left"));

            table.Append(dataRow);

            body.Append(table);
        }

        // 杭の基本情報の表を追加するメソッド
        public static void AddPileBodySegmentTable(Body body, PileBodyInput pileBodyInput)
        {
            Table table = CreateTableWithBorders();
            double fontSize = 8;
            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["区間No"], fontSize, "center"),
            CreateTableCell(["下端深度"], fontSize, "center"),
            CreateTableCell(["区間長"], fontSize, "center"),
            CreateTableCell(["断面タイプ"], fontSize, "center"),
            CreateTableCell(["杭径"], fontSize, "center")
            );

            table.Append(headerRow);

            // データ行を追加
            int i = 0;
            foreach (PileBodySegment pileBodySegment in pileBodyInput.PileBodySegments)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileBodySegment.SegmentDepth:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileBodySegment.SegmentLength:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileBodySegment.PileSection.PileSectionType}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileBodySegment.PileSection.PileDiameter:N0}"], fontSize, "right"));

                table.Append(dataRow);
            }
            body.Append(table);
        }

        // 杭区間の表を追加するメソッド
        public static void AddPileSectionTable(Body body, PileBodyInput pileBodyInput)
        {

            // データ行を追加
            foreach (PileBodySegment pileBodySegment in pileBodyInput.PileBodySegments)
            {
                double fontSize = 8;
                Table table = CreateTableWithBorders();

                TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["項目"], fontSize, "center"),
                CreateTableCell(["値"], fontSize, "center")
                );

                table.Append(headerRow);

                TableRow dataRow1 = new();
                dataRow1.Append(CreateTableCell(["断面タイプ"], fontSize, "left"));
                dataRow1.Append(CreateTableCell([pileBodySegment.PileSection.PileSectionType], fontSize, "left"));

                table.Append(dataRow1);

                TableRow dataRow2 = new();
                dataRow2.Append(CreateTableCell(["杭径"], fontSize, "left"));
                dataRow2.Append(CreateTableCell([pileBodySegment.PileSection.PileDiameter.ToString()], fontSize, "left"));

                table.Append(dataRow2);

                body.Append(table);
            }
        }

        // 杭配置の表を追加するメソッド
        public static void AddPileLayoutTables(Body body, ObservableCollection<PileLayoutDataItem> pileLayoutItems)
        {
            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["No"], fontSize, "center"),
            CreateTableCell(["X", "[m]"], fontSize, "center"),
            CreateTableCell(["Y", "[m]"], fontSize, "center"),
            CreateTableCell(["杭頭", "Z", "[m]"], fontSize, "center"),
            CreateTableCell(["杭体", "番号"], fontSize, "center"),
            CreateTableCell(["地盤", "番号"], fontSize, "center"),
            CreateTableCell(["地盤", "杭", "レベル", "セット", "番号"], fontSize, "center"),
            CreateTableCell(["群杭", "係数"], fontSize, "center"),
            CreateTableCell(["杭間隔", "比"], fontSize, "center")
            );
            table.Append(headerRow);

            int i = 0;
            // データ行を追加
            foreach (PileLayoutDataItem pileLayoutData in pileLayoutItems)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.Point3D.X:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.Point3D.Y:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.Point3D.Z:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.PileBodyNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.GroundNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.SoilPileAltNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.GroupPileFactor:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.PileSpacingFactor:N3}"], fontSize, "right"));
                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 杭軸力の表を追加するメソッド
        public static void AddPileAxialLoadTables(Body body, ObservableCollection<PileLayoutDataItem> pileLayoutItems)
        {
            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["No"], fontSize, "center"),
            //headerRow.Append(CreateTableCell("X","[m]", fontSize, "center"));
            //headerRow.Append(CreateTableCell("Y","[m]", fontSize, "center"));
            CreateTableCell(["VL", "[kN]"], fontSize, "center"),
            CreateTableCell(["VL<_add>", "[kN]"], fontSize, "center"),
            CreateTableCell(["1-1", "[kN]"], fontSize, "center"),
            CreateTableCell(["1-2", "[kN]"], fontSize, "center"),
            CreateTableCell(["1-3", "[kN]"], fontSize, "center"),
            CreateTableCell(["1-4", "[kN]"], fontSize, "center"),
            CreateTableCell(["2-1", "[kN]"], fontSize, "center"),
            CreateTableCell(["2-2", "[kN]"], fontSize, "center"),
            CreateTableCell(["2-3", "[kN]"], fontSize, "center"),
            CreateTableCell(["2-4", "[kN]"], fontSize, "center")
            );

            table.Append(headerRow);

            int i = 0;
            // データ行を追加
            foreach (PileLayoutDataItem pileLayoutData in pileLayoutItems)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell($"{pileLayoutData.X}", "right"));
                //dataRow.Append(CreateTableCell($"{pileLayoutData.Y}", "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceVL0:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceVLAdditional:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel1s[0]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel1s[1]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel1s[2]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel1s[3]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel2s[0]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel2s[1]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel2s[2]:N0}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceLevel2s[3]:N0}"], fontSize, "right"));

                string AF(IList<double> list, int idx) => (list != null && idx >= 0 && idx < list.Count) ? $"{list[idx]:N0}" : string.Empty;

                // データ行の該当箇所
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel1s, 0)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel1s, 1)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel1s, 2)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel1s, 3)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel2s, 0)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel2s, 1)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel2s, 2)], fontSize, "right"));
                dataRow.Append(CreateTableCell([AF(pileLayoutData.AxialForceLevel2s, 3)], fontSize, "right"));
                table.Append(dataRow);
            }
            body.Append(table);
        }

        // 前後方杭の表を追加するメソッド
        public static void AddIsFrontPileTables(Body body, ObservableCollection<PileLayoutDataItem> pileLayoutItems)
        {
            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["No"], fontSize, "center"),
            CreateTableCell(["X", "[m]"], fontSize, "center"),
            CreateTableCell(["Y", "[m]"], fontSize, "center"),

            CreateTableCell(["方向1"], fontSize, "center"),
            CreateTableCell(["方向2"], fontSize, "center"),
            CreateTableCell(["方向3"], fontSize, "center"),
            CreateTableCell(["方向4"], fontSize, "center")
            );

            table.Append(headerRow);

            int i = 0;
            // データ行を追加
            foreach (PileLayoutDataItem pileLayoutData in pileLayoutItems)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.Point3D.X:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.Point3D.Y:N3}"], fontSize, "right"));
                //dataRow.Append(CreateTableCell([pileLayoutData.IsFrontPiles[0] ? "前" : "後"], fontSize, "center"));
                //dataRow.Append(CreateTableCell([pileLayoutData.IsFrontPiles[1] ? "前" : "後"], fontSize, "center"));
                //dataRow.Append(CreateTableCell([pileLayoutData.IsFrontPiles[2] ? "前" : "後"], fontSize, "center"));
                //dataRow.Append(CreateTableCell([pileLayoutData.IsFrontPiles[3] ? "前" : "後"], fontSize, "center"));

                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 0)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 1)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 2)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 3)], fontSize, "center"));
                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 前後方杭の表を追加するメソッド
        public static void AddEmbedment(Body body, EmbedmentInput embedmentInput)
        {
            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["No"], fontSize, "center"),
                CreateTableCell(["厚さ", "[m]"], fontSize, "center"),
                CreateTableCell(["上端", "Z", "[m]"], fontSize, "center"),
                CreateTableCell(["下端", "Z", "[m]"], fontSize, "center"),
                CreateTableCell(["X1", "[m]"], fontSize, "center"),
                CreateTableCell(["Y1", "[m]"], fontSize, "center"),
                CreateTableCell(["X2", "[m]"], fontSize, "center"),
                CreateTableCell(["Y2", "[m]"], fontSize, "center"),
                CreateTableCell(["DX", "[m]"], fontSize, "center"),
                CreateTableCell(["DY", "[m]"], fontSize, "center"));

            table.Append(headerRow);

            int i = 0;
            // データ行を追加
            foreach (EmbedmentDataItem embedmentLayer in embedmentInput.EmbedmentLayers)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.LayerThickness:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.TopAltitude:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.BottomAltitude:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.X1:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.Y1:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.X2:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.Y2:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.DX:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{embedmentLayer.DY:N3}"], fontSize, "right"));
                table.Append(dataRow);
            }
            body.Append(table);
        }

        private static string FrontMark(IList<bool> src, int idx)
            => (src != null && idx >= 0 && idx < src.Count) ? (src[idx] ? "前" : "後") : string.Empty;

        // 杭抵抗
        private static void AddPileResistanceDescription(Body body, ObservableCollection<SoilPile> soilPiles)
        {
            AddHeader1(body, "杭支持力の検討", 3);
            AddText(body, "杭支持力の検討では、杭の押込側と引抜側の使用限界、損傷限界、終局限界を計算し、表にまとめます。");
            AddText(body, "杭の押込側と引抜側の支持力は、地盤の種類や杭の工法によって異なります。");
            AddText(body, "以下に杭支持力一覧表を示します。");

            AddText(body, "【先端支持力】");

            foreach (SoilPile soilPile in soilPiles)
            {
                if (soilPile.PileBodyInput.PileBodySegments.Count > 0)
                {

                    AddText(body, $"杭体番号: {soilPile.PileBodyInput.PileBodyRef}");
                    AddText(body, $"杭先端径: {soilPile.PileBodyInput.PileToeDia:N0} mm");
                    AddText(body, $"杭先端標高: {soilPile.PileBottomAltitude:N3} m");
                }
                else
                {
                    AddText(body, "杭体番号: 不明");
                    AddText(body, "杭先端径: 不明");
                    AddText(body, "杭先端標高: 不明");
                }

                AddText(body, $"先端平均N値算定用上端標高:{soilPile.PileToeNValueAverageRangeUpperAltitude:N3} m");
                AddText(body, $"杭先端標高:{soilPile.PileBottomAltitude:N3} m");
                AddText(body, $"先端平均N値算定用下端標高:{soilPile.PileToeNValueAverageRangeLowerAltitude:N3} m");

                AddText(body, $"平均N値算定対象N値:{soilPile.NValuesForAverage}");
                AddText(body, $"平均N値:{soilPile.PileToeNValue:N1}");
            }
        }

        // 均等列幅リスト取得メソッド
        private static int[] GetEqualColumnWidths(int count, double totalWidth = 8500)
        {
            List<int> columnWidths = [];
            int width = (int)Math.Ceiling(totalWidth / count);
            for (int i = 0; i < count; i++)
            {
                columnWidths.Add(width);
            }
            return [.. columnWidths];
        }

        // 杭支持力一覧表メソッド
        public static void AddVerticalResistance(Body body, ObservableCollection<SoilPile> soilPiles)
        {
            double fontSize = 8;
            Table table = CreateTableWithBordersAndWidths(GetEqualColumnWidths(13));

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["No"], fontSize, "center"),
                CreateTableCell(["杭体", "番号"], fontSize, "center"),
                CreateTableCell(["地盤", "番号"], fontSize, "center"),
                CreateTableCell(["杭工法"], fontSize, "center"),
                CreateTableCell(["杭頭径", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端径", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端", "Z標高", "[m]"], fontSize, "center"),
                CreateTableCell(["押込側", "使用", "限界", "R_SLS", "[kN]"], fontSize, "center"),
                CreateTableCell(["押込側", "損傷", "限界", "R_DLS", "[kN]"], fontSize, "center"),
                CreateTableCell(["押込側", "終局", "限界", "R_ULS", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "使用", "限界", "Rt_SLS", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "損傷", "限界", "Rt_DLS", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "終局", "限界", "Rt_ULS", "[kN]"], fontSize, "center"));

            table.Append(headerRow);

            int i = 0;
            // データ行を追加
            foreach (SoilPile soilPile in soilPiles)
            {
                i += 1;
                TableRow dataRow = new();
                dataRow.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.GroundNo}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileConstructionType}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileBodySegments[0].PileSection.PileDiameter:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileToeDia:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBottomAltitude:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.R_SLS:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.R_DLS:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.R_ULS:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.Rt_SLS:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.Rt_DLS:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.Rt_ULS:N1}"], fontSize, "right"));
                table.Append(dataRow);
            }
            body.Append(table);
        }

        //数式
        public static DocumentFormat.OpenXml.Math.Run GetRun(string text)
        {
            // Create the base (mc).
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Text(text));
        }

        // 数式　上付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSuperscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run superscriptRun)
        {
            // ベースのテキストを作成
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);

            // 上付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.SuperArgument superArgument = new(superscriptRun);

            // Subscript要素を作成
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Superscript(baseElement, superArgument));
        }

        // 数式　下付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSubscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subscriptRun)
        {
            // ベースのテキストを作成
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);

            // 下付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.SubArgument subArgument = new(subscriptRun);

            // Subscript要素を作成
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.Subscript(baseElement, subArgument));
        }

        // 数式　上付き・下付き文字
        public static DocumentFormat.OpenXml.Math.Run GetSubSuperscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subscriptRun,
            DocumentFormat.OpenXml.Math.Run superscriptRun)
        {
            // ベースのテキストを作成
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);

            // 下付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.SubArgument subArgument = new(subscriptRun);

            // 上付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.SuperArgument superArgument = new(superscriptRun);

            // Subscript要素を作成
            return new DocumentFormat.OpenXml.Math.Run(new DocumentFormat.OpenXml.Math.SubSuperscript(baseElement, subArgument, superArgument));
        }

        // 左下と右下に下付き文字をもつ文字
        public static DocumentFormat.OpenXml.Math.Run GetDoubleSubscript(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run leftSubscriptRun,
            DocumentFormat.OpenXml.Math.Run rightSubscriptRun)
        {
            // 左下付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.Subscript leftSubscript = new(
                new DocumentFormat.OpenXml.Math.Base(baseRun),
                new DocumentFormat.OpenXml.Math.SubArgument(leftSubscriptRun)
            );

            // 右下付き文字のテキストを作成
            DocumentFormat.OpenXml.Math.Subscript rightSubscript = new(
                new DocumentFormat.OpenXml.Math.Base(leftSubscript),
                new DocumentFormat.OpenXml.Math.SubArgument(rightSubscriptRun)
            );

            // Run要素を作成
            return new DocumentFormat.OpenXml.Math.Run(rightSubscript);
        }

        // 数式　分数
        public static DocumentFormat.OpenXml.Math.Run GetFraction(
            DocumentFormat.OpenXml.Math.Run numerator, DocumentFormat.OpenXml.Math.Run denominator)
        {
            return new DocumentFormat.OpenXml.Math.Run(
                    new DocumentFormat.OpenXml.Math.Fraction(
                        new DocumentFormat.OpenXml.Math.FractionProperties(),
                        new DocumentFormat.OpenXml.Math.Numerator(numerator),
                        new DocumentFormat.OpenXml.Math.Denominator(denominator)));
        }

        // 数式　分数
        public static DocumentFormat.OpenXml.Math.OfficeMath GetFraction(
            DocumentFormat.OpenXml.Math.OfficeMath numerator, DocumentFormat.OpenXml.Math.OfficeMath denominator)
        {
            return new DocumentFormat.OpenXml.Math.OfficeMath(
                new DocumentFormat.OpenXml.Math.Fraction(
                    new DocumentFormat.OpenXml.Math.FractionProperties(),
                    new DocumentFormat.OpenXml.Math.Numerator(numerator),
                    new DocumentFormat.OpenXml.Math.Denominator(denominator)));
        }

        // 数式　平方根
        public static DocumentFormat.OpenXml.Math.Run GetRadicalRun(DocumentFormat.OpenXml.Math.Run run)
        {
            return new DocumentFormat.OpenXml.Math.Run(
                new DocumentFormat.OpenXml.Math.Radical(new DocumentFormat.OpenXml.Math.Base(run)));
        }

        // 数式　上バー付
        public static DocumentFormat.OpenXml.Math.Run GetTopBarredRun(DocumentFormat.OpenXml.Math.Run baseRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.AccentProperties accentProperties = new(new DocumentFormat.OpenXml.Math.AccentChar { Val = "¯" });

            // Create the accent element.
            DocumentFormat.OpenXml.Math.Accent accent = new(accentProperties, baseElement);

            return new DocumentFormat.OpenXml.Math.Run(accent);
        }

        // 数式　下バー付
        public static DocumentFormat.OpenXml.Math.Run GetBottomBarredRun(DocumentFormat.OpenXml.Math.Run baseRun)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            return new DocumentFormat.OpenXml.Math.Run(
                new DocumentFormat.OpenXml.Math.Bar(baseElement));
        }

        // 数式　()
        public static DocumentFormat.OpenXml.Math.Run GetDelimiteredRun(
            DocumentFormat.OpenXml.Math.Run baseRun, string beginChar, string endChar)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.DelimiterProperties delimiterProperties = new()
            {
                BeginChar = new DocumentFormat.OpenXml.Math.BeginChar { Val = beginChar }, // "(", "{", "["
                EndChar = new DocumentFormat.OpenXml.Math.EndChar { Val = endChar } // ")", "}", "]"
            };

            // Create the delimiter element.
            return new DocumentFormat.OpenXml.Math.Run(
               new DocumentFormat.OpenXml.Math.Delimiter(delimiterProperties, baseElement));
        }

        // 数式　()
        public static DocumentFormat.OpenXml.Math.OfficeMath GetDelimiteredMath(
            DocumentFormat.OpenXml.Math.OfficeMath baseRun, string beginChar, string endChar)
        {
            DocumentFormat.OpenXml.Math.Base baseElement = new(baseRun);
            DocumentFormat.OpenXml.Math.DelimiterProperties delimiterProperties = new()
            {
                BeginChar = new DocumentFormat.OpenXml.Math.BeginChar { Val = beginChar }, // "(", "{", "["
                EndChar = new DocumentFormat.OpenXml.Math.EndChar { Val = endChar } // ")", "}", "]"
            };

            // Create the delimiter element.
            return new DocumentFormat.OpenXml.Math.OfficeMath(
               new DocumentFormat.OpenXml.Math.Delimiter(delimiterProperties, baseElement));
        }

        // 数式　総和∑
        public static DocumentFormat.OpenXml.Math.Run GetSummationRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            // NaryPropertiesを作成
            DocumentFormat.OpenXml.Math.NaryProperties naryProperties = new(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∑" }
            );

            // Nary要素を作成
            DocumentFormat.OpenXml.Math.Nary nary = new(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );

            // Run要素を作成
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // 積分記号
        public static DocumentFormat.OpenXml.Math.Run GetIntegralRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            // NaryPropertiesを作成（積分記号は "∫"）
            var naryProperties = new DocumentFormat.OpenXml.Math.NaryProperties(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∫" }
            );

            // Nary要素を作成
            var nary = new DocumentFormat.OpenXml.Math.Nary(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );

            // Run要素を作成
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // 数式　総和∑
        public static DocumentFormat.OpenXml.Math.OfficeMath GetSummationMath(
            DocumentFormat.OpenXml.Math.OfficeMath basemath,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            // NaryPropertiesを作成
            DocumentFormat.OpenXml.Math.NaryProperties naryProperties = new(
                new DocumentFormat.OpenXml.Math.AccentChar { Val = "∑" }
            );

            // Nary要素を作成
            DocumentFormat.OpenXml.Math.Nary nary = new(
                naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(basemath)
            );

            // OfficeMath要素を作成
            DocumentFormat.OpenXml.Math.OfficeMath officeMath = new();
            officeMath.Append(nary);
            return officeMath;
        }


        // 数式　積分記号
        public static DocumentFormat.OpenXml.Math.Run GetSekibunRun(
            DocumentFormat.OpenXml.Math.Run baseRun,
            DocumentFormat.OpenXml.Math.Run subRun,
            DocumentFormat.OpenXml.Math.Run superRun)
        {
            // Nary要素を作成
            DocumentFormat.OpenXml.Math.Nary nary = new(
                //naryProperties,
                new DocumentFormat.OpenXml.Math.SubArgument(subRun),
                new DocumentFormat.OpenXml.Math.SuperArgument(superRun),
                new DocumentFormat.OpenXml.Math.Base(baseRun)
            );

            // Run要素を作成
            return new DocumentFormat.OpenXml.Math.Run(nary);
        }

        // CombineRun to Math
        public static DocumentFormat.OpenXml.Math.OfficeMath GetCombinedRunToMath(
            List<DocumentFormat.OpenXml.Math.Run> runs)
        {
            DocumentFormat.OpenXml.Math.OfficeMath officeMath = new();
            foreach (DocumentFormat.OpenXml.Math.Run run in runs)
            {
                officeMath.Append(run);
            }
            return officeMath;
        }

        // CombineRun
        public static DocumentFormat.OpenXml.Math.Run GetCombinedRun(
            List<DocumentFormat.OpenXml.Math.Run> runs)
        {
            DocumentFormat.OpenXml.Math.Run combinedRun = new();
            foreach (DocumentFormat.OpenXml.Math.Run run in runs)
            {
                combinedRun.Append(run);
            }
            return combinedRun;
        }

        // Mathをbodyに挿入するメソッド
        public static void PutMathInBody(Body body, DocumentFormat.OpenXml.Math.OfficeMath math)
        {
            Paragraph paragraph = new();
            paragraph.Append(math);
            body.Append(paragraph);
        }

        // タブ付きシンボル説明
        public static void AddSymbolDescriptionWithTab(
            Body body,
            double tabPositionsMm,
            object[] parts,
            double fontSize = 10.5,
            int leftIndentMm = 15,
            int firstLineIndentMm = -10
        )
        {
            if (body == null) return;

            // 空でも段落は生成（従来互換）
            parts ??= [];

            var paragraph = new Paragraph
            {
                ParagraphProperties = new ParagraphProperties
                {
                    Indentation = new Indentation
                    {
                        //Left = leftIndentMm != 0 ? (leftIndentMm * TwipsPerMm).ToString() : null,
                        //FirstLine = firstLineIndentMm != 0 ? (firstLineIndentMm * TwipsPerMm).ToString() : null
                        Left = leftIndentMm != 0 ? MmToTwips(leftIndentMm) : null,
                        FirstLine = firstLineIndentMm != 0 ? MmToTwips(firstLineIndentMm) : null,
                        //Hanging = hangingIndentMm != 0 ? MmToTwips(hangingIndentMm) : null
                    },
                    Tabs = tabPositionsMm > 0
                        ? new Tabs(new TabStop
                        {
                            Val = TabStopValues.Left,
                            Position = (int)(tabPositionsMm * TwipsPerMm)
                        })
                        : null
                }
            };

            if (parts.Length == 0)
            {
                body.Append(paragraph);
                return;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == null) continue;

                void AppendString(string s)
                {
                    if (string.IsNullOrEmpty(s)) return;
                    var lines = s.Replace("\r\n", "\n").Split('\n');
                    for (int li = 0; li < lines.Length; li++)
                    {
                        foreach (var e in BuildInlineMixedRuns(lines[li], fontSize))
                            paragraph.Append(e);
                        if (li < lines.Length - 1)
                            paragraph.Append(new Run(new Break()));
                    }
                }

                switch (part)
                {
                    case string s:
                        AppendString(s);
                        break;

                    case Run r:
                        paragraph.Append(r);
                        break;

                    case DocumentFormat.OpenXml.Math.OfficeMath math:
                        paragraph.Append(math);
                        break;

                    case OpenXmlElement oxEl:
                        // 他の OpenXmlElement が来た場合もそのまま追加
                        paragraph.Append(oxEl);
                        break;

                    default:
                        AppendString(part.ToString());
                        break;
                }

                // 1つ目の要素の後にのみタブを挿入（残りがある場合）
                if (i == 0 && parts.Length > 1)
                {
                    paragraph.Append(new Run(new TabChar()));
                }
            }
            body.Append(paragraph);
        }

        // p = k_h y（TeX形式で記述してパーサ経由でOfficeMathに変換）
        public static void AddEquationP(Body body)
        {
            var math = Tex("p = k_{h} y");
            PutMathInBody(body, math);
        }

        // kh = 3.16 kh0
        public static void AddEquationkh_1(Body body)
        {
            var math = Tex("k_{h}=3.16k_{h0}");
            PutMathInBody(body, math);
        }

        // kh = kh0 / sqrt(ybar)
        public static void AddEquationkh_2(Body body)
        {
            var math = Tex(@"k_{h} = \frac{k_{h0}}{\sqrt{\overline{y}}} = \frac{k_{h0}}{\sqrt{\frac{y}{0.01}}}");
            PutMathInBody(body, math);
        }

        // kh0
        public static void AddEquation_kh0(Body body)
        {
            var math = Tex(@"k_{h0} = \alpha \xi E_{0} \left(\frac{B}{B_{0}}\right)^{-3/4}");
            PutMathInBody(body, math);
        }

        // Kp
        public static void AddEquation_Kp(Body body)
        {
            var math = Tex(@"k_{p} = \frac{1+\sin\phi}{1-\sin\phi}");
            PutMathInBody(body, math);
        }

        private static void AddEq(Body body, string tex)
        {
            if (body == null) return;
            PutMathInBody(body, Tex(tex));
        }

        // PHC杭の使用限界曲げモーメント
        public static void AddEquation_PHCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の損傷限界曲げモーメント
        public static void AddEquation_PHCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の安全限界曲げモーメント
        public static void AddEquation_PHCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");
            PutMathInBody(body, math);
        }

        // PHC杭の使用限界せん断力
        public static void AddEquation_PHCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \min\left(Q_{s1},Q_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の損傷限界せん断力
        public static void AddEquation_PHCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \min\left(Q_{d1},Q_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PHC杭の安全限界せん断力
        public static void AddEquation_PHCPileQu(Body body)
        {
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(Q_{u1},Q_{u2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の使用限界曲げモーメント
        public static void AddEquation_PRCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の損傷限界曲げモーメント
        public static void AddEquation_PRCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の安全限界曲げモーメント
        public static void AddEquation_PRCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{u1},M_{u2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の使用限界せん断力
        public static void AddEquation_PRCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \min\left(Q_{s1},Q_{s2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の損傷限界せん断力
        public static void AddEquation_PRCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \min\left(Q_{d1},Q_{d2}\right)");
            PutMathInBody(body, math);
        }

        // PRC杭の安全限界せん断力
        public static void AddEquation_PRCPileQu(Body body)
        {
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(Q_{u1},Q_{u2}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の使用限界曲げモーメント
        public static void AddEquation_SCPileMs(Body body)
        {
            var math = Tex(@"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の損傷限界曲げモーメント
        public static void AddEquation_SCPileMd(Body body)
        {
            var math = Tex(@"M_{d} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の安全限界曲げモーメント
        public static void AddEquation_SCPileMu(Body body)
        {
            var math = Tex(@"M_{u} = \beta_{1}\cdot \beta_{2}\cdot \min\left(M_{u1},M_{u2}\right)");
            PutMathInBody(body, math);
        }

        // SC杭の使用限界せん断力
        public static void AddEquation_SCPileQs(Body body)
        {
            var math = Tex(@"Q_{s} = \beta_{1}\cdot \frac{1}{\kappa_{s}}\cdot f_{s}\cdot A_{s}");
            PutMathInBody(body, math);
        }

        // SC杭の損傷限界せん断力
        public static void AddEquation_SCPileQd(Body body)
        {
            var math = Tex(@"Q_{d} = \beta_{1}\cdot \beta_{2}\cdot \frac{1}{\kappa_{s}}\cdot f_{d}\cdot A_{s}");
            PutMathInBody(body, math);
        }

        // SC杭の安全限界せん断力
        public static void AddEquation_SCPileQu(Body body)
        {
            // TeX 表記に置き換え（Q_{s,un} は元の GetDoubleSubscript(Q, s, un) 相当）
            var math = Tex(@"Q_{u} = \beta_{1}\cdot \beta_{2}\cdot Q_{s,un}");
            PutMathInBody(body, math);
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

        // FT-Pile構法説明
        private void AddDescriptionFTPile(Body body)
        {
            //AddText(body, "F.T.Pile構法", "left");
            AddHeader1(body, "F.T.Pile構法", 1);
            AddText(body, "(BCJ評定-FD0141)", "left");

            AddHeader1(body, "概要", 2);

            AddText(body, "設計用地震力に相当する水平力と鉛直力が作用したとき，" +
                "杭頭接合部および杭体の応力\r\nを4.4 に示す応力解析により算定し，" +
                "以下の項目を満足していることを確認する。" +
                "ただし，\r\nパイルキャップにFc36を超える高強度コンクリートを使用する場合は，" +
                "杭頭の損傷限界に\r\nついて別途検討を行うものとする。" +
                "表4-3-1に設計クライテリアを示す。", "left");
            AddText(body, "（1） 標準タイプにおいては，杭頭接合部の回転角がパイルキャップのひび割れ発生限界によって決まる$\\theta_{ac}$を超えないこと。");

            AddText(body, "（2） 引抜き対応タイプにおいては，" +
                "杭頭接合部の回転角がパイルキャップのひび割れ発生限界によって" +
                "求まる回転角$\\theta_{ac}$を超えないこと，" +
                "かつ引抜き抵抗用鋼棒の短期許容応力度から求まる回転角$\\theta_{as}$を超えないこと。");
            AddText(body, "（3） 杭頭接合部のせん断力が短期許容せん断力を超えないこと。");
            AddText(body, "（4） 杭体の軸力・曲げモーメント・せん断力が短期許容耐力を超えないこと。");

            AddHeader1(body, "許容回転角の設定", 2);
            //AddText(body, "許容回転角の設定");
            AddText(body, "杭頭接合部の許容回転角$\\theta_{a}$は，パイルキャップにひび割れが発生する回転角$\\theta_{ac}$以下かつ，" +
                "引抜き対応タイプにおいては引抜き抵抗用鋼棒が短期許容応力に達する回転角$\theta_{as}$以下とし，次式によって定める。 ");

            AddInlineMathParagraph(body, [Tex(@"\theta_{a} = \min\left(\theta_{ac},\theta_{as}\right)")]);
            AddInlineMathParagraph(body, [Tex(@"\theta_{ac} = 0.03 - 0.05 + \frac{\sigma_{nc}}{\phi_{c}\cdot F_{c}}")]);
            AddInlineMathParagraph(body, [Tex(@"\theta_{as} = \frac{\delta_{a}}{D_{s}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{ac}"), ": パイルキャップのひび割れで決まる許容回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{as}"), ": 引抜き抵抗用鋼棒の短期許容応力で決まる許容回転角(rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{nc}"), ": 圧縮合力による軸応力度(N/mm<^2>)"]);

            AddInlineMathParagraph(body, [Tex(@"\sigma_{nc} = \frac{N_{c}}{A_{p}} \times 10^{-3}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N_{c}"), ": コンクリートの圧縮合力"]);

            AddText(body, "標準タイプ", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = N")]);

            AddInlineMathParagraph(body, [Tex(@"N \le 0")]);
            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);

            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| < \left|\frac{M}{D_{s}}\right|")]);

            AddInlineMathParagraph(body, [Tex(@"N > 0")]);
            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);

            AddText(body, "引抜きタイプ", "left");
            AddText(body, "ⅰ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = 0")]);
            AddText(body, "ⅱ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = \left|\frac{M}{D_{s}}\right| + \frac{N}{2}")]);
            AddText(body, "ⅲ)", "left");
            AddInlineMathParagraph(body, [Tex(@"N_{c} = N")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{p}"), ": 支圧面積（杭頭面積）(m<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\phi_{c}"), ": 支圧係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": パイルキャップコンクリートの設計基準強度(N/mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度(N/mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数(N/mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ(m)"]);

            AddHeader1(body, "短期許容せん断力の算定", 2);

            AddText(body, "設計用地震力によるせん断力が，杭頭接合部の短期許容せん断力以内であることを確認する。" +
                "杭頭接合部の短期許容せん断力は，" +
                "杭頭の摩擦抵抗とパイルキャップ（へりあき）のせん断抵抗を考慮した（4.4）式により算定する。", "left");

            AddInlineMathParagraph(body, [Tex(@"Q_{a} = \mu_{a}\cdot N_{c} + Q_{ha}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{a}"), ": 杭頭接合部の短期許容せん断力(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\mu_{a}"), ": 許容摩擦係数(=0.2)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離(m)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{ha}"), ": パイルキャップの短期許容せん断抵抗(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{ct}"), ": コンクリートの引張抵抗(N/mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{ct}"), ": 破壊面の水平投影面積(mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"B"), ": パイルキャップの短辺(mm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"h"), ": へりあき(mm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"d_{e}"), ": 杭頭の有効埋込み深さ(mm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"d"), ": 杭頭の埋込み深さ(mm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D"), ": 杭径(mm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Q_{hu}"), ": パイルキャップの終局せん断抵抗(kN)"]);

            AddHeader1(body, "杭頭接合部の回転剛性の設定", 2);

            AddText(body, "$M$-$\\theta$関係の基本式", "left");
            AddText(body, "杭頭接合部の回転剛性は，杭頭の曲げモーメント$M$と回転角$\\theta$の関係（以後，$M$-$\\theta$関係と略す）を" +
                "（4.8）式と（4.9）式でモデル化して用いる。図4-4-2に$M$-$\\theta$関係のモデル化の概要を示す。", "left");

            AddInlineMathParagraph(body, [Tex(@"\left|\frac{N}{2}\right| > \left|\frac{M}{D_{s}}\right|")]);
            AddInlineMathParagraph(body, [Tex(@"M = \frac{\theta}{\theta + \theta_{f}} \cdot M_{max}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M"), ": 杭頭接合部の曲げモーメント(kNm)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta"), ": 杭頭接合部の回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{f}"), ": 基準回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kN・m/rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大曲げモーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{c} = \frac{M_{c}}{K_{0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{c}"), ": 浮き上がりモーメント(kNm)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{c} = \frac{D_{1}^{2} + D_{2}^{2}}{8 D_{1}} \cdot N")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{c}"), ": 浮き上がり回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{f} = \frac{M_{max} - M_{c}}{K_{0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\\theta_{f}"), ": 基準回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大曲げモーメント(kN・m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{c}"), ": 浮き上がりモーメント(kNm)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 初期回転剛性(kN・m/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{\pi E}{32(1-\nu^{2})}\cdot\frac{10^{3}}{D_{1}^{3}-D_{2}^{3}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E"), ": 材料の弾性係数(N/mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu"), ": ポアソン比"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{2}"), ": 杭の内径(m)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{0} = \frac{M_{a}}{\theta_{a}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{0}"), ": 杭頭接合部の初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{a}"), ": 杭頭接合部の許容モーメント(kNm)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{a} = 3.75\times 10^{-4}a_{g}\cdot f_{t}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddInlineMathParagraph(body, [Tex(@"\theta_{a} = \frac{\delta_{a}}{D_{s}}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{a}"), ": 許容回転角(rad)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\delta_{a}"), ": 鋼棒の許容伸び(m)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\epsilon_{a}"), ": 鋼棒の許容ひずみ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{t}"), ": 鋼棒の短期許容応力度(N/mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{s}"), ": 鋼棒の弾性係数(N/mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L_{s}"), ": 鋼棒の有効長さ(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離(m)"]);

            AddHeader1(body, "最大抵抗モーメント$M_{max}$の設定方法", 2);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{max}"), ": 最大抵抗モーメント(kN・m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{e}"), ": 基準抵抗抵抗モーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{max} = \eta \cdot M_{e}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\eta"), ": 補正係数"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{n}"), ":  パイルキャップの軸応力度(N/mm<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{p}"), ": 杭頭面積(m<^2>)"]);

            AddInlineMathParagraph(body, [Tex(@"\eta = -0.16 \frac{\sigma_{n}}{F_{c}} + 1.0")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{e}"), ": 基準抵抗モーメント(kN・m)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{e} = 0.5N \cdot D_{1}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{1}"), ": 杭の外径(m)"]);

            AddInlineMathParagraph(body, [Tex(@"M_{e} = 5\times 10^{-4}a_{g}\cdot f_{u}\cdot D_{s} + 0.5N\cdot D_{s}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"a_{g}"), ": 鋼棒の全断面積(mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"f_{u}"), ": 鋼棒の短期許容応力度(N/mm<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"N"), ": 軸力(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D_{s}"), ": 鋼棒の配置距離(m)"]);
        }

        // キャプテンパイル工法説明
        private void AddDescriptionCaptainPile(Body body)
        {
            AddHeader1(body, "キャプテンパイル工法", 1);
            //AddText(body, "キャプテンパイル工法", "left");
            AddText(body, "(BCJ評定-FD0230-01)", "left");
            //AddText(body, "杭頭接合部の曲げモーメント-回転角関係の評価", "left");
            AddHeader1(body, "杭頭接合部の曲げモーメント-回転角関係の評価", 2);
            //AddText(body, "（1）杭頭接合部構造性能モデル化", "left");
            AddHeader1(body, "杭頭接合部構造性能モデル化", 3);

            AddInlineMathParagraph(body, ["杭頭回転特性は、杭頭曲げモーメント", Tex(@"M_{p}"), "と回転角", Tex(@"\theta_{p}"), "の関係により評価する。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{1}"), ": 杭頭接合部の等価初期回転剛性"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"K_{2}"), ": 杭頭接合部の圧縮軸力時における2次回転剛性", Tex(@"K_{2} = \frac{M_{y} - M_{1}}{\theta_{y} - \theta_{1}}")]);

            AddInlineMathParagraph(body, ["あるいは、引張軸力時における初期回転剛性", Tex(@"K_{2} = \frac{M_{y}}{\theta_{y}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{1}"), ": 離間時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{y}"), ": 降伏時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M_{u}"), ": 終局時曲げモーメント"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{1}"), ": 離間時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}"), ": 降伏時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{y}'"), ": 終局時回転角"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\theta_{u}"), ": 限界回転角(=0.04rad)"]);

            AddHeader1(body, "設定値の算出法", 3);
            //AddText(body, "（2）設定値の算出法");
            AddHeader1(body, "杭頭接合部の等価初期回転剛性", 4);

            AddInlineMathParagraph(body, ["杭頭接合部の圧縮軸力時における等価初期回転剛性", Tex(@"K_{1}"), "は下式により算定する。"]);

            AddInlineMathParagraph(body, [Tex(@"K_{1} = K_{e} = \frac{1}{\dfrac{1}{K_{p}} + \dfrac{1}{K_{c}} + \dfrac{1}{K_{b}}}")]);

            AddText(body, "ここに", "left");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{e}"), ": 初期回転剛性(kNm/rad)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{p}"), ": 杭体部分の回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{p} = \frac{E_{p}\cdot I_{p}}{H_{p}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{p}"), ": 杭体のヤング係数(kN/m<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{p}"), ": 杭体の断面二次モーメント、絞り部ありの場合は絞り部径を杭径とみなす(m<^4>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{p}"), ": 杭体とPCリングの重なり長さ(m)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{c}"), ": PCリング内コンクリートの回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{c} = \frac{E_{c}\cdot I_{c}}{H_{c}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{c}"), ": 杭体のヤング係数(kN/m<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{c}"), ": PCリング内側コンクリートの断面二次モーメント、絞り部有無によらない(m<^4>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{c}"), ": 杭頭接合面からPCリング上端までの長さ(m)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"K_{b}"), ": パイルキャップ部分の回転剛性(kNm/rad)"]);

            AddInlineMathParagraph(body, [Tex(@"K_{b} = \frac{E_{b}\cdot I_{b}}{H_{b}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"E_{b}"), ": パイルキャップコンクリートのヤング係数(kN/m<^2>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"I_{b}"), ": 仮想円柱のコンクリートの断面二次モーメント、絞り部有無によらない(m<^4>)"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"H_{b}"), ": 仮想円柱の高さ(=D/2)(m)"]);

            AddHeader1(body, "離間時曲げモーメント", 4);

            AddInlineMathParagraph(body, ["引張応力が発生しない離間時曲げモーメント", Tex(@"M_{1}"), "は下式により算定する。"]);

            AddInlineMathParagraph(body, [Tex(@"M_{1} = \sigma_{0} \cdot Z")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["ここに、", Tex(@"\sigma_{0}"), ": 杭頭接合部の軸方向応力度(kN/m<^2>)"]);

            AddInlineMathParagraph(body, [Tex(@"\sigma_{0} = \frac{N}{A_{e}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"A_{e}"), ": 杭頭接合部の断面積(m<^2>)"]);

            AddInlineMathParagraph(body, [Tex(@"A_{e} = \frac{\pi}{4} D^{2}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Z"), ": 杭頭接合部の断面係数(m<^3>)"]);

            AddInlineMathParagraph(body, [Tex(@"Z = \frac{\pi}{32} D^{3}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"D"), ": 杭径(m)ただし絞り部ありの場合は絞り部径を杭径とみなす。"]);

            AddHeader1(body, "降伏曲げモーメントおよび終局曲げモーメント", 4);

            AddInlineMathParagraph(body, ["降伏曲げモーメント", Tex(@"M_{y}"), "および終局曲げモーメント", Tex(@"M_{u}"),
                "については、以下の仮定に基づいて算定する。"]);

            AddInlineMathParagraph(body, ["① 断面力の釣合いとひずみ度の適合条件を考慮した塑性曲げ理論に基づいて算定する。"]);

            AddInlineMathParagraph(body, ["② 解析における断面形状は、杭径を直径とする円形断面とする。" +
                "ただし、絞り部がある場合は絞り部径を直径とする。"]);

            AddInlineMathParagraph(body, ["③ 引張定着筋降伏時の曲げモーメントは、最外縁の引張定着筋が降伏するときの値とする。"]);

            AddInlineMathParagraph(body, ["④ 圧縮降伏時の曲げモーメントは、圧縮縁コンクリートが0.85", Tex(@"\sigma_{max}"),
                "に達したときの値とする。"]);

            AddInlineMathParagraph(body, ["⑤ 降伏時の曲げモーメント", Tex(@"M_{y}"),
                "は、引張定着筋降伏時と圧縮降伏時の曲げモーメントのうち小さい方の値とする。" +
                "ただし、引張定着筋を配置しない場合の降伏時曲げモーメントは、上記④に達したときの値とする。"]);

            AddInlineMathParagraph(body, ["⑥ 終局時の曲げモーメント", Tex(@"M_{u}"),
                "は、最大曲げモーメントとする。"]);

            AddInlineMathParagraph(body, [ "⑦ コンクリートの応力度", Tex(@"\sigma"),
                "とひずみ度", Tex(@"\varepsilon"), "の関係は下式とする。" ]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon \le 0"), ": ", Tex(@"\sigma = 0")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition,
                [Tex(@"0 < \varepsilon \le \varepsilon_{B}"), ": ",
                Tex(@"\sigma = 6.75\left{ e^{-0.812\frac{\varepsilon}{\varepsilon_{0}}} - e^{-1.218\frac{\varepsilon}{\varepsilon_{0}}} \right}, \sigma_{max}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon_{B} < \varepsilon"), ": ", Tex(@"\sigma = \sigma_{max}")]);

            AddInlineMathParagraph(body, ["ここに、"]);

            AddInlineMathParagraph(body, [Tex(@"\varepsilon_{B}"), ": コンクリート最大強度時のひずみ度で", Tex(@"\varepsilon_{B}"), " = 0.003 とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{max}"), ": コンクリートの最大強度で", Tex(@"\sigma_{max} = \frac{F_{c}}{\nu^{2}}"), "とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"F_{c}"), ": コンクリートの設計基準強度で杭体、パイルキャップの設計基準強度のうち、最小の値とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu"), ": 絞り係数"]);


            AddInlineMathParagraph(body, ["⑧ 引張定着筋の応力度", Tex(@"\sigma"), "とひずみ度", Tex(@"\varepsilon"), "の関係は下式とする。"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon \le -\varepsilon_{y}"), ": ", Tex(@"\sigma = -\sigma_{y}")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"- \varepsilon_{y} < \varepsilon \le \varepsilon_{y}"), ": ", Tex(@"\sigma = E_{s}\varepsilon")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\varepsilon_{y} < \varepsilon"), ": ", Tex(@"\sigma = \sigma_{y}")]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathEpsilonyEq() =>
                Tex(@"\varepsilon_{y} = \frac{\sigma_{y}}{E_{s}}");

            AddInlineMathParagraph(body, ["ここに、", Tex(@"\sigma_{y}"), "：引張定着筋降伏点強度"]);
            AddInlineMathParagraph(body, [Tex(@"\varepsilon_{y}"), "：引張定着筋降伏時ひずみ度（", mathEpsilonyEq(), "）"]);

            AddHeader1(body, "降伏時回転角", 4);
            //AddInlineMathParagraph(body, ["d. 降伏時回転角", mathThetau()]);

            AddInlineMathParagraph(body, ["断面解析における曲率から降伏時回転角", Tex(@"\theta_{u}"), "への換算は、杭体のヒンジ領域を杭径部とし、ヒンジ領域では一定の曲率であると仮定した下式で評価する。"]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathPhiy() =>
                Tex(@"\phi_{y}");

            AddEq(body, @"\theta_{y} = \int_{0}^{D} {\phi_{y}dx} = \phi_{y}D");

            AddInlineMathParagraph(body, ["ここに"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [mathPhiy(), ": 前項c.で算定される", Tex(@"M_{y}"), "時曲率"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [ Tex(@"D"), ": 杭径 " +
                "ただし、杭頭接合部に絞り部がある場合には杭径を絞り部径とみなす。" ]);

            AddHeader1(body, "杭頭接合部の曲げ耐力の算定法", 3);

            AddInlineMathParagraph(body, [
                "短期許容曲げモーメント", Tex(@"M_{a}"), "は、",
                "降伏時曲げモーメント", Tex(@"M_{y}"), "算定に用いたモーメント-曲率関係において、",
                "以下の条件に達した時点の曲げモーメント①、②のうち、小さいほうの値とする。",
                "ただし、引張鉄筋を配置しない場合は、以下の②の条件に達したときの値とする。"
            ]);

            static DocumentFormat.OpenXml.Math.OfficeMath mathShortTermAllowableStressEq() =>
                Tex(@"\frac{2}{3}\cdot\frac{F_{c}}{\nu^{2}}");

            AddInlineMathParagraph(body, ["①最外縁の引張定着筋が降伏するとき"]);
            AddInlineMathParagraph(body, ["②圧縮縁コンクリートが短期許容圧縮応力度(",
                mathShortTermAllowableStressEq(), "(", Tex(@"\nu"), ": 絞り係数))に達するとき"]);
        }

        // 鉛直支持力の章
        private static void AddSectionVerticalResistance(Body body)
        {
            AddHeader1(body, "杭の支持力検討", 2);

            AddText(body,
                "杭の鉛直支持力は、「基礎指針'19」6.2節、引抜き抵抗力は「基礎指針'19」6.5節による。");

            AddText(body, "極限鉛直支持力", "left");
            AddEq(body, @"R_{u} = R_{p} + R_{f}");
            //AddEquation_Ru(body);

            AddText(body, "極限先端支持力", "left");
            //AddEquation_Rp(body);
            AddEq(body, @"R_{p} = q_{p} A");

            AddText(body, "極限周面支持力", "left");
            //AddEquation_Rf(body);
            AddEq(body, @"R_{f} = R_{fs} + R_{fc}");

            AddText(body, "砂質土部分の周面抵抗力", "left");
            //AddEquation_Rfs(body);
            AddEq(body, @"R_{fs} = \tau_{fs} L_{s} \psi");

            AddText(body, "粘性土部分の周面抵抗力", "left");
            //AddEquation_Rfc(body);
            AddEq(body, @"R_{fc} = \tau_{fc} L_{c} \psi");

            AddText(body, "使用限界支持力", "left");
            //AddEquation_Rd_SLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{3} R_{u}");

            AddText(body, "損傷限界支持力", "left");
            //AddEquation_Rd_DLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{1.5} R_{u}");

            AddText(body, "終局限界支持力", "left");
            //AddEquation_Rd_ULS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{u} = \frac{1}{1} R_{u}");



            AddText(body, "最大引抜き抵抗力", "left");
            //AddEquation_RTU(body);
            AddEq(body, @"R_{TU} = \left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "残留引抜き抵抗力", "left");
            //AddEquation_RTR(body);
            AddEq(body, @"R_{TR} = \frac{1}{1.2}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "降伏引抜き抵抗力", "left");
            //AddEquation_RTY(body);
            AddEq(body, @"R_{TY} = \frac{2}{3}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

            AddText(body, "砂質土の引抜き時の最大周面抵抗力", "left");
            //AddEquation_TauSti(body);
            AddEq(body, @"\tau_{sti} = \frac{2}{3}\tau_{si}");

            AddText(body, "粘性土の引抜き時の最大周面抵抗力", "left");
            //AddEquation_TauCti(body);
            AddEq(body, @"\tau_{cti} = \frac{4}{5}\tau_{ci}");

            AddText(body, "使用限界引抜力", "left");
            //AddEquation_Rdt_SLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TU} = \frac{1}{3} R_{TU} = \frac{1}{3}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + \frac{1}{3}W");

            AddText(body, "損傷限界引抜力", "left");
            //AddEquation_Rdt_DLS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TY} = \frac{1}{3} R_{TY} = \frac{2}{9}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + \frac{1}{3}W");

            AddText(body, "終局限界引抜力", "left");
            //AddEquation_Rdt_ULS(body);
            AddEq(body, @"R_{d} = \phi_{R} R_{TR} = R_{TR} = \frac{1}{1.2}\left(\tau_{sti} L_{si} + \tau_{cti} L_{ci}\right) + W");

        }

        // 沈下量の節
        private void AddSectionSettlement(Body body)
        {
            AddHeader1(body, "杭の沈下検討");


            AddInlineMathParagraph(body, ["杭の沈下は、「基礎指針'19」6.3節による。" +
                "単杭の沈下量は、同節の荷重伝達解析モデルにより求め、" +
                "群杭の沈下量は、等価荷重面を用いたスタインブレナーの近似解（「基礎指針'19」5.3節）を用いて求める。"]);

            AddText(body, "単杭の先端抵抗-沈下量関係");
            //AddEquation_PileSettlment(body);
            AddEq(body, @"\frac{S_{p}/d_{p}}{0.1} = \alpha \frac{R_{p}/A_{p}}{(R_{p}/A_{p})_{u}} + (1-\alpha)\left(\frac{R_{p}/A_{p}}{(R_{p}/A_{p})_{u}}\right)^{n}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\S_{p}"), ": 杭先端沈下量(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\d_{p}"), ": 杭先端直径(m)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\R_{p}"), ": 杭先端荷重(kN)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\A_{p}"), ": 杭先端断面積(m<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\left(\frac{R_{p}}{A_{p}}\right)_{u}"), ": 極限先端支持力(kN/m<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 曲線の初期接線勾配"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"n"), ": 曲線形状を決定する次数"]);

            AddText(body, "単杭の周面抵抗力度-沈下量関係");

            AddTrilinearCircumResistanceTable(body);

            AddText(body, "スタインブレナーの解による成層多層地盤の即時沈下量");
            //AddEquation_SettlementDeltaSE(body);
            AddEq(body, @"\Delta S_{E} = S_{E} - S_{E}' = q \frac{B}{E_{S}} I_{S}");
            //AddEquation_SettlementDeltaIS(body);
            AddEq(body, @"I_{S} = \left(1-\nu_{s}^{2}\right)F_{1}+\left(1-\nu_{s}-2\nu_{s}^{2}\right)F_{2}");

            //AddEquation_SettlementF1(body);
            AddEq(body, @"
            F_{1} = \frac{1}{\pi}\left[
                l\log_{e}\frac
                {\left(1+\sqrt{l^{2}+1}\right)\sqrt{l^{2}+d^{2}}}
                {l\left(1+\sqrt{l^{2}+d^{2}+1}\right)}
                + \log_{e}\frac
                {\left(1+\sqrt{l^{2}+1}\right)\sqrt{l^{2}+d^{2}}}
                {l+\\sqrt{l^{2}+d^{2}+1}}
            \right]");
            //AddEquation_SettlementF2(body);
            AddEq(body, @"F_{2} = \frac{d}{2\pi}\tan^{-1}\left(\frac{l}{d+\sqrt{l^{2}+d^{2}+1}}\right)");
            //AddEquation_SettlementSE(body);
            AddEq(body, @"S_{E} = \left[\frac{I_{s}(H_{1},\nu_{s1})}{E_{s1}} + \sum_{k=2}^{n} {\frac{I_{s}(H_{k},\nu_{sk}) - I_{s}(H_{k-1},\nu_{sk})}{E_{sk}}} \right] qB");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\S_{E}"), ": 隅角部の即時沈下量"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\I_{s}\left(H_{k},\nu_{sk}\right)"), ": 層厚", Tex(@"\H_{k}"), "ポアソン比", Tex(@"\nu_{sk}"), "の地盤における沈下係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\E_{s}"), ": 地盤の変形係数（kN/m<^2>）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\nu_{sk}"), ": ポアソン比"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"q"), ": 基礎に作用する荷重度（kN/m<^2>）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"B"), ": 基礎の短辺長さ（m）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L"), ": 基礎の長辺長さ（m）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"l"), ": 基礎の長辺長さ／短辺長さ"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\H_{k}"), ": 地表面から", Tex(@"k"), "層下端までの距離"]);
        }

        // 水平抵抗の節
        private void AddSectionHorizontalResistance(Body body)
        {
            AddHeader1(body, "杭の水平抵抗");

            AddInlineMathParagraph(body, ["杭の水平抵抗は、「基礎指針'19」6.6節による。"]);

            AddInlineMathParagraph(body, ["群杭フレームモデル"]);

            // Word の正式な番号付きリストとして追加
            //AddList(body, new[]
            //{
            //    "慣性力の作用点、各杭頭は剛体として連結する。根入部がある場合は、根入部も剛体として連結する。",
            //    "地盤条件および杭径と群杭硬化を考慮して各杭、各深度の水平地盤反力係数を設定し、杭径と支配区間長を乗じて水平地盤ばねを求める。液状化を生じる場合は水平地盤反力係数を低減する。",
            //    "基礎根入れ部がある場合は、土圧合力ばねを設定する。",
            //    "上部、基礎の慣性力を設定する。",
            //    "転倒モーメントは、各杭に軸力として与えて入力する。",
            //    "各杭位置における杭頭軸力から杭の変形性能を設定する。",
            //    "地震時地盤変位を設定する。",
            //    "水平地盤反力の非線形性および杭体の非線形性を考慮して、杭頭水平力および地震時地盤変位を同時に作用させ、杭応力を評価する。"
            //}, 2);

            AddInlineMathParagraph(body, ["① 慣性力の作用点、各杭頭は剛体として連結する。" +
                "根入部がある場合は、根入部も剛体として連結する。"]);
            AddInlineMathParagraph(body, ["② 地盤条件および杭径と群杭硬化を考慮して各杭、" +
                "各深度の水平地盤反力係数を設定し、" +
                "杭径と支配区間長を乗じて水平地盤ばねを求める。" +
                "液状化を生じる場合は水平地盤反力係数を低減する。"]);
            AddInlineMathParagraph(body, ["③ 基礎根入れ部がある場合は、土圧合力ばねを設定する。"]);
            AddInlineMathParagraph(body, ["④ 上部、基礎の慣性力を設定する。"]);
            AddInlineMathParagraph(body, ["⑤ 転倒モーメントは、各杭に軸力として与えて入力する。"]);
            AddInlineMathParagraph(body, ["⑥ 各杭位置における杭頭軸力から杭の変形性能を設定する。"]);
            AddInlineMathParagraph(body, ["⑦ 地震時地盤変位を設定する。"]);
            AddInlineMathParagraph(body, ["⑧ 水平地盤反力の非線形性および杭体の非線形性を考慮して、杭頭水平力および" +
                "地震時地盤変位を同時に作用させ、杭応力を評価する。"]);

            AddText(body, "水平地盤反力係数", "left");
            AddEquationP(body);

            AddText(body, "0m≦$y$≦0.001m", "left");

            AddEquationkh_1(body);

            AddText(body, "0.001m≦$y$", "left");
            AddEquationkh_2(body);

            AddText(body, "基準水平地盤反力係数", "left");
            AddEquation_kh0(body);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKh() =>
            //    Tex(@"\k_{h}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKh0() =>
            //    Tex(@"\k_{h0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlpha() =>
            //    Tex(@"\alpha");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathB0() =>
            //    Tex(@"\B_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathE0() =>
            //    Tex(@"\E_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathGsi() =>
            //    Tex(@"\xi");
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{h}"), ": 水平地盤反力係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{h0}"), ": 基準水平地盤反力係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 80m<^-1>"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\B_{0}"), ": 0.01m"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\E_{0}"), ": 基準水平地盤反力係数を評価するための地盤の変形係数(kN/m<^2>)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\xi"), ": 基準水平地盤反力係数に群杭の影響を考慮する係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPySand() =>
            //    Tex(@"\p_{y} = \kappa K_{p}\sigma_{z}'");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKappaFront() =>
            //    Tex(@"\kappa = 3");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKappaRear() =>
            //    Tex(@"\kappa = \min\left(0.55 - 0.007\phi, \frac{R}{B} - 1.0, 0.4\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathMu1() =>
            //    Tex(@"\mu = 1.4");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathMu2() =>
            //    Tex(@"\mu = 0.6\left(\dfrac{R}{B}\right) - 0.4");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathLambda1() =>
            //    Tex(@"\lambda = 9.0");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathLambda2() =>
            //    Tex(@"\lambda = 3.0\left(\dfrac{R}{B}\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRonBRange1() =>
            //    Tex(@"\frac{R}{B} \ge 3.0");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRonBRange2() =>
            //    Tex(@"\frac{R}{B} < 3.0");

            AddText(body, "砂質土の塑性水平地盤反力係数", "left");

            AddInlineMathParagraph(body, [Tex(@"\p_{y} = \kappa K_{p}\sigma_{z}'")]);

            AddText(body, "受働土圧係数", "left");
            AddEquation_Kp(body);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["単杭および前方杭の場合:", Tex(@"\kappa = 3")]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["後方杭の場合:", Tex(@"\kappa = \min\left(0.55 - 0.007\phi, \frac{R}{B} - 1.0, 0.4\right)")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, ["単杭および前方杭の場合:", Tex(@"\mu = 1.4"), ",", Tex(@"\lambda = 9.0")]);
            AddInlineMathParagraph(body, ["後方杭で", Tex(@"\frac{R}{B} \ge 3.0"), "の場合　:",
                Tex(@"\mu = 1.4"), ",", Tex(@"\lambda = 9.0")]);
            AddInlineMathParagraph(body, ["後方杭で", Tex(@"\frac{R}{B} < 3.0"), "の場合　:",
                Tex(@"\mu = 0.6\left(\dfrac{R}{B}\right) - 0.4"), ",", Tex(@"\lambda = 3.0\left(\dfrac{R}{B}\right)")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClayRange1() =>
            //    Tex(@"\frac{z}{B} \le 2.5");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClayRange2() =>
            //    Tex(@"\frac{z}{B} > 2.5");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClay1() =>
            //    Tex(@"\p_{y} = 2 \left[ 1 + \mu \frac{z}{B} \right] c_u");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPyClay2() =>
            //    Tex(@"\p_{y} = \lambda c_u");

            AddText(body, "粘性土の塑性水平地盤反力係数", "left");
            //AddEquation_Py(body);
            AddInlineMathParagraph(body, [Tex(@"\frac{z}{B} \le 2.5"), "の場合:　", Tex(@"\p_{y} = 2 \left[ 1 + \mu \frac{z}{B} \right] c_u")]);
            AddInlineMathParagraph(body, [Tex(@"\frac{z}{B} > 2.5"), "の場合:　", Tex(@"\p_{y} = \lambda c_u")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathYRange1() =>
            //    Tex(@"y < \Delta_{p}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathYRange2() =>
            //    Tex(@"y \ge \Delta_{p}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPEt1() =>
            //    Tex(@"\P_{Et} = \frac{(P_{p}-P_{0}) \left(\Delta_{p}-y_{sp}\right) y}{2\Delta_{p}y_{sp} + (\Delta_{p}-3)y_{sp} + \left|y\right|}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPEt2() =>
            //    Tex(@"\P_{Et} = P_{p} - P_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathP0() =>
            //    Tex(@"\P_{0} = \tfrac{1}{2}\gamma B z^{2} K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPp() =>
            //    Tex(@"\P_{p} = 2czB\sqrt{K_{p}} + \tfrac{1}{2}\gamma B z^{2} K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKp() =>
            //    Tex(@"\K_{p} = \tan^{2}\left(45^{\circ} + \frac{\phi}{2}\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathP0Vary() =>
            //    Tex(@"\P_{0} = \tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathPpVary() =>
            //    Tex(@"\P_{p} = 2c(z_{b}-z_{a})B\sqrt{K_{p}}+\tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{p}");
            AddText(body, "土圧合力ばね", "left");

            AddInlineMathParagraph(body, [Tex(@"y < \Delta_{p}"), "の場合:　 ", Tex(@"\P_{Et} = \frac{(P_{p}-P_{0}) \left(\Delta_{p}-y_{sp}\right) y}{2\Delta_{p}y_{sp} + (\Delta_{p}-3)y_{sp} + \left|y\right|}")]);
            AddInlineMathParagraph(body, [Tex(@"y \ge \Delta_{p}"), "の場合:　 ", Tex(@"\P_{Et} = P_{p} - P_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{0} = \tfrac{1}{2}\gamma B z^{2} K_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{p} = 2czB\sqrt{K_{p}} + \tfrac{1}{2}\gamma B z^{2} K_{0}")]);

            AddInlineMathParagraph(body, [Tex(@"\P_{0} = \tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{0}")]);
            AddInlineMathParagraph(body, [Tex(@"\P_{p} = 2c(z_{b}-z_{a})B\sqrt{K_{p}}+\tfrac{1}{2}\gamma B \left(z_{b}^{2} - z_{a}^{2}\right) K_{p}")]);

            AddInlineMathParagraph(body, [Tex(@"\K_{p} = \tan^{2}\left(45^{\circ} + \frac{\phi}{2}\right)")]);
        }

        // 部材の性能
        private static void AddSectionMemberCapacities(Body body)
        {
            AddHeader1(body, "基礎部材の強度と変形性能");

            AddText(body, "コンクリートのヤング係数");
            //AddEquation_ConcreteEc(body);
            AddEq(body, @"E_{c} = 3.35\times 10^{4} \left(\frac{\gamma}{24}\right)^{2} \left(\frac{\zeta\cdot F_{c}}{60}\right)^{\frac{1}{3}}");

            AddText(body, "場所打ち鉄筋コンクリート杭の使用限界曲げモーメントMs");
            //AddEquation_InsituReinforcedPileMs(body);
            AddEq(body, @"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3}\right)");

            AddText(body, "場所打ち鉄筋コンクリート杭の損傷限界曲げモーメントMd");
            //AddEquation_InsituReinforcedPileMd(body);
            AddEq(body, @"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2},M_{d3}\right)");

            AddText(body, "場所打ち鉄筋コンクリート杭の安全限界曲げモーメントMu");
            //AddEquation_InsituReinforcedPileMu(body);
            AddEq(body, @"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");

            AddText(body, "場所打ち鉄筋コンクリート杭の使用限界せん断力Qs");
            //AddEquation_InsituReinforcedPileQs(body);
            AddEq(body, @"
                Q_{s} = \beta_{1}\cdot \frac{2}{3}\cdot
                \frac{0.065k_{c}\left(49.0+\xi F_{c}\right)}
                {\dfrac{M}{Qd}+1.7}
                \left(1+\frac{\sigma_{o}}{14.7}bj\right)
                ");

            AddText(body, "場所打ち鉄筋コンクリート杭の損傷限界せん断力Qd");
            //AddEquation_InsituReinforcedPileQd(body);
            AddEq(body, @"
                Q_{d} = \beta_{1}\cdot
                \frac{0.065k_{c}\left(49.0+\xi F_{c}\right)}
                {\dfrac{M}{Qd}+1.7}
                \left(1+\frac{\sigma_{o}}{14.7}bj\right)
                ");

            AddText(body, "場所打ち鉄筋コンクリート杭の安全限界せん断力Qu");
            //AddEquation_InsituReinforcedPileQu(body);
            AddEq(body, @"
                Q_{u} = \beta_{1}\cdot \beta_{2}\cdot
                \left{
                \frac{0.053p_{t}^{0.23}\left(18+\xi F_{c}\right)}
                {\dfrac{M}{Qd}+0.12}
                +0.85+\sqrt{p_{w}\cdot\sigma_{wy}}+0.1\sigma_{o}\right}
                bj
                ");

            AddText(body, "場所打ち鋼管コンクリート杭の使用限界曲げモーメントMs");
            //AddEquation_InsituSteelReinforcedPileMs(body);
            AddEq(body, @"M_{s} = \beta_{1}\cdot \min\left(M_{s1},M_{s2},M_{s3},M_{s4},M_{s5}\right)");

            AddText(body, "場所打ち鋼管コンクリート杭の損傷限界曲げモーメントMd");
            //AddEquation_InsituSteelReinforcedPileMd(body);
            AddEq(body, @"M_{d} = \beta_{1}\cdot \min\left(M_{d1},M_{d2},M_{d3},M_{d4},M_{d5}\right)");


            AddText(body, "場所打ち鋼管コンクリート杭の安全限界曲げモーメントMu");
            //AddEquation_InsituSteelReinforcedPileMu(body);
            AddEq(body, @"M_{u} = \beta_{1}\cdot \beta_{2}\cdot M_{u0}");


            AddText(body, "場所打ち鋼管コンクリート杭の使用限界せん断力Qs");
            //AddEquation_InsituSteelReinforcedPileQs(body);
            AddEq(body, @"Q_{s} = \beta_{1}\frac{A_{s}}{\kappa}f_{s,ss}");

            AddText(body, "場所打ち鋼管コンクリート杭の損傷限界せん断力Qd");
            //AddEquation_InsituSteelReinforcedPileQd(body);
            AddEq(body, @"Q_{d} = \beta_{1}\frac{A_{s}}{\kappa}f_{s,sd}");

            AddText(body, "場所打ち鋼管コンクリート杭の安全限界せん断力Qu");
            //AddEquation_InsituSteelReinforcedPileQu(body);
            AddEq(body, @"
                Q_{u} = \beta_{1}\beta_{2}
                \frac{2}{3}\pi t_{s}(D - t_{s})
                \frac{2}{3}\frac{f_{cy}}{\sqrt{3}}
                \sqrt{1 - p^{2}}
            ");

            AddText(body, "e関数法");
            //AddEquation_EFunction(body);
            AddEq(body, @"
                \frac{\sigma}{\xi\cdot F_{c}} = 6.75 \left{
                  e^{-0.812\left(\frac{\varepsilon}{\varepsilon_{m}}\right)}
                  - e^{-1.218\left(\frac{\varepsilon}{\varepsilon_{m}}\right)}
                \right}
            ");
        }

        // 荷重条件の表を追加するメソッド
        private static void AddTrilinearCircumResistanceTable(Body body)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTau1() =>
            //    GetCombinedRunToMath([
            //        GetSubscript(GetRun("τ"), GetRun("1")),
            //        ]);

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["地盤種別"], fontSize, "center"),
            CreateTableCell(["τ<_1>", "(kN/m<^2>)"], fontSize, "center"),
            CreateTableCell(["S<_1>", "(mm)"], fontSize, "center"),
            CreateTableCell(["τ<_2>", "(kN/m<^2>)"], fontSize, "center"),
            CreateTableCell(["S<_2>", "(mm)"], fontSize, "center")
            );
            table.Append(headerRow);

            // データ行を追加
            TableRow dataRowSand = new();
            dataRowSand.Append(CreateTableCell(["砂質土"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["0.8τ<_max>"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["5"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowSand.Append(CreateTableCell(["20"], fontSize, "right"));
            table.Append(dataRowSand);

            TableRow dataRowGravel = new();
            dataRowGravel.Append(CreateTableCell(["礫質土"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["0.7τ<_max>"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["10"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowGravel.Append(CreateTableCell(["30"], fontSize, "right"));
            table.Append(dataRowGravel);

            TableRow dataRowClay = new();
            dataRowClay.Append(CreateTableCell(["粘性土"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["0.8τ<_max>"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["3"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["τ<_max>"], fontSize, "right"));
            dataRowClay.Append(CreateTableCell(["10"], fontSize, "right"));
            table.Append(dataRowClay);

            body.Append(table);
        }

        // 地盤変位の節
        private void AddGroundDisplacementSection(Body body)
        {
            AddHeader1(body, "地盤の水平変位");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmaxA2Eq() =>
            //    Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmax() =>
            //    Tex(@"\D_{max}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDmaxA1Eq() =>
            //    Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}} \left{ C_{2}\left(1 - \frac{1}{\alpha^{2}}\right) + \frac{2R_{z0}}{\alpha}\right}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathC1() =>
            //    Tex(@"\C_{1}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathC2() =>
            //    Tex(@"\C_{2}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlpha() =>
            //    Tex(@"\alpha");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFa() =>
            //    Tex(@"\f_{A}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRz0() =>
            //    Tex(@"\R_{Z0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathL() =>
            //    Tex(@"L");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathZ() =>
            //    Tex(@"Z");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCAlpha() =>
            //    Tex(@"\C_{\alpha}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathT0() =>
            //    Tex(@"\T_{0}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathT0Eq() =>
            //    Tex(@"\T_{0} = 4 \sum \frac{H_{i}}{V_{S0i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlphaEq() =>
            //    Tex(@"\alpha = 1 + \frac{L Z C_{\alpha} T_{0}}{\sum {H_{i}}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFaEq() =>
            //    Tex(@"\f_{A} = \min\left(1.6\alpha T_{0},1\right)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRz0Eq() =>
            //    Tex(@"\R_{Z0} = \frac{\sum {\gamma_{i}V_{S0i}H_{i}}}{\gamma_{B}V_{SB}\sum {H_{i}}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUEq() =>
            //    Tex(@"\u_{i+1} = u_{i} - \frac{40}{k_{i}\left(\alpha T_{0}\right)^{2}}\sum_{j=1}^{i} {m_{j}u_{j}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKi() =>
            //    Tex(@"\k_{i}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathKiEq() =>
            //    Tex(@"\k_{i} = \frac{\gamma_{i}}{g}\cdot\frac{V_{SEi}^{2}}{H_{i}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathVsei() =>
            //    Tex(@"\V_{SEi}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathVseiEq() =>
            //    Tex(@"\V_{SEi} = \left(\frac{\gamma_{i}V_{S0i}}{\gamma_{B}V_{SB}}\right)^{\beta}V_{S0i}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathBeta() =>
            //    Tex(@"\beta");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathBetaEq() =>
            //    Tex(@"\beta = \frac{3}{4}\left(1 - \frac{1}{2^{\alpha-1}}\right)\frac{1}{1 - R_{Z0}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUStar() =>
            //    Tex(@"\u_{i}^{*}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathUStarEq() =>
            //    Tex(@"\u_{i}^{*} = \frac{u_{i} - u_{N+1}}{1 - u_{N+1}}");
            AddInlineMathParagraph(body, ["a) 地表変位", Tex(@"\D_{max}"), "の算定"]);

            AddInlineMathParagraph(body, [Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}} \left{ C_{2}\left(1 - \frac{1}{\alpha^{2}}\right) + \frac{2R_{z0}}{\alpha}\right}")]);
            AddInlineMathParagraph(body, [Tex(@"\D_{max} = C_{1}\left(\alpha^{2}-1\right)f_{A}\sum {H_{i}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{1}"), ": 表層の土質のG-γ関係から決まる定数（粘性土で0.0028、砂質土で0.0015）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{2}"), ": 表層の土質の減衰特性から決まる定数（粘性土で0.53、砂質土で0.66）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha"), ": 地盤の地震時の固有周期ののび"]);
            AddInlineMathParagraph(body, [Tex(@"\alpha = 1 + \frac{L Z C_{\alpha} T_{0}}{\sum {H_{i}}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\f_{A}"), ": 地震荷重の加速度一定領域の影響を考慮する補正係数"]);
            AddInlineMathParagraph(body, [Tex(@"\f_{A} = \min\left(1.6\alpha T_{0},1\right)")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\R_{Z0}"), ": 地盤の表層と工学的基盤の初期インピーダンス比"]);
            AddInlineMathParagraph(body, [Tex(@"\R_{Z0} = \frac{\sum {\gamma_{i}V_{S0i}H_{i}}}{\gamma_{B}V_{SB}\sum {H_{i}}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"L"), ": 地震荷重レベルにより決まる定数（レベル１では0.2、レベル２で1.0）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"Z"), ": 地域係数"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{\alpha}"), ": 表層の土質の動的変形特性から決まる定数（粘性土で25、砂質土で40）"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\T_{0}"), ": 地盤の初期固有周期"]);

            AddInlineMathParagraph(body, ["b) 地盤の水平変位の深さ方向分布の算定"]);

            //AddInlineMathParagraph(body, mathUi());
            AddInlineMathParagraph(body, [Tex(@"\u_{i+1} = u_{i} - \frac{40}{k_{i}\left(\alpha T_{0}\right)^{2}}\sum_{j=1}^{i} {m_{j}u_{j}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\k_{i}"), ": 地表から第i番目の等価せん断ばね剛性"]);

            AddInlineMathParagraph(body, [Tex(@"\k_{i} = \frac{\gamma_{i}}{g}\cdot\frac{V_{SEi}^{2}}{H_{i}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\V_{SEi}"), ": 地震時の等価S波速度"]);

            AddInlineMathParagraph(body, [Tex(@"\V_{SEi} = \left(\frac{\gamma_{i}V_{S0i}}{\gamma_{B}V_{SB}}\right)^{\beta}V_{S0i}")]);

            AddInlineMathParagraph(body, [Tex(@"\beta = \frac{3}{4}\left(1 - \frac{1}{2^{\alpha-1}}\right)\frac{1}{1 - R_{Z0}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\u_{i}^{*}"), ": 地盤の水平変位の深さ方向分布"]);

            AddInlineMathParagraph(body, [Tex(@"\u_{i}^{*} = \frac{u_{i} - u_{N+1}}{1 - u_{N+1}}")]);
        }

        // 液状化の節
        private void AddLiquefactionSection(Body body)
        {
            AddHeader1(body, "液状化の検討");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTauDonSigmaZPrimeEq() =>
            //    Tex(@"\frac{\tau_{d}}{\sigma_{z}'} = r_{n}\frac{\alpha_{max}}{g}\frac{\sigma_{z}}{\sigma_{z}'}r_{d}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRnEq() =>
            //    Tex(@"\r_{n} = 0.1(M-1)");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRdEq() =>
            //    Tex(@"\r_{d} = 1 - 0.015z");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathNaEq() =>
            //    Tex(@"\N_{a} = N_{1} + \Delta N_{f}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN1Eq() =>
            //    Tex(@"\N_{1} = C_{N}N");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCnEq() =>
            //    Tex(@"\C_{N} = \sqrt{\frac{100}{\sigma_{z}'}}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDcyEq() =>
            //    Tex(@"\sum {\frac{\gamma_{cyi}H_{i}}{100}}");

            AddInlineMathParagraph(body, ["① 検討地点の地盤内の各深さに発生する等価なせん断応力比"]);

            AddInlineMathParagraph(body, [Tex(@"\frac{\tau_{d}}{\sigma_{z}'} = r_{n}\frac{\alpha_{max}}{g}\frac{\sigma_{z}}{\sigma_{z}'}r_{d}")]);

            AddText(body, "液状化時の土のせん断応力度比");

            AddInlineMathParagraph(body, [Tex(@"\r_{n} = 0.1(M-1)")]);

            AddInlineMathParagraph(body, [Tex(@"\r_{d} = 1 - 0.015z")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathTauD() =>
            //    Tex(@"\tau_{d}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\tau_{d}"), ": 水平面に生じる等価な一定繰返しせん断応力振幅"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathSigmaZPrime() =>
            //    Tex(@"\sigma_{z}'");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{z}'"), ": 検討深さにおける有効土被り圧（鉛直有効応力）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRn() =>
            //    Tex(@"\r_{n}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\r_{n}"), ": 等価な繰返し回数に関する補正係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathM() =>
            //    Tex(@"M");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"M"), ": 地震のマグニチュードで通常は7.5"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathAlphaMax() =>
            //    Tex(@"\alpha_{max}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\alpha_{max}"), ": 地表面における設計用水平加速度（m/s^2）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathG() =>
            //    Tex(@"g");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"g"), ": 重力加速度（9.8m/s^2）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathSigmaZ() =>
            //    Tex(@"\sigma_{z}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\sigma_{z}"), ": 検討深さにおける全土被り圧（鉛直全応力）"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathRd() =>
            //    Tex(@"\r_{d}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\r_{d}"), ": 地盤が剛体でないことによる低減係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathZ() =>
            //    Tex(@"z");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"z"), ": 地表面からの検討深さ(m)"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN1() =>
            //    Tex(@"\N_{1}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathCn() =>
            //    Tex(@"\C_{N}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathDeltaNf() =>
            //    Tex(@"\Delta N_{f}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathN() =>
            //    Tex(@"N");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFc() =>
            //    Tex(@"F_{c}");
            AddInlineMathParagraph(body, ["② 対応する深度の補正", Tex(@"N"), "値"]);

            AddInlineMathParagraph(body, [Tex(@"\N_{1} = C_{N}N")]);

            AddInlineMathParagraph(body, [Tex(@"\N_{a} = N_{1} + \Delta N_{f}")]);

            AddInlineMathParagraph(body, [Tex(@"\C_{N} = \sqrt{\frac{100}{\sigma_{z}'}}")]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\N_{1}"), ": 換算N値"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\Delta N_{f}"), ": 細粒分含有率", Tex(@"F_{c}"), "に応じた補正", Tex(@"N"), "N値成分"]);

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\C_{N}"), ": 拘束圧に関する換算係数"]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathREq() =>
            //    Tex(@"\R = \frac{\tau_{L}}{\sigma_{z}'}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathR() =>
            //    Tex(@"R");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFlEq() =>
            //    Tex(@"\F_{L} = \frac{\dfrac{\tau_{L}}{\sigma_{z}'}}{\dfrac{\tau_{d}}{\sigma_{z}'}}");

            AddInlineMathParagraph(body, ["③ 補正", Tex(@"\C_{N}"), "値に対応する飽和土層の液状化抵抗比", Tex(@"R")]);

            AddInlineMathParagraph(body, [Tex(@"\R = \frac{\tau_{L}}{\sigma_{z}'}")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathFl() =>
            //    Tex(@"\F_{L}");

            AddInlineMathParagraph(body, ["④ 各深さにおける液状化発生に対する安全率", Tex(@"\F_{L}")]);

            AddInlineMathParagraph(body, [Tex(@"\F_{L} = \frac{\dfrac{\tau_{L}}{\sigma_{z}'}}{\dfrac{\tau_{d}}{\sigma_{z}'}}")]);

            AddInlineMathParagraph(body, ["液状化の程度と地盤変位の予測"]);

            AddInlineMathParagraph(body, [Tex(@"\sum {\frac{\gamma_{cyi}H_{i}}{100}}")]);

            //static DocumentFormat.OpenXml.Math.OfficeMath mathGammaCyi() =>
            //    Tex(@"\gamma_{cyi}");

            //static DocumentFormat.OpenXml.Math.OfficeMath mathHi() =>
            //    Tex(@"\H_{i}");

            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\gamma_{cyi}"), ": i層の繰返しせん断ひずみ(%)"]);
            AddSymbolDescriptionWithTab(body, symbolDescTabPosition, [Tex(@"\H_{i}"), ": i層の層厚(m)"]);
        }

        string a001 = "基礎部材の強度と変形性能";
        string a002 = "液状化危険度、地盤変形量と液状化程度の予測";
        string a003 = "沈下";

        string a0031 = "単杭の沈下：荷重伝達解析による荷重-沈下量関係の評価（「基礎指針'19」 6.3節、1(2)）を行う。";
        string a0032 = "群杭の沈下:杭ごとに等価荷重面を設定し、杭先端以深の地盤の圧縮量（沈下量）を直接基礎と" +
            "同じくスタインブレナーの近似解（多層地盤の場合（「基礎指針'19」 5.3節、1(3)(iii)））を用いて求める。";
        string a004 = "鉛直支持力および引抜き抵抗力：";
        string a005 = "水平抵抗：「基礎指針'19」6.6節による。";


        string b001 = "場所打ちコンクリート杭の曲げモーメントと曲率の関係";

        string b002 = "断面の平面保持を仮定して、鉄筋とコンクリートの応力度-ひずみ度関係をモデル化し、断面の曲げ解析を行って、M-φ関係を計算する。" +
            "鉄筋の応力度-ひずみ度関係は、規格降伏店を用いたバイリニアとする。コンクリートの応力度-ひずみ度関係にはe関数法を用いる";
        string b003 = "a.曲げひび割れモーメントおよび曲げひび割れ時の曲率##は以下による。";
        string b004 = "b.杭の主筋降伏発生時の曲げモーメント##とその時の曲率##は、断面の曲げ解析による。" +
            "ただし、最外縁の杭主筋が引張降伏するとき（杭の主筋降伏発生時）の曲げモーメントと曲率とする。";
        string b005 = "c.安全限界曲げモーメント時の曲率";



    }
}

