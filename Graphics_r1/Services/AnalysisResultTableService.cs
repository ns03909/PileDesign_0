using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    public sealed class AnalysisResultTableService
    {
        public IReadOnlyList<ResultTable> BuildTables(
            AnaModel model,
            LoadCase? loadCase,
            LoadCombination? loadCombination,
            bool isLiquefaction,
            int step,
            InputModel? inputModel = null)
        {
            var tables = new List<ResultTable>();

            var beams = model.Beams?.ToList() ?? [];
            var nodes = model.Nodes?.ToList() ?? [];
            var rotSprings = model.RotationalSprings?.ToList() ?? [];

            // 結果検索用のヘルパー: BeamResultsから該当する結果を取得
            BeamResult? FindBeamResult(Beam beam) =>
                beam.BeamResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            // 結果検索用のヘルパー: RotationalSpringResultsから該当する結果を取得
            RotationalSpringResult? FindRotSpringResult(RotationalSpring rs) =>
                rs.RotationalSpringResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            // 結果検索用のヘルパー: NodeResultsから該当する結果を取得
            NodeResult? FindNodeResult(PileDesign.FEM.Node node) =>
                node.NodeResults?.FirstOrDefault(r =>
                    r.IsLiquefaction == isLiquefaction &&
                    r.Step == step &&
                    (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                    (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));

            if (beams.Count > 0 || rotSprings.Count > 0)
            {
                var forceRows = new List<object>();

                // 梁要素
                for (int idx = 0; idx < beams.Count; idx++)
                {
                    var beam = beams[idx];
                    int n1 = nodes.IndexOf(beam.NodeI) + 1;
                    int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                    var result = FindBeamResult(beam);
                    var bf = result?.CumulativeForce ?? beam.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    forceRows.Add(ElementSectionForceRow.From(idx + 1, n1, n2, beam, bf));
                }

                // リンク要素（杭頭接合ばね）
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    int n1 = nodes.IndexOf(rs.NodeI) + 1;
                    int n2 = nodes.IndexOf(rs.NodeJ) + 1;
                    var result = FindRotSpringResult(rs);
                    var bf = result?.CumulativeForce ?? rs.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    forceRows.Add(ElementSectionForceRow.FromSpring(beams.Count + idx + 1, n1, n2, rs, bf));
                }

                tables.Add(new ResultTable
                {
                    Name = "梁断面力",
                    Category = "BeamForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(ElementSectionForceRow)),
                    Rows = forceRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            if (beams.Count > 0 || rotSprings.Count > 0)
            {
                var dispRows = new List<object>();

                // 梁要素
                for (int idx = 0; idx < beams.Count; idx++)
                {
                    var beam = beams[idx];
                    int n1 = nodes.IndexOf(beam.NodeI) + 1;
                    int n2 = nodes.IndexOf(beam.NodeJ) + 1;
                    var result = FindBeamResult(beam);
                    var bd = result?.CumulativeDisp ?? beam.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    dispRows.Add(ElementSectionDispRow.From(idx + 1, n1, n2, beam, bd));
                }

                // リンク要素（杭頭接合ばね）
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];
                    int n1 = nodes.IndexOf(rs.NodeI) + 1;
                    int n2 = nodes.IndexOf(rs.NodeJ) + 1;
                    var result = FindRotSpringResult(rs);
                    var bd = result?.CumulativeDisp ?? rs.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    dispRows.Add(ElementSectionDispRow.FromSpring(beams.Count + idx + 1, n1, n2, rs, bd));
                }

                tables.Add(new ResultTable
                {
                    Name = "梁断面変位",
                    Category = "BeamDisp",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(ElementSectionDispRow)),
                    Rows = dispRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            // 杭頭応力テーブル（最も上の杭要素の i 端応力、全体座標系に変換）
            if (beams.Count > 0)
            {
                var pileHeadRows = new List<object>();
                int pileNo = 0;
                foreach (var beam in beams)
                {
                    if (!beam.IsPileHeadElement) continue;
                    pileNo++;
                    var result = FindBeamResult(beam);
                    var bf = result?.CumulativeForce ?? beam.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                    // 要素座標系 → 全体座標系に変換
                    var f_local = bf.GetVector();
                    var T = beam.GetCachedCoordTransform();
                    var f_global = T.Transpose() * f_local;

                    var bfGlobal = new BeamForce(
                        f_global[0], f_global[1], f_global[2], f_global[3], f_global[4], f_global[5],
                        f_global[6], f_global[7], f_global[8], f_global[9], f_global[10], f_global[11]);

                    pileHeadRows.Add(PileHeadForceRow.FromBeamIEnd(pileNo, beam, bfGlobal));
                }

                if (pileHeadRows.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = "杭頭応力",
                        Category = "PileHeadForce",
                        Columns = ResultColumnReflectionCache.GetColumns(typeof(PileHeadForceRow)),
                        Rows = pileHeadRows,
                        LoadCaseName = loadCase?.LoadName ?? "",
                        LoadCombinationName = loadCombination?.Name ?? "",
                        IsLiquefaction = isLiquefaction
                    });
                }
            }

            if (nodes.Count > 0)
            {
                var nodeDispRows = nodes
                    .Select((node, idx) =>
                    {
                        // 保存された結果から取得、なければ現在の値を使用
                        var result = FindNodeResult(node);
                        var disp = result?.CumulativeDisp ?? node.CumulativeDisp ?? new NodeDisp(0, 0, 0, 0, 0, 0);
                        return NodeDisplacementRow.From(idx + 1, node, disp);
                    })
                    .Cast<object>()
                    .ToList();

                tables.Add(new ResultTable
                {
                    Name = "節点変位（水平）",
                    Category = "NodeDisp",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(NodeDisplacementRow)),
                    Rows = nodeDispRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            // 地盤反力テーブル（水平地盤ばね）
            var soilSprings = model.HorizontalSoilSprings?.ToList() ?? [];
            if (soilSprings.Count > 0)
            {
                HorizontalSpringResult? FindSoilSpringResult(HorizontalSoilSpring hss) =>
                    hss.HorizontalSpringResults?.FirstOrDefault(r =>
                        r.IsLiquefaction == isLiquefaction &&
                        r.Step == step &&
                        (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                        (loadCombination == null || r.LoadCombination?.No == loadCombination.No));

                var soilSpringRows = new List<object>();
                for (int idx = 0; idx < soilSprings.Count; idx++)
                {
                    var spring = soilSprings[idx];
                    int n1 = nodes.IndexOf(spring.NodeI) + 1;
                    int n2 = nodes.IndexOf(spring.NodeJ) + 1;
                    var result = FindSoilSpringResult(spring);
                    var bf = result?.CumulativeForce ?? spring.CumulativeForce ?? new BeamForce(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    var bd = result?.CumulativeDisp ?? spring.CumulativeDisp ?? new BeamDisp(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                    soilSpringRows.Add(SoilSpringForceRow.From(idx + 1, n1, n2, spring, bf, bd));
                }

                tables.Add(new ResultTable
                {
                    Name = "地盤反力（水平）",
                    Category = "SoilSpringForce",
                    Columns = ResultColumnReflectionCache.GetColumns(typeof(SoilSpringForceRow)),
                    Rows = soilSpringRows,
                    LoadCaseName = loadCase?.LoadName ?? "",
                    LoadCombinationName = loadCombination?.Name ?? "",
                    IsLiquefaction = isLiquefaction
                });
            }

            // M-φ曲線テーブル（杭要素ごと）
            if (beams.Count > 0)
            {
                // Beam → (杭No, 杭頭からの要素順) の辞書を構築
                // NodeI名 "杭節点-{pileNo}-{nodeIdx}" から杭No取得、同一杭内の出現順で要素順を決定
                var beamPileInfo = new Dictionary<Beam, (int pileNo, int segOrder)>();
                var pileBeamCounter = new Dictionary<int, int>(); // 杭No → 要素カウンタ
                foreach (var b in beams)
                {
                    if (b.NodeI?.Name != null && b.NodeI.Name.StartsWith("杭節点-"))
                    {
                        var parts = b.NodeI.Name.Split('-');
                        if (parts.Length >= 3 && int.TryParse(parts[1], out int pNo))
                        {
                            if (!pileBeamCounter.TryGetValue(pNo, out int cnt)) cnt = 0;
                            cnt++;
                            pileBeamCounter[pNo] = cnt;
                            beamPileInfo[b] = (pNo, cnt);
                        }
                    }
                }

                var mphiRows = new List<object>();
                for (int idx = 0; idx < beams.Count; idx++)
                {
                    var beam = beams[idx];
                    var result = FindBeamResult(beam);

                    // M-φ曲線の取得: BeamResult → Beam.ResolvedCombinedCurve
                    List<double>? phis = result?.MPhiCurve_Phis;
                    List<double>? moments = result?.MPhiCurve_Moments;
                    if (phis == null || moments == null || phis.Count < 2)
                    {
                        var curve = beam.ResolvedCombinedCurve;
                        if (curve?.Points != null && curve.Points.Count >= 2)
                        {
                            phis = curve.Points.Select(p => p.Phi).ToList();
                            moments = curve.Points.Select(p => p.Moment).ToList();
                        }
                    }
                    if (phis == null || moments == null || phis.Count < 2) continue;

                    // -Fxi: 圧縮が正になるよう符号反転
                    double analysisAxialN = -(result?.CumulativeForce?.Fxi ?? beam.CumulativeForce?.Fxi ?? 0);

                    beamPileInfo.TryGetValue(beam, out var pileInfo);

                    // 入力値軸力を取得（杭配置のAxialForce: 圧縮正 kN）
                    double inputAxialN = 0;
                    if (pileInfo.pileNo > 0 && inputModel?.PileLayoutItems != null)
                    {
                        var pile = inputModel.PileLayoutItems.FirstOrDefault(p => p.No == pileInfo.pileNo);
                        if (pile != null)
                            inputAxialN = pile.AxialForce; // 最終ステップの入力値軸力（kN）
                    }

                    for (int j = 0; j < phis.Count; j++)
                    {
                        double ei = CalcSegmentSlope(phis, moments, j);
                        mphiRows.Add(new MPhiCurveRow
                        {
                            ElementIndex = idx + 1,
                            PileNo = pileInfo.pileNo,
                            SegmentOrder = pileInfo.segOrder,
                            ElementName = beam.Name,
                            InputAxialForce = inputAxialN,
                            AnalysisAxialForce = analysisAxialN,
                            PointIndex = j + 1,
                            Phi = phis[j],
                            Moment = moments[j],
                            EI = ei,
                        });
                    }

                    // 最終ステップの解析結果行を追加
                    if (result?.CumulativeDisp != null && result?.CumulativeForce != null)
                    {
                        var bd = result.CumulativeDisp;
                        // 曲率 = (θi - θj) / L の近似、または保存済みCurvatureを使用
                        double phiRes = result.Curvature;
                        double mRes = result.Moment;
                        if (System.Math.Abs(phiRes) < 1e-15 && System.Math.Abs(mRes) < 1e-15)
                        {
                            // Curvatureが保存されていない場合はスキップ
                        }
                        else
                        {
                            string status = FindSegmentStatus(phis, phiRes);
                            double eiRes = CalcSlopeAtValue(phis, moments, phiRes);
                            mphiRows.Add(new MPhiCurveRow
                            {
                                ElementIndex = idx + 1,
                                PileNo = pileInfo.pileNo,
                                SegmentOrder = pileInfo.segOrder,
                                ElementName = beam.Name,
                                InputAxialForce = inputAxialN,
                            AnalysisAxialForce = analysisAxialN,
                                PointIndex = 0,
                                Phi = phiRes,
                                Moment = mRes,
                                EI = eiRes,
                                Status = $"★結果({status})",
                            });
                        }
                    }
                }

                if (mphiRows.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = "杭体M-φ",
                        Category = "MPhiCurve",
                        Columns = ResultColumnReflectionCache.GetColumns(typeof(MPhiCurveRow)),
                        Rows = mphiRows,
                        LoadCaseName = loadCase?.LoadName ?? "",
                        LoadCombinationName = loadCombination?.Name ?? "",
                        IsLiquefaction = isLiquefaction
                    });
                }
            }

            // M-θ曲線テーブル（杭頭ばねごと）
            // 曲線 (CurveXY/Curve) が null でも Kθ 線形フォールバックで行を生成し、
            // 必ず解析結果行 (★結果) は追加する。グラフ側と整合させ、テーブルが
            // 一覧から消える/値比較できない状況を回避する。
            if (rotSprings.Count > 0)
            {
                var mthetaRows = new List<object>();
                // Y 案: 表示中ケースに対応するスナップショットを引いて使う。
                string snapKey = RotationalSpring.MakeCaseKey(
                    loadCase?.LoadName, loadCombination?.No ?? 0, isLiquefaction);
                for (int idx = 0; idx < rotSprings.Count; idx++)
                {
                    var rs = rotSprings[idx];

                    // M-θ曲線の取得 (snapshot 優先、無ければ rs 直接、それも無ければ K·θ フォールバック)
                    MomentRotationCurve? curve = null;
                    RotationalSpringMode snapMode = rs.Mode;
                    double? snapKxy = rs.KthetaXY;
                    double? snapKsingle = rs.Ktheta;
                    string snapReason = "";
                    if (rs.CaseMThetaSnapshots.TryGetValue(snapKey, out var snap))
                    {
                        curve = snap.CurveXY ?? snap.Curve;
                        snapMode = snap.Mode;
                        snapKxy = snap.KthetaXY;
                        snapKsingle = snap.Ktheta;
                        snapReason = snap.SetupReason;
                    }
                    else
                    {
                        // フォールバック (旧経路): rs 直接の構成
                        curve = rs.CurveXY ?? rs.Curve;
                    }

                    var thetas = new List<double>();
                    var moms = new List<double>();
                    bool isFallbackCurve = false;

                    if (curve?.Points != null && curve.Points.Count >= 1)
                    {
                        // 原点を追加（曲線は原点を含まない場合がある）
                        if (curve.Points[0].Theta > 1e-12)
                        {
                            thetas.Add(0.0);
                            moms.Add(0.0);
                        }
                        foreach (var p in curve.Points)
                        {
                            thetas.Add(p.Theta);
                            moms.Add(p.Moment);
                        }
                    }
                    else
                    {
                        // フォールバック: Kθ から線形 M = K·θ を 50 点生成
                        // (GraphViewModel.DrawMThetaCurvesWithMarker と同条件)
                        double? k = snapMode == RotationalSpringMode.CombinedXY ? snapKxy : snapKsingle;
                        if (!k.HasValue || k.Value <= 0.0) continue;
                        const double thetaMax = 0.02;
                        const int nDiv = 50;
                        for (int i = 0; i < nDiv; i++)
                        {
                            double t = i * thetaMax / (nDiv - 1);
                            thetas.Add(t);
                            moms.Add(k.Value * t);
                        }
                        isFallbackCurve = true;
                    }

                    for (int j = 0; j < thetas.Count; j++)
                    {
                        double kth = CalcSegmentSlope(thetas, moms, j);
                        // PointIndex=1 (先頭行) には経路情報を出す。フォールバック時は K·θ線形外挿、
                        // それ以外はスナップショットが記録した SetupReason をそのまま表示。
                        // snapReason が空なら旧経路 (rs.LastSetupReason) で補完。
                        string reasonText = !string.IsNullOrEmpty(snapReason) ? snapReason : (rs.LastSetupReason ?? "?");
                        string headerStatus = j == 0
                            ? (isFallbackCurve
                                ? $"K·θ線形外挿 [{reasonText}]"
                                : reasonText)
                            : "";
                        mthetaRows.Add(new MThetaCurveRow
                        {
                            SpringIndex = idx + 1,
                            SpringName = rs.Name,
                            PointIndex = j + 1,
                            Theta = thetas[j],
                            Moment = moms[j],
                            Ktheta = kth,
                            Status = headerStatus,
                        });
                    }

                    // 最終ステップの解析結果行を追加（曲線フォールバック時も含めて常時）
                    var rsResult = FindRotSpringResult(rs);
                    if (rsResult?.CumulativeDisp != null && rsResult?.CumulativeForce != null)
                    {
                        var bd = rsResult.CumulativeDisp;
                        var bf = rsResult.CumulativeForce;
                        // CombinedXY: θ = √(dRx² + dRy²), M = √(Mx² + My²)
                        double dRx = bd.Rxi - bd.Rxj;
                        double dRy = bd.Ryi - bd.Ryj;
                        double thetaRes = System.Math.Sqrt(dRx * dRx + dRy * dRy);
                        double mRes = System.Math.Sqrt(bf.Mxi * bf.Mxi + bf.Myi * bf.Myi);

                        string status = FindSegmentStatus(thetas, thetaRes);
                        double kthRes = CalcSlopeAtValue(thetas, moms, thetaRes);
                        mthetaRows.Add(new MThetaCurveRow
                        {
                            SpringIndex = idx + 1,
                            SpringName = rs.Name,
                            PointIndex = 0,
                            Theta = thetaRes,
                            Moment = mRes,
                            Ktheta = kthRes,
                            Status = $"★結果({status})",
                        });
                    }
                }

                if (mthetaRows.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = "杭頭M-θ",
                        Category = "MThetaCurve",
                        Columns = ResultColumnReflectionCache.GetColumns(typeof(MThetaCurveRow)),
                        Rows = mthetaRows,
                        LoadCaseName = loadCase?.LoadName ?? "",
                        LoadCombinationName = loadCombination?.Name ?? "",
                        IsLiquefaction = isLiquefaction
                    });
                }
            }

            // 外力・反力サマリーテーブル
            {
                var summaryRows = new List<object>();

                // 1. 代表節点慣性力（ActionPoint = Nodes[0]）
                if (nodes.Count > 0)
                {
                    var ap = nodes[0];
                    var apResult = ap.NodeResults?.FirstOrDefault(r =>
                        r.IsLiquefaction == isLiquefaction && r.Step == step &&
                        (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                        (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));
                    var apLoad = apResult?.CumulativedLoad ?? ap.CumulativedLoad;
                    if (apLoad != null)
                    {
                        summaryRows.Add(new ForceSummaryRow
                        {
                            Item = "代表節点慣性力",
                            Fx = apLoad.Fx, Fy = apLoad.Fy, Fz = apLoad.Fz,
                            Fh = System.Math.Sqrt(apLoad.Fx * apLoad.Fx + apLoad.Fy * apLoad.Fy)
                        });
                    }
                }

                // 2. 杭周地盤水平反力の合計（HorizontalSoilSpring: NodeJ名が杭地盤節点-で始まるもの）
                {
                    double sumFx = 0, sumFy = 0, sumFz = 0;
                    var pileSoilSprings = model.HorizontalSoilSprings?.Where(s =>
                        s.NodeJ?.Name.StartsWith("杭地盤節点-") == true) ?? [];
                    foreach (var spring in pileSoilSprings)
                    {
                        var sr = spring.HorizontalSpringResults?.FirstOrDefault(r =>
                            r.IsLiquefaction == isLiquefaction && r.Step == step &&
                            (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                            (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));
                        var bf = sr?.CumulativeForce ?? spring.CumulativeForce;
                        if (bf == null) continue;
                        sumFx += bf.Fxi; sumFy += bf.Fyi; sumFz += bf.Fzi;
                    }
                    summaryRows.Add(new ForceSummaryRow
                    {
                        Item = "杭周地盤反力合計",
                        Fx = sumFx, Fy = sumFy, Fz = sumFz,
                        Fh = System.Math.Sqrt(sumFx * sumFx + sumFy * sumFy)
                    });
                }

                // 4. 土圧合力ばね水平反力の合計（NodeI名が根入部節点のばね）
                {
                    double sumFx = 0, sumFy = 0, sumFz = 0;
                    var dgbSprings = model.HorizontalSoilSprings?.Where(s =>
                        s.NodeI?.Name == "根入部節点") ?? [];
                    foreach (var spring in dgbSprings)
                    {
                        var sr = spring.HorizontalSpringResults?.FirstOrDefault(r =>
                            r.IsLiquefaction == isLiquefaction && r.Step == step &&
                            (loadCase == null || r.LoadCase?.LoadName == loadCase.LoadName) &&
                            (loadCombination == null || r.LoadCombination?.Name == loadCombination.Name));
                        var bf = sr?.CumulativeForce ?? spring.CumulativeForce;
                        if (bf == null) continue;
                        sumFx += bf.Fxi; sumFy += bf.Fyi; sumFz += bf.Fzi;
                    }
                    summaryRows.Add(new ForceSummaryRow
                    {
                        Item = "土圧合力ばね反力合計",
                        Fx = sumFx, Fy = sumFy, Fz = sumFz,
                        Fh = System.Math.Sqrt(sumFx * sumFx + sumFy * sumFy)
                    });
                }

                if (summaryRows.Count > 0)
                {
                    tables.Add(new ResultTable
                    {
                        Name = "外力・反力サマリー",
                        Category = "ForceSummary",
                        Columns = ResultColumnReflectionCache.GetColumns(typeof(ForceSummaryRow)),
                        Rows = summaryRows,
                        LoadCaseName = loadCase?.LoadName ?? "",
                        LoadCombinationName = loadCombination?.Name ?? "",
                        IsLiquefaction = isLiquefaction
                    });
                }
            }

            return tables;
        }

        /// <summary>隣接点間の傾きを計算</summary>
        private static double CalcSegmentSlope(List<double> xs, List<double> ys, int j)
        {
            if (j > 0 && System.Math.Abs(xs[j] - xs[j - 1]) > 1e-15)
                return (ys[j] - ys[j - 1]) / (xs[j] - xs[j - 1]);
            if (j == 0 && xs.Count > 1 && System.Math.Abs(xs[1] - xs[0]) > 1e-15)
                return (ys[1] - ys[0]) / (xs[1] - xs[0]);
            return 0;
        }

        /// <summary>指定値がどの区間に属するかを PointIdx で返す（例: "区間2-3"）</summary>
        private static string FindSegmentStatus(List<double> xs, double value)
        {
            int n = xs.Count;
            if (n < 2) return "";
            if (value >= xs[n - 1])
                return $"区間{n}-超";
            for (int i = 0; i < n - 1; i++)
            {
                if (value <= xs[i + 1])
                    return $"区間{i + 1}-{i + 2}";
            }
            return "";
        }

        /// <summary>指定値での区間傾きを返す</summary>
        private static double CalcSlopeAtValue(List<double> xs, List<double> ys, double value)
        {
            for (int i = 0; i < xs.Count - 1; i++)
            {
                if (value <= xs[i + 1] || i == xs.Count - 2)
                {
                    double dx = xs[i + 1] - xs[i];
                    return System.Math.Abs(dx) > 1e-15 ? (ys[i + 1] - ys[i]) / dx : 0;
                }
            }
            return 0;
        }
    }
}