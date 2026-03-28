using System;

namespace PileDesign.Models.InputData
{
    public class HorizontalSoilReactionItem
    {
        public string SoilType { get; set; }
        public double B { get; set; }
        public double ZTop { get; set; }
        public double ZBtm { get; set; }
        public double Xi { get; set; }
        public double ROnB { get; set; }
        public double Phi { get; set; }
        public double NValue { get; set; }
        public double Cu { get; set; }

        public string Name { get; set; }
        public double SigmaZPrimeTop { get; set; }
        public double SigmaZPrimeBtm { get; set; }

        public double PyFrontTop { get; set; } // 塑性地盤反力
        public double PyFrontBtm { get; set; } // 塑性地盤反力

        public double PyRearTop { get; set; } // 塑性地盤反力
        public double PyRearBtm { get; set; } // 塑性地盤反力

        public double E0 { get; set; }
        public double Kh0 { get; set; } // 基準水平地盤反力係数
        public double Gamma { get; set; }


        // コンストラクタ
        public HorizontalSoilReactionItem()
        { }

        // パラメータセット
        public void SetParameters(
            string name, string soilType, double gamma, double b, double e0,
            double zTop, double zBtm,
            double xi, double rOnB, double nValue, double phi, double cu,
            double sigmaZPrimeTop, double sigmaZPrimeBtm, double alpha = 60)
        {
            Name = name;
            SoilType = soilType;
            Gamma = gamma;
            B = b;
            E0 = e0;
            ZTop = zTop;
            ZBtm = zBtm;
            Xi = xi;
            ROnB = rOnB;
            NValue = nValue;
            Phi = phi;
            Cu = cu;
            SigmaZPrimeTop = sigmaZPrimeTop;
            SigmaZPrimeBtm = sigmaZPrimeBtm;

            //double alpha = 80;
            Kh0 = GetKh0(alpha, xi, e0, b);
            PyFrontTop = GetPy(soilType, true, b, zTop, rOnB, phi, cu, sigmaZPrimeTop);
            PyFrontBtm = GetPy(soilType, true, b, zBtm, rOnB, phi, cu, sigmaZPrimeBtm);
            PyRearTop = GetPy(soilType, false, b, zTop, rOnB, phi, cu, sigmaZPrimeTop);
            PyRearBtm = GetPy(soilType, false, b, zBtm, rOnB, phi, cu, sigmaZPrimeBtm);
        }

        // DeepCopy メソッドの追加
        public HorizontalSoilReactionItem DeepCopy()
        {
            return new HorizontalSoilReactionItem()
            {
                Name = this.Name,
                SoilType = this.SoilType,
                Gamma = this.Gamma,
                B = this.B,
                E0 = this.E0,
                ZTop = this.ZTop,
                ZBtm = this.ZBtm,
                Xi = this.Xi,
                ROnB = this.ROnB,
                NValue = this.NValue,
                Phi = this.Phi,
                Cu = this.Cu,
                SigmaZPrimeTop = this.SigmaZPrimeTop,
                SigmaZPrimeBtm = this.SigmaZPrimeBtm,
                PyFrontTop = this.PyFrontTop,
                PyFrontBtm = this.PyFrontBtm,
                PyRearTop = this.PyRearTop,
                PyRearBtm = this.PyRearBtm,
                Kh0 = this.Kh0
            };
        }

        // 反力を返すメソッド (kN)
        public double GetSoilReaction(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetP(y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        //  接線剛性を返すメソッド (kN/m)
        public double GetSoilTangentReactionCoefficient(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetkhTan(Kh0, y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        public double GetSoilSecantReactionCoefficient(double y, bool isTop, bool isFront)
        {
            double py = isFront ? (isTop ? PyFrontTop : PyFrontBtm) : (isTop ? PyRearTop : PyRearBtm);
            return GetKh(Kh0, y, py) * B * (ZTop - ZBtm) * 0.5;
        }

        // 水平地盤反力係数khを返すメソッド (kN/m3)
        private static double GetKh(double kh0, double y, double py)
        {
            if (py == 0) return 0;

            double y0 = 0.01; // m, 1cm
            if (Math.Abs(y) / y0 <= 0.1)
            {
                return 3.16 * kh0;
            }
            else if (kh0 / Math.Sqrt(Math.Abs(y) / y0) * Math.Abs(y) < py)
            {
                return kh0 / Math.Sqrt(Math.Abs(y) / y0);
            }
            else
            {
                //return py / Math.Abs(y);
                double yy = Math.Pow(py / kh0, 2) / y0;
                double gradient = 0.001 * py / yy; // 降伏時割線剛性の 0.1%
                double p = gradient * (Math.Abs(y) - yy) + py;
                return p / Math.Abs(y);
            }
        }

        // 水平地盤反力の接線剛性を返すメソッド (kN/m3)
        public static double GetkhTan(double kh0, double y, double py)
        {
            double y0 = 0.01; // m, 1cm

            if (py == 0) return 0;

            if (Math.Abs(y) / y0 <= 0.1)
            {
                return 3.16 * kh0;
            }
            else if (kh0 / Math.Sqrt(Math.Abs(y) / y0) * Math.Abs(y) < py)
            {
                return Math.Sqrt(y0) / 2.0 * kh0 / Math.Sqrt(Math.Abs(y));
            }
            else
            {
                double yy = Math.Pow(py / kh0, 2) / y0;
                double gradient = 0.001 * py / yy; // 降伏時割線剛性の 0.1%
                return gradient;
            }
        }

        // 反力pを返すメソッド (kN/m2)
        public double GetP(double y, double py)
        {
            return Math.Min(GetKh(Kh0, y, py) * y, py);
        }

        // 基準水平地盤反力係数kh0を返すメソッド (kN/m3)
        private static double GetKh0(double alpha, double xi, double e0, double b)
        {
            double b0 = 0.01;
            return alpha * xi * e0 * Math.Pow(b / b0, -3.0 / 4.0);
        }

        // 塑性地盤反力pyを返すメソッド (kN/m2)
        public static double GetPy(string soilType, bool isFront, double b, double z, double rOnB, double phi, double cu, double sigmaZPrime)
        {

            if (soilType == "砂質土" || soilType == "礫質土")
            {
                double kappa = GetKappa(isFront, rOnB, phi);
                double Kp = (1 + Math.Sin(phi * Math.PI / 180)) / (1 - Math.Sin(phi * Math.PI / 180));

                return kappa * Kp * sigmaZPrime;
            }

            else /*(soilType == "粘性土")*/
            {
                (double mu, double lambda) = GetMuLambda(isFront, rOnB);

                if (Math.Abs(z) / b <= 2.5)
                {
                    return 2 * (1 + mu * Math.Abs(z) / b) * cu;
                }
                else
                {
                    return lambda * cu;
                }
            }
        }


        // κを返すメソッド
        private static double GetKappa(bool isFront, double rOnB, double phi)
        {
            if (isFront) // 前方杭
            {
                return 3.0;
            }
            else // 後方杭
            {
                return Math.Min((0.55 - 0.007 * phi) * (rOnB - 1.0) + 0.4, 3);
            }
        }

        // µ、λを返すメソッド
        private static (double, double) GetMuLambda(bool isFront, double rOnB)
        {
            if (isFront) // 前方杭
            {
                return (1.4, 9.0);
            }
            else // 後方杭
            {
                if (rOnB >= 3.0)
                {
                    return (1.4, 9.0);
                }
                else
                {
                    return (0.6 * rOnB - 0.4, 3.0);
                }
            }
        }
    }
}
