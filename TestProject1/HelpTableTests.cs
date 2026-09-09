using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの表で、行ごとの列数が揃っていること。
    ///
    /// <c>rowspan</c> や <c>colspan</c> の数が行数と合っていないと、
    /// <b>その行だけ左に詰まって別の列に見える</b>。ブラウザは黙って描くので、
    /// 開いてその表を見た人だけが気づく。
    ///
    /// 実際に 2 件あった。
    ///
    /// <list type="bullet">
    /// <item>ショートカット一覧の「ファイル」が <c>rowspan="4"</c> なのに 5 行あり、
    ///   5 行目 (Docx 計算書出力) だけカテゴリ列に食い込んでいた</item>
    /// <item>解析ケースの表が <c>colspan="12"</c> なのに下の見出しが 10 列で、
    ///   右に空の列が 2 つ出ていた</item>
    /// </list>
    ///
    /// 行を足すときに <c>rowspan</c> を直し忘れるのが典型なので、機械的に見る。
    /// </summary>
    [TestClass]
    public class HelpTableTests
    {
        [TestMethod]
        public void EveryHelpTable_HasConsistentColumnCounts()
        {
            var text = File.ReadAllText(
                Path.Combine(TestSource.Root(), "Graphics_r1", "Help", "help.html"));

            var tables = Regex.Matches(text, @"<table\b.*?</table>", RegexOptions.Singleline);
            TestSource.AssertScanned(tables.Count, 100, "ヘルプの表");

            var broken = new List<string>();
            foreach (Match table in tables)
            {
                var widths = MeasureRows(table.Value);
                if (widths.Count < 2 || widths.Distinct().Count() == 1) continue;

                int line = text.Take(table.Index).Count(c => c == '\n') + 1;
                var caption = Regex.Match(table.Value, @"<caption[^>]*>(.*?)</caption>", RegexOptions.Singleline);
                string name = caption.Success
                    ? Regex.Replace(caption.Groups[1].Value, "<[^>]+>", "").Trim()
                    : "(見出しなし)";

                int normal = widths.GroupBy(w => w).OrderByDescending(g => g.Count()).First().Key;
                var odd = widths.Select((w, i) => (w, i)).Where(t => t.w != normal)
                                .Select(t => $"{t.i + 1} 行目({t.w} 列)").Take(5);

                broken.Add($"{line} 行目の表「{name}」: 通常 {normal} 列 / {string.Join("・", odd)}");
            }

            Assert.AreEqual(0, broken.Count,
                "表の行ごとの列数が揃っていません。rowspan / colspan の数を確認してください。"
                + "ずれた行は左に詰まって別の列に見えます:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", broken));
        }

        /// <summary>
        /// 表の各行が占める列数を数える。rowspan の持ち越しを追う。
        /// </summary>
        private static List<int> MeasureRows(string table)
        {
            var widths = new List<int>();
            var pending = new Dictionary<int, int>();   // 列 -> 残り行数

            foreach (Match row in Regex.Matches(table, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline))
            {
                int col = 0, used = 0;

                foreach (Match cell in Regex.Matches(row.Groups[1].Value, @"<t[dh]\b([^>]*)>"))
                {
                    // 上の行から持ち越した列を飛ばす
                    while (pending.TryGetValue(col, out int left) && left > 0)
                    {
                        pending[col] = left - 1;
                        col++; used++;
                    }

                    string attrs = cell.Groups[1].Value;
                    int cspan = Span(attrs, "colspan");
                    int rspan = Span(attrs, "rowspan");

                    for (int k = 0; k < cspan; k++)
                    {
                        if (rspan > 1) pending[col] = rspan - 1;
                        col++; used++;
                    }
                }

                // 行末に残った持ち越しも数える
                while (pending.TryGetValue(col, out int left2) && left2 > 0)
                {
                    pending[col] = left2 - 1;
                    col++; used++;
                }

                widths.Add(used);
            }
            return widths;

            static int Span(string attrs, string name)
            {
                var m = Regex.Match(attrs, name + @"=""(\d+)""");
                return m.Success ? int.Parse(m.Groups[1].Value) : 1;
            }
        }
    }
}
