using MathNet.Numerics.LinearAlgebra;
using System;

namespace PileDesign.FEM
{
    public class NodeDisp(double ux, double uy, double uz, double rx, double ry, double rz)
    {
        public double Ux { get; set; } = ux;
        public double Uy { get; set; } = uy;
        public double Uz { get; set; } = uz;
        public double Rx { get; set; } = rx;
        public double Ry { get; set; } = ry;
        public double Rz { get; set; } = rz;

        public double Uh => Math.Sqrt(Ux * Ux + Uy * Uy);
        public double Th => Math.Sqrt(Rx * Rx + Ry * Ry);

        public double GetByIndex(int index) => index switch
        {
            0 => Ux,
            1 => Uy,
            2 => Uz,
            3 => Rx,
            4 => Ry,
            5 => Rz,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public void SetByIndex(int index, double value)
        {
            if (index == 0) Ux = value;
            else if (index == 1) Uy = value;
            else if (index == 2) Uz = value;
            else if (index == 3) Rx = value;
            else if (index == 4) Ry = value;
            else if (index == 5) Rz = value;
            else throw new ArgumentOutOfRangeException(nameof(index));
        }

        public Vector<double> GetVector()
        {
            // より簡潔かつ効率的に6成分を配列でまとめて生成
            return Vector<double>.Build.DenseOfArray([Ux, Uy, Uz, Rx, Ry, Rz]);
        }

        public void SetVector(Vector<double> vector)
        {
            if (vector.Count != 6)
                throw new ArgumentException("Vector must have exactly 6 elements.", nameof(vector));
            Ux = vector[0];
            Uy = vector[1];
            Uz = vector[2];
            Rx = vector[3];
            Ry = vector[4];
            Rz = vector[5];
        }

        public static NodeDisp operator +(NodeDisp disp1, NodeDisp disp2)
        {
            return new NodeDisp(
                disp1.Ux + disp2.Ux,
                disp1.Uy + disp2.Uy,
                disp1.Uz + disp2.Uz,
                disp1.Rx + disp2.Rx,
                disp1.Ry + disp2.Ry,
                disp1.Rz + disp2.Rz
            );
        }

        public static NodeDisp operator -(NodeDisp disp1, NodeDisp disp2)
        {
            return new NodeDisp(
                disp1.Ux - disp2.Ux,
                disp1.Uy - disp2.Uy,
                disp1.Uz - disp2.Uz,
                disp1.Rx - disp2.Rx,
                disp1.Ry - disp2.Ry,
                disp1.Rz - disp2.Rz
            );
        }

        public NodeDisp Clone()
        {
            return new NodeDisp(Ux, Uy, Uz, Rx, Ry, Rz);
        }
    }
}
