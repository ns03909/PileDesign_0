using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Output
{
    // 計算書の末尾に置く「参考文献」と「用語・記号」の章。
    //
    // 第三者が計算書だけを見て何に依った計算かを追えるようにするための章で、
    // 計算書レベル (簡易/詳細) に依らず常に出力する (仮定の章と同じ扱い)。
    internal partial class WordDocument
    {
        /// <summary>
        /// 用語・記号の一覧。<b>定義はここだけ</b>。
        ///
        /// <para>本文に出てくる語のうち、読み手が取り違えると結論を読み違えるものを並べる。
        /// 限界状態の呼び名は基本設定の告示オプションで変わるので、
        /// <see cref="ConcreteModelOptions.MapLimitStateText"/> を通して書く。</para>
        /// </summary>
        internal static IReadOnlyList<(string Term, string Meaning)> GlossaryRows() =>
        [
            ("使用限界状態",
             ConcreteModelOptions.MapLimitStateText(
                 "常時の荷重に対し、杭体が弾性の範囲に収まることを確かめる限界状態。長期に対応する")),
            ("損傷限界状態",
             ConcreteModelOptions.MapLimitStateText(
                 "レベル1地震時の限界状態。補修せずに使い続けられる範囲に収めることを確かめる")),
            ("安全限界状態",
             "レベル2地震時の限界状態。崩壊しないこと (変形性能を含む) を確かめる"),
            ("kh0 (基準水平地盤反力係数)",
             "相対変位 y0 = 1 cm における水平地盤反力係数。変形係数 E0 と杭径から求め、群杭係数 ξ と液状化の低減率 βL を掛ける"),
            ("py (塑性水平地盤反力度)",
             "水平地盤反力の上限。砂質土は受働土圧に、粘性土は非排水せん断強度 cu と地表からの深さに依る"),
            ("ξ (群杭係数)",
             "群杭で水平地盤反力係数が下がる割合。杭配置ごとに入力し、kh0 に掛かる"),
            ("R/B (杭間隔比)",
             "杭間隔を杭径で割った値。後方杭の py (κ・µ・λ) に入る。未入力の杭は群杭の影響を考えない"),
            ("βL (液状化の低減率)",
             "液状化した土層で水平地盤反力を下げる率。深さごと・地震動レベル別に kh0 と py に掛かる"),
            ("αL・βU・βL (組合せ係数)",
             "杭の応力を重ね合わせるときの係数。順に、地盤変位・上部構造慣性力・基礎部慣性力の最大値に対する比"),
            ("ΔZc (接合-杭頭)",
             "杭配置に入力する接合節点 Z と杭頭の高さの差。杭頭は接合節点より ΔZc 下がった位置になる"),
            ("M-φ 関係",
             "杭体の曲げモーメントと曲率の関係。非線形解析の部材剛性はこの曲線から決まる"),
            ("M-θ 関係",
             "杭頭接合部の曲げモーメントと回転角の関係。半固定接合の回転ばねに用いる"),
            ("地盤変位",
             "地震時に地盤が水平に動く量。杭には強制変位として与える。土質点の値は層の上端に置き、その間を直線で補間する"),
            ("検定比",
             "応答値を限界値で割った値。1.0 を超えると NG"),
            ("収束判定の基準値",
             "非線形反復で残差を割る力の大きさ。既定は外力と強制変位の反力の大きい方 (基本設定で変更できる)"),
        ];

        /// <summary>
        /// 「参考文献」と「用語・記号」の章を出す。計算書の末尾に置く。
        ///
        /// <para>文献の一覧は <see cref="ReferenceCatalog"/> の 1 か所から作る。工法の文献は
        /// そのモデルで実際に使っているものだけを載せる (使っていないマニュアルを並べると、
        /// 何に依った計算書なのかが読めなくなる)。</para>
        /// </summary>
        private void AddReferencesAndGlossarySection(Body body)
        {
            AddPageBreak(body);
            AddHeader1(body, "参考文献", 1);
            AddIntroText(body,
                "本計算書の算定に用いた指針・規準・告示と、その用いどころを示す。"
                + "杭頭工法・工法ごとの文献は、本モデルで使用しているものだけを挙げている。");

            AddTableCaption(body, "参考文献");
            var table = CreateTableWithBordersAndWidths(12, 20, 30, 14, 24);
            table.Append(CreateHeaderRow(
                CreateTableCell(["分類"], 8.0, "center"),
                CreateTableCell(["発行者"], 8.0, "center"),
                CreateTableCell(["書名・規準名"], 8.0, "center"),
                CreateTableCell(["版・公布"], 8.0, "center"),
                CreateTableCell(["本計算書での用いどころ"], 8.0, "center")));

            foreach (var r in CollectReferences())
            {
                var row = new TableRow();
                row.Append(
                    CreateTableCell([KindText(r.Kind)], 8.0, "center"),
                    CreateTableCell([r.Publisher], 8.0, "left"),
                    CreateTableCell([r.Title], 8.0, "left"),
                    CreateTableCell([r.Edition], 8.0, "center"),
                    CreateTableCell([r.UsedFor], 8.0, "left"));
                table.Append(row);
            }
            body.Append(table);

            AddTableNote(body,
                "※ 同梱の計算例による照合の対象と結果は、プログラムの「文献検証」画面に一覧がある。");

            AddHeader1(body, "用語・記号", 1);
            AddIntroText(body,
                "本文で用いる語のうち、取り違えると結論の読み方が変わるものを示す。");

            AddTableCaption(body, "用語・記号");
            var glossary = CreateTableWithBordersAndWidths(26, 74);
            glossary.Append(CreateHeaderRow(
                CreateTableCell(["用語・記号"], 8.0, "center"),
                CreateTableCell(["意味"], 8.0, "center")));

            foreach (var (term, meaning) in GlossaryRows())
            {
                var row = new TableRow();
                row.Append(
                    CreateTableCell([term], 8.0, "left"),
                    CreateTableCell([meaning], 8.0, "left"));
                glossary.Append(row);
            }
            body.Append(glossary);
        }

        /// <summary>載せる文献。常に載せるもの + 使っている工法のもの。</summary>
        private IEnumerable<DesignReference> CollectReferences()
        {
            foreach (var r in ReferenceCatalog.Always()) yield return r;

            var methods = ReferenceCatalog.Methods;
            if (HasCaptainPile() && methods.TryGetValue("キャプテンパイル工法", out var captain))
                yield return captain;
            if (HasFTPile() && methods.TryGetValue("F.T.Pile構法", out var ft))
                yield return ft;
            if (HasCapringPile() && methods.TryGetValue("キャプリングパイル工法", out var capring))
                yield return capring;
            if (ConcreteModelOptions.FollowsKctbEvaluation && methods.TryGetValue("KCTB", out var kctb))
                yield return kctb;
        }

        private static string KindText(ReferenceKind kind) => kind switch
        {
            ReferenceKind.Guideline => "指針",
            ReferenceKind.Regulation => "法令",
            _ => "工法",
        };
    }
}
