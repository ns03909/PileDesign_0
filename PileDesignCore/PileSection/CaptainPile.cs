using PileDesignCore.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;

namespace PileDesignCore
{
    public class CaptainPile : BaseViewModel
    {
        internal int DivisionNum { get; } = 100;
        private double _d;
        public double D
        {
            get => _d;
            set => SetProperty(ref _d, value);
        }
        public string ShearResistanceType { get; }
        public double RD1 { get; } // PCリング内径
        public double RD2 { get; } // PCリング外径
        public double SD { get; } // ウルボンスパイラル外径
        public double Hr { get; } // PCリング高さ
        public double L1 { get; } // 定着筋定着長さ
        public double L2 { get; } // 定着筋全長
        public double Ts { get; } // 鋼板リング厚さ
        public double Tc { get; } // コンクリート厚さ

        public double Fcb { get; }    // パイルキャップコンクリートFc
        public double Fcc { get; }   // パイルキャップコンクリートFc

        private double _nu = 1.00; // 絞り係数
        public double Nu
        {
            get => _nu;
            set => SetProperty(ref _nu, value);
        }

        // 絞り率
        public ObservableCollection<double> ContractionRatioOption { get; } = new ObservableCollection<double>()
        {
            1.00,
            0.85,
            0.70
        };

