using PileDesign.Constants;
using PileDesign.Models.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Numerics;

namespace PileDesign.Models
{
    // CaptainPileクラス
    public class CaptainPile : BaseModel
    {
        // 追加: 計算結果のメモ化（epsilon0/curvature を丸めたキー）
        private readonly ConcurrentDictionary<long, (double N, double M)> _forceMomentCache = new();

        private static long MakeKey(double epsilon0, double curvature, int roundDigits = 9)
        {
            // 丸めてビット列からキー生成（簡易）
            long a = BitConverter.DoubleToInt64Bits(Math.Round(epsilon0, roundDigits));
            long b = BitConverter.DoubleToInt64Bits(Math.Round(curvature, roundDigits));
            unchecked
            {
                return a ^ (b << 1);
            }
        }

        // Cached wrapper: 既存の GetForceAndMoment(epsilon0, curvature) を内部で呼ぶ
        private (double N, double M) GetForceAndMomentCached(double epsilon0, double curvature)
        {
            var key = MakeKey(epsilon0, curvature);
            return _forceMomentCache.GetOrAdd(key, _ =>
            {
                var res = GetForceAndMoment(epsilon0, curvature); // 既存の重い実計算（N,M,epsilon0,curvature）を呼ぶ
                return (res.Item1, res.Item2);
            });
        }
        internal int DivisionNum { get; } = 100;

        private double _d;
        public double D
        {
            get => _d;
            set => SetProperty(ref _d, value);
        }

        private string _shearResistanceType;
        public string ShearResistanceType
        {
            get => _shearResistanceType;
            set => SetProperty(ref _shearResistanceType, value);
        }

        // PCリング内径
        private double _rd1;
        public double RD1
        {
            get => _rd1;
            set => SetProperty(ref _rd1, value);
        }

        // PCリング外径
        private double _rd2;
        public double RD2
        {
            get => _rd2;
            set => SetProperty(ref _rd2, value);
        }

        // ウルボンスパイラル外径
        private double _sd;
        public double SD
        {
            get => _sd;
            set => SetProperty(ref _sd, value);
        }

        // PCリング高さ
        private double _hr;
        public double Hr
        {
            get => _hr;
            set => SetProperty(ref _hr, value);
        }

        // 定着筋定着長さ

        private double _l1;
        public double L1
        {
            get => _l1;
            set => SetProperty(ref _l1, value);
        }

        // 定着筋全長
        private double _l2;
        public double L2
        {
            get => _l2;
            set => SetProperty(ref _l2, value);
        }

        // 鋼板リング厚さ
        private double _ts;
        public double Ts
        {
            get => _ts;
            set => SetProperty(ref _ts, value);
        }

        // コンクリート厚さ
        private double _tc;
        public double Tc
        {
            get => _tc;
            set => SetProperty(ref _tc, value);
        }

        // パイルキャップコンクリートFc
        private double _fcb;
        public double Fcb
        {
            get => _fcb;
            set => SetProperty(ref _fcb, value);
        }

        // パイルキャップコンクリートFc
        private double _fcc;
        public double Fcc
        {
            get => _fcc;
            set => SetProperty(ref _fcc, value);
        }

        // 絞り係数
        private double _nu = 1.00; // 
        public double Nu
        {
            get => _nu;
            set => SetProperty(ref _nu, value);
        }

        public double[] ContractionRatioOption { get; } = [1.00, 0.85, 0.70];        // 絞り率
        private readonly CaptainPileTensionBarPCDLoader _captainPileTensionBarPCDLoader = new();

        private List<CaptainPileTensionBarPCD> _captainPileTensionBarPCDsSquare = [];
        public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsSquare
        {
            get => _captainPileTensionBarPCDsSquare;
            set => SetProperty(ref _captainPileTensionBarPCDsSquare, value);
        }

        private List<CaptainPileTensionBarPCD> _captainPileTensionBarPCDsCircle = [];
        public List<CaptainPileTensionBarPCD> CaptainPileTensionBarPCDsCircle
        {
            get => _captainPileTensionBarPCDsCircle;
            set => SetProperty(ref _captainPileTensionBarPCDsCircle, value);
        }

        private ObservableCollection<string> _PCD_squareOption = [];
        public ObservableCollection<string> PCD_squareOption
        {
            get => _PCD_squareOption;
            set => SetProperty(ref _PCD_squareOption, value);
        }

        private ObservableCollection<string> _PCD_circleOption = [];
        public ObservableCollection<string> PCD_circleOption
        {
            get => _PCD_circleOption;
            set => SetProperty(ref _PCD_circleOption, value);
        }

        private double _ep;
        public double Ep
        {
            get => _ep;
            set => SetProperty(ref _ep, value);
        }

        private double _ip;
        public double Ip
        {
            get => _ip;
            set => SetProperty(ref _ip, value);
        }

        private double _hp;
        public double Hp
        {
            get => _hp;
            set => SetProperty(ref _hp, value);
        }

        private double _ec;
        public double Ec
        {
            get => _ec;
            set => SetProperty(ref _ec, value);
        }

        private double _ic;
        public double Ic
        {
            get => _ic;
            set => SetProperty(ref _ic, value);
        }

        private double _hc;
        public double Hc
        {
            get => _hc;
            set => SetProperty(ref _hc, value);
        }

        private double _eb;
        public double Eb
        {
            get => _eb;
            set => SetProperty(ref _eb, value);
        }

        private double _ib;
        public double Ib
        {
            get => _ib;
            set => SetProperty(ref _ib, value);
        }

