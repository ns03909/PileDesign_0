using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// リボンの解析結果選択肢に出る記号 (Mh・UH・RX など) に説明が付いているか。
    ///
    /// 選択肢を足したときに説明を書き忘れると、説明の無い記号が黙って混ざる。
    /// </summary>
    [TestClass]
    public class ResultSymbolDescriptionTests
    {
        [TestMethod]
        public void EverySelectableResultSymbol_HasDescription()
        {
            var vm = new MainWindowViewModel();

            var groups = new (string Name, IEnumerable<string> Symbols)[]
            {
                ("梁応力",   vm.AnalysisResultBeamForceOption),
                ("節点変位", vm.AnalysisResultNodeDisplacementOption),
                ("地盤反力", vm.AnalysisResultSoilSpringOption),
            };

            var missing = new List<string>();
            int total = 0;

            foreach (var (name, symbols) in groups)
            {
                foreach (string symbol in symbols)
                {
                    total++;
                    if (string.IsNullOrWhiteSpace(ResultSymbolDescriptionConverter.Describe(symbol)))
                        missing.Add($"{name}: \"{symbol}\"");
                }
            }

            Assert.IsTrue(total >= 26, $"選択肢が {total} 件しか見つからない (収集が壊れている可能性)");
            Assert.AreEqual(0, missing.Count,
                "説明の無い記号があります。ResultSymbolDescriptionConverter に追加してください:\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// 逆方向: 使われなくなった記号の説明が残っていないこと。
        /// 残っていても害は無いが、選択肢の実態と説明表がずれている合図になる。
        /// </summary>
        [TestMethod]
        public void NoOrphanedSymbolDescriptions()
        {
            var vm = new MainWindowViewModel();
            var used = vm.AnalysisResultBeamForceOption
                .Concat(vm.AnalysisResultNodeDisplacementOption)
                .Concat(vm.AnalysisResultSoilSpringOption)
                .ToHashSet();

            var orphans = ResultSymbolDescriptionConverter.KnownSymbols
                .Where(s => !used.Contains(s))
                .ToList();

            Assert.AreEqual(0, orphans.Count,
                "選択肢に存在しない記号の説明が残っています: " + string.Join(", ", orphans));
        }
    }
}
