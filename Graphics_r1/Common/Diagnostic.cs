using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Common
{
    /// <summary>
    /// 問題が見つかった段階。数値でない値などは、入力・解析・結果の出力のどこで生じたかで直す場所が違う。
    /// </summary>
    public enum DiagnosticOrigin
    {
        /// <summary>解析の前の入力の検査。</summary>
        Input,
        /// <summary>解析 (モデルの組み立て・反復・結果)。</summary>
        Analysis,
        /// <summary>結果の出力 (表・計算書)。</summary>
        Output,
    }

    /// <summary>問題の場所の種類。直しに行く入力画面と、メイン画面で選ぶ杭の決め方がこれで決まる。</summary>
    public enum DiagnosticTargetKind
    {
        /// <summary>場所を特定できない (解析のログの段階・ケースで追う)。</summary>
        None,
        /// <summary>特定の杭 (杭配置の 1 行)。</summary>
        Pile,
        /// <summary>杭体 (その杭体を使う杭すべてに効く)。</summary>
        PileBody,
        /// <summary>杭体の区間 (その杭体を使う杭すべてに効く)。</summary>
        PileBodySegment,
        /// <summary>地盤 (その地盤を使う杭すべてに効く)。</summary>
        Ground,
        /// <summary>地盤の土層。</summary>
        GroundLayer,
        /// <summary>根入部。</summary>
        Embedment,
        /// <summary>荷重ケース・組合せ。</summary>
        LoadCase,
        /// <summary>基礎梁。</summary>
        FoundationBeam,
        /// <summary>群杭沈下の沈下用土層。</summary>
        SettlementLayer,
    }

    /// <summary>
    /// 問題の場所。番号は文に埋め込まず、ここに個別に持つ。
    ///
    /// <para>以前は知らせの文から正規表現で「杭No.n」「杭体n」を拾って杭を選んでいた。文の書き方を変えると
    /// 選択が黙って外れ、「杭体番号2」(杭体) と「杭 No.2」(杭) のように似た書き方を取り違える余地もあった。
    /// 知らせの文と杭の選択・入力画面への移動は、同じこの値から作る。</para>
    /// </summary>
    public sealed record DiagnosticTarget(DiagnosticTargetKind Kind)
    {
        /// <summary>杭配置の番号 (No)。<see cref="DiagnosticTargetKind.Pile"/> のとき。</summary>
        public int? PileNo { get; init; }
        /// <summary>杭体の番号 (1 から)。</summary>
        public int? PileBodyNo { get; init; }
        /// <summary>杭体の区間の番号 (1 から)。</summary>
        public int? SegmentNo { get; init; }
        /// <summary>地盤の番号 (1 から)。</summary>
        public int? GroundNo { get; init; }
        /// <summary>土層の番号 (1 から。地盤の土層・根入部の層・沈下用土層)。</summary>
        public int? LayerNo { get; init; }
        /// <summary>基礎梁の番号。</summary>
        public int? BeamNo { get; init; }
        /// <summary>荷重ケースの表示名。</summary>
        public string? CaseName { get; init; }
        /// <summary>止まった段階 (「準備」「荷重ステップ 3/12 の反復」など)。</summary>
        public string? Stage { get; init; }

        public static DiagnosticTarget Nowhere { get; } = new(DiagnosticTargetKind.None);

        public static DiagnosticTarget Pile(int pileNo, int? pileBodyNo = null)
            => new(DiagnosticTargetKind.Pile) { PileNo = pileNo, PileBodyNo = pileBodyNo };
        public static DiagnosticTarget PileBody(int pileBodyNo)
            => new(DiagnosticTargetKind.PileBody) { PileBodyNo = pileBodyNo };
        public static DiagnosticTarget PileBodySegment(int pileBodyNo, int segmentNo)
            => new(DiagnosticTargetKind.PileBodySegment) { PileBodyNo = pileBodyNo, SegmentNo = segmentNo };
        public static DiagnosticTarget Ground(int groundNo)
            => new(DiagnosticTargetKind.Ground) { GroundNo = groundNo };
        public static DiagnosticTarget GroundLayer(int groundNo, int layerNo)
            => new(DiagnosticTargetKind.GroundLayer) { GroundNo = groundNo, LayerNo = layerNo };
        public static DiagnosticTarget Embedment(int? layerNo = null)
            => new(DiagnosticTargetKind.Embedment) { LayerNo = layerNo };
        public static DiagnosticTarget LoadCase(string caseName, string? stage = null)
            => new(DiagnosticTargetKind.LoadCase) { CaseName = caseName, Stage = stage };
        public static DiagnosticTarget FoundationBeam(int? beamNo = null)
            => new(DiagnosticTargetKind.FoundationBeam) { BeamNo = beamNo };
        public static DiagnosticTarget SettlementLayer(int layerNo)
            => new(DiagnosticTargetKind.SettlementLayer) { LayerNo = layerNo };

        /// <summary>画面に出す場所の名前 (「杭 No.3」「杭体2 区間1」「地盤1 層2」)。場所を特定できなければ空。</summary>
        public string Label => Kind switch
        {
            DiagnosticTargetKind.Pile => $"杭 No.{PileNo}",
            DiagnosticTargetKind.PileBody => $"杭体{PileBodyNo}",
            DiagnosticTargetKind.PileBodySegment => $"杭体{PileBodyNo} 区間{SegmentNo}",
            DiagnosticTargetKind.Ground => $"地盤{GroundNo}",
            DiagnosticTargetKind.GroundLayer => $"地盤{GroundNo} 層{LayerNo}",
            DiagnosticTargetKind.Embedment => LayerNo is int l ? $"根入部 第{l}層" : "根入部",
            DiagnosticTargetKind.LoadCase => CaseName ?? "荷重ケース",
            DiagnosticTargetKind.FoundationBeam => BeamNo is int b ? $"基礎梁 No.{b}" : "基礎梁",
            DiagnosticTargetKind.SettlementLayer => $"沈下用土層 {LayerNo} 層目",
            _ => "",
        };

        /// <summary>ログに残す形 (番号を項目ごとに書く。空の項目は省く)。</summary>
        public string ToLogFields()
        {
            var parts = new List<string> { $"kind={Kind}" };
            if (PileNo is int p) parts.Add($"pile={p}");
            if (PileBodyNo is int pb) parts.Add($"pileBody={pb}");
            if (SegmentNo is int s) parts.Add($"segment={s}");
            if (GroundNo is int g) parts.Add($"ground={g}");
            if (LayerNo is int l) parts.Add($"layer={l}");
            if (BeamNo is int b) parts.Add($"beam={b}");
            if (!string.IsNullOrEmpty(CaseName)) parts.Add($"case={CaseName}");
            if (!string.IsNullOrEmpty(Stage)) parts.Add($"stage={Stage}");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// 見つかった問題の 1 件。知らせる文 (<see cref="Message"/>) と場所 (<see cref="Target"/>)・段階 (<see cref="Origin"/>) を
    /// 一緒に持ち、画面の知らせ・杭の選択・入力画面への移動・ログが同じものを使う。
    /// </summary>
    public sealed record Diagnostic(DiagnosticOrigin Origin, DiagnosticTarget Target, string Message)
    {
        /// <summary>1 行の問題がほかの場所にも及ぶとき、その場所 (同じ杭体・地盤・杭頭の高さの杭をまとめた行など)。</summary>
        public IReadOnlyList<DiagnosticTarget> MoreTargets { get; init; } = [];

        /// <summary>この問題の場所すべて (<see cref="Target"/> と <see cref="MoreTargets"/>)。</summary>
        public IEnumerable<DiagnosticTarget> Targets => MoreTargets.Count == 0 ? [Target] : MoreTargets.Prepend(Target);

        /// <summary>入力の問題。<paramref name="detail"/> の頭に場所の名前を付ける (「杭体2 区間1: …」)。</summary>
        public static Diagnostic InputAt(DiagnosticTarget target, string detail)
            => new(DiagnosticOrigin.Input, target, target.Label.Length > 0 ? $"{target.Label}: {detail}" : detail);

        /// <summary>入力の問題。場所の名前を文の中に書いた (頭に付けない) もの。</summary>
        public static Diagnostic Input(DiagnosticTarget target, string message)
            => new(DiagnosticOrigin.Input, target, message);

        /// <summary>解析で見つかった問題。<paramref name="detail"/> の頭に場所の名前を付ける。</summary>
        public static Diagnostic AnalysisAt(DiagnosticTarget target, string detail)
            => new(DiagnosticOrigin.Analysis, target, target.Label.Length > 0 ? $"{target.Label}: {detail}" : detail);

        /// <summary>段階の名前 (知らせ・ログ・計算書で同じ呼び方にする)。</summary>
        public static string OriginLabel(DiagnosticOrigin origin) => origin switch
        {
            DiagnosticOrigin.Input => "入力",
            DiagnosticOrigin.Analysis => "解析",
            DiagnosticOrigin.Output => "結果の出力",
            _ => "",
        };

        /// <summary>ログに残す 1 行 (段階・場所の番号を項目に分けて書く)。</summary>
        public string ToLogLine() => $"[{OriginLabel(Origin)}] {Target.ToLogFields()} : {Message}";
    }

    /// <summary>
    /// 場所の分かっている問題で止めるときの例外。受け取った側は文を解かずに <see cref="Diagnostics"/> から
    /// 杭を選び、入力画面へ案内する。
    /// </summary>
    public sealed class DiagnosticException : InvalidOperationException
    {
        public DiagnosticException(Diagnostic diagnostic, Exception? inner = null)
            : this(diagnostic.Message, [diagnostic], inner) { }

        public DiagnosticException(string message, IReadOnlyList<Diagnostic> diagnostics, Exception? inner = null)
            : base(message, inner)
        {
            Diagnostics = diagnostics ?? [];
        }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// 例外 (包まれたもの・並列に解いたときの AggregateException の中を含む) が持つ問題をすべて集める。出た順。
        /// </summary>
        public static IReadOnlyList<Diagnostic> CollectFrom(Exception? ex)
        {
            var found = new List<Diagnostic>();
            void Walk(Exception? e)
            {
                for (; e != null; e = e.InnerException)
                {
                    if (e is DiagnosticException d) found.AddRange(d.Diagnostics);
                    if (e is AggregateException agg)
                    {
                        foreach (var inner in agg.InnerExceptions) Walk(inner);
                        return;
                    }
                }
            }
            Walk(ex);
            return found.Distinct().ToList();
        }
    }
}