        private double _hb;
        public double Hb
        {
            get => _hb;
            set => SetProperty(ref _hb, value);
        }

        private double _ae;
        public double Ae
        {
            get => _ae;
            set => SetProperty(ref _ae, value);
        }

        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private double _ke;
        public double Ke
        {
            get => _ke;
            set => SetProperty(ref _ke, value);
        }

        private double _yieldMaxCurvature;
        public double YieldMaxCurvature
        {
            get => _yieldMaxCurvature;
            set => SetProperty(ref _yieldMaxCurvature, value);
        }

        private ObservableCollection<double> _yieldMs;
        public ObservableCollection<double> YieldMs
        {
            get => _yieldMs;
            set => SetProperty(ref _yieldMs, value);
        }

        private ObservableCollection<double> _yieldNs;
        public ObservableCollection<double> YieldNs
        {
            get => _yieldNs;
            set => SetProperty(ref _yieldNs, value);
        }

        private ObservableCollection<double> _yieldEpsilon0s;
        public ObservableCollection<double> YieldEpsilon0s
        {
            get => _yieldEpsilon0s;
            set => SetProperty(ref _yieldEpsilon0s, value);
        }

        private ObservableCollection<double> _yieldCurvatures;
        public ObservableCollection<double> YieldCurvatures
        {
            get => _yieldCurvatures;
            set => SetProperty(ref _yieldCurvatures, value);
        }

        private ObservableCollection<double> _ultimateMs;
        public ObservableCollection<double> UltimateMs
        {
            get => _ultimateMs;
            set => SetProperty(ref _ultimateMs, value);
        }

        private ObservableCollection<double> _ultimateNs;
        public ObservableCollection<double> UltimateNs
        {
            get => _ultimateNs;
            set => SetProperty(ref _ultimateNs, value);
        }

        private ObservableCollection<double> _ultimateEpsilon0s;
        public ObservableCollection<double> UltimateEpsilon0s
        {
            get => _ultimateEpsilon0s;
            set => SetProperty(ref _ultimateEpsilon0s, value);
        }

        private ObservableCollection<double> _ultimateCurvatures;
        public ObservableCollection<double> UltimateCurvatures
        {
            get => _ultimateCurvatures;
            set => SetProperty(ref _ultimateCurvatures, value);
        }

        private ObservableCollection<double> _Ns;
        public ObservableCollection<double> Ns
        {
            get => _Ns;
            set => SetProperty(ref _Ns, value);
        }

        private ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> _thetasMs;
        public ObservableCollection<(ObservableCollection<double>, ObservableCollection<double>)> ThetasMs
        {
            get => _thetasMs;
            set => SetProperty(ref _thetasMs, value);
        }

        // PCリングオプション
        private ObservableCollection<string> _pcRingOption = [];
        public ObservableCollection<string> PCRingOption
        {
            get => _pcRingOption;
            set => SetProperty(ref _pcRingOption, value);
        }

        private ObservableCollection<PCRing> _pcRings = [];
        public ObservableCollection<PCRing> PCRings
        {
            get => _pcRings;
            set => SetProperty(ref _pcRings, value);
        }

        private PCRing _pcRing = new();
        public PCRing PCRing
        {
            get => _pcRing;
            set => SetProperty(ref _pcRing, value);
        }

        public CTPConcrete CTPConcrete { get; set; }
        public CTPTensionRebars CTPTensionRebars { get; set; } = new CTPTensionRebars();

        private string _selectedPCRingName;
        public string SelectedPCRingName
        {
            get => _selectedPCRingName;
            set => SetProperty(ref _selectedPCRingName, value);
        }

        private double PileCapFc { get; set; }
        private double PileCapEc { get; set; }

        // パラメータなしコンストラクタ（System.Text.Json デシリアライズ用）。
        // CSV テーブル読込は Load*() を明示的に呼んでもらう。
        public CaptainPile() { }

        // CaptainPileクラス コンストラクタ
        internal CaptainPile(double pileCapFc, double pileCapEc)
        {
            PileCapFc = pileCapFc;
            PileCapEc = pileCapEc;
            LoadPCRingOptions();
            LoadCaptainPileTensionBarPCDs();

            if (D != 0) Update();
        }

        // キャプテンパイル引張鉄筋PCD読み込みメソッド
        private void LoadCaptainPileTensionBarPCDs()
        {
            // 実行ファイルのディレクトリを基準にパスを組み立てる
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string squarePath = Path.Combine(baseDir, "Models", "PileLibrary", "CaptainPileTensionBarPCD_square.csv");
            if (File.Exists(squarePath))
            {
                try
                {
                    CaptainPileTensionBarPCDsSquare = CaptainPileTensionBarPCDLoader.LoadFromCsv(squarePath);
                    foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
                        PCD_squareOption.Add($"{pcdSquare.D}-{pcdSquare.Nu}");
                }
                catch (Exception ex)
                {
                }
            }
            string circlePath = Path.Combine(baseDir, "Models", "PileLibrary", "CaptainPileTensionBarPCD_circle.csv");
            if (File.Exists(circlePath))
            {
                try
                {
                    CaptainPileTensionBarPCDsCircle = CaptainPileTensionBarPCDLoader.LoadFromCsv(circlePath);
                    foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
                        PCD_circleOption.Add($"{pcdCircle.D}-{pcdCircle.Nu}");
                }
                catch (Exception ex)
                {
                }
            }
            //CaptainPileTensionBarPCDsSquare = CaptainPileTensionBarPCDLoader.LoadFromCsv(squarePath);
            //foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
            //{
            //    PCD_squareOption.Add($"{pcdSquare.D}-{pcdSquare.Nu}");
            //}

            //CaptainPileTensionBarPCDsCircle = CaptainPileTensionBarPCDLoader.LoadFromCsv(circlePath);
            //foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
            //{
            //    PCD_circleOption.Add($"{pcdCircle.D}-{pcdCircle.Nu}");
            //}
        }

