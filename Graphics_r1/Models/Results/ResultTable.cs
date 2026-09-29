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
        /// 数値の列にある数値でない値 (NaN・無限大) の数。画面に出す直前の共通の検査。
        ///
        /// 表ごとに行を組む処理がばらばらで、どの表でも数値でない値がそのまま「NaN」「∞」と並び、
        /// 気付く手掛かりは各セルを見ることしかなかった。数えて表の名前に添える。
        /// </summary>
        public int NonFiniteCellCount => _nonFiniteCellCount ??= CountNonFiniteCells(Columns, Rows);
        private int? _nonFiniteCellCount;

        internal static int CountNonFiniteCells(IReadOnlyList<ResultColumnDescriptor>? columns, IReadOnlyList<object>? rows)
        {
            if (columns == null || rows == null) return 0;
            int count = 0;
            foreach (var column in columns)
            {
                var type = column.Property?.PropertyType;
                if (type != typeof(double) && type != typeof(double?) && type != typeof(float) && type != typeof(float?)) continue;
                foreach (var row in rows)
                {
                    if (row == null || !column.Property!.DeclaringType!.IsInstanceOfType(row)) continue;
                    object? value = column.Property.GetValue(row);
                    double v = value switch { double d => d, float f => f, _ => 0 };
                    if (!double.IsFinite(v)) count++;
                }
            }
            return count;
        }

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
                if (SpansAllConditions)
                    return NonFiniteCellCount > 0 ? Name + $"（数値でない値 {NonFiniteCellCount} か所）" : Name;

                // 液状化状態を先頭に表示（ListBoxで切れても区別できるように）
                var parts = new List<string> { $"[{LiquefactionLabel}]", Name };
                if (!string.IsNullOrEmpty(LoadCaseName))
                    parts.Add(LoadCaseName);
                if (!string.IsNullOrEmpty(LoadCombinationName))
                    parts.Add(LoadCombinationName);
                string name = string.Join(" / ", parts);
                if (OmittedRowCount > 0) name += $"（結果の無い {OmittedRowCount} 行は省略）";
                return NonFiniteCellCount > 0 ? name + $"（数値でない値 {NonFiniteCellCount} か所）" : name;
            }
        }
    }
}