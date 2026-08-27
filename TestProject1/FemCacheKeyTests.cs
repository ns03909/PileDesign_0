using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// FEM の材料・断面キャッシュの鍵が足りていること。
    ///
    /// 鍵が足りないと、別物どうしが同じものとして使い回される。
    /// 例外も警告も出ず、剛性だけが黙って変わるので、結果を見ても気付けない。
    ///
    /// 実際に次の 2 つが起きていた。
    /// <list type="bullet">
    /// <item>材料をヤング係数だけで引いていた。杭は &#957;=0.2 固定、基礎梁は利用者の入力値。
    ///   同じ E で &#957; が違う組合せでは先に作られた方が使い回され、
    ///   せん断・ねじり剛性 G = E / (2(1+&#957;)) が変わる</item>
    /// <item>断面の鍵にせん断断面積が無かった。杭は「せん断断面積 = 断面積」、
    ///   基礎梁は (5/6)bh と作り方が違うのに同じキャッシュを共有している</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class FemCacheKeyTests
    {
        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(FemCacheKeyTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        private static readonly string[] ModellingFiles =
            ["AnalysisModelling.cs", "VerticalBeamModelling.cs"];

        /// <summary>
        /// 材料の鍵にポアソン比が入っていること。
        /// </summary>
        [TestMethod]
        public void MaterialCache_IsKeyedByPoissonRatioToo()
        {
            foreach (var (file, text) in ReadModellingSources())
            {
                var decl = Regex.Match(text, @"ConcurrentDictionary<([^>]*)>\s+_materialCache");
                Assert.IsTrue(decl.Success, $"{file}: _materialCache の宣言が見つからない");

                string key = decl.Groups[1].Value;
                StringAssert.Contains(key, "Nu",
                    $"{file}: 材料の鍵にポアソン比が入っていない ({key})。"
                    + " 同じヤング係数で ν が違う材料を取り違える");
            }
        }

        /// <summary>
        /// 断面の鍵にせん断断面積が入っていること。
        /// </summary>
        [TestMethod]
        public void SectionCache_IsKeyedByShearAreaToo()
        {
            foreach (var (file, text) in ReadModellingSources())
            {
                foreach (Match m in Regex.Matches(text, @"_sectionCache\.GetOrAdd\((\w+)"))
                {
                    string keyVar = m.Groups[1].Value;
                    var assign = Regex.Match(text,
                        Regex.Escape(keyVar) + @"\s*=\s*\((?<body>[^;]*?)\);", RegexOptions.Singleline);
                    Assert.IsTrue(assign.Success, $"{file}: {keyVar} の組み立てが見つからない");

                    int fields = assign.Groups["body"].Value.Split(',').Length;
                    Assert.IsTrue(fields >= 7,
                        $"{file}: 断面の鍵が {fields} 項目しかない ({keyVar})。"
                        + " 断面積・ねじり・Iy・Iz・E に加えて、せん断断面積 2 つが要る");
                }
            }
        }

        /// <summary>
        /// 同じ E でポアソン比だけ違う材料が、別のせん断剛性を持つこと。
        /// (鍵を直しても材料側が ν を使っていなければ意味がない)
        /// </summary>
        [TestMethod]
        public void PoissonRatio_ChangesTheShearModulus()
        {
            var concrete = new PileDesign.FEM.Material(25000.0, 0.2);
            var steel = new PileDesign.FEM.Material(25000.0, 0.3);

            double g(PileDesign.FEM.Material m)
            {
                var p = m.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(x => x.Name is "G" or "ShearModulus");
                Assert.IsNotNull(p, "Material にせん断弾性係数のプロパティが無い");
                return Convert.ToDouble(p!.GetValue(m));
            }

            Assert.AreNotEqual(g(concrete), g(steel), 1e-9,
                "ポアソン比を変えてもせん断弾性係数が変わらない");
            Assert.AreEqual(25000.0 / (2.0 * 1.2), g(concrete), 1e-9);
            Assert.AreEqual(25000.0 / (2.0 * 1.3), g(steel), 1e-9);
        }

        private static IEnumerable<(string File, string Text)> ReadModellingSources()
        {
            string fem = Path.Combine(FindSolutionRoot(), "Graphics_r1", "FEM");
            foreach (string name in ModellingFiles)
            {
                string path = Path.Combine(fem, name);
                Assert.IsTrue(File.Exists(path), $"{name} が見つからない");
                yield return (name, File.ReadAllText(path));
            }
        }
    }
}
