using PileDesign.Models.InputData;

namespace PileDesign.FEM
{
    public class DummyBeamResult(LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step)
    {
        public LoadCase LoadCase { get; } = loadCase;
        public LoadCombination LoadCombination { get; } = loadCombination;
        public bool IsLiquefaction { get; } = isLiquefaction;
        public int Step { get; } = step;

        public DummyBeamResult DeepCopy()
        {
            return new DummyBeamResult(
                this.LoadCase.DeepCopy(),
                this.LoadCombination.DeepCopy(),
                this.IsLiquefaction,
                this.Step
            );
        }
    }
}
