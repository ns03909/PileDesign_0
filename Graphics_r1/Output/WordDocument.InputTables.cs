using DocumentFormat.OpenXml;
using System.Linq;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;
using Text = DocumentFormat.OpenXml.Wordprocessing.Text;

using Serilog;
namespace PileDesign.Output
{
    // 入力データ（基本設定・荷重ケース・地盤・杭体・杭配置・根入部・杭支持力）を
    // Word 表として出力する partial。
    internal partial class WordDocument
    {
        // 液状化判定リスト（FL/βL/γcy 等）が null や長さ不足でもセルを "-" にして、
        // 表全体が例外で欠落しないよう安全に取得するヘルパー。
        private static string SafeListCell(IReadOnlyList<double?> list, int idx, string fmt)
            => (list != null && idx >= 0 && idx < list.Count && list[idx].HasValue)
                ? list[idx]!.Value.ToString(fmt) : "-";
        private static string SafeListCell(IReadOnlyList<double> list, int idx, string fmt)
            => (list != null && idx >= 0 && idx < list.Count) ? list[idx].ToString(fmt) : "-";

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
                Log.Warning(ex, "AddFundamentalTableでエラーが発生しました");
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
            CreateTableCell(["杭", "非線形性"], fontSize, "center"),
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
                dataRow.Append(CreateTableCell([SoilNonlinearityModes.ToShortText(loadCase.SoilNonlinearityMode)], fontSize, "center"));
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

