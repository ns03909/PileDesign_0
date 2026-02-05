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

        /// <summary>
        /// ListBox表示用の名前（液状化状態を含む）
        /// 液状化状態を先頭に表示して、切れても区別できるようにする
        /// </summary>
        public string DisplayName
        {
            get
            {
                // 液状化状態を先頭に表示（ListBoxで切れても区別できるように）
                var parts = new List<string> { $"[{LiquefactionLabel}]", Name };
                if (!string.IsNullOrEmpty(LoadCaseName))
                    parts.Add(LoadCaseName);
                if (!string.IsNullOrEmpty(LoadCombinationName))
                    parts.Add(LoadCombinationName);
                return string.Join(" / ", parts);
            }
        }
    }
}