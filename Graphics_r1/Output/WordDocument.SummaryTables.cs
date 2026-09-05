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
                                if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionYes) liqPatterns2.Add(true);
                                if (mainWindowViewModel.DocxOutput.IncludeOutputLiquefactionNo) liqPatterns2.Add(false);

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
                                        var beamResult = GetBeamResultCached(beam, loadCase, loadCombination, isLiquefaction);
                                        var cumForce = beamResult?.CumulativeForce;
                                        if (cumForce == null) continue;

                                        double momentInPile = cumForce.MabsMax;
                                        double shearInPile = cumForce.FabsMax;

                                        // Node results may be missing -> fallback to 0.0
                                        double uhI = 0.0, uhJ = 0.0;
                                        try
                                        {
                                            var nodeIResult = beam.NodeI is null ? null : GetNodeResultCached(beam.NodeI, loadCase, loadCombination, isLiquefaction);
                                            var nodeJResult = beam.NodeJ is null ? null : GetNodeResultCached(beam.NodeJ, loadCase, loadCombination, isLiquefaction);
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
                            else if (colIdx == 3) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentTop[i]:N3}", "center", 8), "center");
                            else if (colIdx == 4) SetTableCellWithVerticalAlign(cell, GetParagraph($"{selectedSegmentBtm[i]:N3}", "center", 8), "center");
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
                        double sumFx = 0, sumFy = 0;
                        foreach (var spring in springs)
                        {
                            var r = spring.HorizontalSpringResults?.FirstOrDefault(rr =>
                                rr.IsLiquefaction == isLiq && rr.Step == lastStep &&
                                rr.LoadCase?.LoadName == lc.LoadName &&
                                rr.LoadCombination?.Name == comb.Name);
                            if (r?.CumulativeForce == null) continue;
                            sumFx += r.CumulativeForce.Fxi;
                            sumFy += r.CumulativeForce.Fyi;
                        }
                        double fh = Math.Sqrt(sumFx * sumFx + sumFy * sumFy);
                        string liqMark = liqPatterns.Count > 1 ? (isLiq ? "[液]" : "[非液]") : string.Empty;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append($"{lc.LoadName}{liqMark}: {fh:N1}");
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
        }

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
                Log.Warning(ex, "[DOCX] 検定結果の生成に失敗");
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
                AddText(body, result.UnconvergedCount > 0
                    ? "収束した荷重ケースの検定項目は、すべて限界値を下回っている（NG 項目なし）。"
                      + "収束しなかったケースについては上記のとおり判定できていない。"
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
            foreach (var item in result.ByRatioDescending.Where(i => !i.IsOk))
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
                Log.Warning(ex, "[DOCX] 支持力の検定の生成に失敗");
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

    }
}
