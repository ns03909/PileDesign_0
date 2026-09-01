using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    // 「計算条件・仮定」章: 基本設定の材料モデル化オプション・性能グレード・腐食代・kh0 手入力・
    // 単位系/符号規約など、計算に影響する仮定を 1 箇所に集約して明記する。
    // 計算書レベル（簡易/詳細）に依らず常時出力（仮定の記載は計算書の必須情報）。
    internal partial class WordDocument
    {
        /// <summary>「計算条件・仮定」章を出力する。</summary>
        private void AddCalculationAssumptionsSection(Body body, InputModel inputModel)
        {
            AddHeader1(body, "計算条件・仮定", 1);
            AddText(body,
                "本章では、本計算書の解析および断面検定に用いた計算条件・仮定を一覧する。" +
                "各表に示す条件が、本計算に適用したものである。");

            // 解析実行時とオプションが食い違っている場合の警告（旧ファイル等で記録がない場合は照合しない）
            string currentSignature = ConcreteModelOptions.Signature();
            if (anaModel?.ConcreteOptionsSignature is string recorded && recorded != currentSignature)
            {
                AddTableNote(body,
                    "※ 注意: 材料モデル化オプションが水平解析の実行後に変更されています。" +
                    "本計算書の解析結果は変更前のオプションによるものです。再解析のうえ出力し直してください。");
            }

            AddHeader2(body, "単位系・符号規約");
            AddUnitAndSignConventionText(body, inputModel.FundamentalInput);

            AddHeader2(body, "材料モデル化の選択");
            // 計算書は「この計算で何を採ったか」を述べる文書なので、
            // 「既定は」「代替を選択できる」「基本設定画面」のような操作の説明は書かない。
            AddText(body,
                "杭体の材料モデル化は、日本建築学会「基礎部材の強度と変形性能」（第1版、2022年）による。" +
                "項目により、告示 平13国交告第1113号(第8)、" +
                "日本建築学会「鉄筋コンクリート基礎構造部材の耐震設計指針（案）・同解説」（2017）" +
                "またはファイバーモデルを用いる。", fontSize: 9);
            AddAssumptionTable(body, "項目", "選択", "内容", BuildMaterialOptionRows());

            AddHeader2(body, "設計条件");
            AddAssumptionTable(body, "項目", "設定", "内容", BuildDesignConditionRows(inputModel));
            WarnIfAnalysisConditionsChanged(body, inputModel);

            AddLineBreak(body);
        }

        /// <summary>
        /// 解析を実行したときの条件と、いま計算書に書いている条件が食い違っていないか照合する。
        ///
        /// 「設計条件」の表は<b>現在の入力</b>を読む。一方、解析条件のいくつか
        /// (接続方式・基礎のねじれ拘束・杭軸力モード) は切り替えても解析結果が破棄されないので、
        /// 「解析 → 設定変更 → 計算書出力」の順に操作すると、
        /// 解析に使った条件と計算書の記載がずれる。材料モデル化オプションには
        /// 同じ照合が既にある (ConcreteOptionsSignature)。
        /// </summary>
        private void WarnIfAnalysisConditionsChanged(Body body, InputModel inputModel)
        {
            var runConfig = anaModel?.LastRunConfig;
            if (runConfig == null) return;   // 旧いファイル等で記録がない場合は照合しない

            var diffs = new List<string>();

            if (runConfig.RestrainFoundationTorsion != (inputModel?.RestrainFoundationTorsion ?? false))
            {
                diffs.Add($"基礎のねじれ拘束（解析時: {(runConfig.RestrainFoundationTorsion ? "拘束する" : "拘束しない")}）");
            }

            string currentMode = (inputModel?.FoundationBeamInput?.ConnectionMode
                                  ?? FoundationBeamConnectionMode.RigidBody).ToString();
            if (!string.IsNullOrEmpty(runConfig.ConnectionMode) && runConfig.ConnectionMode != currentMode)
            {
                diffs.Add($"杭頭の接続仮定（解析時: {runConfig.ConnectionMode}）");
            }

            if (diffs.Count == 0) return;

            AddTableNote(body,
                "※ 注意: 次の条件が水平解析の実行後に変更されています。"
                + "本計算書の解析結果は変更前の条件によるものです。再解析のうえ出力し直してください — "
                + string.Join("、", diffs));
        }

        /// <summary>単位系・符号規約の宣言文。</summary>
        private static void AddUnitAndSignConventionText(Body body, FundamentalInput fund)
        {
            string refLevel = string.IsNullOrEmpty(fund?.RefLevel) ? "TP" : fund.RefLevel;
            double refAlt = fund?.ReferenceAltitude ?? 0.0;
            AddText(body,
                "本計算書では、特記なき限り力は kN、長さは m、応力度は N/mm² を用いる" +
                "（断面寸法は mm、地盤反力係数は kN/m³）。" +
                "軸力は圧縮を正とし、座標系は Z 軸上向きを正、" +
                $"Z=0 を基準標高（{refLevel} {refAlt:N3} m）とする。");
        }

        /// <summary>
        /// 材料モデル化オプションの一覧行を作成する（ConcreteModelOptions の現在値を読む純関数）。
        /// 「選択」列は本計算に適用された側、「内容」列はその意味の 1 行説明。
        /// </summary>
        internal static List<(string Item, string Choice, string Note)> BuildMaterialOptionRows()
        {
            var rows = new List<(string, string, string)>();

            bool guideline2025 = ConcreteModelOptions.UseNotification1113Compression
                              && ConcreteModelOptions.UseNotification1113Shear;
            rows.Add(("2025年版 技術基準解説書 付録1-3（コンクリート系杭体の許容耐力）",
                guideline2025 ? "準拠" : "個別選択",
                guideline2025
                    ? "使用・損傷限界の許容圧縮/せん断を告示1113(第8) の長期・短期許容応力度で算定する"
                    : "許容圧縮・許容せん断の扱いを下記の項目で個別に選択している"));

            rows.Add(("解析用 M-φ 関係（コンクリート系杭）",
                ConcreteModelOptions.UseFiberMPhi ? "ファイバーモデル" : "指針ポリリニア（既定）",
                ConcreteModelOptions.UseFiberMPhi
                    ? "断面分割積分により各曲率で軸力つり合いを解く。β1・β2 の低減は乗じない素の断面応答" +
                      "（対象: 場所打ち系・PHC・PRC・SC・充填鋼管部。鋼管杭の鋼管部は従来どおり）"
                    : "Mcr-My-β1·Mu0 等の折線でモデル化する（「基礎部材の強度と変形性能」）"));

            rows.Add(("コンクリートの引張強度",
                ConcreteModelOptions.IgnoreTensileStrength ? "無視（σ = 0）" : "考慮（既定）",
                ConcreteModelOptions.IgnoreTensileStrength
                    ? "引張側の応力負担を 0 とする"
                    : "引張強度 ft まで弾性負担を考慮する"));

            rows.Add(("コンクリート圧縮側の折れ点応力度",
                ConcreteModelOptions.UseReducedCompression ? "0.85·Fc" : "ξ·Fc（既定）",
                ConcreteModelOptions.UseReducedCompression
                    ? "バイリニア型の折れ点を 0.85·Fc とする（ξ を乗じない）"
                    : "バイリニア型の折れ点を ξ·Fc（施工品質管理係数 ξ を考慮）とする"));

            rows.Add(("ヤング係数 Ec の算定における ξ",
                ConcreteModelOptions.UseUnitGsiForConcreteE ? "ξ = 1.0" : "実際の ξ（既定）",
                ConcreteModelOptions.UseUnitGsiForConcreteE
                    ? "Ec の算定式のみ σB = Fc として計算する（強度側には実際の ξ を用いる）"
                    : "Ec の算定式に σB = ξ·Fc を用いる"));

            rows.Add(("鉄筋の降伏応力度（場所打ち系）",
                ConcreteModelOptions.RebarYieldAt11F ? "1.1·σy 完全バイリニア" : "規格降伏点 σy（既定）",
                ConcreteModelOptions.RebarYieldAt11F
                    ? "±1.1·σy で頭打ちの完全バイリニア型とする（SD490 を除く）"
                    : "規格降伏点 σy で降伏する完全バイリニア型とする"));

            rows.Add(("鋼管の降伏応力度（場所打ち鋼管コンクリート杭）",
                ConcreteModelOptions.SteelPipeYieldAt11F ? "±1.1F 完全バイリニア" : "ひずみ硬化考慮（既定）",
                ConcreteModelOptions.SteelPipeYieldAt11F
                    ? "±1.1F で頭打ちの完全バイリニア型とする"
                    : "1.1F で降伏し、ひずみ硬化（E/30）と破断応力 σu を考慮する"));

            rows.Add((ConcreteModelOptions.MapLimitStateText("使用限界・損傷限界の許容圧縮応力度（場所打ち系）"),
                ConcreteModelOptions.UseNotification1113Compression
                    ? "告示1113(第8) 長期・短期"
                    : "基礎部材 (1/3)ξFc・(2/3)ξFc（既定）",
                ConcreteModelOptions.UseNotification1113Compression
                    ? "使用限界 = 長期許容圧縮応力度、損傷限界 = 短期許容圧縮応力度（長期の 2 倍）で算定する"
                    : "使用限界 (1/3)ξFc、損傷限界 (2/3)ξFc で算定する"));

            rows.Add((ConcreteModelOptions.MapLimitStateText("使用限界・損傷限界の許容せん断（場所打ちRC杭）"),
                ConcreteModelOptions.UseNotification1113Shear
                    ? "告示1113(第8) Q = fs·b·j"
                    : "基礎部材のせん断耐力式（既定）",
                ConcreteModelOptions.UseNotification1113Shear
                    ? "許容せん断応力度 fs による Q = fs·b·j（軸力・M/(Q·d) 非依存。短期は長期の 1.5 倍）"
                    : "軸力と M/(Q·d) を考慮したせん断耐力式で算定する"));

            bool anyNotification = ConcreteModelOptions.UseNotification1113Compression
                                || ConcreteModelOptions.UseNotification1113Shear;
            rows.Add(("告示1113(第8) 長期許容応力度の区分",
                !anyNotification ? "—"
                    : ConcreteModelOptions.Notification1113CompressionCase == 2 ? "区分 2" : "区分 1",
                !anyNotification ? "告示1113(第8) のオプションを使用していないため対象外"
                    : ConcreteModelOptions.Notification1113CompressionCase == 2
                        ? "圧縮 min(Fc/4.5, 6.0)・せん断 Fc/45（短期は圧縮 2 倍・せん断 1.5 倍）"
                        : "圧縮 Fc/4・せん断 Fc/40（短期は圧縮 2 倍・せん断 1.5 倍）"));

            rows.Add(("安全限界曲げ強度の応力ひずみ関係（場所打ちRC杭／鋼管巻き杭）",
                ConcreteModelOptions.UseInsituUltimateEFunction
                    ? "e関数法" : "バイリニア（既定）",
                ConcreteModelOptions.UseInsituUltimateEFunction
                    ? "RC基礎構造部材の耐震設計指針(案) 5.4.1 の e関数でコンクリートをモデル化する" +
                      "（β1・β2 低減および軸力適用範囲の制限は課さない）"
                    : "バイリニア型でモデル化し、β1・β2 低減と軸力適用範囲を考慮する"));

            rows.Add(("本体部の設計法（場所打ち鋼管コンクリート杭）",
                ConcreteModelOptions.UseFiberNMForSteelPipeConcrete ? "断面分割積分" : "単純累加",
                ConcreteModelOptions.UseFiberNMForSteelPipeConcrete
                    ? "断面を微小要素に分割し、平面保持のもとで応力を積分して許容時 N-M を求める"
                    : "鋼管部と鉄筋コンクリート部の許容耐力を累加する" +
                      "（BCJ評定-FD0356-08 5.(3)、鉄骨鉄筋コンクリート構造計算規準・同解説 2014 4章2節）"));

            rows.Add(("終局の圧縮縁ひずみ εcu（場所打ち鋼管コンクリート杭）",
                ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete ? "5,000μ" : "3,000μ（既定）",
                ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete
                    ? "鋼管によるコンクリートの拘束効果を考慮した値" +
                      "（建設省総合技術開発プロジェクト 基礎WG 最終報告書 資料4-7。BCJ評定-FD0356-08 の評定範囲外）"
                    : "場所打ち系杭に共通の終局圧縮縁ひずみを用いる"));

            rows.Add((ConcreteModelOptions.MapLimitStateText("使用限界・損傷限界の判定材料（場所打ち鋼管コンクリート杭）"),
                ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete
                    ? "コンクリートと鋼管" : "コンクリート・鉄筋・鋼管（既定）",
                ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete
                    ? "鉄筋は限界状態の判定に用いず、耐力への寄与としてのみ考慮する" +
                      "（ジャパンパイル Technical Note Vol.1-5。BCJ評定-FD0356-08 の評定範囲外）"
                    : "断面内の全材料のうち最初に許容ひずみに達したもので限界状態を決める"));

            rows.Add(("限界状態の呼称",
                ConcreteModelOptions.UseAllowableStressLabels ? "長期許容・短期許容" : "使用限界・損傷限界（既定）",
                ConcreteModelOptions.UseAllowableStressLabels
                    ? "告示1113(第8) オプションの使用に伴い、使用限界を「長期許容」、損傷限界を「短期許容」と表記する"
                    : "「基礎部材の強度と変形性能」の限界状態の呼称を用いる"));

            return rows;
        }

        /// <summary>
        /// 設計条件（性能グレード・杭頭接続・腐食代・kh0 手入力）の一覧行を作成する。
        /// </summary>
        internal static List<(string Item, string Value, string Note)> BuildDesignConditionRows(InputModel inputModel)
        {
            var rows = new List<(string, string, string)>();

            string grade = inputModel?.FundamentalInput?.SeismicGrade ?? "A";
            rows.Add(("耐震性能グレード", grade,
                ConcreteModelOptions.MapLimitStateText(
                    grade == "S"
                        ? "レベル2荷重時に基礎部材が損傷限界状態を超えないことを確認する"
                        : "レベル2荷重時に基礎部材が安全限界状態を超えないことを確認する")));

            var mode = inputModel?.FoundationBeamInput?.ConnectionMode ?? FoundationBeamConnectionMode.RigidBody;
            rows.Add(("杭頭の接続仮定",
                mode == FoundationBeamConnectionMode.RigidBody ? "剛体連結" : "剛床連結",
                mode == FoundationBeamConnectionMode.RigidBody
                    ? "全杭頭を代表点に対して全 6 自由度で剛体拘束する（詳細は「検討方針」章）"
                    : "水平変位と鉛直軸回転のみ剛体拘束し、鉛直・回転は基礎梁が負担する（詳細は「検討方針」章）"));

            // 既定 (拘束しない) のときは行を出さない。仮定一覧が長くなるうえ、
            // 「従来どおり」を毎回書いても読み手の判断材料にならない。
            if (inputModel?.RestrainFoundationTorsion == true)
            {
                rows.Add(("基礎のねじれ", "拘束する",
                    "代表点の鉛直軸回りの回転を拘束する。基礎はねじれず、杭頭の水平変位は全杭で等しくなる"));
            }

            rows.Add(("鋼管の腐食代", BuildCorrosionSummary(inputModel, out string corrosionNote), corrosionNote));

            rows.Add(("基準水平地盤反力係数 kh0", BuildKh0OverrideSummary(inputModel, out string kh0Note), kh0Note));

            return rows;
        }

        /// <summary>鋼管系断面の腐食代を杭体・区間ごとに要約する。</summary>
        private static string BuildCorrosionSummary(InputModel inputModel, out string note)
        {
            var entries = new List<string>();
            int pileBodyNo = 0;
            foreach (var pileBody in inputModel?.PileBodies ?? [])
            {
                pileBodyNo++;
                int segNo = 0;
                foreach (var seg in pileBody.PileBodySegments ?? [])
                {
                    segNo++;
                    var sec = seg.PileSection;
                    if (sec == null) continue;
                    bool hasOuterPipe = sec.PileSectionType is PileTypeNames.SteelPipeConcreteSection
                        or PileTypeNames.SteelPipeSection or PileTypeNames.CftSection or PileTypeNames.Sc;
                    if (!hasOuterPipe) continue;
                    entries.Add($"杭体{pileBodyNo}({pileBody.PileBodyRef}) 区間{segNo}: {sec.CorrosionDepth:N1} mm");
                }
            }

            if (entries.Count == 0)
            {
                note = "鋼管を外面に持つ断面がないため対象外";
                return "—";
            }

            note = "解析剛性・断面耐力とも腐食後断面（外径 D−2t・板厚 ts−t）で算定している。" +
                   "杭体諸元表の杭径は公称（腐食前）外径で表示している。";
            return string.Join("\n", entries);
        }

        /// <summary>kh0 手入力オーバーライドの有無と内容を要約する。</summary>
        private static string BuildKh0OverrideSummary(InputModel inputModel, out string note)
        {
            var entries = new List<string>();
            foreach (var soilPile in inputModel?.ElementDivision?.SoilPiles ?? [])
            {
                foreach (var o in soilPile.Kh0LayerOverrides ?? [])
                {
                    entries.Add($"杭体{soilPile.PileBodyNo}×地盤{soilPile.GroundNo} {o.LayerName}: kh0 = {o.Kh0:N0} kN/m³");
                }
            }

            if (entries.Count == 0)
            {
                note = "全土層とも「基礎指針'19」6.6節に基づき自動算定している";
                return "自動算定";
            }

            note = "以下の土層は要素分割ウィンドウで手入力した kh0 を用いている（記載のない土層は自動算定）";
            return string.Join("\n", entries);
        }

        /// <summary>仮定一覧の 3 列表を出力する。</summary>
        internal static void AddAssumptionTable(
            Body body, string header1, string header2, string header3,
            IEnumerable<(string, string, string)> rows)
        {
            const double fontSize = 8;
            int w1 = 3200, w2 = 2400, w3 = 4400;
            Table table = CreateTableWithBordersAndWidths(w1, w2, w3);

            table.Append(CreateHeaderRow(
                CreateTableCellWithWidth(header1, "center", w1, fontSize),
                CreateTableCellWithWidth(header2, "center", w2, fontSize),
                CreateTableCellWithWidth(header3, "center", w3, fontSize)));

            foreach (var (item, choice, note) in rows)
            {
                TableRow row = new();
                row.Append(CreateTableCellWithWidth(item, "left", w1, fontSize));
                row.Append(CreateTableCellWithWidth(choice, "left", w2, fontSize));
                row.Append(CreateTableCellWithWidth(note, "left", w3, fontSize));
                table.Append(row);
            }

            body.Append(table);
        }
    }
}
