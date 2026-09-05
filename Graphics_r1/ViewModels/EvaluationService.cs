using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 解析結果の検定。
    ///
    /// 出力は 2 つ。
    ///   ・<see cref="BuildEvaluationResult"/>  構造化した結果 (解析結果テーブル用)
    ///   ・<see cref="BuildEvaluationText"/>    テキスト (計算書用)
    /// テキストは構造化した結果から組み立てる。
    ///
    /// かつては専用の検定ウィンドウの ViewModel だったが、
    /// 検定は解析結果テーブルから見るようにしたのでウィンドウは廃止した。
    /// </summary>
    public sealed class EvaluationService
    {
        private readonly MainWindowViewModel _mainVm;

        /// <summary>組み立てたテキスト。</summary>
        private string EvaluationText { get; set; } = "";

        /// <summary>表示フィルタ: 0=NGのみ, 1=OKのみ, 2=OK/NG両方。テキストの絞り込みに使う。</summary>
        private int DisplayFilter { get; init; }

        /// <summary>直近の検定結果 (構造化)。</summary>
        private EvaluationResult Result { get; set; } = new([]);

        private EvaluationService(MainWindowViewModel mainVm)
        {
            _mainVm = mainVm;
        }

        /// <summary>
        /// 検定テキストを取得する (計算書から利用)。
        /// </summary>
        /// <param name="displayFilter">0=NGのみ, 1=OKのみ, 2=両方</param>
        public static string BuildEvaluationText(MainWindowViewModel mainVm, bool factored, int displayFilter)
        {
            var service = new EvaluationService(mainVm) { DisplayFilter = displayFilter };
            service.RunEvaluation(factored);
            return service.EvaluationText;
        }

        /// <summary>
        /// UI を介さず検定結果 (構造化) を取得する。
        /// 解析結果テーブルに検定を並べるために使う。
        /// 表示フィルタは掛けない (全項目を返す)。
        /// </summary>
        public static EvaluationResult BuildEvaluationResult(MainWindowViewModel mainVm, bool factored)
        {
            var service = new EvaluationService(mainVm) { DisplayFilter = 2 };
            service.RunEvaluation(factored);
            return service.Result;
        }

        private void RunEvaluation(bool factored)
        {
            var sb = new StringBuilder();
            string header = factored ? "【低減後水平解析 検定】" : "【低減前水平解析 検定】";
            sb.AppendLine(header);
            sb.AppendLine(new string('=', 60));
            sb.AppendLine();

            var model = _mainVm.CurrentModel;
            var inputModel = _mainVm.ResultInputModel;
            if (model == null || inputModel == null)
            {
                sb.AppendLine("解析結果がありません。水平解析を実行してください。");
                EvaluationText = sb.ToString();
                Result = new EvaluationResult([]);
                return;
            }

            string seismicGrade = inputModel.FundamentalInput?.SeismicGrade ?? "A";
            sb.AppendLine($"耐震グレード: {seismicGrade}");
            sb.AppendLine();

            // SoilPileをPileBodyNoでキャッシュ
            var soilPileByPileBodyNo = new Dictionary<int, SoilPile>();
            if (inputModel.ElementDivision?.SoilPiles != null)
            {
                foreach (var sp in inputModel.ElementDivision.SoilPiles)
                {
                    if (sp.PileBodyNo > 0 && !soilPileByPileBodyNo.ContainsKey(sp.PileBodyNo))
                        soilPileByPileBodyNo[sp.PileBodyNo] = sp;
                }
            }

            // PileLayoutDataItemをPileBodyNoでキャッシュ（軸力取得用）
            var pileByPileBodyNo = new Dictionary<int, PileLayoutDataItem>();
            if (inputModel.PileLayoutItems != null)
            {
                foreach (var pile in inputModel.PileLayoutItems)
                {
                    if (pile.PileBodyNo > 0 && !pileByPileBodyNo.ContainsKey(pile.PileBodyNo))
                        pileByPileBodyNo[pile.PileBodyNo] = pile;
                }
            }

            // 梁 → 杭 の対応表。
            // 杭体番号 (PileBodyNo) は<b>断面と区間の仕様</b>の番号で、同じ杭体を複数の杭が使う。
            // 杭体番号で杭を引くと最初の 1 本しか当たらず、その杭の軸力で全部の杭を検定してしまう
            // (限界値は軸力で変わるので、他の杭の限界値が別の杭の軸力で引かれていた)。
            var pileByBeam = new Dictionary<Beam, PileLayoutDataItem>();
            if (inputModel.PileLayoutItems != null)
            {
                foreach (var pile in inputModel.PileLayoutItems)
                {
                    if (pile.Beams == null) continue;
                    foreach (var b in pile.Beams) pileByBeam[b] = pile;
                }
            }

            // 杭・荷重ケースごとの M/(Q·d) を出すための最大断面力。
            // せん断耐力は M/(Q·d) に依存する (場所打ちRC の (M/Qd + 1.7)、既製杭の α)。
            // 既定値で固定すると、解析した形状と関係のない耐力で検定することになる。
            var maxForcesByPileCase = BuildMaxForcesByPileCase(inputModel);

            // NM曲線キャッシュ: (PileBodyNo, SegmentIndex, factored, isDamageLimit, level) → (Ns, Ms)
            // Parallel.ForEach から共有アクセスするため ConcurrentDictionary を使用
            var nmCache = new ConcurrentDictionary<(int, int, bool, LimitState, int), (List<double> Ns, List<double> Ms)>();

            // Q-N曲線キャッシュ: (PileBodyNo, SegmentIndex, factored, 限界状態, 損傷限界レベル, M/(Q·d)) → (Ns, Qs)
            // 損傷限界はレベルで、せん断耐力は M/(Q·d) で変わるので、どちらもキーに含める。
            var nqCache = new ConcurrentDictionary<(int, int, bool, LimitState, int, double), (List<double> Ns, List<double> Qs)>();

            // 全ての解析結果の組合せ（LoadCase, LoadCombination, IsLiquefaction）を取得
            var uniqueCombinations = model.AnalysisStepResults
                .GroupBy(r => new
                {
                    LoadCaseName = r.LoadCase?.LoadName ?? "",
                    LoadCombName = r.LoadCombination?.Name ?? "",
                    r.IsLiquefaction
                })
                .Select(g => g.OrderByDescending(r => r.Step).First())
                .ToList();

            // 長期 (常時) / レベル1 / レベル2 に分けて評価。
            // 長期は VL 系ケース (LoadCase.Level == 0)。VL 単独解析を行っていなければ 0 件になる。
            var longTermResults = uniqueCombinations.Where(r => r.LoadCase?.Level == 0).ToList();
            var level1Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 1).ToList();
            var level2Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 2).ToList();

            // ── 検定を実行して項目を集める (テキストはこの後で組む) ──
            var longTermItems = longTermResults.Count > 0
                ? EvaluateLongTerm(model, soilPileByPileBodyNo, pileByPileBodyNo, pileByBeam,
                    maxForcesByPileCase, nmCache, nqCache, longTermResults, factored)
                : [];
            var level1Items = level1Results.Count > 0
                ? EvaluateLevel1(model, soilPileByPileBodyNo, pileByPileBodyNo, pileByBeam,
                    maxForcesByPileCase, nmCache, nqCache, level1Results, factored)
                : [];
            var level2Items = level2Results.Count > 0
                ? EvaluateLevel2(model, soilPileByPileBodyNo, pileByPileBodyNo, pileByBeam,
                    maxForcesByPileCase, nmCache, nqCache, level2Results, factored, seismicGrade)
                : [];
            var inclinationItems = EvaluateBeamAwareInclination();

            // 群杭沈下の沈下量から求めた杭頭変形角 (長期・使用限界)。
            // 常時荷重による即時沈下の不同分で、水平解析の杭頭変位とは別の量なので別項目にする。
            var settlementAngleItems = EvaluateSettlementDeformationAngle();

            // 収束しなかったケースの行に印を付ける。
            // 応答値は釣り合っていないので、OK / NG のどちらとも言えない。
            var convergenceByCase = model.BuildCaseConvergenceMap();
            longTermItems = MarkUnconvergedCases(longTermItems, convergenceByCase);
            level1Items = MarkUnconvergedCases(level1Items, convergenceByCase);
            level2Items = MarkUnconvergedCases(level2Items, convergenceByCase);

            // 未収束の行は OK にも NG にも数えない。
            int totalUnconvergedCount = longTermItems.Count(i => i.IsFromUnconvergedCase)
                + level1Items.Count(i => i.IsFromUnconvergedCase) + level2Items.Count(i => i.IsFromUnconvergedCase);
            int totalNgCount = longTermItems.Count(i => !i.IsFromUnconvergedCase && !i.IsOk)
                + level1Items.Count(i => !i.IsFromUnconvergedCase && !i.IsOk)
                + level2Items.Count(i => !i.IsFromUnconvergedCase && !i.IsOk);
            int totalOkCount = longTermItems.Count(i => !i.IsFromUnconvergedCase && i.IsOk)
                + level1Items.Count(i => !i.IsFromUnconvergedCase && i.IsOk)
                + level2Items.Count(i => !i.IsFromUnconvergedCase && i.IsOk);

            // ── テキスト組立 ──
            if (longTermResults.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("■ 長期（常時）");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                AppendLevelSection(sb, longTermItems);
            }

            if (level1Results.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("■ レベル1地震動");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                AppendLevelSection(sb, level1Items);
            }

            if (level2Results.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"■ レベル2地震動（耐震グレード{seismicGrade}）");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                AppendLevelSection(sb, level2Items);
            }

            if (level1Results.Count == 0 && level2Results.Count == 0)
            {
                sb.AppendLine("レベル1・レベル2の荷重ケースが見つかりません。");
            }

            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine($"チェック合計: OK {totalOkCount} 件 / NG {totalNgCount} 件");
            if (totalNgCount == 0)
                sb.AppendLine("検定: すべてOK");
            else
                sb.AppendLine($"検定: NG項目 {totalNgCount} 件");

            // 未収束のケースがあったときだけ足す。
            // すべて収束していれば従来と 1 文字も変わらない (golden テストが固定している)。
            if (totalUnconvergedCount > 0)
            {
                sb.AppendLine($"未収束のケースの検定: {totalUnconvergedCount} 件");
                sb.AppendLine("  これらは解析が収束しておらず、応答値が釣り合いを満たしていません。");
                sb.AppendLine("  OK / NG の判定はできません。水平解析ウィンドウで計算ステップ数を増やして");
                sb.AppendLine("  やり直すか、耐力が足りているかを確認してください。");
            }

            // ── 個別矩形（基礎梁考慮）反復解析の傾斜角検定 ──
            AppendInclinationSection(sb, inclinationItems);

            // ── 群杭沈下による杭頭変形角 ──
            if (settlementAngleItems.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(new string('=', 60));
                sb.AppendLine("【沈下による杭頭変形角】");
                sb.AppendLine($"沈下検討の対象: "
                    + (_mainVm.ResultInputModel?.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下"));
                sb.AppendLine($"使用限界変形角: {settlementAngleItems[0].Limit:E3} (rad)");
                sb.AppendLine();
                foreach (var item in settlementAngleItems)
                {
                    if (EvaluationResult.PassesFilter(item, DisplayFilter))
                        EvaluationTextFormatter.AppendItem(sb, item);
                }
                sb.AppendLine();
            }

            var all = new List<EvaluationItem>(
                longTermItems.Count + level1Items.Count + level2Items.Count
                + inclinationItems.Count + settlementAngleItems.Count);
            all.AddRange(longTermItems);
            all.AddRange(level1Items);
            all.AddRange(level2Items);
            all.AddRange(inclinationItems);
            all.AddRange(settlementAngleItems);
            Result = new EvaluationResult(all);

            EvaluationText = sb.ToString();
        }

        /// <summary>
        /// 収束しなかった荷重ケースから作られた検定項目に印を付ける。
        ///
        /// 検定は項目を作る場所が多い (曲げ・せん断・回転角・変形角・支持力…) ので、
        /// 作る側すべてに収束状態を配るのではなく、<b>出来上がった項目に後から付ける</b>。
        /// 項目は自分の荷重条件を名乗っている (<c>IHasLoadCondition</c>) ので、
        /// それを鍵にケース単位の収束状態を引ける。
        ///
        /// 液状化の別が無い項目 (基礎梁の傾斜角など) は水平解析の外なので対象外。
        /// </summary>
        private static List<EvaluationItem> MarkUnconvergedCases(
            List<EvaluationItem> items,
            Dictionary<(string LoadCaseName, string LoadCombinationName, bool IsLiquefaction), FEM.StepStatus> convergenceByCase)
        {
            if (items.Count == 0 || convergenceByCase.Count == 0) return items;

            var marked = new List<EvaluationItem>(items.Count);
            foreach (var item in items)
            {
                if (item.IsLiquefaction is not bool liq)
                {
                    marked.Add(item);
                    continue;
                }

                var key = (item.LoadCaseName, item.LoadCombinationName, liq);
                marked.Add(convergenceByCase.TryGetValue(key, out var status) && status != FEM.StepStatus.Converged
                    ? item with { CaseConvergence = status }
                    : item);
            }

            return marked;
        }

        /// <summary>
        /// レベル別セクションの本文。表示フィルタはここで掛ける
        /// (件数の集計は常に全項目が対象なので、集計後に絞る)。
        /// </summary>
        private void AppendLevelSection(StringBuilder sb, List<EvaluationItem> items)
        {
            foreach (var item in items)
            {
                if (EvaluationResult.PassesFilter(item, DisplayFilter))
                    EvaluationTextFormatter.AppendItem(sb, item);
            }

            int ng = items.Count(i => !i.IsOk);
            if (items.Count == 0)
                sb.AppendLine("  → チェック対象なし");
            else if (ng == 0)
                sb.AppendLine("  → NG項目なし");
            sb.AppendLine();
        }

        /// <summary>
        /// 個別矩形（基礎梁考慮）反復解析の結果から、各基礎梁の傾斜角を検定する。
        /// 傾斜角 = (Uz_j - Uz_i) / L、許容値 1/300 (基礎指針) を既定値として比較。
        /// </summary>
        private List<EvaluationItem> EvaluateBeamAwareInclination()
        {
            var items = new List<EvaluationItem>();

            var pgs = _mainVm.ResultInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return items;
            var beamAwareCases = pgs.CaseRecords.Where(r => r.IsBeamAware).ToList();
            if (beamAwareCases.Count == 0) return items;

            var inputModel = _mainVm.ResultInputModel;
            if (inputModel?.FoundationBeamInput?.Beams == null
                || inputModel.FoundationBeamInput.Beams.Count == 0) return items;

            const double inclinationLimit = 1.0 / 300.0;

            foreach (var rec in beamAwareCases)
            {
                if (rec.NodeResults == null || rec.NodeResults.Count == 0) continue;

                var uzByName = rec.NodeResults.ToDictionary(n => n.NodeName, n => n.Uz_mm);

                foreach (var fbBeam in inputModel.FoundationBeamInput.Beams)
                {
                    var coordsI = inputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    var coordsJ = inputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                    if (coordsI == null || coordsJ == null) continue;

                    double dx = coordsJ.Value.X - coordsI.Value.X;
                    double dy = coordsJ.Value.Y - coordsI.Value.Y;
                    double L = Math.Sqrt(dx * dx + dy * dy);
                    if (L < 1e-6) continue;

                    string nameI = ResolveFemNodeName(inputModel, fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    string nameJ = ResolveFemNodeName(inputModel, fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                    if (string.IsNullOrEmpty(nameI) || string.IsNullOrEmpty(nameJ)) continue;
                    if (!uzByName.TryGetValue(nameI, out double uzI)) continue;
                    if (!uzByName.TryGetValue(nameJ, out double uzJ)) continue;

                    double inclination = Math.Abs((uzJ - uzI) * 0.001 / L);
                    int beamNo = inputModel.FoundationBeamInput.GetBeamNo(fbBeam);

                    items.Add(new EvaluationItem
                    {
                        Kind = EvaluationKind.FoundationBeamInclination,
                        Level = 0,   // 水平解析の地震動レベルとは別軸
                        Category = "基礎梁の傾斜角",
                        TargetName = $"FoundationBeam-{beamNo}",
                        FoundationBeamNo = beamNo,
                        LoadCaseName = rec.LoadCaseName ?? "",
                        // 反復沈下解析には液状化の区別が無い (null のまま)
                        Response = inclination,
                        Limit = inclinationLimit,
                        Unit = "rad",
                        BeamLength = L,
                        // 判定は従来どおり「限界未満なら OK」(ちょうど等しいと NG)
                        IsOk = inclination < inclinationLimit,
                    });
                }
            }

            return items;
        }

        /// <summary>
        /// 傾斜角検定の本文。ケースごとにまとめ、末尾に合計と最大傾斜角を出す。
        /// 最大傾斜角は<b>表示フィルタに関係なく</b>そのケースの全項目から求める
        /// (従来の実装と同じ)。
        /// </summary>
        private void AppendInclinationSection(StringBuilder sb, List<EvaluationItem> items)
        {
            if (items.Count == 0) return;

            const double inclinationLimit = 1.0 / 300.0;

            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("【個別矩形（基礎梁考慮）反復解析 傾斜角検定】");
            sb.AppendLine($"許容傾斜角: 1/300 = {inclinationLimit:E3} (rad)");
            sb.AppendLine();

            foreach (var caseGroup in items.GroupBy(i => i.LoadCaseName))
            {
                sb.AppendLine($"--- ケース: {caseGroup.Key} ---");

                var caseItems = caseGroup.ToList();
                foreach (var item in caseItems)
                {
                    if (EvaluationResult.PassesFilter(item, DisplayFilter))
                        EvaluationTextFormatter.AppendItem(sb, item);
                }

                int caseOk = caseItems.Count(i => i.IsOk);
                int caseNg = caseItems.Count - caseOk;
                var worst = caseItems.OrderByDescending(i => i.Response).First();
                string maxBeamName = worst.Response > 0 ? worst.TargetName : "";

                sb.AppendLine($"  → ケース合計: OK {caseOk} 件 / NG {caseNg} 件 / 最大傾斜角 = {worst.Response:E3} rad ({maxBeamName})");
                sb.AppendLine();
            }
        }

        private static string ResolveFemNodeName(InputModel inputModel, NodeReferenceType type, Guid id)
        {
            switch (type)
            {
                case NodeReferenceType.FoundationNode:
                    var fnode = inputModel.FoundationBeamInput?.Nodes?.FirstOrDefault(n => n.Id == id);
                    return fnode != null ? $"FoundationNode-{fnode.No}" : null;
                case NodeReferenceType.PileLayout:
                    var pile = inputModel.PileLayoutItems?.FirstOrDefault(p => p.UniqueId == id);
                    return pile != null ? $"FoundationNode-P{pile.No}" : null;
                case NodeReferenceType.GeneralNode:
                    var node = inputModel.InputNodes?.FirstOrDefault(n => n.UniqueId == id);
                    return node != null ? $"InputNode-{node.No}" : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 長期 (常時) の評価。
        /// VL 系ケース (LoadCase.Level == 0) の曲げとせん断を<b>使用限界</b>で検定する。
        /// 回転角は地震時の限界状態で見るものなので、ここでは扱わない。
        /// VL 単独ケースを解析していない場合、そもそも呼ばれない (項目 0 件)。
        /// </summary>
        private List<EvaluationItem> EvaluateLongTerm(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)> maxForcesByPileCase,
            ConcurrentDictionary<(int, int, bool, LimitState, int), (List<double> Ns, List<double> Ms)> nmCache,
            ConcurrentDictionary<(int, int, bool, LimitState, int, double), (List<double> Ns, List<double> Qs)> nqCache,
            List<AnalysisStepResult> results, bool factored)
        {
            var items = new List<EvaluationItem>();

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";

                // 曲げチェック: 使用限界
                items.AddRange(CheckMPhiLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, nmCache,
                    stepResult, factored, LimitState.Service,
                    lcName, combName));

                // せん断チェック: 使用限界
                items.AddRange(CheckShearLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, maxForcesByPileCase, nqCache,
                    stepResult, factored, LimitState.Service,
                    lcName, combName));

                // 杭頭 2 点間の変形角: 使用限界
                items.AddRange(CheckPileHeadDeformationAngle(
                    stepResult, LimitState.Service, lcName, combName));
            }

            return items;
        }

        /// <summary>
        /// レベル1地震動の評価
        /// - i端もしくはj端がM-φ関係が損傷限界状態を超える場合
        /// - i端もしくはj端のせん断力が損傷限界のQ-N曲線を超える場合
        /// - 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private List<EvaluationItem> EvaluateLevel1(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)> maxForcesByPileCase,
            ConcurrentDictionary<(int, int, bool, LimitState, int), (List<double> Ns, List<double> Ms)> nmCache,
            ConcurrentDictionary<(int, int, bool, LimitState, int, double), (List<double> Ns, List<double> Qs)> nqCache,
            List<AnalysisStepResult> results, bool factored)
        {
            var items = new List<EvaluationItem>();

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";

                // M-φ チェック: 損傷限界
                items.AddRange(CheckMPhiLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, nmCache,
                    stepResult, factored, LimitState.Damage,
                    lcName, combName));

                // せん断チェック: 損傷限界
                items.AddRange(CheckShearLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, maxForcesByPileCase, nqCache,
                    stepResult, factored, LimitState.Damage,
                    lcName, combName));

                // 杭頭 2 点間の変形角: 損傷限界
                items.AddRange(CheckPileHeadDeformationAngle(
                    stepResult, LimitState.Damage, lcName, combName));

                // θ チェック: 場所打ちRC杭で 1/100rad
                items.AddRange(CheckThetaLimit(model, soilPileByPileBodyNo,
                    stepResult, lcName, combName));
            }

            return items;
        }

        /// <summary>
        /// レベル2地震動の評価
        /// 耐震グレードA: 安全限界 / 耐震グレードS: 損傷限界
        /// + 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private List<EvaluationItem> EvaluateLevel2(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)> maxForcesByPileCase,
            ConcurrentDictionary<(int, int, bool, LimitState, int), (List<double> Ns, List<double> Ms)> nmCache,
            ConcurrentDictionary<(int, int, bool, LimitState, int, double), (List<double> Ns, List<double> Qs)> nqCache,
            List<AnalysisStepResult> results, bool factored, string seismicGrade)
        {
            var items = new List<EvaluationItem>();
            bool isDamageLimit = seismicGrade == "S"; // S→損傷限界、A→安全限界

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";

                items.AddRange(CheckMPhiLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, nmCache,
                    stepResult, factored,
                    isDamageLimit ? LimitState.Damage : LimitState.Ultimate,
                    lcName, combName));

                // せん断も曲げと同じ限界状態に揃える (グレードS→損傷限界、A→安全限界)
                items.AddRange(CheckShearLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, pileByBeam, maxForcesByPileCase, nqCache,
                    stepResult, factored,
                    isDamageLimit ? LimitState.Damage : LimitState.Ultimate,
                    lcName, combName));

                // 杭頭 2 点間の変形角も同じ限界状態に揃える
                items.AddRange(CheckPileHeadDeformationAngle(
                    stepResult,
                    isDamageLimit ? LimitState.Damage : LimitState.Ultimate,
                    lcName, combName));

                items.AddRange(CheckThetaLimit(model, soilPileByPileBodyNo,
                    stepResult, lcName, combName));
            }

            return items;
        }

        /// <summary>
        /// 各梁要素のi端・j端のモーメントが限界状態のNM相関曲線を超えるかチェック
        /// 軸力は荷重ケースに応じたユーザー入力値（AxialForceLevel1s/AxialForceLevel2s）を使用
        /// </summary>
        private List<EvaluationItem> CheckMPhiLimitForBeams(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            ConcurrentDictionary<(int, int, bool, LimitState, int), (List<double> Ns, List<double> Ms)> nmCache,
            AnalysisStepResult stepResult, bool factored, LimitState momentLimit,
            string lcName, string combName)
        {
            string limitName = LimitStateName(momentLimit);
            int level = stepResult.LoadCase?.Level ?? 1;

            // Beam ごとに独立。並列で項目を produce し、最後に添字順に連結する
            // (順序を添字で固定しているので、並列でも項目の並びは決定的)。
            var beamsArr = model.Beams.ToArray();
            var perBeamResults = new List<EvaluationItem>[beamsArr.Length];

            Parallel.For(0, beamsArr.Length, idx =>
            {
                var beam = beamsArr[idx];
                var found = new List<EvaluationItem>(2);

                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                {
                    perBeamResults[idx] = found; return;
                }

                // BeamResultを検索
                var result = beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (result?.CumulativeForce == null) { perBeamResults[idx] = found; return; }

                // SoilPileからPileSectionを取得
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) { perBeamResults[idx] = found; return; }
                if (soilPile.PileBodySegments == null || seg >= soilPile.PileBodySegments.Count) { perBeamResults[idx] = found; return; }
                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null) { perBeamResults[idx] = found; return; }

                // 軸力: 荷重ケースに応じたユーザー入力値 (kN)。
                // 杭は梁から引く。杭体番号で引くとその杭体を使う最初の 1 本しか当たらず、
                // 同じ杭体を共有する他の杭まで同じ軸力で検定してしまう。
                var pileItem = ResolvePile(beam, pb, pileByBeam, pileByPileBodyNo);
                double axialN_kN = 0.0;
                if (pileItem != null)
                {
                    int lcNo = stepResult.LoadCase?.No ?? 0;
                    int level = stepResult.LoadCase?.Level ?? 1;
                    // 地震時軸力優先・未入力 (0) / 範囲外は常時軸力。
                    // グラフ・計算書の限界線と同じ軸力を使う (食い違うと判定が一致しない)。
                    axialN_kN = pileItem.GetDesignAxialForce(lcNo, level);
                }

                // NM相関曲線をキャッシュから取得 (ConcurrentDictionary、初回のみ計算)
                int loadCaseLevel = stepResult.LoadCase?.Level ?? 1;
                var cacheKey = (pb, seg, factored, momentLimit, loadCaseLevel);
                var nmCurve = nmCache.GetOrAdd(cacheKey, _ => GetNMCurve(section, factored, momentLimit, loadCaseLevel));
                if (nmCurve.Ns == null || nmCurve.Ms == null || nmCurve.Ns.Count < 2) { perBeamResults[idx] = found; return; }

                // NM相関曲線から許容モーメントを補間
                double allowableM = InterpolateAllowableMoment(nmCurve.Ns, nmCurve.Ms, axialN_kN);
                // 範囲外の軸力では NaN が返る (NaN <= 0 は false なので、必ず > 0 で判定すること)
                if (!(allowableM > 0)) { perBeamResults[idx] = found; return; }

                // i端モーメント |M| = √(Myi² + Mzi²)
                double mI = Math.Sqrt(
                    result.CumulativeForce.Myi * result.CumulativeForce.Myi +
                    result.CumulativeForce.Mzi * result.CumulativeForce.Mzi);

                // j端モーメント |M| = √(Myj² + Mzj²)
                double mJ = Math.Sqrt(
                    result.CumulativeForce.Myj * result.CumulativeForce.Myj +
                    result.CumulativeForce.Mzj * result.CumulativeForce.Mzj);

                // i端チェック (判定は従来どおり「超えたら NG」)
                found.Add(MakeMomentItem(mI, allowableM, "i端"));

                // j端チェック
                found.Add(MakeMomentItem(mJ, allowableM, "j端"));

                perBeamResults[idx] = found;

                EvaluationItem MakeMomentItem(double response, double limit, string end) => new()
                {
                    Kind = EvaluationKind.PileSectionMoment,
                    Level = level,
                    Category = $"杭体曲げ ({limitName})",
                    LimitName = limitName,
                    TargetName = beam.Name,
                    EndLabel = end,
                    PileBodyNo = pb,
                    PileNo = pileItem?.PileNo,
                    SegmentIndex = seg,
                    LoadCaseName = lcName,
                    LoadCombinationName = combName,
                    IsLiquefaction = stepResult.IsLiquefaction,
                    Response = response,
                    Limit = limit,
                    Unit = "kN·m",
                    AxialForce = axialN_kN,
                    IsOk = !(response > limit),
                };
            });

            // 添字順に連結 (並列実行でも並びは変わらない)
            var items = new List<EvaluationItem>();
            for (int i = 0; i < perBeamResults.Length; i++)
            {
                if (perBeamResults[i] != null) items.AddRange(perBeamResults[i]);
            }
            return items;
        }

        /// <summary>
        /// 杭・荷重ケースごとの最大断面力 (|M|, |Q|) を集める。M/(Q·d) の算定に使う。
        ///
        /// 杭 1 本ぶんの最大値から求めるのは、無効化されていた検定比の実装と同じ扱い。
        /// 要素ごとの M/Q を使うと、モーメントの反曲点付近で Q だけが大きい要素の
        /// M/(Q·d) が 0 に近づき、せん断耐力が跳ね上がる。
        /// </summary>
        private static Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)>
            BuildMaxForcesByPileCase(InputModel inputModel)
        {
            var map = new Dictionary<(PileLayoutDataItem, string), (double MaxM, double MaxQ)>();
            if (inputModel.PileLayoutItems == null) return map;

            foreach (var pile in inputModel.PileLayoutItems)
            {
                if (pile.Beams == null) continue;
                foreach (var beam in pile.Beams)
                {
                    if (beam.BeamResults == null) continue;
                    foreach (var r in beam.BeamResults)
                    {
                        var force = r.CumulativeForce;
                        if (force == null || r.LoadCase == null) continue;

                        var key = (pile, r.LoadCase.LoadName ?? "");
                        double m = force.MabsMax;   // max(Mi, Mj) [kNm]
                        double q = force.FabsMax;   // max(Qi, Qj) [kN]
                        if (map.TryGetValue(key, out var prev))
                            map[key] = (Math.Max(prev.MaxM, m), Math.Max(prev.MaxQ, q));
                        else
                            map[key] = (m, q);
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// せん断耐力の算定に使う M/(Q·d) を返す。
        ///
        /// 解析した断面力から求める。求められないとき (せん断力が 0、有効せいが不明、
        /// その荷重ケースの結果が無い) だけ既定値に落とす。
        /// </summary>
        private static double ResolveMonQd(
            PileLayoutDataItem? pile, string loadCaseName, PileSection section,
            Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)> maxForces)
        {
            if (pile == null) return PileSection.DefaultMonQd;

            double d = section.EffectiveDepth;   // [mm]
            if (!(d > 0)) return PileSection.DefaultMonQd;

            if (!maxForces.TryGetValue((pile, loadCaseName), out var mf)) return PileSection.DefaultMonQd;
            if (!(mf.MaxQ > 0)) return PileSection.DefaultMonQd;

            // M [kNm] → [N·mm] は ×1e6、Q [kN] → [N] は ×1e3
            double monQd = mf.MaxM * 1e6 / (mf.MaxQ * 1e3 * d);
            return double.IsFinite(monQd) && monQd > 0 ? monQd : PileSection.DefaultMonQd;
        }

        /// <summary>
        /// 梁が属する杭を返す。
        ///
        /// 正は<b>梁 → 杭</b>の対応 (杭が自分の梁を持っている)。
        /// 杭体番号での引き当ては、対応表に無い梁 (古い結果など) のための保険で、
        /// 同じ杭体を共有する杭が複数あると最初の 1 本しか当たらない。
        /// </summary>
        private static PileLayoutDataItem? ResolvePile(
            Beam beam, int pileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo)
        {
            if (pileByBeam.TryGetValue(beam, out var pile)) return pile;
            return pileByPileBodyNo.TryGetValue(pileBodyNo, out var fallback) ? fallback : null;
        }

        /// <summary>
        /// 検定で使う限界状態。曲げとせん断で同じ割り当てにする。
        /// 長期(常時)=使用限界 / レベル1=損傷限界 / レベル2=安全限界 (耐震グレードS は損傷限界)。
        /// </summary>
        internal enum LimitState
        {
            Service,
            Damage,
            Ultimate,
        }

        private static string LimitStateName(LimitState limit) => limit switch
        {
            LimitState.Service => "使用限界",
            LimitState.Damage => "損傷限界",
            _ => "安全限界",
        };

        /// <summary>
        /// 各梁要素のi端・j端のせん断力が限界状態の Q-N 曲線を超えるかチェックする。
        ///
        /// 曲げ (<see cref="CheckMPhiLimitForBeams"/>) と対になる検定で、作りも揃えてある。
        /// せん断耐力は杭種によって軸力で変わる (PHC・PRC の斜めひび割れ、鋼管系の安全限界) ので、
        /// 曲げと同じく荷重ケースに応じた軸力で限界値を補間する。
        /// さらに M/(Q·d) にも依存するので、解析した断面力から求めた値で曲線を作る。
        /// </summary>
        private List<EvaluationItem> CheckShearLimitForBeams(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<Beam, PileLayoutDataItem> pileByBeam,
            Dictionary<(PileLayoutDataItem Pile, string LoadCase), (double MaxM, double MaxQ)> maxForcesByPileCase,
            ConcurrentDictionary<(int, int, bool, LimitState, int, double), (List<double> Ns, List<double> Qs)> nqCache,
            AnalysisStepResult stepResult, bool factored, LimitState shearLimit,
            string lcName, string combName)
        {
            string limitName = LimitStateName(shearLimit);
            int level = stepResult.LoadCase?.Level ?? 1;

            var beamsArr = model.Beams.ToArray();
            var perBeamResults = new List<EvaluationItem>[beamsArr.Length];

            Parallel.For(0, beamsArr.Length, idx =>
            {
                var beam = beamsArr[idx];
                var found = new List<EvaluationItem>(2);

                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                {
                    perBeamResults[idx] = found; return;
                }

                var result = beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (result?.CumulativeForce == null) { perBeamResults[idx] = found; return; }

                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) { perBeamResults[idx] = found; return; }
                if (soilPile.PileBodySegments == null || seg >= soilPile.PileBodySegments.Count) { perBeamResults[idx] = found; return; }
                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null) { perBeamResults[idx] = found; return; }

                // 軸力: 曲げと同じ値を使う (食い違うと同じ断面で限界線の前提が 2 通りになる)
                var pileItem = ResolvePile(beam, pb, pileByBeam, pileByPileBodyNo);
                double axialN_kN = 0.0;
                if (pileItem != null)
                {
                    int lcNo = stepResult.LoadCase?.No ?? 0;
                    int lcLevel = stepResult.LoadCase?.Level ?? 1;
                    axialN_kN = pileItem.GetDesignAxialForce(lcNo, lcLevel);
                }

                // 損傷限界はレベルで低減係数が変わる (レベル1: β2 なし / レベル2: β1×β2)。
                // グラフ・計算書と同じ規則で曲線を選ぶ。
                int damageLevel = shearLimit == LimitState.Damage ? Math.Max(level, 1) : 2;

                // せん断耐力は M/(Q·d) に依存するので、解析した断面力から求めた値を使う。
                //
                // 丸めた値をキーにするなら曲線もその丸めた値で作ること。
                // 生の値で作ると、同じキーに丸められる 2 つの値のうち先にキャッシュへ入れたほうが
                // 採用され、結果が実行ごとに変わる (安全限界せん断が 0.1 kN 揺れた)。
                double monQd = Math.Round(
                    ResolveMonQd(pileItem, lcName, section, maxForcesByPileCase), 3);

                var nqCurve = nqCache.GetOrAdd((pb, seg, factored, shearLimit, damageLevel, monQd),
                    _ => GetNQCurve(section, factored, shearLimit, damageLevel, monQd));
                if (nqCurve.Ns == null || nqCurve.Qs == null || nqCurve.Ns.Count < 2) { perBeamResults[idx] = found; return; }

                double allowableQ = InterpolateAllowableMoment(nqCurve.Ns, nqCurve.Qs, axialN_kN);
                // 範囲外の軸力では NaN が返る (NaN <= 0 は false なので、必ず > 0 で判定すること)
                if (!(allowableQ > 0)) { perBeamResults[idx] = found; return; }

                // i端・j端のせん断力 |Q| = √(Fy² + Fz²)
                double qI = result.CumulativeForce.Fi;
                double qJ = result.CumulativeForce.Fj;

                found.Add(MakeShearItem(qI, allowableQ, "i端"));
                found.Add(MakeShearItem(qJ, allowableQ, "j端"));

                perBeamResults[idx] = found;

                EvaluationItem MakeShearItem(double response, double limit, string end) => new()
                {
                    Kind = EvaluationKind.PileSectionShear,
                    Level = level,
                    Category = $"杭体せん断 ({limitName})",
                    LimitName = limitName,
                    TargetName = beam.Name,
                    EndLabel = end,
                    PileBodyNo = pb,
                    PileNo = pileItem?.PileNo,
                    SegmentIndex = seg,
                    LoadCaseName = lcName,
                    LoadCombinationName = combName,
                    IsLiquefaction = stepResult.IsLiquefaction,
                    Response = response,
                    Limit = limit,
                    Unit = "kN",
                    AxialForce = axialN_kN,
                    MonQd = monQd,
                    // 判定は曲げと同じ「超えたら NG」
                    IsOk = !(response > limit),
                };
            });

            var items = new List<EvaluationItem>();
            for (int i = 0; i < perBeamResults.Length; i++)
            {
                if (perBeamResults[i] != null) items.AddRange(perBeamResults[i]);
            }
            return items;
        }

        /// <summary>
        /// Q-N 曲線を取得する (低減前/低減後 × 限界状態 × 損傷限界のレベル × M/(Q·d))。
        /// 画面のグラフ・計算書に描かれる限界線とまったく同じ曲線を使う。
        /// </summary>
        private static (List<double> Ns, List<double> Qs) GetNQCurve(
            PileSection section, bool factored, LimitState limit, int damageLevel, double monQd)
        {
            try
            {
                // 曲線の取り出しは PileSection.GetQNCurvesForLevel に一本化してある。
                // キャッシュ済みプロパティを直に読むと損傷限界のレベルと帯筋が反映されず、
                // グラフ・計算書と違う曲線で検定することになる。
                var curves = section.GetQNCurvesForLevel(damageLevel, monQd);
                var nq = (factored, limit) switch
                {
                    (false, LimitState.Service) => curves.UnfactoredService,
                    (false, LimitState.Damage) => curves.UnfactoredDamage,
                    (false, _) => curves.UnfactoredUltimate,
                    (true, LimitState.Service) => curves.FactoredService,
                    (true, LimitState.Damage) => curves.FactoredDamage,
                    (true, _) => curves.FactoredUltimate,
                };
                return (nq.N, nq.Q);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "検定: Q-N 曲線の取得に失敗 (PileBodyType={Type})", section.PileBodyType);
                return (null, null);
            }
        }

        // ── 杭頭 2 点間の変形角 ─────────────────────────────
        //
        // すべての杭頭の組について、鉛直変位の差を杭間の水平距離で割った角
        //   θ = |Uz_i − Uz_j| / √((Xi−Xj)² + (Yi−Yj)²)
        // を求め、その最大値を限界値と比べる。基礎の回転・不同沈下による変形角。
        //
        // 限界値は限界状態ごとの既定値。曲げ・せん断と同じ割り当て
        // (長期 = 使用限界 / レベル1 = 損傷限界 / レベル2 = 終局限界) で使う。

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の杭頭 安全限界回転角の既定値 1/100 rad。基本設定で変更できる。
        /// 杭頭固定の場合にこの杭種だけ規定がある (他の杭種には無く、杭頭半固定は工法ごとの閾値)。
        /// </summary>
        internal const double DefaultInsituRcUltimateRotationAngleLimit = 1.0 / 100.0;

        /// <summary>使用限界の変形角の既定値 1.0×10⁻³ rad (= 1/1000)。基本設定で変更できる。</summary>
        internal const double DefaultServiceDeformationAngleLimit = 1.0e-3;

        /// <summary>損傷限界の変形角の既定値 5.0×10⁻³ rad (= 1/200)。基本設定で変更できる。</summary>
        internal const double DefaultDamageDeformationAngleLimit = 5.0e-3;

        /// <summary>終局限界の変形角の既定値 7.0×10⁻³ rad (≒ 1/143)。基本設定で変更できる。</summary>
        internal const double DefaultUltimateDeformationAngleLimit = 7.0e-3;

        /// <summary>
        /// 限界状態に対応する変形角の限界値。基本設定の値を使い、
        /// 0 以下 (旧いファイルで未設定) のときだけ既定値に落とす。
        /// </summary>
        internal static double DeformationAngleLimitFor(FundamentalInput? fundamental, LimitState limit)
        {
            double value = limit switch
            {
                LimitState.Service => fundamental?.ServiceDeformationAngleLimit ?? 0.0,
                LimitState.Damage => fundamental?.DamageDeformationAngleLimit ?? 0.0,
                _ => fundamental?.UltimateDeformationAngleLimit ?? 0.0,
            };
            if (value > 0 && double.IsFinite(value)) return value;

            return limit switch
            {
                LimitState.Service => DefaultServiceDeformationAngleLimit,
                LimitState.Damage => DefaultDamageDeformationAngleLimit,
                _ => DefaultUltimateDeformationAngleLimit,
            };
        }

        /// <summary>
        /// 杭頭の (X, Y, 鉛直変位) から、全ペアの変形角の最大値と、その組を返す。
        /// 杭が 2 本未満、または杭間距離が 0 のときは null。
        /// </summary>
        /// <param name="heads">(杭No, X[m], Y[m], 鉛直変位[m])。符号はそのままでよい (差で使う)</param>
        internal static (double Angle, int PileNoA, int PileNoB)? MaxDeformationAngle(
            IReadOnlyList<(int PileNo, double X, double Y, double Uz)> heads)
        {
            if (heads.Count < 2) return null;

            double maxAngle = -1.0;
            int a = 0, b = 0;

            for (int i = 0; i < heads.Count - 1; i++)
            {
                for (int j = i + 1; j < heads.Count; j++)
                {
                    double dx = heads[i].X - heads[j].X;
                    double dy = heads[i].Y - heads[j].Y;
                    double span = Math.Sqrt(dx * dx + dy * dy);
                    if (span < 1e-9) continue;   // 同じ位置の杭 (重なり) は角が定義できない

                    double angle = Math.Abs(heads[i].Uz - heads[j].Uz) / span;
                    if (angle > maxAngle)
                    {
                        maxAngle = angle;
                        a = heads[i].PileNo;
                        b = heads[j].PileNo;
                    }
                }
            }

            return maxAngle < 0 ? null : (maxAngle, a, b);
        }

        /// <summary>
        /// 水平解析の杭頭鉛直変位から、杭頭 2 点間の変形角を検定する。
        /// 1 つの荷重条件につき<b>最大値 1 件</b>を出す (全ペアを並べると読めないため、
        /// どの組で最大になったかは対象名に入れる)。
        /// </summary>
        private List<EvaluationItem> CheckPileHeadDeformationAngle(
            AnalysisStepResult stepResult, LimitState limit,
            string lcName, string combName)
        {
            var items = new List<EvaluationItem>();
            var inputModel = _mainVm.ResultInputModel;
            if (inputModel?.PileLayoutItems == null) return items;

            var heads = new List<(int PileNo, double X, double Y, double Uz)>();
            foreach (var pile in inputModel.PileLayoutItems)
            {
                // 杭頭の節点は<b>梁から</b>引く。PileNodes も梁も [JsonIgnore] だが、
                // 結果セットの張り直しが対象にしているのは梁の側で、
                // PileNodes から辿ると結果の付いていない節点に当たることがある。
                var head = ResolvePileHeadNode(pile);
                if (head == null) continue;

                var nr = head.NodeResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (nr?.CumulativeDisp == null) continue;
                // NodeDisp は m 単位、杭の座標も m なので、そのまま割れば rad になる
                heads.Add((pile.PileNo, pile.Point3D.X, pile.Point3D.Y, nr.CumulativeDisp.Uz));
            }

            var max = MaxDeformationAngle(heads);
            if (max == null) return items;

            string limitName = LimitStateName(limit);
            double limitValue = DeformationAngleLimitFor(inputModel.FundamentalInput, limit);

            items.Add(new EvaluationItem
            {
                Kind = EvaluationKind.PileHeadDeformationAngle,
                Level = stepResult.LoadCase?.Level ?? 0,
                Category = $"杭頭変形角 ({limitName})",
                LimitName = limitName,
                TargetName = $"杭No.{max.Value.PileNoA} − 杭No.{max.Value.PileNoB}",
                PileNo = max.Value.PileNoA,
                LoadCaseName = lcName,
                LoadCombinationName = combName,
                IsLiquefaction = stepResult.IsLiquefaction,
                Response = max.Value.Angle,
                Limit = limitValue,
                Unit = "rad",
                // 判定は曲げ・せん断と同じ「超えたら NG」
                IsOk = !(max.Value.Angle > limitValue),
            });

            return items;
        }

        /// <summary>
        /// 杭頭の節点を返す。杭の梁のうち最も浅い区間を取り、その両端で Z の大きい方。
        /// 梁が張られていない (解析していない) 杭では null。
        /// </summary>
        private static Node? ResolvePileHeadNode(PileLayoutDataItem pile)
        {
            if (pile.Beams == null || pile.Beams.Count == 0) return null;

            Beam? top = null;
            foreach (var b in pile.Beams)
            {
                if (b?.NodeI?.Coord == null || b.NodeJ?.Coord == null) continue;
                if (b.SegmentIndex is not int seg) continue;
                if (top == null || seg < (top.SegmentIndex ?? int.MaxValue)) top = b;
            }
            if (top == null) return null;

            return top.NodeI.Coord.Z >= top.NodeJ.Coord.Z ? top.NodeI : top.NodeJ;
        }

        /// <summary>
        /// 群杭沈下解析の沈下量から、杭頭 2 点間の変形角を検定する (使用限界)。
        ///
        /// 水平解析の杭頭変位と別の項目にしてある。こちらは常時荷重による
        /// 群杭の即時沈下 (Steinbrenner の弾性沈下) の不同分で、
        /// 地震時の杭頭変位とは別の量。
        ///
        /// <b>杭基礎では圧密沈下は生じない</b> (圧密沈下の検討が要るのは
        /// 直接基礎を圧密層に載せる場合)。基礎指針'19 表5.3.8 の常時荷重・使用限界には
        /// 即時沈下 1×10⁻³ と圧密沈下 2×10⁻³ が併記されているが、ここで使うのは前者。
        /// </summary>
        private List<EvaluationItem> EvaluateSettlementDeformationAngle()
        {
            var items = new List<EvaluationItem>();

            var inputModel = _mainVm.ResultInputModel;
            var pgs = inputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null || inputModel?.PileLayoutItems == null) return items;

            // 杭No → 座標
            var coords = new Dictionary<int, (double X, double Y)>();
            foreach (var pile in inputModel.PileLayoutItems)
                coords[pile.PileNo] = (pile.Point3D.X, pile.Point3D.Y);

            // 沈下検討の対象 (基本設定)。単杭沈下だけか、群杭沈下を足した合計か。
            bool includesGroup = inputModel.FundamentalInput?.SettlementDesignIncludesGroup ?? true;
            string basisName = inputModel.FundamentalInput?.SettlementDesignBasisName ?? "単杭＋群杭沈下";

            // 単杭沈下 (常時) は杭ごとに 1 つ。群杭沈下はケースごとに持つ。
            var singleByPileNo = new Dictionary<int, double>();
            foreach (var pile in inputModel.PileLayoutItems)
                singleByPileNo[pile.PileNo] = pile.SinglePileSettlementVL;   // m

            foreach (var rec in pgs.CaseRecords)
            {
                if (rec.PileSettlements_mm == null || rec.PileSettlements_mm.Count < 2) continue;

                var heads = new List<(int PileNo, double X, double Y, double Uz)>();
                foreach (var kv in rec.PileSettlements_mm.OrderBy(kv => kv.Key))
                {
                    if (!coords.TryGetValue(kv.Key, out var c)) continue;

                    // 単杭沈下 [m] + 群杭沈下 [mm→m]。単杭沈下だけで検討するときは群杭分を足さない。
                    singleByPileNo.TryGetValue(kv.Key, out double single);
                    double settlement = single + (includesGroup ? kv.Value * 1e-3 : 0.0);
                    heads.Add((kv.Key, c.X, c.Y, settlement));
                }

                var max = MaxDeformationAngle(heads);
                if (max == null) continue;

                double limitValue = DeformationAngleLimitFor(inputModel.FundamentalInput, LimitState.Service);
                string caseName = string.IsNullOrEmpty(rec.LoadCaseName) ? "群杭沈下" : rec.LoadCaseName;
                string typeName = string.IsNullOrEmpty(rec.LoadingType) ? "" : $"（{rec.LoadingType}）";

                items.Add(new EvaluationItem
                {
                    Kind = EvaluationKind.PileHeadDeformationAngle,
                    Level = 0,
                    Category = $"杭頭変形角 ({basisName}・使用限界)",
                    LimitName = "使用限界",
                    TargetName = $"杭No.{max.Value.PileNoA} − 杭No.{max.Value.PileNoB}{typeName}",
                    PileNo = max.Value.PileNoA,
                    LoadCaseName = caseName,
                    // 沈下解析に液状化の区別は無い (null のまま)
                    Response = max.Value.Angle,
                    Limit = limitValue,
                    Unit = "rad",
                    IsOk = !(max.Value.Angle > limitValue),
                });
            }

            return items;
        }

        /// <summary>
        /// 杭頭回転角の照査 1 件分の条件。杭頭工法ごとに限界値と照査するレベルが違う。
        /// </summary>
        /// <param name="Limit">限界回転角 (rad)</param>
        /// <param name="LimitName">限界状態の名乗り</param>
        /// <param name="MethodName">工法の名乗り (対象の説明に出す)</param>
        private sealed record RotationCriterion(double Limit, string LimitName, string MethodName);

        /// <summary>
        /// 杭頭回転角の照査条件を返す。該当する規定が無ければ null (検定しない)。
        ///
        /// 規定の出所は工法ごとに違う。
        /// <list type="bullet">
        /// <item><b>杭頭固定</b>: 場所打ち鉄筋コンクリート杭に限り θu ≤ 1/100 (基本設定で変更可)。
        ///   レベル2 (安全限界) の照査。他の杭種にこの規定は無い。</item>
        /// <item><b>キャプリングパイル工法</b>: θu = 0.03 rad。設計フローが短期なのでレベル1。</item>
        /// <item><b>FT-Pile 構法</b>: θa = min(θac, θas) (軸力で変わる)。短期の照査なのでレベル1。</item>
        /// <item><b>キャプテンパイル工法</b>: θu = 0.04 rad。終局設計のフローに置かれておりレベル2。</item>
        /// </list>
        /// FT-Pile とキャプリングは短期の規定しか無いので、レベル2 で照査するかは
        /// 基本設定の <c>ApplyLevel1RotationLimitToLevel2</c> で選ぶ (既定は照査する)。
        /// </summary>
        private static RotationCriterion? ResolveRotationCriterion(
            PileBodyInput? pileBody, PileSection? section, int level,
            double axialN_kN, FundamentalInput? fundamental)
        {
            string top = pileBody?.PileTopType ?? "";
            bool applyToLevel2 = fundamental?.ApplyLevel1RotationLimitToLevel2 ?? true;

            // ── 杭頭半固定 (工法ごとの限界回転角) ──
            if (top.Contains("キャプテンパイル工法"))
            {
                // 終局設計のフローに置かれた照査。レベル2 のみ。
                return level == 2
                    ? new RotationCriterion(CaptainPile.ThetaU, "安全限界", "キャプテンパイル工法")
                    : null;
            }

            if (top.Contains("キャプリングパイル工法"))
            {
                if (level == 2 && !applyToLevel2) return null;
                return new RotationCriterion(
                    CapringPile.ThetaU,
                    level == 2 ? "安全限界" : "損傷限界",
                    "キャプリングパイル工法");
            }

            if (top.Contains("FT-Pile構法"))
            {
                if (level == 2 && !applyToLevel2) return null;

                // θa は軸力で変わる (θac に圧縮合力による軸応力度が入る)。
                double thetaA = pileBody?.PileTop?.FTPile?.GetAllowableRotationAngle(axialN_kN) ?? 0.0;
                if (!(thetaA > 0) || !double.IsFinite(thetaA)) return null;

                return new RotationCriterion(
                    thetaA,
                    level == 2 ? "安全限界" : "損傷限界",
                    "FT-Pile構法");
            }

            // ── 杭頭固定 ──
            // 規定があるのは場所打ち鉄筋コンクリート杭だけ。安全限界 (レベル2) の照査。
            if (level != 2) return null;
            if (section?.PileBodyType != PileTypeNames.InsituRc) return null;

            double configured = fundamental?.InsituRcUltimateRotationAngleLimit ?? 0.0;
            double limit = configured > 0 && double.IsFinite(configured)
                ? configured
                : DefaultInsituRcUltimateRotationAngleLimit;

            return new RotationCriterion(limit, "安全限界", "場所打ちRC杭・杭頭固定");
        }

        /// <summary>
        /// 杭頭回転角を照査する。限界値と照査するレベルは杭頭工法で決まる
        /// (<see cref="ResolveRotationCriterion"/>)。
        /// </summary>
        private List<EvaluationItem> CheckThetaLimit(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            AnalysisStepResult stepResult,
            string lcName, string combName)
        {
            int level = stepResult.LoadCase?.Level ?? 1;
            var inputModel = _mainVm.ResultInputModel;
            var fundamental = inputModel?.FundamentalInput;

            // 回転ばね → 杭 の対応。杭体番号では杭を特定できない (同じ杭体を複数の杭が使う)。
            var pileBySpring = new Dictionary<RotationalSpring, PileLayoutDataItem>();
            foreach (var pile in inputModel?.PileLayoutItems ?? [])
            {
                if (pile.PileTopRotationalSpring != null)
                    pileBySpring[pile.PileTopRotationalSpring] = pile;
            }

            var rsArr = model.RotationalSprings.ToArray();
            var perItem = new List<EvaluationItem>[rsArr.Length];

            Parallel.For(0, rsArr.Length, idx =>
            {
                var rs = rsArr[idx];
                var found = new List<EvaluationItem>(1);

                // 場所打ちRC杭かどうかを判定
                int pb = (rs.PileBodyNo is int v && v > 0) ? v : 0;
                if (pb <= 0) { perItem[idx] = found; return; }

                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) { perItem[idx] = found; return; }
                if (soilPile.PileBodySegments == null || soilPile.PileBodySegments.Count == 0) { perItem[idx] = found; return; }

                var section = soilPile.PileBodySegments[0].PileSection;
                if (section == null) { perItem[idx] = found; return; }

                // 軸力は FT-Pile の θa (軸力依存) に要る。杭が特定できないときは 0 のまま。
                pileBySpring.TryGetValue(rs, out var pileItem);
                double axialN_kN = 0.0;
                if (pileItem != null)
                {
                    int lcNo = stepResult.LoadCase?.No ?? 0;
                    axialN_kN = pileItem.GetDesignAxialForce(lcNo, level);
                }

                var criterion = ResolveRotationCriterion(
                    soilPile.PileBodyInput, section, level, axialN_kN, fundamental);
                if (criterion == null) { perItem[idx] = found; return; }

                // RotationalSpringResultを検索
                var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (rsResult?.CumulativeDisp == null) { perItem[idx] = found; return; }

                // CombinedXY: θ = √(dRx² + dRy²)
                double dRx = rsResult.CumulativeDisp.Rxi - rsResult.CumulativeDisp.Rxj;
                double dRy = rsResult.CumulativeDisp.Ryi - rsResult.CumulativeDisp.Ryj;
                double theta = Math.Sqrt(dRx * dRx + dRy * dRy);

                // 判定は従来どおり「超えたら NG」
                found.Add(new EvaluationItem
                {
                    Kind = EvaluationKind.PileHeadRotation,
                    Level = level,
                    Category = $"杭頭回転角 ({criterion.MethodName})",
                    LimitName = criterion.LimitName,
                    TargetName = rs.Name,
                    PileBodyNo = pb,
                    PileNo = pileItem?.PileNo,
                    LoadCaseName = lcName,
                    LoadCombinationName = combName,
                    IsLiquefaction = stepResult.IsLiquefaction,
                    Response = theta,
                    Limit = criterion.Limit,
                    Unit = "rad",
                    AxialForce = pileItem != null ? axialN_kN : null,
                    IsOk = !(theta > criterion.Limit),
                });
                perItem[idx] = found;
            });

            var items = new List<EvaluationItem>();
            for (int i = 0; i < perItem.Length; i++)
            {
                if (perItem[i] != null) items.AddRange(perItem[i]);
            }
            return items;
        }

        /// <summary>
        /// NM相関曲線を取得（低減前/低減後 × 限界状態）
        /// PileSection のプロパティは (List N[kN], List M[kNm]) を返す
        /// </summary>
        private static (List<double> Ns, List<double> Ms) GetNMCurve(
            PileSection section, bool factored, LimitState limit, int level)
        {
            try
            {
                var nm = (factored, limit) switch
                {
                    // 損傷限界はレベル依存（L1: β2 なし、L2: β1×β2）
                    (true, LimitState.Damage) => section.GetFactoredDamageNM(level),
                    (true, LimitState.Service) => section.FactoredServiceNM,
                    (true, _) => section.FactoredUltimateNM,
                    (false, LimitState.Damage) => section.UnfactoredDamageNM,
                    (false, LimitState.Service) => section.UnfactoredServiceNM,
                    (false, _) => section.UnfactoredUltimateNM,
                };

                return (nm.N, nm.M);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// 限界曲線から軸力に対応する限界値を補間する。
        /// 実装は <see cref="PileSection.InterpolateLimitAtAxialForce"/> に一本化してある
        /// (計算書の限界線がここと違う補間をしており、同じ軸力で違う限界値になっていた)。
        /// </summary>
        private static double InterpolateAllowableMoment(List<double> ns, List<double> ms, double targetN)
            => PileSection.InterpolateLimitAtAxialForce(ns, ms, targetN);
    }
}
