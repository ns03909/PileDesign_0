using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// 基礎梁鉛直解析における杭ごとの結果
    /// </summary>
    public class VerticalBeamPileResult
    {
        public int PileNo { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double InputLoad_kN { get; set; }
        public double Reaction_kN { get; set; }
        public double Settlement_mm { get; set; }

        public VerticalBeamPileResult() { }

        public VerticalBeamPileResult(int pileNo, double x, double y,
            double inputLoad_kN, double reaction_kN, double settlement_mm)
        {
            PileNo = pileNo;
            X = x;
            Y = y;
            InputLoad_kN = inputLoad_kN;
            Reaction_kN = reaction_kN;
            Settlement_mm = settlement_mm;
        }

        public VerticalBeamPileResult DeepCopy() => new()
        {
            PileNo = PileNo,
            X = X,
            Y = Y,
            InputLoad_kN = InputLoad_kN,
            Reaction_kN = Reaction_kN,
            Settlement_mm = Settlement_mm
        };
    }

    /// <summary>
    /// 基礎梁鉛直解析における節点ごとの結果
    /// </summary>
    public class VerticalBeamNodeResult
    {
        public string NodeName { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Uz_mm { get; set; }
        public double Rx_rad { get; set; }
        public double Ry_rad { get; set; }

        public VerticalBeamNodeResult() { }

        public VerticalBeamNodeResult(string nodeName, double x, double y, double z,
            double uz_mm, double rx_rad, double ry_rad)
        {
            NodeName = nodeName;
            X = x;
            Y = y;
            Z = z;
            Uz_mm = uz_mm;
            Rx_rad = rx_rad;
            Ry_rad = ry_rad;
        }

        public VerticalBeamNodeResult DeepCopy() => new()
        {
            NodeName = NodeName,
            X = X,
            Y = Y,
            Z = Z,
            Uz_mm = Uz_mm,
            Rx_rad = Rx_rad,
            Ry_rad = Ry_rad
        };
    }

    /// <summary>
    /// 基礎梁鉛直解析における梁要素ごとの結果
    /// </summary>
    public class VerticalBeamBeamResult
    {
        public string BeamName { get; set; } = string.Empty;
        public double Ni { get; set; }   // 軸力 I端 [kN]
        public double Qyi { get; set; }  // せん断力Y I端 [kN]
        public double Qzi { get; set; }  // せん断力Z I端 [kN]
        public double Mxi { get; set; }  // ねじりモーメント I端 [kNm]
        public double Myi { get; set; }  // 曲げモーメントY I端 [kNm]
        public double Mzi { get; set; }  // 曲げモーメントZ I端 [kNm]
        public double Nj { get; set; }   // 軸力 J端 [kN]
        public double Qyj { get; set; }  // せん断力Y J端 [kN]
        public double Qzj { get; set; }  // せん断力Z J端 [kN]
        public double Mxj { get; set; }  // ねじりモーメント J端 [kNm]
        public double Myj { get; set; }  // 曲げモーメントY J端 [kNm]
        public double Mzj { get; set; }  // 曲げモーメントZ J端 [kNm]

        public VerticalBeamBeamResult() { }

        public VerticalBeamBeamResult(string beamName, BeamForce force)
        {
            BeamName = beamName;
            if (force != null)
            {
                Ni = force.Fxi;   Qyi = force.Fyi;   Qzi = force.Fzi;
                Mxi = force.Mxi;  Myi = force.Myi;   Mzi = force.Mzi;
                Nj = force.Fxj;   Qyj = force.Fyj;   Qzj = force.Fzj;
                Mxj = force.Mxj;  Myj = force.Myj;   Mzj = force.Mzj;
            }
        }

        public VerticalBeamBeamResult DeepCopy() => new()
        {
            BeamName = BeamName,
            Ni = Ni, Qyi = Qyi, Qzi = Qzi, Mxi = Mxi, Myi = Myi, Mzi = Mzi,
            Nj = Nj, Qyj = Qyj, Qzj = Qzj, Mxj = Mxj, Myj = Myj, Mzj = Mzj
        };
    }

    /// <summary>
    /// 基礎梁鉛直解析における収束ステップの結果
    /// </summary>
    public class VerticalBeamStepResult
    {
        public string LoadCaseName { get; set; } = string.Empty;
        public int Step { get; set; }
        public int Iterations { get; set; }
        public double Residual { get; set; }
        public bool IsConverged { get; set; }

        public VerticalBeamStepResult() { }

        public VerticalBeamStepResult(string loadCaseName, int step, int iterations,
            double residual, bool isConverged)
        {
            LoadCaseName = loadCaseName;
            Step = step;
            Iterations = iterations;
            Residual = residual;
            IsConverged = isConverged;
        }

        public VerticalBeamStepResult DeepCopy() => new()
        {
            LoadCaseName = LoadCaseName,
            Step = Step,
            Iterations = Iterations,
            Residual = Residual,
            IsConverged = IsConverged
        };
    }

    /// <summary>
    /// 荷重ケースごとの全結果をまとめるコンテナ
    /// </summary>
    public class VerticalBeamCaseResult
    {
        public string LoadCaseName { get; set; } = string.Empty;
        public List<VerticalBeamPileResult> PileResults { get; set; } = [];
        public List<VerticalBeamNodeResult> NodeResults { get; set; } = [];
        public List<VerticalBeamBeamResult> BeamResults { get; set; } = [];
        public List<VerticalBeamStepResult> StepResults { get; set; } = [];
        public bool IsConverged { get; set; }
    }
}
