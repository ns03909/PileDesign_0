using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;


namespace PileDesign.Common
{
    public class LoadTransferMethod : BaseModel
    {
        //private readonly InputModel InputModel = InputModel.Instance;/*{ get; set; }*/
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        private readonly SoilPile SoilPile;/*{ get; set; }*/

        private readonly int NodesCount /*{get; set;}*/;

        private Vector<double> X; /*{ get; set; }*/  // 杭体節点の鉛直変位
        private readonly Vector<double> Rz; /*{ get; set; }*/ // 杭体節点の鉛直反力

        private readonly Matrix<double> Kmat;

        private Vector<double> R; /*{ get; set; }*/ // 残留荷重
        private Vector<double> DeltaF; /*{ get; set; }*/ // 荷重増分ベクトル
        private Vector<double> F; /*{ get; set; }*/ // 荷重ベクトル

        private Vector<double> X0; /*{ get; set; }*/ // 初期変位
        private Vector<double> F0; /*{ get; set; }*/ // 初期荷重
        private Vector<double> Rz0; /*{ get; set; }*/ // 初期反力

        private readonly double Tolerance = Math.Pow(10, -8); /*{ get; set; }*/
        private double PileWeight; /*{ get; set; }*/
        private double FricpMax; /*{ get; set; }*/
        private double FricmMax; /*{ get; set; }*/
        private readonly List<double> RzToes = [];

        private Vector<double> Weights { get; set; }
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

            public double _pileTopLoad;
            public double PileTopLoad
            {
                get => _pileTopLoad;
                set => SetProperty(ref _pileTopLoad, value);
            }

            public double _rzToe;
            public double RzToe
            {
                get => _rzToe;
                set => SetProperty(ref _rzToe, value);
            }

            public double _weight;
            public double Weight
            {
                get => _weight;
                set => SetProperty(ref _weight, value);
            }

            public double _rzCircum;
            public double RzCircum
            {
                get => _rzCircum;
                set => SetProperty(ref _rzCircum, value);
            }
        }

        private ObservableCollection<LoadDisplacement> _loadDisplacements = [];
        public ObservableCollection<LoadDisplacement> LoadDisplacements
        {
            get => _loadDisplacements;
            set => SetProperty(ref _loadDisplacements, value);
        }

        private ObservableCollection<LoadDisplacement> _loadDisplacementsLimit = [];
        public ObservableCollection<LoadDisplacement> LoadDisplacementsLimit
        {
            get => _loadDisplacementsLimit;
            set => SetProperty(ref _loadDisplacementsLimit, value);
        }

        // コンストラクタ
        public LoadTransferMethod(SoilPile soilPile)
        {
            //InputModel = InputModel.Instance;
            this.SoilPile = soilPile;

            NodesCount = SoilPile.PileCircumVerticals.Count + 1;

            X = Vector<double>.Build.Dense(NodesCount, 0);
            Rz = Vector<double>.Build.Dense(NodesCount, 0);
            F = Vector<double>.Build.Dense(NodesCount, 0);
            DeltaF = Vector<double>.Build.Dense(NodesCount, 0);
            X0 = Vector<double>.Build.Dense(NodesCount, 0);
            F0 = Vector<double>.Build.Dense(NodesCount, 0);
            Rz0 = Vector<double>.Build.Dense(NodesCount, 0);

            R = Vector<double>.Build.Dense(NodesCount, 0);

            Weights = Vector<double>.Build.Dense(NodesCount, 0);

            //Fs = [];
            //Rs = [];
            //Ds = [];
            //RzToes = [];

            //FsLimit = [];
            //RsLimit = [];
            //DsLimit = [];
            //RzToesLimit = [];

            //LoadDisplacements = [];
            //LoadDisplacementsLimit = [];

            SetBeamStiffnesses();
            Kmat = GenerateElementStiffnessMatrix(BeamStiffnesses);
            SetWeights();

            Initialize();

            //NewMain();/////////////////////////////////////////////////////////
            Main();
        }

