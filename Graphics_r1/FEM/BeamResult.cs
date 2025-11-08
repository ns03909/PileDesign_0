using PileDesign.Models.InputData;

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
                CumulativeForce = this.CumulativeForce?.Clone()
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


