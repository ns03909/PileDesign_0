using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class DummyBeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step, DummyBeam dummyBeam)
    {
        public LoadCase LoadCase { get; } = loadCase;
        public LoadCombination LoadCombination { get; } = loadCombination;
        public bool IsLiquefaction { get; } = isLiquefaction;
        public int Step { get; } = step;
        //public BeamDisp CumulativeDisp { get; } = dummyBeam.CumulativeDisp.Clone();

        public DummyBeamResult DeepCopy()
        {
            // dummyBeamはコピー時には不要なのでダミーでOK
            return new DummyBeamResult(
                this.LoadCase.DeepCopy(),
                this.LoadCombination.DeepCopy(),
                this.IsLiquefaction,
                this.Step,
                null // コピー時はnullやダミーでOK
            );
        }
    }
}
