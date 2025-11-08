using System;

namespace PileDesign.Common
{

    internal class Chang
    //  Changの式
    {
        double EI; // 杭の曲げ剛性
        double beta;
        double h; //地上高
        double horizontalLoad; // 水平荷重
        double ar; // 杭頭の固定度

        // コンストラクタ
        public Chang(double _EI, double _beta, double _h, double _horizontalLoad, double _ar)
        {
            EI = _EI;
            beta = _beta;
            h = _h;
            horizontalLoad = _horizontalLoad;
            ar = _ar;
        }

        // たわみ曲線
        public double GetDeflection(double x)
        {
            if (h == 0)
            {
                return horizontalLoad / (4 * EI * beta * beta) * Math.Exp(-beta * x) * ((2 - ar) * Math.Cos(beta * x) + ar * Math.Sin(beta * x));
            }
            else if (x < 0)
            {
                return horizontalLoad / (12 * EI * beta * beta) * (Math.Pow((1 + beta * h), 3) * (4 - 3 * ar) + 2
                    - 6 * beta * (1 + beta * h) * (1 + beta * h) * (1 - ar) * (h + x)
                    - 3 * beta * beta * (1 + beta * h) * ar * (h + x) * (h + x) + 2 * Math.Pow(beta, 3) * Math.Pow(h + x, 3));
            }
            else
            {
                return horizontalLoad / (4 * EI * beta * beta) * Math.Exp(-beta * h) * ((1 + beta * h) * (2 - ar) * Math.Cos(beta * x)
                    - (2 * beta * h - (1 + beta * h) * ar) * Math.Sin(beta * x));
                // たわみ曲線の計算
                // ここでは簡単な例として、たわみ曲線を直線で近似
            }
        }

        // 杭頭変位
        public double GetPileHeadDisplacement(double x)
        {
            if (h == 0)
            {
                return horizontalLoad / (4 * EI * beta * beta) * (2 - ar);
            }

            else
            {
                return horizontalLoad / (12 * EI * beta * beta) * (Math.Pow(1 + beta * h, 3) * (4 - 3 * ar) + 2);
            }
        }

        // 地表面変位
        public double GetGroundSurfaceDisplacement(double x)
        {
            if (h == 0)
            {
                return GetPileHeadDisplacement(x);
            }
            else
            {
                return horizontalLoad / (4 * EI * beta * beta) * (1 + beta * h) * (2 - ar);
            }
        }

        // 杭頭拘束モーメント
        public double GetPileHeadMoment()
        {
            if (h == 0)
            {
                return horizontalLoad / (2 * beta) * ar;
            }
            else
            {
                return horizontalLoad / (2 * beta) * (1 + beta * h) * ar;
            }
        }

        // 杭各部の曲げモーメント
        public double GetBendingMoment(double x)
        {
            if (h == 0)
            {
                return -horizontalLoad / (2 * beta) * Math.Exp(-beta * x) * ((2 - ar) * Math.Sin(beta * x) - ar * Math.Cos(beta * x));
            }
            else if (x < 0)
            {
                return horizontalLoad / (2 * beta) * ((1 + beta * h) * ar - 2 * beta * (h + x));

            }
            else
            {
                return -horizontalLoad / (2 * beta) * Math.Exp(-beta * x) * ((1 + beta * h) * (2 - ar) * Math.Sin(beta * x)
                    + (2 * beta * h - (1 + beta * h) * ar) * Math.Cos(beta * x));
            }
        }
        // 地中部最大曲げモーメント
        public double GetMaxBendingMoment()
        {
            if (h == 0)
            {
                return -horizontalLoad / (2 * beta) * Math.Sqrt((1 - ar) * (1 - ar) + 1) * Math.Exp(-Math.Atan(1 / (1 - ar)));
            }
            else
            {
                return -horizontalLoad / (2 * beta) * Math.Sqrt(Math.Pow(((1 + 2 * beta * h) - (1 + beta * h) * ar), 2) + 1) * Math.Exp(-Math.Atan(1 / (1 + 2 * beta * h - (1 + beta * h) * ar)));
            }
        }
        // Mmを生じる深さ
        public double GetDepthOfMaxBendingMoment()
        {
            if (h == 0)
            {
                return 1 / beta * Math.Atan(1 / (1 - ar));
            }
            else
            {
                return 1 / beta * Math.Atan(1 / (1 + 2 * beta * h - (1 + beta * h) * ar));
            }
        }

        // 杭各部のせん断力
        public double GetShearForce(double x)
        {
            if (h == 0)
            {
                return -horizontalLoad * Math.Exp(-beta * x) * (Math.Cos(beta * x) - (1 - ar) * Math.Sin(beta * x));
            }
            else if (x < 0)
            {
                return -horizontalLoad;
            }
            else
            {
                return -horizontalLoad / 2 * Math.Exp(-beta * h) * ((1 + beta * h) * (2 - ar) * (Math.Cos(beta * x) - Math.Sin(beta * x))
                    - (2 * beta * h - (1 + beta * h) * ar) * (Math.Cos(beta * x) - Math.Sin(beta * x)));
            }
        }

    }
}
