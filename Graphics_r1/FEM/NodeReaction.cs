using System;

namespace PileDesign.FEM
{
    // 節点荷重
    public class NodeReaction(double fx, double fy, double fz, double mx, double my, double mz)
    {
        public double Fx { get; set; } = fx;
        public double Fy { get; set; } = fy;
        public double Fz { get; set; } = fz;
        public double Mx { get; set; } = mx;
        public double My { get; set; } = my;
        public double Mz { get; set; } = mz;

        /// <summary>
        /// インデックスで成分を取得する
        /// 0:Fx, 1:Fy, 2:Fz, 3:Mx, 4:My, 5:Mz
        /// </summary>
        public double GetByIndex(int index) => index switch
        {
            0 => Fx,
            1 => Fy,
            2 => Fz,
            3 => Mx,
            4 => My,
            5 => Mz,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public void SetByIndex(int index, double value)
        {
            if (index == 0) Fx = value;
            else if (index == 1) Fy = value;
            else if (index == 2) Fz = value;
            else if (index == 3) Mx = value;
            else if (index == 4) My = value;
            else if (index == 5) Mz = value;
            else throw new ArgumentOutOfRangeException(nameof(index));
        }

        public static NodeReaction operator -(NodeReaction load1, NodeReaction load2)
        {
            return new NodeReaction(
                load1.Fx - load2.Fx,
                load1.Fy - load2.Fy,
                load1.Fz - load2.Fz,
                load1.Mx - load2.Mx,
                load1.My - load2.My,
                load1.Mz - load2.Mz
            );
        }

        public static NodeReaction operator +(NodeReaction load1, NodeReaction load2)
        {
            return new NodeReaction(
                load1.Fx + load2.Fx,
                load1.Fy + load2.Fy,
                load1.Fz + load2.Fz,
                load1.Mx + load2.Mx,
                load1.My + load2.My,
                load1.Mz + load2.Mz
            );
        }

        public NodeReaction Clone()
        {
            return new NodeReaction(Fx, Fy, Fz, Mx, My, Mz);
        }
    }
}
