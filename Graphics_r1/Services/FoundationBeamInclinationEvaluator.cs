using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>
    /// 群杭沈下解析（基礎梁あり）の結果から、各基礎梁の傾斜角を検定する。
    /// 傾斜角 = |Uz_j − Uz_i| / L を、記録の荷重レベルに応じた限界値 (<see cref="InclinationLimitFor"/>) と比べる。
    ///
    /// <para>以前は水平解析の検定 (<c>EvaluationService</c>) の中にあり、<b>水平解析を済ませないと出ず</b>、
    /// 計算書でも水平解析の章にしか載らなかった。使うのは沈下解析の結果だけなので、沈下による杭頭変形角
    /// (<see cref="SettlementDeformationAngleEvaluator"/>) と同じく沈下の側で組み、同じ場所 (結果のまとめ・
    /// 解析結果テーブル・計算書の沈下の検定) に出す。</para>
    ///
    /// <para>以前は節点の結果が無い記録・座標の無い梁・節点名を引けない梁・沈下量の無い節点を<b>黙って飛ばし</b>、
    /// 残りの梁だけで判定していた。検定の対象なのに判定できない梁は「検定不能」として理由を付けて出す。</para>
    /// </summary>
    public static class FoundationBeamInclinationEvaluator
    {
        /// <summary>検定項目の分類名 (表・計算書で使う)。</summary>
        public const string Category = "基礎梁の傾斜角";

        private const string RerunHint = "群杭沈下ウィンドウから群杭沈下解析（基礎梁あり）をやり直してください";

        /// <summary>
        /// 検定項目を作る。基礎梁ありの群杭沈下の記録が無い、または基礎梁が無ければ空 (対象外)。
        /// </summary>
        public static List<EvaluationItem> Evaluate(InputModel? inputModel)
        {
            var items = new List<EvaluationItem>();

            var records = inputModel?.PileGroupSettlement?.CaseRecords?.Where(r => r != null && r.IsBeamAware).ToList();
            if (records == null || records.Count == 0) return items;

            var beams = inputModel!.FoundationBeamInput?.Beams;
            if (beams == null || beams.Count == 0) return items;

            foreach (var rec in records)
            {
                var (level, limitName, limit) = InclinationLimitFor(rec, inputModel.FundamentalInput);
                string caseName = rec.LoadCaseName ?? "";
                // 反復が収束しなかったケースは判定しない (釣り合っていない沈下の傾斜角なので「未収束」)
                var convergence = rec.IsConverged ? FEM.StepStatus.Converged : FEM.StepStatus.Unconverged;

                EvaluationItem Unavailable(string target, int? beamNo, string reason) => new()
                {
                    Kind = EvaluationKind.FoundationBeamInclination,
                    Level = level,
                    Category = Category,
                    LimitName = limitName,
                    TargetName = target,
                    FoundationBeamNo = beamNo,
                    LoadCaseName = caseName,
                    Response = double.NaN,
                    Limit = limit,
                    Unit = "rad",
                    UnavailableReason = reason,
                };

                if (rec.NodeResults == null || rec.NodeResults.Count == 0)
                {
                    // 以前はこの記録を黙って飛ばしていた (基礎梁の傾斜角の検定が丸ごと出ない)
                    items.Add(Unavailable("基礎梁", null, $"この荷重ケースの記録に節点の沈下量がありません。{RerunHint}"));
                    continue;
                }

                // 同じ節点名が 2 つあっても例外にしない (最初の値を使う)
                var uzByName = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var n in rec.NodeResults)
                    if (n?.NodeName != null) uzByName.TryAdd(n.NodeName, n.Uz_mm);

                foreach (var fbBeam in beams)
                {
                    if (fbBeam == null) continue;
                    int beamNo = inputModel.FoundationBeamInput!.GetBeamNo(fbBeam);
                    string target = $"FoundationBeam-{beamNo}";

                    var coordsI = inputModel.GetNodeCoordinates(fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    var coordsJ = inputModel.GetNodeCoordinates(fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                    if (coordsI == null || coordsJ == null)
                    {
                        items.Add(Unavailable(target, beamNo,
                            $"基礎梁 #{beamNo} の端の節点が見つからず、梁の長さを求められません (基礎梁の画面で節点を確かめてください)"));
                        continue;
                    }

                    double dx = coordsJ.Value.X - coordsI.Value.X;
                    double dy = coordsJ.Value.Y - coordsI.Value.Y;
                    double L = PileDesign.Common.StableNumerics.Norm(dx, dy);
                    if (!double.IsFinite(L) || PileDesign.Common.GeometryTolerance.IsZeroLength(L))
                    {
                        items.Add(Unavailable(target, beamNo,
                            $"基礎梁 #{beamNo} の平面上の長さが 0 です。傾斜角を求められません (基礎梁の画面で節点を確かめてください)"));
                        continue;
                    }

                    string? nameI = FEM.AnalysisModelling.FemNodeNameOf(inputModel, fbBeam.NodeI_Type, fbBeam.NodeI_Id);
                    string? nameJ = FEM.AnalysisModelling.FemNodeNameOf(inputModel, fbBeam.NodeJ_Type, fbBeam.NodeJ_Id);
                    if (string.IsNullOrEmpty(nameI) || string.IsNullOrEmpty(nameJ)
                        || !uzByName.TryGetValue(nameI, out double uzI) || !uzByName.TryGetValue(nameJ, out double uzJ))
                    {
                        items.Add(Unavailable(target, beamNo,
                            $"基礎梁 #{beamNo} の端の節点の沈下量がこの荷重ケースの結果にありません。{RerunHint}"));
                        continue;
                    }
                    if (!double.IsFinite(uzI) || !double.IsFinite(uzJ))
                    {
                        items.Add(Unavailable(target, beamNo,
                            $"基礎梁 #{beamNo} の端の節点の沈下量が数値として求まっていません。{RerunHint}"));
                        continue;
                    }

                    double inclination = Math.Abs((uzJ - uzI) * 0.001 / L);

                    items.Add(new EvaluationItem
                    {
                        Kind = EvaluationKind.FoundationBeamInclination,
                        Level = level,
                        Category = Category,
                        LimitName = limitName,
                        TargetName = target,
                        FoundationBeamNo = beamNo,
                        LoadCaseName = caseName,
                        CaseConvergence = convergence,
                        // 反復沈下解析には液状化の区別が無い (null のまま)
                        Response = inclination,
                        Limit = limit,
                        Unit = "rad",
                        BeamLength = L,
                        // 判定は従来どおり「限界未満なら OK」(ちょうど等しいと NG)
                        IsOk = inclination < limit,
                    });
                }
            }

            return items;
        }

        /// <summary>
        /// 基礎梁の傾斜角の記録のレベルと限界値。常時 (VL) は使用限界、レベル1 (「L1-…」) は損傷限界、
        /// レベル2 (「L2-…」) は安全限界 (基本設定「杭頭 2 点間の変形角の限界値」)。<b>性能グレード S ではレベル2 も損傷限界</b>
        /// (基本設定の説明・水平解析の杭頭変形角と同じ。2026-10-11 まではグレードを見ず、レベル2 を常に安全限界で照査していた)。
        /// 以前は全記録 (地震時も) を固定の 1/300 で、常時として判定していた (出典を確かめられない値だった)。
        /// </summary>
        internal static (int Level, string LimitName, double Limit) InclinationLimitFor(
            GroupSettlementCaseRecord rec, FundamentalInput? fundamental)
        {
            string name = rec.LoadCaseName ?? "";
            if (name.StartsWith("L2-", StringComparison.Ordinal))
                return (fundamental?.SeismicGrade ?? "A") == "S"
                    ? (2, "損傷限界", PileHeadDeformationAngle.DamageLimit(fundamental))
                    : (2, "安全限界", PileHeadDeformationAngle.UltimateLimit(fundamental));
            if (name.StartsWith("L1-", StringComparison.Ordinal))
                return (1, "損傷限界", PileHeadDeformationAngle.DamageLimit(fundamental));
            return (0, "使用限界", PileHeadDeformationAngle.ServiceLimit(fundamental));
        }
    }
}
