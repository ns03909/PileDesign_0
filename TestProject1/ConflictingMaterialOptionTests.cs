using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 安全限界を「e 関数型」で算定するときに効かなくなる材料オプションが、
    /// どの画面でも灰色になり、理由が読めること。
    ///
    /// これらのオプションを読むのは <c>ConcreteMaterial.GetSigma</c> の<b>バイリニアの枝だけ</b>で、
    /// e 関数の枝は 1 つも読まない。つまり e 関数型を選んでいる間は、切り替えても
    /// 安全限界 N-M 曲線は 1 mm も動かない。
    ///
    /// 基本設定ウィンドウでは圧縮側と鋼管降伏だけが灰色になっており、
    /// <b>引張側は灰色にならず、杭断面ウィンドウでは 3 つとも押せた</b>。
    /// 押しても何も起きないので「壊れている」としか見えない
    /// （実際に「引張側を変えても N-M が変わらない」として報告された）。
    ///
    /// 灰色にするだけでは理由が読めないので、<c>DisabledReason</c> も必須にする。
    /// </summary>
    [TestClass]
    public class ConflictingMaterialOptionTests
    {
        /// <summary>e 関数型のとき効かなくなるオプション（VM のプロパティ名）。</summary>
        private static readonly string[] ConflictingOptions =
        [
            nameof(FundamentalInput.IgnoreConcreteTensileStrength),
            nameof(FundamentalInput.UseReducedConcreteCompressiveStrength),
            nameof(FundamentalInput.SteelPipeYieldAt11F),
        ];

        private static readonly string[] Windows =
        [
            "FundamentalWindow.xaml",
            "PileSectionWindow.xaml",
        ];

        private static string FindSolutionRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(
                typeof(ConflictingMaterialOptionTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html")))
                    return dir.FullName;
            }
            throw new FileNotFoundException("ソリューションルートが見つかりません");
        }

        // <ctrl:MaterialOptionRadioPair ... /> を 1 個ずつ取り出す
        private static IEnumerable<string> RadioPairTags(string xaml) =>
            Regex.Matches(xaml, @"<ctrl:MaterialOptionRadioPair\b.*?/>", RegexOptions.Singleline)
                 .Select(m => m.Value);

        [TestMethod]
        public void ConflictingOptionsAreDisabledAndExplainedInEveryWindow()
        {
            string root = FindSolutionRoot();
            var offenders = new List<string>();
            int checkedCount = 0;

            foreach (string window in Windows)
            {
                string xaml = File.ReadAllText(Path.Combine(root, "Graphics_r1", "Views", window));

                foreach (string tag in RadioPairTags(xaml))
                {
                    string? option = ConflictingOptions.FirstOrDefault(
                        o => Regex.IsMatch(tag, $@"IsAlternativeSelected=""\{{Binding\s+{o}\b"));
                    if (option == null) continue;

                    checkedCount++;

                    if (!tag.Contains("AreRadiosEnabled=", StringComparison.Ordinal))
                        offenders.Add($"{window}: {option} に AreRadiosEnabled が無い（e 関数型でも押せてしまう）");
                    else if (!tag.Contains("ConflictingMaterialOptionsEnabled", StringComparison.Ordinal))
                        offenders.Add($"{window}: {option} の AreRadiosEnabled が "
                                    + "ConflictingMaterialOptionsEnabled ではない");

                    if (!tag.Contains("DisabledReason=", StringComparison.Ordinal))
                        offenders.Add($"{window}: {option} に DisabledReason が無い（灰色の理由が読めない）");
                }
            }

            Assert.AreEqual(ConflictingOptions.Length * Windows.Length, checkedCount,
                "競合オプションのラジオペアが想定数だけ見つかりませんでした。"
                + "画面から消したか、バインド名を変えたのなら ConflictingOptions を直してください。");

            Assert.AreEqual(0, offenders.Count,
                "e 関数型で効かなくなるオプションが、押せる／理由が読めない状態です:\n  "
                + string.Join("\n  ", offenders));
        }

        /// <summary>
        /// 「効かない」の根拠。e 関数型では、3 つのオプションをどう変えても
        /// 安全限界の応力度が 1 つも動かないこと（バイリニアでは動くこと）。
        /// ここが崩れたら、灰色にしている前提そのものが変わっている。
        /// </summary>
        [TestMethod]
        public void EFunctionIgnoresTheConflictingOptions()
        {
            var concrete = new InsituConcrete(1000.0, 0.75, 27.0);
            double epsT = -0.5 * concrete.EpsilonCr_bilinear;   // 引張側（ひび割れ前）
            double epsC = 0.9 * concrete.EpsilonCu;             // 圧縮側（頭打ち域）

            static double[] Sample(InsituConcrete c, MaterialLaw law, double t, double p) =>
                [c.GetStress(law, t), c.GetStress(law, p)];

            try
            {
                ConcreteModelOptions.IgnoreTensileStrength = false;
                ConcreteModelOptions.UseReducedCompression = false;
                double[] eBase = Sample(concrete, MaterialLaw.EFunction, epsT, epsC);
                double[] bBase = Sample(concrete, MaterialLaw.Bilinear, epsT, epsC);

                ConcreteModelOptions.IgnoreTensileStrength = true;
                ConcreteModelOptions.UseReducedCompression = true;
                double[] eOpt = Sample(concrete, MaterialLaw.EFunction, epsT, epsC);
                double[] bOpt = Sample(concrete, MaterialLaw.Bilinear, epsT, epsC);

                CollectionAssert.AreEqual(eBase, eOpt,
                    "e 関数型がオプションに反応しました。灰色にしている前提が変わっています。");
                CollectionAssert.AreNotEqual(bBase, bOpt,
                    "バイリニア型がオプションに反応しません。オプションが死んでいる可能性があります。");
            }
            finally
            {
                ConcreteModelOptions.IgnoreTensileStrength = false;
                ConcreteModelOptions.UseReducedCompression = false;
            }
        }
    }
}
