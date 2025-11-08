using System;

namespace PileDesignCore.Pile
{
    internal class Neire
    {
        public int SoilID { get; set; }
        public int SoildispID { get; set; }
        public double[] Datum1 { get; set; }
        public double TopAlt { get; set; }
        public double[] Datum2 { get; set; }
        public double BottomAlt { get; set; }
        public double Height { get; set; }
        public double[] X1 { get; set; }
        public double[] X2 { get; set; }
        public double[] Y1 { get; set; }
        public double[] Y2 { get; set; }
        public double[] X { get; set; }
        public double[] Y { get; set; }
        public double[] WX { get; set; }
        public double[] WY { get; set; }
        public double Ds { get; set; }
        public double Dd { get; set; }

        public void Input(int neireSoilID, int neireSoildispID, double[] neireDatum1, double neireTopAlt, double[] neireDatum2, double neireBtmAlt, double neireHeight, double[] neireX1, double[] neireX2, double[] neireY1, double[] neireY2)
        {
            SoilID = neireSoilID;
            SoildispID = neireSoildispID;
            Datum1 = neireDatum1; // 基準1(リスト)
            TopAlt = neireTopAlt; // 根入れ上端レベル
            Datum2 = neireDatum2; // 基準2(リスト)
            BottomAlt = neireBtmAlt; // 根入れ下端レベル
            Height = neireHeight; // 根入れ高さ
            X1 = neireX1; // 根入れ
            X2 = neireX2; // 根入れ
            Y1 = neireY1; // 根入れ
            Y2 = neireY2; // 根入れ
            X = CalculateAverage(X1, X2); // 土圧合力中心
            Y = CalculateAverage(Y1, Y2); // 土圧合力中心
            WX = CalculateDifference(X2, X1); // 幅
            WY = CalculateDifference(Y2, Y1); // 幅
        }

        public void SetDosou(double ds)
        {
            Ds = ds;
        }

        public void SetDosoudisp(double dd)
        {
            Dd = dd;
        }

        private double[] CalculateAverage(double[] arr1, double[] arr2)
        {
            int length = Math.Min(arr1.Length, arr2.Length);
            double[] result = new double[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = (arr1[i] + arr2[i]) * 0.5;
            }
            return result;
        }

        private double[] CalculateDifference(double[] arr1, double[] arr2)
        {
            int length = Math.Min(arr1.Length, arr2.Length);
            double[] result = new double[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = arr2[i] - arr1[i];
            }
            return result;
        }
    }
}