        private readonly CaptainPileTensionBarPCDLoader _captainPileTensionBarPCDLoader = new CaptainPileTensionBarPCDLoader();
        public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsSquare { get; set; } = new List<CaptainPileTensionBarPCD>();
        public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsCircle { get; set; } = new List<CaptainPileTensionBarPCD>();
        public ObservableCollection<string> PCD_squareOption { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> PCD_circleOption { get; set; } = new ObservableCollection<string>();
        private void LoadCaptainPileTensionBarPCDs()
        {
            CaptainPileTensionBarPCDsSquare = _captainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_square.csv");
            foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
            {
                PCD_squareOption.Add($"{pcdSquare.D}-{pcdSquare.Nu}");
            }

            CaptainPileTensionBarPCDsCircle = _captainPileTensionBarPCDLoader.LoadFromCsv(@"..\..\PileLibrary\CaptainPileTensionBarPCD_circle.csv");
            foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
            {
                PCD_circleOption.Add($"{pcdCircle.D}-{pcdCircle.Nu}");
            }
        }

        public void UpdateTDorTB()
        {
            if (CTPTensionRebars.IsSquareArrangement)
            {
                foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
                {
                    if (D == pcdSquare.D && Nu == pcdSquare.Nu)
                    {
                        CTPTensionRebars.TDorTB = pcdSquare.TDorTB;
                        break;
                    }
                }
            }
            else if (CTPTensionRebars.IsCircleArrangement)
            {
                foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
                {
                    if (D == pcdCircle.D && Nu == pcdCircle.Nu)
                    {
                        CTPTensionRebars.TDorTB = pcdCircle.TDorTB;
                        break;
                    }
                }
            }
        }

        public double Ep { get; set; } // コンクリート厚さ
        public double Ip { get; set; }
        public double Hp { get; set; }

        public double Ec { get; set; } // コンクリート厚さ
        public double Ic { get; set; }
        public double Hc { get; set; }

        public double Eb { get; set; } // コンクリート厚さ
        public double Ib { get; set; }
        public double Hb { get; set; }

        public double Ae { get; set; }
        public double Z { get; set; }
        public double Ke { get; set; }

        public double YieldMaxCurvature { get; set; }
        public List<double> YieldMs { get; set; }
        public List<double> YieldNs { get; set; }
        public List<double> YieldEpsilon0s { get; set; }
        public List<double> YieldCurvatures { get; set; }
        public List<double> UltimateMs { get; set; }
        public List<double> UltimateNs { get; set; }
        public List<double> UltimateEpsilon0s { get; set; }
        public List<double> UltimateCurvatures { get; set; }

        public List<double> Ns { get; private set; }
        public List<(List<double>, List<double>)> ThetasMs { get; private set; }

        // PCリングオプション
        public ObservableCollection<string> PCRingOption { get; } = new ObservableCollection<string>();
        public List<PCRing> PCRings { get; } = new List<PCRing>();
        readonly PCRingLoader _PCRingPileLoader = new PCRingLoader();

        public CTPConcrete CTPConcrete { get; set; } = new CTPConcrete();
        public CTPTensionRebars CTPTensionRebars { get; set; } = new CTPTensionRebars();

        public PCRing PCRing { get; set; } = new PCRing();

        public string SelectedPCRingName { get; set; }

        // コンストラクタ
        internal CaptainPile()
        {
            Update();
            LoadCaptainPileTensionBarPCDs();
        }

        private void Update()
        {
            (Ns, ThetasMs) = GetNMThetaRelationship(); // N、M、θ関係
        }

        //N、θ、M関係を返すメソッド
        (List<double>, List<(List<double>, List<double>)>) GetNMThetaRelationship()
        {
            List<double> Ns = new List<double>();
            List<(List<double>, List<double>)> thetasMs = new List<(List<double>, List<double>)>();
            int nNum = 10;
            double nMin = GetNMin();
            double nMax = GetNMax();
            double N;

            for (int i = 0; i < nNum; i++)
            {
                N = nMin + (nMax - nMin) * (double)i / nNum;
                thetasMs.Add(GetMThetaRelationship(N));
                Ns.Add(N);
            }
            return (Ns, thetasMs);
        }

        // ある軸力に対するθ, M関係を返すメソッド
        (List<double>, List<double>) GetMThetaRelationship(double N)
        {
            List<double> thetas = new List<double>();
            List<double> Ms = new List<double>();

            
            double Mu = GetMu(N);
            (double My, double curvatureY) = GetMyCurvatureY(N);
            double thetaY = curvatureY * D;
            double thetaU = 0.04;

            if (0 <= N) // 圧縮
            {
                double M1 = GetM1(N);
                double theta1;
                if (Ke !=0)
                {
                    theta1 = M1 / Ke;
                }
                else
                {
                    theta1 = 0;
                }
                
                double thetaYD = GetThetaYDonCompressionSide(theta1, thetaY, Mu, My, M1);

                Ms.Add(0.0);
                Ms.Add(M1);
                Ms.Add(Mu);
                Ms.Add(Mu);
                //Ms.AddRange(new double[] { 0.0, GetM1(N), GetMu(N) });
                thetas.Add(0.0);
                thetas.Add(theta1);
                thetas.Add(thetaYD);
                thetas.Add(thetaU);

            }
            else //if (N < 0) 引張
            {

                double thetaYD = GetThetaYDonTensionSide(My, Mu, thetaY);
                Ms.Add(0.0);
                Ms.Add(Mu);
                Ms.Add(Mu);
                thetas.Add(0.0);
                thetas.Add(thetaYD);
                thetas.Add(thetaU);
            }
            return (thetas, Ms);
        }

        // 圧縮側のθ、yを得るメソッド
        private double GetThetaYDonCompressionSide(double theta1, double thetaY, double Mu, double My, double M1)
        {
            if(thetaY-theta1 != 0)
            {
                double K2 = (My - M1) / (thetaY - theta1);
                return  theta1 + (Mu - M1) / K2;
            }
            else
            {
                return 0;
            }
        }

        // 引張側のθ、yを得るメソッド
        private double GetThetaYDonTensionSide(double My,  double Mu, double thetaY)
        {
            if(My != 0)
            {
                return thetaY * Mu / My;
            }
            else
            {
                return 0;
            }
        }

        // 最大圧縮軸力を返すメソッド
        private double GetNMax()
        {
            return 2 / 3 * Fcb * Math.PI * Math.Pow(D, 2) / 4.0;
        }

        // 最大引張軸力を返すメソッド
        private double GetNMin()
        {
            if (CTPTensionRebars.HasTensionRebars)
            {
                return -CTPTensionRebars.Ag * CTPTensionRebars.SigmaY;
            }
            else
            {
                return 0.0;
            }
        }


        // 離間時曲げモーメントM1を返すメソッド
        internal double GetM1(double NTarget)
        {
            if (Ae != 0)
            {
                double sigma0 = NTarget / Ae;
                return sigma0 * Z;
            }
            else
            {
                return 0;
            }
        }

        // 降伏時曲げモーメントMyを返すメソッド
        internal (double, double) GetMyCurvatureY(double NTarget)
        {
            if (YieldNs != null && YieldNs.Count > 0)
            {
                double N = 0.0; double N1;
                double M = 0.0;
                double curvature = 1.0 * Math.Pow(10, -6);
                double deltaCurvature = curvature / 500.0;
                double epsilon0;
                double x = -CTPConcrete.D * Nu / 2; // isCompressionSide=true;
                double sigma = 0.85 * CTPConcrete.SigmaMax; // isCompressionSide=true;
                for (int i = 0; i < YieldNs.Count; i++)
                {
                    if (NTarget < YieldNs[i])
                    {
                        if (i == 0) { return (0.0, 0.0); }
                        N = (YieldNs[i - 1] + YieldNs[i]) / 2.0;
                        M = (YieldMs[i - 1] + YieldMs[i]) / 2.0;
                        curvature = (YieldCurvatures[i - 1] + YieldCurvatures[i]) / 2.0;
                        deltaCurvature = (YieldCurvatures[i] - YieldCurvatures[i - 1]) / 100;

                        if (i < YieldNs.Count / 2.0) // isCompressionSide = false;
                        {
                            x = CTPTensionRebars.TDorTB / 2;
                            sigma = -CTPTensionRebars.SigmaY;
                        }
                        break;

                    }
                    else if (i == YieldNs.Count - 1)
                    { return (0.0, 0.0); }
                }

                while (Math.Abs(N - NTarget) > 0.1)
                {

                    epsilon0 = GetEpsilon0(curvature, x, sigma);
                    N1 = GetForceAndMoment(epsilon0, curvature + deltaCurvature).Item1;
                    curvature = deltaCurvature / (N1 - N) * (NTarget - N) + curvature;

                    epsilon0 = GetEpsilon0(curvature, x, sigma);
                    var result = GetForceAndMoment(epsilon0, curvature);
                    (N, M) = (result.Item1, result.Item2);
                }

                return (M, curvature);
            }
            else
            {
                return (0.0, 0.0);
            }
        }

        // 終局時曲げモーメントMuを返すメソッド
        internal double GetMu(double NTarget)
        {
            if (UltimateNs != null && UltimateNs.Count > 0)
            {
                double N = 0.0; double N1;
                double M = 0.0;
                double curvature = 1.0 * Math.Pow(10, -6);
                double deltaCurvature = curvature / 500.0;
                double epsilon0;

                for (int i = 0; i < UltimateNs.Count; i++)
                {
                    if (NTarget < UltimateNs[i])
                    {
                        if (i == 0) { return 0.0; }
                        N = (UltimateNs[i - 1] + UltimateNs[i]) / 2.0;
                        M = (UltimateMs[i - 1] + UltimateMs[i]) / 2.0;
                        curvature = (UltimateCurvatures[i - 1] + UltimateCurvatures[i]) / 2.0;
                        deltaCurvature = (UltimateCurvatures[i] - UltimateCurvatures[i - 1]) / 100;
                        break;

                    }
                    else if (i == UltimateNs.Count - 1)
                    { return 0.0; }
                }

                while (Math.Abs(N - NTarget) > 0.1)
                {
                    epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * Nu / 2, CTPConcrete.SigmaMax);
                    N1 = GetForceAndMoment(epsilon0, curvature + deltaCurvature).Item1;
                    curvature = deltaCurvature / (N1 - N) * (NTarget - N) + curvature;

                    epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * Nu / 2, CTPConcrete.SigmaMax);
                    var result = GetForceAndMoment(epsilon0, curvature);
                    (N, M) = (result.Item1, result.Item2);
                }
                return M;
            }
            else
            {
                return 0.0;
            }
        }

