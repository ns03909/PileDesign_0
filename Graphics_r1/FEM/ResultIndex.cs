using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PileDesign.FEM
{
    /// <summary>
    /// 「どの荷重条件・どのステップの結果か」を持つ結果の行
    /// (<see cref="AnalysisStepResult"/>・<see cref="NodeResult"/>・<see cref="BeamResult"/>・
    /// <see cref="HorizontalSpringResult"/>・<see cref="RotationalSpringResult"/>)。
    /// </summary>
    public interface ICaseStepResult
    {
        LoadCase LoadCase { get; }
        LoadCombination LoadCombination { get; }
        bool IsLiquefaction { get; }
        int Step { get; }
    }

    /// <summary>
    /// 解析結果の荷重条件の識別 (レベル, 荷重ケース番号, 荷重組合せ番号, 液状化)。
    ///
    /// 荷重ケースはレベルと番号 (<see cref="LoadCase.IsSameCase"/>)、荷重組合せは番号
    /// (<see cref="LoadCombination.IsSameCombination"/>) で見分ける。名前は空欄・重複がありうるので使わない。
    /// 以前はこの鍵が 3 通り (収束状態の鍵・計算書の索引の文字列・検定の文字列) あり、
    /// 照合の書き方は 4 通りあった。結果を引く処理は <see cref="ResultLookup{T}"/> に集める。
    /// </summary>
    public readonly record struct ResultCaseKey(int Level, int LoadCaseNo, int LoadCombinationNo, bool IsLiquefaction)
    {
        /// <summary>鍵を作る。荷重ケース・荷重組合せが無いときは -1 を入れる (集計の鍵として使うため)。</summary>
        public static ResultCaseKey Of(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction)
            => new(loadCase?.Level ?? -1, loadCase?.No ?? -1, loadCombination?.No ?? -1, isLiquefaction);

        /// <summary>
        /// 照合に使える鍵を作る。荷重ケースか荷重組合せが無ければ false
        /// (<see cref="LoadCase.IsSameCase"/> と同じく、null はどの結果とも一致しない)。
        /// </summary>
        public static bool TryOf(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction, out ResultCaseKey key)
        {
            if (loadCase == null || loadCombination == null) { key = default; return false; }
            key = Of(loadCase, loadCombination, isLiquefaction);
            return true;
        }

        /// <summary>結果の行が、この鍵の荷重条件のものか (行の荷重ケース・組合せが無ければ一致しない)。</summary>
        public bool Matches(ICaseStepResult r)
            => r.LoadCase != null && r.LoadCombination != null
               && r.LoadCase.Level == Level && r.LoadCase.No == LoadCaseNo
               && r.LoadCombination.No == LoadCombinationNo && r.IsLiquefaction == IsLiquefaction;
    }

    /// <summary>
    /// 1 つの持ち主 (梁・節点・ばね・解析モデルのステップ一覧) の結果を、荷重条件とステップで引く索引。
    ///
    /// <para><b>なぜあるか</b>: 結果の照合は以前 4 通りに書かれていた
    /// (<c>Beam.GetBeamResult</c> / <c>Node.GetNodeResult</c> / <c>AnaModel.GetAnalysisLastStep</c> の線形走査、
    /// 検定の FirstOrDefault、計算書の Dictionary)。うち前者 3 つと計算書は、指定した液状化の状態に結果が無いと
    /// <b>黙って逆の状態の結果を返していた</b>ので、画面の切替が「液状化」のままキャンバス・ツールチップ・グラフに
    /// 非液状化の値が出る一方、検定は同じ条件を「結果なし」とし、両者が食い違った。
    /// また最終ステップを求めるたびに全ステップの結果を走査して <c>ToList()</c> しており、
    /// キャンバスの再描画のたびに梁・節点の数だけ繰り返していた。</para>
    ///
    /// <para><b>液状化の扱い</b>: 指定した状態の結果だけを返す。逆の状態の結果を出したい画面は
    /// <see cref="AnaModel.ResolveLiquefactionWithResults"/> で状態を明示的に切り替え、切り替えたことを表示すること。</para>
    ///
    /// <para><b>索引の作り直し</b>: 索引は初めて引いたときに作り、結果の一覧の
    /// (インスタンス, 件数, 先頭の行, 末尾の行) を控えておく。引くたびにこれを照合し、変わっていれば作り直す
    /// (追加・削除・差し替え・1 件消して 1 件足す、のいずれでも末尾か件数が変わる)。
    /// 結果を書き換える処理 (解析の取り込み・削除・読込) は <see cref="AnaModel.InvalidateResultIndex"/> も呼ぶ。</para>
    ///
    /// <para><b>スレッドの約束</b>: 引く処理は複数のスレッドから同時に呼んでよい (検定・計算書は Parallel.For から引く)。
    /// 索引はでき上がってから差し替えるので、読み手が作りかけを見ることはない。
    /// ただし<b>結果の一覧を書き換えている最中に引いてはいけない</b> (List を走査中に追加されると例外になる。
    /// 以前の線形走査も同じ)。ケース並列の解析は <c>_caseMergeLock</c> の中で主モデルへ結果を移し、
    /// 移し終えてから索引を捨てる (<see cref="AnaModel.AppendCaseResultsToMain"/>)。
    /// 解析中は主モデルの結果を画面から引かない (解析の窓が閉じてから結果を差し替える)。</para>
    ///
    /// <para>結果の行の荷重ケース・組合せのレベルと番号は、解析後に変えないこと
    /// (結果は解析時の入力の複製 (<c>AnalysisResultSet</c>) を指すので、画面の編集では変わらない)。</para>
    /// </summary>
    public sealed class ResultLookup<T> where T : class, ICaseStepResult
    {
        private readonly Func<List<T>?> _source;
        private Table? _table;

        public ResultLookup(Func<List<T>?> source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>ある荷重条件の結果 (ステップ順に並べ直したもの)。</summary>
        private sealed class CaseEntry
        {
            public int MinStep;
            public T?[] ByStep = [];      // ByStep[step - MinStep]。同じステップが複数あれば最初の行
            public T Latest = null!;      // ステップが最大の行。同じステップが複数あれば最初の行
        }

        private sealed class Table
        {
            public List<T>? List;
            public int Count;
            public T? First;
            public T? Last;
            public Dictionary<ResultCaseKey, CaseEntry> Cases = [];
            public List<T>? Overflow;     // ステップの番号が飛びすぎて配列にしなかった荷重条件の行 (一覧の順)

            public bool IsFor(List<T>? list)
            {
                if (!ReferenceEquals(List, list)) return false;
                if (list == null) return true;
                int n = list.Count;
                if (n != Count) return false;
                return n == 0 || (ReferenceEquals(First, list[0]) && ReferenceEquals(Last, list[n - 1]));
            }
        }

        /// <summary>索引を捨てる。次に引いたときに作り直す。</summary>
        public void Invalidate() => Volatile.Write(ref _table, null);

        /// <summary>索引を作っておく (まだ無いか古ければ)。多数のスレッドから引く前に呼ぶと、作る処理が重ならない。</summary>
        public void EnsureBuilt() => Current();

        private Table Current()
        {
            var list = _source();
            var table = Volatile.Read(ref _table);
            if (table != null && table.IsFor(list)) return table;

            table = Build(list);
            Volatile.Write(ref _table, table);
            return table;
        }

        private static Table Build(List<T>? list)
        {
            var table = new Table { List = list };
            if (list == null || list.Count == 0) return table;

            table.Count = list.Count;
            table.First = list[0];
            table.Last = list[^1];

            // 1 回目: 荷重条件ごとにステップの範囲を求める
            var ranges = new Dictionary<ResultCaseKey, (int Min, int Max)>();
            foreach (var r in list)
            {
                if (r?.LoadCase == null || r.LoadCombination == null) continue; // どの照合とも一致しない行
                var key = ResultCaseKey.Of(r.LoadCase, r.LoadCombination, r.IsLiquefaction);
                ranges[key] = ranges.TryGetValue(key, out var mm)
                    ? (Math.Min(mm.Min, r.Step), Math.Max(mm.Max, r.Step))
                    : (r.Step, r.Step);
            }

            foreach (var (key, mm) in ranges)
            {
                long span = (long)mm.Max - mm.Min + 1;
                table.Cases[key] = new CaseEntry
                {
                    MinStep = mm.Min,
                    // ステップは 0 から順に振られる。極端に飛んだ番号 (壊れたファイル) で巨大な配列を取らないよう上限を置く
                    ByStep = span <= MaxStepSpan ? new T?[span] : [],
                };
            }

            // 2 回目: 一覧の順に詰める。以前の線形走査と同じく、同じ鍵・同じステップが並べば最初の行を採る
            List<T>? overflow = null;
            foreach (var r in list)
            {
                if (r?.LoadCase == null || r.LoadCombination == null) continue;
                var entry = table.Cases[ResultCaseKey.Of(r.LoadCase, r.LoadCombination, r.IsLiquefaction)];
                if (entry.Latest == null || r.Step > entry.Latest.Step) entry.Latest = r;
                if (entry.ByStep.Length > 0)
                {
                    int i = r.Step - entry.MinStep;
                    entry.ByStep[i] ??= r;
                }
                else (overflow ??= []).Add(r);
            }
            table.Overflow = overflow;
            return table;
        }

        private const long MaxStepSpan = 1_000_000;

        /// <summary>
        /// 指定した荷重条件・ステップの結果。無ければ null。
        /// 荷重ケース・組合せが null なら一致するものは無い (<see cref="LoadCase.IsSameCase"/> と同じ)。
        /// </summary>
        public T? At(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction, int step)
        {
            if (!ResultCaseKey.TryOf(loadCase, loadCombination, isLiquefaction, out var key)) return null;
            return At(key, step);
        }

        /// <summary>指定した荷重条件・ステップの結果。無ければ null。</summary>
        public T? At(ResultCaseKey key, int step)
        {
            var table = Current();
            if (!table.Cases.TryGetValue(key, out var entry)) return null;
            if (entry.ByStep.Length > 0)
            {
                long i = (long)step - entry.MinStep;
                return i >= 0 && i < entry.ByStep.Length ? entry.ByStep[i] : null;
            }
            // ステップの番号が飛びすぎていて配列にしなかった荷重条件 (壊れたファイルのみ)
            if (table.Overflow != null)
                foreach (var r in table.Overflow)
                    if (r.Step == step && key.Matches(r)) return r;
            return null;
        }

        /// <summary>
        /// この持ち主にある、指定した荷重条件の最もステップの大きい結果。無ければ null。
        /// 解析モデルのステップ一覧に対して使うと「その荷重条件の最終ステップ」になる。
        /// </summary>
        public T? Latest(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction)
        {
            if (!ResultCaseKey.TryOf(loadCase, loadCombination, isLiquefaction, out var key)) return null;
            return Current().Cases.TryGetValue(key, out var entry) ? entry.Latest : null;
        }

        /// <summary>
        /// 荷重ケース・荷重組合せのうち null のものを「すべて」として照合する。
        /// 検定と結果の表は、荷重条件の分からない古い結果でも引けるようにこの形で引いている。
        /// どちらも指定されていれば <see cref="At(LoadCase?, LoadCombination?, bool, int)"/> と同じ。
        /// </summary>
        public T? Matching(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction, int step)
        {
            if (loadCase != null && loadCombination != null)
                return At(loadCase, loadCombination, isLiquefaction, step);

            var list = _source();
            if (list == null) return null;
            foreach (var r in list)
            {
                if (r == null || r.IsLiquefaction != isLiquefaction || r.Step != step) continue;
                if (loadCase != null && !LoadCase.IsSameCase(r.LoadCase, loadCase)) continue;
                if (loadCombination != null && !LoadCombination.IsSameCombination(r.LoadCombination, loadCombination)) continue;
                return r;
            }
            return null;
        }

        /// <summary>ステップの結果 (検定の 1 行) と同じ荷重条件・ステップの結果。null の項目は「すべて」として照合する。</summary>
        public T? AtStepOf(AnalysisStepResult stepResult)
            => stepResult == null ? null
             : Matching(stepResult.LoadCase, stepResult.LoadCombination, stepResult.IsLiquefaction, stepResult.Step);

        /// <summary>
        /// 指定した荷重条件の結果が 1 つでもあるか。荷重組合せが null なら、どの組合せでもよい。
        /// 荷重ケースが null なら false。
        /// </summary>
        public bool Any(LoadCase? loadCase, LoadCombination? loadCombination, bool isLiquefaction)
        {
            if (loadCase == null) return false;
            if (loadCombination != null) return Latest(loadCase, loadCombination, isLiquefaction) != null;
            foreach (var key in Current().Cases.Keys)
                if (key.Level == loadCase.Level && key.LoadCaseNo == loadCase.No && key.IsLiquefaction == isLiquefaction)
                    return true;
            return false;
        }
    }
}
