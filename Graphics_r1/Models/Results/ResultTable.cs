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

        /// <summary>
        /// 1 つの荷重条件ではなく<b>全条件をまたぐ</b>表か。
        ///
        /// 検定結果のように、荷重ケース・組合せ・液状化を横断して 1 枚にまとめる表がこれ。
        /// 名前に液状化の有無を出さず、条件のフィルタでも絞り込みの対象外にする
        /// (条件は表の中の列で区別できる)。
        /// </summary>
        public bool SpansAllConditions { get; init; }

        public string LiquefactionLabel => IsLiquefaction ? "有" : "無";

        /// <summary>
        /// ListBox表示用の名前（液状化状態を含む）
        /// 液状化状態を先頭に表示して、切れても区別できるようにする
        /// </summary>
        public string DisplayName
        {
            get
            {
                // 全条件をまたぐ表は液状化の有無を持たないので、名前だけにする
                if (SpansAllConditions) return Name;

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