        // TD, TBの更新メソッド
        public void UpdateTDorTB()
        {
            if (CTPTensionRebars.IsSquareArrangement)
            {
                foreach (var pcdSquare in CaptainPileTensionBarPCDsSquare)
                {
                    if (Math.Abs(D - pcdSquare.D) < NumericalConstants.PCD_COMPARISON_TOLERANCE
                        && Math.Abs(Nu - pcdSquare.Nu) < NumericalConstants.PCD_COMPARISON_TOLERANCE)
                    {
                        CTPTensionRebars.TDorTBmax = pcdSquare.TDorTBmax;
                        break;
                    }
                }
            }
            else if (CTPTensionRebars.IsCircleArrangement)
            {
                foreach (var pcdCircle in CaptainPileTensionBarPCDsCircle)
                {
                    if (Math.Abs(D - pcdCircle.D) < NumericalConstants.PCD_COMPARISON_TOLERANCE
                        && Math.Abs(Nu - pcdCircle.Nu) < NumericalConstants.PCD_COMPARISON_TOLERANCE)
                    {
                        CTPTensionRebars.TDorTBmax = pcdCircle.TDorTBmax;
                        break;
                    }
                }
            }
        }

        private void LoadPCRingOptions()
        {
            // 実行ファイルのディレクトリを基準にパスを組み立てる
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string filePath = Path.Combine(baseDir, "Models", "PileLibrary", "PCRing.csv");
            PCRings = PCRingLoader.LoadFromCsv(filePath);

            foreach (var pcRing in PCRings)
            {
                PCRingOption.Add($"{pcRing.Name}");
            }
        }

        // CaptainPileクラス 更新メソッド
        public void Update()
        {
            CTPConcrete = new(PileCapFc, Nu, D);
            CTPTensionRebars.Update();
            SetBasicProperties(PCRing, PileCapEc);

            (Ns, ThetasMs) = GetNMThetaRelationship(); // N、M、θ関係
        }

        //N、θ、M関係を返すメソッド
        private (ObservableCollection<double>, ObservableCollection<(
            ObservableCollection<double>, ObservableCollection<double>)
            >) GetNMThetaRelationship()
        {
            ObservableCollection<double> Ns = [];
            ObservableCollection<
                (ObservableCollection<double>, ObservableCollection<double>)> thetasMs = [];
            int nNum = 10;
            double NMin = GetNMin();
            double NMax = GetNMax();
            double N;

            for (int i = 0; i <= nNum; i++)
            {
                N = NMin + (NMax - NMin) * i / nNum;
                thetasMs.Add(GetMThetaRelationship(N));
                Ns.Add(N);
            }
            return (Ns, thetasMs);
        }

