using System.Collections.Generic;

namespace TestProject1.LiteratureVerification
{
    /// <summary>
    /// 検証の範囲を分ける計算の分野。照合 1 件ごとの分野は <see cref="LiteratureCatalog.SectionInfos"/> が持つ。
    /// </summary>
    public enum VerificationArea
    {
        Liquefaction,
        GroundDisplacement,
        LateralResponse,
        PileFlexure,
        PileShear,
        TipBearing,
        Settlement,
        PileHeadMethod,
    }

    /// <summary>
    /// 照合 1 か所 (出典の同じ箇所) の説明。何を比べたか (比較対象) と、どの条件で比べたか (入力条件)。
    /// </summary>
    public sealed record SectionInfo(VerificationArea Area, string Target, string Condition);

    /// <summary>
    /// 分野ごとの検証の範囲 (外部レビュー #28 2026-11-21)。
    ///
    /// <para>照合件数が多くても、すべての杭種・境界条件・非線形の経路を検証したことにはならない。
    /// 分野ごとに「文献の数値と自動で照合しているもの」(件数はカタログから数える)・「設計例と図や表で比べたもの」
    /// (verification.html の節)・「照合していない範囲」を並べ、検証ウィンドウと README に書き出す。</para>
    ///
    /// <para>図・表で比べた節の名前は verification.html の節の見出しと一致することを
    /// <see cref="VerificationTableTests"/> が確かめる。「照合していない範囲」は手で書く文で、
    /// カタログに照合を足したときはここも見直すこと。</para>
    /// </summary>
    public static class VerificationScope
    {
        public sealed record Row(VerificationArea Area, string Name, string[] FigureSections, string NotVerified);

        public static readonly IReadOnlyList<Row> Rows =
        [
            new(VerificationArea.Liquefaction, "液状化判定 (FL・繰返しせん断ひずみ)",
                ["基礎指針'19 計算例1", "基礎指針'19 計算例3", "設計例 3.4 ― SC杭 液状化"],
                "自動照合は計算例1 の γcy の 5 点だけ。FL・液状化による地盤反力係数の低減率 βLq は図・表での比較のみ。"),
            new(VerificationArea.GroundDisplacement, "地盤変位 (応答変位法)",
                ["基礎指針'19 計算例2", "基礎指針'19 計算例3", "設計例 3.1 ― SC杭, PRC杭", "設計例 3.2 ― PHC杭", "関東支部 計算例8"],
                "自動照合は計算例2 の地表面の 1 点だけ。深さ方向の分布は図での比較のみ。応答スペクトル法 (MDOF・等価線形化) の地盤変位は照合していない (検証ページの計算例3 は「確認中」)。"),
            new(VerificationArea.LateralResponse, "杭の水平解析 (非線形の応力・変位)",
                ["基礎指針'19 計算例9", "基礎指針'19 計算例10", "設計例 3.1 ― SC杭, PRC杭", "設計例 3.2 ― PHC杭", "設計例 3.3 ― 場所打ち杭", "設計例 3.4 ― SC杭 液状化", "関東支部 計算例8"],
                "文献の数値との自動照合はない。図での比較と、同梱の例題の結果を固定する回帰テスト (変化の見張りで、文献との一致を保証するものではない) のみ。基礎梁あり・剛床以外の接合条件・群杭効果の組合せは、例題にない組み合わせが多い。"),
            new(VerificationArea.PileFlexure, "杭体の曲げ耐力・M-φ 関係",
                ["基礎指針'19 計算例9", "基礎指針'19 計算例10"],
                "文献の数値との自動照合はない。既製杭はメーカーのカタログの Mcr・Mu との突合テスト (PrecastCatalogCrackMomentTests) がある。場所打ち杭・鋼管杭 (杭頭部の検討式を含む) の耐力は図での比較のみ。"),
            new(VerificationArea.PileShear, "杭体のせん断耐力",
                [],
                "自動照合はエムケーパイルリング785 (場所打ち RC 杭) の使用限界・損傷限界の 4 件だけ。ほかの杭種・工法と終局限界のせん断耐力は文献の数値と照合していない。"),
            new(VerificationArea.TipBearing, "鉛直支持力 (杭先端)",
                ["基礎指針'19 計算例8", "設計例 3.1 ― SC杭, PRC杭", "関東支部 計算例8"],
                "自動照合は Smart-MAGNUM・Hybrid ニーディングの先端支持力係数 α と計算例の Rpa だけ。基礎指針の一般式による支持力と引抜き抵抗は図・表での比較のみ。ほかの工法は照合していない。"),
            new(VerificationArea.Settlement, "沈下 (単杭・群杭・即時・圧密)",
                ["基礎指針'19 計算例5", "基礎指針'19 計算例6", "基礎指針'19 計算例7", "基礎指針'19 計算例8", "基礎構造の設計 計算例7", "設計例 3.3 ― 場所打ち杭", "関東支部 計算例8"],
                "文献の数値との自動照合はない。図・表での比較のみ。"),
            new(VerificationArea.PileHeadMethod, "杭頭工法 (FT・キャプテン・キャプリングなど)",
                [],
                "文献・カタログとの照合はない。各工法の資料の式を実装し、式の形・境界の連続性を単体テストで確かめているだけ。"),
        ];
    }
}
