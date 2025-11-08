using PileDesignCore.PileLibrary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PileDesignCore
{
    // FTパイル引張鉄筋クラス
    public class FTPileTensionBars : BaseViewModel
    {
        // 引張鉄筋
        public ObservableCollection<string> TensionAnchorGradeOption { get; private set; } = new ObservableCollection<string>()
        {
            "φ11, SBPR785/1030",
            "φ11, SBPR1080/1230",
        };

        public double Ft { get; private set; } // 鋼棒の短期許容応力度(N/mm2)
        public double Fu { get; private set; } // 鋼棒の引張耐力（N/mm2）
        public double Ag { get; private set; } // 引抜き抵抗用鋼棒の全断面積（mm2）
        public double Ag1 { get; private set; } // 引抜き抵抗用鋼棒の全断面積（mm2）
        public double Es { get; } = 205_000.0; // 鋼棒の弾性係数（N/mm2）
        public double Ls { get; } = 600.0; // 鋼棒の有効長さ（m）600 - 2000
        public double Ds { get; } = 500.0;// 引抜き抵抗用鋼棒の配置距離（m）
        public double EpsilonA { get; private set; } // 鋼棒の許容ひずみ
        public double DeltaA { get; private set; } // 鋼棒の許容伸び(m)
        public double ThetaAS { get; private set; }
        public int TensionAnchorNum { get; set; } = 8; // 引抜き抵抗用鋼棒の配置距離（m）
        public string SelectedTensionAnchorGrade { get; set; } = "φ11, SBPR1080/1230";// 引抜き抵抗用鋼棒の配置距離（m）
        public bool IsTensionType { get; set; } = false;


        // コンストラクタ
        public FTPileTensionBars() //bool isTensionType, int tensionAnchorNum, double ds, double ls)
        {
            //IsTensionType = isTensionType;
            //Ls = ls;
            //Ds = ds;
            //TensionAnchorNum = tensionAnchorNum;
            //SelectedTensionAnchorGrade = tensionAnchorGrade;
            //if (SelectedTensionAnchorGrade == "φ11, SBPR785/1030")
            //{
            //    Ft = 785.0;
            //    Fu = 1030.0;
            //    Ag1 = 95.03;
            //}
            //else if(SelectedTensionAnchorGrade == "φ11, SBPR1080/1230")
            //{
            //    Ft = 1080.0;
            //    Fu = 1230.0;
            //    Ag1 = 95.03;
            //}
            //Ag = Ag1 * TensionAnchorNum;
            //EpsilonA = Ft / Es;
            //DeltaA = EpsilonA * Ls;
            //ThetaAS = DeltaA / Ds;
            Update();
        }

        // アップデートメソッド
        public void Update()
        {
            if (SelectedTensionAnchorGrade == "φ11, SBPR785/1030")
            {
                Ft = 785.0;
                Fu = 1030.0;
                Ag1 = 95.03;
            }
            else if (SelectedTensionAnchorGrade == "φ11, SBPR1080/1230")
            {
                Ft = 1080.0;
                Fu = 1230.0;
                Ag1 = 95.03;
            }
            Ag = Ag1 * TensionAnchorNum;
            EpsilonA = Ft / Es;
            DeltaA = EpsilonA * Ls;
            ThetaAS = DeltaA / Ds;
        }
    }

    // FTパイルキャップクラス
    public class FTPileCap : BaseViewModel
    {
        public double Fc { get; private set; } = 24.0;// パイルキャップのコンクリートの設計基準強度(N/mm2)
        public double E { get; private set; } = 33_500.0 * Math.Pow(23.0 / 24.0, 2.0) * Math.Pow((24.0 / 60), 1.0 / 3.0); // パイルキャップコンクリートの弾性係数（N/mm2）
        public double Density { get; private set; } = 23.0;
        public double Nu { get; private set; } = 0.2; // パイルキャップコンクリートのポアソン比
        public double Ac { get; private set; } // 支承面積（パイルキャップ面積）(m2)

        // コンストラクタ
        public FTPileCap() //double fc, double ac)
        {
            //Fc = fc;
            //Nu = 0.2;
            //double gamma = GetGamma(Fc);
            //E = GetE(Fc, gamma);
            //Ac = ac;
            Update();
        }

        public void Update()
        {
            Density = GetGamma(Fc);
            E = GetE(Fc, Density);
        }

        private static double GetGamma(double fc)
        {
            if (fc <= 36) { return 23.0; }
            else if (fc <= 48) { return 23.5; }
            else { return 24.0; }
        }

        // ヤング係数を得るメソッド
        private static double GetE(double fc, double gamma = 23.0)
        {
            return 33_500.0 * Math.Pow(gamma / 24.0, 2.0) * Math.Pow((fc / 60.0), 1.0 / 3.0);
        }
    }

    // FTパイル杭クラス
    public class FTPilePile : BaseViewModel
    {
        public double D1 { get; private set; } = 600.0; // 杭の外径
        public double D2 { get; private set; } = 400.0; // 杭の内径
        public double Ap { get; private set; } // 杭頭面積（m2）

        public FTPilePile()//double d1, double d2)
        {
            //D1 = d1;
            //D2 = d2;
            Update();
        }

        public void Update()
        {
            Ap = Math.PI * (Math.Pow(D1, 2) - Math.Pow(D2, 2)) / 4.0;
        }
    }

    // FTパイルクラス
    public class FTPile : BaseViewModel
    {
        public FTPilePile FTPilePile { get; set; }= new FTPilePile();
        public FTPileCap FTPileCap { get; set; } = new FTPileCap();
        public FTPileTensionBars FTPileTensionBars { get; set; } = new FTPileTensionBars();
        public FTCap FTCap = new FTCap();
        public double PhiC { get; private set; } // 支圧係数
        public double K0 { get; private set; } // 杭頭接合部の初期回転剛性
        public List<double> Ns { get; private set; }
        public List<(List<double>, List<double>)> ThetasMs { get; private set; }

        // コンストラクタ
        public FTPile()//FTPilePile ftPilePile, FTPileCap ftPileCap, FTPileTensionBars ftPileTensionBars)
        {

            //PhiC = Math.Min(Math.Sqrt(FTPileCap.Ac / FTPilePile.Ap), 4.0); // 支圧係数
            //(Ns, ThetasMs) = GetNMThetaRelationship(); // N、M、θ関係
            //Update();
            
        }

        private void Update()
        {
            PhiC = Math.Min(Math.Sqrt(FTPileCap.Ac / FTPilePile.Ap), 4.0); // 支圧係数
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
            int DivNum = 100;

            double thetaC = GetThetaC(N);
            double thetaMax = GetThetaAc(N);
            if (FTPileTensionBars.IsTensionType)
            {
                thetaMax = Math.Min(GetThetaAc(N), FTPileTensionBars.ThetaAS);
            }
            
            for (int i = 0; i <= DivNum; i++)
            {
                thetas.Add(thetaC + (thetaMax - thetaC) * i / DivNum);
                Ms.Add(GetMoment(thetas[i], N));
            }
            return (thetas, Ms);
        }

        // 最大圧縮軸力を返すメソッド
        private double GetNMax()
        {
            return 3.0 / 5.0 * PhiC * FTPilePile.Ap;
        }

        // 最大引張軸力を返すメソッド
        private double GetNMin()
        {
            if (FTPileTensionBars.IsTensionType)
            {
                return -FTPileTensionBars.Ag * FTPileTensionBars.Fu;
            }
            else
            {
                return 0.0;
            }
        }

        // 許容回転角を返すメソッド
        private double GetThetaA(double N)
        {
            double thetaA = Math.Min(GetThetaAc(N), FTPileTensionBars.ThetaAS);
            return thetaA;
        }

        // コンクリートの圧縮合力を返すメソッド
        private double GetNc(double N, double M)
        {
            double Nc;
            if (FTPileTensionBars.IsTensionType)
            {
                Nc = N;
            }
            else
            {
                if (N <= 0 && Math.Abs(N / 2) > Math.Abs(M / FTPileTensionBars.Ds))
                {
                    Nc = 0;
                }
                else if (Math.Abs(N / 2) < Math.Abs(M / FTPileTensionBars.Ds))
                {
                    Nc = Math.Abs(M / FTPileTensionBars.Ds) + N / 2;
                }
                else
                {
                    Nc = N;
                }
            }
            return Nc;
        }

        // 浮き上がり回転角を返すメソッド
        private double GetThetaC(double N)
        {
            return GetMc(N) / K0;
        }

        // パイルキャップのひび割れで決まる許容回転角を返すメソッド
        private double GetThetaAc(double N)
        {
            return 0.03 - 0.05 * GetSigmaN(N) / (PhiC * FTPileCap.Fc);
        }

        // 曲げモーメントを返すメソッド
        private double GetMoment(double theta, double N)
        {
            double M;
            double thetaC = GetThetaC(N);
            if(0 <= theta && theta <= thetaC)
            {
                M = K0 * theta;
            }
            else
            {
                M = theta / (theta + GetThetaF(N)) * GetMmax(N);
            }
            return M;
        }

        // 基準回転角θfを返すメソッド
        private double GetThetaF(double N)
        {
            double Mmax = GetMmax(N);
            double Mc = GetMc(N);
            return (Mmax - Mc) / K0;
        }

        // 初期回転剛性K0を返すメソッド
        private double GetK0(double N)
        {
            double K0;
            if (N > 0)
            {
                K0 = Math.PI * FTPileCap.E / 32.0 / (1 - Math.Pow(FTPileCap.Nu, 2.0)) * (Math.Pow(FTPilePile.D1, 3.0) - Math.Pow(FTPilePile.D2, 3.0));
            }
            else
            {
                K0 = GetMa(N) / GetThetaA(N);
            }
            return K0;
        }

        // 浮き上がりモーメントMcを返すメソッド
        private double GetMc(double N)
        {
            return Math.Max((Math.Pow(FTPilePile.D1, 2.0) + Math.Pow(FTPilePile.D2, 2.0)) / (8.0 * FTPilePile.D1) * N, 0);
        }

        // 最大抵抗モーメントMmaxを返すメソッド
        private double GetMmax(double N)
        {
            double sigmaN = GetSigmaN(N);
            double eta = Math.Min(Math.Max(-0.16 * sigmaN / FTPileCap.Fc + 1.0, 0.85), 1.0);
            double Me = GetMe(N);
            return eta * Me;
        }

        // パイルキャップの軸応力度を返すメソッド
        private double GetSigmaN(double N)
        {
            return N / FTPilePile.Ap;
        }

        // 基準抵抗モーメントMeを返すメソッド
        private double GetMe(double N)
        {
            double Me;
            if (!FTPileTensionBars.IsTensionType)
            {
                Me = Math.Max(0.5 * N * FTPilePile.D1, 0.0);
            }
            else
            {
                Me = Math.Max(5.0 * Math.Pow(10, -4) * FTPileTensionBars.Ag * FTPileTensionBars.Fu * FTPileTensionBars.Ds + 0.5 * N * FTPileTensionBars.Ds, 0.0);
            }
            return Me;
        }

        // 引き抜き対応タイプの許容モーメントを返すメソッド
        private double GetMa(double N)
        {
            return 0.375 * FTPileTensionBars.Ag * FTPileTensionBars.Ft * FTPileTensionBars.Ds + 0.5 * N * FTPileTensionBars.Ds;
        }
         
    }
}
