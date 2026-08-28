using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 保存グラフに入る型が「書き出せるが読み戻せない」形になっていないこと。
    ///
    /// この製品で最も痛いのは保存ファイルが開けなくなることで、
    /// その原因は決まって<b>復元できないプロパティ</b>にある。
    /// <list type="bullet">
    /// <item><c>ValueTuple</c> の要素 — 中身がフィールドなので既定では直列化されない。
    ///   保存すると <c>{}</c> の羅列になり、読み戻すと空になる (例外は出ない)</item>
    /// <item>get のみのプロパティ — 書き出されるが読み戻せない。
    ///   <c>ReferenceHandler.Preserve</c> では <c>$id</c> がそこに付くため、
    ///   他所から <c>$ref</c> されると<b>ファイルが開けなくなる</b></item>
    /// </list>
    /// どちらも <c>[JsonIgnore]</c> を付けるか、保存できる形に直すこと。
    /// </summary>
    [TestClass]
    public class SaveGraphShapeTests
    {
        /// <summary>
        /// ValueTuple を持つ公開プロパティのうち、保存対象のままでよいと判断したもの。
        /// <b>増やさないための一覧</b>。追加するときは「保存できなくても困らない」根拠を書くこと。
        /// </summary>
        private static readonly HashSet<string> KnownTupleProperties =
        [
            // 杭頭接合部の N-M-θ 表。読込後に GetNMThetaRelationship() で作り直す
            "CapringPile.ThetasMs",
            "CaptainPile.ThetasMs",
            "FTPile.ThetasMs",
            // 画面表示用の説明文。値を持たない
            "PileTop.Description",
        ];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(SaveGraphShapeTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        /// <summary>
        /// ValueTuple を要素に持つ公開プロパティは <c>[JsonIgnore]</c> か、一覧に載っていること。
        /// </summary>
        [TestMethod]
        public void TuplePropertiesAreNotSilentlySaved()
        {
            var offenders = new List<string>();

            foreach (var (file, lines) in ModelSources())
            {
                string typeName = Path.GetFileNameWithoutExtension(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    var m = Regex.Match(lines[i],
                        @"^\s*public\s+[\w\.<>\[\]\?]*<\(.*?\)>\s+(\w+)\s*(\{|=>)");
                    if (!m.Success) continue;

                    string name = $"{typeName}.{m.Groups[1].Value}";
                    if (KnownTupleProperties.Contains(name)) continue;
                    if (HasJsonIgnoreAbove(lines, i)) continue;

                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {name}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "ValueTuple のプロパティが保存対象のままです "
                + "(保存すると {} の羅列になり、読み戻すと空になります):\n  "
                + string.Join("\n  ", offenders));
        }

        /// <summary><c>[JsonIgnore]</c> が直前 3 行以内に付いているか。</summary>
        private static bool HasJsonIgnoreAbove(string[] lines, int index)
        {
            for (int i = Math.Max(0, index - 3); i < index; i++)
                if (lines[i].Contains("JsonIgnore", StringComparison.Ordinal)) return true;
            return false;
        }

        private static IEnumerable<(string File, string[] Lines)> ModelSources()
        {
            string root = FindSolutionRoot();
            foreach (string dir in new[] { "FEM", "Models" })
            {
                string full = Path.Combine(root, "Graphics_r1", dir);
                if (!Directory.Exists(full)) continue;

                foreach (string cs in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                {
                    if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                    yield return (cs, File.ReadAllLines(cs));
                }
            }
        }
    }
}
