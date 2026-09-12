using System.Collections.Generic;

namespace PileDesign.Models
{
    /// <summary>
    /// この計算書が依る文献の 1 件。
    /// </summary>
    /// <param name="Publisher">発行者 (学会・協会・官庁・メーカー)。</param>
    /// <param name="Title">書名・規準名。</param>
    /// <param name="Edition">版・公布年など、版を特定する表記。</param>
    /// <param name="UsedFor">この計算書のどこで用いているか。1 文で書く。</param>
    /// <param name="Kind">分類。参考文献の表を並べる順でもある。</param>
    public sealed record DesignReference(
        string Publisher, string Title, string Edition, string UsedFor, ReferenceKind Kind);

    /// <summary>参考文献の分類。表はこの順に並べる。</summary>
    public enum ReferenceKind
    {
        /// <summary>設計の基本とする指針・規準。常に載せる。</summary>
        Guideline = 0,

        /// <summary>法令・告示と、その解説書。常に載せる。</summary>
        Regulation = 1,

        /// <summary>杭頭工法・杭種ごとの評定書やマニュアル。使っているときだけ載せる。</summary>
        Method = 2,
    }

    /// <summary>
    /// 参考文献の一覧。<b>定義はここだけ</b>に置く。
    ///
    /// <para>計算書 (docx) の「参考文献」表がここから作られ、ヘルプの「準拠指針・参考文献」の
    /// タイルと同じ内容であることをテスト (<c>ReferenceCatalogTests</c>) が見張る。
    /// 一覧を 2 か所に手で書くと、片方だけ増えて取り残される (このリポジトリで繰り返している形)。</para>
    ///
    /// <para>工法の文献 (<see cref="ReferenceKind.Method"/>) は、そのモデルで実際に使っている
    /// ときだけ載せる。使っていない工法のマニュアルを並べると、何に依った計算書なのかが読めなくなる。</para>
    /// </summary>
    public static class ReferenceCatalog
    {
        /// <summary>設計の基本とする指針・規準 (常に載せる)。</summary>
        public static IReadOnlyList<DesignReference> Guidelines { get; } =
        [
            new("日本建築学会", "建築基礎構造設計指針", "第3版、2019年",
                "水平地盤反力係数・塑性水平地盤反力・群杭の影響・地盤変位・液状化の低減率・支持力の算定",
                ReferenceKind.Guideline),
            new("日本建築学会", "基礎部材の強度と変形性能", "第1版、2022年",
                "杭体の限界状態 (使用・損傷・安全) の耐力、解析用 M-φ の折線、低減係数 β1・β2、鋼管杭の座屈長",
                ReferenceKind.Guideline),
            new("日本建築学会", "建築基礎構造設計例集", "第3版、2024年",
                "算定手順の照合に用いた設計例 (同梱の計算例)",
                ReferenceKind.Guideline),
            new("日本建築学会 関東支部", "基礎構造の設計　学びやすい構造設計", "第4版、2023年",
                "算定手順の照合に用いた設計例 (同梱の計算例)",
                ReferenceKind.Guideline),
        ];

        /// <summary>法令・告示とその解説書 (常に載せる)。</summary>
        public static IReadOnlyList<DesignReference> Regulations { get; } =
        [
            new("国土交通省", "平成13年国土交通省告示第1113号 (第8)", "最終改正 令和2年",
                "場所打ちコンクリート杭の長期・短期許容応力度 (基本設定で告示による算定を選んだ場合)",
                ReferenceKind.Regulation),
            new("国土交通省住宅局建築指導課ほか監修", "建築物の構造関係技術基準解説書", "2025年版",
                "許容応力度の扱い (付録1-3) と限界状態の呼称",
                ReferenceKind.Regulation),
        ];

        /// <summary>
        /// 杭頭工法・工法ごとの文献。<b>使っているときだけ</b>載せる。
        /// 鍵は判定に使う識別子で、計算書側が使用の有無を判断する。
        /// </summary>
        public static IReadOnlyDictionary<string, DesignReference> Methods { get; } =
            new Dictionary<string, DesignReference>
            {
                ["キャプテンパイル工法"] = new(
                    "キャプテンパイル工法協会",
                    "キャプテンパイル工法 (場所打ち杭用杭頭半固定構法) 設計・施工マニュアル",
                    "第6版、2026年4月改定",
                    "杭頭半固定接合の回転剛性 M-θ と許容回転角",
                    ReferenceKind.Method),
                ["F.T.Pile構法"] = new(
                    "F.T.Pile構法既製杭協会",
                    "F.T.Pile構法既製コンクリート杭　設計・施工指針【暫定版】",
                    "BCJ評定-FD0141-05、2018年12月",
                    "既製杭の杭頭半固定接合の回転剛性と許容値",
                    ReferenceKind.Method),
                ["キャプリングパイル工法"] = new(
                    "一般社団法人キャプリングパイル工法協会 (CAPIA)",
                    "キャプリングパイル工法 設計マニュアル",
                    "2023年4月版",
                    "杭頭半固定接合の回転剛性と許容値",
                    ReferenceKind.Method),
                ["KCTB"] = new(
                    "日本建築センター",
                    "KCTB 場所打ち鋼管コンクリート杭 (TB工法) 建設技術審査証明",
                    "BCJ評定-FD0356-08",
                    "TB工法の設計法 (本体部の耐力の累加、適用範囲)",
                    ReferenceKind.Method),
            };

        /// <summary>常に載せる文献 (指針・規準 → 法令)。</summary>
        public static IEnumerable<DesignReference> Always()
        {
            foreach (var r in Guidelines) yield return r;
            foreach (var r in Regulations) yield return r;
        }

        /// <summary>全件 (テストとヘルプの突合用)。</summary>
        public static IEnumerable<DesignReference> All()
        {
            foreach (var r in Always()) yield return r;
            foreach (var r in Methods.Values) yield return r;
        }
    }
}
