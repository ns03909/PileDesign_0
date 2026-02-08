using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.FEM
{
    public class BeamResult
    {
        public LoadCase LoadCase { get; set; }
        public LoadCombination LoadCombination { get; set; }
        public bool IsLiquefaction { get; set; }
        public int Step { get; set; }
        public BeamDisp CumulativeDisp { get; set; }
        public BeamForce CumulativeForce { get; set; }

        // 要素中央の曲率 [rad/m]
        public double Curvature { get; set; }

        // 要素中央のモーメント [kNm]（M-φ曲線から直接評価した値）
        public double Moment { get; set; }

        // M-φ曲線データ（グラフ表示用）
        public List<double> MPhiCurve_Phis { get; set; }
        public List<double> MPhiCurve_Moments { get; set; }

        // パラメータなしコンストラクタ（必須）
        public BeamResult() { }

        // 生成用コンストラクタ
        public BeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, Beam beam)
        {
            LoadCase = loadCase;
            LoadCombination = loadCombination;
            IsLiquefaction = isLiquefaction;
            Step = step;
            CumulativeDisp = beam.CumulativeDisp?.Clone();
            CumulativeForce = beam.CumulativeForce?.Clone();
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
                LoadCase = this.LoadCase?.DeepCopy(),
                LoadCombination = this.LoadCombination?.DeepCopy(),
                IsLiquefaction = this.IsLiquefaction,
                Step = this.Step,
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                Curvature = this.Curvature,
                Moment = this.Moment,
                MPhiCurve_Phis = this.MPhiCurve_Phis?.ToList(),
                MPhiCurve_Moments = this.MPhiCurve_Moments?.ToList()
            };
            return copy;
        }
    }
    //public class BeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, Beam beam)
    //{
    //    public LoadCase LoadCase { get; } = loadCase;
    //    public LoadCombination LoadCombination { get; } = loadCombination;
    //    public bool IsLiquefaction { get; } = isLiquefaction;
    //    public int Step { get; } = step;
    //    public BeamDisp CumulativeDisp { get; } = beam.CumulativeDisp.Clone();
    //    public BeamForce CumulativeForce { get; } = beam.CumulativeForce.Clone();

    //    public BeamResult DeepCopy()
    //    {
    //        // ダミーNodeを生成
    //        var dummyNodeI = new Node();
    //        dummyNodeI.SetNodeInfo("dummyI", 0, 0, 0);
    //        var dummyNodeJ = new Node();
    //        dummyNodeJ.SetNodeInfo("dummyJ", 0, 0, 0);

    //        // ダミーSectionも必要なら生成
    //        var dummySection = new Section(
    //            new Material(20000, 0.2), // 必要に応じて適切なMaterialを渡す
    //            0, 0, 0, 0, 0, 0
    //        );

    //        var dummyBeam = new Beam("dummy", dummySection, dummyNodeI, dummyNodeJ, 0, 0)
    //        {
    //            CumulativeDisp = this.CumulativeDisp.Clone(),
    //            CumulativeForce = this.CumulativeForce.Clone()
    //        };
    //        return new BeamResult(
    //            this.LoadCase.DeepCopy(),
    //            this.LoadCombination.DeepCopy(),
    //            this.IsLiquefaction,
    //            this.Step,
    //            dummyBeam
    //        );
    //    }
    //}
}


