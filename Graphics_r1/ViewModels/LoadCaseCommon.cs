//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace PileDesign.ViewModels

//{
//    public class CommonLoadCaseBase
//    {
//        public bool IsSoilNonLinear { get; set; }
//        public bool IsPileNonLinear { get; set; }
//        public double UpperMassForce { get; set; }
//        public double FoundationMassForce { get; set; }
//        public double ForceActionPointX { get; set; }
//        public double ForceActionPointY { get; set; }
//        public double ForceActionPointAltitude { get; set; }
//    }


//    public class CommonLoadCase : CommonLoadCaseBase
//    {
//        public CommonLoadCase(bool isSoilNonLinear, bool isPileNonLinear,
//                         double upperMassForce, double foundationMassForce,
//                         double forceActionPointX, double forceActionPointY, double forceActionPointAltitude)
//        {
//            IsSoilNonLinear = isSoilNonLinear;
//            IsPileNonLinear = isPileNonLinear;
//            UpperMassForce = upperMassForce;
//            FoundationMassForce = foundationMassForce;
//            ForceActionPointX = forceActionPointX;
//            ForceActionPointY = forceActionPointY;
//            ForceActionPointAltitude = forceActionPointAltitude;
//        }
//    }
//}