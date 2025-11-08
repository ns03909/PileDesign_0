namespace PileDesignCore
{
    internal class Section
    {
        public Material Material { get; }
        public double AX { get; }
        public double AY { get; }
        public double AZ { get; }
        public double IX { get; }
        public double IY { get; }
        public double IZ { get; }

        public Section(Material material, double ax, double ay, double az, double ix, double iy, double iz)
        {
            Material = material;
            AX = ax;
            AY = ay;
            AZ = az;
            IX = ix;
            IY = iy;
            IZ = iz;
        }
    }
}