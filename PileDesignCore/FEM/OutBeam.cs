namespace PileDesignCore
{
    internal class OutBeam
    {
        public double[] Stress { get; private set; }

        public void SetStress(double[] stress)
        {
            Stress = stress;
        }
    }
}