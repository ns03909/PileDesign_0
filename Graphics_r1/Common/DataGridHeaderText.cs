using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Common
{
    /// <summary>
    /// DataGrid の列ヘッダーから表示文字列を取り出す。
    ///
    /// ヘッダーは 3 通りの形を取る:
    ///   ・素の文字列                      … 入力系グリッドの大半
    ///   ・StackPanel + TextBlock 複数     … 「変形 / 係数 / (kN/m²)」のような多段見出し
    ///   ・TextBlock 1 つ                  … ツールチップを付けた列 (解析結果テーブル)
    ///
    /// これを各所で個別に判定していたため、TextBlock の分岐が漏れると
    /// <c>ToString()</c> にフォールバックして "System.Windows.Controls.TextBlock" が
    /// CSV・クリップボード・列レイアウト保存に混入する。取り出しはここに一本化する。
    /// </summary>
    public static class DataGridHeaderText
    {
        /// <summary>ヘッダーの表示文字列。取り出せなければ空文字。</summary>
        public static string From(object? header)
        {
            switch (header)
            {
                case null:
                    return string.Empty;

                case string s:
                    return s.Trim();

                case TextBlock tb:
                    return (tb.Text ?? string.Empty).Trim();

                case Panel panel:
                    {
                        var parts = new List<string>();
                        CollectText(panel, parts);
                        return string.Join(" ", parts).Trim();
                    }

                case ContentControl cc:
                    return From(cc.Content);

                default:
                    return header.ToString()?.Trim() ?? string.Empty;
            }
        }

        /// <summary>列のヘッダーの表示文字列。</summary>
        public static string From(DataGridColumn? column) => column == null ? string.Empty : From(column.Header);

        private static void CollectText(DependencyObject node, List<string> parts)
        {
            if (node is not Panel panel) return;

            foreach (var child in panel.Children)
            {
                switch (child)
                {
                    case TextBlock tb when !string.IsNullOrEmpty(tb.Text):
                        parts.Add(tb.Text);
                        break;
                    case Panel nested:
                        CollectText(nested, parts);
                        break;
                }
            }
        }
    }
}
