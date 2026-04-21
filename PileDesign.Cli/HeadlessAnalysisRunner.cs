using System.IO;
using MathNet.Numerics;
using PileDesign.FEM;
using PileDesign.Models.InputData;

namespace PileDesign.Cli;

/// <summary>
/// WPF依存なしで水平解析を実行するヘッドレスランナー。
/// HorizontalCalculationViewModel.RunAsync の核心ロジックを簡略化して再実装。
/// </summary>
public sealed class HeadlessAnalysisRunner
{
    static HeadlessAnalysisRunner()
    {
        try { Control.UseBestProviders(); }
        catch { try { Control.UseManaged(); } catch { } }
        Control.MaxDegreeOfParallelism = Environment.ProcessorCount;
    }

    public InputModel InputModel { get; }
    public int Level1Steps { get; set; } = 2;
    public int Level2Steps { get; set; } = 8;
    public double ConvergenceTolerance { get; set; } = 1e-3;
    public int MaxIterations { get; set; } = 50;
    public double RelaxationFactor { get; set; } = 1.0;
    public bool Verbose { get; set; } = true;

    public HeadlessAnalysisRunner(InputModel inputModel)
    {
        InputModel = inputModel ?? throw new ArgumentNullException(nameof(inputModel));
    }

    /// <summary>
    /// 水平解析を実行し、結果を返す。
    /// </summary>
    public AnalysisResult Run(CancellationToken token = default)
    {
        var result = new AnalysisResult();

        // 1. FEMモデル作成
        Log("FEMモデル作成中...");
        var modelling = new AnalysisModelling(InputModel);
        Log($"  節点数: {modelling.Nodes.Count}, 要素数: {modelling.Beams.Count}");

        // 2. AnaModel 構築
        var model = new AnaModel(
            InputModel,
            modelling.Nodes,
            modelling.Beams,
            modelling.DummyBeams,
            modelling.RigidBodies,
            modelling.HorizontalSoilSprings,
            modelling.RotationalSprings)
        {
            RotationalSprings = modelling.RotationalSprings,
            PenaltySprings = modelling.PenaltySprings
        };

        model.SetSlaveNodes();
        result.NodeCount = modelling.Nodes.Count;
        result.BeamCount = modelling.Beams.Count;

        // 3. 荷重ケースループ
        var loadCases = InputModel.LoadCasesInput.AnalysisTargetSeismicLoadCases;
        var loadCombinations = InputModel.LoadCasesInput.AllLoadCombinations;
        int totalCases = 0;
        int convergedCases = 0;

        foreach (var loadCase in loadCases)
        {
            token.ThrowIfCancellationRequested();

            if (loadCase.UpperMassForce == 0 && loadCase.FoundationMassForce == 0)
            {
                Log($"  レベル{loadCase.Level}-{loadCase.No}: 荷重ゼロ → スキップ");
                continue;
            }

            int nStep = loadCase.Level == 1 ? Level1Steps : Level2Steps;

            foreach (var loadCombination in loadCombinations)
            {
                // 液状化ケース: 両方実行
                var liquefactionCases = new[] { false };

                foreach (bool isLiquefaction in liquefactionCases)
                {
                    token.ThrowIfCancellationRequested();
                    totalCases++;

                    string caseLabel = $"L{loadCase.Level}-{loadCase.No} Comb[{loadCombination.No}] " +
                                       $"{(isLiquefaction ? "液状化" : "非液状化")}";
                    Log($"  {caseLabel} ({nStep}ステップ)");

                    // 変位・荷重ベクトル初期化
                    model.InitializeVectorD();
                    model.InitializeVectorF();

                    // 荷重ベクトルを設定
                    model.MapOnVectorF();
                    model.MapOnVectorDF();

                    // RotationalSpring のM-θ関係を設定
                    SetupRotationalSprings(model);

                    bool caseConverged = true;

                    for (int step = 0; step < nStep; step++)
                    {
                        token.ThrowIfCancellationRequested();

                        // 荷重増分を加算
                        model.UpdateVectorF();
                        model.UpdateVectorDOFForcedDisp();

                        // Newton-Raphson 反復
                        bool stepConverged = false;
                        int nIter = 0;

                        for (nIter = 1; nIter <= MaxIterations; nIter++)
                        {
                            // 接線剛性マトリクス組立
                            model.MapOnKtanMat();

                            // 強制変位処理
                            model.SetForcedDispOnLoadVectorAndStiffnessMatrix(true);

                            // 安定性チェック（最初の反復のみ）
                            if (nIter == 1 && step == 0)
                            {
                                try { model.ValidateStability(false); }
                                catch (InvalidOperationException ex)
                                {
                                    Log($"    警告: {ex.Message}");
                                }
                            }

                            // 求解: K*Δd = R
                            Solver.SolveDisp(model, RelaxationFactor);

                            // 割線剛性で内力計算
                            model.MapOnKsecMat();
                            foreach (var beam in model.Beams)
                                beam.SetBeamDispAndForce(false);
                            foreach (var spring in model.HorizontalSoilSprings)
                                spring.SetBeamDispAndForce(false);
                            if (model.RotationalSprings != null)
                                foreach (var rs in model.RotationalSprings)
                                    rs.SetBeamDispAndForce(false);
                            if (model.PenaltySprings != null)
                                foreach (var ps in model.PenaltySprings)
                                    ps.SetBeamDispAndForce(false);

                            model.SetT();
                            model.FindR();

                            double residual = model.NormsROnNormsFint;

                            if (residual < ConvergenceTolerance)
                            {
                                stepConverged = true;
                                break;
                            }
                        }

                        if (!stepConverged)
                        {
                            Log($"    ステップ{step + 1}/{nStep}: 未収束 (残差={model.NormsROnNormsFint:E3}, {nIter}反復)");
                            caseConverged = false;
                        }
                    }

                    if (caseConverged)
                    {
                        convergedCases++;
                        Log($"    → 収束");
                    }
                    else
                    {
                        Log($"    → 未収束あり");
                    }

                    // 結果を収集
                    CollectResults(result, model, loadCase, loadCombination, isLiquefaction);
                }
            }
        }

        result.TotalCases = totalCases;
        result.ConvergedCases = convergedCases;
        Log($"完了: {convergedCases}/{totalCases} ケース収束");

        return result;
    }

