//namespace Sample
//{
//    internal class Class1
//    {
//    }
//}

namespace PileDesign.FEM
{
    internal class Coord
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Coord(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}