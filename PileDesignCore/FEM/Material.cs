namespace PileDesignCore
{
    internal class Material
    {
        public double E { get; }
        public double G { get; }
        public double P { get; }

        public Material(double e, double g, double p)
        {
            E = e;
            G = g;
            P = p;
        }
    }
}