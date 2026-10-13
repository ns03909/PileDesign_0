using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    public class BeamResult : ICaseStepResult
    {
        public LoadCase LoadCase { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public LoadCombination LoadCombination { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }
        public BeamDisp CumulativeDisp { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る
        public BeamForce CumulativeForce { get; set; } = null!; // 読込 (JSON) と生成用コンストラクタで入る

        // 要素中央の曲率 [rad/m]
        public double Curvature { get; set; }

        // 要素中央のモーメント [kNm]（M-φ曲線から直接評価した値）
        public double Moment { get; set; }

        // M-φ曲線データ（グラフ表示用）。曲線が無い (点が 2 つ未満) ときは null
        public List<double>? MPhiCurve_Phis { get; set; }
        public List<double>? MPhiCurve_Moments { get; set; }

        // パラメータなしコンストラクタ（必須）
        public BeamResult() { }

        // 生成用コンストラクタ
        public BeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, Beam beam)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            CumulativeDisp = beam.CumulativeDisp?.Clone()!;
            CumulativeForce = beam.CumulativeForce?.Clone()!;
            Curvature = beam.CurrentCurvature;
            Moment = beam.CurrentMoment;

            // M-φ曲線データを保存（解析時に解決済みの曲線から）
            var curve = beam.ResolvedCombinedCurve;
            if (curve?.Points != null && curve.Points.Count >= 2)
            {
                MPhiCurve_Phis = curve.Points.Select(p => p.Phi).ToList();
                MPhiCurve_Moments = curve.Points.Select(p => p.Moment).ToList();
            }
        }

        public BeamResult DeepCopy()
        {
            var copy = new BeamResult()
            {
                LoadCase = this.LoadCase?.DeepCopy()!,
                LoadCombination = this.LoadCombination?.DeepCopy()!,
                IsLiquefaction = this.IsLiquefaction,
                Step = this.Step,
                CumulativeDisp = this.CumulativeDisp?.Clone()!,
                CumulativeForce = this.CumulativeForce?.Clone()!,
                Curvature = this.Curvature,
                Moment = this.Moment,
                MPhiCurve_Phis = this.MPhiCurve_Phis?.ToList(),
                MPhiCurve_Moments = this.MPhiCurve_Moments?.ToList()
            };
            return copy;
        }
    }
}


