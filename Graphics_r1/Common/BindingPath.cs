using System;
using System.Collections;
using System.Reflection;

namespace PileDesign.Common
{
    /// <summary>
    /// バインディングのパス (例: "Child.Value"、"BetaL[0]"、"[2]") を、WPF を通さずに値へたどる。
    ///
    /// 表の貼り付け (<see cref="EnhancedDataGrid"/>) と、画面に出ていないセルの
    /// コピー・CSV 出力 (<c>DataGridCsv</c>) が使う。以前は両方に写しがあり、
    /// 添字 ([0]) を読めたのは貼り付け側だけだった。CSV 側は添字付きの列
    /// (地盤ウィンドウの液状化判定の τd/σz'・βL・γcy など) を、
    /// <b>画面外の行だけ空欄で出していた</b>。
    /// </summary>
    internal static class BindingPath
    {
        /// <summary>たどれなければ null。</summary>
        public static object? Resolve(object? item, string? path)
        {
            if (item == null || string.IsNullOrEmpty(path)) return null;

            object? current = item;
            foreach (var segment in path.Split('.'))
            {
                if (current == null) return null;

                // インデクサ表記 (例: "AxialForceLevel1s[0]") を解析
                string propName = segment;
                int? index = null;
                int bracketStart = segment.IndexOf('[');
                if (bracketStart >= 0)
                {
                    int bracketEnd = segment.IndexOf(']', bracketStart);
                    if (bracketEnd > bracketStart &&
                        int.TryParse(segment.Substring(bracketStart + 1, bracketEnd - bracketStart - 1), out int idx))
                    {
                        index = idx;
                        propName = bracketStart > 0 ? segment[..bracketStart] : string.Empty;
                    }
                }

                if (!string.IsNullOrEmpty(propName))
                {
                    // 静的プロパティも探す。WPF は項目経由で静的プロパティにも結べる
                    // (Chang の画面の鋼板ヤング率 Es がこの形で、以前はコピーで空欄になった)
                    var prop = current.GetType().GetProperty(propName,
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.FlattenHierarchy);
                    if (prop == null) return null;
                    current = prop.GetValue(current);
                }

                if (index.HasValue && current != null)
                {
                    if (current is IList list && index.Value >= 0 && index.Value < list.Count)
                        current = list[index.Value];
                    else if (current is Array arr && index.Value >= 0 && index.Value < arr.Length)
                        current = arr.GetValue(index.Value);
                    else
                        return null;
                }
            }
            return current;
        }
    }
}
