using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Models;

/// <summary>解析を再現・特定するため、結果と一緒に保存する実行条件。</summary>
public sealed class AnalysisRunMetadata
{
    public string ApplicationVersion { get; set; } = "";
    public string ConvergenceMethod { get; set; } = "";
    public int Level1Steps { get; set; }
    public int Level2Steps { get; set; }
    public int CaseParallelism { get; set; }
    public double InitialRelaxationFactor { get; set; }
    public double BaseResidualTolerance { get; set; }
    public double RelaxedResidualTolerance { get; set; }
    public int MaximumIterations { get; set; }
    public double LinearSolverResidualTolerance { get; set; }
    public List<AnalysisCaseMetadata> Cases { get; set; } = [];

    /// <summary>計算書表紙に載せる簡潔な設定説明。</summary>
    public string DescribeSettings()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApplicationVersion)) parts.Add($"解析プログラム {ApplicationVersion}");
        if (!string.IsNullOrWhiteSpace(ConvergenceMethod)) parts.Add($"収束安定化 {ConvergenceMethod}");
        if (Level1Steps > 0 || Level2Steps > 0) parts.Add($"解析ステップ L1={Level1Steps}, L2={Level2Steps}");
        if (CaseParallelism > 0) parts.Add($"ケース並列数 {CaseParallelism}");
        if (MaximumIterations > 0) parts.Add($"反復上限 {MaximumIterations}");
        if (BaseResidualTolerance > 0) parts.Add($"基本収束基準 {BaseResidualTolerance:G}");
        if (RelaxedResidualTolerance > 0) parts.Add($"緩和収束基準 {RelaxedResidualTolerance:G}");
        if (InitialRelaxationFactor > 0) parts.Add($"初期緩和係数 {InitialRelaxationFactor:G}");
        if (LinearSolverResidualTolerance > 0) parts.Add($"線形ソルバー残差基準 {LinearSolverResidualTolerance:G}");
        return string.Join("　", parts);
    }

    /// <summary>ケース名は空欄・重複しうるため、番号と液状化条件も含む一覧を作る。</summary>
    public string DescribeCases()
        => string.Join("、", (Cases ?? []).Select(c => c.Describe()).Where(s => s.Length > 0));
}

public sealed class AnalysisCaseMetadata
{
    public int Level { get; set; }
    public int LoadCaseNo { get; set; }
    public string LoadCaseName { get; set; } = "";
    public int LoadCombinationNo { get; set; }
    public string LoadCombinationName { get; set; } = "";
    public bool IsLiquefaction { get; set; }
    public string Status { get; set; } = "";

    public string Describe()
    {
        if (Level <= 0 && LoadCaseNo <= 0 && LoadCombinationNo <= 0) return "";
        string caseName = string.IsNullOrWhiteSpace(LoadCaseName) ? $"荷重ケースNo.{LoadCaseNo}" : LoadCaseName;
        string combination = string.IsNullOrWhiteSpace(LoadCombinationName)
            ? $"組合せNo.{LoadCombinationNo}" : LoadCombinationName;
        string liquefaction = IsLiquefaction ? "・液状化" : "";
        string statusLabel = Status switch
        {
            "Converged" => "収束",
            "ConvergedRelaxed" => "緩和して受理",
            "Unconverged" => "未収束",
            "PhysicallyUnconverged" => "物理的未収束",
            _ => Status,
        };
        string status = string.IsNullOrWhiteSpace(statusLabel) ? "" : $"・{statusLabel}";
        return $"L{Level} {caseName} ({combination}){liquefaction}{status}";
    }
}
