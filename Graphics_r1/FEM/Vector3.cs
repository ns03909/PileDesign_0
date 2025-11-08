namespace PileDesign.FEM
{
    public class Vector3S
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public Vector3S() { }
        public Vector3S(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
    }
}
