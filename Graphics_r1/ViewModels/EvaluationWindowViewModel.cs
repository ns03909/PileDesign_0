using PileDesign.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.FEM;
using PileDesign.Models.InputData;
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
            var inputModel = _mainVm.CurrentInputModel;
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

            int totalNgCount = 0;
            int totalOkCount = 0;

            // レベル1 / レベル2 に分けて評価
            var level1Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 1).ToList();
            var level2Results = uniqueCombinations.Where(r => r.LoadCase?.Level == 2).ToList();

            if (level1Results.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine("■ レベル1地震動");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                var (ng, ok) = EvaluateLevel1(sb, model, soilPileByPileBodyNo, pileByPileBodyNo, nmCache, level1Results, factored);
                totalNgCount += ng;
                totalOkCount += ok;
            }

            if (level2Results.Count > 0)
            {
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine($"■ レベル2地震動（耐震グレード{seismicGrade}）");
                sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                sb.AppendLine();
                var (ng, ok) = EvaluateLevel2(sb, model, soilPileByPileBodyNo, pileByPileBodyNo, nmCache, level2Results, factored, seismicGrade);
                totalNgCount += ng;
                totalOkCount += ok;
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
            int beamAwareNg = 0, beamAwareOk = 0;
            EvaluateBeamAwareInclination(sb, ref beamAwareOk, ref beamAwareNg);

            EvaluationText = sb.ToString();
            int grandOk = totalOkCount + beamAwareOk;
            int grandNg = totalNgCount + beamAwareNg;
            StatusText = grandNg == 0
                ? $"すべてOK (チェック {grandOk + grandNg} 件)"
                : $"NG: {grandNg} 件 / OK: {grandOk} 件";
        }

        /// <summary>
        /// 個別矩形（基礎梁考慮）反復解析の結果から、各基礎梁の傾斜角を検定する。
        /// 傾斜角 = (Uz_j - Uz_i) / L、許容値 1/300 (基礎指針) を既定値として比較。
        /// </summary>
        private void EvaluateBeamAwareInclination(StringBuilder sb, ref int okCount, ref int ngCount)
        {
            var pgs = _mainVm.CurrentInputModel?.PileGroupSettlement;
            if (pgs?.CaseRecords == null) return;
            var beamAwareCases = pgs.CaseRecords.Where(r => r.IsBeamAware).ToList();
            if (beamAwareCases.Count == 0) return;

            var inputModel = _mainVm.CurrentInputModel;
            if (inputModel?.FoundationBeamInput?.Beams == null
                || inputModel.FoundationBeamInput.Beams.Count == 0) return;

            const double inclinationLimit = 1.0 / 300.0;

            sb.AppendLine();
            sb.AppendLine(new string('=', 60));
            sb.AppendLine("【個別矩形（基礎梁考慮）反復解析 傾斜角検定】");
            sb.AppendLine($"許容傾斜角: 1/300 = {inclinationLimit:E3} (rad)");
            sb.AppendLine();

            foreach (var rec in beamAwareCases)
            {
                if (rec.NodeResults == null || rec.NodeResults.Count == 0) continue;
                sb.AppendLine($"--- ケース: {rec.LoadCaseName} ---");
                int caseOk = 0, caseNg = 0;
                double maxAbsInclination = 0;
                string maxBeamName = "";

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
                    if (inclination > maxAbsInclination)
                    {
                        maxAbsInclination = inclination;
                        maxBeamName = $"FoundationBeam-{inputModel.FoundationBeamInput.GetBeamNo(fbBeam)}";
                    }

                    bool isOk = inclination < inclinationLimit;
                    if (isOk) caseOk++; else caseNg++;

                    bool show = (DisplayFilter == 0 && !isOk)
                              || (DisplayFilter == 1 && isOk)
                              || (DisplayFilter == 2);
                    if (show)
                    {
                        string status = isOk ? "OK" : "NG";
                        double inv = inclination > 0 ? 1.0 / inclination : 0;
                        sb.AppendLine($"  {status} 梁 #{inputModel.FoundationBeamInput.GetBeamNo(fbBeam)}: " +
                                      $"傾斜角 = {inclination:E3} rad (1/{inv:F0}), L={L:F2}m");
                    }
                }
                sb.AppendLine($"  → ケース合計: OK {caseOk} 件 / NG {caseNg} 件 / 最大傾斜角 = {maxAbsInclination:E3} rad ({maxBeamName})");
                sb.AppendLine();
                okCount += caseOk;
                ngCount += caseNg;
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
        private (int ng, int ok) EvaluateLevel1(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            List<AnalysisStepResult> results, bool factored)
        {
            int ngCount = 0, okCount = 0;

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";
                string liqLabel = stepResult.IsLiquefaction ? "液状化有" : "液状化無";

                // M-φ チェック: 損傷限界
                var (ng1, ok1) = CheckMPhiLimitForBeams(sb, model, soilPileByPileBodyNo,
                    pileByPileBodyNo, nmCache,
                    stepResult, factored, isDamageLimit: true,
                    lcName, combName, liqLabel);
                ngCount += ng1;
                okCount += ok1;

                // θ チェック: 場所打ちRC杭で 1/100rad
                var (ng2, ok2) = CheckThetaLimit(sb, model, soilPileByPileBodyNo,
                    stepResult, lcName, combName, liqLabel);
                ngCount += ng2;
                okCount += ok2;
            }

            if (ngCount == 0 && okCount == 0)
                sb.AppendLine("  → チェック対象なし");
            else if (ngCount == 0)
                sb.AppendLine("  → NG項目なし");
            sb.AppendLine();
            return (ngCount, okCount);
        }

        /// <summary>
        /// レベル2地震動の評価
        /// 耐震グレードA: 安全限界 / 耐震グレードS: 損傷限界
        /// + 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private (int ng, int ok) EvaluateLevel2(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            List<AnalysisStepResult> results, bool factored, string seismicGrade)
        {
            int ngCount = 0, okCount = 0;
            bool isDamageLimit = seismicGrade == "S"; // S→損傷限界、A→安全限界

            foreach (var stepResult in results)
            {
                string lcName = stepResult.LoadCase?.LoadName ?? "?";
                string combName = stepResult.LoadCombination?.Name ?? "?";
                string liqLabel = stepResult.IsLiquefaction ? "液状化有" : "液状化無";

                // M-φ チェック
                var (ng1, ok1) = CheckMPhiLimitForBeams(sb, model, soilPileByPileBodyNo,
                    pileByPileBodyNo, nmCache,
                    stepResult, factored, isDamageLimit,
                    lcName, combName, liqLabel);
                ngCount += ng1;
                okCount += ok1;

                // θ チェック
                var (ng2, ok2) = CheckThetaLimit(sb, model, soilPileByPileBodyNo,
                    stepResult, lcName, combName, liqLabel);
                ngCount += ng2;
                okCount += ok2;
            }

            if (ngCount == 0 && okCount == 0)
                sb.AppendLine("  → チェック対象なし");
            else if (ngCount == 0)
                sb.AppendLine("  → NG項目なし");
            sb.AppendLine();
            return (ngCount, okCount);
        }

        /// <summary>
        /// 各梁要素のi端・j端のモーメントが限界状態のNM相関曲線を超えるかチェック
        /// 軸力は荷重ケースに応じたユーザー入力値（AxialForceLevel1s/AxialForceLevel2s）を使用
        /// </summary>
        private (int ng, int ok) CheckMPhiLimitForBeams(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            ConcurrentDictionary<(int, int, bool, bool, int), (List<double> Ns, List<double> Ms)> nmCache,
            AnalysisStepResult stepResult, bool factored, bool isDamageLimit,
            string lcName, string combName, string liqLabel)
        {
            string limitName = isDamageLimit ? "損傷限界" : "安全限界";
            bool showNg = DisplayFilter == 0 || DisplayFilter == 2;
            bool showOk = DisplayFilter == 1 || DisplayFilter == 2;

            // Beam ごとに独立。並列で (ngLocal, okLocal, message) を produce、最後に順序通り集約
            var beamsArr = model.Beams.ToArray();
            var perBeamResults = new (int ng, int ok, string text)[beamsArr.Length];

            Parallel.For(0, beamsArr.Length, idx =>
            {
                var beam = beamsArr[idx];
                int ngL = 0, okL = 0;
                var msg = new StringBuilder();

                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                {
                    perBeamResults[idx] = (0, 0, ""); return;
                }

                // BeamResultを検索
                var result = beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (result?.CumulativeForce == null) { perBeamResults[idx] = (0, 0, ""); return; }

                // SoilPileからPileSectionを取得
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) { perBeamResults[idx] = (0, 0, ""); return; }
                if (soilPile.PileBodySegments == null || seg >= soilPile.PileBodySegments.Count) { perBeamResults[idx] = (0, 0, ""); return; }
                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null) { perBeamResults[idx] = (0, 0, ""); return; }

                // 軸力: 荷重ケースに応じたユーザー入力値 (kN)
                double axialN_kN = 0.0;
                if (pileByPileBodyNo.TryGetValue(pb, out var pileItem))
                {
                    int lcNo = stepResult.LoadCase?.No ?? 0;
                    int level = stepResult.LoadCase?.Level ?? 1;
                    try
                    {
                        axialN_kN = pileItem.GetSeismicAxialForce(lcNo, level);
                    }
                    catch
                    {
                        // インデックス範囲外の場合はAxialForceVL(常時)をフォールバック
                        axialN_kN = pileItem.AxialForceVL;
                    }
                }

                // NM相関曲線をキャッシュから取得 (ConcurrentDictionary、初回のみ計算)
                int loadCaseLevel = stepResult.LoadCase?.Level ?? 1;
                var cacheKey = (pb, seg, factored, isDamageLimit, loadCaseLevel);
                var nmCurve = nmCache.GetOrAdd(cacheKey, _ => GetNMCurve(section, factored, isDamageLimit, loadCaseLevel));
                if (nmCurve.Ns == null || nmCurve.Ms == null || nmCurve.Ns.Count < 2) { perBeamResults[idx] = (0, 0, ""); return; }

                // NM相関曲線から許容モーメントを補間
                double allowableM = InterpolateAllowableMoment(nmCurve.Ns, nmCurve.Ms, axialN_kN);
                if (allowableM <= 0) { perBeamResults[idx] = (0, 0, ""); return; }

                // i端モーメント |M| = √(Myi² + Mzi²)
                double mI = Math.Sqrt(
                    result.CumulativeForce.Myi * result.CumulativeForce.Myi +
                    result.CumulativeForce.Mzi * result.CumulativeForce.Mzi);

                // j端モーメント |M| = √(Myj² + Mzj²)
                double mJ = Math.Sqrt(
                    result.CumulativeForce.Myj * result.CumulativeForce.Myj +
                    result.CumulativeForce.Mzj * result.CumulativeForce.Mzj);

                // i端チェック
                if (mI > allowableM)
                {
                    ngL++;
                    if (showNg)
                    {
                        msg.AppendLine($"  [NG] {limitName}超過（i端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       M={mI:F1} kNm > {limitName}M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        msg.AppendLine();
                    }
                }
                else
                {
                    okL++;
                    if (showOk)
                    {
                        msg.AppendLine($"  [OK] {limitName}（i端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       M={mI:F1} kNm ≤ {limitName}M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        msg.AppendLine();
                    }
                }

                // j端チェック
                if (mJ > allowableM)
                {
                    ngL++;
                    if (showNg)
                    {
                        msg.AppendLine($"  [NG] {limitName}超過（j端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       M={mJ:F1} kNm > {limitName}M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        msg.AppendLine();
                    }
                }
                else
                {
                    okL++;
                    if (showOk)
                    {
                        msg.AppendLine($"  [OK] {limitName}（j端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       M={mJ:F1} kNm ≤ {limitName}M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        msg.AppendLine();
                    }
                }
                perBeamResults[idx] = (ngL, okL, msg.ToString());
            });

            // 順序通り集約
            int ngCount = 0, okCount = 0;
            for (int i = 0; i < perBeamResults.Length; i++)
            {
                ngCount += perBeamResults[i].ng;
                okCount += perBeamResults[i].ok;
                if (perBeamResults[i].text.Length > 0) sb.Append(perBeamResults[i].text);
            }
            return (ngCount, okCount);
        }

        /// <summary>
        /// 場所打ち鉄筋コンクリート杭の回転角θが1/100 radを超えるかチェック
        /// </summary>
        private (int ng, int ok) CheckThetaLimit(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            AnalysisStepResult stepResult,
            string lcName, string combName, string liqLabel)
        {
            const double thetaLimit = 1.0 / 100.0; // 1/100 rad
            bool showNg = DisplayFilter == 0 || DisplayFilter == 2;
            bool showOk = DisplayFilter == 1 || DisplayFilter == 2;

            var rsArr = model.RotationalSprings.ToArray();
            var perItem = new (int ng, int ok, string text)[rsArr.Length];

            Parallel.For(0, rsArr.Length, idx =>
            {
                var rs = rsArr[idx];
                int ngL = 0, okL = 0;
                var msg = new StringBuilder();

                // 場所打ちRC杭かどうかを判定
                int pb = (rs.PileBodyNo is int v && v > 0) ? v : 0;
                if (pb <= 0) { perItem[idx] = (0, 0, ""); return; }

                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) { perItem[idx] = (0, 0, ""); return; }
                if (soilPile.PileBodySegments == null || soilPile.PileBodySegments.Count == 0) { perItem[idx] = (0, 0, ""); return; }

                var section = soilPile.PileBodySegments[0].PileSection;
                if (section == null) { perItem[idx] = (0, 0, ""); return; }
                if (section.PileBodyType != PileTypeNames.InsituRc) { perItem[idx] = (0, 0, ""); return; }

                // RotationalSpringResultを検索
                var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (rsResult?.CumulativeDisp == null) { perItem[idx] = (0, 0, ""); return; }

                // CombinedXY: θ = √(dRx² + dRy²)
                double dRx = rsResult.CumulativeDisp.Rxi - rsResult.CumulativeDisp.Rxj;
                double dRy = rsResult.CumulativeDisp.Ryi - rsResult.CumulativeDisp.Ryj;
                double theta = Math.Sqrt(dRx * dRx + dRy * dRy);

                if (theta > thetaLimit)
                {
                    ngL++;
                    if (showNg)
                    {
                        msg.AppendLine($"  [NG] θ超過（場所打ちRC杭）: {rs.Name}  杭配置No.{pb}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       θ={theta:F5} rad > {thetaLimit:F2} rad");
                        msg.AppendLine();
                    }
                }
                else
                {
                    okL++;
                    if (showOk)
                    {
                        msg.AppendLine($"  [OK] θ（場所打ちRC杭）: {rs.Name}  杭配置No.{pb}");
                        msg.AppendLine($"       荷重ケース: {lcName} / 組合せ: {combName} / {liqLabel}");
                        msg.AppendLine($"       θ={theta:F5} rad ≤ {thetaLimit:F2} rad");
                        msg.AppendLine();
                    }
                }
                perItem[idx] = (ngL, okL, msg.ToString());
            });

            int ngCount = 0, okCount = 0;
            for (int i = 0; i < perItem.Length; i++)
            {
                ngCount += perItem[i].ng;
                okCount += perItem[i].ok;
                if (perItem[i].text.Length > 0) sb.Append(perItem[i].text);
            }
            return (ngCount, okCount);
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
