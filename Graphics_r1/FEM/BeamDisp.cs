using MathNet.Numerics.LinearAlgebra;
using System;

namespace PileDesign.FEM
{
    public class BeamDisp(
        double dxi, double dyi, double dzi, double rxi, double ryi, double rzi,
        double dxj, double dyj, double dzj, double rxj, double ryj, double rzj)
    {
        public double Dxi { get; set; } = dxi;
        public double Dyi { get; set; } = dyi;
        public double Dzi { get; set; } = dzi;
        public double Rxi { get; set; } = rxi;
        public double Ryi { get; set; } = ryi;
        public double Rzi { get; set; } = rzi;
        public double Dxj { get; set; } = dxj;
        public double Dyj { get; set; } = dyj;
        public double Dzj { get; set; } = dzj;
        public double Rxj { get; set; } = rxj;
        public double Ryj { get; set; } = ryj;
        public double Rzj { get; set; } = rzj;

        // 以下は他の値から求める読み出し専用の値。保存しない (2026-10-24)。以前は書き出していて、
        // 解析結果入りのファイルの 18% (689 MB 中 127 MB) を占めていた。読むときは使っていなかった
        [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore] public double Di => PileDesign.Common.StableNumerics.Norm(Dyi, Dzi);
        [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore] public double Ri => PileDesign.Common.StableNumerics.Norm(Ryi, Rzi);
        [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore] public double Dj => PileDesign.Common.StableNumerics.Norm(Dyj, Dzj);
        [System.Text.Json.Serialization.JsonIgnore, Newtonsoft.Json.JsonIgnore] public double Rj => PileDesign.Common.StableNumerics.Norm(Ryj, Rzj);

        public Vector<double> GetVector()
        {
            return Vector<double>.Build.DenseOfArray([Dxi, Dyi, Dzi, Rxi, Ryi, Rzi, Dxj, Dyj, Dzj, Rxj, Ryj, Rzj]);
        }

        public void SetVector(Vector<double> vector)
        {
            if (vector.Count != 12)
                throw new ArgumentException("Vector must have exactly 12 elements.", nameof(vector));
            Dxi = vector[0];
            Dyi = vector[1];
            Dzi = vector[2];
            Rxi = vector[3];
            Ryi = vector[4];
            Rzi = vector[5];
            Dxj = vector[6];
            Dyj = vector[7];
            Dzj = vector[8];
            Rxj = vector[9];
            Ryj = vector[10];
            Rzj = vector[11];
        }

        public void SetByIndex(int index, double value)
        {
            switch (index)
            {
                case 0: Dxi = value; break;
                case 1: Dyi = value; break;
                case 2: Dzi = value; break;
                case 3: Rxi = value; break;
                case 4: Ryi = value; break;
                case 5: Rzi = value; break;
                case 6: Dxj = value; break;
                case 7: Dyj = value; break;
                case 8: Dzj = value; break;
                case 9: Rxj = value; break;
                case 10: Ryj = value; break;
                case 11: Rzj = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public double GetByIndex(int index) => index switch
        {
            0 => Dxi,
            1 => Dyi,
            2 => Dzi,
            3 => Rxi,
            4 => Ryi,
            5 => Rzi,
            6 => Dxj,
            7 => Dyj,
            8 => Dzj,
            9 => Rxj,
            10 => Ryj,
            11 => Rzj,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public static BeamDisp operator +(BeamDisp beamDisp1, BeamDisp beamDisp2)
        {
            return new BeamDisp(
                beamDisp1.Dxi + beamDisp2.Dxi,
                beamDisp1.Dyi + beamDisp2.Dyi,
                beamDisp1.Dzi + beamDisp2.Dzi,
                beamDisp1.Rxi + beamDisp2.Rxi,
                beamDisp1.Ryi + beamDisp2.Ryi,
                beamDisp1.Rzi + beamDisp2.Rzi,
                beamDisp1.Dxj + beamDisp2.Dxj,
                beamDisp1.Dyj + beamDisp2.Dyj,
                beamDisp1.Dzj + beamDisp2.Dzj,
                beamDisp1.Rxj + beamDisp2.Rxj,
                beamDisp1.Ryj + beamDisp2.Ryj,
                beamDisp1.Rzj + beamDisp2.Rzj
            );
        }


        public BeamDisp Clone()
        {
            return new BeamDisp(Dxi, Dyi, Dzi, Rxi, Ryi, Rzi, Dxj, Dyj, Dzj, Rxj, Ryj, Rzj);
        }
    }
}