        internal void SetBasicProperties(PCRing _pcring, double pileCapEc)
        {
            PCRing = _pcring;
            Ep = pileCapEc; ////////////////// kui ni change
            Ip = Math.Pow(PCRing.D, 4) / Math.PI / 64.0;
            if (PCRing.D < 2100)
            { Hp = 90.0; }
            else
            { Hp = 110.0; }

            Ec = pileCapEc;
            Ic = Math.Pow(PCRing.RD1, 4) / Math.PI / 64.0;
            Hc = PCRing.Hr - Hp; ///

            Eb = Ec;
            Ib = Ic; ///
            Hb = PCRing.D / 2.0;

            double Kp = GetK(Ep, Ip, Hp);
            double Kc = GetK(Ec, Ic, Hc);
            double Kb = GetK(Eb, Ib, Hb);
            Ke = GetKe(Kp, Kc, Kb); // 初期剛性
            //Nu = contractionRatio; // 絞り係数

            Ae = Math.PI * Math.Pow(PCRing.D, 2.0) / 4.0;
            Z = Math.PI * Math.Pow(PCRing.D, 3.0) / 32.0;

            if (CTPTensionRebars.HasTensionRebars)
            {
                YieldMaxCurvature = GetAllowableMaxCurvature();
                var result01 = GetAllowableMNInterection(YieldMaxCurvature);
                (YieldNs, YieldMs, YieldEpsilon0s, YieldCurvatures) = (result01.Item1, result01.Item2, result01.Item3, result01.Item4);
            }
            else
            {
                var result02 = GetUltimateMNInterection(0.85 * CTPConcrete.SigmaMax);
                (YieldNs, YieldMs, YieldEpsilon0s, YieldCurvatures) = (result02.Item1, result02.Item2, result02.Item3, result02.Item4);
            }
            var result03 = GetUltimateMNInterection(CTPConcrete.SigmaMax);
            (UltimateNs, UltimateMs, UltimateEpsilon0s, UltimateCurvatures) = (result03.Item1, result03.Item2, result03.Item3, result03.Item4);
            //ShearResistanceType = shearResistanceType;


        }

