using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 杭 1 本の検定比の帯。画面の色分けと一覧の判定に使う。
    ///
    /// 検定比そのものではなく帯にするのは、色で読めるのは 4 段階程度までで、
    /// 「余裕がある / 余裕が少ない / 超えている / 判定できない」が設計者の次の行動を決めるため。
    /// </summary>
    public enum PileRatioBand
    {
        /// <summary>検定の項目が 1 件も無い (解析していない、対象外)。</summary>
        None,

        /// <summary>検定比が <see cref="PileEvaluationSummary.TightThreshold"/> 以下。余裕がある。</summary>
        Safe,

        /// <summary>検定比が 1.0 以下だが余裕が少ない。杭径・配筋を変えると NG に転じやすい。</summary>
        Tight,

        /// <summary>収束したケースに NG がある。</summary>
        Ng,

        /// <summary>
        /// NG は無いが、収束しなかったケースの項目がある。
        /// 釣り合っていない応答値なので OK とも NG とも言えない。
        /// </summary>
        Unconverged,
    }

    /// <summary>杭 1 本ぶんの検定のまとめ。</summary>
    public sealed class PileEvaluationEntry
    {
        public int PileNo { get; init; }

        /// <summary>
        /// 要素の番号 (0 が杭頭側の最上段)。杭 1 本ぶんの要約では null。
        /// </summary>
        public int? ElementIndex { get; init; }

        /// <summary>収束したケースの中で最大の検定比。収束したケースが無ければ NaN。</summary>
        public double MaxRatio { get; init; }

        /// <summary>最大の検定比を出した項目。収束したケースが無ければ null。</summary>
        public EvaluationItem? Governing { get; init; }

        /// <summary>この杭に、収束しなかったケースの項目があるか。</summary>
        public bool HasUnconverged { get; init; }

        /// <summary>この杭に、収束したケースの NG があるか。</summary>
        public bool HasNg { get; init; }

        public PileRatioBand Band { get; init; }

        /// <summary>一覧に出す判定の文字列。</summary>
        public string StatusLabel => Band switch
        {
            PileRatioBand.Ng => "NG",
            PileRatioBand.Unconverged => "未収束",
            PileRatioBand.None => "—",
            _ => "OK",
        };
    }

    /// <summary>
    /// 検定結果を「設計者が最初に知りたい形」にまとめる。
    ///
    /// 検定の項目 (<see cref="EvaluationItem"/>) は荷重ケース × 部位 × 限界状態の粒度で
    /// 数百件になり、解析結果テーブルに並べても<b>どの杭が厳しいか</b>は目で拾うしかなかった。
    /// ここで杭ごとの最大検定比に畳み、ダッシュボードの一覧とキャンバスの色分けに同じ値を配る。
    ///
    /// 対象は「低減後の水平解析」と「杭の鉛直支持力」。計算書の検定と同じ組合せで、
    /// 低減前はここでは畳まない (低減後と同じ行が二重に出て支配ケースを読み違えるため)。
    /// </summary>
    public sealed class PileEvaluationSummary
    {
        /// <summary>これを超えると「余裕が少ない」(黄)。1.0 を超えると NG (赤)。</summary>
        public const double TightThreshold = 0.8;

        private PileEvaluationSummary(
            EvaluationResult? horizontal,
            EvaluationResult? horizontalUnfactored,
            EvaluationResult bearing,
            string seismicGrade,
            IReadOnlyDictionary<int, PileEvaluationEntry> byPile,
            IReadOnlyDictionary<(int PileNo, int ElementIndex), PileEvaluationEntry> byPileElement,
            IReadOnlyDictionary<int, PileEvaluationEntry> byPileHead)
        {
            ByPileElement = byPileElement;
            ByPileHead = byPileHead;
            Horizontal = horizontal;
            HorizontalUnfactored = horizontalUnfactored;
            Bearing = bearing;
            SeismicGrade = seismicGrade;
            ByPile = byPile;
            ByRatioDescending = byPile.Values
                .OrderByDescending(e => e.Band == PileRatioBand.Ng)
                .ThenByDescending(e => double.IsNaN(e.MaxRatio) ? double.NegativeInfinity : e.MaxRatio)
                .ToList();
        }

        /// <summary>低減後の水平解析の検定。水平解析が済んでいなければ null。</summary>
        public EvaluationResult? Horizontal { get; }

        /// <summary>低減前の水平解析の検定 (参考)。水平解析が済んでいなければ null。</summary>
        public EvaluationResult? HorizontalUnfactored { get; }

        /// <summary>杭の鉛直支持力の検定。杭要素分割が済んでいなければ空。</summary>
        public EvaluationResult Bearing { get; }

        public string SeismicGrade { get; }

        /// <summary>杭配置番号 → その杭のまとめ (全項目の最悪)。一覧・件数用。</summary>
        public IReadOnlyDictionary<int, PileEvaluationEntry> ByPile { get; }

        /// <summary>
        /// (杭配置番号, 要素の番号) → その<b>要素</b>のまとめ。
        /// 杭体の曲げ・せん断だけが入る。キャンバスで要素ごとに色を塗るのに使う。
        /// </summary>
        public IReadOnlyDictionary<(int PileNo, int ElementIndex), PileEvaluationEntry> ByPileElement { get; }

        /// <summary>
        /// 杭配置番号 → <b>部位を持たない検定</b>のまとめ
        /// (杭頭回転角・杭頭 2 点間の変形角・支持力)。キャンバスでは杭頭の印に使う。
        /// </summary>
        public IReadOnlyDictionary<int, PileEvaluationEntry> ByPileHead { get; }

        /// <summary>NG の杭を先頭に、検定比の降順。</summary>
        public IReadOnlyList<PileEvaluationEntry> ByRatioDescending { get; }

        public bool HasHorizontal => Horizontal != null && !Horizontal.IsEmpty;
        public bool HasBearing => !Bearing.IsEmpty;
        public bool IsEmpty => !HasHorizontal && !HasBearing;

        /// <summary>
        /// 収束しなかった荷重ケースの名前 (重複なし)。
        /// 計算書と同じ書式で、「未収束 3 件」の内訳を示す。
        /// </summary>
        public IReadOnlyList<string> UnconvergedCaseNames =>
            Horizontal == null
                ? []
                : Horizontal.Items
                    .Where(i => i.IsFromUnconvergedCase)
                    .Select(i => string.IsNullOrEmpty(i.LiquefactionLabel)
                        ? $"{i.LoadCaseName} {i.LoadCombinationName}"
                        : $"{i.LoadCaseName} {i.LoadCombinationName}（{i.LiquefactionLabel}）")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

        /// <summary>検定比と収束状態から帯を決める。</summary>
        public static PileRatioBand BandOf(double maxRatio, bool hasNg, bool hasUnconverged)
        {
            if (hasNg) return PileRatioBand.Ng;
            if (hasUnconverged) return PileRatioBand.Unconverged;
            if (double.IsNaN(maxRatio)) return PileRatioBand.None;
            if (maxRatio > 1.0) return PileRatioBand.Ng;
            return maxRatio > TightThreshold ? PileRatioBand.Tight : PileRatioBand.Safe;
        }

        /// <summary>
        /// 現在の解析結果からまとめを作る。
        /// 水平解析が済んでいなければ支持力だけ、杭要素分割も済んでいなければ空。
        /// 検定が組めない (古いモデルなど) ときも例外にせず、組めた分だけ返す。
        /// </summary>
        public static PileEvaluationSummary Build(MainWindowViewModel vm)
        {
            ArgumentNullException.ThrowIfNull(vm);

            var inputModel = vm.ResultInputModel ?? vm.CurrentInputModel;
            string seismicGrade = inputModel?.FundamentalInput?.SeismicGrade ?? "A";

            EvaluationResult? horizontal = null;
            EvaluationResult? unfactored = null;
            if (vm.CurrentModel != null && vm.IsHorizontalAnalysisDone)
            {
                horizontal = TryBuild(() => EvaluationService.BuildEvaluationResult(vm, factored: true), "低減後");
                unfactored = TryBuild(() => EvaluationService.BuildEvaluationResult(vm, factored: false), "低減前");
            }

            var bearing = TryBuild(
                () => new EvaluationResult(PileBearingEvaluator.Evaluate(inputModel, seismicGrade)), "支持力")
                ?? new EvaluationResult([]);

            return FromResults(horizontal, unfactored, bearing, seismicGrade);
        }

        /// <summary>検定結果からまとめを作る (テストと、結果が手元にあるときの入口)。</summary>
        public static PileEvaluationSummary FromResults(
            EvaluationResult? horizontal, EvaluationResult? horizontalUnfactored,
            EvaluationResult bearing, string seismicGrade)
        {
            ArgumentNullException.ThrowIfNull(bearing);

            var items = new List<EvaluationItem>();
            if (horizontal != null) items.AddRange(horizontal.Items);
            items.AddRange(bearing.Items);

            var withPile = items.Where(i => i.PileNo is int).ToList();

            var byPile = new Dictionary<int, PileEvaluationEntry>();
            foreach (var group in withPile.GroupBy(i => i.PileNo!.Value))
                byPile[group.Key] = Fold(group, group.Key, elementIndex: null);

            // 要素ごと (杭体の曲げ・せん断)。画面ではこの色を要素に塗る。
            var byPileElement = new Dictionary<(int, int), PileEvaluationEntry>();
            foreach (var group in withPile.Where(i => i.SegmentIndex is int)
                                          .GroupBy(i => (Pile: i.PileNo!.Value, Element: i.SegmentIndex!.Value)))
                byPileElement[group.Key] = Fold(group, group.Key.Pile, group.Key.Element);

            // 部位を持たない検定 (杭頭回転角・変形角・支持力)。画面では杭頭の印に使う。
            var byPileHead = new Dictionary<int, PileEvaluationEntry>();
            foreach (var group in withPile.Where(i => i.SegmentIndex is null).GroupBy(i => i.PileNo!.Value))
                byPileHead[group.Key] = Fold(group, group.Key, elementIndex: null);

            return new PileEvaluationSummary(horizontal, horizontalUnfactored, bearing, seismicGrade,
                byPile, byPileElement, byPileHead);
        }

        /// <summary>検定の項目をまとめて 1 件の帯に畳む。</summary>
        private static PileEvaluationEntry Fold(IEnumerable<EvaluationItem> items, int pileNo, int? elementIndex)
        {
            var list = items as IReadOnlyList<EvaluationItem> ?? items.ToList();
            var converged = list.Where(i => !i.IsFromUnconvergedCase).ToList();
            var governing = converged.OrderByDescending(i => i.Ratio).FirstOrDefault();
            bool hasNg = converged.Any(i => !i.IsOk);
            bool hasUnconverged = list.Any(i => i.IsFromUnconvergedCase);
            double maxRatio = governing?.Ratio ?? double.NaN;

            return new PileEvaluationEntry
            {
                PileNo = pileNo,
                ElementIndex = elementIndex,
                MaxRatio = maxRatio,
                Governing = governing,
                HasNg = hasNg,
                HasUnconverged = hasUnconverged,
                Band = BandOf(maxRatio, hasNg, hasUnconverged),
            };
        }

        private static EvaluationResult? TryBuild(Func<EvaluationResult> build, string label)
        {
            try
            {
                return build();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[検定サマリー] 生成に失敗 ({Label})", label);
                return null;
            }
        }
    }
}
