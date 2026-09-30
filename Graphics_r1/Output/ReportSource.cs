using PileDesign.Models.Results;
using System;
using System.Collections.Generic;

namespace PileDesign.Output
{
    /// <summary>
    /// 計算書の元データのうち、画面 (ViewModel) の<b>いまの状態</b>から読むもの。出力を始めた時点で 1 つにまとめる。
    ///
    /// <para>計算書は画面のスレッドで一気に組み立てるが、表紙のモデル図を写すところで描画を待つために
    /// 画面のメッセージを回す (<c>Dispatcher.Invoke</c>)。そのあいだに待ち行列の処理 (解析の完了の知らせなど)
    /// が走ると、それより後で読む「解析を済ませたか」の印・基礎梁考慮の鉛直解析の結果・検定の結果が
    /// 出力を始めたときと別の時点のものになり、1 冊の計算書に異なる時点の値が混ざる。
    /// 入力 (解析時の控え) と解析モデルは出力の開始時に渡されるので、残りもここで同じ時点に固定する。</para>
    ///
    /// <para>作るのは画面の側 (<c>MainWindowViewModel.CaptureReportSource</c>)。計算書の実装は画面の状態を
    /// 直接読まない (層の向きと、現在の入力を読まないことをテストが見張っている)。</para>
    /// </summary>
    internal sealed class ReportSource
    {
        public bool IsHorizontalAnalysisDone { get; init; }
        public bool IsVerticalAnalysisDone { get; init; }
        public bool IsVerticalBeamAnalysisDone { get; init; }

        /// <summary>基礎梁考慮の鉛直解析の結果 (出力の開始時の一覧の写し)。</summary>
        public IReadOnlyList<FEM.VerticalBeamCaseResult>? VerticalBeamCaseResults { get; init; }

        /// <summary>
        /// 表紙のモデル図を画面から写してよいか。画面は<b>編集中の入力</b>を描くので、計算書の入力 (解析時の控え) と
        /// 中身が違う (解析のあとに編集した) ときは写さない。
        /// </summary>
        public bool CoverMatchesReportInput { get; init; } = true;

        /// <summary>低減後の水平解析の検定の結果 (計算書に載せるときだけ、出力の開始時に求める)。</summary>
        public EvaluationResult? FactoredEvaluation { get; init; }

        /// <summary>検定を組めなかったときの例外 (計算書のその位置に注記する)。</summary>
        public Exception? FactoredEvaluationError { get; init; }

        /// <summary>
        /// 結果の前提 (解析の時刻・解析したときの入力の識別・解いたケースの数・解析のあとの編集)。表紙に 1 行で書き、
        /// あとからどの解析の結果かを追えるようにする。解析していなければ null。
        /// </summary>
        public string? AnalysisConditions { get; init; }

        /// <summary>
        /// 解析の種類ごとの実行条件 (出力の開始時の写し)。「計算条件・仮定」の章の「解析の実行条件」に種類ごとの表で書く。
        /// 表紙に並べると文字で埋まり、どの条件がどの解析のものか読みにくかった。記録の無い旧ファイルでは空。
        /// </summary>
        public IReadOnlyList<Models.AnalysisRunMetadata> RunRecords { get; init; } = [];

        /// <summary>水平解析で解いたケースの数 (解析していなければ 0)。性能の記録に添える。</summary>
        public int HorizontalCaseCount { get; init; }

        /// <summary>
        /// 水平解析の結果そのものにある数値でない値の数 (<c>AnalysisResultValidator.FindNonFinite</c>)。
        /// 計算書の表に数値でない値が出たとき、解析で生じたのか表を作る段階で生じたのかを分けて書くのに使う。
        /// </summary>
        public int AnalysisNonFiniteCount { get; init; }
    }
}
