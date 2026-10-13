using System;
using System.Globalization;

namespace PileDesign.FEM
{
    /// <summary>解析モデルの節点の種類 (名前から読み取れるもの)。</summary>
    internal enum FemNodeKind
    {
        /// <summary>ここで付ける名前ではない (dummy 等)。</summary>
        Unknown,
        /// <summary>荷重の作用点 (基礎の剛体の master)。<c>ActionPoint</c></summary>
        ActionPoint,
        /// <summary>土圧合力ばねの根入部節点 (基礎側)。<c>根入部節点</c></summary>
        Embedment,
        /// <summary>土圧合力ばねの根入部地盤節点。<c>根入部地盤節点</c></summary>
        EmbedmentSoil,
        /// <summary>杭頭 (基礎側)。<c>CapNode-{杭番号}</c></summary>
        Cap,
        /// <summary>杭の接合節点 (基礎梁の端)。<c>FoundationNode-P{杭番号}</c></summary>
        PileJoint,
        /// <summary>基礎梁の節点 (杭位置以外)。<c>FoundationNode-{節点番号}</c></summary>
        FoundationBeam,
        /// <summary>一般節点。<c>InputNode-{節点番号}</c></summary>
        Input,
        /// <summary>鉛直梁 (沈下) モデルの地盤節点。<c>GroundNode-P{杭番号}</c></summary>
        Ground,
        /// <summary>杭節点。<c>杭節点-{杭番号}-{節点順}</c></summary>
        Pile,
        /// <summary>杭の地盤節点。<c>杭地盤節点-{杭番号}-{節点順}</c></summary>
        PileSoil,
    }

    /// <summary>
    /// 解析モデル (<see cref="AnalysisModelling"/> / <see cref="VerticalBeamModelling"/>) の節点の名前の
    /// 付け方と読み方。<b>名前の文字列はここにしか書かない。</b>
    ///
    /// <para>解析モデルの節点は杭番号などを別に持たず、名前が識別子を兼ねている。以前は
    /// <c>"FoundationNode-P{No}"</c> / <c>"CapNode-"</c> / <c>"InputNode-"</c> / <c>"杭節点-{no}-0"</c> などを
    /// 20 か所あまりで手で組み立て・読み分けていて、画面 (鉛直梁の結果) には名前を作る関数の行単位の写し、
    /// 変形図には自前の接頭辞の読み分けがあった。どれか 1 か所の書き方が変わると、名前が合わずに
    /// 「節点が見つからない」= 荷重が載らない・線が描かれない、が<b>例外にならずに</b>起きる。
    /// 付け方と読み方を 1 か所に置き、ソースの走査 (<c>FemNodeNamingTests</c>) で他所に文字列が残らないよう見張る。</para>
    ///
    /// <para>杭番号は解析モデルを組んだときの杭配置の番号 (No。解析の前に 1 から連番に揃えている)。
    /// 名前の文字列は以前と 1 文字も変えていない (保存された結果・ログ・自由度キーと同じ)。</para>
    /// </summary>
    internal static class FemNodeNaming
    {
        // ---- 名前の文字列 (ここ以外に書かない) ----
        /// <summary>荷重の作用点の名前。</summary>
        internal const string ActionPoint = "ActionPoint";
        /// <summary>土圧合力ばねの根入部節点の名前。</summary>
        internal const string Embedment = "根入部節点";
        /// <summary>土圧合力ばねの根入部地盤節点の名前。</summary>
        internal const string EmbedmentSoil = "根入部地盤節点";

        private const string CapPrefix = "CapNode-";
        // 接合節点と基礎梁節点は接頭辞を共有する。読むときは長い方 (…-P) を先に見ること。
        private const string PileJointPrefix = "FoundationNode-P";
        private const string FoundationBeamPrefix = "FoundationNode-";
        private const string InputPrefix = "InputNode-";
        private const string GroundPrefix = "GroundNode-P";
        private const string PilePrefix = "杭節点-";
        private const string PileSoilPrefix = "杭地盤節点-";

        // ---- 付け方 ----
        // 数値の書き方は以前の補間文字列 ($"…{No}") と同じにする (同じ名前になること)。