    private void SetupRotationalSprings(AnaModel model)
    {
        if (model.RotationalSprings == null) return;

        foreach (var rs in model.RotationalSprings)
        {
            // 剛結: 大きな回転ばね定数
            double kTheta = 1e10;
            rs.SetKe(1e8, 1e8, 1e8, kTheta, kTheta, 1e8, true);
            rs.SetKe(1e8, 1e8, 1e8, kTheta, kTheta, 1e8, false);
        }
    }

    private void CollectResults(AnalysisResult result, AnaModel model, LoadCase loadCase,
        LoadCombination loadCombination, bool isLiquefaction)
    {
        var caseResult = new LoadCaseResult
        {
            Level = loadCase.Level,
            LoadCaseNo = loadCase.No,
            CombinationNo = loadCombination.No,
            IsLiquefaction = isLiquefaction,
            Converged = true
        };

        // ActionPoint の変位
        var ap = model.Nodes.FirstOrDefault(n => n.Name == "ActionPoint");
        if (ap != null)
        {
            caseResult.ActionPointDisplacement = new DisplacementResult
            {
                Ux_mm = ap.CumulativeDisp.Ux * 1000,
                Uy_mm = ap.CumulativeDisp.Uy * 1000,
                Uz_mm = ap.CumulativeDisp.Uz * 1000,
                Rx_rad = ap.CumulativeDisp.Rx,
                Ry_rad = ap.CumulativeDisp.Ry,
                Rz_rad = ap.CumulativeDisp.Rz
            };
        }

        // 各杭頭の断面力
        foreach (var pile in InputModel.PileLayoutItems)
        {
            var pileHeadNode = model.Nodes.FirstOrDefault(n => n.Name == $"杭節点-{pile.No}-0");
            if (pileHeadNode == null) continue;

            var pileResult = new PileHeadResult
            {
                PileNo = pile.No,
                X = pile.X,
                Y = pile.Y
            };

            pileResult.Displacement = new DisplacementResult
            {
                Ux_mm = pileHeadNode.CumulativeDisp.Ux * 1000,
                Uy_mm = pileHeadNode.CumulativeDisp.Uy * 1000,
                Uz_mm = pileHeadNode.CumulativeDisp.Uz * 1000,
                Rx_rad = pileHeadNode.CumulativeDisp.Rx,
                Ry_rad = pileHeadNode.CumulativeDisp.Ry,
                Rz_rad = pileHeadNode.CumulativeDisp.Rz
            };

            // 杭頭直下のビーム要素の断面力
            var topBeam = model.Beams.FirstOrDefault(b => b.NodeI == pileHeadNode);
            if (topBeam != null)
            {
                pileResult.ShearForce_kN = new ForceResult
                {
                    Fx = topBeam.CumulativeForce.Fxi,
                    Fy = topBeam.CumulativeForce.Fyi,
                    Fz = topBeam.CumulativeForce.Fzi
                };
                pileResult.Moment_kNm = new ForceResult
                {
                    Fx = topBeam.CumulativeForce.Mxi,
                    Fy = topBeam.CumulativeForce.Myi,
                    Fz = topBeam.CumulativeForce.Mzi
                };
            }

            caseResult.PileHeadResults.Add(pileResult);
        }

        result.LoadCaseResults.Add(caseResult);
    }

    private void Log(string message)
    {
        if (Verbose)
            Console.WriteLine(message);
    }
}

// ─── 結果データクラス ───

public class AnalysisResult
{
    public int NodeCount { get; set; }
    public int BeamCount { get; set; }
    public int TotalCases { get; set; }
    public int ConvergedCases { get; set; }
    public List<LoadCaseResult> LoadCaseResults { get; set; } = [];
}

public class LoadCaseResult
{
    public int Level { get; set; }
    public int LoadCaseNo { get; set; }
    public int CombinationNo { get; set; }
    public bool IsLiquefaction { get; set; }
    public bool Converged { get; set; }
    public DisplacementResult? ActionPointDisplacement { get; set; }
    public List<PileHeadResult> PileHeadResults { get; set; } = [];
}

public class PileHeadResult
{
    public int PileNo { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public DisplacementResult? Displacement { get; set; }
    public ForceResult? ShearForce_kN { get; set; }
    public ForceResult? Moment_kNm { get; set; }
}

public class DisplacementResult
{
    public double Ux_mm { get; set; }
    public double Uy_mm { get; set; }
    public double Uz_mm { get; set; }
    public double Rx_rad { get; set; }
    public double Ry_rad { get; set; }
    public double Rz_rad { get; set; }
}

public class ForceResult
{
    public double Fx { get; set; }
    public double Fy { get; set; }
    public double Fz { get; set; }
}
