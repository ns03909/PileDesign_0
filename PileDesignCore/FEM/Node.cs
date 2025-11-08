using System;

namespace PileDesignCore
{
    internal class Node
    {
        public string Name { get; }
        public Coord Coord { get; }
        public Boundary Boundary { get; set; }
        public int[] EquationNumber { get; }
        public double[] Load { get; }
        public OutNode OutNode { get; set; }

        public Node(string name, double coordX, double coordY, double coordZ)
        {
            Name = name;
            Coord = new Coord(coordX, coordY, coordZ);
            Boundary = new Boundary(BoundaryType.Free, BoundaryType.Free, BoundaryType.Free, BoundaryType.Free, BoundaryType.Free, BoundaryType.Free);
            EquationNumber = new int[6];
            Load = new double[6];
        }

        public void SetBoundary(Boundary boundary)
        {
            Boundary = boundary;
        }

        public void SetEquationNumber(int index, int equationNumber)
        {
            EquationNumber[index] = equationNumber;
        }

        public void SetLoad(LoadNode load)
        {
            Load[0] = load.LoadUX;
            Load[1] = load.LoadUY;
            Load[2] = load.LoadUZ;
            Load[3] = load.LoadTX;
            Load[4] = load.LoadTY;
            Load[5] = load.LoadTZ;
        }

        public BoundaryType GetBoundary(int index)
        {
            switch (index)
            {
                case 0:
                    return Boundary.Ux;
                case 1:
                    return Boundary.Uy;
                case 2:
                    return Boundary.Uz;
                case 3:
                    return Boundary.Tx;
                case 4:
                    return Boundary.Ty;
                case 5:
                    return Boundary.Tz;
                default:
                    throw new Exception("不正なインデックス");
            }
        }

        public void MapOnGlobalLoad(double[] loadVector)
        {
            for (int index = 0; index < 6; index++)
            {
                int equationNumber = EquationNumber[index];
                if (equationNumber >= 0)
                {
                    loadVector[equationNumber] += Load[index];
                }
            }
        }

        public void SetOutNode(OutNode outNode)
        {
            OutNode = outNode;
        }
    }
}