        // 杭体、PCリング内コンクリート、パイルキャップ部分の回転剛性
        internal double GetK(double E, double I, double H)
        {
            return E * I / H;
        }

        // 等価初期回転剛性を得るメソッド
        internal double GetKe(double Kp, double Kc, double Kb)
        {
            return  1 / (1 / Kp + 1 / Kc + 1 / Kb);
        }

        internal double GetEpsilon0(double curvature, double x, double epsilon)
        {
            return x * curvature + epsilon;
        }

        internal double GetEpsilonC(double curvature, double x, double epsilon)
        {
            return (CTPConcrete.D * Nu / 2 + x) * curvature + epsilon;
        }

        internal double GetAllowableMaxCurvature()
        {
            double maxAllowableCurvature = double.MaxValue;
            if (CTPTensionRebars.HasTensionRebars)
            {
                maxAllowableCurvature = (CTPConcrete.Epsilon085 + CTPTensionRebars.EpsilonY) / (CTPConcrete.D * Nu / 2 + CTPTensionRebars.TDorTB / 2);
            }
            return maxAllowableCurvature;
        }

        internal (double, double, double, double) GetForceAndMoment(double epsilon0, double curvature)
        {
            double N;
            double M;
            CTPCirclularSolidSection circlularSoildSection = new CTPCirclularSolidSection(CTPConcrete.D * Nu);

            (N, M) = circlularSoildSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
            if (CTPTensionRebars.HasTensionRebars)
            {
                if (CTPTensionRebars.IsCircleArrangement)
                {
                    CTPCircularDotSection circularDotSection = new CTPCircularDotSection(CTPTensionRebars.TDorTB, CTPTensionRebars.BarNum, CTPTensionRebars.BarArea);

                    var result1 = circularDotSection.GetForceAndMoment(CTPTensionRebars, epsilon0, curvature);
                    var result2 = circularDotSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
                    N += result1.Item1 - result2.Item1;
                    M += result1.Item2 - result2.Item2;
                }

                else
                {
                    CTPSquareDotSection squareDotSection = new CTPSquareDotSection(CTPTensionRebars.TDorTB, CTPTensionRebars.BarNum, CTPTensionRebars.BarArea);

                    var result1 = squareDotSection.GetForceAndMoment(CTPTensionRebars, epsilon0, curvature);
                    var result2 = squareDotSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
                    N += result1.Item1 - result2.Item1;
                    M += result1.Item2 - result2.Item2;
                }
            }
            return (N, M, epsilon0, curvature);
        }

