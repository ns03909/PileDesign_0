using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

namespace PileDesign.Output
{
    /// <summary>
    /// 杭モデルの図に描き足す境界条件の記号 (<see cref="DiagramRenderer.RenderPileForceElevationPngBytes"/>)。
    /// </summary>
    /// <param name="HeadRotationSpring">杭頭の回転 (Rx・Ry) を回転ばねで支える (false は剛接合)</param>
    /// <param name="TipVerticalFixed">杭先端の鉛直変位を固定する (false は各節点の鉛直地盤ばねで支える)</param>
    /// <param name="AxialForceAsLoad">杭の軸力を外力として与える (false は M-φ 関係にだけ用いる)</param>
    internal sealed record PileModelAnnotation(bool HeadRotationSpring, bool TipVerticalFixed, bool AxialForceAsLoad);

    /// <summary>計算書の「解析モデルの境界条件」の表の 1 行。</summary>
    internal sealed record PileModelBoundaryRow(string Position, string Restraint, string Action);

    // 「水平抵抗解析モデル」章: 1 本の杭に注目した境界条件の表と、杭モデルの図の記号。
    // 利用者の声 (2026-11-07): 単杭の解析モデル図 (杭頭・杭・地盤ばねの外側・杭先端の拘束、強制変位、設計用の荷重) が既定で欲しい。
    // 表も図の記号も、解析のモデル化 (AnalysisModelling) と同じ規則を読む。手で書くと設定を変えたときに取り残される
    internal partial class WordDocument
    {
        private const string SemiRigidCaptain = "キャプテンパイル工法";
        private const string SemiRigidFtPile = "FT-Pile構法";
        private const string SemiRigidCapring = "キャプリングパイル工法";

        /// <summary>
        /// 杭頭の回転 (Rx・Ry) の支え方。解析 (SetupNonlinearMThetaForLoadCase) と同じ分け方:
        /// 鉄筋定着工法の場所打ちRC杭・鋼管杭は基本設定「杭頭回転角」に従い、半剛接合の 3 工法は杭を非線形とする荷重ケースで M-θ、
        /// それ以外は剛接合。
        /// </summary>
        internal static (bool Spring, string Text) PileHeadRotation(PileBodyInput? body, bool considerHeadRotation, bool anyNonlinearCase)
        {
            if (body == null) return (false, "剛接合（回転を固定）");
            string top = body.PileTopType ?? "";
            string type = body.PileBodyType ?? "";
            if (body.HasSelectablePileHeadRotation())
            {
                if (!considerHeadRotation) return (false, "剛接合（基本設定で杭頭回転角を考慮しない）");
                return type.Contains(PileTypeNames.SteelPipe)
                    ? (true, "接合部の回転剛性 Kθ の回転ばね（線形）")
                    : (true, "杭頭の M-θ 関係（トリリニア）の回転ばね。軸力が適用範囲の外では剛接合");
            }
            string? method = new[] { SemiRigidCaptain, SemiRigidFtPile, SemiRigidCapring }.FirstOrDefault(top.Contains);
            if (method != null)
            {
                return anyNonlinearCase
                    ? (true, $"{method}の M-θ 関係の回転ばね（杭を非線形とする荷重ケース。線形とするケースは剛接合）")
                    : (false, $"剛接合（{method}。M-θ 関係は杭を非線形とする荷重ケースで用いる）");
            }
            return (false, "剛接合（回転を固定）");
        }

        private static bool AnyPileNonlinearCase(InputModel input)
            => (input.LoadCasesInput?.LoadCasesLevel1 ?? []).Concat(input.LoadCasesInput?.LoadCasesLevel2 ?? [])
                .Any(lc => lc != null && lc.IsPileNonLinear);

        private static bool IsRigidFloor(InputModel input)
            => input.FoundationBeamInput?.ConnectionMode == FoundationBeamConnectionMode.RigidFloor
               && input.FoundationBeamInput.Beams is { Count: > 0 };

        /// <summary>杭モデルの図に描き足す記号。</summary>
        internal static PileModelAnnotation PileModelAnnotationFor(InputModel input, SoilPile soilPile)
        {
            bool consider = input.FundamentalInput?.ConsiderPileHeadRotation ?? true;
            bool springs = AnalysisModelling.AppliesVerticalSpringsToPile(input, soilPile);
            return new PileModelAnnotation(
                PileHeadRotation(soilPile.PileBodyInput, consider, AnyPileNonlinearCase(input)).Spring,
                TipVerticalFixed: !springs,
                AxialForceAsLoad: input.UsePsSpringAtPileTip);
        }

