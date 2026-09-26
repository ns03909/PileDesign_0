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
    // 結果サマリーテーブル群: 杭明細・杭頭反力・荷重組合せ・水平反力サマリー・水平評価レポート。物理分割 partial (純粋移動)。
    internal partial class WordDocument
    {
        // 杭明細を追加
        private void AddPileDescription(MainDocumentPart mainDocumentPart, Body body)
        {
            // 見出しなしで文書最末尾に表だけが出現していたため H1 を付与
            AddPageBreak(body);
            AddHeader1(body, "杭体明細", 1);

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
                        if (seg == null) continue;
                        var section = seg.PileSection;

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
            AddIntroText(body,
                "杭体・杭区間ごとに、選択した全杭・全荷重ケース・全荷重組合せ（液状化の有無を含む）にわたる" +
                "応答の最大値をレベル別にまとめる。Dmax は最大水平変位、Qmax・Mmax はせん断力・曲げモーメントの" +
                "絶対値最大、N_Max・N_Min は軸力の最大値・最小値（圧縮を正）である。");

            var rows = CollectPileForceSummary();

            // レベルごとに解析結果が存在するか判定（実際にデータが収集されたかで判定）
            bool hasLevel1Results = (inputModel.LoadCasesInput?.LoadCasesLevel1?.Any(x => x.IsApplicable) ?? false)
                && rows.Any(r => r.Qmax[0] != null || r.Mmax[0] != null);
            bool hasLevel2Results = (inputModel.LoadCasesInput?.LoadCasesLevel2?.Any(x => x.IsApplicable) ?? false)
                && rows.Any(r => r.Qmax[1] != null || r.Mmax[1] != null);

            BuildAnalysisResultSummaryTable(body, rows, hasLevel1Results, hasLevel2Results);
        }

        /// <summary>
        /// 杭検討結果まとめ一覧の 1 行 (杭体の区間 1 つ)。値はレベル1・2 の順。
        /// その区間・レベルの結果が 1 つも無ければ null (表には「—」と出す)。
        ///
        /// 以前は double.MinValue / MaxValue で初期化した値をそのまま書き出していたので、
        /// 一部の区間だけ結果が欠けると「-1.8E+308」のような値が表に出た。
        /// </summary>
        internal sealed class PileForceSummaryRow
        {
            public string PileBodyRef { get; init; } = "";
            public int SegmentNo { get; init; }
            public double Top { get; init; }
            public double Bottom { get; init; }
            public double?[] Dmax { get; } = new double?[2];
            public double?[] Qmax { get; } = new double?[2];
            public double?[] Mmax { get; } = new double?[2];
            public double?[] NMax { get; } = new double?[2];
            public double?[] NMin { get; } = new double?[2];

            /// <summary>いずれかの列に結果の無いセルがあるか (表に注記するため)。</summary>
            internal bool HasMissing(int level)
                => Dmax[level] == null || Qmax[level] == null || Mmax[level] == null || NMax[level] == null || NMin[level] == null;
        }

        /// <summary>
        /// 杭体の区間ごとに、全杭・全荷重ケース・全組合せ・選択した液状化の有無にわたる最大値を集める。
        ///
        /// 区間は要素分割で複数の要素 (梁) に分かれる。<b>梁の区間番号</b> (<c>Beam.SegmentIndex</c> → 分割後の区間の元の区間番号)
        /// で集めるので、1 区間が何要素に分かれていても、その区間のすべての要素が最大値に入る。
        /// 節点の変位が取れない要素は変位の最大値に入れない (以前は 0 として入れていた)。
        /// </summary>
        internal List<PileForceSummaryRow> CollectPileForceSummary()
        {
            var rows = new List<PileForceSummaryRow>();
            var liqPatterns = new List<bool>();
            if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
            if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionNo) liqPatterns.Add(false);
            var soilPiles = inputModel.ElementDivision?.SoilPiles;

            for (int pileBodyNo = 1; pileBodyNo <= inputModel.PileBodies.Count; pileBodyNo++)
            {
                var pileBody = inputModel.PileBodies[pileBodyNo - 1];
                if (pileBody?.PileBodySegments == null) continue;

                // この杭体の区間ごとの行 (区間番号 → 行)
                var rowBySegment = new Dictionary<int, PileForceSummaryRow>();
                for (int segNo = 1; segNo <= pileBody.PileBodySegments.Count; segNo++)
                {
                    var segment = pileBody.PileBodySegments[segNo - 1];
                    var row = new PileForceSummaryRow
                    {
                        PileBodyRef = pileBody.PileBodyRef,
                        SegmentNo = segNo,
                        Bottom = segment.SegmentDepth,
                        Top = segment.SegmentDepth - segment.SegmentLength,
                    };
                    rows.Add(row);
                    rowBySegment[segNo] = row;
                }

                foreach (PileLayoutDataItem pile in inputModel.PileLayoutItems)
                {
                    if (pile == null || pile.PileBodyNo != pileBodyNo) continue;

                    // 軸力 (入力値) は区間によらない
                    foreach (var row in rowBySegment.Values)
                    {
                        if (pile.AxialForceLevel1s is { Count: > 0 } n1)
                        {
                            row.NMax[0] = Max(row.NMax[0], n1.Max());
                            row.NMin[0] = Min(row.NMin[0], n1.Min());
                        }
                        if (pile.AxialForceLevel2s is { Count: > 0 } n2)
                        {
                            row.NMax[1] = Max(row.NMax[1], n2.Max());
                            row.NMin[1] = Min(row.NMin[1], n2.Min());
                        }
                    }

                    int soilIndex = pile.SoilPileAltNo - 1;
                    if (soilPiles == null || soilIndex < 0 || soilIndex >= soilPiles.Count) continue;
                    var divided = soilPiles[soilIndex]?.PileBodySegments;
                    if (divided == null || pile.Beams == null) continue;

                    for (int b = 0; b < pile.Beams.Count; b++)
                    {
                        var beam = pile.Beams[b];
                        if (beam == null) continue;
                        // 梁の要素番号 → 分割後の区間 → 元の区間番号
                        int element = beam.SegmentIndex ?? b;
                        if (element < 0 || element >= divided.Count || divided[element] == null) continue;
                        if (!rowBySegment.TryGetValue(divided[element].No, out var row)) continue;

                        foreach (LoadCase loadCase in inputModel.LoadCasesInput.AllSeismicLoadCases)
                        {
                            int k = loadCase.Level - 1;
                            if (k < 0 || k > 1) continue;
                            foreach (LoadCombination loadCombination in inputModel.LoadCasesInput.AllLoadCombinations)
                            {
                                foreach (var isLiquefaction in liqPatterns)
                                {
                                    var force = GetBeamResultCached(beam, loadCase, loadCombination, isLiquefaction)?.CumulativeForce;
                                    if (force == null) continue;
                                    row.Qmax[k] = Max(row.Qmax[k], force.FabsMax);
                                    row.Mmax[k] = Max(row.Mmax[k], force.MabsMax);

                                    foreach (var node in new[] { beam.NodeI, beam.NodeJ })
                                    {
                                        if (node == null) continue;
                                        var disp = GetNodeResultCached(node, loadCase, loadCombination, isLiquefaction)?.CumulativeDisp;
                                        if (disp != null) row.Dmax[k] = Max(row.Dmax[k], disp.Uh);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return rows;

            static double Max(double? current, double value) => current is double c ? Math.Max(c, value) : value;
            static double Min(double? current, double value) => current is double c ? Math.Min(c, value) : value;
        }

        private void BuildAnalysisResultSummaryTable(Body body, List<PileForceSummaryRow> rows, bool hasLevel1Results, bool hasLevel2Results)
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

                // 結果の無いセルは「—」。以前は初期値 (double.MinValue など) をそのまま数値で出していた
                static string Value(double? v, string format) => v is double d ? d.ToString(format) : "—";

                for (int rowIdx = 1; rowIdx <= rows.Count + 1; rowIdx++)
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
                            var r = rows[rowIdx - 2];
                            if (colIdx == 1) SetTableCellWithVerticalAlign(cell, GetParagraph($"{r.PileBodyRef}", "center", 8), "center");
                            else if (colIdx == 2) SetTableCellWithVerticalAlign(cell, GetParagraph($"{r.SegmentNo}", "center", 8), "center");
                            else if (colIdx == 3) SetTableCellWithVerticalAlign(cell, GetParagraph($"{r.Top:N3}", "center", 8), "center");
                            else if (colIdx == 4) SetTableCellWithVerticalAlign(cell, GetParagraph($"{r.Bottom:N3}", "center", 8), "center");
                            else if (colIdx == 5) SetTableCellWithVerticalAlign(cell, GetParagraph(Value(r.Dmax[k], "N3"), "center", 8), "center");
                            else if (colIdx == 6) SetTableCellWithVerticalAlign(cell, GetParagraph(Value(r.Qmax[k], "N1"), "center", 8), "center");
                            else if (colIdx == 7) SetTableCellWithVerticalAlign(cell, GetParagraph(Value(r.Mmax[k], "N1"), "center", 8), "center");
                            else if (colIdx == 8) SetTableCellWithVerticalAlign(cell, GetParagraph(Value(r.NMax[k], "N1"), "center", 8), "center");
                            else if (colIdx == 9) SetTableCellWithVerticalAlign(cell, GetParagraph(Value(r.NMin[k], "N1"), "center", 8), "center");
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
                if (rows.Any(r => r.HasMissing(k)))
                    AddTableNote(body, "※「—」は、その区間・レベルの解析結果 (軸力は入力値) が無いことを示す。");
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
                            var para = GetParagraph("作用の組合せ", "center", 8);
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

        /// <summary>
        /// 水平解析の反力合計（土圧合力ばね／杭周地盤ばね）を、指定レベルの荷重ケース × 作用組合せで表出力。
        /// 根入れ部が存在し水平解析が実行済みの場合のみ呼ばれる想定。
        /// </summary>
        private void AddHorizontalReactionSummaryTable(MainDocumentPart mainDocumentPart, Body body, int level)
        {
            var loadCasesInput = inputModel?.LoadCasesInput;
            if (loadCasesInput?.LoadCombinations == null || loadCasesInput.LoadCombinations.Count == 0) return;
            if (anaModel == null) return;

            var cases = level == 1 ? loadCasesInput.LoadCasesLevel1 : loadCasesInput.LoadCasesLevel2;
            if (cases == null || cases.Count == 0) return;

            int combCount = loadCasesInput.LoadCombinations.Count;

            var liqPatterns = new List<bool>();
            if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
            if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionNo) liqPatterns.Add(false);
            if (liqPatterns.Count == 0) liqPatterns.Add(true);

            var dgbSprings = anaModel.HorizontalSoilSprings?
                .Where(s => s.NodeI?.Name == "根入部節点").ToList() ?? [];
            var pileSprings = anaModel.HorizontalSoilSprings?
                .Where(s => s.NodeJ?.Name != null && s.NodeJ.Name.StartsWith("杭地盤節点-")).ToList() ?? [];

            if (dgbSprings.Count == 0 && pileSprings.Count == 0) return;

            bool anyMissing = false;
            string ComputeCellText(List<HorizontalSoilSpring> springs, LoadCombination comb)
            {
                var sb = new System.Text.StringBuilder();
                for (int ci = 0; ci < cases.Count; ci++)
                {
                    var lc = cases[ci];
                    foreach (var isLiq in liqPatterns)
                    {
                        int lastStep = GetLastStepCached(lc, comb, isLiq);
                        if (lastStep < 0) continue;
                        var (fh, found) = SumHorizontalReaction(springs, lc, comb, isLiq, lastStep);
                        if (found < springs.Count) anyMissing = true;
                        string liqMark = liqPatterns.Count > 1 ? (isLiq ? "[液]" : "[非液]") : string.Empty;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(ReactionCellEntry($"{lc.LoadName}{liqMark}", fh, found, springs.Count));
                    }
                }
                return sb.ToString();
            }

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

            // Row 1: ヘッダー
            {
                var row = new TableRow();
                var headCell = new TableCell();
                SetTableCellWithVerticalAlign(headCell, GetParagraph($"レベル{level}\n水平解析 反力合計", "center", 8), "center");
                row.Append(headCell);
                for (int c = 0; c < combCount; c++)
                {
                    var cell = new TableCell();
                    SetTableCellWithVerticalAlign(cell, GetParagraph($"{c + 1}", "center", 8), "center");
                    row.Append(cell);
                }
                table.Append(row);
            }

            // Row 2: 土圧合力ばね反力合計
            {
                var row = new TableRow();
                var labelCell = new TableCell();
                SetTableCellWithVerticalAlign(labelCell, GetParagraph("土圧合力ばね\n反力合計 [kN]", "center", 8), "center");
                row.Append(labelCell);
                for (int c = 0; c < combCount; c++)
                {
                    var cell = new TableCell();
                    string text = dgbSprings.Count > 0 ? ComputeCellText(dgbSprings, loadCasesInput.LoadCombinations[c]) : "—";
                    SetTableCellWithVerticalAlign(cell, GetParagraph(text, "right", 8), "center");
                    row.Append(cell);
                }
                table.Append(row);
            }

            // Row 3: 杭反力合計（杭周地盤ばね反力合計）
            {
                var row = new TableRow();
                var labelCell = new TableCell();
                SetTableCellWithVerticalAlign(labelCell, GetParagraph("杭反力合計\n[kN]", "center", 8), "center");
                row.Append(labelCell);
                for (int c = 0; c < combCount; c++)
                {
                    var cell = new TableCell();
                    string text = pileSprings.Count > 0 ? ComputeCellText(pileSprings, loadCasesInput.LoadCombinations[c]) : "—";
                    SetTableCellWithVerticalAlign(cell, GetParagraph(text, "right", 8), "center");
                    row.Append(cell);
                }
                table.Append(row);
            }

            body.Append(table);
            if (anyMissing)
                AddTableNote(body, "※ 印の値は、一部のばねの解析結果が無く、そのばねを除いて合計した値である (括弧内は結果のあったばねの本数 / 全本数)。"
                    + "「結果なし」はどのばねにも結果が無いことを示す。");
        }

        /// <summary>
        /// 地盤ばねの水平反力を合計する (その荷重条件の最終ステップ)。合計の大きさと、結果のあったばねの本数を返す。
        ///
        /// 結果の無いばねは合計に入らない。以前はそれを数えずに黙って飛ばしていたので、一部が欠けても
        /// 完全な合計のように表に出た。本数を返して、欠けたときは表で示す (<see cref="ReactionCellEntry"/>)。
        /// </summary>
        internal static (double Fh, int Found) SumHorizontalReaction(IEnumerable<HorizontalSoilSpring> springs,
            LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step)
        {
            double sumFx = 0, sumFy = 0;
            int found = 0;
            foreach (var spring in springs)
            {
                var r = spring?.HorizontalSpringResults?.FirstOrDefault(rr =>
                    rr.IsLiquefaction == isLiquefaction && rr.Step == step &&
                    PileDesign.Models.InputData.LoadCase.IsSameCase(rr.LoadCase, loadCase) &&
                    PileDesign.Models.InputData.LoadCombination.IsSameCombination(rr.LoadCombination, loadCombination));
                if (r?.CumulativeForce == null) continue;
                sumFx += r.CumulativeForce.Fxi;
                sumFy += r.CumulativeForce.Fyi;
                found++;
            }
            return (Math.Sqrt(sumFx * sumFx + sumFy * sumFy), found);
        }

        /// <summary>反力合計のセルの 1 行。欠けたばねがあれば「※ (結果のあった本数/全本数)」を添え、1 本も無ければ「結果なし」。</summary>
        internal static string ReactionCellEntry(string label, double fh, int found, int total)
            => found == 0 ? $"{label}: 結果なし"
             : found < total ? $"{label}: {fh:N1} ※({found}/{total})"
             : $"{label}: {fh:N1}";

        /// <summary>
        /// 水平解析の検定結果（NG のみ）を DOCX に追記する。
        /// </summary>
        private void AddHorizontalEvaluationReport(Body body, bool factored)
        {
            if (mainWindowViewModel == null) return;

            // 画面の検定テキスト (罫線を並べた等幅の固定行) をそのまま貼るのをやめ、
            // 構造化した結果から Word の表として組む。テキスト側は画面と golden が使うので触らない。
            Models.Results.EvaluationResult result;
            try
            {
                result = ViewModels.EvaluationService.BuildEvaluationResult(mainWindowViewModel, factored);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, factored ? "水平解析の検定結果 (低減後)" : "水平解析の検定結果 (低減前)", ex);
                return;
            }
            if (result == null || result.IsEmpty) return;

            // 長期 (常時) の検定は既定では載せない。VL 単独ケースを解析したときだけ現れ、
            // 水平荷重が無いので土圧などが無ければ問題にならない。
            // 落とすときは<b>落とした事実を書く</b> (黙って消すと「長期は検定していない」のか
            // 「検定して OK だった」のか読めない)。
            // 対象は長期の<b>曲げ・せん断</b>だけ。杭頭変形角 (不同沈下) は長期そのものが本題なので、
            // 出力しない設定でも残す。
            static bool IsLongTermSectionCheck(Models.Results.EvaluationItem i) =>
                i.Level == 0
                && i.Kind is Models.Results.EvaluationKind.PileSectionMoment
                          or Models.Results.EvaluationKind.PileSectionShear;

            int longTermCount = result.Items.Count(IsLongTermSectionCheck);
            bool includeLongTerm = mainWindowViewModel.DocxOutput.IncludeHorizontal_LongTermEvaluation;
            if (!includeLongTerm && longTermCount > 0)
            {
                result = new Models.Results.EvaluationResult(
                    result.Items.Where(i => !IsLongTermSectionCheck(i)).ToList());
                if (result.IsEmpty) return;
            }

            AddPageBreak(body);
            // H1 に昇格 (旧: H2 で親 H1 がなく、直前の無関係な H1 の子として番号付けされていた)
            AddHeader1(body, factored ? "水平解析 検定（低減後）" : "水平解析 検定（低減前）", 1);

            string grade = inputModel?.FundamentalInput?.SeismicGrade ?? "A";
            string governing = result.Governing is { } g
                ? $"　支配ケース: {g.Category} {g.TargetName}{g.EndLabel}（{g.LoadCaseName} {g.LoadCombinationName}）"
                : string.Empty;
            AddText(body,
                $"耐震性能グレード {grade}。検定項目 {result.Items.Count} 件（OK {result.OkCount} 件 / NG {result.NgCount} 件）。"
                + (result.MaxRatio is { } r ? $" 最大検定比 {r:F2}。{governing}" : string.Empty));

            // 収束しなかったケースがあれば、判定より先に書く。
            // 「OK n 件」だけを読んで安心されると、解けていないケースが検討済みとして通ってしまう。
            if (result.UnconvergedCount > 0)
            {
                var unconvergedCases = result.Items
                    .Where(i => i.IsFromUnconvergedCase)
                    .Select(i => string.IsNullOrEmpty(i.LiquefactionLabel)
                        ? $"{i.LoadCaseName} {i.LoadCombinationName}"
                        : $"{i.LoadCaseName} {i.LoadCombinationName}（{i.LiquefactionLabel}）")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                AddText(body,
                    $"このうち {result.UnconvergedCount} 件は、水平解析が収束しなかった荷重ケースの結果である。"
                    + "収束していない状態の応答値は釣り合いを満たしておらず、限界値と比べても意味を持たないため、"
                    + "OK / NG のいずれにも数えていない。"
                    + "計算ステップ数を増やして再解析するか、耐力が足りているかを確認すること。");
                AddText(body, "収束しなかった荷重ケース: " + string.Join(" / ", unconvergedCases));
            }

            // 算定式 (工法) の適用範囲の外の項目も、判定より先に書く。理由ごとにまとめる。
            if (result.OutOfScopeCount > 0)
            {
                var reasons = result.Items
                    .Where(i => !i.IsFromUnconvergedCase && i.IsOutOfScope)
                    .Select(i => i.OutOfScopeReason!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                AddText(body,
                    $"このうち {result.OutOfScopeCount} 件は、せん断耐力の算定式 (高強度せん断補強筋の工法の指針の式) の"
                    + "適用範囲の外である。範囲の外の限界値は指針の保証の外の値なので、"
                    + "OK / NG のいずれにも数えていない (表の判定は「適用範囲外」)。"
                    + "工法の適用範囲に収まる断面に見直すか、工法を「標準」にして検討すること。");
                AddText(body, "適用範囲の外になった理由: " + string.Join(" / ", reasons));
            }

            // 検定の対象なのにデータが欠けて検定できなかった項目も、判定より先に書く。理由ごとにまとめる。
            // 以前は項目を作らずに進めていたので、表に無い理由が読めなかった。
            if (result.UnavailableCount > 0)
            {
                var reasons = result.Items
                    .Where(i => i.IsUnavailable)
                    .GroupBy(i => $"{i.Category}: {i.UnavailableReason}")
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => $"{g.Key} ({g.Count()} 件)")
                    .ToList();

                AddText(body,
                    $"このうち {result.UnavailableCount} 件は、検定に必要なデータ (その荷重条件の解析結果・断面・限界曲線) が"
                    + "欠けていたため検定できなかった。OK / NG のいずれにも数えていない (表の判定は「検定不能」)。");
                AddText(body, "検定できなかった理由: " + string.Join(" / ", reasons));
            }

            // 緩めた基準で受理したケースも、件数だけは集計に添える。
            // 各行の判定は「OK(緩和受理)」と出るが、まとめだけを読む人には伝わらないため。
            if (result.RelaxedCount > 0)
            {
                var relaxedCases = result.Items
                    .Where(i => i.IsFromRelaxedCase)
                    .Select(i => string.IsNullOrEmpty(i.LiquefactionLabel)
                        ? $"{i.LoadCaseName} {i.LoadCombinationName}"
                        : $"{i.LoadCaseName} {i.LoadCombinationName}（{i.LiquefactionLabel}）")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                AddText(body,
                    $"上記のうち {result.RelaxedCount} 件は、収束基準を緩めて受理したステップを含む荷重ケースの結果である。"
                    + "釣り合いは満たしているとみなして OK / NG に数えているが、残差は本来の許容値まで下がっていない。"
                    + "判定には「(緩和受理)」を添えてある。");
                AddText(body, "緩和受理を含む荷重ケース: " + string.Join(" / ", relaxedCases));
            }

            if (!includeLongTerm && longTermCount > 0)
            {
                AddTableNote(body,
                    $"※ 長期（常時）の曲げ・せん断の検定 {longTermCount} 件は、出力しない設定のため件数に含めていない。");
            }
            else if (includeLongTerm && longTermCount > 0)
            {
                AddTableNote(body,
                    $"※ 長期（常時）の曲げ・せん断の検定 {longTermCount} 件を含む。いずれも使用限界で照査している。");
            }

            if (result.NgCount == 0)
            {
                AddText(body, result.UnconvergedCount > 0 || result.OutOfScopeCount > 0 || result.UnavailableCount > 0
                    ? "判定できた検定項目は、すべて限界値を下回っている（NG 項目なし）。"
                      + "収束しなかったケース・算定式の適用範囲の外の項目・検定できなかった項目については、上記のとおり判定していない。"
                    : "すべての検定項目が限界値を下回っている（NG 項目なし）。");
                return;
            }

            AddTableCaption(body, factored ? "検定 NG 項目（低減後）" : "検定 NG 項目（低減前）");

            const double fontSize = 8.0;
            var table = CreateTableWithBorders();
            table.Append(CreateHeaderRow(
                CreateTableCell(["検定項目"], fontSize, "center"),
                CreateTableCell(["対象"], fontSize, "center"),
                CreateTableCell(["レベル"], fontSize, "center"),
                CreateTableCell(["荷重ケース"], fontSize, "center"),
                CreateTableCell(["組合せ"], fontSize, "center"),
                CreateTableCell(["応答"], fontSize, "center"),
                CreateTableCell(["限界"], fontSize, "center"),
                CreateTableCell(["単位"], fontSize, "center"),
                CreateTableCell(["軸力", "[kN]"], fontSize, "center"),
                CreateTableCell(["M/(Q·d)"], fontSize, "center"),
                CreateTableCell(["検定比"], fontSize, "center")));

            // 検定比の大きい順。どこが一番危ないかを上から読めるようにする
            // NG の件数 (NgCount) と同じく、判定できる項目だけを並べる。未収束・適用範囲外の行は
            // 限界値を超えていても NG とは言えないので、この表には入れない (上の本文で別に知らせている)
            foreach (var item in result.ByRatioDescending.Where(i => i.IsJudged && !i.IsOk))
            {
                // 対象は TargetDescription を使う。要素名 + 端 (「beam i端」) では
                // どの杭のどこか読めず、行がすべて同じ表記になる。
                string target = item.TargetDescription;
                string liq = item.LiquefactionLabel;
                string load = string.IsNullOrEmpty(liq) ? item.LoadCaseName : $"{item.LoadCaseName}（{liq}）";

                var row = new TableRow();
                row.Append(CreateTableCell([item.Category], fontSize, "left"));
                row.Append(CreateTableCell([target], fontSize, "left"));
                row.Append(CreateTableCell([item.Level > 0 ? $"L{item.Level}" : "-"], fontSize, "center"));
                row.Append(CreateTableCell([load], fontSize, "left"));
                row.Append(CreateTableCell([item.LoadCombinationName], fontSize, "left"));
                row.Append(CreateTableCell([item.ResponseText], fontSize, "right"));
                row.Append(CreateTableCell([item.LimitText], fontSize, "right"));
                row.Append(CreateTableCell([item.Unit], fontSize, "center"));
                // 限界値の前提。曲げ・せん断以外は軸力を持たず、M/(Q·d) はせん断のみ。
                row.Append(CreateTableCell(
                    [item.AxialForce is double n ? $"{n:N1}" : "—"], fontSize, "right"));
                row.Append(CreateTableCell(
                    [item.MonQd is double q ? $"{q:N2}" : "—"], fontSize, "right"));
                row.Append(CreateTableCell([item.Ratio is { } ratio ? $"{ratio:F2}" : "-"], fontSize, "right"));
                table.Append(row);
            }

            body.Append(table);
            AddTableNote(body,
                "※ 検定比 = 応答値 / 限界値。1.00 を超えるものを NG として挙げている。"
                + "軸力は限界値を求めるのに使った値、M/(Q·d) はせん断耐力の算定に使った値で、"
                + "いずれも杭ごと・荷重ケースごとに求めている。");
        }

        /// <summary>
        /// 杭の鉛直支持力の検定（押込み・引抜き）を DOCX に追記する。
        ///
        /// 水平解析の検定とは別の節にする。支持力は低減の有無で変わらないので
        /// 「低減前 / 低減後」の 2 枚に混ぜると同じ行が重複し、
        /// 検定比の降順で読んだときに支配ケースを読み違えるため。
        /// NG だけでなく全項目を載せる（件数が杭本数 × 限界状態と少なく、
        /// 余裕度を読むのが目的の表なので、OK 側も見えている方が使える）。
        /// </summary>
        private void AddPileBearingEvaluationReport(Body body)
        {
            if (inputModel == null) return;

            List<Models.Results.EvaluationItem> items;
            try
            {
                string grade = inputModel.FundamentalInput?.SeismicGrade ?? "A";
                items = Services.PileBearingEvaluator.Evaluate(inputModel, grade);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "支持力の検定結果", ex);
                return;
            }
            if (items == null || items.Count == 0) return;

            AddPageBreak(body);
            AddHeader1(body, "杭の鉛直支持力 検定", 1);

            int ng = items.Count(i => !i.IsOk);
            AddText(body,
                $"検定項目 {items.Count} 件（OK {items.Count - ng} 件 / NG {ng} 件）。"
                + "応答値は入力した杭軸力、限界値は地盤と杭体から定まる支持力・引抜き抵抗力である。");
            AddText(body,
                "限界状態の対応は杭体断面の検定と同じで、長期は使用限界（極限支持力の 1/3）、"
                + "レベル1 は損傷限界（1/1.5）、レベル2 は終局限界（極限支持力）とした"
                + "（耐震性能グレード S ではレベル2 も損傷限界）。"
                + "引抜き側は損傷限界で降伏引抜き抵抗力、終局限界で残留引抜き抵抗力を限界値とする。");

            AddTableCaption(body, "検定結果（杭の鉛直支持力）");

            const double fontSize = 8.0;
            var table = CreateTableWithBorders();
            table.Append(CreateHeaderRow(
                CreateTableCell(["検定項目"], fontSize, "center"),
                CreateTableCell(["対象"], fontSize, "center"),
                CreateTableCell(["荷重ケース"], fontSize, "center"),
                CreateTableCell(["応答"], fontSize, "center"),
                CreateTableCell(["限界"], fontSize, "center"),
                CreateTableCell(["単位"], fontSize, "center"),
                CreateTableCell(["検定比"], fontSize, "center"),
                CreateTableCell(["判定"], fontSize, "center")));

            // 検定比の大きい順。どこが一番余裕がないかを上から読めるようにする
            foreach (var item in items.OrderByDescending(i => double.IsNaN(i.Ratio) ? -1.0 : i.Ratio))
            {
                var row = new TableRow();
                row.Append(CreateTableCell([item.Category], fontSize, "left"));
                row.Append(CreateTableCell([item.TargetDescription], fontSize, "left"));
                row.Append(CreateTableCell([item.LoadCaseName], fontSize, "left"));
                row.Append(CreateTableCell([item.ResponseText], fontSize, "right"));
                row.Append(CreateTableCell([item.LimitText], fontSize, "right"));
                row.Append(CreateTableCell([item.Unit], fontSize, "center"));
                row.Append(CreateTableCell([double.IsNaN(item.Ratio) ? "-" : $"{item.Ratio:F2}"], fontSize, "right"));
                row.Append(CreateTableCell([item.StatusLabel], fontSize, "center"));
                table.Append(row);
            }

            body.Append(table);
            AddTableNote(body, "※ 検定比 = 応答値 / 限界値。押込み・引抜きとも大きさ（絶対値）で比較している。");
        }

        /// <summary>
        /// 杭の沈下量の検定。<b>基本設定で有効にしたときだけ出す。</b>
        ///
        /// <para>許容沈下量は設計者が決める量なので、既定では検定しない。既定値で勝手に
        /// 合否を出すと、根拠の無い判定が計算書に残る。有効にした場合は、その許容値が
        /// 入力であることを本文に明記する。</para>
        /// </summary>
        /// <summary>
        /// 沈下による杭頭変形角の検定 (常時・使用限界)。沈下解析をしていなければ出さない。
        ///
        /// <para>以前は水平解析の検定の章 (低減前・低減後) の中にあり、水平解析を済ませないと載らず、
        /// 2 つの章に同じ項目が並んでいた。沈下の結果だけで決まるので沈下の検定として独立させた。</para>
        /// </summary>
        private void AddSettlementDeformationAngleReport(Body body)
        {
            if (inputModel == null) return;

            List<Models.Results.EvaluationItem> items;
            try
            {
                items = Services.SettlementDeformationAngleEvaluator.Evaluate(inputModel);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "沈下による杭頭変形角の検定結果", ex);
                return;
            }
            if (items == null || items.Count == 0) return;   // 沈下解析をしていない、または杭が 1 本

            AddPageBreak(body);
            AddHeader1(body, "沈下による杭頭変形角 検定", 1);

            var judged = items.Where(i => i.IsJudged).ToList();
            int ng = judged.Count(i => !i.IsOk);
            string basis = inputModel.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下";
            double limit = items[0].Limit;
            AddText(body,
                $"検定項目 {items.Count} 件（OK {judged.Count - ng} 件 / NG {ng} 件）。"
                + $"応答値は沈下検討の対象（{basis}）の沈下量から求めた、すべての杭頭の組の変形角 |ΔS| / 杭間の水平距離 の最大値、"
                + $"限界値は使用限界の変形角 {limit:E3} rad である（常時荷重による即時沈下。杭基礎では圧密沈下は生じない）。");
            foreach (var u in items.Where(i => i.IsUnavailable))
                AddText(body, $"{u.LoadCaseName}: 検定できなかった（{u.UnavailableReason}）。");

            AddTableCaption(body, "検定結果（沈下による杭頭変形角）");
            const double fontSize = 8.0;
            var table = CreateTableWithBorders();
            table.Append(CreateHeaderRow(
                CreateTableCell(["荷重ケース"], fontSize, "center"),
                CreateTableCell(["対象"], fontSize, "center"),
                CreateTableCell(["変形角", "[rad]"], fontSize, "center"),
                CreateTableCell(["限界値", "[rad]"], fontSize, "center"),
                CreateTableCell(["検定比"], fontSize, "center"),
                CreateTableCell(["判定"], fontSize, "center")));
            foreach (var i in items)
            {
                table.Append(new TableRow(
                    CreateTableCell([i.LoadCaseName], fontSize, "center"),
                    CreateTableCell([i.TargetName], fontSize, "center"),
                    CreateTableCell([i.ResponseText], fontSize, "right"),
                    CreateTableCell([i.LimitText], fontSize, "right"),
                    CreateTableCell([double.IsFinite(i.Ratio) ? i.Ratio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "—"], fontSize, "right"),
                    CreateTableCell([i.StatusLabel], fontSize, "center")));
            }
            body.Append(table);
        }

        private void AddPileSettlementEvaluationReport(Body body)
        {
            if (inputModel == null) return;

            List<Models.Results.EvaluationItem> items;
            try
            {
                items = Services.PileSettlementEvaluator.Evaluate(inputModel);
            }
            catch (Exception ex)
            {
                NoteOmitted(body, "沈下量の検定結果", ex);
                return;
            }
            if (items == null || items.Count == 0) return;   // 検定しない設定、または許容値が未入力

            AddPageBreak(body);
            AddHeader1(body, "杭の沈下量 検定", 1);

            int ng = items.Count(i => !i.IsOk);
            double limit = inputModel.FundamentalInput?.AllowableSettlement_mm ?? 0.0;
            AddText(body,
                $"検定項目 {items.Count} 件（OK {items.Count - ng} 件 / NG {ng} 件）。"
                + $"応答値は沈下解析が求めた各杭の沈下量（単杭沈下 + 群杭沈下、長期）、"
                + $"限界値は入力した許容沈下量 {limit:0.###} mm である。");
            AddText(body,
                "許容沈下量は、上部構造が許せる変形から設計者が定める値である"
                + "（建築基礎構造設計指針は構造種別に応じて定めるとしており、一意の値を与えない）。"
                + "したがってこの検定は既定では行わず、基本設定で有効にした場合にのみ出力する。"
                + "群杭沈下解析を実行していない場合、応答値は単杭沈下量に等しい。");

            AddTableCaption(body, "検定結果（杭の沈下量）");

            const double fontSize = 8.0;
            var table = CreateTableWithBorders();
            table.Append(CreateHeaderRow(
                CreateTableCell(["検定項目"], fontSize, "center"),
                CreateTableCell(["対象"], fontSize, "center"),
                CreateTableCell(["荷重ケース"], fontSize, "center"),
                CreateTableCell(["応答"], fontSize, "center"),
                CreateTableCell(["限界"], fontSize, "center"),
                CreateTableCell(["単位"], fontSize, "center"),
                CreateTableCell(["検定比"], fontSize, "center"),
                CreateTableCell(["判定"], fontSize, "center")));

            // 検定比の大きい順。どこが一番余裕がないかを上から読めるようにする
            foreach (var item in items.OrderByDescending(i => double.IsNaN(i.Ratio) ? -1.0 : i.Ratio))
            {
                var row = new TableRow();
                row.Append(CreateTableCell([item.Category], fontSize, "left"));
                row.Append(CreateTableCell([item.TargetDescription], fontSize, "left"));
                row.Append(CreateTableCell([item.LoadCaseName], fontSize, "left"));
                row.Append(CreateTableCell([item.ResponseText], fontSize, "right"));
                row.Append(CreateTableCell([item.LimitText], fontSize, "right"));
                row.Append(CreateTableCell([item.Unit], fontSize, "center"));
                row.Append(CreateTableCell([double.IsNaN(item.Ratio) ? "-" : $"{item.Ratio:F2}"], fontSize, "right"));
                row.Append(CreateTableCell([item.StatusLabel], fontSize, "center"));
                table.Append(row);
            }

            body.Append(table);
            AddTableNote(body, "※ 検定比 = 沈下量 / 許容沈下量。");
        }

    }
}