        // 使用損傷限界MNインタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetAllowableMNInterection(double maxCurvature)
        {
            List<double> axialForces = new List<double> { };
            List<double> bendingMoments = new List<double> { };
            List<double> epsilonCs = new List<double> { };
            List<double> curvatures = new List<double> { };
            double epsilon0;
            double curvature;
            double epsilonC;

            for (int i = 0; i <= DivisionNum; i++)
            {
                curvature = maxCurvature * i / DivisionNum;
                epsilonC = GetEpsilonC(curvature, CTPTensionRebars.TDorTB / 2, -CTPTensionRebars.EpsilonY);
                epsilon0 = curvature * CTPTensionRebars.TDorTB / 2 - CTPTensionRebars.EpsilonY;
                var result = GetForceAndMoment(epsilon0, curvature); // 引張側 純引張～
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }

            for (int i = DivisionNum; i >= 0; i--)
            {
                curvature = maxCurvature * i / DivisionNum;
                epsilonC = CTPConcrete.Epsilon085;
                epsilon0 = CTPConcrete.Epsilon085 - curvature * CTPConcrete.D / 2;
                var result = GetForceAndMoment(epsilon0, curvature); // 圧縮側 ～純圧縮
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }

        // 安全限界MN インタラクション取得メソッド
        internal (List<double>, List<double>, List<double>, List<double>) GetUltimateMNInterection(double maxEpsilonC)
        {
            List<double> axialForces = new List<double> { };
            List<double> bendingMoments = new List<double> { };
            List<double> epsilonCs = new List<double> { };
            List<double> curvatures = new List<double> { };
            double epsilonC;
            double epsilon0;
            double curvature;
            double maxCurvature = (0.003 + 0.0025) * 20.0 / (PCRing.D * Nu);

            for (int i = 0; i <= DivisionNum * 2; i++)
            {
                if (i == 0)
                {
                    epsilon0 = -0.006;
                    epsilonC = -0.006;
                    curvature = 0.0;
                }
                else if (i != DivisionNum * 2)
                {
                    curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
                    epsilon0 = maxEpsilonC - curvature * CTPConcrete.D / 2;
                    epsilonC = maxEpsilonC;
                }
                else
                {
                    epsilon0 = maxEpsilonC;
                    epsilonC = maxEpsilonC;
                    curvature = 0.0;
                }

                var result = GetForceAndMoment(epsilon0, curvature); // 引張側 純引張～
                axialForces.Add(result.Item1);
                bendingMoments.Add(result.Item2);
                epsilonCs.Add(epsilonC);
                curvatures.Add(curvature);
            }
            return (axialForces, bendingMoments, epsilonCs, curvatures);
        }
    }

    public abstract class CTPMaterial : BaseViewModel
    {
        public abstract double GetStress(double epsilon);
    }

    public class CTPConcrete : CTPMaterial
    {
        internal double D { get; set; }
        internal double Fc { get; set; }
        internal double Nu { get; set; }
        internal double EpsilonB { get; set; } = 0.003;
        internal double SigmaMax { get; set; }
        internal double Epsilon085 { get; set; }


        // コンストラクタ
        public CTPConcrete() //double _D, double _Fc, double _nu)
        {
            //D = _D;
            //Fc = _Fc;
            //Nu = _nu;
            SigmaMax = Math.Min(Fc / Math.Pow(Nu, 2.0), 2 * Fc);
            Epsilon085 = GetDSigmaonDEpsilon(0.85 * SigmaMax);
        }

        public override double GetStress(double epsilon)
        {
            double sigma;
            if (epsilon <= 0)
            {
                sigma = 0.0;
            }
            else if (0 < epsilon && epsilon < EpsilonB)
            {
                sigma = 6.75 * (Math.Exp(-0.812 * (epsilon / EpsilonB)) - Math.Exp(-1.218 * (epsilon / EpsilonB)));
            }
            else
            {
                sigma = SigmaMax;
            }
            return sigma;
        }

