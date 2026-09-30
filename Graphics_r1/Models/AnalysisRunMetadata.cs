using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models;

/// <summary>実行条件を記録する解析の種類。記録は種類ごとに 1 件持つ。</summary>
public enum AnalysisKind
{
    /// <summary>水平解析。</summary>
    Horizontal,
    /// <summary>単杭沈下解析 (杭体・地盤ごとの荷重-沈下曲線)。</summary>
    SingleSettlement,
    /// <summary>単杭沈下解析 (基礎梁考慮の鉛直解析)。</summary>
    VerticalBeam,
    /// <summary>群杭沈下解析。</summary>
    GroupSettlement,
}

/// <summary>解析の種類ごとの条件の 1 項目 (水平解析以外の条件はこの形で持つ)。</summary>
public sealed class AnalysisConditionEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";

    public AnalysisConditionEntry() { }
    public AnalysisConditionEntry(string name, string value) { Name = name; Value = value; }
}

/// <summary>
/// 解析を再現・特定するため、結果と一緒に保存する実行条件。解析の種類ごとに 1 件。
///
/// 以前は 1 件だけを結果の控えに持ち、水平解析だけが書いていた。控えは解析のたびに取り直すので、
/// 水平解析のあとに沈下解析を実行すると水平解析の条件が消え、沈下解析の条件はもともと残らなかった。
/// </summary>
public sealed class AnalysisRunMetadata
{
    [JsonConverter(typeof(JsonStringEnumConverter<AnalysisKind>))]
    public AnalysisKind Kind { get; set; } = AnalysisKind.Horizontal;

    /// <summary>解析を実行した時刻。</summary>
    public DateTime? ExecutedAt { get; set; }

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

    /// <summary>水平解析以外の条件 (荷重の置き方・収束の基準など)。画面に出す名前と値の組。</summary>
    public List<AnalysisConditionEntry> Conditions { get; set; } = [];

    /// <summary>解析の種類の画面の呼び名。</summary>
    public static string KindLabel(AnalysisKind kind) => kind switch
    {
        AnalysisKind.Horizontal => "水平解析",
        AnalysisKind.SingleSettlement => "単杭沈下解析",
        AnalysisKind.VerticalBeam => "単杭沈下解析（基礎梁考慮）",
        AnalysisKind.GroupSettlement => "群杭沈下解析",
        _ => kind.ToString(),
    };

    /// <summary>計算書の表紙に載せる 1 行 (「【水平解析 2026/09/30 10:00】 解析プログラム …」)。</summary>
    public string Describe()
    {
        string when = ExecutedAt is { } t ? $" {t:yyyy/MM/dd HH:mm}" : "";
        string settings = DescribeSettings();
        return $"【{KindLabel(Kind)}{when}】" + (settings.Length > 0 ? " " + settings : "");
    }

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
        foreach (var c in Conditions ?? [])
        {
            if (c != null && !string.IsNullOrWhiteSpace(c.Name)) parts.Add($"{c.Name} {c.Value}");
        }
        return string.Join("　", parts);
    }

    /// <summary>ケース名は空欄・重複しうるため、番号と液状化条件も含む一覧を作る。</summary>
    public string DescribeCases()
        => string.Join("、", (Cases ?? []).Select(c => c.Describe()).Where(s => s.Length > 0));

    /// <summary>
    /// 記録を 1 件差し替える。同じ種類の前の記録は捨て、ほかの種類の記録は結果が残っている限り引き継ぐ
    /// (<paramref name="stillHasResult"/> が false の種類は結果が消えているので記録も捨てる)。種類の順に並べる。
    /// </summary>
    public static List<AnalysisRunMetadata> Merge(
        IEnumerable<AnalysisRunMetadata>? previous, AnalysisRunMetadata? latest, Func<AnalysisKind, bool> stillHasResult)
    {
        var merged = (previous ?? [])
            .Where(r => r != null && (latest == null || r.Kind != latest.Kind) && stillHasResult(r.Kind))
            .ToList();
        if (latest != null) merged.Add(latest);
        return merged.OrderBy(r => r.Kind).ToList();
    }
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
