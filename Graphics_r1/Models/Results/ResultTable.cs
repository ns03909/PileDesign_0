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

        /// <summary>荷重組合せの番号 (表の絞り込みで組合せを見分ける。表示名は係数を丸めた文字列で重なりうる)。</summary>
        public int? LoadCombinationNo { get; init; }
        public bool IsLiquefaction { get; init; }

        /// <summary>
        /// 表示中の荷重条件の結果が無くて省いた行の数。名前に添えて知らせる。
        ///
        /// 以前は結果が無いと要素・節点・ばね本体の「現在の値」を出していた。それは最後に解いた
        /// 別の荷重条件の値かもしれず、表の荷重条件の名前と数値が食い違った。
        /// </summary>
        public int OmittedRowCount { get; init; }

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
        /// 行だけ差し替えた複製。条件フィルタで行を絞った表を作るのに使う。
        /// </summary>
        public ResultTable WithRows(IReadOnlyList<object> rows) => new()
        {
            Name = Name,
            Category = Category,
            Columns = Columns,
            Rows = rows,
            LoadCaseName = LoadCaseName,
            LoadCombinationName = LoadCombinationName,
            LoadCombinationNo = LoadCombinationNo,
            IsLiquefaction = IsLiquefaction,
            SpansAllConditions = SpansAllConditions,
            OmittedRowCount = OmittedRowCount,
        };

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
                string name = string.Join(" / ", parts);
                return OmittedRowCount > 0 ? name + $"（結果の無い {OmittedRowCount} 行は省略）" : name;
            }
        }
    }
}