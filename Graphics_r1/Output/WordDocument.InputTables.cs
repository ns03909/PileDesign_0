using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace PileDesign.Output
{
    // 入力データ（基本設定・荷重ケース・地盤・杭体・杭配置・根入部・杭支持力）を
    // Word 表として出力する partial。
    internal partial class WordDocument
    {
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
                    CreateTableCellWithWidth("標高記号", "left", width0),
                    CreateTableCellWithWidth(fundamentalInput.RefLevel, "left", width1)
                );
                table.Append(row2);

                // 4行目: Z=0 の標高
                TableRow row3 = new();
                row3.Append(
                    CreateTableCellWithWidth("Z=0 の標高 (m)", "left", width0),
                    CreateTableCellWithWidth($"{fundamentalInput.ReferenceAltitude:N3}", "left", width1)
                );
                table.Append(row3);

                // 列幅を設定
                //SetColumnWidth(table, 0, 20); // 1列目の幅
                //SetColumnWidth(table, 1, 60); // 2列目の幅

                body.Append(table);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddFundamentalTableでエラーが発生しました: {ex.Message}");
            }
        }

        // 荷重条件の表を追加するメソッド
        private static void AddLoadCaseTable(Body body, ObservableCollection<LoadCase> loadCases, FundamentalInput fundamentalInput)
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
            CreateTableCell(["慣性力", "位置", "Z", "[m]"], fontSize, "center"),
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
                dataRow.Append(CreateTableCell([loadCase.LoadName], fontSize, "left"));
                dataRow.Append(CreateTableCell([loadCase.LoadAngle.ToString()], fontSize, "right"));
                dataRow.Append(CreateTableCell([BoolMark(loadCase.IsSoilNonLinear)], fontSize, "center"));
                dataRow.Append(CreateTableCell([BoolMark(loadCase.IsPileNonLinear)], fontSize, "center"));

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
        public static void AddGroundInfo(Body body, ObservableCollection<GroundInput> grounds, FundamentalInput fundamentalInput)
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
                    CreateTableCell(["標高[m]"], fontSize, "center"),
                    CreateTableCell(["Z[m]"], fontSize, "center")
                );
                table.Append(headerRow);

                // 2行目: データ
                TableRow row1 = new();
                row1.Append(
                    CreateTableCell(["孔口レベル"], fontSize, "center"),
                    CreateTableCell([$"{ground.GLDepth:N3}"], fontSize, "right"),
                    CreateTableCell([$"{fundamentalInput.ToAbsolute(ground.GroundTopAltitude):N3}"], fontSize, "right"),
                    CreateTableCell([$"{ground.GroundTopAltitude:N3}"], fontSize, "right")
                );
                table.Append(row1);

                // 3行目: データ
                TableRow row2 = new();
                row2.Append(
                    CreateTableCell(["地下水位"], fontSize, "center"),
                    CreateTableCell([$"{ground.GroundWaterGLDepth:N3}"], fontSize, "right"),
                    CreateTableCell([$"{fundamentalInput.ToAbsolute(ground.GroundWaterTableAltitude):N3}"], fontSize, "right"),
                    CreateTableCell([$"{ground.GroundWaterTableAltitude:N3}"], fontSize, "right")
                );
                table.Append(row2);

                // 4行目: データ
                TableRow row3 = new();
                row3.Append(
                    CreateTableCell(["地中応力検討用レベル"], fontSize, "center"),
                    CreateTableCell([$"{ground.StressGLDepth:N3}"], fontSize, "right"),
                    CreateTableCell([$"{fundamentalInput.ToAbsolute(ground.StressAltitude):N3}"], fontSize, "right"),
                    CreateTableCell([$"{ground.StressAltitude:N3}"], fontSize, "right")
                );
                table.Append(row3);

                body.Append(table);

                body.Append(new Paragraph());

                AddGroundLayerTable(body, ground.GroundLayers, fundamentalInput);

                body.Append(new Paragraph());

                AddGroundMassTable(body, ground.GroundMassesData, fundamentalInput);

                body.Append(new Paragraph());

                AddLiquefactionInfo(body, ground);

                body.Append(new Paragraph());

                AddGroundDisplacementInfo(body, ground);
            }
        }

        // 地盤情報の表を追加するメソッド
        public static void AddGroundLayerTable(Body body, ObservableCollection<GroundLayerInput> groundLayers, FundamentalInput fundamentalInput)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["土層", "番号"], fontSize, "center"),
            CreateTableCell(["層厚", "[m]"], fontSize, "center"),
            CreateTableCell(["下端", "深度", "[m]"], fontSize, "center"),
            CreateTableCell(["下端", "標高", "[m]"], fontSize, "center"),
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
                dataRow.Append(CreateTableCell([$"{fundamentalInput.ToAbsolute(groundLayer.BottomAltitude):N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.BottomAltitude:N3}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.GranularityClass}"], fontSize, "center"));
                dataRow.Append(CreateTableCell([$"{groundLayer.Density:N1}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{groundLayer.AgeCategory}"], fontSize, "center"));
                dataRow.Append(CreateTableCell([BoolMark(groundLayer.IsPositiveCircumResistance)], fontSize, "center"));
                dataRow.Append(CreateTableCell([BoolMark(groundLayer.IsNegativeCircumResistance)], fontSize, "center"));
                dataRow.Append(CreateTableCell([BoolMark(groundLayer.IsEngineeringBedrock)], fontSize, "center"));

                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 地盤情報の表を追加するメソッド
        public static void AddGroundMassTable(Body body, ObservableCollection<GroundMassDataInput> groundMasses, FundamentalInput fundamentalInput)
        {
            double fontSize = 8.0;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["土", "質点", "番号"], fontSize, "center"),
            CreateTableCell(["間隔", "[m]"], fontSize, "center"),
            CreateTableCell(["深度", "[m]"], fontSize, "center"),
            CreateTableCell(["標高", "[m]"], fontSize, "center"),
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
                dataRow.Append(CreateTableCell([$"{fundamentalInput.ToAbsolute(groundMass.AltitudeDepth):N3}"], fontSize, "right"));
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
                AddText(body, $"液状化レベル{i + 1}(L{i + 1})");

                Table table0 = CreateTableWithBorders();

                table0.Append(new TableRow(
                    CreateTableCell(["地表面加速度"], fontSize, "center"),
                    CreateTableCell([i == 0 ?$"{groundInput.GroundAcceleration1:F1}[m/s]" :
                                             $"{groundInput.GroundAcceleration2:F1}[m/s]"], fontSize, "right")
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
                CreateTableCell([$"L{i + 1}", "繰返し", "せん断", "応力度", "τ<_d>/σ<_z>'"], fontSize, "center"),
                CreateTableCell([$"L{i + 1}", "液状化", "安全率", "F<_L>"], fontSize, "center"),
                CreateTableCell([$"L{i + 1}", "地盤", "反力", "係数", "低減率", "β<_L>"], fontSize, "center"),
                CreateTableCell([$"L{i + 1}", "繰返し", "せん断", "ひずみ", "γ<_cy>", "[%]"], fontSize, "center"),
                CreateTableCell([$"L{i + 1}", "液状化", "水平", "変位", "∑γ<_cy>H", "[mm]"], fontSize, "center")
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
                    dataRow.Append(CreateTableCell([BoolMark(groundMass.IsLiquefactionLayer)], fontSize, "center"));
                    dataRow.Append(CreateTableCell([$"{groundMass.SigmaZ:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.SigmaZPrime:N1}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.RD:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.N1:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.DeltaNf:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.NL:N2}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.TauLonSigmaZPrime:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.TauDonSigmaZPrime[i]:N3}"], fontSize, "right"));
                    dataRow.Append(CreateTableCell([$"{groundMass.FL[i]:N2}"], fontSize, "right", bold: true));
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
                CreateTableCell([$"レベル{i + 1}", "地盤の", "水平変位", "D<_max> u<^*>", "+∑γ<_cy>H", "[mm]"], fontSize, "center")
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
                    dataRow.Append(CreateTableCell([BoolMark(groundMass.IsEngineeringBedrock)], fontSize, "center"));
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
        public static void AddLoadCasesTable(Body body, LoadCasesInput loadCasesInput, FundamentalInput fundamentalInput)
        {
            // レベル1の表を追加
            AddLoadCaseTable(body, loadCasesInput.LoadCasesLevel1, fundamentalInput);
            AddText(body, "");
            // レベル2の表を追加
            AddLoadCaseTable(body, loadCasesInput.LoadCasesLevel2, fundamentalInput);

        }

        // 杭体の表を追加するメソッド
        public static void AddPileBodiesTables(Body body, ObservableCollection<PileBodyInput> pileBodies)
        {
            int pileBodyNo = 0;
            foreach (PileBodyInput pileBodyInput in pileBodies)
            {
                pileBodyNo++;
                AddPileBodyDetailedTable(body, pileBodyInput, pileBodyNo);
            }
        }

        // 杭体の詳細情報を含む表を追加するメソッド
        public static void AddPileBodyDetailedTable(Body body, PileBodyInput pileBodyInput, int pileBodyNo)
        {
            double fontSize = 8;

            // 杭体No.のヘッダー
            var headerPara = new Paragraph();
            headerPara.Append(new Run(
                new RunProperties { Bold = new Bold(), FontSize = new FontSize { Val = "18" }, RunFonts = CreateDefaultRunFonts() },
                new Text($"杭体No.{pileBodyNo}") { Space = SpaceProcessingModeValues.Preserve }));
            body.Append(headerPara);

            // 杭先端径（最後のセグメントから取得）
            string tipDiameter = "-";
            if (pileBodyInput.PileBodySegments != null && pileBodyInput.PileBodySegments.Count > 0)
            {
                var lastSegment = pileBodyInput.PileBodySegments[^1];
                if (lastSegment?.PileSection != null)
                {
                    tipDiameter = $"{lastSegment.PileSection.PileDiameter:N0}";
                }
            }

            // 杭体基本情報テーブル（杭体符号、杭体タイプ、杭頭接合タイプ、杭工法、杭先端径）
            Table infoTable = CreateTableWithBorders();

            TableRow refRow = new();
            refRow.Append(CreateTableCell(["杭体符号"], fontSize, "left"));
            refRow.Append(CreateTableCell([pileBodyInput.PileBodyRef ?? "-"], fontSize, "left"));
            infoTable.Append(refRow);

            TableRow typeRow = new();
            typeRow.Append(CreateTableCell(["杭体タイプ"], fontSize, "left"));
            typeRow.Append(CreateTableCell([pileBodyInput.PileBodyType ?? "-"], fontSize, "left"));
            infoTable.Append(typeRow);

            TableRow topTypeRow = new();
            topTypeRow.Append(CreateTableCell(["杭頭接合タイプ"], fontSize, "left"));
            topTypeRow.Append(CreateTableCell([pileBodyInput.PileTopType ?? "-"], fontSize, "left"));
            infoTable.Append(topTypeRow);

            TableRow constructionRow = new();
            constructionRow.Append(CreateTableCell(["杭工法"], fontSize, "left"));
            constructionRow.Append(CreateTableCell([pileBodyInput.PileConstructionType ?? "-"], fontSize, "left"));
            infoTable.Append(constructionRow);

            TableRow tipRow = new();
            tipRow.Append(CreateTableCell(["杭先端径 [mm]"], fontSize, "left"));
            tipRow.Append(CreateTableCell([tipDiameter], fontSize, "left"));
            infoTable.Append(tipRow);

            body.Append(infoTable);

            // 「杭体区間」ラベル
            AddText(body, "杭体区間", "left", 9);

            // 杭体区間テーブル
            AddPileBodySegmentTable(body, pileBodyInput);

            // 杭体間のスペース
            AddLineBreak(body);
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
            CreateTableCell(["下端深度", "[m]"], fontSize, "center"),
            CreateTableCell(["区間長", "[m]"], fontSize, "center"),
            CreateTableCell(["断面タイプ"], fontSize, "center"),
            CreateTableCell(["杭径", "[mm]"], fontSize, "center")
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
        public static void AddPileLayoutTables(Body body, ObservableCollection<PileLayoutDataItem> pileLayoutItems, FundamentalInput fundamentalInput)
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
                dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceVL0:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{pileLayoutData.AxialForceVLAdditional:N0}"], fontSize, "right"));

                static string AF(IList<double> list, int idx) => (list != null && idx >= 0 && idx < list.Count) ? $"{list[idx]:N0}" : string.Empty;

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

                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 0)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 1)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 2)], fontSize, "center"));
                dataRow.Append(CreateTableCell([FrontMark(pileLayoutData.IsFrontPiles, 3)], fontSize, "center"));
                table.Append(dataRow);
            }

            body.Append(table);
        }

        // 前後方杭の表を追加するメソッド
        public static void AddEmbedment(Body body, EmbedmentInput embedmentInput, FundamentalInput fundamentalInput)
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

        private static string BoolMark(bool value) => value ? "○" : "×";

        private static string FrontMark(ObservableCollection<bool> src, int idx)
            => (src != null && idx >= 0 && idx < src.Count) ? (src[idx] ? "前" : "後") : string.Empty;

        // 杭抵抗
        private static void AddPileResistanceDescription(Body body, ObservableCollection<SoilPile> soilPiles, FundamentalInput fundamentalInput)
        {
            AddHeader1(body, "杭支持力の検討", 3);
            AddText(body, "杭支持力の検討では、杭の押込側と引抜側の使用限界、損傷限界、終局限界を計算し、表にまとめます。");
            AddText(body, "以下に杭支持力一覧表を示します。");

            AddText(body, "【先端支持力】");

            // 先端支持力の一覧表
            {
                double fs = 8;
                Table tipTable = CreateTableWithBorders();
                TableRow tipHeader = CreateHeaderRow(
                    CreateTableCell(["杭体番号"], fs, "center"),
                    CreateTableCell(["杭先端径", "[mm]"], fs, "center"),
                    CreateTableCell(["杭先端", "標高", "[m]"], fs, "center"),
                    CreateTableCell(["杭先端", "Z", "[m]"], fs, "center"),
                    CreateTableCell(["N値算定", "上端", "標高", "[m]"], fs, "center"),
                    CreateTableCell(["N値算定", "上端", "Z", "[m]"], fs, "center"),
                    CreateTableCell(["N値算定", "下端", "標高", "[m]"], fs, "center"),
                    CreateTableCell(["N値算定", "下端", "Z", "[m]"], fs, "center"),
                    CreateTableCell(["算定対象N値"], fs, "center"),
                    CreateTableCell(["平均N値"], fs, "center")
                );
                tipTable.Append(tipHeader);

                foreach (SoilPile soilPile in soilPiles)
                {
                    string pileRef = soilPile.PileBodyInput?.PileBodyRef ?? "不明";
                    string toeDia = soilPile.PileBodyInput?.PileBodySegments?.Count > 0
                        ? $"{soilPile.PileBodyInput.PileToeDia:N0}" : "不明";
                    bool hasSeg = soilPile.PileBodyInput?.PileBodySegments?.Count > 0;
                    string toeAlt = hasSeg ? $"{fundamentalInput.ToAbsolute(soilPile.PileBottomAltitude):N3}" : "不明";
                    string toeZ = hasSeg ? $"{soilPile.PileBottomAltitude:N3}" : "不明";

                    TableRow row = new();
                    row.Append(CreateTableCell([$"{pileRef}"], fs, "center"));
                    row.Append(CreateTableCell([$"{toeDia}"], fs, "right"));
                    row.Append(CreateTableCell([$"{toeAlt}"], fs, "right"));
                    row.Append(CreateTableCell([$"{toeZ}"], fs, "right"));
                    row.Append(CreateTableCell([$"{fundamentalInput.ToAbsolute(soilPile.PileToeNValueAverageRangeUpperAltitude):N3}"], fs, "right"));
                    row.Append(CreateTableCell([$"{soilPile.PileToeNValueAverageRangeUpperAltitude:N3}"], fs, "right"));
                    row.Append(CreateTableCell([$"{fundamentalInput.ToAbsolute(soilPile.PileToeNValueAverageRangeLowerAltitude):N3}"], fs, "right"));
                    row.Append(CreateTableCell([$"{soilPile.PileToeNValueAverageRangeLowerAltitude:N3}"], fs, "right"));
                    row.Append(CreateTableCell([$"{soilPile.NValuesForAverage}"], fs, "center"));
                    row.Append(CreateTableCell([$"{soilPile.PileToeNValue:N1}"], fs, "right"));
                    tipTable.Append(row);
                }

                body.Append(tipTable);
                AddLineBreak(body);
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
        public static void AddVerticalResistance(Body body, ObservableCollection<SoilPile> soilPiles, FundamentalInput fundamentalInput)
        {
            double fontSize = 8;
            Table table = CreateTableWithBordersAndWidths(GetEqualColumnWidths(14));

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["No"], fontSize, "center"),
                CreateTableCell(["杭体", "番号"], fontSize, "center"),
                CreateTableCell(["地盤", "番号"], fontSize, "center"),
                CreateTableCell(["杭工法"], fontSize, "center"),
                CreateTableCell(["杭頭径", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端径", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端", "標高", "[m]"], fontSize, "center"),
                CreateTableCell(["杭先端", "Z", "[m]"], fontSize, "center"),
                CreateTableCell(["押込側", "使用", "限界", "R_{SLS}", "[kN]"], fontSize, "center"),
                CreateTableCell(["押込側", "損傷", "限界", "R_{DLS}", "[kN]"], fontSize, "center"),
                CreateTableCell(["押込側", "終局", "限界", "R_{ULS}", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "使用", "限界", "R_{t,SLS}", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "損傷", "限界", "R_{t,DLS}", "[kN]"], fontSize, "center"),
                CreateTableCell(["引抜側", "終局", "限界", "R_{t,ULS}", "[kN]"], fontSize, "center"));

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
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileBodySegments[0].PileSection?.PileDiameter ?? 0:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileToeDia:N0}"], fontSize, "right"));
                dataRow.Append(CreateTableCell([$"{fundamentalInput.ToAbsolute(soilPile.PileBottomAltitude):N3}"], fontSize, "right"));
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
    }
}