        public double GetDSigmaonDEpsilon(double epsilon)
        {
            return 6.75 * ((-0.812) / EpsilonB * (Math.Exp(-0.812 * (epsilon / EpsilonB)) - (-1.218) / EpsilonB * Math.Exp(-1.218 * (epsilon / EpsilonB))));
        }
        private double GetEpsilon(double targetSigma)
        {
            double sigma = double.MaxValue;
            double epsilon = 0.003 / 100.0;
            double _DSigmaonDEpsilon;

            while (Math.Abs(targetSigma - sigma) > 0.003 / 1000.0)
            {
                sigma = GetStress(epsilon);
                _DSigmaonDEpsilon = GetDSigmaonDEpsilon(epsilon);
                epsilon += (targetSigma - sigma) / _DSigmaonDEpsilon;
            }
            return epsilon;
        }
    }

    // キャプテンパイル引張鉄筋クラス
    public class CTPTensionRebars : CTPMaterial
    {
        public double SigmaY { get; set; }
        public double EpsilonY { get; set; }
        public double Es { get; set; } = 205_000;
        public double BarArea { get; set; }

        private double _TDorTB;
        public double TDorTB
        {
            get => _TDorTB;
            set => SetProperty(ref _TDorTB, value);
        }



        public int BarNum { get; set; }
        public string BarSize { get; set; }

        private bool _hasTensionRebars = false;
        public bool HasTensionRebars
        {
            get => _hasTensionRebars;
            set => SetProperty(ref _hasTensionRebars, value);
        }

        private bool _isCircleArrangement = false;
        public bool IsCircleArrangement
        {
            get => _isCircleArrangement;
            set => SetProperty(ref _isCircleArrangement, value);
        }

        private bool _isSquareArrangement = true;
        public bool IsSquareArrangement
        {
            get => _isSquareArrangement;
            set => SetProperty(ref _isSquareArrangement, value);
        }

        public double Ag { get; set; }

        // 引張鉄筋材質
        public ObservableCollection<string> TensionAnchorGradeOption { get; } = new ObservableCollection<string>()
        {
            "SD390",
            "SD490",
            "SD685"
        };

        // 引張鉄筋径
        public ObservableCollection<string> TensionAnchorDiaOption { get; } = new ObservableCollection<string>()
        {
            "D38",
            "D41",
        };

        // 円形配置引張鉄筋本数オプション
        public ObservableCollection<int> CaptainPileTensionBarNumberOption_circle { get; set; } = new ObservableCollection<int>()
        { 6, 8, 10, 12, 16, 20 };

        // 正方形配置引張鉄筋本数オプション
        public ObservableCollection<int> CaptainPileTensionBarNumberOption_square { get; set; } = new ObservableCollection<int>()
        { 4, 8, 12, 16, 20 };

        private int _selectedBarNumberCircle = 6;
        public int SelectedBarNumberCircle
        {
            get => _selectedBarNumberCircle;
            set => SetProperty(ref _selectedBarNumberCircle, value);
        }

        private int _selectedBarNumberSquare = 4;
        public int SelectedBarNumberSquare
        {
            get => _selectedBarNumberSquare;
            set => SetProperty(ref _selectedBarNumberSquare, value);
        }

        public string _selectedTensionAnchorGrade = "SD390";
        public string SelectedTensionAnchorGrade
        {
            get => _selectedTensionAnchorGrade;
            set => SetProperty(ref _selectedTensionAnchorGrade, value);
        }

        private string _selectedTensionAnchorDia = "D38";
        public string SelectedTensionAnchorDia
        {
            get => _selectedTensionAnchorDia;
            set => SetProperty(ref _selectedTensionAnchorDia, value);
        }

        //public void UpdateTDorTB()
        //{
        //    foreach (a in CaptainPileTensionBarPCDsSquare)
        //}

        // コンストラクタ
        public CTPTensionRebars() // bool hasTensionRebars, string grade, string barSize, int barNum, bool isCircleArrangement, int _TDorTB)
        {
            Update();
        }

