using System;
using System.Collections.ObjectModel;
using System.Windows;
using PileDesign.Services;
//using System.Windows.Forms;

namespace PileDesign.Models
{
    abstract public class Stiffness
    {
        public abstract double GetTangentStiffness(double displacement);
    }

    public class PolyLineStiffness : Stiffness
    {
        ObservableCollection<double> Displacements { get; set; }
        ObservableCollection<double> Forces { get; set; }
        ObservableCollection<double> TangentStiffnesses { get; set; }

        // 力と変位からの入力
        public void SetForcesDisplacements(ObservableCollection<double> forces, ObservableCollection<double> displacements, double k0, double kn)
        {
            if (forces.Count != displacements.Count)
            {
                MessageService.Show("荷重の個数と変位の個数が一致していません。\n同じ数だけ入力してください。", "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Forces = forces;
            Displacements = displacements;

            ObservableCollection<double> TangentStiffnesses = [k0];

            for (int i = 0; i < Forces.Count - 1; i++)
            {
                TangentStiffnesses.Add((Forces[i + 1] - Forces[i]) / (Displacements[i + 1] - Displacements[i]));
            }

            TangentStiffnesses.Add(kn);
        }

        // 接線剛性と変位からの入力
        public void SetStiffnessDisplacements(ObservableCollection<double> tangentStiffnesses, ObservableCollection<double> displacements)
        {
            if (tangentStiffnesses.Count != displacements.Count + 1)
            {
                MessageService.Show("接線剛性の個数が、変位の個数 + 1 になっていません。\n変位が n 点なら接線剛性は n + 1 点必要です (両端の外側の勾配を含むため)。",
                    "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TangentStiffnesses = tangentStiffnesses;
            Displacements = displacements;
        }

        // 変位に対応する接線剛性の取得メソッド
        public override double GetTangentStiffness(double displacement)
        {
            for (int i = 0; i < Displacements.Count; i++)
            {
                if (displacement < Displacements[i])
                {
                    return TangentStiffnesses[i];
                }
            }
            return TangentStiffnesses[^1];
        }
    }

    public class HolizontalPileSoilStiffness : Stiffness
    {
        double Kh0 { get; set; }
        double Py { get; set; }

        // コンストラクタ
        public HolizontalPileSoilStiffness(double kh0, double py)
        {
            Kh0 = kh0;
            Py = py;
        }

        public override double GetTangentStiffness(double displacement)
        {
            double absDisp = Math.Abs(displacement);
            double sign = Math.Sign(displacement);

            if (absDisp < 0.010)
            {
                return sign * 3.16 * Kh0;
            }
            else if (Kh0 / Math.Sqrt(absDisp / 0.01) * absDisp < Py)
            {
                return sign * Math.Sqrt(0.01) * 0.5 * Kh0 * Math.Pow(absDisp, -0.5);
            }
            else
            {
                return 0.0;
            }
        }

        public double GetForce(double displacement)
        {
            double absDisp = Math.Abs(displacement);
            double sign = Math.Sign(displacement);

            if (absDisp < 0.010)
            {
                return sign * 3.16 * Kh0 * absDisp;
            }
            else if (Kh0 / Math.Sqrt(absDisp / 0.01) * absDisp < Py)
            {
                return sign * Kh0 / Math.Sqrt(absDisp / 0.01) * absDisp;
            }
            else
            {
                return sign * Py;
            }
        }
    }

    // 杭先端
    public class PileTip
    {
        double Alpha { get; set; }
        double N { get; set; }
        double Rpu { get; set; }
        double Ap { get; set; }

        // コンストラクタ
        public PileTip(double ap, double rpu, double alpha, double n)
        {
            Ap = ap;
            Rpu = rpu;
            Alpha = alpha;
            N = n;
        }

        public double GetD(double rp)
        {
            double rpOnRpu = rp / Rpu;
            return 0.1 * (Alpha * rpOnRpu + (1 - Alpha) * Math.Pow(rpOnRpu, N));
        }

        public double GetDDonDR(double rp)
        {
            double rpOnRpu = rp / Rpu;
            return 0.1 * Rpu * (Alpha + N * (1 - Alpha) * Math.Pow(rpOnRpu, N - 1));
        }

        public double GetTangentStiffness(double disp)
        {
            double rp = Rpu;
            double rpNext;
            double ratio = double.MaxValue;

            while (ratio > Math.Pow(10, -8))
            {
                rpNext = rp - (GetD(rp) - disp) / GetDDonDR(rp);
                ratio = rpNext / rp;
                rp = rpNext;
            }
            return 1 / GetDDonDR(rp);
        }

        public double GetForce(double disp)
        {
            double rp = Rpu;
            double rpNext;
            double ratio = double.MaxValue;

            while (ratio > Math.Pow(10, -8))
            {
                rpNext = rp - (GetD(rp) - disp) / GetDDonDR(rp);
                ratio = rpNext / rp;
                rp = rpNext;
            }
            return rp;
        }
    }

    //public class DoatsuGoryokuBane : Stiffness
    //{
    //    double B { get; set; }
    //    double Gamma { get; set; }
    //    double Phi { get; set; }
    //    double C { get; set; }
    //    double Za { get; set; }
    //    double Zb { get; set; }
    //    double DeltaP { get; set; }
    //    double Ysp { get; set; }
    //    double K0 { get; set; }
    //    double Kp { get; set; }
    //    double Q { get; set; } // (kN/m2) 深さZaでの上載圧
    //    double P0 { get; set; } // (kN/m2)
    //    double Pp { get; set; } // (kN/m2)

    //    // コンストラクタ
    //    public DoatsuGoryokuBane(double b, double gamma, double phi, double c, double za, double zb, double deltaP)
    //    {
    //        B = b;
    //        Gamma = gamma;
    //        Phi = phi;
    //        C = c;
    //        Za = za;
    //        Zb = zb;
    //        DeltaP = deltaP;
    //        Ysp = 0.1 * DeltaP;
    //        K0 = 0.5;
    //        Kp = Math.Pow(Math.Tan((45 + Phi * 0.5) * Math.PI / 180), 2);
    //        P0 = (Q + Gamma * (Zb - Za) * 0.5) * K0;
    //        Pp = (Q + Gamma * (Zb - Za) * 0.5) * Kp + 2 * C * Math.Sqrt(Kp);
    //    }

    //    // kN/m2
    //    public double GetForce(double disp)
    //    {
    //        if (disp < DeltaP)
    //        {
    //            return (Pp - P0) * (DeltaP - Ysp) * disp / (2 * DeltaP * Ysp + (DeltaP - 3 * Ysp) * Math.Abs(disp));
    //        }
    //        else
    //        {
    //            return Pp - P0;
    //        }
    //    }

    //    public override double GetTangentStiffness(double disp)
    //    {
    //        double fraction = Math.Pow(10, -6);
    //        if (disp == 0)
    //        {
    //            return GetForce(fraction) / fraction;
    //        }
    //        else if (disp < DeltaP)
    //        {
    //            return (GetForce(disp + fraction) - GetForce(disp - fraction)) / (2 * fraction);
    //        }
    //        else { return 0; }
    //    }
    //}
}