            // 「地盤 非線形性」列の凡例（3 段階の意味）
            AddText(body,
                $"※ 地盤非線形性 … 「{SoilNonlinearityModes.ToShortText(SoilNonlinearityMode.Linear)}」: " +
                "水平地盤反力係数を基準値 kh0（相対変位 y0 = 1cm における値）で一定とする。 " +
                $"「{SoilNonlinearityModes.ToShortText(SoilNonlinearityMode.KhReduction)}」: " +
                "kh = kh0/√(|y|/y0) の変位依存低減を考慮する（塑性地盤反力 py による頭打ちは行わない）。 " +
                $"「{SoilNonlinearityModes.ToShortText(SoilNonlinearityMode.KhReductionWithPy)}」: " +
                "上記に加え地盤反力 p を塑性地盤反力 py で頭打ちとする。");
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
                    CreateTableCell([i == 0 ?$"{groundInput.GroundAcceleration1:F1}[m/s²]" :
                                             $"{groundInput.GroundAcceleration2:F1}[m/s²]"], fontSize, "right")
                ));

                body.Append(table0);
                body.Append(new Paragraph());

                Table table = CreateTableWithBorders();

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
                    dataRow.Append(CreateTableCell([SafeListCell(groundMass.TauDonSigmaZPrime, i, "N3")], fontSize, "right"));
                    // FL < 1.0 (液状化発生) の行は FL / βL / γcy / ΣγcyH を太字で強調。
                    // FL ≥ 1.0 または FL=null の場合は通常表示。
                    bool isLiquefying = groundMass.FL != null && groundMass.FL.Count > i
                        && groundMass.FL[i].HasValue && groundMass.FL[i]!.Value < 1.0;
                    dataRow.Append(CreateTableCell([SafeListCell(groundMass.FL, i, "N2")], fontSize, "right", bold: isLiquefying));
                    dataRow.Append(CreateTableCell([SafeListCell(groundMass.BetaL, i, "N2")], fontSize, "right", bold: isLiquefying));
                    dataRow.Append(CreateTableCell([SafeListCell(groundMass.GammaCy, i, "N2")], fontSize, "right", bold: isLiquefying));
                    dataRow.Append(CreateTableCell([SafeListCell(groundMass.SigmaGammaCyH, i, "N2")], fontSize, "right", bold: isLiquefying));

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

            // 応答スペクトル法選択時の固有値解析結果サマリー
            if (groundInput.CalculationMethod == "応答スペクトル法")
            {
                AddText(body, "応答スペクトル法 固有値解析結果 (収束後)");
                Table tableRsSummary = CreateTableWithBorders();
                tableRsSummary.Append(new TableRow(
                    CreateTableCell([""], fontSize, "center"),
                    CreateTableCell(["T<_1>", "[s]"], fontSize, "center"),
                    CreateTableCell(["T<_2>", "[s]"], fontSize, "center"),
                    CreateTableCell(["β", "(参加係数)"], fontSize, "center"),
                    CreateTableCell(["ξ<_e>", "(等価減衰)"], fontSize, "center"),
                    CreateTableCell(["α<_E>", "(ｲﾝﾋﾟｰﾀﾞﾝｽ比)"], fontSize, "center"),
                    CreateTableCell(["Gs<_1>"], fontSize, "center"),
                    CreateTableCell(["Gs<_2>"], fontSize, "center")));
                for (int lv = 0; lv < 2; lv++)
                {
                    tableRsSummary.Append(new TableRow(
                        CreateTableCell([$"レベル{lv + 1}"], fontSize, "center"),
                        CreateTableCell([$"{groundInput.NaturalPeriods[lv]:N3}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.T2Levels[lv]:N3}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.BetaLevels[lv]:N3}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.XiELevels[lv]:N3}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.ImpedanceLevels[lv]:N3}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.Gs1Levels[lv]:N2}"], fontSize, "right"),
                        CreateTableCell([$"{groundInput.Gs2Levels[lv]:N2}"], fontSize, "right")));
                }
                body.Append(tableRsSummary);
                body.Append(new Paragraph());

                AddText(body, "土質点別 Hardin-Drnevich パラメータ");
                Table tableHd = CreateTableWithBorders();
                tableHd.Append(CreateHeaderRow(
                    CreateTableCell(["深度", "[m]"], fontSize, "center"),
                    CreateTableCell(["土層名"], fontSize, "center"),
                    CreateTableCell(["基準せん断", "ひずみ", "γ<_0.5>"], fontSize, "center"),
                    CreateTableCell(["減衰上限", "h<_max>"], fontSize, "center")));
                foreach (GroundMassDataInput gm in groundMasses)
                {
                    tableHd.Append(new TableRow(
                        CreateTableCell([$"{gm.GLDepth:N3}"], fontSize, "right"),
                        CreateTableCell([$"{gm.Name}"], fontSize, "center"),
                        CreateTableCell([$"{gm.Gamma05:0.00E+00}"], fontSize, "right"),
                        CreateTableCell([$"{gm.HMax:N2}"], fontSize, "right")));
                    if (gm.IsEngineeringBedrock) break;
                }
                body.Append(tableHd);
                body.Append(new Paragraph());
            }

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
                // 杭径は腐食代を見込まない公称外径で表示（鋼管系の有効径 PileDiameter ではなく NominalPileDiameter）
                dataRow.Append(CreateTableCell([$"{pileBodySegment.PileSection.NominalPileDiameter:N0}"], fontSize, "right"));

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
                // 杭径は腐食代を見込まない公称外径で表示
                dataRow2.Append(CreateTableCell([pileBodySegment.PileSection.NominalPileDiameter.ToString()], fontSize, "left"));

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
                // 表ヘッダ「杭頭 Z」に合わせて杭頭 Z (= pile.Z - ΔZc) を出力 (v2 セマンティクス)
                dataRow.Append(CreateTableCell([$"{pileLayoutData.PileHeadZ:N3}"], fontSize, "right"));
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
            AddIntroText(body,
                "本表は各杭の杭頭に作用する鉛直軸力の入力値を示す（圧縮を正、単位 kN）。" +
                "VL は長期（常時）軸力、VL_add は追加軸力で、長期の検討には VL + VL_add を用いる。" +
                "L1-1〜L1-4 はレベル1地震時、L2-1〜L2-4 はレベル2地震時の、" +
                "各加力方向（方向1〜4）における杭軸力である。");

            double fontSize = 8;
            Table table = CreateTableWithBorders();

            // 1行目: 表題
            TableRow headerRow = CreateHeaderRow(
            CreateTableCell(["No"], fontSize, "center"),
            //headerRow.Append(CreateTableCell("X","[m]", fontSize, "center"));
            //headerRow.Append(CreateTableCell("Y","[m]", fontSize, "center"));
            CreateTableCell(["VL", "[kN]"], fontSize, "center"),
            CreateTableCell(["VL<_add>", "[kN]"], fontSize, "center"),
            CreateTableCell(["L1-1", "[kN]"], fontSize, "center"),
            CreateTableCell(["L1-2", "[kN]"], fontSize, "center"),
            CreateTableCell(["L1-3", "[kN]"], fontSize, "center"),
            CreateTableCell(["L1-4", "[kN]"], fontSize, "center"),
            CreateTableCell(["L2-1", "[kN]"], fontSize, "center"),
            CreateTableCell(["L2-2", "[kN]"], fontSize, "center"),
            CreateTableCell(["L2-3", "[kN]"], fontSize, "center"),
            CreateTableCell(["L2-4", "[kN]"], fontSize, "center")
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
            AddIntroText(body,
                "加力方向に対して前面側に位置する杭を前方杭（前）、その背後に位置する杭を後方杭（後）とし、" +
                "加力方向（方向1〜4）ごとに設定する。この区分は群杭の塑性水平地盤反力 py の係数 κ・μ・λ の" +
                "選択に用いられ、後方杭には前方杭より小さい κ（杭間隔比 R/B と内部摩擦角 φ に依存）が適用される" +
                "（詳細は「杭の水平抵抗」章）。");

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
            AddText(body, ConcreteModelOptions.MapLimitStateText("杭支持力の検討では、杭の押込側と引抜側の使用限界、損傷限界、終局限界を計算し、表にまとめます。"));
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
                // 杭頭径も腐食代を見込まない公称外径で表示（区間表・杭体表と統一）
                dataRow.Append(CreateTableCell([$"{soilPile.PileBodyInput.PileBodySegments[0].PileSection?.NominalPileDiameter ?? 0:N0}"], fontSize, "right"));
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

        /// <summary>
        /// Smart-MAGNUM 工法の支持力算定根拠表。該当する杭が無ければ何も出力しない。
        /// 列の並びはカタログ p.10 の計算例と照合しやすい順にしている。
        /// </summary>
        public static void AddSmartMagnumBasisTable(Body body, ObservableCollection<SoilPile> soilPiles)
        {
            var targets = soilPiles?.Where(sp => sp.IsSmartMagnum).ToList();
            if (targets == null || targets.Count == 0) return;

            AddHeader2(body, "Smart-MAGNUM 工法 支持力算定根拠", 1);
            AddText(body,
                "先端支持力は Rp = α・N・Ap（Ap は根固め部に位置する節杭の節部径 Don による面積）、"
                + "周面抵抗は砂質・礫質地盤で β・Ns、粘土質地盤で γ・qu により算定しています。"
                + "一軸圧縮強度 qu は入力の粘着力 Cu から qu = 2Cu として換算しています。"
                + "周面摩擦の算定範囲は杭先端より 0.4m 上（先端支持力評価位置）までです。");

            double fontSize = 8;
            Table table = CreateTableWithBordersAndWidths(GetEqualColumnWidths(13));

            table.Append(CreateHeaderRow(
                CreateTableCell(["No"], fontSize, "center"),
                CreateTableCell(["杭体", "番号"], fontSize, "center"),
                CreateTableCell(["地盤", "番号"], fontSize, "center"),
                CreateTableCell(["周面", "タイプ"], fontSize, "center"),
                CreateTableCell(["節部径", "D_{on}", "[m]"], fontSize, "center"),
                CreateTableCell(["基準", "掘削径", "D_{sn}", "[m]"], fontSize, "center"),
                CreateTableCell(["拡大根", "固め部径", "D_{en}", "[m]"], fontSize, "center"),
                CreateTableCell(["拡大比", "ω_p"], fontSize, "center"),
                CreateTableCell(["LL", "[m]"], fontSize, "center"),
                CreateTableCell(["先端支持", "力係数", "α"], fontSize, "center"),
                CreateTableCell(["N_U", "/ N_L"], fontSize, "center"),
                CreateTableCell(["杭先端", "平均N値", "N"], fontSize, "center"),
                CreateTableCell(["節部有効", "断面積", "A_p [m²]"], fontSize, "center")));

            int i = 0;
            foreach (var sp in targets)
            {
                i += 1;
                TableRow row = new();
                row.Append(CreateTableCell([$"{i}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.PileBodyNo}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.GroundNo}"], fontSize, "right"));
                row.Append(CreateTableCell(
                    [sp.PileBodyInput.SmartMagnumIsReinforcedCircum ? "周面強化型" : "標準型"], fontSize, "center"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumDon:N3}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumDsn:N3}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumDen:N3}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumOmegaP:N2}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumLL:N2}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumAlphaValue:N0}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumNu:N1} / {sp.SmartMagnumNl:N1}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.PileToeNValue:N1}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{sp.SmartMagnumAp:N3}"], fontSize, "right"));
                table.Append(row);
            }
            body.Append(table);
        }
    }
}
