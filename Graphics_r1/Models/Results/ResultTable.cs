using System.Collections.Generic;

namespace PileDesign.Models.Results
{
    public sealed class ResultTable
    {
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public IReadOnlyList<ResultColumnDescriptor> Columns { get; init; } = [];
        public IReadOnlyList<object> Rows { get; init; } = [];
        public int Count => Rows?.Count ?? 0;

        // 追加メタデータ
        public string LoadCaseName { get; init; } = "";
        public string LoadCombinationName { get; init; } = "";
        public bool IsLiquefaction { get; init; }

        public string LiquefactionLabel => IsLiquefaction ? "有" : "無";
    }
}