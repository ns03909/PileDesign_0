//using PileDesign.Models.InputData;
//using System;
//using System.Collections.ObjectModel;

//namespace PileDesign.Models.Analysis
//{
//    public partial class SettlementAnalysis : BaseModel
//    {
//        SoilPile SoilPile { get; set; }

//        // 杭周面データ
//        ObservableCollection<SettlementPileCircumDataItem> SettlementPileCircumDataItems { get; set; }

//        // 接線剛性
//        private double _kvs;
//        public double Kvs
//        {
//            get => _kvs;
//            set => SetProperty(ref _kvs, value);
//        }

//        // 杭の周面抵抗剛性
//        private static double GetPileCircumKvs(bool apc, bool apt, double dz, double s1, double s2, double tau1, double tau2, double l, double b)
//        {
//            if (dz >= 0 && apc == false)
//            { return 0; }
//            else if (dz < 0 && apt == false)
//            { return 0; }
//            else if (Math.Abs(dz) <= s1)
//            {
//                return tau1 / s1 * l * b * Math.PI;
//            }
//            else if (Math.Abs(dz) <= s2)
//            {
//                return (tau2 - tau1) / (s2 - s1) * l * b * Math.PI;
//            }
//            else
//            { return 0; }
//        }

//        // 杭の先端抵抗剛性
//        private static double GetPileToeKvs(double rz, double dp, double rpu, double alpha, double n)
//        {
//            if (rz <= 0)
//            { return 0; }
//            else
//            {
//                return GetTangentStiffnessPileToe(dp, Math.Abs(rz), rpu, alpha, n);
//            }
//        }

//        // 杭の先端抵抗剛性
//        private static double GetTangentStiffnessPileToe(double dp, double rz, double rpu, double alpha, double n)
//        {
//            if (rpu < 0)
//            { return 0; }
//            else
//            {
//                double stan = 0.1 * dp * (alpha * (1 / rpu) + n * (1 - alpha) * (1 / rpu) * Math.Pow(rpu / rpu, n));
//                return 1 / stan;
//            }
//        }



//        public SettlementAnalysis(SoilPile soilPile)
//        {
//            SoilPile = soilPile;

//        }
//    }
//}
