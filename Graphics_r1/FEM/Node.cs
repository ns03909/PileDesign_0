using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

namespace PileDesign.FEM
{
    //public class Node(string name, double coordX, double coordY, double coordZ) : BaseModel
    public class Node : BaseModel
    {
        public string Name { get; set; }
        public Point3D Coord { get; set; }

        public Boundary Boundary { get; set; } = new(false, false, false, false, false, false); // 拘束条件
        public int[] EquationNumber { get; set; } = new int[6];

        // 節点荷重
        public NodeLoad IncrementalLoad { get; set; } = new(0, 0, 0, 0, 0, 0);   // 節点荷重
        public NodeLoad CumulativedLoad { get; set; } = new(0, 0, 0, 0, 0, 0); // 節点荷重

        public NodeSpring TangentSpring { get; set; } = new(0, 0, 0, 0, 0, 0);  // 節点接線ばね
        public NodeSpring SecantSpring { get; set; } = new(0, 0, 0, 0, 0, 0);  // 節点割線ばね

        // 節点変位
        public NodeDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0);  // 節点変位
        public NodeDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0);   // 節点累積変位

        public bool IsLoaded { get; set; } = false;
        public bool IsSpringed { get; set; } = false;

        // 節点反力
        public NodeReaction IncrementalReaction { get; set; } = new(0, 0, 0, 0, 0, 0);    // 節点反力
        public NodeReaction CumulativeReaction { get; set; } = new(0, 0, 0, 0, 0, 0);   // 節点累積反力

        // 地盤変位
        public NodeDisp SoilDisp { get; set; } // 基準変位ベクトル
        public NodeDisp SoilDispIncrement { get; set; } // 基準変位ベクトル

        //public Vector3D SlaveArm { get; set; } = new(0, 0, 0);

        // マスター
        public Node[] MasterNodes { get; set; } = new Node[6];
        public Vector3S SlaveArm { get; set; } = new(0, 0, 0);

        // 接点ばね
        public NodeLoad CumulativeSpringForce { get; set; } = new(0, 0, 0, 0, 0, 0);     // 節点累積合成力
        public NodeLoad IncrementalSpringForce { get; set; } = new(0, 0, 0, 0, 0, 0);     // 節点累積合成力

        // 強制変位
        public NodeDisp CumulativeForcedDisp { get; set; } = new(0, 0, 0, 0, 0, 0);
        public NodeDisp IncrementalForcedDisp { get; set; } = new(0, 0, 0, 0, 0, 0);

        public bool IsForcedDisped { get; set; } = false; // 強制変位が設定されているかどうか

        [JsonIgnore]
        public Matrix<double> TransferMatrix { get; set; } = Matrix<double>.Build.DenseIdentity(6);

        // シリアライズ用プロパティ
        //[JsonPropertyName("TransferMatrixArray")]
        //public double[,] TransferMatrixArray
        //{
        //    get => TransferMatrix?.ToArray();
        //    set => TransferMatrix = value != null ? Matrix<double>.Build.DenseOfArray(value) : null;
        //}

        public ObservableCollection<NodeResult> NodeResults { get; set; } = [];

        // 
        public NodeResult GetNodeResult(AnaModel anaModel, LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step = -1)
        {
            if (step == -1)
            {
                step = anaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
            }

            foreach (NodeResult nodeResult in NodeResults)
            {
                if (loadCase == nodeResult.LoadCase &&
                    loadCombination == nodeResult.LoadCombination &&
                    isLiquefaction == nodeResult.IsLiquefaction &&
                    step == nodeResult.Step)
                {
                    return nodeResult;
                }
            }

            //MessageBox.Show("Analysis result Not found.");
            return null;
        }

        // initializer
        public void SetNodeInfo(string name, double coordX, double coordY, double coordZ)
        {
            Name = name;
            Coord = new Point3D(coordX, coordY, coordZ);
        }

        // 強制変位の設定メソッド
        public void SetIsForcedDisped(bool isForcedDisped)
        {
            IsForcedDisped = isForcedDisped;
        }

        // 強制変位の設定メソッド
        public void SetCumulativeForcedDisp(NodeDisp forcedDisp)
        {
            CumulativeForcedDisp = forcedDisp;
            IsForcedDisped = true; // 強制変位が設定されたことを示す
        }

        // 強制変位の設定メソッド
        public void SetIncrementalForcedDisp(NodeDisp forcedDisp)
        {
            IncrementalForcedDisp = forcedDisp;
            IsForcedDisped = true; // 強制変位が設定されたことを示す
        }

        // 境界条件の設定メソッド
        public void SetBoundary(Boundary boundary)
        {
            Boundary = boundary;
        }

        // 方程式番号の設定メソッド
        public void SetEquationNumber(int index, int equationNumber)
        {
            EquationNumber[index] = equationNumber;
        }

        // 
        public void SetIncrementalLoad(NodeLoad load)
        {
            SetLoad(load, true);
        }

        public void UpdateCumulativeLoad()
        {
            CumulativedLoad += IncrementalLoad;
        }

        public void SetCumulativeLoad(NodeLoad load)
        {
            SetLoad(load, false);
        }

        // 節点荷重のセット(変更)
        private void SetLoad(NodeLoad load, bool isTan)
        {
            if (isTan)
            {
                IncrementalLoad = load;
            }
            else
            {
                CumulativedLoad = load;
            }
            IsLoaded = true;
        }

        // 地盤変位増分のセット
        public void SetSoilDispIncrement(NodeDisp fdisp)
        {
            SoilDispIncrement = fdisp;
        }

        // 地盤変位のセット
        public void SetSoilDisp(NodeDisp fdisp)
        {
            SoilDisp = fdisp;
        }

        // 初期変位状態のセット
        public void InitializeDisp()
        {
            IncrementalDisp = new(0, 0, 0, 0, 0, 0);
            CumulativeDisp = new(0, 0, 0, 0, 0, 0);
        }


        // 境界条件の取得メソッド
        public bool GetBoundary(int index)
        {
            return index switch
            {
                0 => Boundary.Ux,
                1 => Boundary.Uy,
                2 => Boundary.Uz,
                3 => Boundary.Rx,
                4 => Boundary.Ry,
                5 => Boundary.Rz,
                _ => throw new Exception("不正なインデックス"),
            };
        }

        public float GetSlaveArm(int i)
        {
            return i switch
            {
                0 => (float)SlaveArm.X,
                1 => (float)SlaveArm.Y,
                2 => (float)SlaveArm.Z,
                _ => throw new Exception("不正なインデックス"),
            };


        }

        public void SetDisp2(Vector<double> dd/*, AnaModel anaModel*/)
        {
            //(Node[] masterNodes, Vector3D armVector) = anaModel.GetMasterNodesArmVector(this);

            double[] ddisp = new double[6];

            // クロス項の定義: (クロス先index, arm成分, 符号)
            (int crossIdx, Func<Vector3S, double> arm, double sign)[][] crossTerms =
            [
                // 0: Ux
                [(4, v => v.Z, 1.0), (5, v => v.Y, -1.0)],
                // 1: Uy
                [(5, v => v.X, 1.0), (3, v => v.Z, -1.0)],
                // 2: Uz
                [(3, v => v.Y, 1.0), (4, v => v.X, -1.0)],
                // 3: Tx
                [],
                // 4: Ty
                [],
                // 5: Tz
                [],
            ];

            for (int i = 0; i < 6; i++)
            {
                int e_num = EquationNumber[i];
                if (MasterNodes[i] != null)
                {
                    int eq = MasterNodes[i].EquationNumber[i];
                    ddisp[i] = dd[eq];

                    // クロス項の加算
                    foreach (var (crossIdx, arm, sign) in crossTerms[i])
                    {
                        if (MasterNodes[crossIdx] != null)
                        {
                            int eqCross = MasterNodes[i].EquationNumber[crossIdx];
                            ddisp[i] += dd[eqCross] * arm(SlaveArm) * sign;
                        }
                    }
                }
                else if (e_num >= 0)
                {
                    ddisp[i] = dd[e_num];
                }
                else
                {
                    ddisp[i] = 0;
                }
            }

            // ddisp: double[6]  (新しい増分変位)
            double[] cum =
            [
                CumulativeDisp.Ux + ddisp[0],
                CumulativeDisp.Uy + ddisp[1],
                CumulativeDisp.Uz + ddisp[2],
                CumulativeDisp.Rx + ddisp[3],
                CumulativeDisp.Ry + ddisp[4],
                CumulativeDisp.Rz + ddisp[5]
            ];

            IncrementalDisp = new NodeDisp(ddisp[0], ddisp[1], ddisp[2], ddisp[3], ddisp[4], ddisp[5]);
            CumulativeDisp = new NodeDisp(cum[0], cum[1], cum[2], cum[3], cum[4], cum[5]);
        }
        //節点変位の累加変位Node.Dispへの追加
        public void SetDisp(Vector<double> dd/*, AnaModel anaModel*/)
        {
            double[] ddisp = new double[6];

            int e_num = EquationNumber[0];

            if (MasterNodes[0] != null)
            {
                int e_num_x = MasterNodes[0].EquationNumber[0];
                ddisp[0] = dd[e_num_x];
                if (MasterNodes[4] != null)
                {
                    int e_num_yy = MasterNodes[0].EquationNumber[4];
                    ddisp[0] += dd[e_num_yy] * SlaveArm.Z;
                }
                if (MasterNodes[5] != null)
                {
                    int e_num_zz = MasterNodes[0].EquationNumber[5];
                    ddisp[0] += dd[e_num_zz] * -SlaveArm.Y;
                }
            }
            else if (e_num >= 0)
            {
                ddisp[0] = dd[e_num];
            }
            else
            {
                ddisp[0] = 0;
            }

            e_num = EquationNumber[1];

            if (MasterNodes[1] != null)
            {
                int e_num_y = MasterNodes[1].EquationNumber[1];
                ddisp[1] += dd[e_num_y];
                if (MasterNodes[5] != null)
                {
                    int e_num_zz = MasterNodes[1].EquationNumber[4];
                    ddisp[1] += dd[e_num_zz] * SlaveArm.X;
                }

                if (MasterNodes[3] != null)
                {
                    int e_num_xx = MasterNodes[1].EquationNumber[5];
                    ddisp[1] += dd[e_num_xx] * -SlaveArm.Z;
                }
            }
            else if (e_num <= 0)
            {
                ddisp[1] = dd[e_num];
            }
            else
            {
                ddisp[1] = 0;
            }

            e_num = EquationNumber[2];
            if (MasterNodes[2] != null)
            {
                int e_num_z = MasterNodes[2].EquationNumber[2];
                ddisp[2] += dd[e_num_z];
                if (MasterNodes[3] != null)
                {
                    int e_num_xx = MasterNodes[2].EquationNumber[3];
                    ddisp[2] += dd[e_num_xx] * SlaveArm.Y;
                }
                if (MasterNodes[4] != null)
                {
                    int e_num_yy = MasterNodes[2].EquationNumber[4];
                    ddisp[2] += dd[e_num_yy] * -SlaveArm.X;
                }
            }
            else if (e_num >= 0)
            {
                ddisp[2] = dd[e_num];
            }
            else
            {
                ddisp[2] = 0;
            }

            e_num = EquationNumber[3];
            if (MasterNodes[3] != null)
            {
                int e_num_xx = MasterNodes[3].EquationNumber[3];
                ddisp[3] = dd[e_num_xx];
            }
            else if (e_num >= 0)
            {
                ddisp[3] = dd[e_num];
            }
            else
            {
                ddisp[3] = 0;
            }

            e_num = EquationNumber[4];
            if (MasterNodes[4] != null)
            {
                int e_num_yy = MasterNodes[4].EquationNumber[4];
                ddisp[4] = dd[e_num_yy];
            }
            else if (e_num >= 0)
            {
                ddisp[4] = dd[e_num];
            }
            else
            {
                ddisp[4] = 0;
            }

            e_num = EquationNumber[5];
            if (MasterNodes[5] != null)
            {
                int e_num_zz = MasterNodes[5].EquationNumber[5];
                ddisp[5] = dd[e_num_zz];
            }
            else if (e_num >= 0)
            {
                ddisp[5] = dd[e_num];
            }
            else
            {
                ddisp[5] = 0;
            }

            // ddisp: double[6]  (新しい増分変位)
            double[] cum =
            [
                CumulativeDisp.Ux + ddisp[0],
                CumulativeDisp.Uy + ddisp[1],
                CumulativeDisp.Uz + ddisp[2],
                CumulativeDisp.Rx + ddisp[3],
                CumulativeDisp.Ry + ddisp[4],
                CumulativeDisp.Rz + ddisp[5]
            ];

            IncrementalDisp = new NodeDisp(ddisp[0], ddisp[1], ddisp[2], ddisp[3], ddisp[4], ddisp[5]);
            CumulativeDisp = new NodeDisp(cum[0], cum[1], cum[2], cum[3], cum[4], cum[5]);
        }

        // 節点反力の初期化
        // 節点反力の累加反力の初期化
        public void InitializeReact()
        {
            IncrementalReaction = new(0, 0, 0, 0, 0, 0);
            CumulativeReaction = new(0, 0, 0, 0, 0, 0);
        }

        public void InitializeSpringForce()
        {
            IncrementalSpringForce = new(0, 0, 0, 0, 0, 0); ;
            CumulativeSpringForce = new(0, 0, 0, 0, 0, 0);
        }

        public void InitializeSoilDisp()
        {
            SoilDisp = new(0, 0, 0, 0, 0, 0);
            SoilDispIncrement = new(0, 0, 0, 0, 0, 0);
        }

        public void SetReact()
        {
            NodeReaction dreact = new(0, 0, 0, 0, 0, 0);  // 初期値
            for (int index = 0; index < 6; index++)
            {
                int eq = EquationNumber[index];
                if (eq >= 0)
                {
                    dreact.SetByIndex(index, IncrementalReaction.GetByIndex(eq));
                }
                else
                {
                    dreact.SetByIndex(index, 0.0);
                }
            }
            IncrementalReaction = dreact;
            CumulativeReaction += dreact;
        }

        public void SetSForce()
        {
            CumulativeSpringForce = new(0, 0, 0, 0, 0, 0); ;   // 節点累積ばね力
            IncrementalSpringForce = new(0, 0, 0, 0, 0, 0); ;    // 節点増分ばね力
        }

        public void SetMasterNode(int index, Node node)
        {
            MasterNodes[index] = node;
        }

        public void SetArmVector(int index, Vector3D slaveArm)
        {
            if (index == 0)
            {
                SlaveArm.X = slaveArm.X;
            }
            else if (index == 1)
            {
                SlaveArm.Y = slaveArm.Y;
            }
            else if (index == 2)
            {
                SlaveArm.Z = slaveArm.Z;
            }
        }

        public void SetTransferMatrix()
        {
            Matrix<double> transferMatrix = Matrix<double>.Build.DenseIdentity(6);
            if (MasterNodes[3] != null)
            {
                transferMatrix[0, 3] = 0;
                transferMatrix[0, 4] = GetSlaveArm(2);
                transferMatrix[0, 5] = -GetSlaveArm(1);
            }
            if (MasterNodes[4] != null)
            {
                transferMatrix[1, 3] = -GetSlaveArm(2);
                transferMatrix[1, 4] = 0;
                transferMatrix[1, 5] = GetSlaveArm(0);
            }
            if (MasterNodes[5] != null)
            {
                transferMatrix[2, 3] = GetSlaveArm(1);
                transferMatrix[2, 4] = -GetSlaveArm(0);
                transferMatrix[2, 5] = 0;
            }
            TransferMatrix = transferMatrix;
        }

        public Vector<double> MapIncrementalLoadOnGlobalLoad(Vector<double> loadVector/*, AnaModel anaModel*/)
        {
            return MapOnGlobalLoad(loadVector, true/*, anaModel*/);
        }

        public Vector<double> MapCumulativeLoadOnGlobalLoad(Vector<double> loadVector/*, AnaModel anaModel*/)
        {
            return MapOnGlobalLoad(loadVector, false/*, anaModel*/);
        }

        // Node.dLoad、Node.Loadの全体荷重ベクトルload_vectorへのマップオン
        private Vector<double> MapOnGlobalLoad(Vector<double> loadVector, bool isF/*, AnaModel anaModel*/)
        {
            //NodeLoad load;
            //if(isF == true)
            //{
            //    load = IncrementalLoad;
            //}
            //else
            //{
            //    load = CumulativedLoad;
            //}
            NodeLoad load = isF == true ? IncrementalLoad : CumulativedLoad;

            for (int index = 0; index < 6; index++)
            {
                if (MasterNodes[index] == null) // slave以外の場合
                {
                    int e_num = EquationNumber[index];
                    if (e_num >= 0)
                    {
                        loadVector[e_num] += load.GetByIndex(index);
                    }
                }
                else // slaveの場合
                {
                    int e_num = MasterNodes[index].EquationNumber[index];
                    loadVector[e_num] += load.GetByIndex(index);
                    if (0 <= index && index <= 2 /*&& Boundary.BoundaryList[index] == BoundaryType.Slave*/)
                    {
                        int ia = (index + 4) % 3 + 3;
                        if (MasterNodes[ia] != null)
                        {
                            float ia_arm = +GetSlaveArm((index + 2) % 3);
                            loadVector[MasterNodes[index].EquationNumber[ia]] += load.GetByIndex(index) * ia_arm;
                        }
                        int ib = (index + 5) % 3 + 3;
                        if (MasterNodes[ib] != null)
                        {
                            float ib_arm = -GetSlaveArm((index + 1) % 3);
                            loadVector[MasterNodes[index].EquationNumber[ib]] += load.GetByIndex(index) * ib_arm;
                        }
                    }
                }
            }
            return loadVector;
        }

        // 節点ばね(6自由度)をセットする
        public void SetNodeSpring(NodeSpring nodeSpring, bool isTan)
        {
            if (isTan)
            {
                TangentSpring = nodeSpring;
            }
            else
            {
                SecantSpring = nodeSpring;
            }
            IsSpringed = true;
        }

        // 節点ばね剛性を全体剛性マトリクスにマップオンする
        public Matrix<double> MapSpringOnGlobalStiff(Matrix<double> matrixGlobalStiffness, bool isTan/*, AnaModel anaModel*/)
        {
            //(Node[] masterNodes, Vector3D _) = anaModel.GetMasterNodesArmVector(this);

            NodeSpring ns = isTan ? TangentSpring : SecantSpring;

            int[] eq = new int[6];
            for (int index = 0; index < 6; index++)
            {
                eq[index] = MasterNodes[index] == null ?
                    EquationNumber[index] :
                    MasterNodes[index].EquationNumber[index];
            }

            for (int index = 0; index < 6; index++)
            {
                if (eq[index] >= 0)
                {
                    matrixGlobalStiffness[eq[index], eq[index]] += ns.GetByIndex(index);

                    // ## 併進→回転
                    bool isIaSlave = false;
                    double iaArm = 0.0;
                    bool isIbSlave = false;
                    double ibArm = 0.0;

                    if (0 <= index && index <= 2 && MasterNodes[index] != null)
                    {
                        int ia = (index + 4) % 3 + 3;  // i = 0, 1, 2 >> ia = 4, 5, 3
                        int ib = (index + 5) % 3 + 3;  // i = 0, 1, 2 >> ib = 5, 3, 4

                        if (MasterNodes[ia] != null)
                        {
                            iaArm = GetSlaveArm((index + 2) % 3);
                            isIaSlave = true;
                        }
                        if (MasterNodes[ib] != null)
                        {
                            ibArm = GetSlaveArm((index + 1) % 3);
                            isIbSlave = true;
                        }
                        if (isIaSlave && eq[ia] >= 0)
                        {
                            matrixGlobalStiffness[eq[ia], eq[index]] += ns.GetByIndex(index) * iaArm;
                            matrixGlobalStiffness[eq[index], eq[ia]] += ns.GetByIndex(index) * iaArm;
                            matrixGlobalStiffness[eq[ia], eq[ia]] += ns.GetByIndex(index) * iaArm * iaArm;
                        }
                        if (isIbSlave && eq[ib] >= 0)
                        {
                            matrixGlobalStiffness[eq[ib], eq[index]] += ns.GetByIndex(index) * ibArm;
                            matrixGlobalStiffness[eq[index], eq[ib]] += ns.GetByIndex(index) * ibArm;
                            matrixGlobalStiffness[eq[ib], eq[ib]] += ns.GetByIndex(index) * ibArm * ibArm;
                        }
                        if (isIaSlave && isIbSlave && eq[ia] >= 0 && eq[ib] >= 0)
                        {
                            matrixGlobalStiffness[eq[ia], eq[ib]] += ns.GetByIndex(index) * iaArm * ibArm;
                            matrixGlobalStiffness[eq[ib], eq[ia]] += ns.GetByIndex(index) * iaArm * ibArm;
                        }
                    }
                }
            }
            return matrixGlobalStiffness;
        }

        public Node DeepCopy()
        {
            // 新しいNodeを生成
            var copy = new Node()
            {
                Name = this.Name,
                Coord = new Point3D(this.Coord.X, this.Coord.Y, this.Coord.Z),
                Boundary = new Boundary(
                    this.Boundary.Ux, this.Boundary.Uy, this.Boundary.Uz,
                    this.Boundary.Rx, this.Boundary.Ry, this.Boundary.Rz),

                EquationNumber = (int[])this.EquationNumber.Clone(),

                IncrementalLoad = this.IncrementalLoad.Clone(),
                CumulativedLoad = this.CumulativedLoad.Clone(),

                IncrementalDisp = this.IncrementalDisp.Clone(),
                CumulativeDisp = this.CumulativeDisp.Clone(),

                IsLoaded = this.IsLoaded,
                IsSpringed = this.IsSpringed,

                IncrementalReaction = this.IncrementalReaction.Clone(),
                CumulativeReaction = this.CumulativeReaction.Clone(),

                SoilDisp = this.SoilDisp?.Clone(),
                SoilDispIncrement = this.SoilDispIncrement?.Clone(),

                MasterNodes = (Node[])this.MasterNodes.Clone(),
                SlaveArm = new Vector3S(this.SlaveArm.X, this.SlaveArm.Y, this.SlaveArm.Z),

                CumulativeSpringForce = this.CumulativeSpringForce.Clone(),
                IncrementalSpringForce = this.IncrementalSpringForce.Clone(),

                CumulativeForcedDisp = this.CumulativeForcedDisp.Clone(),
                IncrementalForcedDisp = this.IncrementalForcedDisp.Clone(),

                IsForcedDisped = this.IsForcedDisped,

                TransferMatrix = this.TransferMatrix.Clone(),
                NodeResults = new ObservableCollection<NodeResult>(this.NodeResults.Select(r => r.DeepCopy()))
            };
            return copy;
            //var copy = new Node(this.Name, this.Coord.X, this.Coord.Y, this.Coord.Z)
            //{
            //    Boundary = new Boundary(
            //        this.Boundary.Ux, this.Boundary.Uy, this.Boundary.Uz,
            //        this.Boundary.Tx, this.Boundary.Ty, this.Boundary.Tz),
            //    EquationNumber = (int[])this.EquationNumber.Clone(),

            //    IncrementalLoad = this.IncrementalLoad.Clone(),
            //    CumulativedLoad = this.CumulativedLoad.Clone(),

            //    IncrementalDisp = this.IncrementalDisp.Clone(),
            //    CumulativeDisp = this.CumulativeDisp.Clone(),

            //    IsLoaded = this.IsLoaded,
            //    IsSpringed = this.IsSpringed,

            //    IncrementalReaction = this.IncrementalReaction.Clone(),
            //    CumulativeReaction = this.CumulativeReaction.Clone(),

            //    SoilDisp = this.SoilDisp?.Clone(),
            //    SoilDispIncrement = this.SoilDispIncrement?.Clone(),

            //    MasterNodes = (Node[])this.MasterNodes.Clone(), // 参照コピー。必要に応じてID等で再構築
            //    SlaveArm = new Vector3S(this.SlaveArm.X, this.SlaveArm.Y, this.SlaveArm.Z),

            //    CumulativeSpringForce = this.CumulativeSpringForce.Clone(),
            //    IncrementalSpringForce = this.IncrementalSpringForce.Clone(),

            //    CumulativeForcedDisp = this.CumulativeForcedDisp.Clone(),
            //    IncrementalForcedDisp = this.IncrementalForcedDisp.Clone(),

            //    IsForcedDisped = this.IsForcedDisped,

            //    TransferMatrix = this.TransferMatrix.Clone(),
            //    NodeResults = new ObservableCollection<NodeResult>(this.NodeResults.Select(r => r.DeepCopy()))
            //};

            //return copy;
        }

    }
}