        // ある軸力に対するθ, M関係を返すメソッド
        internal (ObservableCollection<double>, ObservableCollection<double>)
            GetMThetaRelationship(double N)
        {
            ObservableCollection<double> thetas = [];
            ObservableCollection<double> Ms = [];

            double Mu = GetMu(N);
            (double My, double curvatureY) = GetMyCurvatureY(N);
            double thetaY = curvatureY * D * Nu;
            double thetaU = 0.04;

            if (0 <= N) // 圧縮
            {
                double M1 = GetM1(N);
                double theta1;
                if (Ke != 0)
                {
                    theta1 = M1 / Ke;
                }
                else
                {
                    theta1 = 0;
                }

                double thetaYD = Math.Min(GetThetaYDonCompressionSide(theta1, thetaY, Mu, My, M1), thetaU);

                Ms.Add(0.0);
                thetas.Add(0.0);

                Ms.Add(Math.Min(M1, Mu));
                thetas.Add(theta1);

                if (thetaYD != 0)
                {
                    Ms.Add(Mu);
                    thetas.Add(thetaYD);
                }

                Ms.Add(Mu);
                thetas.Add(thetaU);

            }
            else //if (N < 0) 引張
            {
                double thetaYD = Math.Min(GetThetaYDonTensionSide(My, Mu, thetaY), thetaU);
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
            if (thetaY - theta1 > 0)
            {
                double K2 = Math.Min(Math.Abs((My - M1) / (thetaY - theta1)), Ke);
                return theta1 + (Mu - M1) / K2;
            }
            else
            {
                return 0;
            }
        }

        // 引張側のθ、yを得るメソッド
        private static double GetThetaYDonTensionSide(double My, double Mu, double thetaY)
        {
            if (My != 0)
            {
                double K2 = My / thetaY;
                return Mu / K2;
            }
            else
            {
                return 0;
            }
        }

        // 最大圧縮軸力を返すメソッド
        private double GetNMax()
        {
            return 0.85 * CTPConcrete.SigmaMax * Ae;
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

        // 降伏時曲げモーメントMyと降伏時曲率を返すメソッド
        internal (double, double) GetMyCurvatureY(double NTarget)
        {
            if (YieldNs != null && YieldNs.Count > 1)
            {
                double N = 0.0, M = 0.0;
                double curvature = 1.0e-6;
                double deltaCurvature = curvature / 500.0;

                double x = -CTPConcrete.D * Nu * 0.5;
                double epsilon = CTPConcrete.Epsilon085;

                int seg = -1;
                for (int i = 1; i < YieldNs.Count; i++)
                {
                    if (NTarget <= YieldNs[i])
                    {
                        seg = i;
                        break;
                    }
                }
                if (seg == -1) return (0.0, 0.0); // 範囲外

                double factor1 = (YieldNs[seg] - NTarget) / (YieldNs[seg] - YieldNs[seg - 1]);
                double factor2 = 1.0 - factor1;

                N = YieldNs[seg - 1] * factor1 + YieldNs[seg] * factor2;
                M = YieldMs[seg - 1] * factor1 + YieldMs[seg] * factor2;
                curvature = YieldCurvatures[seg - 1] * factor1 + YieldCurvatures[seg] * factor2;
                deltaCurvature = Math.Max(1e-12, (YieldCurvatures[seg] - YieldCurvatures[seg - 1]) / 100.0);

                // 圧縮/引張側の判定ロジックは現仕様を踏襲
                if (seg < YieldNs.Count * 0.5 && CTPTensionRebars.HasTensionRebars)
                {
                    x = CTPTensionRebars.TDorTB * 0.5;
                    epsilon = -CTPTensionRebars.SigmaY / CTPTensionRebars.Es;
                }
                else
                {
                    x = -CTPConcrete.D * Nu * 0.5;
                    epsilon = CTPConcrete.Epsilon085;
                }

                // Newton反復
                int maxIter = 200;
                for (int iter = 0; iter < maxIter && Math.Abs(N - NTarget) > 0.1; iter++)
                {
                    double epsilon0 = GetEpsilon0(curvature, x, epsilon);
                    double N1 = GetForceAndMoment(epsilon0, curvature + deltaCurvature).Item1;

                    double dNdPhi = (N1 - N) / deltaCurvature;
                    if (Math.Abs(dNdPhi) < 1e-12) break;

                    curvature += (NTarget - N) / dNdPhi;

                    epsilon0 = GetEpsilon0(curvature, x, epsilon);
                    var result = GetForceAndMoment(epsilon0, curvature);
                    N = result.Item1; M = result.Item2;
                }
                return (M, curvature);
            }
            return (0.0, 0.0);
        }
        

        // 終局時曲げモーメントMuを返すメソッド
        internal double GetMu(double NTarget)
        {
            if (UltimateNs != null && UltimateNs.Count > 1)
            {
                double N = 0.0, M = 0.0;
                double curvature = 1.0e-6;
                double deltaCurvature = curvature / 500.0;

                int seg = -1;
                for (int i = 1; i < UltimateNs.Count; i++)
                {
                    if (NTarget <= UltimateNs[i]) { seg = i; break; }
                }
                if (seg <= 0) return 0.0;

                double factor1 = (UltimateNs[seg] - NTarget) / (UltimateNs[seg] - UltimateNs[seg - 1]);
                double factor2 = 1.0 - factor1;

                N = UltimateNs[seg - 1] * factor1 + UltimateNs[seg] * factor2;
                M = UltimateMs[seg - 1] * factor1 + UltimateMs[seg] * factor2;
                curvature = UltimateCurvatures[seg - 1] * factor1 + UltimateCurvatures[seg] * factor2; // ← 修正
                deltaCurvature = Math.Max(1e-12, (UltimateCurvatures[seg] - UltimateCurvatures[seg - 1]) / 100.0);

                int maxIter = 200;
                for (int iter = 0; iter < maxIter && Math.Abs(N - NTarget) > 0.1; iter++)
                {
                    double epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * Nu * 0.5, CTPConcrete.EpsilonB);
                    double N1 = GetForceAndMoment(epsilon0, curvature + deltaCurvature).Item1;

                    double dNdPhi = (N1 - N) / deltaCurvature;
                    if (Math.Abs(dNdPhi) < 1e-12) break;

                    curvature += (NTarget - N) / dNdPhi;

                    epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * Nu * 0.5, CTPConcrete.EpsilonB);
                    var result = GetForceAndMoment(epsilon0, curvature);
                    N = result.Item1; M = result.Item2;
                }
                return M;
            }
            return 0.0;
        }

        // 基本属性をセットするメソッド
        internal void SetBasicProperties(PCRing _pcring, double pileCapEc)
        {
            PCRing = _pcring;
            Ep = 40_000; // kui ni change
            Ip = Math.PI * Math.Pow(Nu * PCRing.D, 4) / 64.0;
            if (PCRing.D < 2100)
            { Hp = 90.0; }
            else
            { Hp = 110.0; }

            Ec = pileCapEc;
            Ic = Math.PI * Math.Pow(PCRing.RD1, 4) / 64.0;
            Hc = PCRing.Hr - Hp; ///

            Eb = Ec;
            Ib = Ic; ///
            Hb = PCRing.D * 0.5;

            double Kp = GetK(Ep, Ip, Hp);
            double Kc = GetK(Ec, Ic, Hc);
            double Kb = GetK(Eb, Ib, Hb);
            Ke = GetKe(Kp, Kc, Kb); // 初期剛性
            //Nu = contractionRatio; // 絞り係数

            Ae = Math.PI * Math.Pow(Nu * PCRing.D, 2.0) / 4.0;
            Z = Math.PI * Math.Pow(Nu * PCRing.D, 3.0) / 32.0;

            if (CTPTensionRebars.HasTensionRebars) // 引張定着筋がある場合
            {
                YieldMaxCurvature = GetAllowableMaxCurvature();
                var result01 = GetAllowableMNInteraction(YieldMaxCurvature);
                (YieldNs, YieldMs, YieldEpsilon0s, YieldCurvatures) = (result01.Item1, result01.Item2, result01.Item3, result01.Item4);
            }
            else // 引張定着筋がない場合
            {
                var result02 = GetUltimateMNInteraction(CTPConcrete.GetEpsilon(0.85 * CTPConcrete.SigmaMax));
                (YieldNs, YieldMs, YieldEpsilon0s, YieldCurvatures) = (result02.Item1, result02.Item2, result02.Item3, result02.Item4);
            }
            var result03 = GetUltimateMNInteraction(0.003);
            (UltimateNs, UltimateMs, UltimateEpsilon0s, UltimateCurvatures) = (result03.Item1, result03.Item2, result03.Item3, result03.Item4);
        }

