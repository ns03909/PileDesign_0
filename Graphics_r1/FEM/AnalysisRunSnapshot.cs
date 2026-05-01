using System.Collections.Generic;

namespace PileDesign.FEM
{
    /// <summary>
    /// 水平解析の前回実行設定スナップショット。
    /// 「追加実行」(段階追加再解析) で前回と互換性がある実行かを検証するために
    /// AnaModel.LastRunConfig として保持され、ProjectData 経由で JSON 永続化される。
    /// 旧 JSON 互換のため、AnaModel.LastRunConfig は null 許容 (= 過去の追加実行情報なし)。
    /// </summary>
    public sealed class AnalysisRunSnapshot
    {
        // 解析パラメータ (互換性比較用)
        public string LiquefactionOption { get; set; } = "None";
        public int Level1StepsCount { get; set; }
        public int Level2StepsCount { get; set; }
        public bool UseModifiedNewtonRaphson { get; set; }
        public int FullNRIterations { get; set; }
        public bool SkipIteration { get; set; }
        public bool UseLineSearch { get; set; }
        public double RelaxationFactor { get; set; }
        public bool UseAnalysisAxialForce { get; set; }
        public string ConnectionMode { get; set; } = "RigidBody";

        // 実行済みケース集合 (積集合判定用)
        public List<CaseKey> ExecutedCaseKeys { get; set; } = new();

        // 入力モデル変更検出 (Phase 1 は null 許容で簡易、Phase 2 で SHA256 等)
        public string? InputModelHash { get; set; }

        /// <summary>
        /// 1 ケース 1 件を表す識別子。LoadName + 荷重組合せ名 + 液状化フラグ。
        /// record の自動生成 Equals/GetHashCode で HashSet 検索 O(1)。
        /// </summary>
        public sealed record CaseKey(string LoadName, string CombinationName, bool IsLiquefaction);
    }
}
