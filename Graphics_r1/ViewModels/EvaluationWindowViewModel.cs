using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

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

            // NM曲線キャッシュ: (PileBodyNo, SegmentIndex, factored, isDamageLimit) → (Ns, Ms)
            var nmCache = new Dictionary<(int, int, bool, bool), (List<double> Ns, List<double> Ms)>();

            // 全ての解析結果の組み合わせ（LoadCase, LoadCombination, IsLiquefaction）を取得
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

            EvaluationText = sb.ToString();
            StatusText = totalNgCount == 0
                ? $"すべてOK (チェック {totalOkCount + totalNgCount} 件)"
                : $"NG: {totalNgCount} 件 / OK: {totalOkCount} 件";
        }

        /// <summary>
        /// レベル1地震動の評価
        /// - i端もしくはj端がM-φ関係が損傷限界状態を超える場合
        /// - 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private (int ng, int ok) EvaluateLevel1(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<(int, int, bool, bool), (List<double> Ns, List<double> Ms)> nmCache,
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
        /// 耐震グレードA: 終局限界 / 耐震グレードS: 損傷限界
        /// + 場所打ち鉄筋コンクリート杭で、θが1/100radを超える場合
        /// </summary>
        private (int ng, int ok) EvaluateLevel2(StringBuilder sb, AnaModel model,
            Dictionary<int, SoilPile> soilPileByPileBodyNo,
            Dictionary<int, PileLayoutDataItem> pileByPileBodyNo,
            Dictionary<(int, int, bool, bool), (List<double> Ns, List<double> Ms)> nmCache,
            List<AnalysisStepResult> results, bool factored, string seismicGrade)
        {
            int ngCount = 0, okCount = 0;
            bool isDamageLimit = seismicGrade == "S"; // S→損傷限界、A→終局限界

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
            Dictionary<(int, int, bool, bool), (List<double> Ns, List<double> Ms)> nmCache,
            AnalysisStepResult stepResult, bool factored, bool isDamageLimit,
            string lcName, string combName, string liqLabel)
        {
            int ngCount = 0, okCount = 0;
            string limitName = isDamageLimit ? "損傷限界" : "終局限界";
            bool showNg = DisplayFilter == 0 || DisplayFilter == 2;
            bool showOk = DisplayFilter == 1 || DisplayFilter == 2;

            foreach (var beam in model.Beams)
            {
                if (beam.PileBodyNo is not int pb || beam.SegmentIndex is not int seg)
                    continue;

                // BeamResultを検索
                var result = beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (result?.CumulativeForce == null) continue;

                // SoilPileからPileSectionを取得
                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) continue;
                if (soilPile.PileBodySegments == null || seg >= soilPile.PileBodySegments.Count) continue;
                var section = soilPile.PileBodySegments[seg].PileSection;
                if (section == null) continue;

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

                // NM相関曲線をキャッシュから取得
                var cacheKey = (pb, seg, factored, isDamageLimit);
                if (!nmCache.TryGetValue(cacheKey, out var nmCurve))
                {
                    nmCurve = GetNMCurve(section, factored, isDamageLimit);
                    nmCache[cacheKey] = nmCurve;
                }
                if (nmCurve.Ns == null || nmCurve.Ms == null || nmCurve.Ns.Count < 2) continue;

                // NM相関曲線から許容モーメントを補間
                double allowableM = InterpolateAllowableMoment(nmCurve.Ns, nmCurve.Ms, axialN_kN);
                if (allowableM <= 0) continue;

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
                    ngCount++;
                    if (showNg)
                    {
                        sb.AppendLine($"  [NG] {limitName}超過（i端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       M={mI:F1} kNm > 許容M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        sb.AppendLine();
                    }
                }
                else
                {
                    okCount++;
                    if (showOk)
                    {
                        sb.AppendLine($"  [OK] {limitName}（i端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       M={mI:F1} kNm ≤ 許容M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        sb.AppendLine();
                    }
                }

                // j端チェック
                if (mJ > allowableM)
                {
                    ngCount++;
                    if (showNg)
                    {
                        sb.AppendLine($"  [NG] {limitName}超過（j端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       M={mJ:F1} kNm > 許容M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        sb.AppendLine();
                    }
                }
                else
                {
                    okCount++;
                    if (showOk)
                    {
                        sb.AppendLine($"  [OK] {limitName}（j端）: {beam.Name}  杭配置No.{pb} / 要素{seg}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       M={mJ:F1} kNm ≤ 許容M={allowableM:F1} kNm (N={axialN_kN:F1} kN)");
                        sb.AppendLine();
                    }
                }
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
            int ngCount = 0, okCount = 0;
            const double thetaLimit = 1.0 / 100.0; // 1/100 rad
            bool showNg = DisplayFilter == 0 || DisplayFilter == 2;
            bool showOk = DisplayFilter == 1 || DisplayFilter == 2;

            foreach (var rs in model.RotationalSprings)
            {
                // 場所打ちRC杭かどうかを判定
                int pb = (rs.PileBodyNo is int v && v > 0) ? v : 0;
                if (pb <= 0) continue;

                if (!soilPileByPileBodyNo.TryGetValue(pb, out var soilPile)) continue;
                if (soilPile.PileBodySegments == null || soilPile.PileBodySegments.Count == 0) continue;

                var section = soilPile.PileBodySegments[0].PileSection;
                if (section == null) continue;
                if (section.PileBodyType != "場所打ち鉄筋コンクリート杭") continue;

                // RotationalSpringResultを検索
                var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == stepResult.IsLiquefaction &&
                    r.Step == stepResult.Step &&
                    (stepResult.LoadCase == null || r.LoadCase?.LoadName == stepResult.LoadCase.LoadName) &&
                    (stepResult.LoadCombination == null || r.LoadCombination?.Name == stepResult.LoadCombination.Name));

                if (rsResult?.CumulativeDisp == null) continue;

                // CombinedXY: θ = √(dRx² + dRy²)
                double dRx = rsResult.CumulativeDisp.Rxi - rsResult.CumulativeDisp.Rxj;
                double dRy = rsResult.CumulativeDisp.Ryi - rsResult.CumulativeDisp.Ryj;
                double theta = Math.Sqrt(dRx * dRx + dRy * dRy);

                if (theta > thetaLimit)
                {
                    ngCount++;
                    if (showNg)
                    {
                        sb.AppendLine($"  [NG] θ超過（場所打ちRC杭）: {rs.Name}  杭配置No.{pb}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       θ={theta:F5} rad > {thetaLimit:F2} rad");
                        sb.AppendLine();
                    }
                }
                else
                {
                    okCount++;
                    if (showOk)
                    {
                        sb.AppendLine($"  [OK] θ（場所打ちRC杭）: {rs.Name}  杭配置No.{pb}");
                        sb.AppendLine($"       荷重ケース: {lcName} / 組み合わせ: {combName} / {liqLabel}");
                        sb.AppendLine($"       θ={theta:F5} rad ≤ {thetaLimit:F2} rad");
                        sb.AppendLine();
                    }
                }
            }
            return (ngCount, okCount);
        }

        /// <summary>
        /// NM相関曲線を取得（低減前/低減後、損傷限界/終局限界）
        /// PileSection のプロパティは (List N[kN], List M[kNm]) を返す
        /// </summary>
        private static (List<double> Ns, List<double> Ms) GetNMCurve(PileSection section, bool factored, bool isDamageLimit)
        {
            try
            {
                (List<double> N, List<double> M) nm;
                if (factored)
                    nm = isDamageLimit ? section.FactoredDamageNM : section.FactoredUltimateNM;
                else
                    nm = isDamageLimit ? section.UnfactoredDamageNM : section.UnfactoredUltimateNM;

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
                MessageBox.Show($"出力エラー:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