        // 杭体、PCリング内コンクリート、パイルキャップ部分の回転剛性
        internal static double GetK(double E, double I, double H)
        {
            return E * I / H;
        }

        // 等価初期回転剛性を得るメソッド
        internal static double GetKe(double Kp, double Kc, double Kb)
        {
            return 1.0 / (1.0 / Kp + 1.0 / Kc + 1.0 / Kb);
        }

        // ε0を得るメソッド
        internal static double GetEpsilon0(double curvature, double x, double epsilon)
        {
            return x * curvature + epsilon;
        }

        // εCを得るメソッド
        internal double GetEpsilonC(double curvature, double x, double epsilon)
        {
            return (CTPConcrete.D * Nu * 0.5 + x) * curvature + epsilon;
        }

        // 最大許容曲率を返すメソッド
        internal double GetAllowableMaxCurvature()
        {
            double maxAllowableCurvature = double.MaxValue;
            if (CTPTensionRebars.HasTensionRebars)
            {
                maxAllowableCurvature = (CTPConcrete.Epsilon085 + CTPTensionRebars.EpsilonY) / (CTPConcrete.D * Nu * 0.5 + CTPTensionRebars.TDorTB * 0.5);
            }
            return maxAllowableCurvature;
        }

        // 軸力、曲げモーメント取得メソッド
        internal (double, double, double, double) GetForceAndMoment(double epsilon0, double curvature)
        {
            double N;
            double M;
            CTPCircularSolidSection circularSolidSection = new(CTPConcrete.D * Nu);

            (N, M) = circularSolidSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
            if (CTPTensionRebars.HasTensionRebars)
            {
                if (CTPTensionRebars.IsCircleArrangement)
                {
                    CTPCircularDotSection circularDotSection = new(CTPTensionRebars.TDorTB, CTPTensionRebars.SelectedBarNumberCircle, CTPTensionRebars.BarArea);

                    var result1 = circularDotSection.GetForceAndMoment(CTPTensionRebars, epsilon0, curvature);
                    var result2 = circularDotSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
                    N += result1.Item1 - result2.Item1;
                    M += result1.Item2 - result2.Item2;
                }

                else
                {
                    CTPSquareDotSection squareDotSection = new(CTPTensionRebars.TDorTB, CTPTensionRebars.SelectedBarNumberSquare, CTPTensionRebars.BarArea);

                    var result1 = squareDotSection.GetForceAndMoment(CTPTensionRebars, epsilon0, curvature);
                    var result2 = squareDotSection.GetForceAndMoment(CTPConcrete, epsilon0, curvature);
                    N += result1.Item1 - result2.Item1;
                    M += result1.Item2 - result2.Item2;
                }

            }
            return (N, M, epsilon0, curvature);
        }

        // 使用損傷限界MNインタラクション取得メソッド
        //internal (ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>) GetAllowableMNInterection(double maxCurvature)
        //{
        //    ObservableCollection<double> axialForces = [];
        //    ObservableCollection<double> bendingMoments = [];
        //    ObservableCollection<double> epsilonCs = [];
        //    ObservableCollection<double> curvatures = [];
        //    double epsilon0;
        //    double curvature;
        //    double epsilonC;

        //    for (int i = 0; i <= DivisionNum; i++)
        //    {
        //        curvature = maxCurvature * i / DivisionNum;
        //        if (CTPTensionRebars.HasTensionRebars == true)
        //        {
        //            epsilonC = GetEpsilonC(curvature, CTPTensionRebars.TDorTB * 0.5, -CTPTensionRebars.EpsilonY);
        //            epsilon0 = GetEpsilon0(curvature, CTPTensionRebars.TDorTB * 0.5, -CTPTensionRebars.EpsilonY);
        //        }
        //        else
        //        {
        //            epsilonC = GetEpsilonC(curvature, -CTPConcrete.D * 0.5, 0.0);
        //            epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * 0.5, 0.0);
        //        }
        //        var result = GetForceAndMoment(epsilon0, curvature); // 引張側 純引張～
        //        axialForces.Add(result.Item1);
        //        bendingMoments.Add(result.Item2);
        //        epsilonCs.Add(epsilonC);
        //        curvatures.Add(curvature);
        //    }

