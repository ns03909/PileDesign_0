using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 地盤変位の式 (基礎指針'19 (4.5.1)) の定数 C2 が文献値であること。
    ///
    /// <para>C2 は表層の土質の減衰特性から決まる定数で、文献値は粘性土 0.53、砂質土 0.66。
    /// 解析が通る経路 (<c>GroundLayerViewModel.RecalculateVSE</c> → <c>DmaxUStar</c> →
    /// 水平解析の強制変位) だけが砂質土を 0.666 にしていた。0.66 を持つ
    /// <c>GroundHorizontalDisplacementInput</c> はどこからも参照されない複製で (同日に削除)、
    /// <b>正しい方が使われていなかった</b>。ヘルプの記号表は 0.66 のまま。
    /// 2026-09-11 に文献で 0.66 を確かめて解析側を直した
    /// (応答変位の a1(b1) 法・砂質土で Dmax の第 1 項が 0.9% 小さくなる)。</para>
    ///
    /// <para>この値は実装ではなく<b>文献が正</b>なので、期待値はここに書く。
    /// 同じ式の複製が別の値を持ち込んだら落ちるよう、C2 への代入はすべて見る。</para>
    /// </summary>
    [TestClass]
    public class GroundDisplacementConstantTests
    {
        private const double Clay = 0.53;
        private const double Sand = 0.66;

        [TestMethod]
        public void TheAnalysisPathUsesTheLiteratureValues()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "GroundLayerViewModel.cs");
            var m = Regex.Match(src,
                @"\bC2\s*=\s*\(shallowSoilType\s*==\s*""粘性土""\)\s*\?\s*([0-9.]+)\s*:\s*([0-9.]+)\s*;");
            Assert.IsTrue(m.Success, "解析が通る経路の C2 が見つかりません (書き方が変わった?)");

            Assert.AreEqual(Clay, Parse(m.Groups[1].Value), 0.0, "粘性土の C2 が文献値と違います");
            Assert.AreEqual(Sand, Parse(m.Groups[2].Value), 0.0, "砂質土の C2 が文献値と違います");
        }

        [TestMethod]
        public void EveryAssignmentUsesALiteratureValue()
        {
            string root = TestSource.Dir("Graphics_r1");
            int seen = 0;

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                         .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
            {
                string src = Regex.Replace(File.ReadAllText(path), "//.*", "");
                foreach (Match a in Regex.Matches(src, @"\bC2\s*=\s*([^;=][^;]*);"))
                {
                    foreach (Match n in Regex.Matches(a.Groups[1].Value, @"\d+\.\d+"))
                    {
                        double v = Parse(n.Value);
                        Assert.IsTrue(v == Clay || v == Sand,
                            $"{Path.GetFileName(path)} の C2 に文献値 ({Clay} / {Sand}) でない {n.Value} があります: {a.Value.Trim()}");
                        seen++;
                    }
                }
            }

            // 解析が通る経路の 2 つ (粘性土・砂質土)
            TestSource.AssertScanned(seen, 2, "C2 への代入");
        }

        [TestMethod]
        public void TheHelpStatesTheLiteratureValues()
        {
            string help = TestSource.Read("Graphics_r1", "Help", "help.html");
            string expected = $"粘性土で{Clay.ToString("0.00", CultureInfo.InvariantCulture)},"
                + $"砂質土で{Sand.ToString("0.00", CultureInfo.InvariantCulture)}";
            StringAssert.Contains(help, expected, "ヘルプの記号表の C2 が文献値と違います");
        }

        private static double Parse(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
