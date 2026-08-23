using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.FEM;
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
    public partial class EvaluationWindowViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _mainVm;

        [ObservableProperty]
        private string _evaluationText = "「低減前水平解析」または「低減後水平解析」ボタンを押してください。";

        [ObservableProperty]
        private string _statusText = "Ready";

        // 表示フィルタ: 0=NGのみ, 1=OKのみ, 2=OK/NG両方
        [ObservableProperty]
        private int _displayFilter = 0;

        partial void OnDisplayFilterChanged(int value)
        {
            // フィルタ変更時に前回の評価結果を再表示
            if (_lastFactored.HasValue)
                RunEvaluation(_lastFactored.Value);
        }

        private bool? _lastFactored;

        /// <summary>
        /// 直近の検定結果 (構造化)。テキストはここから組み立てる。
        /// 画面の要約行・一覧もこれを見る。
        /// </summary>
        [ObservableProperty]
        private EvaluationResult _result = new([]);

        public EvaluationWindowViewModel(MainWindowViewModel mainVm)
        {
            _mainVm = mainVm;
        }

        [RelayCommand]
        private void EvaluateUnfactored()
        {
            RunEvaluation(factored: false);
        }

        [RelayCommand]
        private void EvaluateFactored()
        {
            RunEvaluation(factored: true);
        }

        /// <summary>
        /// UI を介さず検定テキストを取得する（DOCX 出力などから利用）。
        /// </summary>
        /// <param name="displayFilter">0=NGのみ, 1=OKのみ, 2=両方</param>
        public static string BuildEvaluationText(MainWindowViewModel mainVm, bool factored, int displayFilter)
        {
            var vm = new EvaluationWindowViewModel(mainVm) { DisplayFilter = displayFilter };
            vm.RunEvaluation(factored);
            return vm.EvaluationText;
        }

        private void RunEvaluation(bool factored)
        {
            _lastFactored = factored;

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
                StatusText = "解析結果なし";
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

            // NM曲線キャッシュ: (PileBodyNo, SegmentIndex, factored, isDamageLimit, level) → (Ns, Ms)
            // Parallel.ForEach から共有アクセスするため ConcurrentDictionary を使用
            var nmCache = new ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)>();

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

            // レベル1 / レベル2 に分けて評価
            var level1Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 1).ToList();
            var level2Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 2).ToList();

            // ── 検定を実行して項目を集める (テキストはこの後で組む) ──
            var level1Items = level1Results.Count > 0
                ? EvaluateLevel1(model, soilPileByPileBodyNo, pileByPileBodyNo, nmCache, level1Results, factored)
                : [];
            var level2Items = level2Results.Count > 0
                ? EvaluateLevel2(model, soilPileByPileBodyNo, pileByPileBodyNo, nmCache, level2Results, factored, seismicGrade)
                : [];
            var inclinationItems = EvaluateBeamAwareInclination();

            int totalNgCount = level1Items.Count(i => !i.IsOk) + level2Items.Count(i => !i.IsOk);
            int totalOkCount = level1Items.Count(i => i.IsOk) + level2Items.Count(i => i.IsOk);

            // ── テキスト組立 ──
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

            // ── 個別矩形（基礎梁考慮）反復解析の傾斜角検定 ──
            AppendInclinationSection(sb, inclinationItems);

            var all = new List<EvaluationItem>(level1Items.Count + level2Items.Count + inclinationItems.Count);
            all.AddRange(level1Items);
            all.AddRange(level2Items);
            all.AddRange(inclinationItems);
            Result = new EvaluationResult(all);

            EvaluationText = sb.ToString();
            int grandOk = Result.OkCount;
            int grandNg = Result.NgCount;
            StatusText = grandNg == 0
                ? $"すべてOK (チェック {grandOk + grandNg} 件)"
                : $"NG: {grandNg} 件 / OK: {grandOk} 件";
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
        /// レベル1地震動の評価
        /// - i端もしくはj端がM-φ関係が損傷限界状態を超える場合
        /// - 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private List<EvaluationItem> EvaluateLevel1(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            List<AnalysisStepResult> results, bool factored)
        {
            var items = new List<EvaluationItem>();

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";
                string liqLabel = stepResult.IsLiquefaction ? "液状化有" : "液状化無";

                // M-φ チェック: 損傷限界
                items.AddRange(CheckMPhiLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, nmCache,
                    stepResult, factored, isDamageLimit: true,
                    lcName, combName, liqLabel));

                // θ チェック: 場所打ちRC杭で 1/100rad
                items.AddRange(CheckThetaLimit(model, soilPileByPileBodyNo,
                    stepResult, lcName, combName, liqLabel));
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
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            List<AnalysisStepResult> results, bool factored, string seismicGrade)
        {
            var items = new List<EvaluationItem>();
            bool isDamageLimit = seismicGrade == "S"; // S→損傷限界、A→安全限界

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";
                string liqLabel = stepResult.IsLiquefaction ? "液状化有" : "液状化無";

                items.AddRange(CheckMPhiLimitForBeams(model, soilPileByPileBodyNo,
                    pileByPileBodyNo, nmCache,
                    stepResult, factored, isDamageLimit,
                    lcName, combName, liqLabel));

                items.AddRange(CheckThetaLimit(model, soilPileByPileBodyNo,
                    stepResult, lcName, combName, liqLabel));
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
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            AnalysisStepResult stepResult, bool factored, bool isDamageLimit,
            string lcName, string combName, string liqLabel)
        {
            string limitName = isDamageLimit ? "損傷限界" : "安全限界";
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

                // 軸力: 荷重ケースに応じたユーザー入力値 (kN)
                double axialN_kN = 0.0;
                if (pileByPileBodyNo.TryGetValue(pb, out var pileItem))
                {
                    int lcNo = stepResult.LoadCase?.No ?? 0;
                    int level = stepResult.LoadCase?.Level ?? 1;
                    // 地震時軸力優先・未入力 (0) / 範囲外は常時軸力。
                    // グラフ・計算書の限界線と同じ軸力を使う (食い違うと判定が一致しない)。
                    axialN_kN = pileItem.GetDesignAxialForce(lcNo, level);
                }

                // NM相関曲線をキャッシュから取得 (ConcurrentDictionary、初回のみ計算)
                int loadCaseLevel = stepResult.LoadCase?.Level ?? 1;
                var cacheKey = (pb, seg, factored, isDamageLimit, loadCaseLevel);
                var nmCurve = nmCache.GetOrAdd(cacheKey, _ => GetNMCurve(section, factored, isDamageLimit, loadCaseLevel));
                if (nmCurve.Ns == null || nmCurve.Ms == null || nmCurve.Ns.Count < 2) { perBeamResults[idx] = found; return; }

                // NM相関曲線から許容モーメントを補間
                double allowableM = InterpolateAllowableMoment(nmCurve.Ns, nmCurve.Ms, axialN_kN);
                if (allowableM <= 0) { perBeamResults[idx] = found; return; }

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
                    SegmentIndex = seg,
                    LoadCaseName = lcName,
                    LoadCombinationName = combName,
                    LiquefactionLabel = liqLabel,
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
        /// 場所打ち鉄筋コンクリート杭の回転角θが1/100 radを超えるかチェック
        /// </summary>
        private List<EvaluationItem> CheckThetaLimit(AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            AnalysisStepResult stepResult,
            string lcName, string combName, string liqLabel)
        {
            const double thetaLimit = 1.0 / 100.0; // 1/100 rad
            int level = stepResult.LoadCase?.Level ?? 1;

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
                if (section.PileBodyType != PileTypeNames.InsituRc) { perItem[idx] = found; return; }

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
                    Category = "杭頭回転角 (場所打ちRC杭)",
                    TargetName = rs.Name,
                    PileBodyNo = pb,
                    LoadCaseName = lcName,
                    LoadCombinationName = combName,
                    LiquefactionLabel = liqLabel,
                    Response = theta,
                    Limit = thetaLimit,
                    Unit = "rad",
                    IsOk = !(theta > thetaLimit),
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
        /// NM相関曲線を取得（低減前/低減後、損傷限界/安全限界）
        /// PileSection のプロパティは (List N[kN], List M[kNm]) を返す
        /// </summary>
        private static (List<double> Ns, List<double> Ms) GetNMCurve(PileSection section, bool factored, bool isDamageLimit, int level)
        {
            try
            {
                (List<double> N, List<double> M) nm;
                if (factored)
                {
                    // 損傷限界はレベル依存（L1: β2 なし、L2: β1×β2）
                    nm = isDamageLimit ? section.GetFactoredDamageNM(level) : section.FactoredUltimateNM;
                }
                else
                {
                    nm = isDamageLimit ? section.UnfactoredDamageNM : section.UnfactoredUltimateNM;
                }

                return (nm.N, nm.M);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// NM相関曲線から指定軸力に対応する許容モーメントを補間
        /// NM曲線は閉じた包絡線（引張側→圧縮側→引張側）なので、
        /// 指定Nにおける最大Mを返す
        /// </summary>
        private static double InterpolateAllowableMoment(List<double> ns, List<double> ms, double targetN)
        {
            if (ns == null || ms == null || ns.Count < 2) return 0;

            double maxM = 0;

            for (int i = 0; i < ns.Count - 1; i++)
            {
                double n0 = ns[i], n1 = ns[i + 1];
                double m0 = ms[i], m1 = ms[i + 1];

                // targetNがこの区間に含まれるか
                if ((targetN >= Math.Min(n0, n1)) && (targetN <= Math.Max(n0, n1)))
                {
                    double dn = n1 - n0;
                    double interpM;
                    if (Math.Abs(dn) < 1e-10)
                        interpM = Math.Max(m0, m1);
                    else
                        interpM = m0 + (m1 - m0) * (targetN - n0) / dn;

                    if (interpM > maxM)
                        maxM = interpM;
                }
            }

            return maxM;
        }

        [RelayCommand]
        private void Export()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = $"Evaluation_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, EvaluationText, Encoding.UTF8);
                    StatusText = $"出力完了: {Path.GetFileName(dialog.FileName)}";
                }
            }
            catch (Exception ex)
            {
                MessageService.Show($"出力エラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
