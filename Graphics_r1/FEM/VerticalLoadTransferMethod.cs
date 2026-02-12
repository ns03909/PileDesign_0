using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.FEM
{
    /// <summary>
    /// 解析制御モード
    /// </summary>
    public enum AnalysisControlMode
    {
        /// <summary>荷重制御法（従来方式）</summary>
        LoadControl,
        /// <summary>変位制御法（極限状態でも安定）</summary>
        DisplacementControl
    }

    /// <summary>
    /// 杭の鉛直荷重伝達解析を行うクラス
    /// </summary>
    public class VerticalLoadTransferMethod : BaseModel
    {

        #region Fields

        /// <summary>解析制御モード</summary>
        private AnalysisControlMode _analysisMode = AnalysisControlMode.LoadControl;

        private readonly InputModel _inputModel;
        public InputModel InputModel => _inputModel;

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

        private readonly double Tolerance = Math.Pow(10, -6);
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
        public VerticalLoadTransferMethod(InputModel inputModel, SoilPile _soilPile,
            AnalysisControlMode analysisMode = AnalysisControlMode.LoadControl)
        {
            _inputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
            this.soilPile = _soilPile;
            _analysisMode = analysisMode;
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
                    double rpu = soilPile.SettleRpu;
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
                    double settlement = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    double dp = soilPile.Dp / 1000.0; //m
                    double rpu = soilPile.SettleRpu; // kN
                    double alpha = InputModel.PileBodies[^1].SettleAlpha;
                    double n = InputModel.PileBodies[^1].SettleN;

                    soilReaction += GetSecantStiffnessPileToeFromSettlement(settlement, dp, rpu, alpha, n) * settlement;
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

                    double ktan = GetTangentStiffnessPilePerimeter(state, s, aPC, aPT, tau1, tau2, S1, S2, psiL);
                    stiffness += ktan;
                }

                if (i == pileNodesCount - 1) // 杭先端抵抗
                {
                    double settlment = xs[2 * i] - xs[2 * i + 1]; // 相対変位
                    double dp = soilPile.Dp / 1000.0; // m
                    double rpu = soilPile.SettleRpu; // kN
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
            else if (rpu <= 1e-12) // rpu≈0のガード（0除算防止）
            {
                ktan = 0;
            }
            else
            {
                double stan = 0.1 * dp * (alpha * (1 / rpu) + n * (1 - alpha) * (1 / rpu) * Math.Pow(rp / rpu, n - 1));
                ktan = (stan > 1e-15) ? 1 / stan : 0; // stan≈0のガード（オーバーフロー防止）
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
            else if (rpu <= 1e-12) // rpu≈0のガード
            {
                ktan = 0;
            }
            else
            {
                double rp = GetRp(settlement, dp, rpu, alpha, n);
                double stan = 0.1 * dp * (alpha * (1 / rpu) + n * (1 - alpha) * (1 / rpu) * Math.Pow(rp / rpu, n - 1));
                ktan = (stan > 1e-15) ? 1 / stan : 0; // stan≈0のガード
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
                double safeSettlement = Math.Max(settlment, 1e-12); // 0除算防止
                ksec = GetRp(settlment, dp, rpu, alpha, n) / safeSettlement;
            }
            return ksec;
        }

        // sTarget -> Rp メソッド（ニュートンラフソン法）
        // 杭先端支持力は数式に従って上昇し続ける（極限支持力rpuを超えても上昇）
        private static double GetRp(double sTarget, double dp, double rpu, double alpha, double n)
        {
            // 早期リターン
            if (sTarget <= 0 || rpu <= 1e-12) return 0;

            // 極限沈下量（杭径の10%）
            double ultimateSettlement = 0.1 * dp;

            // 良好な初期値を設定（沈下量に応じて調整、rpuを超える場合も考慮）
            double ratio = sTarget / ultimateSettlement;
            double rp = rpu * ratio; // 沈下量比率に応じた初期値

            const int maxIter = 100;
            const double tolerance = 1e-8;

            for (int i = 0; i < maxIter; i++)
            {
                double sn = GetSettlementPileToe(dp, rp, rpu, alpha, n);
                double ktan = GetTangentStiffnessPileToeFromRp(rp, dp, rpu, alpha, n);

                // 収束判定
                if (Math.Abs(sn - sTarget) <= tolerance) break;

                // ktan≈0の場合は更新できないので打ち切り
                if (Math.Abs(ktan) < 1e-15) break;

                rp -= (sn - sTarget) * ktan;

                // rpは0以上（負の支持力は物理的に意味がない）
                rp = Math.Max(0, rp);
            }

            return rp;
        }

        // 杭周面の接線剛性を返すメソッド
        private static double GetTangentStiffnessPilePerimeter(
            string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
        {
            //double PI = Math.PI;
            double ktan;

            // S1が0または非常に小さい場合の安全対策（最小値 1mm = 0.001m として計算）
            const double minS = 0.001; // 1mm
            double safeS1 = Math.Max(S1, minS);
            double safeS2 = Math.Max(S2, safeS1 + minS); // S2はS1より大きくする

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
            else if (Math.Abs(s) <= safeS1)
            {
                ktan = tau1 / safeS1 * psiL;
            }
            else if (Math.Abs(s) <= safeS2)
            {
                double denom = safeS2 - safeS1;
                if (Math.Abs(denom) < minS) denom = minS;
                ktan = (tau2 - tau1) / denom * psiL;
            }
            else
            {
                // 塑性状態では剛性をほぼゼロに（tau2で抵抗力一定）
                // 数値安定性のため極小値を残す
                ktan = tau1 / safeS1 * psiL * 0.001;
            }
            return ktan;
        }

        // 杭周面の割線剛性を返すメソッド
        private static double GetSecantStiffnessPilePerimeter(
            string state, double s, bool aPC, bool aPT, double tau1, double tau2, double S1, double S2, double psiL)
        {
            double ksec;

            // S1が0または非常に小さい場合の安全対策（最小値 1mm = 0.001m として計算）
            const double minS = 0.001; // 1mm
            double safeS1 = Math.Max(S1, minS);
            double safeS2 = Math.Max(S2, safeS1 + minS); // S2はS1より大きくする

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
            else if (Math.Abs(s) <= safeS1)
            {
                ksec = tau1 / safeS1 * psiL;
            }
            else if (Math.Abs(s) <= safeS2)
            {
                double absS = Math.Max(Math.Abs(s), minS); // 0除算防止
                double denom = safeS2 - safeS1;
                if (Math.Abs(denom) < minS) denom = minS;
                ksec = ((tau2 - tau1) / denom * (Math.Abs(s) - safeS1) + tau1) / absS * psiL;
            }
            else
            {
                double absS = Math.Max(Math.Abs(s), minS); // 0除算防止
                ksec = tau2 / absS * psiL;
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

                if (_analysisMode == AnalysisControlMode.LoadControl)
                {
                    RunLoadIncrementAnalysis(state, pn);
                }
                else
                {
                    RunDisplacementIncrementAnalysis(state, pn);
                }
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
                // TryConvergenceCalculationを使用して、収束失敗時もエラーではなく警告として処理
                bool converged = TryConvergenceCalculation(state);
                if (!converged)
                {
                    // 収束しなかった場合は警告をトレースに出力し、計算は継続
                    double norm = VectorR.L2Norm() / VectorF.L2Norm();
                    System.Diagnostics.Trace.TraceWarning(
                        $"初期状態の収束計算が完了しませんでした。残差ノルム: {norm:E3}。計算結果の精度が低下する可能性があります。");
                }

                double settlement = VectorX[^2];
                double dp = soilPile.Dp / 1000.0;
                double rpu = soilPile.SettleRpu;
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

            double step = (pn == -1) ? GetStepCompression(soilPile.SettleRpu, FricpMax) : GetStepTension(FricmMax, PileWeight);
            double minStep = Math.Abs(step) * 0.01; // 最小ステップ（初期の1%）
            VectorDF.Clear();
            VectorDF[0] = step;

            var limitFlags = new LimitFlags();

            do
            {
                // 収束失敗時のバックアップ用に状態を保存
                var backupVectorX = VectorX.Clone();
                var backupVectorF = VectorF.Clone();

                ApplyLoadIncrements(pn, limitFlags);

                bool converged = TryConvergenceCalculation(state);

                if (!converged)
                {
                    // 収束失敗：状態を復元してステップを半分に
                    VectorX = backupVectorX;
                    VectorF = backupVectorF;

                    double currentStep = Math.Abs(VectorDF[0]);
                    if (currentStep <= minStep)
                    {
                        // ステップが最小に達しても収束しない場合 → 極限状態に到達したと判断
                        break;
                    }

                    // ステップを半分に縮小
                    VectorDF[0] = VectorDF[0] / 2;
                    continue; // 縮小したステップで再試行
                }

                double settlement = VectorX[^2];
                double dp = soilPile.Dp / 1000.0;
                double rpu = soilPile.SettleRpu;
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

        #region 変位制御法

        /// <summary>
        /// 変位制御による増分解析（極限状態でも安定）
        /// </summary>
        private void RunDisplacementIncrementAnalysis(string state, int pn)
        {
            VectorX = VectorX0.Clone();
            VectorF = VectorF0.Clone();
            VectorRz.Clear();
            VectorR.Clear();

            // 目標変位を設定
            // 圧縮側: 杭径10%まで（極限支持力の定義）、さらに余裕を持って15%まで
            // 引張側: 杭径5%程度
            double dp = soilPile.Dp / 1000.0; // m
            double targetDisp = (pn == -1) ? dp * 0.15 : -dp * 0.10;
            int numSteps = 30;  // 30ステップで目標変位に到達
            double dispStep = targetDisp / numSteps;

            var limitFlags = new LimitFlags();

            for (int step = 1; step <= numSteps; step++)
            {
                // 目標杭頭変位（杭DOF = VectorX[0]）
                double targetHeadDisp = VectorX0[0] + dispStep * step * pn * (-1);

                // 収束計算（変位制御）
                bool converged = TryDisplacementControlConvergence(state, targetHeadDisp);

                if (!converged)
                {
                    break;
                }

                // 杭頭反力から荷重を計算
                double headLoad = CalculateHeadReaction(state);

                // VectorFを更新（反力から逆算）
                VectorF.Clear();
                VectorF[0] = headLoad + Weights[0];
                for (int i = 0; i < pileNodesCount; i++)
                {
                    VectorF[2 * i] += Weights[i];
                }

                // 先端抵抗力を計算
                double settlement = VectorX[^2];
                double rpu = soilPile.SettleRpu;
                double alpha = InputModel.PileBodies[^1].SettleAlpha;
                double n = InputModel.PileBodies[^1].SettleN;
                double rzToe = GetRp(settlement, dp, rpu, alpha, n);

                // 限界状態フラグの更新
                UpdateLimitFlags(headLoad, pn, limitFlags);

                // 結果を記録
                if (state == "positive")
                {
                    Fs.Add(VectorF.Clone());
                    Rs.Add(VectorRz.Clone());
                    Ds.Add(VectorX.Clone());
                    RzToes.Add(rzToe);

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(VectorF, VectorRz, VectorX, rzToe, true);
                }
                else
                {
                    Fs.Insert(0, VectorF.Clone());
                    Rs.Insert(0, VectorRz.Clone());
                    Ds.Insert(0, VectorX.Clone());
                    RzToes.Insert(0, rzToe);

                    if (limitFlags.IsAnyJustLimit)
                        RecordLimitState(VectorF, VectorRz, VectorX, rzToe, false);
                }

                // 極限状態到達でループ終了
                if (limitFlags.IsAnyJustULS)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 変位制御用の収束計算（直接剛性法）
        /// 杭頭変位を固定し、セカント剛性マトリクスで直接解く
        /// </summary>
        private bool TryDisplacementControlConvergence(string state, double targetHeadDisp)
        {
            double norm = double.MaxValue;
            double prevNorm = double.MaxValue;
            int iterationCount = 0;
            const int maxIterations = 500;

            // 杭頭変位を目標値に設定
            VectorX[0] = targetHeadDisp;

            // 外力ベクトル（自重のみ）
            var vectorF_gravity = Vector<double>.Build.Dense(nodesCount);
            for (int i = 0; i < pileNodesCount; i++)
            {
                vectorF_gravity[2 * i] = Weights[i];
            }

            while (iterationCount < maxIterations)
            {
                iterationCount++;

                // セカント剛性マトリクスを生成
                var soilStiffnesses = GetSecantSoilStiffness(state, VectorX);
                var stiffnessMatrix = GenerateStiffnessMatrix(BeamStiffnesses, soilStiffnesses);

                // 右辺ベクトル = 外力（自重）
                var rhs = vectorF_gravity.Clone();

                // 杭頭変位の境界条件を適用（ペナルティ法）
                double penalty = 1e12;
                stiffnessMatrix[0, 0] += penalty;
                rhs[0] += penalty * targetHeadDisp;

                // 土DOFの境界条件（強制変位=0）
                for (int i = 0; i < pileNodesCount; i++)
                {
                    int soilIdx = 2 * i + 1;
                    stiffnessMatrix[soilIdx, soilIdx] += penalty;
                    rhs[soilIdx] += penalty * ForcedSoilDispList[i];
                }

                // 連立方程式を解く
                Vector<double> newX;
                try
                {
                    newX = stiffnessMatrix.Solve(rhs);
                }
                catch
                {
                    return false;
                }

                if (!newX.ForAll(double.IsFinite))
                {
                    return false;
                }

                // 変位の変化量を計算
                double deltaSum = 0;
                double dispSum = 0;
                for (int i = 0; i < pileNodesCount; i++)
                {
                    int pileIdx = 2 * i;
                    double delta = newX[pileIdx] - VectorX[pileIdx];
                    deltaSum += delta * delta;
                    dispSum += VectorX[pileIdx] * VectorX[pileIdx];
                }
                norm = Math.Sqrt(deltaSum) / Math.Max(Math.Sqrt(dispSum), 1e-6);

                // 緩和係数で更新（収束を安定化）
                double relaxation = 0.5;
                for (int i = 0; i < nodesCount; i++)
                {
                    VectorX[i] = (1 - relaxation) * VectorX[i] + relaxation * newX[i];
                }

                // 杭頭変位を強制
                VectorX[0] = targetHeadDisp;

                // 土DOFを強制
                for (int i = 0; i < pileNodesCount; i++)
                {
                    VectorX[2 * i + 1] = ForcedSoilDispList[i];
                }

                // 収束判定
                if (norm < 1e-6)
                {
                    return true;
                }

                // 発散検出
                if (norm > prevNorm * 10 && iterationCount > 10)
                {
                    break;
                }
                prevNorm = norm;
            }

            // 最大反復でも収束しなかった場合、緩和条件でチェック
            if (norm < 0.001)  // 0.1%以下なら実用上OK
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 変位境界条件（杭頭固定 + 土固定）を剛性マトリクスと残差ベクトルに適用
        /// </summary>
        private void ApplyDisplacementBoundaryConditions(Matrix<double> stiffnessMatrix, Vector<double> residualVector, double targetHeadDisp)
        {
            // 1. 杭頭DOF (index=0) を固定
            // 残差ベクトルの杭頭成分を0に
            residualVector[0] = 0;

            // 剛性マトリクスの杭頭行・列をクリア、対角を1に
            for (int j = 0; j < nodesCount; j++)
            {
                stiffnessMatrix[0, j] = 0;
                stiffnessMatrix[j, 0] = 0;
            }
            stiffnessMatrix[0, 0] = 1.0;

            // 2. 土DOF (奇数インデックス) を固定
            for (int i = 0; i < pileNodesCount; i++)
            {
                int dofIndex = 2 * i + 1;
                residualVector[dofIndex] = 0;

                for (int j = 0; j < nodesCount; j++)
                {
                    stiffnessMatrix[dofIndex, j] = 0;
                    stiffnessMatrix[j, dofIndex] = 0;
                }
                stiffnessMatrix[dofIndex, dofIndex] = 1.0;
            }
        }

        /// <summary>
        /// 杭頭反力（荷重）を計算
        /// </summary>
        private double CalculateHeadReaction(string state)
        {
            // 地盤反力ベクトルを取得
            Vector<double> soilReactions = GetSoilReactionVector(state, VectorX);

            // 杭頭の釣り合いから荷重を求める
            // P = Σ(地盤反力) - 杭自重
            double totalSoilReaction = 0;
            for (int i = 0; i < pileNodesCount; i++)
            {
                totalSoilReaction += soilReactions[2 * i];
            }

            // 杭頭荷重 = 全地盤反力 - 杭自重
            return totalSoilReaction - PileWeight;
        }

        /// <summary>
        /// 限界状態フラグの更新
        /// </summary>
        private void UpdateLimitFlags(double load, int pn, LimitFlags flags)
        {
            double r_SLS = soilPile.R_SLS;
            double r_DLS = soilPile.R_DLS;
            double r_ULS = soilPile.R_ULS;
            double rt_SLS = soilPile.Rt_SLS;
            double rt_DLS = soilPile.Rt_DLS;
            double rt_ULS = soilPile.Rt_ULS;

            if (pn == -1)  // 圧縮側
            {
                if (!flags.IsR_SLS && load >= r_SLS) flags.IsJustR_SLS = true;
                if (!flags.IsR_DLS && load >= r_DLS) flags.IsJustR_DLS = true;
                if (!flags.IsR_ULS && load >= r_ULS) flags.IsJustR_ULS = true;

                if (flags.IsJustR_SLS) flags.IsR_SLS = true;
                if (flags.IsJustR_DLS) flags.IsR_DLS = true;
                if (flags.IsJustR_ULS) flags.IsR_ULS = true;

                flags.IsJustR_SLS = false;
                flags.IsJustR_DLS = false;
                flags.IsJustR_ULS = false;
            }
            else  // 引張側
            {
                // rt_SLS, rt_DLS, rt_ULSは負の値で格納されている
                if (!flags.IsRt_SLS && load <= rt_SLS) flags.IsJustRt_SLS = true;
                if (!flags.IsRt_DLS && load <= rt_DLS) flags.IsJustRt_DLS = true;
                if (!flags.IsRt_ULS && load <= rt_ULS) flags.IsJustRt_ULS = true;

                if (flags.IsJustRt_SLS) flags.IsRt_SLS = true;
                if (flags.IsJustRt_DLS) flags.IsRt_DLS = true;
                if (flags.IsJustRt_ULS) flags.IsRt_ULS = true;

                flags.IsJustRt_SLS = false;
                flags.IsJustRt_DLS = false;
                flags.IsJustRt_ULS = false;
            }
        }

        #endregion

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
                // rt_SLS, rt_DLS, rt_ULSは負の値で格納されている
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
        // Rt_ULS等は負の値で格納されている（引張は負）
        private bool IsWithinLoadRange(double pileTopLoad, int pn)
        {
            double r_ULS = soilPile.R_ULS;
            double rt_ULS = soilPile.Rt_ULS;
            // 圧縮側: 荷重がR_ULS以下なら継続
            // 引張側: 荷重がRt_ULS以上なら継続（rt_ULSは負値なので、荷重がより負になるまで継続）
            return (pn == -1) ? (pileTopLoad <= r_ULS) : (pileTopLoad >= rt_ULS);
        }


        private class LimitFlags
        {
            public bool IsR_SLS, IsJustR_SLS, IsR_DLS, IsJustR_DLS, IsR_ULS, IsJustR_ULS;
            public bool IsRt_SLS, IsJustRt_SLS, IsRt_DLS, IsJustRt_DLS, IsRt_ULS, IsJustRt_ULS;
            public bool IsAnyJustLimit => IsJustR_SLS || IsJustR_DLS || IsJustR_ULS || IsJustRt_SLS || IsJustRt_DLS || IsJustRt_ULS;
            public bool IsAnyJustULS => IsJustR_ULS || IsJustRt_ULS;
        }


        // 収束ループ（例外なしで結果を返す版）
        private bool TryConvergenceCalculation(string state)
        {
            double norm = double.MaxValue;
            double prevNorm = double.MaxValue;
            int iterationCount = 0;
            double damping = 0.8;
            int stagnationCount = 0;
            const int maxIterations = 200;

            // 許容値を動的に調整（反復が進むにつれて少し緩和）
            double currentTolerance = Tolerance;

            while (norm > currentTolerance)
            {
                iterationCount += 1;

                // 反復回数が多くなったら許容値を段階的に緩和（最大100倍まで）
                if (iterationCount > 100)
                {
                    currentTolerance = Tolerance * Math.Min(100.0, 1.0 + (iterationCount - 100) * 0.5);
                }

                if (iterationCount >= maxIterations)
                {
                    // 許容値を大幅に緩和しても収束しない場合のみ失敗
                    if (norm > Tolerance * 1000)
                    {
                        return false;
                    }
                    // norm <= Tolerance * 1000 なら許容範囲内として成功扱い
                    return true;
                }

                List<double> soilTangentStiffnesses = GetTangentSoilStiffness(state, VectorX);
                Vector<double> U = SolveDisp(BeamStiffnesses, soilTangentStiffnesses, -VectorR, VectorX);

                if (!U.ForAll(double.IsFinite))
                {
                    return false;
                }

                VectorX += damping * U;
                Vector<double> T = FindT(state);
                VectorR = T - VectorF;
                norm = VectorR.L2Norm() / VectorF.L2Norm();

                if (!double.IsFinite(norm))
                {
                    return false;
                }

                // 適応的減衰係数調整（より積極的に）
                if (norm > prevNorm * 0.99)
                {
                    stagnationCount++;
                    if (stagnationCount >= 3) // 3回停滞で調整（以前は5回）
                    {
                        damping = Math.Max(0.05, damping * 0.6); // より大きく減衰
                        stagnationCount = 0;
                    }
                }
                else
                {
                    stagnationCount = 0;
                    if (norm < prevNorm * 0.5 && damping < 0.95)
                    {
                        damping = Math.Min(0.95, damping * 1.2); // より大きく回復
                    }
                }
                prevNorm = norm;
            }
            return true;
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
            // 引張容量 = 摩擦力 - 杭自重
            // 摩擦力が杭自重より大きい場合でも、最小ステップで引張解析を実行
            double pmin = fricmMax - pileWeight;

            // 引張容量がない場合（摩擦力が自重を上回る場合）でも、
            // 負の変位（浮き上がり）方向の解析を行うため最小ステップを使用
            if (pmin >= 0)
            {
                // 最小ステップで解析（摩擦力 > 自重の場合）
                return -10;
            }

            return pmin switch
            {
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
        /// 指定した杭頭荷重に対する変位ベクトルを返す
        /// LoadDisplacementsの結果から線形補間して求める（荷重制御解析結果を利用）
        /// </summary>
        public Vector<double>? GetDisplacementForGivenLoad(double pileTopForce)
        {
            // LoadDisplacementsから補間して沈下量を求める
            if (LoadDisplacements == null || LoadDisplacements.Count < 2)
                return null;

            // 荷重でソートされたリストを作成
            var sortedList = LoadDisplacements.OrderBy(ld => ld.PileTopLoad).ToList();

            // 範囲外チェック
            if (pileTopForce <= sortedList[0].PileTopLoad)
            {
                // 最小荷重以下の場合は最小値を返す
                var result = Vector<double>.Build.Dense(nodesCount);
                result[0] = sortedList[0].DD0s / 1000.0; // mm -> m
                result[^2] = sortedList[0].DDns / 1000.0; // mm -> m
                return result;
            }

            if (pileTopForce >= sortedList[^1].PileTopLoad)
            {
                // 最大荷重以上の場合は最大値を返す
                var result = Vector<double>.Build.Dense(nodesCount);
                result[0] = sortedList[^1].DD0s / 1000.0; // mm -> m
                result[^2] = sortedList[^1].DDns / 1000.0; // mm -> m
                return result;
            }

            // 線形補間
            for (int i = 0; i < sortedList.Count - 1; i++)
            {
                var lower = sortedList[i];
                var upper = sortedList[i + 1];

                if (pileTopForce >= lower.PileTopLoad && pileTopForce <= upper.PileTopLoad)
                {
                    double ratio = (pileTopForce - lower.PileTopLoad) / (upper.PileTopLoad - lower.PileTopLoad);
                    double d0s = lower.DD0s + ratio * (upper.DD0s - lower.DD0s);
                    double dns = lower.DDns + ratio * (upper.DDns - lower.DDns);

                    var result = Vector<double>.Build.Dense(nodesCount);
                    result[0] = d0s / 1000.0; // mm -> m
                    result[^2] = dns / 1000.0; // mm -> m
                    return result;
                }
            }

            return null;
        }

    }
}

