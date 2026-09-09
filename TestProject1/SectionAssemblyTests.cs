using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 断面計算オブジェクトを組み立てるのは PileSection.CreateSectionCalculator (杭体) と
    /// PileTop (杭頭部、パイルキャップのコンクリートで組む別物) だけ。他の場所で new しない。
    ///
    /// 2026-09-07 まで、杭断面ウィンドウ (10 か所) と MphiCurveResolver の杭中間部 M-φ (解析経路) が
    /// 断面を自前で new しており、材料側のオプション (KCTB の εcu=0.005、帯筋、1.1F 等) が渡らなかった。
    /// εcu=0.005 では終点の曲率だけ 0.005・材料は 0.003 のままとなり、M-φ が頂点の後に下がる形で
    /// 画面に出、杭中間部では解析にも入っていた。組み立てが 2 か所にあると、片方にだけ引数を足した
    /// ときにもう片方が黙って既定値で動く。組み立て方は 1 か所に置き、他はそれを呼ぶ。
    ///
    /// 鋼管杭の SteelPipeSection は別系統 (ファクトリが null を返す設計) で、断面クラス自身が
    /// 部分断面を組む (累加式の RC 部など) のも対象外。
    /// </summary>
    [TestClass]
    public class SectionAssemblyTests
    {
        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();

        private static readonly Regex HandBuilt = new(
            @"\bnew\s+(InsituConcrete|InsituSteelPipe|MainBars|Tendons|PrecastPHCConcrete|PrecastPRCConcrete|PrecastSCConcrete|PrecastSteelPipe" +
            @"|InsituReinforcedConcreteSection|InsituSteelPipeReinforcedConcreteSection|PHCSection|PRCSection|SCSection" +
            @"|InsituSteelPipeReinforcedConcreteTopSection|PrecastPileTopSection)\s*\(");

        /// <summary>組み立てを許す場所 (相対パスの接頭辞)。</summary>
        private static readonly string[] Allowed =
        [
            "Models/InputData/PileSection.cs",                              // 杭体のファクトリ CreateSectionCalculator
            "Models/InputData/PileTop.cs",                                  // 杭頭部 (パイルキャップのコンクリートで組む)
            "Models/InputData/AbstractPileSection.cs",                      // 断面クラス自身
            "Models/InputData/InsituReinforcedConcreteSection.cs",
            "Models/InputData/InsituSteelPipeReinforcedConcreteSection.cs",
            "Models/InputData/InsituSteelPipeReinforcedConcreteSection.Superposition.cs",  // 累加式の RC 部
            "Models/InputData/InsituSteelPipeReinforcedConcreteTopSection.cs",
            "Models/InputData/PrecastPileSection.cs",
            "Models/InputData/PrecastPileTopSection.cs",
        ];

        [TestMethod]
        public void SectionsAreAssembledOnlyByTheFactoryAndThePileTop()
        {
            string root = Path.Combine(FindSolutionRoot(), "Graphics_r1");
            var offenders = new List<string>();
            int scanned = 0;
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal)) continue;
                if (Allowed.Any(a => rel.Equals(a, StringComparison.Ordinal))) continue;
                scanned++;

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    if (HandBuilt.IsMatch(line))
                        offenders.Add($"{rel}:{i + 1}: {line.Trim()}");
                }
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");

            Assert.AreEqual(0, offenders.Count,
                "ファクトリと杭頭以外で断面・材料を組み立てています。PileSection.CreateSectionCalculator() を使ってください:\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