        //    for (int i = DivisionNum; i >= 0; i--)
        //    {
        //        curvature = maxCurvature * i / DivisionNum;
        //        epsilonC = CTPConcrete.Epsilon085;
        //        epsilon0 = CTPConcrete.Epsilon085 - curvature * CTPConcrete.D * 0.5 * CTPConcrete.Nu;
        //        var result = GetForceAndMoment(epsilon0, curvature); // 圧縮側 ～純圧縮
        //        axialForces.Add(result.Item1);
        //        bendingMoments.Add(result.Item2);
        //        epsilonCs.Add(epsilonC);
        //        curvatures.Add(curvature);
        //    }
        //    return (axialForces, bendingMoments, epsilonCs, curvatures);
        //}
                internal (ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>) GetAllowableMNInteraction(double maxCurvature)
        {
            int n = DivisionNum + 1;
            var axialA = new double[n];
            var bendingA = new double[n];
            var epsilonCsA = new double[n];
            var curvA = new double[n];

            // 引張側〜中立側（ ascending ）
            Parallel.For(0, n, i =>
            {
                double curvature = maxCurvature * i / DivisionNum;
                double epsilonC, epsilon0;
                if (CTPTensionRebars.HasTensionRebars)
                {
                    epsilonC = GetEpsilonC(curvature, CTPTensionRebars.TDorTB * 0.5, -CTPTensionRebars.EpsilonY);
                    epsilon0 = GetEpsilon0(curvature, CTPTensionRebars.TDorTB * 0.5, -CTPTensionRebars.EpsilonY);
                }
                else
                {
                    epsilonC = GetEpsilonC(curvature, -CTPConcrete.D * 0.5, 0.0);
                    epsilon0 = GetEpsilon0(curvature, -CTPConcrete.D * 0.5, 0.0);
                }
                var res = GetForceAndMomentCached(epsilon0, curvature);
                axialA[i] = res.N;
                bendingA[i] = res.M;
                epsilonCsA[i] = epsilonC;
                curvA[i] = curvature;
            });

            // 圧縮側（descending を保持するため index を反転して計算）
            var axialB = new double[n];
            var bendingB = new double[n];
            var epsilonCsB = new double[n];
            var curvB = new double[n];

            Parallel.For(0, n, i =>
            {
                int idx = DivisionNum - i; // 逆順
                double curvature = maxCurvature * idx / DivisionNum;
                double epsilonC = CTPConcrete.Epsilon085;
                double epsilon0 = CTPConcrete.Epsilon085 - curvature * CTPConcrete.D * 0.5 * CTPConcrete.Nu;
                var res = GetForceAndMomentCached(epsilon0, curvature);
                axialB[i] = res.N;
                bendingB[i] = res.M;
                epsilonCsB[i] = epsilonC;
                curvB[i] = curvature;
            });

            // 結果を ObservableCollection にまとめる（元の順序に合わせる）
            var axialList = new ObservableCollection<double>();
            var bendingList = new ObservableCollection<double>();
            var epsList = new ObservableCollection<double>();
            var curvList = new ObservableCollection<double>();

            for (int i = 0; i < n; i++)
            {
                axialList.Add(axialA[i]);
                bendingList.Add(bendingA[i]);
                epsList.Add(epsilonCsA[i]);
                curvList.Add(curvA[i]);
            }
            for (int i = 0; i < n; i++)
            {
                axialList.Add(axialB[i]);
                bendingList.Add(bendingB[i]);
                epsList.Add(epsilonCsB[i]);
                curvList.Add(curvB[i]);
            }

            return (axialList, bendingList, epsList, curvList);
        }

        // 安全限界MN インタラクション取得メソッド
        //internal (ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>) GetUltimateMNInteraction(double maxEpsilonC)
        //{
        //    ObservableCollection<double> axialForces = [];
        //    ObservableCollection<double> bendingMoments = [];
        //    ObservableCollection<double> epsilonCs = [];
        //    ObservableCollection<double> curvatures = [];
        //    double epsilonC;
        //    double epsilon0;
        //    double curvature;
        //    double maxCurvature;

        //    if (PCRing.D != 0)　// 0除算回避
        //    {
        //        maxCurvature = (0.003 + 0.0025) * 20.0 / (PCRing.D * Nu);
        //    }
        //    else
        //    {
        //        maxCurvature = 0;
        //    }

        //    for (int i = 0; i <= DivisionNum * 2; i++)
        //    {
        //        if (i == 0) // 純引張
        //        {
        //            epsilon0 = -0.006;
        //            epsilonC = -0.006;
        //            curvature = 0.0;
        //        }
        //        else if (i != DivisionNum * 2)
        //        {
        //            curvature = maxCurvature * (DivisionNum * 2 - i) / (DivisionNum * 2);
        //            epsilon0 = maxEpsilonC - curvature * CTPConcrete.D * 0.5 * CTPConcrete.Nu;

        //            epsilonC = maxEpsilonC;
        //        }
        //        else // i = = DivisionNum * 2 // 純圧縮
        //        {
        //            epsilon0 = maxEpsilonC;
        //            epsilonC = maxEpsilonC;
        //            curvature = 0.0;
        //        }

