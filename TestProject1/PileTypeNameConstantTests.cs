using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭種の呼び名は <see cref="PileTypeNames"/> の 1 か所で決めること。
    ///
    /// <para>解析・検定・計算書・画面が同じ名前で分岐する。文字列を直書きすると、呼び名を変えた
    /// ときに片方だけ取り残され、<b>その杭種の分岐に一度も入らなくなる</b> (落ちないので気づけない)。
    /// 2026-09-19 に計算書の β1・β2 の表で 20 か所を定数へ寄せた。</para>
    ///
    /// <para>文の中に出てくる語 (説明文・ヘルプ・警告) は対象にしない。見るのは
    /// <b>杭種名そのものと完全に一致する文字列リテラル</b>だけ。</para>
    /// </summary>
    [TestClass]
    public class PileTypeNameConstantTests
    {
        /// <summary>定数の値が重複していないこと (重複すると分岐が食い合う)。</summary>
        [TestMethod]
        public void TheNamesAreUnique()
        {
            var values = typeof(PileTypeNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (Name: f.Name, Value: (string)f.GetValue(null)!))
                .ToList();

            TestSource.AssertScanned(values.Count, 12, "杭種の定数");

            var duplicated = values.GroupBy(v => v.Value).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} = {string.Join(" / ", g.Select(x => x.Name))}")
                .ToList();
            Assert.AreEqual(0, duplicated.Count,
                "同じ文字列の定数が 2 つあります: " + string.Join(", ", duplicated));
        }

        /// <summary>
        /// 杭種名と完全に一致する文字列リテラルを、定数の外に書かないこと。
        /// </summary>
        [TestMethod]
        public void NobodyWritesAPileTypeNameAsALiteral()
        {
            var names = typeof(PileTypeNames)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(string))
                .Select(f => (string)f.GetValue(null)!)
                .Where(v => v.Length >= 3)          // 「鋼管部」等の短い部位名も対象にする
                .ToHashSet();

            var offenders = new List<string>();
            int scanned = 0;

            foreach (string file in Directory.EnumerateFiles(
                         TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("\\obj\\") || file.Contains("\\bin\\")) continue;
                if (Path.GetFileName(file) == "PileTypeNames.cs") continue;
                scanned++;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///")) continue;

                    foreach (Match m in Regex.Matches(line, "\"([^\"\\\\]*)\""))
                    {
                        if (names.Contains(m.Groups[1].Value))
                            offenders.Add($"{Path.GetFileName(file)}:{i + 1}  \"{m.Groups[1].Value}\"");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 100, "走査した C# ファイル");

            Assert.AreEqual(0, offenders.Count,
                "杭種名を直書きしています (呼び名を変えたときに取り残されます。PileTypeNames を使うこと):\n  "
                + string.Join("\n  ", offenders.Take(20)));
        }
    }
}