        /// <summary>杭頭 (基礎側) <c>CapNode-{杭番号}</c>。</summary>
        internal static string Cap(int pileNo) => $"{CapPrefix}{pileNo}";

        /// <summary>杭の接合節点 <c>FoundationNode-P{杭番号}</c>。</summary>
        internal static string PileJoint(int pileNo) => $"{PileJointPrefix}{pileNo}";

        /// <summary>基礎梁の節点 <c>FoundationNode-{節点番号}</c>。</summary>
        internal static string FoundationBeam(int nodeNo) => $"{FoundationBeamPrefix}{nodeNo}";

        /// <summary>一般節点 <c>InputNode-{節点番号}</c>。</summary>
        internal static string Input(int nodeNo) => $"{InputPrefix}{nodeNo}";

        /// <summary>鉛直梁モデルの地盤節点 <c>GroundNode-P{杭番号}</c>。</summary>
        internal static string Ground(int pileNo) => $"{GroundPrefix}{pileNo}";

        /// <summary>杭節点 <c>杭節点-{杭番号}-{節点順}</c>。</summary>
        internal static string Pile(int pileNo, int index) => $"{PilePrefix}{pileNo}-{index}";

        /// <summary>杭頭の杭節点 (節点順 0) <c>杭節点-{杭番号}-0</c>。</summary>
        internal static string PileHead(int pileNo) => Pile(pileNo, 0);

        /// <summary>杭の地盤節点 <c>杭地盤節点-{杭番号}-{節点順}</c>。</summary>
        internal static string PileSoil(int pileNo, int index) => $"{PileSoilPrefix}{pileNo}-{index}";

        /// <summary>自由度のキー <c>{節点名}:{自由度}</c> (剛性の検査・残差の診断が使う)。</summary>
        internal static string DofKey(string nodeName, string dof) => $"{nodeName}:{dof}";

        // ---- 読み方 ----

        /// <summary>
        /// 節点の名前 (または「名前:自由度」) を読む。<paramref name="number"/> は杭番号または節点番号、
        /// <paramref name="index"/> は杭節点・杭地盤節点の節点順 (他は -1)。
        /// 番号が数として読めない名前は <see cref="FemNodeKind.Unknown"/>。
        /// </summary>
        internal static FemNodeKind Parse(string? name, out int number, out int index)
        {
            number = -1;
            index = -1;
            if (string.IsNullOrEmpty(name)) return FemNodeKind.Unknown;

            // 「名前:自由度」なら名前の部分だけを見る
            int colon = name.IndexOf(':');
            var body = colon >= 0 ? name.AsSpan(0, colon) : name.AsSpan();

            if (body.SequenceEqual(ActionPoint)) return FemNodeKind.ActionPoint;
            if (body.SequenceEqual(Embedment)) return FemNodeKind.Embedment;
            if (body.SequenceEqual(EmbedmentSoil)) return FemNodeKind.EmbedmentSoil;

            if (TryNumber(body, CapPrefix, out number)) return FemNodeKind.Cap;
            // 接合節点 (…-P) を基礎梁節点より先に見る
            if (TryNumber(body, PileJointPrefix, out number)) return FemNodeKind.PileJoint;
            if (TryNumber(body, FoundationBeamPrefix, out number)) return FemNodeKind.FoundationBeam;
            if (TryNumber(body, InputPrefix, out number)) return FemNodeKind.Input;
            if (TryNumber(body, GroundPrefix, out number)) return FemNodeKind.Ground;
            if (TryNumberIndex(body, PilePrefix, out number, out index)) return FemNodeKind.Pile;
            if (TryNumberIndex(body, PileSoilPrefix, out number, out index)) return FemNodeKind.PileSoil;

            number = -1;
            index = -1;
            return FemNodeKind.Unknown;
        }

        /// <summary>節点の種類だけを読む。</summary>
        internal static FemNodeKind KindOf(string? name) => Parse(name, out _, out _);

