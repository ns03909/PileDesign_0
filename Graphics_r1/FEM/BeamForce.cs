using MathNet.Numerics.LinearAlgebra;
using System;

namespace PileDesign.FEM
{
    public class BeamForce(
        double fxi, double fyi, double fzi, double mxi, double myi, double mzi,
        double fxj, double fyj, double fzj, double mxj, double myj, double mzj)
    {
        public double Fxi { get; set; } = fxi;
        public double Fyi { get; set; } = fyi;
        public double Fzi { get; set; } = fzi;
        public double Mxi { get; set; } = mxi;
        public double Myi { get; set; } = myi;
        public double Mzi { get; set; } = mzi;
        public double Fxj { get; set; } = fxj;
        public double Fyj { get; set; } = fyj;
        public double Fzj { get; set; } = fzj;
        public double Mxj { get; set; } = mxj;
        public double Myj { get; set; } = myj;
        public double Mzj { get; set; } = mzj;

        public double Fi => Math.Sqrt(Fyi * Fyi + Fzi * Fzi);
        public double Mi => Math.Sqrt(Myi * Myi + Mzi * Mzi);
        public double Fj => Math.Sqrt(Fyj * Fyj + Fzj * Fzj);
        public double Mj => Math.Sqrt(Myj * Myj + Mzj * Mzj);

        public double FabsMax => Math.Max(Fi, Fj);
        public double MabsMax => Math.Max(Mi, Mj);

        public Vector<double> GetVector()
        {
            // より簡潔かつ効率的に6成分を配列でまとめて生成
            return Vector<double>.Build.DenseOfArray([Fxi, Fyi, Fyi, Mxi, Myi, Mzi, Fxj, Fyj, Fzj, Mxj, Myj, Mzj]);
        }

        public void SetVector(Vector<double> vector)
        {
            if (vector.Count != 12)
                throw new ArgumentException("Vector must have exactly 12 elements.", nameof(vector));
            Fxi = vector[0];
            Fyi = vector[1];
            Fzi = vector[2];
            Mxi = vector[3];
            Myi = vector[4];
            Mzi = vector[5];
            Fxj = vector[6];
            Fyj = vector[7];
            Fzj = vector[8];
            Mxj = vector[9];
            Myj = vector[10];
            Mzj = vector[11];
        }

        public void SetByIndex(int index, double value)
        {
            switch (index)
            {
                case 0: Fxi = value; break;
                case 1: Fyi = value; break;
                case 2: Fzi = value; break;
                case 3: Mxi = value; break;
                case 4: Myi = value; break;
                case 5: Mzi = value; break;
                case 6: Fxj = value; break;
                case 7: Fyj = value; break;
                case 8: Fzj = value; break;
                case 9: Mxj = value; break;
                case 10: Myj = value; break;
                case 11: Mzj = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public double GetByIndex(int index) => index switch
        {
            0 => Fxi,
            1 => Fyi,
            2 => Fzi,
            3 => Mxi,
            4 => Myi,
            5 => Mzi,
            6 => Fxj,
            7 => Fyj,
            8 => Fzj,
            9 => Mxj,
            10 => Myj,
            11 => Mzj,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public static BeamForce operator +(BeamForce beamforce1, BeamForce beamforce2)
        {
            return new BeamForce(
                beamforce1.Fxi + beamforce2.Fxi,
                beamforce1.Fyi + beamforce2.Fyi,
                beamforce1.Fzi + beamforce2.Fzi,
                beamforce1.Mxi + beamforce2.Mxi,
                beamforce1.Myi + beamforce2.Myi,
                beamforce1.Mzi + beamforce2.Mzi,
                beamforce1.Fxj + beamforce2.Fxj,
                beamforce1.Fyj + beamforce2.Fyj,
                beamforce1.Fzj + beamforce2.Fzj,
                beamforce1.Mxj + beamforce2.Mxj,
                beamforce1.Myj + beamforce2.Myj,
                beamforce1.Mzj + beamforce2.Mzj
            );
        }

        public BeamForce Clone()
        {
            return new BeamForce(Fxi, Fyi, Fzi, Mxi, Myi, Mzi, Fxj, Fyj, Fzj, Mxj, Myj, Mzj);
        }

        public double[] GetAbsMoment()
        {
            return [Mi, Mj];
        }

        public double[] GetAbsShear()
        {
            return [Fi, Fj];
        }
    }
}