        private void Initialize()
        {
            FricpMax = 0;
            FricmMax = 0;
            PileWeight = 0;
            foreach (var pileCircumVertical in SoilPile.PileCircumVerticals)
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

        // 地盤の接線剛性取得メソッド
        public List<double> GetTangentSoilStiffness(string state, Vector<double> us)
        {
            List<double> tangentStiffnesses = [];
            for (int i = 0; i < us.Count; i++)
            {
                double stiffness = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == us.Count - 1)
                    { continue; }
                    double s = us[i] - X0[i]; // 相対変位
                    bool aPC = SoilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = SoilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = SoilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = SoilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = SoilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = SoilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = SoilPile.PileCircumVerticals[j].PsiL * 0.5; // m

                    stiffness += GetTangentStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);
                }

                if (i == us.Count - 1) // 杭先端抵抗
                {
                    double s = us[^1];

                    double dp = SoilPile.Dp / 1000.0; //m
                    double rpu = SoilPile.Rpu;
                    double alpha = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha;
                    double n = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN;

                    stiffness += GetTangentStiffnessPileToeFromSettlement(s, dp, rpu, alpha, n);
                }
                tangentStiffnesses.Add(stiffness);
            }
            return tangentStiffnesses;
        }

        // 地盤反力取得メソッド
        public Vector<double> GetSoilReactionVector(string state, Vector<double> us)
        {
            Vector<double> soilReactions = Vector<double>.Build.Dense(NodesCount, 0);
            for (int i = 0; i < us.Count; i++)
            {
                double soilReaction = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == us.Count - 1)
                    { continue; }
                    double s = us[i] - X0[i]; // 相対変位
                    bool aPC = SoilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = SoilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = SoilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = SoilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = SoilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = SoilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = SoilPile.PileCircumVerticals[j].PsiL * 0.5; // m

                    soilReaction += GetSecantStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL) * s;
                }

                if (i == us.Count - 1) // 杭先端抵抗
                {
                    double settlment = us[^1];
                    double dp = SoilPile.Dp / 1000.0; //m
                    double rpu = SoilPile.Rpu; // kN
                    double alpha = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha;
                    double n = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN;

                    soilReaction += GetSecantStiffnessPileToeFromSettlement(settlment, dp, rpu, alpha, n) * settlment;
                }
                soilReactions[i] = soilReaction;
            }
            return soilReactions;
        }

        // 地盤の割線剛性取得メソッド
        public List<double> GetSecantSoilStiffnessList(string state, Vector<double> us)
        {
            List<double> secantStiffnesses = [];
            for (int i = 0; i < us.Count; i++)
            {
                double stiffness = 0;
                for (int j = i - 1; j <= i; j++)
                {
                    if (j == -1 || j == us.Count - 1)
                    { continue; }
                    double s = us[i] - X0[i];
                    bool aPC = SoilPile.PileCircumVerticals[j].GroundLayer.IsPositiveCircumResistance;
                    bool aPT = SoilPile.PileCircumVerticals[j].GroundLayer.IsNegativeCircumResistance;
                    double tau1 = SoilPile.PileCircumVerticals[j].Tau1; // kN/m2
                    double tau2 = SoilPile.PileCircumVerticals[j].Tau2; // kN/m2
                    double S1 = SoilPile.PileCircumVerticals[j].S1 / 1000.0; // m
                    double S2 = SoilPile.PileCircumVerticals[j].S2 / 1000.0; // m
                    double psiL = SoilPile.PileCircumVerticals[j].PsiL * 0.5; // m2

                    stiffness += GetSecantStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);
                }

                if (i == us.Count - 1) // 杭先端抵抗
                {
                    double settlment = us[^1];
                    double dp = SoilPile.Dp / 1000.0; // m
                    double rpu = SoilPile.Rpu; // kN
                    double alpha = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha;
                    double n = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN;

                    stiffness += GetSecantStiffnessPileToeFromSettlement(settlment, dp, rpu, alpha, n);
                }
                secantStiffnesses.Add(stiffness);
            }
            return secantStiffnesses;
        }

        // Rpから杭先端の沈下量dpを返すメソッド
        private static double GetSettlementPileToe(double dp, double rp, double rpu, double alpha, double n)
        {
            return 0.1 * dp * (alpha * (rp / rpu) + (1 - alpha) * Math.Pow(rp / rpu, n));
        }

        // Rp -> Tangent K メソッド
        private static double GetTangentStiffnessPileToeFromRp(double rp, double dp, double rpu, double alpha, double n)
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
        private static double GetTangentStiffnessPileToeFromSettlement(double settlement, double dp, double rpu, double alpha, double n)
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
        private static double GetSecantStiffnessPileToeFromSettlement(double settlment, double dp, double rpu, double alpha, double n)
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
        private static double GetTangentStiffnessPilePerimeter(string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
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
        private static double GetSecantStiffnessPilePerimeter(string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
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
            foreach (var pileCircumVertical in SoilPile.PileCircumVerticals)
            {
                BeamStiffnesses.Add(pileCircumVertical.PileBodySegment.PileSection.EA / pileCircumVertical.L);　// kN/m
            }
        }

        // 重量[kN]の取得メソッド
        public void SetWeights()
        {
            if (SoilPile.PileCircumVerticals.Count == 0)
            { return; }

            for (int i = 0; i < SoilPile.PileCircumVerticals.Count + 1; i++)
            {
                if (i == 0)
                {
                    Weights[i] = (SoilPile.PileCircumVerticals[i].PileBodySegment.PileSection.W * SoilPile.PileCircumVerticals[i].L * 0.5); // kN
                }
                else if (i < SoilPile.PileCircumVerticals.Count)
                {
                    Weights[i] = (SoilPile.PileCircumVerticals[i - 1].PileBodySegment.PileSection.W * SoilPile.PileCircumVerticals[i - 1].L * 0.5 +
                                SoilPile.PileCircumVerticals[i].PileBodySegment.PileSection.W * SoilPile.PileCircumVerticals[i].L * 0.5);
                }
                else // if (i = SoilPile.PileCircumVerticals.Count)
                {
                    Weights[i] = (SoilPile.PileCircumVerticals[i - 1].PileBodySegment.PileSection.W * SoilPile.PileCircumVerticals[i - 1].L * 0.5);
                }
            }
        }

        // 剛性マトリクスの生成メソッド
        public static Matrix<double> GenerateStiffnessMatrix(List<double> beamStiffnesses, List<double> nodeStiffnesses)
        {
            var stiffnessMatrix = Matrix<double>.Build.Dense(nodeStiffnesses.Count, nodeStiffnesses.Count, 0.0);

            for (int i = 0; i < beamStiffnesses.Count; i++)
            {
                stiffnessMatrix[i, i] += beamStiffnesses[i];
                stiffnessMatrix[i, i + 1] -= beamStiffnesses[i];
                stiffnessMatrix[i + 1, i] -= beamStiffnesses[i];
                stiffnessMatrix[i + 1, i + 1] += beamStiffnesses[i];
            }

            for (int i = 0; i < nodeStiffnesses.Count; ++i)
            {
                stiffnessMatrix[i, i] += nodeStiffnesses[i];
            }

            return stiffnessMatrix;
        }

        // 要素による剛性マトリクス生成メソッド
        public static Matrix<double> GenerateElementStiffnessMatrix(List<double> beamStiffnesses)
        {
            var stiffnessMatrix = Matrix<double>.Build.Dense(beamStiffnesses.Count + 1, beamStiffnesses.Count + 1, 0.0);

            for (int i = 0; i < beamStiffnesses.Count; i++)
            {
                stiffnessMatrix[i, i] += beamStiffnesses[i];
                stiffnessMatrix[i, i + 1] -= beamStiffnesses[i];
                stiffnessMatrix[i + 1, i] -= beamStiffnesses[i];
                stiffnessMatrix[i + 1, i + 1] += beamStiffnesses[i];
            }
            return stiffnessMatrix;
        }

        // 荷重ベクトルの生成メソッド
        public static Vector<double> GenerateForceVector(List<double> weights, double pileTopForce)
        {
            var forceVector = Vector<double>.Build.Dense([.. weights]);
            forceVector[0] += pileTopForce;
            return forceVector;
        }

        // 解法メソッド
        public static Vector<double> SolveDisplacementVector(
            List<double> beamStiffnesses, List<double> nodeStiffnesses, List<double> weights, double pileTopForce)
        {
            Matrix<double> stiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, nodeStiffnesses);
            Vector<double> forceVector = GenerateForceVector(weights, pileTopForce);
            Vector<double> displacementVector = stiffnessMatrix.Solve(forceVector);
            return displacementVector;
        }

        // 解法メソッド
        public static Vector<double> SolveDisp(
            List<double> beamStiffnesses, List<double> nodeStiffnesses, Vector<double> forceVector)
        {
            Matrix<double> stiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, nodeStiffnesses);
            Vector<double> displacementVector = stiffnessMatrix.Solve(forceVector);
            return displacementVector;
        }

        private void Main()
        {
            InitializeAnalysisState();

            // 押込側（圧縮）と引抜側（引張）でループ
            for (int pn = -1; pn <= 1; pn += 2)
            {
                string state = (pn == -1) ? "positive" : "negative";
                RunLoadIncrementAnalysis(state, pn);
            }
            RecordLoadDisplacementResults();
            RecordLimitStateResults();
        }

        // 解析初期化
        private void InitializeAnalysisState()
        {
            string state = "initial";
            X.Clear();
            Rz.Clear();
            Rz[^1] = PileWeight;

            DeltaF += Weights;
            F += DeltaF;
            R -= DeltaF;

            if (R.L2Norm() / F.L2Norm() != 0)
            {
                ConvergenceCaluculation(state);
                X0 = X.Clone();
                F0 = F.Clone();
                Rz0 = Rz.Clone();
                X.Clear();
            }
            else
            {
                X0.Clear();
                F0.Clear();
                X.Clear();
            }

            Fs.Add(F0);
            Rs.Add(Rz0);
            Ds.Add(X0);
            RzToes.Add(PileWeight);
        }

        // 荷重増分解析本体
        private void RunLoadIncrementAnalysis(string state, int pn)
        {
            X = X0.Clone();
            F = F0.Clone();
            Rz.Clear();
            R.Clear();

            double step = (pn == -1) ? GetStepCompression(SoilPile.Rpu, FricpMax/*, PileWeight*/) : GetStepTension(FricmMax, PileWeight);
            DeltaF.Clear();
            DeltaF[0] = step;

            int stepCount = 0;
            var limitFlags = new LimitFlags();

            do
            {
                ApplyLoadIncrements(ref stepCount, pn, step, limitFlags);

                ConvergenceCaluculation(state);

                double settlement = X[^1];
                double dp = SoilPile.Dp / 1000.0;
                double rpu = SoilPile.Rpu;
                double alpha = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha;
                double n = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN;
                double rzToe = GetRp(settlement, dp, rpu, alpha, n);

                if (state == "positive")
                {
                    Fs.Add(F);
                    Rs.Add(Rz);
                    Ds.Add(X);
                    RzToes.Add(rzToe);

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(F, Rz, X, rzToe, true);
                }
                else
                {
                    Fs.Insert(0, F);
                    Rs.Insert(0, Rz);
                    Ds.Insert(0, X);
                    RzToes.Insert(0, rzToe);

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(F, Rz, X, rzToe, false);
                }

                if (limitFlags.IsAnyJustULS)
                    break;

                stepCount++;
            }
            while (IsWithinLoadRange(F[0] - Weights[0], pn));
        }

        // 荷重増分の判定・適用
        private void ApplyLoadIncrements(ref int step, int pn, double stepValue, LimitFlags flags)
        {
            // 限界値
            double r_SLS = SoilPile.R_SLS;
            double r_DLS = SoilPile.R_DLS;
            double r_ULS = SoilPile.R_ULS;
            double rt_SLS = SoilPile.Rt_SLS;
            double rt_DLS = SoilPile.Rt_DLS;
            double rt_ULS = SoilPile.Rt_ULS;

            // 荷重増分ベクトル
            Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);

            if (pn == -1) // 圧縮側
            {
                if ((step + 1) * stepValue > r_SLS && !flags.IsR_SLS)
                {
                    tempDeltaF[0] = r_SLS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsR_SLS = true;
                    flags.IsJustR_SLS = true;
                }
                else if ((step + 1) * stepValue > r_DLS && !flags.IsR_DLS)
                {
                    tempDeltaF[0] = r_DLS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsR_DLS = true;
                    flags.IsJustR_DLS = true;
                }
                else if ((step + 1) * stepValue > r_ULS && !flags.IsR_ULS)
                {
                    tempDeltaF[0] = r_ULS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsR_ULS = true;
                    flags.IsJustR_ULS = true;
                }
                else if (flags.IsJustR_SLS)
                {
                    tempDeltaF[0] = (step + 1) * stepValue - r_SLS;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsJustR_SLS = false;
                    step += 1;
                }
                else if (flags.IsJustR_DLS)
                {
                    tempDeltaF[0] = (step + 1) * stepValue - r_DLS;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsJustR_DLS = false;
                    step += 1;
                }
                else
                {
                    F += DeltaF;
                    R -= DeltaF;
                    step += 1;
                }
            }
            else // 引抜側 (pn == 1)
            {
                if ((step + 1) * stepValue < rt_SLS && !flags.IsRt_SLS)
                {
                    tempDeltaF[0] = rt_SLS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsRt_SLS = true;
                    flags.IsJustRt_SLS = true;
                }
                else if ((step + 1) * stepValue < rt_DLS && !flags.IsRt_DLS)
                {
                    tempDeltaF[0] = rt_DLS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsRt_DLS = true;
                    flags.IsJustRt_DLS = true;
                }
                else if ((step + 1) * stepValue < rt_ULS && !flags.IsRt_ULS)
                {
                    tempDeltaF[0] = rt_ULS - step * stepValue;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsRt_ULS = true;
                    flags.IsJustRt_ULS = true;
                }
                else if (flags.IsJustRt_SLS)
                {
                    tempDeltaF[0] = (step + 1) * stepValue - rt_SLS;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsJustRt_SLS = false;
                    step += 1;
                }
                else if (flags.IsJustRt_DLS)
                {
                    tempDeltaF[0] = (step + 1) * stepValue - rt_DLS;
                    F += tempDeltaF;
                    R -= tempDeltaF;
                    flags.IsJustRt_DLS = false;
                    step += 1;
                }
                else
                {
                    F += DeltaF;
                    R -= DeltaF;
                    step += 1;
                }
            }
        }

        // 結果記録
        private void RecordLimitState(Vector<double> F, Vector<double> Rz, Vector<double> X, double rzToe, bool isPositive)
        {
            if (isPositive)
            {
                FsLimit.Add(F);
                RsLimit.Add(Rz);
                DsLimit.Add(X);
                RzToesLimit.Add(rzToe);
            }
            else
            {
                FsLimit.Insert(0, F);
                RsLimit.Insert(0, Rz);
                DsLimit.Insert(0, X);
                RzToesLimit.Insert(0, rzToe);
            }
        }

        // 荷重範囲判定
        private bool IsWithinLoadRange(double pileTopLoad, int pn)
        {
            double r_ULS = SoilPile.R_ULS;
            double rt_ULS = SoilPile.Rt_ULS;
            return (pn == -1) ? (pileTopLoad <= r_ULS) : (pileTopLoad >= rt_ULS);
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
                    Dns = Ds[i][^1] * 1000.0,
                    DD0s = (Ds[i][0] - X0[0]) * 1000.0,
                    DDns = (Ds[i][^1] - X0[^1]) * 1000.0,
                    PileTopLoad = Fs[i][0] - Weights[0],
                    RzToe = RzToes[i],
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
                    Dns = DsLimit[i][^1] * 1000.0,
                    DD0s = (DsLimit[i][0] - X0[0]) * 1000.0,
                    DDns = (DsLimit[i][^1] - X0[^1]) * 1000.0,
                    PileTopLoad = FsLimit[i][0] - Weights[0],
                    RzToe = RzToesLimit[i],
                    Weight = PileWeight,
                    RzCircum = FsLimit[i][0] - Weights[0] + PileWeight - RzToesLimit[i]
                });
            }
        }
        private class LimitFlags
        {
            public bool IsR_SLS, IsJustR_SLS, IsR_DLS, IsJustR_DLS, IsR_ULS, IsJustR_ULS;
            public bool IsRt_SLS, IsJustRt_SLS, IsRt_DLS, IsJustRt_DLS, IsRt_ULS, IsJustRt_ULS;
            public bool IsAnyJustLimit => IsJustR_SLS || IsJustR_DLS || IsJustR_ULS || IsJustRt_SLS || IsJustRt_DLS || IsJustRt_ULS;
            public bool IsAnyJustULS => IsJustR_ULS || IsJustRt_ULS;
        }

        private void NewMain()
        {
            //節点初期状態の解析
            string state = "initial";
            X.Clear();
            Rz.Clear();
            Rz[^1] = PileWeight;

            // '<<FIND dVF>>'荷重増分
            DeltaF += Weights;

            //'最大押込力 圧縮正
            double r_ULS = SoilPile.R_ULS;
            double r_DLS = SoilPile.R_DLS;
            double r_SLS = SoilPile.R_SLS;

            //'最大引抜力 引抜負
            double rt_ULS = SoilPile.Rt_ULS;
            double rt_DLS = SoilPile.Rt_DLS;
            double rt_SLS = SoilPile.Rt_SLS;

            // 押込側荷重ステップ
            double step_p = GetStepCompression(SoilPile.Rpu, FricpMax/*, PileWeight*/);

            // 引抜側荷重ステップ
            double step_n = GetStepTension(FricmMax, PileWeight);

            //'<<SET F>>, <<SET R>>
            F += DeltaF;
            R -= DeltaF;

            int k = 1;

            if (R.L2Norm() / F.L2Norm() != 0)
            {
                // 初期状態
                ConvergenceCaluculation(state);
                X0 = X.Clone();
                F0 = F.Clone();
                Rz0 = Rz.Clone();

                X.Clear();
            }
            else
            {
                X0.Clear(); // 初期変位
                F0.Clear(); // 初期荷重
                X.Clear();
            }

            Fs.Add(F0);
            Rs.Add(Rz0);
            Ds.Add(X0);
            RzToes.Add(PileWeight);

            k += 1;

            for (int pn = -1; pn <= 1; pn += 2) // pn = -1：押込側 pn = 1：引抜側
            {
                state = (pn == -1) ? "positive" : "negative";
                // 初期状態
                X = X0.Clone(); // 変位ベクトル
                F = F0.Clone(); // 荷重ベクトル
                Rz.Clear(); /*= Rz0.Clone();*/ // 反力ベクトル
                R.Clear(); // 残留ベクトル

                //'<<FIND dVF>>'荷重増分
                DeltaF.Clear();
                DeltaF[0] = (pn == -1) ? step_p : step_n;

                int step = 0;
                bool isR_SLS = false;
                bool isJustR_SLS = false;
                bool isR_DLS = false;
                bool isJustR_DLS = false;
                bool isR_ULS = false;
                bool isJustR_ULS = false;

                bool isRt_SLS = false;
                bool isJustRt_SLS = false;
                bool isRt_DLS = false;
                bool isJustRt_DLS = false;
                bool isRt_ULS = false;
                bool isJustRt_ULS = false;

                do
                {
                    //'<<SET F>>, <<SET R>>
                    if (pn == -1 && (step + 1) * step_p > r_SLS && isR_SLS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = r_SLS - step * step_p;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isR_SLS = true;
                        isJustR_SLS = true;
                    }
                    else if (pn == -1 && (step + 1) * step_p > r_DLS && isR_DLS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = r_DLS - step * step_p;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isR_DLS = true;
                        isJustR_DLS = true;
                    }
                    else if (pn == -1 && (step + 1) * step_p > r_ULS && isR_ULS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = r_ULS - step * step_p;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isR_ULS = true;
                        isJustR_ULS = true;
                    }
                    else if (isJustR_SLS)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = (step + 1) * step_p - r_SLS;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isJustR_SLS = false;
                        step += 1;
                    }
                    else if (isJustR_DLS)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = (step + 1) * step_p - r_DLS;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isJustR_DLS = false;
                        step += 1;
                    }
                    else if (pn == 1 && (step + 1) * step_n < rt_SLS && isRt_SLS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = rt_SLS - step * step_n;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isRt_SLS = true;
                        isJustRt_SLS = true;
                    }
                    else if (pn == 1 && (step + 1) * step_n < rt_DLS && isRt_DLS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = rt_DLS - step * step_n;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isRt_DLS = true;
                        isJustRt_DLS = true;
                    }
                    else if (pn == 1 && (step + 1) * step_n < rt_ULS && isRt_ULS == false)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = rt_ULS - step * step_n;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isRt_ULS = true;
                        isJustRt_ULS = true;
                    }
                    else if (isJustRt_SLS)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = (step + 1) * step_n - rt_SLS;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isJustRt_SLS = false;
                        step += 1;
                    }
                    else if (isJustRt_DLS)
                    {
                        Vector<double> tempDeltaF = Vector<double>.Build.Dense(NodesCount, 0);
                        tempDeltaF[0] = (step + 1) * step_n - rt_DLS;
                        F += tempDeltaF;
                        R -= tempDeltaF;
                        isJustRt_DLS = false;
                        step += 1;
                    }
                    else
                    {
                        F += DeltaF;
                        R -= DeltaF;
                        step += 1;
                    }

                    ConvergenceCaluculation(state);

                    // 鉛直反力の計算
                    double settlment = X[^1];
                    double dp = SoilPile.Dp / 1000.0; // m
                    double rpu = SoilPile.Rpu; // kN
                    double alpha = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleAlpha;
                    double n = InputModel.PileBodies[SoilPile.PileBodyNo - 1].SettleN;

                    if (state == "positive")
                    {
                        Fs.Add(F);
                        Rs.Add(Rz);
                        Ds.Add(X);
                        RzToes.Add(GetRp(settlment, dp, rpu, alpha, n));

                        if (isJustR_SLS || isJustR_DLS || isJustR_ULS)
                        {
                            FsLimit.Add(F);
                            RsLimit.Add(Rz);
                            DsLimit.Add(X);
                            RzToesLimit.Add(GetRp(settlment, dp, rpu, alpha, n));
                        }
                    }
                    else
                    {
                        Fs.Insert(0, F);
                        Rs.Insert(0, Rz);
                        Ds.Insert(0, X);
                        RzToes.Insert(0, GetRp(settlment, dp, rpu, alpha, n));

                        if (isJustRt_SLS || isJustRt_DLS || isJustRt_ULS)
                        {
                            FsLimit.Insert(0, F);
                            RsLimit.Insert(0, Rz);
                            DsLimit.Insert(0, X);
                            RzToesLimit.Insert(0, GetRp(settlment, dp, rpu, alpha, n));
                        }
                    }

                    k += 1;

                    if (isJustR_ULS)
                    {
                        //isJustR_ULS = false;
                        break;
                    }
                    else if (isJustRt_ULS)
                    {
                        //isJustRt_ULS = false;
                        break;
                    }

                } while (rt_ULS <= F[0] - Weights[0] && F[0] - Weights[0] <= r_ULS);
            }

            // Fs と Ds を LoadDisplacements に追加
            for (int i = 0; i < Fs.Count; i++)
            {
                LoadDisplacements.Add(new LoadDisplacement
                {
                    F0s = Fs[i][0], // kN
                    D0s = Ds[i][0] * 1000.0, // mm
                    Dns = Ds[i][^1] * 1000.0, // mm
                    DD0s = (Ds[i][0] - X0[0]) * 1000.0, // mm 上端
                    DDns = (Ds[i][^1] - X0[^1]) * 1000.0, // mm 下端
                    PileTopLoad = Fs[i][0] - Weights[0], // kN
                    RzToe = RzToes[i], // kN
                    Weight = PileWeight, // kN
                    RzCircum = Fs[i][0] - Weights[0] + PileWeight - RzToes[i] // kN
                });
            }

            for (int i = 0; i < FsLimit.Count; i++)
            {
                LoadDisplacementsLimit.Add(new LoadDisplacement
                {
                    F0s = FsLimit[i][0],
                    D0s = DsLimit[i][0] * 1000.0, // mm
                    Dns = DsLimit[i][^1] * 1000.0, // mm
                    DD0s = (DsLimit[i][0] - X0[0]) * 1000.0, // mm
                    DDns = (DsLimit[i][^1] - X0[^1]) * 1000.0, // mm
                    PileTopLoad = FsLimit[i][0] - Weights[0], // kN
                    RzToe = RzToesLimit[i], // kN
                    Weight = PileWeight, // kN
                    RzCircum = FsLimit[i][0] - Weights[0] + PileWeight - RzToesLimit[i] // kN
                });
            }
        }

        // 収束ループ
        private void ConvergenceCaluculation(string state)
        {
            //||res|| / ||F|| < tolerance となるまで繰り返し計算を行う
            //do while ||R|| / ||F|| > tolerance
            //Find K
            //Solve Ku=-R
            //Update x = x + u
            //Find F(e), b(e), sigma(e)
            //Find T
            //Find R=T-F
            //END DO
            double norm = double.MaxValue;

            while (norm > Tolerance)
            {
                // Find K 接線剛性マトリクスの計算
                List<double> nodeTangentStiffnesses = GetTangentSoilStiffness(state, X);
                Vector<double> U = SolveDisp(BeamStiffnesses, nodeTangentStiffnesses, -R);
                X += U; // update x = x + u (配置更新)

                Vector<double> T = FindT(state);

                // Find R 残差ベクトル
                R = T - F;
                norm = R.L2Norm() / F.L2Norm();
            }
        }

        // 内力
        private Vector<double> FindT(string state)
        {
            // Find T
            Vector<double> T = GetSoilReactionVector(state, X) + Kmat * (X - X0);

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
        private static double GetStepCompression(double rpu, double fripMax/*, double pileWeight*/)
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

        public Vector<double>? GetDisplacementForGivenLoad(double pileTopForce)
        {
            // 杭体節点の鉛直変位
            Vector<double> vectorX = X0 / 1000;

            // 現在の杭・地盤状態に基づくビーム剛性リスト
            var beamStiffnesses = BeamStiffnesses;

            // 節点ごとの自重
            var weights = new List<double>();
            for (int i = 0; i < Weights.Count; i++)
                weights.Add(Weights[i]);

            // 荷重ベクトル生成
            var vectorF = GenerateForceVector(weights, pileTopForce);
            var vectorR = vectorF;
            double norm = double.MaxValue; // 初期化
            int counter = 0;
            while (norm > Tolerance)
            {
                counter += 1;
                // 現在の節点ごとの地盤剛性（割線剛性）を取得
                // ここでは初期変位X0を使い、"positive"状態で計算（必要に応じてstateを変更）
                var nodeTangentStiffnesses = GetTangentSoilStiffness("positive", vectorX);

                // 接線剛性マトリクス生成
                var tangentStiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, nodeTangentStiffnesses);

                // 変位ベクトルを解く
                var vectorU = tangentStiffnessMatrix.Solve(vectorR);
                vectorX += vectorU; // 変位更新
                var nodeSecantStiffnesses = GetTangentSoilStiffness("positive", vectorX);

                // 割線剛性マトリクス生成
                var secantStiffnessMatrix = GenerateStiffnessMatrix(beamStiffnesses, nodeSecantStiffnesses);

                var vectorT = secantStiffnessMatrix * vectorX;

                vectorR = vectorF - vectorT;
                norm = vectorR.L2Norm() / vectorF.L2Norm();

                if (Math.Abs(vectorU[0]) > 10.0 || counter > 100) { return null; }
            }
            vectorX -= X0 / 1000; // 初期変位を引いて相対変位に変換
            return vectorX;
        }
    }
}