        //        var result = GetForceAndMoment(epsilon0, curvature); // 引張側 純引張～
        //        axialForces.Add(result.Item1);
        //        bendingMoments.Add(result.Item2);
        //        epsilonCs.Add(epsilonC);
        //        curvatures.Add(curvature);
        //    }
        //    return (axialForces, bendingMoments, epsilonCs, curvatures);
        //}
        internal (ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>, ObservableCollection<double>) GetUltimateMNInteraction(double maxEpsilonC)
        {
            int n = DivisionNum * 2 + 1;
            var axial = new double[n];
            var bending = new double[n];
            var epsilonCs = new double[n];
            var curvatures = new double[n];

            double maxCurvature;
            if (PCRing.D != 0) // 0除算回避
            {
                maxCurvature = (0.003 + 0.0025) * 20.0 / (PCRing.D * Nu);
            }
            else
            {
                maxCurvature = 0;
            }

            // 各インデックスは独立なので Parallel.For で並列計算
            Parallel.For(0, n, i =>
            {
                double epsilon0;
                double epsilonC;
                double curvature;

                if (i == 0) // 純引張
                {
                    epsilon0 = -0.006;
                    epsilonC = -0.006;
                    curvature = 0.0;
                }
                else if (i != n - 1)
                {
                    // オリジナルの式を保つ：curvature は減少方向
                    curvature = maxCurvature * (DivisionNum * 2 - i) / (double)(DivisionNum * 2);
                    epsilon0 = maxEpsilonC - curvature * CTPConcrete.D * 0.5 * CTPConcrete.Nu;
                    epsilonC = maxEpsilonC;
                }
                else // 純圧縮
                {
                    epsilon0 = maxEpsilonC;
                    epsilonC = maxEpsilonC;
                    curvature = 0.0;
                }

                // キャッシュ済み計算を利用
                var res = GetForceAndMomentCached(epsilon0, curvature);
                axial[i] = res.N;
                bending[i] = res.M;
                epsilonCs[i] = epsilonC;
                curvatures[i] = curvature;
            });

            // 結果を ObservableCollection に詰め替え（UI スレッドでの利用を想定）
            var axialList = new ObservableCollection<double>(axial);
            var bendingList = new ObservableCollection<double>(bending);
            var epsList = new ObservableCollection<double>(epsilonCs);
            var curvList = new ObservableCollection<double>(curvatures);

            return (axialList, bendingList, epsList, curvList);
        }
    }

    // CaptainPile材料抽象クラス
    public abstract class CTPMaterial : BaseModel
    {
        public abstract double GetStress(double epsilon);
    }

    // CaptainPileコンクリートクラス
    public class CTPConcrete : CTPMaterial
    {
        internal double D { get; set; }
        internal double PileCapFc { get; set; }
        internal double Nu { get; set; }
        internal double EpsilonB { get; set; }
        internal double SigmaMax { get; set; }
        internal double Epsilon085 { get; set; }

        // CaptainPileコンクリートクラスコンストラクタ
        public CTPConcrete(double pileCapFc, double nu, double d)
        {
            D = d;
            Nu = nu;
            PileCapFc = pileCapFc;
            SigmaMax = Math.Min(PileCapFc / Math.Pow(Nu, 2.0), 2 * PileCapFc);
            EpsilonB = 0.003;
            Epsilon085 = GetEpsilon(0.85 * SigmaMax);
        }

        // CaptainPileコンクリートクラス ストレス取得メソッド
        public override double GetStress(double epsilon)
        {
            double sigma;
            if (epsilon <= 0)
            {
                sigma = 0.0;
            }
            else if (0 < epsilon && epsilon < EpsilonB)
            {
                sigma = 6.75 * (Math.Exp(-0.812 * (epsilon / EpsilonB)) - Math.Exp(-1.218 * (epsilon / EpsilonB))) * SigmaMax;
            }
            else
            {
                sigma = SigmaMax;
            }
            return sigma;
        }

        // CaptainPileコンクリートクラス dσ/dε取得メソッド
        public double GetDSigmaOnDEpsilon(double epsilon)
        {
            return 6.75 * (-0.812 / EpsilonB * Math.Exp(-0.812 * (epsilon / EpsilonB)) - (-1.218) / EpsilonB * Math.Exp(-1.218 * (epsilon / EpsilonB))) * SigmaMax;
        }

