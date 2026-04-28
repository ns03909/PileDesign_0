using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using Body = DocumentFormat.OpenXml.Wordprocessing.Body;

using Serilog;
namespace PileDesign.Output
{
    // 各種グラフ出力（N-M, Q-N, M-φ, M-θ インタラクション曲線、全杭ダイアグラム、荷重沈下曲線）を提供する partial。
    internal partial class WordDocument
    {
        private void AddNMinT(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.PileBodies == null || inputModel.PileBodies.Count == 0)
                return;

            foreach (var pileBody in inputModel.PileBodies)
            {
                if (pileBody?.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                    continue;

                // レベル別「最大曲げ」時の縦断図出力用トラッカー（後段で使用）
                List<double> maxMomentsInPileBody = [double.MinValue, double.MinValue];
                List<PileLayoutDataItem?> pileLayoutDataItemsWithMaxMoment = [null, null];
                List<LoadCase?> loadCasesWithMaxMoment = [null, null];
                List<LoadCombination?> loadCombinationsWithMaxMoment = [null, null];
                List<bool> isLiquefactionsWithMaxMoment = [false, false];

                for (int j = 0; j < pileBody.PileBodySegments.Count; j++)
                {
                    var segment = pileBody.PileBodySegments[j];
                    if (segment?.PileSection == null) continue;
                    int pileBodySegmentNo = segment.No;
                    var pileSection = segment.PileSection;

                    // ライン（耐力曲線）- NMプロパティは既に kN/kNm 単位
                    List<List<double>> lineListsX = [];
                    List<List<double>> lineListsY = [];
                    List<string> lineListsLegend = [];

                    static List<double> ToList(IEnumerable<double>? src) => src?.ToList() ?? [];

                    try
                    {
                        var nmUUlt = pileSection.UnfactoredUltimateNM;
                        lineListsX.Add(ToList(nmUUlt.N));
                        lineListsY.Add(ToList(nmUUlt.M));
                        lineListsLegend.Add("低減前安全限界");

                        var nmFUlt = pileSection.FactoredUltimateNM;
                        lineListsX.Add(ToList(nmFUlt.N));
                        lineListsY.Add(ToList(nmFUlt.M));
                        lineListsLegend.Add("低減後安全限界");

                        var nmUDmg = pileSection.UnfactoredDamageNM;
                        lineListsX.Add(ToList(nmUDmg.N));
                        lineListsY.Add(ToList(nmUDmg.M));
                        lineListsLegend.Add("低減前損傷限界");

                        var nmFDmg = pileSection.FactoredDamageNM;
                        lineListsX.Add(ToList(nmFDmg.N));
                        lineListsY.Add(ToList(nmFDmg.M));
                        lineListsLegend.Add("低減後損傷限界");

                        var nmUSvc = pileSection.UnfactoredServiceNM;
                        lineListsX.Add(ToList(nmUSvc.N));
                        lineListsY.Add(ToList(nmUSvc.M));
                        lineListsLegend.Add("低減前使用限界");

                        var nmFSvc = pileSection.FactoredServiceNM;
                        lineListsX.Add(ToList(nmFSvc.N));
                        lineListsY.Add(ToList(nmFSvc.M));
                        lineListsLegend.Add("低減後使用限界");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WordDoc] NM曲線取得失敗 (杭体:{pileBody.PileBodyRef}, 区間:{segment.No}): {ex.Message}");
                    }

                    // 散布点
                    List<double> axialForceResultsVL = [];
                    List<double> momentResultsVL = [];

                    List<double> axialForceResultsLevel1 = [];
                    List<double> momentResultsLevel1 = [];

                    List<double> axialForceResultsLevel2 = [];
                    List<double> momentResultsLevel2 = [];

                    // 解析済みの場合のみ散布点を作成
                    if (mainWindowViewModel?.IsHorizontalAnalysisDone == true &&
                        inputModel.PileLayoutItems != null &&
                        inputModel.LoadCasesInput != null)
                    {
                        var allSeismicLoadCases = inputModel.LoadCasesInput.AllSeismicLoadCases ?? [];
                        var allLoadCombinations = inputModel.LoadCasesInput.AllLoadCombinations ?? [];

                        foreach (var pli in inputModel.PileLayoutItems)
                        {
                            if (pli == null) continue;

                            // 常時（VL）は同一杭体のみ採用。モーメントは 0 扱い
                            try
                            {
                                int pbIdx = pli.PileBodyNo - 1;
                                if (pbIdx >= 0 && pbIdx < inputModel.PileBodies.Count &&
                                    inputModel.PileBodies[pbIdx] == pileBody)
                                {
                                    axialForceResultsVL.Add(pli.AxialForceVL0 + pli.AxialForceVLAdditional);
                                    momentResultsVL.Add(0.0);
                                }
                            }
                            catch { /* ignore */ }

                            foreach (var loadCase in allSeismicLoadCases)
                            {
                                if (loadCase == null || !loadCase.IsApplicable) continue;
                                double axialForce = pli.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                                foreach (var loadCombination in allLoadCombinations)
                                {
                                    // 液状化パターン（ユーザー選択に基づく）
                                    var liqPatternsNM = new List<bool>();
                                    if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatternsNM.Add(true);
                                    if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatternsNM.Add(false);

                                    foreach (var isLiquefaction in liqPatternsNM)
                                    {
                                        // 対象の soilPile をアイテムの代替番号から取得
                                        var soilPiles = inputModel.ElementDivision?.SoilPiles;
                                        int soilIndex = pli.SoilPileAltNo - 1;
                                        if (soilPiles == null || soilIndex < 0 || soilIndex >= soilPiles.Count)
                                            continue;

                                        var soilPileForItem = soilPiles[soilIndex];
                                        if (soilPileForItem?.PileBodySegments == null ||
                                            pli.Beams == null || pli.Beams.Count == 0)
                                            continue;

                                        // この区間Noに一致するビーム群の中から、当該ケースの最大曲げを抽出
                                        double maxMomentInPile = double.MinValue;
                                        for (int iSeg = 0; iSeg < soilPileForItem.PileBodySegments.Count; iSeg++)
                                        {
                                            var segI = soilPileForItem.PileBodySegments[iSeg];
                                            if (segI == null || segI.No != pileBodySegmentNo) continue;

                                            if (iSeg < 0 || iSeg >= pli.Beams.Count) continue;
                                            var beam = pli.Beams[iSeg];
                                            if (beam == null) continue;

                                            var cum = beam.GetBeamResult(anaModel, loadCase, loadCombination, isLiquefaction)?.CumulativeForce;
                                            if (cum != null)
                                                maxMomentInPile = Math.Max(maxMomentInPile, cum.MabsMax);
                                        }

                                        // 区間ループ後に1回だけ散布点を追加（重複防止）
                                        if (maxMomentInPile == double.MinValue) maxMomentInPile = 0.0;

                                        if (loadCase.Level == 1)
                                        {
                                            axialForceResultsLevel1.Add(axialForce);
                                            momentResultsLevel1.Add(maxMomentInPile);
                                        }
                                        else if (loadCase.Level == 2)
                                        {
                                            axialForceResultsLevel2.Add(axialForce);
                                            momentResultsLevel2.Add(maxMomentInPile);
                                        }

                                        // レベル別の「杭全体の最大」トラッキング（後段の縦断図に使用）
                                        for (int lvl = 0; lvl < 2; lvl++)
                                        {
                                            if (loadCase.Level != (lvl + 1)) continue;
                                            if (maxMomentInPile > maxMomentsInPileBody[lvl])
                                            {
                                                maxMomentsInPileBody[lvl] = maxMomentInPile;
                                                pileLayoutDataItemsWithMaxMoment[lvl] = pli;
                                                loadCasesWithMaxMoment[lvl] = loadCase;
                                                loadCombinationsWithMaxMoment[lvl] = loadCombination;
                                                isLiquefactionsWithMaxMoment[lvl] = isLiquefaction;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // グラフ出力（ライン＋散布）
                    List<List<double>> xsLists = [axialForceResultsLevel2, axialForceResultsLevel1, axialForceResultsVL];
                    List<List<double>> ysLists = [momentResultsLevel2, momentResultsLevel1, momentResultsVL];
                    List<string> legends = ["レベル2", "レベル1", "常時"];

                    AddNMinTScottPlotGraphToBody(
                        mainPart,
                        body,
                        lineListsX, lineListsY, lineListsLegend,
                        xsLists, ysLists, legends,
                        "", "軸力[kN]", "曲げモーメント[kNm]",
                        150, 150);

                    AddAutoFigureCaption(body,
                        $"軸力-モーメント関係　杭体符号:{pileBody.PileBodyRef} | 杭区間番号: {segment.No}",
                        "図");
                }

                // 縦断図（各レベルで最大曲げモーメントのケースを可視化）
                if (mainWindowViewModel?.IsHorizontalAnalysisDone == true)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var pli = pileLayoutDataItemsWithMaxMoment[k];
                        var lc = loadCasesWithMaxMoment[k];
                        var comb = loadCombinationsWithMaxMoment[k];
                        var isLiq = isLiquefactionsWithMaxMoment[k];

                        if (pli == null || lc == null || comb == null) continue;
                        if (pli.PileNodes == null || pli.SoilNodes == null || pli.Beams == null) continue;

                        List<double> moments = [];
                        List<double> shears = [];
                        List<double> disps = [];
                        List<double> zs = [];
                        List<double> soilDisps = [];

                        // Z 軸（節点列）と地盤変位
                        for (int i = 0; i < pli.PileNodes.Count; i++)
                        {
                            double z = -pli.Z + pli.PileNodes[i].Coord.Z;
                            zs.Add(z);
                            if (i != 0 && i != pli.PileNodes.Count - 1)
                                zs.Add(z);

                            double soilUh = 0.0;
                            try
                            {
                                soilUh = pli.SoilNodes[i]
                                    .GetNodeResult(anaModel, lc, comb, isLiq)
                                    ?.CumulativeDisp?.Uh ?? 0.0;
                            }
                            catch { soilUh = 0.0; }

                            soilDisps.Add(soilUh);
                            if (i != 0 && i != pli.PileNodes.Count - 1)
                                soilDisps.Add(soilUh);
                        }

                        // 要素力・変位
                        for (int i = 0; i < pli.Beams.Count; i++)
                        {
                            var res = pli.Beams[i].GetBeamResult(anaModel, lc, comb, isLiq)?.CumulativeForce;
                            moments.Add(res?.Mi ?? 0.0);
                            moments.Add(res?.Mj ?? 0.0);

                            shears.Add(res?.Fi ?? 0.0);
                            shears.Add(res?.Fj ?? 0.0);

                            double uhI = 0.0, uhJ = 0.0;
                            try
                            {
                                uhI = pli.Beams[i].NodeI.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                uhJ = pli.Beams[i].NodeJ.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                            }
                            catch (Exception ex) { Log.Warning(ex, "[WordDoc] NodeResult取得失敗"); }
                            disps.Add(uhI);
                            disps.Add(uhJ);
                        }

                        List<string> titles = ["", "", ""];
                        List<string> xLabels = ["変位[m]", "せん断力[kN]", "曲げモーメント[kNm]"];
                        List<string> yLabels = ["Z[m]", "Z[m]", "Z[m]"];

                        AddPileElevResultToBody(
                            mainPart, body,
                            [disps, shears, moments, soilDisps], [zs, zs, zs],
                            titles, xLabels, yLabels,
                            150, 100);

                        AddAutoFigureCaption(
                            body,
                            $"杭の応力・変位　杭番号:{pli.No} | 荷重ケース:{lc.LoadName} | 荷重組合せ:{comb.Name} | 液状化: {(isLiq ? "考慮" : "非考慮")}",
                            "図");
                    }
                }
            }
        }

        // 軸力-せん断力関係（QNインタラクション）
        private void AddQNInT(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.PileBodies == null || inputModel.PileBodies.Count == 0)
                return;

            foreach (var pileBody in inputModel.PileBodies)
            {
                if (pileBody?.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                    continue;

                for (int j = 0; j < pileBody.PileBodySegments.Count; j++)
                {
                    var segment = pileBody.PileBodySegments[j];
                    if (segment?.PileSection == null) continue;
                    int pileBodySegmentNo = segment.No;
                    var pileSection = segment.PileSection;

                    // ライン（耐力曲線）- NQプロパティは既に kN 単位
                    List<List<double>> lineListsX = [];
                    List<List<double>> lineListsY = [];
                    List<string> lineListsLegend = [];

                    static List<double> ToList(IEnumerable<double>? src) => src?.ToList() ?? [];

                    try
                    {
                        var nqUUlt = pileSection.UnfactoredUltimateNQ;
                        lineListsX.Add(ToList(nqUUlt.N));
                        lineListsY.Add(ToList(nqUUlt.Q));
                        lineListsLegend.Add("低減前安全限界");

                        var nqFUlt = pileSection.FactoredUltimateNQ;
                        lineListsX.Add(ToList(nqFUlt.N));
                        lineListsY.Add(ToList(nqFUlt.Q));
                        lineListsLegend.Add("低減後安全限界");

                        var nqUDmg = pileSection.UnfactoredDamageNQ;
                        lineListsX.Add(ToList(nqUDmg.N));
                        lineListsY.Add(ToList(nqUDmg.Q));
                        lineListsLegend.Add("低減前損傷限界");

                        var nqFDmg = pileSection.FactoredDamageNQ;
                        lineListsX.Add(ToList(nqFDmg.N));
                        lineListsY.Add(ToList(nqFDmg.Q));
                        lineListsLegend.Add("低減後損傷限界");

                        var nqUSvc = pileSection.UnfactoredServiceNQ;
                        lineListsX.Add(ToList(nqUSvc.N));
                        lineListsY.Add(ToList(nqUSvc.Q));
                        lineListsLegend.Add("低減前使用限界");

                        var nqFSvc = pileSection.FactoredServiceNQ;
                        lineListsX.Add(ToList(nqFSvc.N));
                        lineListsY.Add(ToList(nqFSvc.Q));
                        lineListsLegend.Add("低減後使用限界");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WordDoc] NQ曲線取得失敗 (杭体:{pileBody.PileBodyRef}, 区間:{segment.No}): {ex.Message}");
                    }

                    // 散布点
                    List<double> axialForceResultsVL = [];
                    List<double> shearResultsVL = [];

                    List<double> axialForceResultsLevel1 = [];
                    List<double> shearResultsLevel1 = [];

                    List<double> axialForceResultsLevel2 = [];
                    List<double> shearResultsLevel2 = [];

                    // 解析済みの場合のみ散布点を作成
                    if (mainWindowViewModel?.IsHorizontalAnalysisDone == true &&
                        inputModel.PileLayoutItems != null &&
                        inputModel.LoadCasesInput != null)
                    {
                        var allSeismicLoadCases = inputModel.LoadCasesInput.AllSeismicLoadCases ?? [];
                        var allLoadCombinations = inputModel.LoadCasesInput.AllLoadCombinations ?? [];

                        foreach (var pli in inputModel.PileLayoutItems)
                        {
                            if (pli == null) continue;

                            // 常時（VL）は同一杭体のみ採用。せん断力は 0 扱い
                            try
                            {
                                int pbIdx = pli.PileBodyNo - 1;
                                if (pbIdx >= 0 && pbIdx < inputModel.PileBodies.Count &&
                                    inputModel.PileBodies[pbIdx] == pileBody)
                                {
                                    axialForceResultsVL.Add(pli.AxialForceVL0 + pli.AxialForceVLAdditional);
                                    shearResultsVL.Add(0.0);
                                }
                            }
                            catch { /* ignore */ }

                            foreach (var loadCase in allSeismicLoadCases)
                            {
                                if (loadCase == null || !loadCase.IsApplicable) continue;
                                double axialForce = pli.GetSeismicAxialForce(loadCase.No, loadCase.Level);

                                foreach (var loadCombination in allLoadCombinations)
                                {
                                    var liqPatterns = new List<bool>();
                                    if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
                                    if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatterns.Add(false);

                                    foreach (var isLiquefaction in liqPatterns)
                                    {
                                        var soilPiles = inputModel.ElementDivision?.SoilPiles;
                                        int soilIndex = pli.SoilPileAltNo - 1;
                                        if (soilPiles == null || soilIndex < 0 || soilIndex >= soilPiles.Count)
                                            continue;

                                        var soilPileForItem = soilPiles[soilIndex];
                                        if (soilPileForItem?.PileBodySegments == null ||
                                            pli.Beams == null || pli.Beams.Count == 0)
                                            continue;

                                        // この区間Noに一致するビーム群の中から、当該ケースの最大せん断力を抽出
                                        double maxShearInPile = double.MinValue;
                                        for (int iSeg = 0; iSeg < soilPileForItem.PileBodySegments.Count; iSeg++)
                                        {
                                            var segI = soilPileForItem.PileBodySegments[iSeg];
                                            if (segI == null || segI.No != pileBodySegmentNo) continue;

                                            if (iSeg < 0 || iSeg >= pli.Beams.Count) continue;
                                            var beam = pli.Beams[iSeg];
                                            if (beam == null) continue;

                                            var cum = beam.GetBeamResult(anaModel, loadCase, loadCombination, isLiquefaction)?.CumulativeForce;
                                            if (cum != null)
                                                maxShearInPile = Math.Max(maxShearInPile, cum.FabsMax);
                                        }

                                        if (maxShearInPile == double.MinValue) maxShearInPile = 0.0;

                                        if (loadCase.Level == 1)
                                        {
                                            axialForceResultsLevel1.Add(axialForce);
                                            shearResultsLevel1.Add(maxShearInPile);
                                        }
                                        else if (loadCase.Level == 2)
                                        {
                                            axialForceResultsLevel2.Add(axialForce);
                                            shearResultsLevel2.Add(maxShearInPile);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // グラフ出力（ライン＋散布）
                    List<List<double>> xsLists = [axialForceResultsLevel2, axialForceResultsLevel1, axialForceResultsVL];
                    List<List<double>> ysLists = [shearResultsLevel2, shearResultsLevel1, shearResultsVL];
                    List<string> legends = ["レベル2", "レベル1", "常時"];

                    AddNMinTScottPlotGraphToBody(
                        mainPart,
                        body,
                        lineListsX, lineListsY, lineListsLegend,
                        xsLists, ysLists, legends,
                        "", "軸力[kN]", "せん断力[kN]",
                        150, 150);

                    AddAutoFigureCaption(body,
                        $"軸力-せん断力関係　杭体符号:{pileBody.PileBodyRef} | 杭区間番号: {segment.No}",
                        "図");
                }
            }
        }

        // M-φ関係（曲げモーメント-曲率関係）— GraphWindow の DrawMPhiCurves と整合
        private void AddMPhiCurves(MainDocumentPart mainPart, Body body)
        {
            if (inputModel?.PileBodies == null || inputModel.PileBodies.Count == 0)
                return;
            if (anaModel == null) return;

            var allSeismicLoadCases = inputModel.LoadCasesInput?.AllSeismicLoadCases ?? [];
            var allLoadCombinations = inputModel.LoadCasesInput?.AllLoadCombinations ?? [];

            foreach (var pileBody in inputModel.PileBodies)
            {
                if (pileBody?.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                    continue;

                for (int j = 0; j < pileBody.PileBodySegments.Count; j++)
                {
                    var segment = pileBody.PileBodySegments[j];
                    if (segment?.PileSection == null) continue;
                    int pileBodySegmentNo = segment.No;

                    // ライン: 杭×荷重ケース×組合せ×液状化ごとの M-φ 曲線
                    // 同一 (LC, Comb, Liq, 軸力) 相当のキーで重複曲線を間引く
                    List<List<double>> lineListsX = [];
                    List<List<double>> lineListsY = [];
                    List<string> lineListsLegend = [];
                    var seenCurveKeys = new HashSet<string>();

                    // 散布点: 最終ステップの (φ, M)
                    List<double> phiResultsLevel1 = [];
                    List<double> momentResultsLevel1 = [];
                    List<double> phiResultsLevel2 = [];
                    List<double> momentResultsLevel2 = [];

                    if (inputModel.PileLayoutItems == null) continue;

                    foreach (var pli in inputModel.PileLayoutItems)
                    {
                        if (pli == null) continue;
                        int pbIdx = pli.PileBodyNo - 1;
                        if (pbIdx < 0 || pbIdx >= inputModel.PileBodies.Count ||
                            inputModel.PileBodies[pbIdx] != pileBody)
                            continue;

                        // SoilPile 取得
                        var soilPiles = inputModel.ElementDivision?.SoilPiles;
                        int soilIndex = pli.SoilPileAltNo - 1;
                        if (soilPiles == null || soilIndex < 0 || soilIndex >= soilPiles.Count) continue;
                        var soilPile = soilPiles[soilIndex];
                        if (soilPile?.PileBodySegments == null || pli.Beams == null || pli.Beams.Count == 0) continue;

                        // この区間に一致する beam 群を収集
                        var matchedBeams = new List<Beam>();
                        for (int iSeg = 0; iSeg < soilPile.PileBodySegments.Count; iSeg++)
                        {
                            var segI = soilPile.PileBodySegments[iSeg];
                            if (segI == null || segI.No != pileBodySegmentNo) continue;
                            if (iSeg >= pli.Beams.Count) continue;
                            matchedBeams.Add(pli.Beams[iSeg]);
                        }

                        foreach (var targetBeam in matchedBeams)
                        {
                            foreach (var loadCase in allSeismicLoadCases)
                            {
                                if (loadCase == null || !loadCase.IsApplicable) continue;

                                foreach (var loadCombination in allLoadCombinations)
                                {
                                    var liqPatterns = new List<bool>();
                                    if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
                                    if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatterns.Add(false);

                                    foreach (var isLiquefaction in liqPatterns)
                                    {
                                        // 軸力推定（GraphWindow と同一ロジック）
                                        double axialN = 0.0;
                                        try
                                        {
                                            double nSeis = pli.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                            if (double.IsFinite(nSeis) && nSeis != 0.0) axialN = nSeis;
                                        }
                                        catch { /* ignore */ }
                                        if (axialN == 0.0 && double.IsFinite(pli.AxialForce))
                                            axialN = pli.AxialForce;

                                        // M-φ曲線取得（GraphWindow と同一優先順位）
                                        List<double> phis = null;
                                        List<double> moments = null;

                                        int lastStep = anaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                        BeamResult beamResultForCurve = null;
                                        if (lastStep >= 0)
                                        {
                                            beamResultForCurve = targetBeam.GetBeamResult(anaModel, loadCase, loadCombination, isLiquefaction, lastStep);

                                            // 方法0: BeamResult に保存された M-φ 曲線（最優先）
                                            if (beamResultForCurve?.MPhiCurve_Phis != null && beamResultForCurve.MPhiCurve_Phis.Count >= 2)
                                            {
                                                phis = beamResultForCurve.MPhiCurve_Phis;
                                                moments = beamResultForCurve.MPhiCurve_Moments;
                                            }
                                        }

                                        // 方法1: 解析時に解決済みのキャッシュ曲線
                                        if ((phis == null || phis.Count < 2) && targetBeam.ResolvedCombinedCurve?.Points != null)
                                        {
                                            var cachedCurve = targetBeam.ResolvedCombinedCurve;
                                            if (cachedCurve.Points.Count >= 2)
                                            {
                                                phis = [.. cachedCurve.Points.Select(p => p.Phi)];
                                                moments = [.. cachedCurve.Points.Select(p => p.Moment)];
                                            }
                                        }

                                        // 方法2: フォールバック - PileSection から新規取得
                                        if ((phis == null || phis.Count < 2) && targetBeam.SegmentIndex is int fallbackSeg
                                            && fallbackSeg >= 0 && fallbackSeg < soilPile.PileBodySegments.Count)
                                        {
                                            var pileSection = soilPile.PileBodySegments[fallbackSeg].PileSection;
                                            if (pileSection != null)
                                            {
                                                try
                                                {
                                                    var mPhi = pileSection.GetMPhiRelationship(axialN);
                                                    if (mPhi.Phis != null && mPhi.Moments != null && mPhi.Phis.Count >= 2)
                                                    {
                                                        phis = mPhi.Phis;
                                                        moments = mPhi.Moments;
                                                    }
                                                }
                                                catch { /* ignore */ }
                                            }
                                        }

                                        if (phis == null || moments == null || phis.Count < 2) continue;

                                        // 曲線の重複を抑制: 同一 (LC, Comb, Liq, 軸力[10kN丸め]) はほぼ同一曲線
                                        int axialNBucket = (int)Math.Round(axialN / 10.0);
                                        string curveKey = $"{loadCase.LoadName}|{loadCombination.No}|{isLiquefaction}|{axialNBucket}";
                                        if (seenCurveKeys.Add(curveKey))
                                        {
                                            lineListsX.Add(phis);
                                            lineListsY.Add(moments);
                                            string liqTag = isLiquefaction ? "液状化" : "非液状化";
                                            lineListsLegend.Add($"{loadCase.LoadName}|Comb{loadCombination.No}|{liqTag}|N≈{axialN:F0}kN");
                                        }

                                        // 最終ステップの (φ, M) 散布点
                                        if (lastStep >= 0 && beamResultForCurve != null)
                                        {
                                            double phiFinal = beamResultForCurve.Curvature;

                                            // フォールバック: Curvature が 0 以下の場合、回転角差から計算
                                            if (phiFinal <= 0.0 && beamResultForCurve.CumulativeDisp != null)
                                            {
                                                double length = targetBeam.Length;
                                                if (length > 0)
                                                {
                                                    double dRy = beamResultForCurve.CumulativeDisp.Ryj - beamResultForCurve.CumulativeDisp.Ryi;
                                                    double dRz = beamResultForCurve.CumulativeDisp.Rzj - beamResultForCurve.CumulativeDisp.Rzi;
                                                    phiFinal = Math.Sqrt(dRy * dRy + dRz * dRz) / length;
                                                }
                                            }

                                            double mFinal = beamResultForCurve.Moment;
                                            if (!double.IsFinite(phiFinal) || !double.IsFinite(mFinal)) continue;

                                            if (loadCase.Level == 1)
                                            {
                                                phiResultsLevel1.Add(phiFinal);
                                                momentResultsLevel1.Add(mFinal);
                                            }
                                            else if (loadCase.Level == 2)
                                            {
                                                phiResultsLevel2.Add(phiFinal);
                                                momentResultsLevel2.Add(mFinal);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (lineListsX.Count == 0) continue;

                    List<List<double>> xsScatter = [phiResultsLevel2, phiResultsLevel1];
                    List<List<double>> ysScatter = [momentResultsLevel2, momentResultsLevel1];
                    List<string> scatterLegends = ["レベル2", "レベル1"];

                    AddNMinTScottPlotGraphToBody(
                        mainPart, body,
                        lineListsX, lineListsY, lineListsLegend,
                        xsScatter, ysScatter, scatterLegends,
                        "", "曲率φ[rad/m]", "曲げモーメント[kNm]",
                        150, 150, showLegend: false);

                    AddAutoFigureCaption(body,
                        $"M-φ関係　杭体符号:{pileBody.PileBodyRef} | 杭区間番号: {segment.No}",
                        "図");
                }
            }
        }

        // M-θ関係（曲げモーメント-回転角関係）— GraphWindow の DrawMThetaCurvesWithMarker と整合
        // 杭体ごとに1つのグラフにまとめる
        private void AddMThetaCurves(MainDocumentPart mainPart, Body body)
        {
            if (anaModel?.RotationalSprings == null || anaModel.RotationalSprings.Count == 0)
                return;
            if (inputModel?.PileBodies == null || inputModel.PileBodies.Count == 0)
                return;

            var allSeismicLoadCases = inputModel.LoadCasesInput?.AllSeismicLoadCases ?? [];
            var allLoadCombinations = inputModel.LoadCasesInput?.AllLoadCombinations ?? [];

            // 回転ばね → 杭レイアウトのマッピングを構築
            var rsToLayout = new Dictionary<RotationalSpring, PileLayoutDataItem>();
            foreach (var rs in anaModel.RotationalSprings)
            {
                PileLayoutDataItem pileLayout = null;
                if (rs.Name != null && rs.Name.Contains('-'))
                {
                    var parts = rs.Name.Split('-');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out int pileNo))
                        pileLayout = inputModel.PileLayoutItems?.FirstOrDefault(pl => pl.No == pileNo);
                }
                if (pileLayout == null && rs.NodeJ != null)
                    pileLayout = inputModel.PileLayoutItems?.FirstOrDefault(pl => pl.PileNodes.Count > 0 && ReferenceEquals(pl.PileNodes[0], rs.NodeJ));
                if (pileLayout == null && rs.PileBodyNo is int pb && pb > 0 && pb <= inputModel.PileBodies.Count)
                    pileLayout = inputModel.PileLayoutItems?.FirstOrDefault(pl => pl.PileBodyNo == pb);
                if (pileLayout != null)
                    rsToLayout[rs] = pileLayout;
            }

            foreach (var pileBody in inputModel.PileBodies)
            {
                // この杭体に属する回転ばねを収集
                var springsForBody = new List<(RotationalSpring rs, PileLayoutDataItem pli)>();
                foreach (var (rs, pli) in rsToLayout)
                {
                    int pbIdx = pli.PileBodyNo - 1;
                    if (pbIdx >= 0 && pbIdx < inputModel.PileBodies.Count && inputModel.PileBodies[pbIdx] == pileBody)
                        springsForBody.Add((rs, pli));
                }
                if (springsForBody.Count == 0) continue;

                List<List<double>> lineListsX = [];
                List<List<double>> lineListsY = [];
                List<string> lineListsLegend = [];
                var seenMThetaKeys = new HashSet<string>();

                List<double> thetaResultsLevel1 = [];
                List<double> momentResultsLevel1 = [];
                List<double> thetaResultsLevel2 = [];
                List<double> momentResultsLevel2 = [];

                foreach (var (rs, pileLayout) in springsForBody)
                {
                    foreach (var loadCase in allSeismicLoadCases)
                    {
                        if (loadCase == null || !loadCase.IsApplicable) continue;

                        foreach (var loadCombination in allLoadCombinations)
                        {
                            var liqPatterns = new List<bool>();
                            if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
                            if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatterns.Add(false);

                            foreach (var isLiquefaction in liqPatterns)
                            {
                                double axialN = 0.0;
                                try
                                {
                                    double nSeis = pileLayout.GetSeismicAxialForce(loadCase.No, loadCase.Level);
                                    if (double.IsFinite(nSeis) && nSeis != 0.0) axialN = nSeis;
                                }
                                catch { /* ignore */ }
                                if (axialN == 0.0 && double.IsFinite(pileLayout.AxialForce))
                                    axialN = pileLayout.AxialForce;

                                // 曲線取得（CurveXY → Curve → 線形Kθ）
                                double[] thetas;
                                double[] moments;
                                string modeTag;
                                if (rs.Mode == RotationalSpringMode.CombinedXY && rs.CurveXY != null)
                                {
                                    (thetas, moments) = rs.CurveXY.ToArrays();
                                    modeTag = "XY";
                                }
                                else if (rs.Mode == RotationalSpringMode.SingleDof && rs.Curve != null)
                                {
                                    (thetas, moments) = rs.Curve.ToArrays();
                                    modeTag = rs.Dof.ToString();
                                }
                                else
                                {
                                    double? k = rs.Mode == RotationalSpringMode.CombinedXY ? rs.KthetaXY : rs.Ktheta;
                                    if (!k.HasValue || k.Value <= 0.0) continue;
                                    const double thetaMax = 0.02;
                                    int nDiv = 50;
                                    thetas = [.. Enumerable.Range(0, nDiv).Select(i => i * thetaMax / (nDiv - 1))];
                                    moments = [.. thetas.Select(t => k.Value * t)];
                                    modeTag = rs.Mode == RotationalSpringMode.CombinedXY ? "XY" : rs.Dof.ToString();
                                }
                                if (thetas.Length == 0 || moments.Length == 0) continue;

                                // 曲線の重複抑制: 同一 (LC, Comb, Liq, Mode, 軸力[10kN丸め]) はほぼ同一曲線
                                int axialNBucket = (int)Math.Round(axialN / 10.0);
                                string curveKey = $"{loadCase.LoadName}|{loadCombination.No}|{isLiquefaction}|{modeTag}|{axialNBucket}";
                                if (seenMThetaKeys.Add(curveKey))
                                {
                                    lineListsX.Add(thetas.ToList());
                                    lineListsY.Add(moments.ToList());
                                    string liqTag = isLiquefaction ? "液状化" : "非液状化";
                                    lineListsLegend.Add($"{loadCase.LoadName}|Comb{loadCombination.No}|{liqTag}|Mode:{modeTag}|N≈{axialN:F0}kN");
                                }

                                // 最終ステップの (θ, M) 散布点
                                int lastStep = anaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
                                if (lastStep >= 0)
                                {
                                    var rsResult = rs.RotationalSpringResults?.FirstOrDefault(r =>
                                        r.LoadCase?.No == loadCase.No &&
                                        r.LoadCombination?.No == loadCombination.No &&
                                        r.IsLiquefaction == isLiquefaction &&
                                        r.Step == lastStep);

                                    if (rsResult?.CumulativeDisp != null && rsResult.CumulativeForce != null)
                                    {
                                        double dRx = rsResult.CumulativeDisp.Rxj - rsResult.CumulativeDisp.Rxi;
                                        double dRy = rsResult.CumulativeDisp.Ryj - rsResult.CumulativeDisp.Ryi;
                                        double dRz = rsResult.CumulativeDisp.Rzj - rsResult.CumulativeDisp.Rzi;

                                        double thetaFinal = rs.Mode == RotationalSpringMode.CombinedXY
                                            ? Math.Sqrt(dRx * dRx + dRy * dRy)
                                            : (rs.Dof == RotationalDof.Rx ? Math.Abs(dRx)
                                                : rs.Dof == RotationalDof.Ry ? Math.Abs(dRy)
                                                : Math.Abs(dRz));

                                        double mFinal = rs.Mode == RotationalSpringMode.CombinedXY
                                            ? Math.Sqrt(rsResult.CumulativeForce.Mxi * rsResult.CumulativeForce.Mxi +
                                                         rsResult.CumulativeForce.Myi * rsResult.CumulativeForce.Myi)
                                            : (rs.Dof == RotationalDof.Rx ? Math.Abs(rsResult.CumulativeForce.Mxi)
                                                : rs.Dof == RotationalDof.Ry ? Math.Abs(rsResult.CumulativeForce.Myi)
                                                : Math.Abs(rsResult.CumulativeForce.Mzi));

                                        if (double.IsFinite(thetaFinal) && double.IsFinite(mFinal) && thetaFinal > 0)
                                        {
                                            if (loadCase.Level == 1)
                                            {
                                                thetaResultsLevel1.Add(thetaFinal);
                                                momentResultsLevel1.Add(mFinal);
                                            }
                                            else if (loadCase.Level == 2)
                                            {
                                                thetaResultsLevel2.Add(thetaFinal);
                                                momentResultsLevel2.Add(mFinal);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (lineListsX.Count == 0) continue;

                List<List<double>> xsScatter = [thetaResultsLevel2, thetaResultsLevel1];
                List<List<double>> ysScatter = [momentResultsLevel2, momentResultsLevel1];
                List<string> scatterLegends = ["レベル2", "レベル1"];

                AddNMinTScottPlotGraphToBody(
                    mainPart, body,
                    lineListsX, lineListsY, lineListsLegend,
                    xsScatter, ysScatter, scatterLegends,
                    "", "回転角θ[rad]", "曲げモーメント[kNm]",
                    150, 150);

                AddAutoFigureCaption(body,
                    $"M-θ関係　杭体符号:{pileBody.PileBodyRef}",
                    "図");
            }
        }

        /// <summary>
        /// 全杭×全解析荷重ケースの曲げモーメント・せん断力・変位ダイアグラムを出力
        /// mainWindowViewModel.GroupPileStressBySoilPile でグループ化の有無を切替。
        /// </summary>
        private void AddAllPileStressDiagrams(MainDocumentPart mainPart, Body body,
            bool includeBending, bool includeShear)
        {
            if (anaModel == null) return;

            var loadCases = inputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases;
            var loadCombinations = inputModel.LoadCasesInput.AllLoadCombinations;
            if (loadCases == null || loadCases.Count == 0) return;
            if (loadCombinations == null || loadCombinations.Count == 0) return;

            AddPageBreak(body);
            AddHeader1(body, "杭の変位・応力ダイアグラム", 2);

            if (mainWindowViewModel.GroupPileStressBySoilPile)
            {
                AddAllPileStressDiagramsGrouped(mainPart, body, includeBending, includeShear, loadCases, loadCombinations);
                return;
            }

            foreach (var pli in inputModel.PileLayoutItems)
            {
                if (pli.PileNodes == null || pli.SoilNodes == null || pli.Beams == null) continue;
                if (pli.Beams.Count == 0) continue;

                foreach (var lc in loadCases)
                {
                    foreach (var comb in loadCombinations)
                    {
                        // 液状化パターン（ユーザー選択に基づく）
                        var liqPatterns = new List<bool>();
                        if (mainWindowViewModel.IncludeOutputLiquefactionNo)
                            liqPatterns.Add(false);
                        if (mainWindowViewModel.IncludeOutputLiquefactionYes)
                            liqPatterns.Add(true);
                        if (liqPatterns.Count == 0) continue;

                        foreach (bool isLiq in liqPatterns)
                        {
                            List<double> moments = [];
                            List<double> shears = [];
                            List<double> disps = [];
                            List<double> zs = [];
                            List<double> soilDisps = [];

                            bool hasData = false;

                            // Z軸（節点列）と地盤変位
                            for (int i = 0; i < pli.PileNodes.Count; i++)
                            {
                                double z = -pli.Z + pli.PileNodes[i].Coord.Z;
                                zs.Add(z);
                                if (i != 0 && i != pli.PileNodes.Count - 1)
                                    zs.Add(z);

                                double soilUh = 0.0;
                                try
                                {
                                    soilUh = pli.SoilNodes[i]
                                        .GetNodeResult(anaModel, lc, comb, isLiq)
                                        ?.CumulativeDisp?.Uh ?? 0.0;
                                }
                                catch { soilUh = 0.0; }
                                soilDisps.Add(soilUh);
                                if (i != 0 && i != pli.PileNodes.Count - 1)
                                    soilDisps.Add(soilUh);
                            }

                            // 要素力・変位
                            for (int i = 0; i < pli.Beams.Count; i++)
                            {
                                var res = pli.Beams[i].GetBeamResult(anaModel, lc, comb, isLiq)?.CumulativeForce;
                                if (res != null) hasData = true;

                                moments.Add(res?.Mi ?? 0.0);
                                moments.Add(res?.Mj ?? 0.0);

                                shears.Add(res?.Fi ?? 0.0);
                                shears.Add(res?.Fj ?? 0.0);

                                double uhI = 0.0, uhJ = 0.0;
                                try
                                {
                                    uhI = pli.Beams[i].NodeI.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                    uhJ = pli.Beams[i].NodeJ.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                }
                                catch { /* NodeResult取得失敗 */ }
                                disps.Add(uhI);
                                disps.Add(uhJ);
                            }

                            if (!hasData) continue;

                            // ダイアグラム出力（変位・せん断力・曲げモーメントの3列グラフ — GraphWindow と同順）
                            List<List<double>> xsLists = [];
                            List<string> titles = [];
                            List<string> xLabels = [];
                            List<string> yLabels = [];

                            // 変位は常に含める（先頭）
                            xsLists.Add(disps);
                            titles.Add("");
                            xLabels.Add("変位[m]");
                            yLabels.Add("Z[m]");

                            if (includeShear)
                            {
                                xsLists.Add(shears);
                                titles.Add("");
                                xLabels.Add("せん断力[kN]");
                                yLabels.Add("Z[m]");
                            }
                            if (includeBending)
                            {
                                xsLists.Add(moments);
                                titles.Add("");
                                xLabels.Add("曲げモーメント[kNm]");
                                yLabels.Add("Z[m]");
                            }
                            // 地盤変位（変位パネルに重ねる用）
                            xsLists.Add(soilDisps);
                            yLabels.Add("Z[m]");

                            List<List<double>> ysLists = [];
                            for (int i = 0; i < xsLists.Count; i++)
                                ysLists.Add(zs);

                            AddPileElevResultToBody(
                                mainPart, body,
                                xsLists, ysLists,
                                titles, xLabels, yLabels,
                                150, 100);

                            string liqText = isLiq ? "液状化" : "非液状化";
                            AddAutoFigureCaption(body,
                                $"杭No.{pli.No} | {lc.LoadName} | {comb.Name} | {liqText}",
                                "図");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 杭地盤セット（ElementDivision.SoilPiles）単位でグループ化し、同一セット内の杭を
        /// 系列オーバーレイとして 1 図にまとめる。(loadCase × comb × liq) ごとに 1 図生成。
        /// </summary>
        private void AddAllPileStressDiagramsGrouped(
            MainDocumentPart mainPart, Body body,
            bool includeBending, bool includeShear,
            System.Collections.Generic.IEnumerable<LoadCase> loadCases,
            System.Collections.Generic.IEnumerable<LoadCombination> loadCombinations)
        {
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            if (soilPiles == null || soilPiles.Count == 0) return;

            for (int soilIdx = 0; soilIdx < soilPiles.Count; soilIdx++)
            {
                var soilPile = soilPiles[soilIdx];
                int altNo = soilIdx + 1;

                var groupPiles = inputModel.PileLayoutItems?
                    .Where(p => p.SoilPileAltNo == altNo
                             && p.PileNodes != null && p.SoilNodes != null
                             && p.Beams != null && p.Beams.Count > 0)
                    .ToList();
                if (groupPiles == null || groupPiles.Count == 0) continue;

                string pileRef = soilPile.PileBodyInput?.PileBodyRef ?? "-";
                string soilRef = soilPile.GroundInput?.GroundRef ?? "-";
                double headZ = soilPile.Z;

                foreach (var lc in loadCases)
                {
                    foreach (var comb in loadCombinations)
                    {
                        var liqPatterns = new List<bool>();
                        if (mainWindowViewModel.IncludeOutputLiquefactionNo) liqPatterns.Add(false);
                        if (mainWindowViewModel.IncludeOutputLiquefactionYes) liqPatterns.Add(true);
                        if (liqPatterns.Count == 0) continue;

                        foreach (bool isLiq in liqPatterns)
                        {
                            // 各パネル（変位/せん断/曲げ）ごとに、杭ごとの系列を収集
                            List<List<double>> dispSeries = [];
                            List<List<double>> shearSeries = [];
                            List<List<double>> momentSeries = [];
                            List<List<double>> soilDispSeries = [];
                            List<List<double>> zSeries = [];
                            List<string> legends = [];
                            bool anyData = false;

                            foreach (var pli in groupPiles)
                            {
                                List<double> zs = [], moments = [], shears = [], disps = [], soilDisps = [];
                                bool hasData = false;

                                for (int i = 0; i < pli.PileNodes.Count; i++)
                                {
                                    double z = -pli.Z + pli.PileNodes[i].Coord.Z;
                                    zs.Add(z);
                                    if (i != 0 && i != pli.PileNodes.Count - 1) zs.Add(z);

                                    double soilUh = 0.0;
                                    try
                                    {
                                        soilUh = pli.SoilNodes[i]
                                            .GetNodeResult(anaModel, lc, comb, isLiq)
                                            ?.CumulativeDisp?.Uh ?? 0.0;
                                    }
                                    catch { /* ignore */ }
                                    soilDisps.Add(soilUh);
                                    if (i != 0 && i != pli.PileNodes.Count - 1) soilDisps.Add(soilUh);
                                }

                                for (int i = 0; i < pli.Beams.Count; i++)
                                {
                                    var res = pli.Beams[i].GetBeamResult(anaModel, lc, comb, isLiq)?.CumulativeForce;
                                    if (res != null) hasData = true;
                                    moments.Add(res?.Mi ?? 0.0);
                                    moments.Add(res?.Mj ?? 0.0);
                                    shears.Add(res?.Fi ?? 0.0);
                                    shears.Add(res?.Fj ?? 0.0);

                                    double uhI = 0.0, uhJ = 0.0;
                                    try
                                    {
                                        uhI = pli.Beams[i].NodeI.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                        uhJ = pli.Beams[i].NodeJ.GetNodeResult(anaModel, lc, comb, isLiq)?.CumulativeDisp?.Uh ?? 0.0;
                                    }
                                    catch { /* ignore */ }
                                    disps.Add(uhI);
                                    disps.Add(uhJ);
                                }

                                if (!hasData) continue;
                                anyData = true;
                                zSeries.Add(zs);
                                dispSeries.Add(disps);
                                shearSeries.Add(shears);
                                momentSeries.Add(moments);
                                soilDispSeries.Add(soilDisps);
                                legends.Add($"杭No.{pli.No}");
                            }

                            if (!anyData) continue;

                            // パネル構成: 変位 → (せん断力) → (曲げ)
                            List<List<List<double>>> xsByPanelBySeries = [dispSeries];
                            List<string> titles = [""];
                            List<string> xLabels = ["変位[m]"];
                            List<string> yLabels = ["Z[m]"];

                            if (includeShear)
                            {
                                xsByPanelBySeries.Add(shearSeries);
                                titles.Add("");
                                xLabels.Add("せん断力[kN]");
                                yLabels.Add("Z[m]");
                            }
                            if (includeBending)
                            {
                                xsByPanelBySeries.Add(momentSeries);
                                titles.Add("");
                                xLabels.Add("曲げモーメント[kNm]");
                                yLabels.Add("Z[m]");
                            }

                            AddPileElevResultMultiToBody(
                                mainPart, body,
                                xsByPanelBySeries,
                                zSeries,
                                legends,
                                soilDispSeries,
                                titles, xLabels, yLabels,
                                150, 100);

                            string liqText = isLiq ? "液状化" : "非液状化";
                            AddAutoFigureCaption(body,
                                $"杭地盤セット 杭体:{pileRef}|地盤:{soilRef}|杭頭Z={headZ:N1}m | {lc.LoadName} | {comb.Name} | {liqText}",
                                "図");
                        }
                    }
                }
            }
        }

        // 荷重沈下関係グラフ挿入メソッド
        private void AddSettlementGraph(MainDocumentPart mainPart, Body body)
        {
            if (inputModel.ElementDivision?.SoilPiles == null) return;
            foreach (var soilPile in inputModel.ElementDivision.SoilPiles)
            {
                string pileRef = soilPile.PileBodyInput?.PileBodyRef ?? "-";
                string soilRef = soilPile.GroundInput?.GroundRef ?? "-";
                double alt = soilPile.Z;
                string caption = $"杭体:{pileRef}|地盤:{soilRef}|杭頭Z{alt:N1}m";

                var loadDisplacements = soilPile.LoadDisplacements;
                if (loadDisplacements == null || loadDisplacements.Count == 0) continue;

                // --- 1. 荷重-杭頭沈下曲線 ---
                List<double> dd0s = [];
                List<double> pileTopLoads = [];
                List<double> rzToes = [];
                List<double> rzCircums = [];
                List<double> weights = [];
                List<double> ddns = [];

                foreach (var ld in loadDisplacements)
                {
                    dd0s.Add(ld.DD0s);
                    ddns.Add(ld.DDns);
                    pileTopLoads.Add(ld.PileTopLoad);
                    rzToes.Add(ld.RzToe);
                    rzCircums.Add(ld.RzCircum);
                    weights.Add(ld.Weight);
                }

                AddScottPlotGraphWithMultipleDataToBody(
                    mainPart, body,
                    [dd0s, dd0s, dd0s, dd0s],
                    [pileTopLoads, rzToes, rzCircums, weights],
                    ["杭頭荷重", "杭先端支持力", "杭周面抵抗力", "杭自重"],
                    "", "杭頭沈下量[mm]", "荷重[kN]",
                    150, 150);
                AddAutoFigureCaption(body, $"{caption}：荷重-杭頭沈下関係", "図");

                // --- 2. 杭頭沈下量の表 ---
                AddSettlementDetailTable(body, loadDisplacements, caption);

                // --- 3. 杭先端沈下曲線 ---
                AddScottPlotGraphWithMultipleDataToBody(
                    mainPart, body,
                    [ddns, ddns],
                    [pileTopLoads, rzToes],
                    ["杭頭荷重", "杭先端支持力"],
                    "", "杭先端沈下量[mm]", "荷重[kN]",
                    150, 150);
                AddAutoFigureCaption(body, $"{caption}：荷重-杭先端沈下関係", "図");

                // --- 4. 杭周面抵抗力グラフ ---
                if (soilPile.PileCircumVerticals != null && soilPile.PileCircumVerticals.Count > 0)
                {
                    AddCircumResistanceGraph(mainPart, body, soilPile, caption);
                }
            }
        }

        /// <summary>
        /// 杭頭沈下量の表を追加
        /// </summary>
        private void AddSettlementDetailTable(
            Body body,
            System.Collections.ObjectModel.ObservableCollection<FEM.VerticalLoadTransferMethod.LoadDisplacement> loadDisplacements,
            string caption)
        {
            AddLineBreak(body);
            AddAutoFigureCaption(body, $"{caption}：荷重-沈下関係", "表");

            double fontSize = 7;
            Table table = CreateTableWithBorders();

            TableRow headerRow = CreateHeaderRow(
                CreateTableCell(["杭頭荷重", "[kN]"], fontSize, "center"),
                CreateTableCell(["杭頭沈下量", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端沈下量", "[mm]"], fontSize, "center"),
                CreateTableCell(["杭先端支持力", "[kN]"], fontSize, "center"),
                CreateTableCell(["杭周面抵抗力", "[kN]"], fontSize, "center"),
                CreateTableCell(["杭自重", "[kN]"], fontSize, "center")
            );
            table.Append(headerRow);

            foreach (var ld in loadDisplacements)
            {
                TableRow row = new();
                row.Append(CreateTableCell([$"{ld.PileTopLoad:N1}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{ld.DD0s:N3}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{ld.DDns:N3}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{ld.RzToe:N1}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{ld.RzCircum:N1}"], fontSize, "right"));
                row.Append(CreateTableCell([$"{ld.Weight:N1}"], fontSize, "right"));
                table.Append(row);
            }

            body.Append(table);
        }

        /// <summary>
        /// 杭周面抵抗力グラフを追加
        /// </summary>
        private void AddCircumResistanceGraph(
            MainDocumentPart mainPart,
            Body body,
            Models.InputData.SoilPile soilPile,
            string caption)
        {
            List<List<double>> xsLists = [];
            List<List<double>> ysLists = [];
            List<string> legends = [];

            int segNo = 0;
            foreach (var pcv in soilPile.PileCircumVerticals)
            {
                segNo++;
                // τ-S関係曲線（折線）: 0→S1→S2→延長
                List<double> sValues = [0, pcv.S1, pcv.S2, pcv.S2 * 1.5];
                List<double> tauValues = [0, pcv.Tau1, pcv.Tau2, pcv.Tau2];

                xsLists.Add(sValues);
                ysLists.Add(tauValues);
                legends.Add($"区間{segNo} (Z={pcv.Bottom:N1}~{pcv.Top:N1}m)");
            }

            if (xsLists.Count > 0)
            {
                AddScottPlotGraphWithMultipleDataToBody(
                    mainPart, body,
                    xsLists, ysLists, legends,
                    "", "相対変位[mm]", "周面抵抗力[kN/m]",
                    150, 150);
                AddAutoFigureCaption(body, $"{caption}：杭周面抵抗力-変位関係", "図");
            }
        }
    }
}