        public void Update()
        {

            if (SelectedTensionAnchorGrade == "SD390")
            { SigmaY = 390; }
            else if (SelectedTensionAnchorGrade == "SD490")
            { SigmaY = 490; }
            else if (SelectedTensionAnchorGrade == "SD685")
            { SigmaY = 685; }

            //BarSize = barSize;
            if (BarSize == "D38")
            { BarArea = 1140.0; }
            else if (BarSize == "D41")
            { BarArea = 1340.0; }
            
            EpsilonY = SigmaY / Es;
            if (IsSquareArrangement)
            {
                Ag = SigmaY * SelectedBarNumberSquare;
            }
            else if (IsCircleArrangement)
            {
                Ag = SigmaY * SelectedBarNumberCircle;
            }
        }

        public override double GetStress(double epsilon)
        {
            double sigma;
            if (epsilon <= -EpsilonY)
            {
                sigma = -SigmaY;
            }
            else if (-EpsilonY < epsilon && epsilon < EpsilonY)
            {
                sigma = Es * epsilon;
            }
            else
            {
                sigma = SigmaY;
            }
            return sigma;
        }
    }

    internal abstract class CTPSection
    { }

    // 円形断面クラス
    internal class CTPCirclularSolidSection : CTPSection
    {
        private double Dia { get; }

        // コンストラクタ
        internal CTPCirclularSolidSection(double diameter)
        {
            Dia = diameter;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(CTPMaterial material, double epsilon0, double curvature, int division = 100)
        {
            double z;
            double dz = Dia / division;
            double epsilon;
            double sigma;
            double width;

            double axialForce = 0.0;
            double bendingMoment = 0.0;

            // 圧縮縁ひずみ度 epsilonC
            // 中心ひずみ度 epsilon0
            for (int i = 0; i < division; i++)
            {
                z = -Dia / 2 + (0.5 + i) * dz;
                width = 2.0 * Math.Sqrt(Math.Pow(Dia / 2, 2) - Math.Pow(z, 2));
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(epsilon);
                axialForce += width * sigma * dz;
                bendingMoment += width * sigma * dz * (-z);
            }
            return (axialForce, bendingMoment);
        }
    }


    // 正方形配置　点断面クラス
    class CTPSquareDotSection : CTPSection
    {
        private double TB { get; }
        private double NumDot { get; }
        private double Area { get; }

        // コンストラクタ
        public CTPSquareDotSection(double _TB, int numDot, double area)
        {
            TB = _TB;
            NumDot = numDot;
            Area = area;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(CTPMaterial material, double epsilon0, double curvature)
        {
            double z;
            double epsilon;
            double sigma;

            double axialForce = 0.0;
            double bendingMoment = 0.0;

            // 圧縮縁ひずみ度 epsilonC
            // 中心ひずみ度 epsilon0
            for (int i = 0; i < NumDot / 4 + 1; i++)
            {
                z = -TB / 2 + TB / (i * (NumDot / 4));
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(epsilon);
                if (i == 0 || i == NumDot / 4)
                {
                    axialForce += sigma * Area * (NumDot / 4 + 1);
                    bendingMoment += sigma * Area * (NumDot / 4 + 1) * (-z);
                }
                else
                {
                    axialForce += sigma * Area;
                    bendingMoment += sigma * Area * (-z);
                }
            }
            return (axialForce, bendingMoment);
        }
    }

    // 円形配置　点断面クラス
    internal class CTPCircularDotSection : CTPSection
    {
        private double TD { get; }
        private double NumDot { get; }
        private double Area { get; }

        // コンストラクタ
        public CTPCircularDotSection(double _TD, int numDot, double area)
        {
            TD = _TD;
            NumDot = numDot;
            Area = area;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double) GetForceAndMoment(CTPMaterial material, double epsilon0, double curvature)
        {
            double z;
            double epsilon;
            double sigma;

            double axialForce = 0.0;
            double bendingMoment = 0.0;

            // 圧縮縁ひずみ度 epsilonC
            // 中心ひずみ度 epsilon0
            for (int i = 0; i < NumDot; i++)
            {
                z = TD / 2.0 * Math.Cos(2.0 * Math.PI * i / NumDot);
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(epsilon);
                axialForce += sigma * Area;
                bendingMoment += sigma * Area * (-z);
            }
            return (axialForce, bendingMoment);
        }
    }
}

