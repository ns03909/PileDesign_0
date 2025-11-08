using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PileDesign.FEM
{
    /// <summary>
    /// 杭の鉛直荷重伝達解析を行うクラス
    /// </summary>
    public class VerticalLoadTransferMethod : BaseModel
    {

        #region Fields

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        //private readonly InputModel inputModel = InputModel.Instance;
        private readonly SoilPile soilPile;
        private readonly int pileNodesCount;
        private readonly int nodesCount;

        private Vector<double> VectorX;   // 杭体節点の鉛直変位
        private readonly Vector<double> VectorRz;  // 杭体節点の鉛直反力

        private readonly Matrix<double> MatrixK;

        // 状態ベクトル
        private Vector<double> VectorR;  // 残留荷重
        private Vector<double> VectorDF;  // 荷重増分ベクトル
        private Vector<double> VectorF;  // 荷重ベクトル

        private Vector<double> VectorX0;  // 初期変位
        private Vector<double> VectorF0;  // 初期荷重
        private Vector<double> VectorRz0;  // 初期反力

        private double prevF0 = 0.0;
        //private double prevPrevF0 = 0.0;

        private readonly double Tolerance = Math.Pow(10, -8);
        private double PileWeight;
        private double FricpMax;
        private double FricmMax;
        private readonly List<double> RzToes = [];

        private List<double> ForcedSoilDispList = [];

        private List<double> Weights { get; set; } = [];
        private List<double> BeamStiffnesses { get; set; }

        private List<Vector<double>> _fs = [];
        public List<Vector<double>> Fs
        {
            get => _fs;
            set => SetProperty(ref _fs, value);
        }

        private List<Vector<double>> _ds = [];
        public List<Vector<double>> Ds
        {
            get => _ds;
            set => SetProperty(ref _ds, value);
        }

        private List<Vector<double>> _rs = [];
        public List<Vector<double>> Rs
        {
            get => _rs;
            set => SetProperty(ref _rs, value);
        }

        private readonly List<Vector<double>> FsLimit = [];
        private readonly List<Vector<double>> RsLimit = [];
        private readonly List<Vector<double>> DsLimit = [];
        private readonly List<double> RzToesLimit = [];

        public class LoadDisplacement : BaseModel
        {
            private double _f0s;
            public double F0s
            {
                get => _f0s;
                set => SetProperty(ref _f0s, value);
            }

            private double _d0s;
            public double D0s
            {
                get => _d0s;
                set => SetProperty(ref _d0s, value);
            }

            private double _dns;
            public double Dns
            {
                get => _dns;
                set => SetProperty(ref _dns, value);
            }

            private double _dD0s;
            public double DD0s
            {
                get => _dD0s;
                set => SetProperty(ref _dD0s, value);
            }

            private double _dDns;
            public double DDns
            {
                get => _dDns;
                set => SetProperty(ref _dDns, value);
            }

            private double _pileTopLoad;
            public double PileTopLoad
            {
                get => _pileTopLoad;
                set => SetProperty(ref _pileTopLoad, value);
            }

            private double _rzToe;
            public double RzToe
            {
                get => _rzToe;
                set => SetProperty(ref _rzToe, value);
            }

            private double _weight;
            public double Weight
            {
                get => _weight;
                set => SetProperty(ref _weight, value);
            }

            private double _rzCircum;
            public double RzCircum
            {
                get => _rzCircum;
                set => SetProperty(ref _rzCircum, value);
            }

            private string _note = string.Empty;
            public string Note
            {
                get => _note;
                set => SetProperty(ref _note, value);
            }
        }

        #endregion

        //private ObservableCollection<LoadDisplacement> _loadDisplacements = [];
        //public ObservableCollection<LoadDisplacement> LoadDisplacements
        //{
        //    get => _loadDisplacements;
        //    set => SetProperty(ref _loadDisplacements, value);
        //}

        //private ObservableCollection<LoadDisplacement> _loadDisplacementsLimit = [];
        //public ObservableCollection<LoadDisplacement> LoadDisplacementsLimit
        //{
        //    get => _loadDisplacementsLimit;
        //    set => SetProperty(ref _loadDisplacementsLimit, value);
        //}
        #region Properties

        public ObservableCollection<LoadDisplacement> LoadDisplacements { get; } = [];
        public ObservableCollection<LoadDisplacement> LoadDisplacementsLimit { get; } = [];

        #endregion

        #region Constructor & Initialization

        // コンストラクタ
        public VerticalLoadTransferMethod(MainWindowViewModel mainWindowViewModel, SoilPile _soilPile)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));
            this.soilPile = _soilPile;
            pileNodesCount = soilPile.PileCircumVerticals.Count + 1;
            nodesCount = (soilPile.PileCircumVerticals.Count + 1) * 2;

            VectorX = Vector<double>.Build.Dense(nodesCount, 0);
            VectorRz = Vector<double>.Build.Dense(nodesCount, 0);
            VectorF = Vector<double>.Build.Dense(nodesCount, 0);
            VectorDF = Vector<double>.Build.Dense(nodesCount, 0);
            VectorX0 = Vector<double>.Build.Dense(nodesCount, 0);
            VectorF0 = Vector<double>.Build.Dense(nodesCount, 0);
            VectorRz0 = Vector<double>.Build.Dense(nodesCount, 0);

            VectorR = Vector<double>.Build.Dense(nodesCount, 0);

            SetBeamStiffnesses();
            MatrixK = GenerateElementStiffnessMatrix(BeamStiffnesses);
            SetWeights();

            Initialize();

            RunAnalysis();
        }

        private void Initialize()
        {
            FricpMax = 0;
            FricmMax = 0;
            PileWeight = 0;
            foreach (var pileCircumVertical in soilPile.PileCircumVerticals)
            {
                PileWeight += pileCircumVertical.PileBodySegment.PileSection.W * pileCircumVertical.L;
                if (pileCircumVertical.GroundLayer.IsPositiveCircumResistance)
                {
                    FricpMax += pileCircumVertical.Tau2 * Math.PI
                        * pileCircumVertical.PileBodySegment.PileSection.PileDiameter / 1000.0
                        * pileCircumVertical.L;
                }
                if (pileCircumVertical.GroundLayer.IsNegativeCircumResistance)
                {
                    FricmMax += pileCircumVertical.TauT * Math.PI
                        * pileCircumVertical.PileBodySegment.PileSection.PileDiameter / 1000.0
                        * pileCircumVertical.L;
                }
            }
        }

        #endregion

        // 地盤の接線剛性取得メソッド
        public List<double> GetTangentSoilStiffness(string state, Vector<double> xs)
        {
            List<double> tangentStiffnesses = [];
            for (int i = 0; i < pileNodesCount; i++)
            {
                double stiffness = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == pileNodesCount - 1)
                    { continue; }
                    double s = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    bool aPC = soilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = soilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = soilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = soilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = soilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = soilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = soilPile.PileCircumVerticals[j].PsiL * 0.5; // m

                    stiffness += GetTangentStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);
                }

                if (i == pileNodesCount - 1) // 杭先端抵抗
                {
                    double s = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    double dp = soilPile.Dp / 1000.0; //m
                    double rpu = soilPile.Rpu;
                    double alpha = InputModel.PileBodies[^1].SettleAlpha;
                    double n = InputModel.PileBodies[^1].SettleN;

                    stiffness += GetTangentStiffnessPileToeFromSettlement(s, dp, rpu, alpha, n);
                }
                tangentStiffnesses.Add(stiffness);
            }
            return tangentStiffnesses;
        }

        // 地盤反力取得メソッド
        public Vector<double> GetSoilReactionVector(string state, Vector<double> xs)
        {
            Vector<double> soilReactions = Vector<double>.Build.Dense(nodesCount, 0);
            for (int i = 0; i < pileNodesCount; i++)
            {
                double soilReaction = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == pileNodesCount - 1)
                    { continue; }
                    double s = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    bool aPC = soilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = soilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = soilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = soilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = soilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = soilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = soilPile.PileCircumVerticals[j].PsiL * 0.5; // m

                    soilReaction += GetSecantStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL) * s;
                }

                if (i == pileNodesCount - 1) // 杭先端抵抗
                {
                    double settlment = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    double dp = soilPile.Dp / 1000.0; //m
                    double rpu = soilPile.Rpu; // kN
                    double alpha = InputModel.PileBodies[^1].SettleAlpha;
                    double n = InputModel.PileBodies[^1].SettleN;

                    soilReaction += GetSecantStiffnessPileToeFromSettlement(settlment, dp, rpu, alpha, n) * settlment;
                }
                soilReactions[2 * i] = soilReaction;
            }
            return soilReactions;
        }

        // 地盤の割線剛性取得メソッド
        public List<double> GetSecantSoilStiffness(string state, Vector<double> xs)
        {
            List<double> secantStiffnesses = [];
            for (int i = 0; i < pileNodesCount; i++)
            {
                double stiffness = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == pileNodesCount - 1)
                    { continue; }
                    double s = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    bool aPC = soilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = soilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = soilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = soilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = soilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = soilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = soilPile.PileCircumVerticals[j].PsiL * 0.5; // m2

                    stiffness += GetSecantStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);
                }

                if (i == pileNodesCount - 1) // 杭先端抵抗
                {
                    double settlment = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    double dp = soilPile.Dp / 1000.0; // m
                    double rpu = soilPile.Rpu; // kN
                    double alpha = InputModel.PileBodies[^1].SettleAlpha;
                    double n = InputModel.PileBodies[^1].SettleN;

                    stiffness += GetSecantStiffnessPileToeFromSettlement(settlment, dp, rpu, alpha, n);
                }
                secantStiffnesses.Add(stiffness);
            }
            return secantStiffnesses;
        }

        // Rpから杭先端の沈下量dpを返すメソッド
        private static double GetSettlementPileToe(
            double dp, double rp, double rpu, double alpha, double n)
        {
            return 0.1 * dp * (alpha * (rp / rpu) + (1 - alpha) * Math.Pow(rp / rpu, n));
        }

        // Rp -> Tangent K メソッド
        private static double GetTangentStiffnessPileToeFromRp(
            double rp, double dp, double rpu, double alpha, double n)
        {
            double ktan;
            if (rp < 0)
            {
                ktan = 0;
            }
            else
            {
                double stan = 0.1 * dp * (alpha * (1 / rpu) + n * (1 - alpha) * (1 / rpu) * Math.Pow(rp / rpu, n - 1));
                ktan = 1 / stan;
            }
            return ktan;
        }

        // sTarget -> Tangent K メソッド
        private static double GetTangentStiffnessPileToeFromSettlement(
            double settlement, double dp, double rpu, double alpha, double n)
        {
            double ktan;
            if (settlement < 0)
            {
                ktan = 0;
            }
            else
            {
                double rp = GetRp(settlement, dp, rpu, alpha, n);
                double stan = 0.1 * dp * (alpha * (1 / rpu) + n * (1 - alpha) * (1 / rpu) * Math.Pow(rp / rpu, n - 1));
                ktan = 1 / stan;
            }
            return ktan;
        }

        // sTarget -> Secant K メソッド
        private static double GetSecantStiffnessPileToeFromSettlement(
            double settlment, double dp, double rpu, double alpha, double n)
        {
            double ksec;
            if (settlment <= 0) // 変位が0以下(0または引抜方向)であれば
            {
                ksec = 0;
            }
            else
            {
                ksec = GetRp(settlment, dp, rpu, alpha, n) / settlment;
            }
            return ksec;
        }

        // sTarget -> Rp メソッド
        private static double GetRp(double sTarget, double dp, double rpu, double alpha, double n)
        {
            double sn;
            double ktan;
            double rp;
            if (sTarget <= 0)
            {
                rp = 0;
            }
            else
            {
                rp = rpu;
                do
                {
                    sn = GetSettlementPileToe(dp, rp, rpu, alpha, n);
                    ktan = GetTangentStiffnessPileToeFromRp(rp, dp, rpu, alpha, n);
                    rp -= (sn - sTarget) * ktan;
                } while (Math.Abs(sn - sTarget) > Math.Pow(10, -8));
            }
            return rp;
        }

        // 杭周面の接線剛性を返すメソッド
        private static double GetTangentStiffnessPilePerimeter(
            string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
        {
            //double PI = Math.PI;
            double ktan;
            if (state == "initial")
            {
                ktan = 0;
            }
            else if (state == "positive" && aPC == false)
            {
                ktan = 0;
            }
            else if (state == "negative" && aPT == false)
            {
                ktan = 0;
            }
            else if (Math.Abs(s) <= S1)
            {
                ktan = tau1 / S1 * psiL;
            }
            else if (Math.Abs(s) <= S2)
            {
                ktan = (tau2 - tau1) / (S2 - S1) * psiL;
            }
            else
            {
                ktan = 0;
            }
            return ktan;
        }

        // 杭周面の割線剛性を返すメソッド
        private static double GetSecantStiffnessPilePerimeter(
            string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
        {
            double ksec;
            if (state == "initial")
            {
                ksec = 0;
            }
            else if (state == "positive" && aPC == false)
            {
                ksec = 0;
            }
            else if (state == "negative" && aPT == false)
            {
                ksec = 0;
            }
            else if (Math.Abs(s) <= S1)
            {
                ksec = tau1 / S1 * psiL;
            }
            else if (Math.Abs(s) <= S2)
            {
                ksec = ((tau2 - tau1) / (S2 - S1) * (Math.Abs(s) - S1) + tau1) / Math.Abs(s) * psiL;
            }
            else
            {
                ksec = tau2 / Math.Abs(s) * psiL;
            }
            return ksec;
        }


        // 杭体要素剛性の取得メソッド
        public void SetBeamStiffnesses()
        {
            BeamStiffnesses = [];
            foreach (var pileCircumVertical in soilPile.PileCircumVerticals)
            {
                BeamStiffnesses.Add(pileCircumVertical.PileBodySegment.PileSection.EA / pileCircumVertical.L);　// kN/m
            }
        }

        // 重量[kN]の取得メソッド
        public void SetWeights()
        {
            if (soilPile.PileCircumVerticals.Count == 0)
            { return; }

            for (int i = 0; i < soilPile.PileCircumVerticals.Count + 1; i++)
            {
                if (i == 0)
                {
                    Weights.Add((soilPile.PileCircumVerticals[i].PileBodySegment.PileSection.W * soilPile.PileCircumVerticals[i].L * 0.5)); // kN
                }
                else if (i < soilPile.PileCircumVerticals.Count)
                {
                    Weights.Add(soilPile.PileCircumVerticals[i - 1].PileBodySegment.PileSection.W * soilPile.PileCircumVerticals[i - 1].L * 0.5 +
                                soilPile.PileCircumVerticals[i].PileBodySegment.PileSection.W * soilPile.PileCircumVerticals[i].L * 0.5);
                }
                else // if (i = soilPile.PileCircumVerticals.Count)
                {
                    Weights.Add(soilPile.PileCircumVerticals[i - 1].PileBodySegment.PileSection.W * soilPile.PileCircumVerticals[i - 1].L * 0.5);
                }
            }
        }

        // 剛性マトリクスの杭体部分を組み立てる共通メソッド
        private static void AddPileStiffness(Matrix<double> matrix, List<double> pileStiffness)
        {
            for (int i = 0; i < pileStiffness.Count; i++)
            {
                matrix[2 * i, 2 * i] += pileStiffness[i];
                matrix[2 * i, 2 * i + 2] -= pileStiffness[i];
                matrix[2 * i + 2, 2 * i] -= pileStiffness[i];
                matrix[2 * i + 2, 2 * i + 2] += pileStiffness[i];
            }
        }

        // 要素による剛性マトリクス生成メソッド（杭体のみ）
        public Matrix<double> GenerateElementStiffnessMatrix(List<double> pileStiffness)
        {
            var matrix = Matrix<double>.Build.Dense(nodesCount, nodesCount, 0.0);
            AddPileStiffness(matrix, pileStiffness);
            return matrix;
        }

        // 剛性マトリクスの生成メソッド（杭体＋地盤）
        public Matrix<double> GenerateStiffnessMatrix(List<double> pileStiffness, List<double> soilStiffness)
        {
            var matrix = Matrix<double>.Build.Dense(nodesCount, nodesCount, 0.0);
            AddPileStiffness(matrix, pileStiffness);

            for (int i = 0; i < soilStiffness.Count; ++i)
            {
                matrix[2 * i, 2 * i] += soilStiffness[i];
                matrix[2 * i, 2 * i + 1] -= soilStiffness[i];
                matrix[2 * i + 1, 2 * i] -= soilStiffness[i];
                matrix[2 * i + 1, 2 * i + 1] += soilStiffness[i];
            }
            return matrix;
        }

        // 荷重ベクトルの生成メソッド
        public Vector<double> GenerateForceVector(List<double> weights, double pileTopForce)
        {
            var forceVector = Vector<double>.Build.Dense(nodesCount, 0);
            for (int i = 0; i < weights.Count; i++)
            {
                forceVector[2 * i] += weights[i];
            }
            forceVector[0] += pileTopForce;
            return forceVector;
        }

        // 内力取得算定メソッド
        private Vector<double> GetVectorT(
            List<double> beamStiffnesses, List<double> soilStiffnesses, Vector<double> vectorX)
        {
            Matrix<double> stiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, soilStiffnesses);
            List<double> forcedDispList = [];
            for (int i = 0; i < ForcedSoilDispList.Count; i++)
            {
                forcedDispList.Add(ForcedSoilDispList[i]);
            }
            var zeroVector = Vector<double>.Build.Dense(vectorX.Count, 0.0);
            ForcedDispTransfer(stiffnessMatrix, zeroVector, forcedDispList);

            return stiffnessMatrix * vectorX - zeroVector;
        }

        // 解法メソッド
        public Vector<double> SolveDisp(
            List<double> beamStiffnesses, List<double> soilStiffnesses, Vector<double> forceVector, Vector<double> vectorX)
        {
            Matrix<double> stiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, soilStiffnesses);
            List<double> forcedDispList = [];
            for (int i = 0; i < ForcedSoilDispList.Count; i++)
            {
                forcedDispList.Add(ForcedSoilDispList[i] - vectorX[2 * i + 1]);
            }

            ForcedDispTransfer(stiffnessMatrix, forceVector, forcedDispList);

            return stiffnessMatrix.Solve(forceVector);
        }

        // 地盤の強制変位の考慮
        private void ForcedDispTransfer(Matrix<double> stiffnessMatrix, Vector<double> forceVector, List<double> forcedDispList)
        {
            // 荷重ベクトルへの操作
            for (int i = 0; i < forcedDispList.Count; i++)
            {
                for (int j = 0; j < forceVector.Count; j++) // すべての項目
                {
                    forceVector[j] -= stiffnessMatrix[j, i * 2 + 1] * forcedDispList[i];
                }
            }

            for (int i = 0; i < forcedDispList.Count; i++)
            {
                forceVector[i * 2 + 1] = forcedDispList[i];
            }

            // 剛性マトリクスの変換
            for (int i = 0; i < forcedDispList.Count; i++)
            {
                stiffnessMatrix[i * 2 + 1, i * 2 + 1] = 1.0; // 剛性マトリクスの該当要素を1に設定
                for (int j = 0; j < nodesCount; j++) // すべての項目
                {
                    if (i * 2 + 1 == j) continue; // 自分自身の方程式番号はスキップ
                    stiffnessMatrix[i * 2 + 1, j] = 0.0; // 剛性マトリクスの該当要素をゼロに設定
                    stiffnessMatrix[j, i * 2 + 1] = 0.0; // 剛性マトリクスの該当要素をゼロに設定
                }
            }
        }

        // 解析実行メソッド
        private void RunAnalysis()
        {
            RunInitialStateAnalysis();

            // 押込側（圧縮）と引抜側（引張）でループ
            for (int pn = -1; pn <= 1; pn += 2)
            {
                string state = (pn == -1) ? "positive" : "negative";
                RunLoadIncrementAnalysis(state, pn);
            }

            RecordResults();
        }

        // 解析初期化メソッド
        private void RunInitialStateAnalysis()
        {
            //string state = "initial";
            string state = "positive";
            VectorX.Clear();
            VectorRz.Clear();
            VectorRz[^1] = PileWeight;

            for (int i = 0; i < pileNodesCount; i++) // 初期化
            {
                ForcedSoilDispList.Add(0);
                VectorDF[2 * i] += Weights[i];
            }

            VectorF += VectorDF;
            VectorR -= VectorDF;

            if (VectorR.L2Norm() / VectorF.L2Norm() != 0)
            {
                ConvergenceCaluculation(state);

                double settlement = VectorX[^2];
                double dp = soilPile.Dp / 1000.0;
                double rpu = soilPile.Rpu;
                double alpha = InputModel.PileBodies[^1].SettleAlpha;
                double n = InputModel.PileBodies[^1].SettleN;
                double rzToe = GetRp(settlement, dp, rpu, alpha, n);

                VectorX0 = VectorX.Clone();
                VectorF0 = VectorF.Clone();
                VectorRz0 = VectorRz.Clone();
                VectorX.Clear();

                Fs.Add(VectorF); // 荷重
                Rs.Add(VectorRz); // 反力
                Ds.Add(VectorX0); // 変位
                RzToes.Add(rzToe); // 杭先端反力
            }
            else
            {
                VectorX0.Clear();
                VectorF0.Clear();
                VectorX.Clear();
            }

            ForcedSoilDispList = []; // 初期化
            for (int i = 0; i < VectorX0.Count / 2 - 1; i++)
            {
                //ForcedSoilDispList.Add(VectorX0[i * 2]);
                ForcedSoilDispList.Add(0);
            }
            ForcedSoilDispList.Add(0);

            // VectorX0の調整
            for (int i = 0; i < VectorX0.Count / 2 - 1; i++)
            {
                //VectorX0[2 * i + 1] = VectorX0[2 * i];
            }

            //Fs.Add(VectorF); // 荷重
            //Rs.Add(VectorRz); // 反力
            //Ds.Add(VectorX); // 変位
            //RzToes.Add(rzToe); // 杭先端反力
        }

        // 圧縮側・引張側荷重増分解析本体
        private void RunLoadIncrementAnalysis(string state, int pn)
        {
            VectorX = VectorX0.Clone();
            VectorF = VectorF0.Clone();
            VectorRz.Clear();
            VectorR.Clear();

            double step = (pn == -1) ? GetStepCompression(soilPile.Rpu, FricpMax) : GetStepTension(FricmMax, PileWeight);
            VectorDF.Clear();
            VectorDF[0] = step;

            var limitFlags = new LimitFlags();

            do
            {
                ApplyLoadIncrements(pn, limitFlags);

                ConvergenceCaluculation(state);

                double settlement = VectorX[^2];
                double dp = soilPile.Dp / 1000.0;
                double rpu = soilPile.Rpu;
                double alpha = InputModel.PileBodies[^1].SettleAlpha;
                double n = InputModel.PileBodies[^1].SettleN;
                double rzToe = GetRp(settlement, dp, rpu, alpha, n);

                if (state == "positive")
                {
                    Fs.Add(VectorF); // 荷重
                    Rs.Add(VectorRz); // 反力
                    Ds.Add(VectorX); // 変位
                    RzToes.Add(rzToe); // 杭先端反力

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(VectorF, VectorRz, VectorX, rzToe, true);
                }
                else // negative
                {
                    Fs.Insert(0, VectorF); // 荷重
                    Rs.Insert(0, VectorRz); // 反力
                    Ds.Insert(0, VectorX); // 変位
                    RzToes.Insert(0, rzToe); // 杭先端反力

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(VectorF, VectorRz, VectorX, rzToe, false);
                }

                if (limitFlags.IsAnyJustULS)
                    break;

            }
            while (IsWithinLoadRange(VectorF[0] - Weights[0], pn));
        }

        // 荷重増分の判定・適用
        private void ApplyLoadIncrements(int pn, LimitFlags flags)
        {
            // 限界値
            double r_SLS = soilPile.R_SLS;
            double r_DLS = soilPile.R_DLS;
            double r_ULS = soilPile.R_ULS;
            double rt_SLS = soilPile.Rt_SLS;
            double rt_DLS = soilPile.Rt_DLS;
            double rt_ULS = soilPile.Rt_ULS;

            // 荷重増分ベクトル
            Vector<double> tempDeltaF = Vector<double>.Build.Dense(nodesCount, 0);

            if (pn == -1) // 圧縮側
            {
                if (VectorF[0] + VectorDF[0] - Weights[0] > r_SLS && !flags.IsR_SLS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = r_SLS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsR_SLS = true;
                    flags.IsJustR_SLS = true;
                }
                else if (VectorF[0] + VectorDF[0] - Weights[0] > r_DLS && !flags.IsR_DLS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = r_DLS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsR_DLS = true;
                    flags.IsJustR_DLS = true;
                }
                else if (VectorF[0] + VectorDF[0] - Weights[0] > r_ULS && !flags.IsR_ULS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = r_ULS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsR_ULS = true;
                    flags.IsJustR_ULS = true;
                }
                else if (flags.IsJustR_SLS)
                {
                    tempDeltaF[0] = (prevF0 + VectorDF[0] - Weights[0]) - r_SLS;
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsJustR_SLS = false;
                }
                else if (flags.IsJustR_DLS)
                {
                    tempDeltaF[0] = (prevF0 + VectorDF[0] - Weights[0]) - r_DLS;
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsJustR_DLS = false;
                }
                else
                {
                    VectorF += VectorDF;
                    VectorR -= VectorDF;
                }
            }
            else // 引抜側 (pn == 1)
            {
                if (VectorF[0] + VectorDF[0] - Weights[0] < rt_SLS && !flags.IsRt_SLS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = rt_SLS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsRt_SLS = true;
                    flags.IsJustRt_SLS = true;
                }
                else if (VectorF[0] + VectorDF[0] - Weights[0] < rt_DLS && !flags.IsRt_DLS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = rt_DLS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsRt_DLS = true;
                    flags.IsJustRt_DLS = true;
                }
                else if (VectorF[0] + VectorDF[0] - Weights[0] < rt_ULS && !flags.IsRt_ULS)
                {
                    prevF0 = VectorF[0];
                    tempDeltaF[0] = rt_ULS - (VectorF[0] - Weights[0]);
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsRt_ULS = true;
                    flags.IsJustRt_ULS = true;
                }
                else if (flags.IsJustRt_SLS)
                {
                    tempDeltaF[0] = (prevF0 + VectorDF[0] - Weights[0]) - rt_SLS;
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsJustRt_SLS = false;
                }
                else if (flags.IsJustRt_DLS)
                {
                    tempDeltaF[0] = (prevF0 + VectorDF[0] - Weights[0]) - rt_DLS;
                    VectorF += tempDeltaF;
                    VectorR -= tempDeltaF;
                    flags.IsJustRt_DLS = false;
                }
                else
                {
                    VectorF += VectorDF;
                    VectorR -= VectorDF;
                }
            }
        }

        // 結果記録
        private void RecordLimitState(Vector<double> VectorF, Vector<double> VectorRz, Vector<double> VectorX, double rzToe, bool isPositive)
        {
            if (isPositive)
            {
                FsLimit.Add(VectorF);
                RsLimit.Add(VectorRz);
                DsLimit.Add(VectorX);
                RzToesLimit.Add(rzToe);
            }
            else　// (isNegative)
            {
                FsLimit.Insert(0, VectorF);
                RsLimit.Insert(0, VectorRz);
                DsLimit.Insert(0, VectorX);
                RzToesLimit.Insert(0, rzToe);
            }
        }

        // 荷重範囲判定
        private bool IsWithinLoadRange(double pileTopLoad, int pn)
        {
            double r_ULS = soilPile.R_ULS;
            double rt_ULS = soilPile.Rt_ULS;
            return (pn == -1) ? (pileTopLoad <= r_ULS) : (pileTopLoad >= rt_ULS);
        }


        private class LimitFlags
        {
            public bool IsR_SLS, IsJustR_SLS, IsR_DLS, IsJustR_DLS, IsR_ULS, IsJustR_ULS;
            public bool IsRt_SLS, IsJustRt_SLS, IsRt_DLS, IsJustRt_DLS, IsRt_ULS, IsJustRt_ULS;
            public bool IsAnyJustLimit => IsJustR_SLS || IsJustR_DLS || IsJustR_ULS || IsJustRt_SLS || IsJustRt_DLS || IsJustRt_ULS;
            public bool IsAnyJustULS => IsJustR_ULS || IsJustRt_ULS;
        }


        // 収束ループ
        private void ConvergenceCaluculation(string state)
        {
            //||res|| / ||VectorF|| < tolerance となるまで繰り返し計算を行う
            //do while ||VectorR|| / ||VectorF|| > tolerance
            //Find K
            //Solve Ku=-VectorR
            //Update x = x + u
            //Find VectorF(e), b(e), sigma(e)
            //Find T
            //Find VectorR=T-VectorF
            //END DO
            double norm = double.MaxValue;
            int iterationCount = 0;
            while (norm > Tolerance)
            {
                iterationCount += 1;
                if (iterationCount >= 100)
                {
                    MessageBox.Show("収束計算が100回を超えました。計算を中断します。", "収束エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    throw new InvalidOperationException("収束計算が100回を超えたため中断しました。");
                }

                // Find K 接線剛性マトリクスの計算
                List<double> soilTangentStiffnesses = GetTangentSoilStiffness(state, VectorX);
                Vector<double> U = SolveDisp(BeamStiffnesses, soilTangentStiffnesses, -VectorR, VectorX);
                VectorX += U; // update x = x + u (配置更新)

                Vector<double> T = FindT(state);

                // Find VectorR 残差ベクトル
                VectorR = T - VectorF;
                norm = VectorR.L2Norm() / VectorF.L2Norm();
            }
        }

        // 内力
        private Vector<double> FindT(string state)
        {
            // Find T
            Vector<double> T = GetSoilReactionVector(state, VectorX) + MatrixK * VectorX;

            return T;
        }

        // 引張ステップ
        private static double GetStepTension(double fricmMax, double pileWeight)
        {
            double pmin = fricmMax - pileWeight;
            return pmin switch
            {
                >= 0 => 0,
                >= -1000 => -10,
                >= -5000 => -50,
                >= -10000 => -100,
                >= -50000 => -500,
                >= -100000 => -1000,
                _ => -5000
            };
        }

        // 圧縮ステップ
        private static double GetStepCompression(double rpu, double fripMax)
        {
            double pmax = rpu + fripMax;
            return pmax switch
            {
                <= 0 => 0,
                <= 1000 => 10,
                <= 5000 => 50,
                <= 10000 => 100,
                <= 50000 => 500,
                <= 100000 => 1000,
                _ => 5000
            };
        }

        #region Results
        private void RecordResults()
        {
            RecordLoadDisplacementResults();
            RecordLimitStateResults();
        }

        // 結果をLoadDisplacementsに記録
        private void RecordLoadDisplacementResults()
        {
            for (int i = 0; i < Fs.Count; i++)
            {
                LoadDisplacements.Add(new LoadDisplacement
                {
                    F0s = Fs[i][0],
                    D0s = Ds[i][0] * 1000.0,
                    Dns = Ds[i][^2] * 1000.0,
                    DD0s = (Ds[i][0] - VectorX0[0]) * 1000.0,
                    DDns = (Ds[i][^2] - VectorX0[^2]) * 1000.0,
                    //DD0s = Ds[i][0] * 1000.0,
                    //DDns = Ds[i][^2] * 1000.0,
                    PileTopLoad = Fs[i][0] - Weights[0],
                    RzToe = RzToes[i],
                    //Weight = -PileWeight,
                    Weight = PileWeight,
                    RzCircum = Fs[i][0] - Weights[0] + PileWeight - RzToes[i]
                });
            }
        }

        private void RecordLimitStateResults()
        {
            for (int i = 0; i < FsLimit.Count; i++)
            {
                LoadDisplacementsLimit.Add(new LoadDisplacement
                {
                    F0s = FsLimit[i][0],
                    D0s = DsLimit[i][0] * 1000.0,
                    Dns = DsLimit[i][^2] * 1000.0,
                    DD0s = (DsLimit[i][0] - VectorX0[0]) * 1000.0,
                    DDns = (DsLimit[i][^2] - VectorX0[^2]) * 1000.0,
                    //DD0s = DsLimit[i][0] * 1000.0,
                    //DDns = DsLimit[i][^2]  * 1000.0,
                    PileTopLoad = FsLimit[i][0] - Weights[0],
                    RzToe = RzToesLimit[i],
                    //Weight = -PileWeight,
                    Weight = PileWeight,
                    RzCircum = FsLimit[i][0] - Weights[0] + PileWeight - RzToesLimit[i],
                    Note = "***"
                });
            }
        }
        #endregion

        /// <summary>
        /// 指定した杭頭荷重に対する変位ベクトルを返す（RunAnalysisと同様の構造）
        /// </summary>
        public Vector<double>? GetDisplacementForGivenLoad(double pileTopForce)
        {
            // 1. 初期化
            var vectorX = VectorX0;
            var beamStiffnesses = BeamStiffnesses;
            var weights = Weights.ToList();
            var vectorF = GenerateForceVector(weights, pileTopForce);

            string state = pileTopForce >= 0 ? "positive" : "negative";

            // 2. 収束計算
            bool converged = TryConvergeDisplacement(vectorF, beamStiffnesses, ref vectorX, out Vector<double> result, state);

            // 3. 結果返却
            return converged ? result : null;
        }


        /// <summary>
        /// 収束計算（ニュートンラフソン法）を実行
        /// </summary>
        private bool TryConvergeDisplacement(
            Vector<double> vectorF,
            List<double> beamStiffnesses,
            ref Vector<double> vectorX,
            out Vector<double> result,
            string state)
        {
            double norm = double.MaxValue;
            int counter = 0;
            Vector<double> vectorR = -vectorF;

            while (norm > Tolerance)
            {
                counter += 1;
                // Find K 接線剛性マトリクスの計算
                List<double> soilTangentStiffnesses = GetTangentSoilStiffness(state, vectorX);
                Vector<double> U = SolveDisp(beamStiffnesses, soilTangentStiffnesses, -vectorR, vectorX);
                vectorX += U; // update x = x + u (配置更新)

                List<double> soilSecantStiffnesses = GetSecantSoilStiffness(state, vectorX);
                Vector<double> vectorT = GetVectorT(beamStiffnesses, soilSecantStiffnesses, vectorX);
                // Find VectorR 残差ベクトル
                vectorR = vectorT - vectorF;
                norm = GetNorm(vectorR, vectorF);
            }

            // 初期変位を引いて相対変位に変換
            result = vectorX - VectorX0;
            return true;


        }

        private static double GetNorm(Vector<double> vectorR, Vector<double> vectorF)
        {
            var vectorOddR = Vector<double>.Build.Dense(
            Enumerable.Range(0, vectorR.Count)
            .Where(i => i % 2 == 0)
            .Select(i => vectorR[i])
            .ToArray());

            var vectorOddF = Vector<double>.Build.Dense(
            Enumerable.Range(0, vectorF.Count)
            .Where(i => i % 2 == 0)
            .Select(i => vectorF[i])
            .ToArray());

            return vectorOddR.L2Norm() / vectorOddF.L2Norm();

        }
    }
}