        /// <summary>杭節点の名前 (または「名前:自由度」) の杭番号。杭節点でなければ null。</summary>
        internal static int? PileNoOf(string? nodeName)
            => Parse(nodeName, out int no, out _) == FemNodeKind.Pile ? no : null;

        /// <summary>杭頭 (基礎側) の名前なら杭番号。</summary>
        internal static int? CapPileNoOf(string? nodeName)
            => Parse(nodeName, out int no, out _) == FemNodeKind.Cap ? no : null;

        /// <summary>接合節点の名前なら杭番号。</summary>
        internal static int? PileJointNoOf(string? nodeName)
            => Parse(nodeName, out int no, out _) == FemNodeKind.PileJoint ? no : null;

        /// <summary>杭地盤節点の名前か。</summary>
        internal static bool IsPileSoil(string? nodeName) => StartsWith(nodeName, PileSoilPrefix);

        /// <summary>鉛直梁モデルの地盤節点の名前か。</summary>
        internal static bool IsGround(string? nodeName) => StartsWith(nodeName, GroundPrefix);

        /// <summary>
        /// 基礎の上の節点 (接合節点・基礎梁の節点・一般節点) か。基礎梁の端を座標で探すときの候補。
        /// 以前の <c>StartsWith("FoundationNode-") || StartsWith("InputNode-")</c> と同じ判定 (番号は読まない)。
        /// </summary>
        internal static bool IsFoundationOrInput(string? nodeName)
            => StartsWith(nodeName, FoundationBeamPrefix) || StartsWith(nodeName, InputPrefix);

        /// <summary>
        /// 剛床の slave にするスラブ上の節点 (一般節点・基礎梁の節点。接合節点は除く) か。
        /// 以前の <c>StartsWith("InputNode-") || (StartsWith("FoundationNode-") &amp;&amp; !StartsWith("FoundationNode-P"))</c> と同じ判定。
        /// </summary>
        internal static bool IsSlabNode(string? nodeName)
            => StartsWith(nodeName, InputPrefix)
               || (StartsWith(nodeName, FoundationBeamPrefix) && !StartsWith(nodeName, PileJointPrefix));

        /// <summary>
        /// 杭頭・接合節点・基礎梁の節点・一般節点の名前の、接頭辞のあとの部分 (番号の文字列)。
        /// 利用者向けの呼び方を作るためのもの (数として読めなくても空でなければ返す。以前の読み方と同じ)。
        /// </summary>
        internal static bool TrySuffix(string name, FemNodeKind kind, out string suffix)
        {
            string? prefix = kind switch
            {
                FemNodeKind.Cap => CapPrefix,
                FemNodeKind.PileJoint => PileJointPrefix,
                FemNodeKind.FoundationBeam => FoundationBeamPrefix,
                FemNodeKind.Input => InputPrefix,
                FemNodeKind.Ground => GroundPrefix,
                _ => null,
            };
            suffix = prefix != null && name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : "";
            return suffix.Length > 0;
        }

        private static bool StartsWith(string? name, string prefix)
            => name != null && name.StartsWith(prefix, StringComparison.Ordinal);

        private static bool TryNumber(ReadOnlySpan<char> body, string prefix, out int number)
        {
            number = -1;
            if (!body.StartsWith(prefix, StringComparison.Ordinal)) return false;
            return int.TryParse(body[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>
        /// <c>{接頭辞}{番号}-{節点順}</c> を読む。以前の杭節点の読み方 (<c>Split('-')</c> の 2 つ目を番号とし、
        /// 3 つ以上に分かれること) に合わせ、節点順は読めなくても番号が読めれば受け付ける。
        /// </summary>
        private static bool TryNumberIndex(ReadOnlySpan<char> body, string prefix, out int number, out int index)
        {
            number = -1;
            index = -1;
            if (!body.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var rest = body[prefix.Length..];
            int dash = rest.IndexOf('-');
            if (dash < 0) return false;
            if (!int.TryParse(rest[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) { number = -1; return false; }
            var tail = rest[(dash + 1)..];
            int dash2 = tail.IndexOf('-');
            if (int.TryParse(dash2 >= 0 ? tail[..dash2] : tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                index = i;
            return true;
        }
    }
}
