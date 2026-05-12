using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PileDesign.Services
{
    /// <summary>
    /// 個別矩形（基礎梁考慮）モード用の反復沈下解析。
    ///
    /// アルゴリズム (Picard 反復):
    ///   1. Pi 初期 = 杭の柱軸力 (Ppi)
    ///   2. Pi を各杭の支配面積 (一辺=√π·r、円と等価面積) に矩形荷重として作用させ、
    ///      Steinbrenner で各杭位置の沈下 S1i を算出
    ///   3. 杭頭ばね剛性 ki = Pi / S1i (零除算/負ばね対策あり)
    ///   4. 線形ばね基礎梁モデルを構築 (各杭頭に ki、外部荷重 Ppi を作用)、解いて S2i を取得
    ///   5. |S2 − S1| / max|S| が許容値以下なら収束、そうでなければ Pi := ki·S2i で 2 へ
    ///
    /// 単杭沈下解析 (LoadDisplacements) を必要としない (= VerticalBeamCalculationViewModel
    /// とは独立) 線形ばね解析。VerticalBeamModelling は流用するが、SpringCurves は使わず
    /// 各反復で ki を直接 SetKe で書き換える。
    /// </summary>
    public class IterativeBeamSettlementResult
    {
        public bool Converged { get; set; }
        public int IterationCount { get; set; }
        public double FinalResidual { get; set; }

        /// <summary>各反復の概要ログ (人間可読)。</summary>
        public List<string> Log { get; set; } = [];

        /// <summary>収束時の杭頭反力 Pi (kN)。Key = PileLayoutDataItem.PileNo</summary>
        public Dictionary<int, double> PileReactions { get; set; } = [];

        /// <summary>収束時の杭頭ばね剛性 ki (kN/m)。Key = PileNo</summary>
        public Dictionary<int, double> SpringStiffness { get; set; } = [];

        /// <summary>収束時の Steinbrenner 沈下 S1i (m)。Key = PileNo</summary>
        public Dictionary<int, double> SteinbrennerSettlement { get; set; } = [];

        /// <summary>収束時の基礎梁モデル沈下 S2i (m)。Key = PileNo (実用上の最終沈下)</summary>
        public Dictionary<int, double> BeamSettlement { get; set; } = [];

        /// <summary>収束後の矩形荷重 (確定 QA = Pi、Linked付き)。Steinbrenner グリッド描画に流用可。</summary>
        public ObservableCollection<RectLoad> ConvergedRectLoads { get; set; } = [];

        /// <summary>収束時の各節点の変位 (Uz mm, Rx/Ry rad)。GroundNode-* は除外。</summary>
        public List<FEM.VerticalBeamNodeResult> NodeResults { get; set; } = [];

        /// <summary>収束時の各梁要素の断面力 (I 端・J 端)。</summary>
        public List<FEM.VerticalBeamBeamResult> BeamResults { get; set; } = [];
    }

    public static class IterativeBeamSettlementService
    {
        /// <summary>
        /// 個別矩形（基礎梁考慮）の反復沈下解析を実行する。
        /// </summary>
        /// <param name="inputModel">入力モデル (杭配置・基礎梁・群杭沈下用土層が必須)</param>
        /// <param name="initialPpi">各杭の柱軸力 Ppi (kN, 正=圧縮)。Key = PileLayoutDataItem.PileNo</param>
        /// <param name="caseLabel">荷重ケースラベル (ログ用、例: "VL", "L1-1: …")</param>
        /// <param name="maxIter">最大反復回数</param>
        /// <param name="tol">収束許容誤差 (= max|ΔS| / max|S|)</param>
        /// <param name="kMin">ばね剛性の下限 (kN/m)。Pi/S1 が負・微小になるときの保護</param>
        /// <param name="kMax">ばね剛性の上限 (kN/m)。S1≈0 のときの暴走防止</param>
        public static IterativeBeamSettlementResult Run(
            InputModel inputModel,
            Dictionary<int, double> initialPpi,
            string caseLabel = "",
            int maxIter = 30,
            double tol = 1e-3,
            double kMin = 1e3,
            double kMax = 1e10)
        {
            var result = new IterativeBeamSettlementResult();
            var piles = inputModel.PileLayoutItems;
            var soilPiles = inputModel.ElementDivision?.SoilPiles;
            var pgs = inputModel.PileGroupSettlement;

            if (piles == null || piles.Count == 0)
            {
                result.Log.Add("[ERROR] 杭配置がありません。");
                return result;
            }
            if (soilPiles == null || soilPiles.Count == 0)
            {
                result.Log.Add("[ERROR] 地盤・杭・レベルセットがありません。");
                return result;
            }
            if (pgs?.SettlementSoilLayers == null || pgs.SettlementSoilLayers.Count == 0)
            {
                result.Log.Add("[ERROR] 群杭沈下用土層がありません。");
                return result;
            }
            if (inputModel.FoundationBeamInput?.Beams == null || inputModel.FoundationBeamInput.Beams.Count == 0)
            {
                result.Log.Add("[ERROR] 基礎梁が定義されていません。");
                return result;
            }

            // 各杭の矩形寸法 (DX/DY 半値) を事前計算。
            // 優先順位:
            //  (1) 既存 RectLoad に LinkedPileNo が一致するエントリがあれば、その DX/DY を採用 (ユーザー編集を尊重)
            //  (2) なければ SoilPile の GroupPileLoadDia から (一辺=√π·r) で正方形として算出
            // どちらも取得不可の杭はスキップ。
            var halfDx = new Dictionary<int, double>();
            var halfDy = new Dictionary<int, double>();
            var existingByPile = (pgs.RectLoads ?? new System.Collections.ObjectModel.ObservableCollection<RectLoad>())
                .Where(r => r.LinkedPileNo > 0)
                .GroupBy(r => r.LinkedPileNo)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var pile in piles)
            {
                if (existingByPile.TryGetValue(pile.PileNo, out var existing)
                    && existing.DX > 0 && existing.DY > 0)
                {
                    halfDx[pile.PileNo] = existing.DX * 0.5;
                    halfDy[pile.PileNo] = existing.DY * 0.5;
                    continue;
                }
                int sIdx = pile.SoilPileAltNo - 1;
                if (sIdx < 0 || sIdx >= soilPiles.Count) continue;
                double radius = soilPiles[sIdx].GroupPileLoadDia * 0.5;
                if (radius <= 0)
                {
                    result.Log.Add($"[WARN] 杭No.{pile.PileNo}: 矩形寸法が取得できません (RectLoad と GroupPileLoadDia の両方が未設定)。スキップ");
                    continue;
                }
                double side = Math.Sqrt(Math.PI) * radius;
                halfDx[pile.PileNo] = side * 0.5;
                halfDy[pile.PileNo] = side * 0.5;
            }
            if (halfDx.Count == 0)
            {
                result.Log.Add("[ERROR] 全杭で矩形寸法が取得できません。反復解析タブの「リセット」ボタンで個別矩形を生成してください。");
                return result;
            }

            // 初期 Pi = Ppi (柱軸力)。Ppi に存在しない杭は 0 として扱う。
            var Pi = piles.ToDictionary(p => p.PileNo,
                                       p => initialPpi.TryGetValue(p.PileNo, out double v) ? v : 0.0);
            var S1 = new Dictionary<int, double>();
            var S2 = new Dictionary<int, double>();
            var ki = new Dictionary<int, double>();

            // 安全策: VerticalBeamModelling は pile.No を ConnectionNodes/PileSpringMap のキーに使うため、
            // 重複や 0 があると全杭が同一ノードに縮退してしまう。1〜N に再付番してから構築する。
            int seqNo = 1;
            foreach (var pile in piles)
            {
                pile.No = seqNo++;
            }

            // FEM モデル構築 (SpringCurves 不要 — kz0 は後段で直接書き換える)
            VerticalBeamModelling modelling;
            AnaModel anaModel;
            try
            {
                modelling = new VerticalBeamModelling(inputModel);
                anaModel = modelling.BuildAnaModel();
            }
            catch (Exception ex)
            {
                result.Log.Add($"[ERROR] FEM モデル構築失敗: {ex.Message}");
                return result;
            }

            string header = string.IsNullOrEmpty(caseLabel)
                ? "個別矩形（基礎梁考慮）反復沈下解析"
                : $"個別矩形（基礎梁考慮）反復沈下解析 [{caseLabel}]";
            result.Log.Add(header);
            result.Log.Add($"  杭本数 = {piles.Count}, 基礎梁本数 = {inputModel.FoundationBeamInput.Beams.Count}");
            result.Log.Add($"  最大反復 = {maxIter}, 収束許容 = {tol:E2}, ばね剛性レンジ = [{kMin:E2}, {kMax:E2}] kN/m");
            double sumPpiInit = Pi.Values.Sum();
            result.Log.Add($"  Σ Ppi = {sumPpiInit:N1} kN");
            if (Math.Abs(sumPpiInit) < 1e-9)
            {
                result.Log.Add($"  [WARN] このケースは柱荷重 Σ Ppi = 0 です。");
                result.Log.Add($"         結果はすべて 0 になりますが、計算は完了扱いとします。");
            }
            result.Log.Add("");

            int iter = 0;
            double residual = double.PositiveInfinity;

            for (iter = 1; iter <= maxIter; iter++)
            {
                // 1. 矩形荷重 (Pi を QA とする) を生成
                var rectLoads = new ObservableCollection<RectLoad>();
                foreach (var pile in piles)
                {
                    if (!halfDx.TryGetValue(pile.PileNo, out double hx)) continue;
                    if (!halfDy.TryGetValue(pile.PileNo, out double hy)) continue;
                    rectLoads.Add(new RectLoad
                    {
                        X1 = pile.Point3D.X - hx,
                        X2 = pile.Point3D.X + hx,
                        Y1 = pile.Point3D.Y - hy,
                        Y2 = pile.Point3D.Y + hy,
                        QA = Pi[pile.PileNo],
                        LinkedPileNo = pile.PileNo
                    });
                }

                // 2. Steinbrenner で各杭位置の沈下 S1
                S1.Clear();
                foreach (var pile in piles)
                {
                    if (!halfDx.ContainsKey(pile.PileNo)) continue;
                    var pt = new Point(pile.Point3D.X, pile.Point3D.Y);
                    S1[pile.PileNo] = Steinnbrener.CalcSettlement(pt, rectLoads, pgs.SettlementSoilLayers);
                }

                // 3. ki = Pi / S1 (零除算・負ばね対策のクランプ)
                ki.Clear();
                foreach (var pile in piles)
                {
                    if (!S1.TryGetValue(pile.PileNo, out double si)) continue;
                    double k;
                    if (si > 1e-9 && Pi[pile.PileNo] > 0)
                        k = Pi[pile.PileNo] / si;
                    else
                        k = kMax;
                    k = Math.Max(kMin, Math.Min(kMax, k));
                    ki[pile.PileNo] = k;
                }

                // 4. 線形ばね基礎梁モデルで Ppi を作用 → S2
                anaModel.InitializeStates();

                // ばね K を ki で上書き
                foreach (var pile in piles)
                {
                    if (!ki.TryGetValue(pile.PileNo, out double k)) continue;
                    // modelling は pile.No (InputNode 由来 runtime 番号) で辞書化されている
                    if (modelling.PileSpringMap.TryGetValue(pile.No, out var spring))
                    {
                        spring.SetKe(0, 0, k, 0, 0, 0, true);   // KeTan
                        spring.SetKe(0, 0, k, 0, 0, 0, false);  // KeSec
                    }
                }

                // 杭頭節点に Ppi を集中荷重として作用 (下向き = -Z)。
                // SolveDisp は VectorR (= VectorR + VectorDF を SetR で取得) を右辺として解くため、
                // IncrementalLoad と CumulativedLoad の両方を -pp でセットする必要がある。
                // (InitializeStates は CumulativedLoad/IncrementalLoad をクリアしないが、
                //  SetCumulativeLoad/SetIncrementalLoad は上書きセットなので反復で重複加算しない)
                foreach (var pile in piles)
                {
                    if (!modelling.ConnectionNodes.TryGetValue(pile.No, out var connNode)) continue;
                    double pp = initialPpi.TryGetValue(pile.PileNo, out double v) ? v : 0.0;
                    var loadVec = new NodeLoad(0, 0, -pp, 0, 0, 0);
                    connNode.SetIncrementalLoad(loadVec);
                    connNode.SetCumulativeLoad(loadVec);
                }

                anaModel.MapOnVectorDF();   // VectorDF = -pp from IncrementalLoad
                anaModel.MapOnVectorF();    // VectorF = -pp from CumulativedLoad

                // 残差ベクトル R = F - F_int を計算 (SolveDisp は K · Δu = R を解く)
                anaModel.InitializeNormsqR_onNormsqFint();
                anaModel.SetR();

                // 線形ソルブ
                anaModel.MapOnKtanMat();
                Solver.SolveDisp(anaModel, 1.0);

                // 梁・ばねの内部力 (節点変位から再計算) を更新
                foreach (var beam in anaModel.Beams)
                    beam.SetBeamDispAndForce();
                foreach (var spring in anaModel.HorizontalSoilSprings)
                    spring.SetBeamDispAndForce();

                // 5. 各杭頭の S2 を取得 (下向き正、modelling は pile.No キー)
                S2.Clear();
                foreach (var pile in piles)
                {
                    if (!modelling.ConnectionNodes.TryGetValue(pile.No, out var connNode)) continue;
                    S2[pile.PileNo] = -connNode.CumulativeDisp.Uz;
                }

                // 6. 収束判定 (max|ΔS| / max|S|)
                double maxS1 = S1.Values.DefaultIfEmpty(0).Max(s => Math.Abs(s));
                double maxS2 = S2.Values.DefaultIfEmpty(0).Max(s => Math.Abs(s));
                double maxAbs = Math.Max(maxS1, maxS2);
                double maxDiff = 0;
                foreach (var pile in piles)
                {
                    if (!S1.ContainsKey(pile.PileNo) || !S2.ContainsKey(pile.PileNo)) continue;
                    double d = Math.Abs(S2[pile.PileNo] - S1[pile.PileNo]);
                    if (d > maxDiff) maxDiff = d;
                }
                residual = maxAbs > 1e-12 ? maxDiff / maxAbs : 0.0;

                double avgS1mm = S1.Values.DefaultIfEmpty(0).Average() * 1000.0;
                double avgS2mm = S2.Values.DefaultIfEmpty(0).Average() * 1000.0;
                result.Log.Add(
                    $"  iter {iter,3}: avg S1 = {avgS1mm,8:F3} mm,  avg S2 = {avgS2mm,8:F3} mm,  " +
                    $"max ΔS = {maxDiff * 1000.0,8:F4} mm,  residual = {residual:E3}");

                if (residual < tol)
                {
                    result.Converged = true;
                    break;
                }

                // 次ステップの Pi = ki · S2
                foreach (var pile in piles)
                {
                    if (ki.TryGetValue(pile.PileNo, out double k) && S2.TryGetValue(pile.PileNo, out double s2))
                        Pi[pile.PileNo] = k * s2;
                }
            }

            result.IterationCount = Math.Min(iter, maxIter);
            result.FinalResidual = residual;
            result.PileReactions = new Dictionary<int, double>(Pi);
            result.SpringStiffness = new Dictionary<int, double>(ki);
            result.SteinbrennerSettlement = new Dictionary<int, double>(S1);
            result.BeamSettlement = new Dictionary<int, double>(S2);

            // 節点変位 (GroundNode-* は除外)
            foreach (var node in anaModel.Nodes)
            {
                if (node.Name.StartsWith("GroundNode-")) continue;
                result.NodeResults.Add(new FEM.VerticalBeamNodeResult(
                    node.Name, node.Coord.X, node.Coord.Y, node.Coord.Z,
                    node.CumulativeDisp.Uz * 1000.0,
                    node.CumulativeDisp.Rx,
                    node.CumulativeDisp.Ry));
            }

            // 梁断面力
            foreach (var beam in anaModel.Beams)
            {
                result.BeamResults.Add(new FEM.VerticalBeamBeamResult(beam.Name, beam.CumulativeForce));
            }

            // 収束後 RectLoads (確定 Pi=QA、Linked付き) を返す
            foreach (var pile in piles)
            {
                if (!halfDx.TryGetValue(pile.PileNo, out double hx)) continue;
                if (!halfDy.TryGetValue(pile.PileNo, out double hy)) continue;
                result.ConvergedRectLoads.Add(new RectLoad
                {
                    X1 = pile.Point3D.X - hx,
                    X2 = pile.Point3D.X + hx,
                    Y1 = pile.Point3D.Y - hy,
                    Y2 = pile.Point3D.Y + hy,
                    QA = Pi.TryGetValue(pile.PileNo, out double qa) ? qa : 0.0,
                    LinkedPileNo = pile.PileNo
                });
            }

            result.Log.Add("");
            if (result.Converged)
                result.Log.Add($"--> 反復 {result.IterationCount} 回で収束 (残差 = {residual:E3} < {tol:E2})");
            else
                result.Log.Add($"--> 最大反復 {maxIter} に達したが未収束 (残差 = {residual:E3})");

            double sumPi = Pi.Values.Sum();
            double sumPpi = initialPpi.Values.Sum();
            result.Log.Add($"    平衡確認: Σ Pi (杭反力) = {sumPi:N1} kN, Σ Ppi (柱荷重) = {sumPpi:N1} kN, 差 = {sumPi - sumPpi:N1} kN");

            return result;
        }
    }
}