        // CaptainPileコンクリートクラス ε0取得メソッド
        internal double GetEpsilon(double targetSigma)
        {
            //double sigma = double.MaxValue;
            //double epsilon = 0.003 / 100.0;
            //double _DSigmaonDEpsilon;

            //while (Math.Abs(targetSigma - sigma) > 0.003 / 1000.0)
            //{
            //    sigma = GetStress(epsilon);
            //    _DSigmaonDEpsilon = GetDSigmaonDEpsilon(epsilon);
            //    epsilon += (targetSigma - sigma) / _DSigmaonDEpsilon;
            //}
            //return epsilon;
            double sigma = double.MaxValue;
            double epsilon = 0.003 / 100.0;
            int iter = 0;
            const int maxIter = 200;
            while (Math.Abs(targetSigma - sigma) > 0.003 / 1000.0 && iter++ < maxIter)
            {
                sigma = GetStress(epsilon);
                double dSigma = GetDSigmaOnDEpsilon(epsilon);
                if (Math.Abs(dSigma) < 1e-12) break;
                epsilon += (targetSigma - sigma) / dSigma;
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

        private double _TDorTBmax;
        public double TDorTBmax
        {
            get => _TDorTBmax;
            set => SetProperty(ref _TDorTBmax, value);
        }

        private double _TDorTB;
        public double TDorTB
        {
            get => _TDorTB;
            set => SetProperty(ref _TDorTB, value);
        }

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
        public string[] TensionAnchorGradeOption { get; } = ["SD390", "SD490", "SD685"];

        // 引張鉄筋径
        public string[] TensionAnchorDiaOption { get; } = ["D38", "D41",];

        // 円形配置引張鉄筋本数オプション
        public int[] CaptainPileTensionBarNumberOption_circle { get; set; } = [6, 8, 10, 12, 16, 20];

        // 正方形配置引張鉄筋本数オプション
        public int[] CaptainPileTensionBarNumberOption_square { get; set; } = [4, 8, 12, 16, 20];

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

        // キャプテンパイル引張鉄筋クラス コンストラクタ
        public CTPTensionRebars() // bool hasTensionRebars, string grade, string barSize, int barNum, bool isCircleArrangement, int _TDorTB)
        {
            Update();
        }

        // キャプテンパイル引張鉄筋クラス 更新メソッド
        public void Update()
        {
            if (SelectedTensionAnchorGrade == "SD390")
            { SigmaY = 390; }
            else if (SelectedTensionAnchorGrade == "SD490")
            { SigmaY = 490; }
            else if (SelectedTensionAnchorGrade == "SD685")
            { SigmaY = 685; }

            EpsilonY = SigmaY / Es;

            if (SelectedTensionAnchorDia == "D38")
            { BarArea = 1140.0; }
            else if (SelectedTensionAnchorDia == "D41")
            { BarArea = 1340.0; }

            if (IsSquareArrangement)
            {
                Ag = BarArea * SelectedBarNumberSquare;
            }
            else if (IsCircleArrangement)
            {
                Ag = BarArea * SelectedBarNumberCircle;
            }
        }

        // キャプテンパイル引張鉄筋クラス ストレス取得メソッド
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

    // キャプテンパイル断面抽象クラス
    internal abstract class CTPSection { }

    // 円形断面クラス
    internal class CTPCircularSolidSection : CTPSection
    //{
        //private double Dia { get; }

        //// 円形断面クラス コンストラクタ
        //internal CTPCircularSolidSection(double diameter)
        {
        private double Dia { get; }
        private readonly int _division;
        private readonly double[] _z;
        private readonly double[] _width;
        private readonly double _dz;

        // コンストラクタ: ノード幅を事前計算してキャッシュ
        internal CTPCircularSolidSection(double diameter, int division = 100)
        {
            Dia = diameter;
            _division = Math.Max(1, division);
            _dz = Dia / _division;
            _z = new double[_division];
            _width = new double[_division];
            double half = Dia * 0.5;
            double half2 = half * half;
            for (int i = 0; i < _division; i++)
            {
                _z[i] = -half + (0.5 + i) * _dz;
                // 数学的オーバーフローを防ぐ
                double tmp = half2 - _z[i] * _z[i];
                _width[i] = tmp <= 0.0 ? 0.0 : 2.0 * Math.Sqrt(tmp);
            }
        }

        // 軸力・曲げモーメント計算（事前計算値を利用）
        internal (double, double) GetForceAndMoment(CTPMaterial material, double epsilon0, double curvature)
        {
            double axialForce = 0.0;
            double bendingMoment = 0.0;

            for (int i = 0; i < _division; i++)
            {
                double epsilon = epsilon0 - curvature * _z[i];
                double sigma = material.GetStress(epsilon);
                double contrib = _width[i] * sigma * _dz;
                axialForce += contrib;
                bendingMoment += contrib * -_z[i];
            }
            return (axialForce, bendingMoment);
        }
    }
    //    {
    //        Dia = diameter;
    //    }

    //    // 軸力、曲げモーメント取得メソッド
    //    internal (double, double) GetForceAndMoment(CTPMaterial material, double epsilon0, double curvature, int division = 100)
    //    {
    //        double z;
    //        double dz = Dia / division;
    //        double epsilon;
    //        double sigma;
    //        double width;

    //        double axialForce = 0.0;
    //        double bendingMoment = 0.0;

    //        // 圧縮縁ひずみ度 epsilonC
    //        // 中心ひずみ度 epsilon0
    //        for (int i = 0; i < division; i++)
    //        {
    //            z = -Dia * 0.5 + (0.5 + i) * dz;
    //            width = 2.0 * Math.Sqrt(Math.Pow(Dia * 0.5, 2) - Math.Pow(z, 2));
    //            epsilon = epsilon0 - curvature * z;
    //            sigma = material.GetStress(epsilon);
    //            axialForce += width * sigma * dz;
    //            bendingMoment += width * sigma * dz * -z;
    //        }
    //        return (axialForce, bendingMoment);
    //    }
    //}

    // 正方形配置　点断面クラス
    class CTPSquareDotSection(double _TB, int numDot, double area) : CTPSection
    {
        private double TB { get; } = _TB;
        private double NumDot { get; } = numDot;
        private double Area { get; } = area;

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
            for (int i = 0; i < NumDot / 4.0 + 1; i++)
            {
                z = -TB * 0.5 + TB * i / (NumDot / 4.0);
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(epsilon);
                if (i == 0 || i == NumDot / 4.0)
                {
                    axialForce += sigma * Area * (NumDot / 4.0 + 1);
                    bendingMoment += sigma * Area * (NumDot / 4.0 + 1) * -z;
                }
                else
                {
                    axialForce += sigma * Area;
                    bendingMoment += sigma * Area * -z;
                }
            }
            return (axialForce, bendingMoment);
        }
    }

    // 円形配置　点断面クラス
    internal class CTPCircularDotSection(double _TD, int numDot, double area) : CTPSection
    {
        private double TD { get; } = _TD;
        private double NumDot { get; } = numDot;
        private double Area { get; } = area;

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
                z = TD * 0.5 * Math.Cos(2.0 * Math.PI * i / NumDot);
                epsilon = epsilon0 - curvature * z;
                sigma = material.GetStress(epsilon);
                axialForce += sigma * Area;
                bendingMoment += sigma * Area * -z;
            }
            return (axialForce, bendingMoment);
        }
    }
}