        /// <summary>
        /// 解析モデルの境界条件の表の行。杭体・地盤の組 (土層-杭セット) によって違う項目 (杭頭の回転・杭先端) は、
        /// 組が複数で中身が違うときだけ組ごとに書き分ける。
        /// </summary>
        internal static List<PileModelBoundaryRow> PileModelBoundaryRows(InputModel input, IReadOnlyList<SoilPile> soilPiles)
        {
            var rows = new List<PileModelBoundaryRow>();
            bool rigidFloor = IsRigidFloor(input);
            bool axialAsLoad = input.UsePsSpringAtPileTip;

            // 基礎 (慣性力中心)
            var ap = AnalysisModelling.ActionPointBoundaryFor(input);
            string apRestraint;
            if (rigidFloor)
                apRestraint = "鉛直 (Dz)・回転 (Rx・Ry) を固定（剛床連結。鉛直と回転は基礎梁が負担）"
                    + (ap.Rz ? "。ねじれ (Rz) も固定" : "");
            else if (ap.Rx && ap.Ry)
                apRestraint = "回転 (Rx・Ry・Rz) を固定（杭が一列に並ぶ、または 1 本のため）";
            else if (ap.Rz)
                apRestraint = "ねじれ (Rz) を固定（基礎のねじれを拘束）";
            else
                apRestraint = "拘束なし";
            rows.Add(new("基礎（慣性力中心）", apRestraint,
                "水平力 H = βU·Ps + βL·Pf を荷重の方向に作用させる（荷重ステップごとに漸増）"));

            // 杭頭
            bool consider = input.FundamentalInput?.ConsiderPileHeadRotation ?? true;
            bool anyNonlinear = AnyPileNonlinearCase(input);
            string follow = rigidFloor
                ? "水平 (Dx・Dy)・鉛直 (Dz)・ねじれ (Rz) は杭頭の節点 (基礎梁の節点) と一体に動く"
                : "水平 (Dx・Dy)・鉛直 (Dz)・ねじれ (Rz) は基礎と一体に動く（全杭頭を慣性力中心に剛体連結）";
            string rotation = PerSoilPile(soilPiles, sp => PileHeadRotation(sp.PileBodyInput, consider, anyNonlinear).Text);
            rows.Add(new("杭頭", $"{follow}。回転 (Rx・Ry): {rotation}",
                axialAsLoad
                    ? "杭の軸力 N を外力として作用させる（常時の軸力から地震時の軸力まで荷重ステップごとに漸増）"
                    : "外力なし（杭の軸力 N は杭の M-φ 関係を求めるのに用いる。「計算条件・仮定」の杭軸力の扱いを参照）"));

            rows.Add(new("杭の節点", "拘束なし（6 自由度とも自由）",
                axialAsLoad ? "杭体の自重" : "—"));

            rows.Add(new("水平地盤ばねの外側の節点", "鉛直 (Dz)・回転 (Rx・Ry・Rz) を固定",
                "水平 (Dx・Dy) に、地盤変位 × αL を強制変位として与える（荷重ステップごとに漸増）"));

            if (input.ElementDivision?.HasEarthPressureSprings() == true)
            {
                rows.Add(new("根入部の土圧合力ばねの外側の節点", "鉛直 (Dz)・回転 (Rx・Ry・Rz) を固定",
                    "水平 (Dx・Dy) に、地盤変位 × αL を強制変位として与える"));
            }

            string tip = PerSoilPile(soilPiles, sp => AnalysisModelling.AppliesVerticalSpringsToPile(input, sp)
                ? "拘束なし（杭の各節点の鉛直地盤ばね (P-S 非線形) で支える）"
                : "鉛直 (Dz) を固定");
            rows.Add(new("杭先端", tip, "—"));
            return rows;
        }

        // 土層-杭セットごとに値が違うときだけ「杭体 A: … / 杭体 B: …」と書き分ける
        private static string PerSoilPile(IReadOnlyList<SoilPile> soilPiles, System.Func<SoilPile, string> text)
        {
            var list = soilPiles.Where(s => s != null).ToList();
            if (list.Count == 0) return "";
            var distinct = list.Select(text).Distinct().ToList();
            if (distinct.Count == 1) return distinct[0];
            return string.Join("\n", list
                .GroupBy(text)
                .Select(g => $"{string.Join("・", g.Select(SoilPileName).Distinct())}: {g.Key}"));
        }

        private static string SoilPileName(SoilPile sp)
            => !string.IsNullOrWhiteSpace(sp.PileBodyInput?.PileBodyRef) ? $"杭体 {sp.PileBodyInput.PileBodyRef}" : $"杭体{sp.PileBodyNo}";

        /// <summary>「水平抵抗解析モデル」の見出し・説明・境界条件の表。</summary>
        private void AddPileModelBoundarySection(Body body, IReadOnlyList<SoilPile> soilPiles)
        {
            AddHeader1(body, "水平抵抗解析モデル", 1);
            AddText(body,
                "水平抵抗の解析は、杭・基礎・地盤ばねを一体とした立体の骨組で行う。1 本の杭に注目したときの境界条件と作用を下表に、"
                + "杭体と地盤の組ごとの杭モデルを次の図に示す（D は変位、R は回転。X・Y は水平、Z は鉛直の向き）。");
            AddTableCaption(body, "解析モデルの境界条件");

            const double fontSize = 8;
            int w1 = 2200, w2 = 4200, w3 = 3600;
            Table table = CreateTableWithBordersAndWidths(w1, w2, w3);
            table.Append(CreateHeaderRow(
                CreateTableCellWithWidth("位置", "center", w1, fontSize),
                CreateTableCellWithWidth("拘束", "center", w2, fontSize),
                CreateTableCellWithWidth("作用・強制変位", "center", w3, fontSize)));
            foreach (var r in PileModelBoundaryRows(inputModel, soilPiles))
            {
                TableRow row = new();
                row.Append(CreateTableCellWithWidth(r.Position, "left", w1, fontSize));
                row.Append(CreateTableCellWithWidth(r.Restraint, "left", w2, fontSize));
                row.Append(CreateTableCellWithWidth(r.Action, "left", w3, fontSize));
                table.Append(row);
            }
            body.Append(table);
        }
    }
}
