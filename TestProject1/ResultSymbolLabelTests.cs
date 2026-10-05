using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 解析結果の選択肢を「記号 名前 [単位]」で見せる (ResultSymbolLabelConverter)。
    /// 以前は記号だけで、何を選んでいるのか・数字の単位が何かがその場で分からなかった。
    /// </summary>
    [TestClass]
    public class ResultSymbolLabelTests
    {
        private static List<string> AllOptions()
        {
            var vm = new MainWindowViewModel();
            return vm.AnalysisResultBeamForceOption
                .Concat(vm.AnalysisResultNodeDisplacementOption)
                .Concat(vm.AnalysisResultSoilSpringOption)
                .ToList();
        }

        [TestMethod]
        public void EverySelectableSymbol_HasNameAndUnit()
        {
            var options = AllOptions();
            Assert.IsTrue(options.Count >= 26, $"選択肢が {options.Count} 件しか見つからない (収集が壊れている可能性)");
            var missing = options.Where(s => !ResultSymbolLabelConverter.KnownSymbols.Contains(s)).ToList();
            Assert.AreEqual(0, missing.Count, "名前と単位の無い記号があります。ResultSymbolLabelConverter に追加してください: " + string.Join(", ", missing));
            CollectionAssert.AreEquivalent(ResultSymbolDescriptionConverter.KnownSymbols.ToList(), ResultSymbolLabelConverter.KnownSymbols.ToList(),
                "名前・単位の表と説明の表で記号の集合がずれています");
        }

        /// <summary>単位は図とツールチップの数字に合わせる: 力 kN・モーメント kN·m・変位 mm・回転角 rad。</summary>
        [TestMethod]
        public void Units_FollowTheKindOfQuantity()
        {
            foreach (string s in ResultSymbolLabelConverter.KnownSymbols)
            {
                string expected = s[0] switch
                {
                    'F' or 'R' => "kN",
                    'M' => "kN·m",
                    'U' => "mm",
                    'θ' => "rad",
                    _ => "?",
                };
                StringAssert.EndsWith(ResultSymbolLabelConverter.Label(s), $"[{expected}]", $"{s} の単位");
                Assert.AreEqual($"{s} [{expected}]", ResultSymbolLabelConverter.Label(s, shortForm: true));
            }
            Assert.AreEqual("Mh　水平合成曲げモーメント　[kN·m]", ResultSymbolLabelConverter.Label("Mh"));
            Assert.AreEqual("未登録", ResultSymbolLabelConverter.Label("未登録"), "未登録の記号はそのまま見せる");
        }

        /// <summary>リボンと結果パネルの 3 つずつの選択肢すべてが、一覧で名前と単位を、閉じた枠で単位を見せる。</summary>
        [TestMethod]
        public void AllSymbolDropdowns_ShowNameAndUnit()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            int withStyle = Regex.Matches(xaml, Regex.Escape("ItemContainerStyle=\"{StaticResource ResultSymbolItemStyle}\"")).Count;
            int withShort = Regex.Matches(xaml, Regex.Escape("ItemContainerStyle=\"{StaticResource ResultSymbolItemStyle}\" ItemTemplate=\"{StaticResource ResultSymbolShortTemplate}\"")).Count;
            Assert.AreEqual(6, withStyle, "記号の選択肢の数が変わりました。このテストの前提を見直してください");
            Assert.AreEqual(withStyle, withShort, "閉じた枠に単位を出していない選択肢があります");
            StringAssert.Contains(xaml, "<TextBlock Text=\"{Binding Converter={StaticResource ResultSymbolLabel}}\"/>");
        }

        /// <summary>
        /// 沈下の結果の「沈下種別」は、沈下の表示内容のすべてで出ること。
        /// 以前は表示の条件が「沈下反力」と完全一致で書かれていたが、実際の表示内容は
        /// 「沈下反力（地盤）」「沈下反力（杭頭集約）」なので出ず、単杭と基礎梁考慮を切り替えられなかった。
        /// </summary>
        [TestMethod]
        public void SettlementTypeDropdown_AppearsForEverySettlementContent()
        {
            var field = typeof(MainWindowViewModel).GetField("CanonicalAnalysisContentOrder", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "CanonicalAnalysisContentOrder が見つかりません (改名したらこのテストも直す)");
            var settlement = ((IEnumerable<string>)field!.GetValue(null)!).Where(c => c.StartsWith("沈下")).ToList();
            Assert.IsTrue(settlement.Count >= 5, "沈下の表示内容が見つかりません");

            string xaml = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            var param = Regex.Match(xaml, @"ConverterParameter=(沈下量[^}]*)\}");
            Assert.IsTrue(param.Success, "結果パネルの沈下種別の表示条件が見つかりません");
            var inPanel = param.Groups[1].Value.Split('|');
            foreach (string c in settlement)
            {
                StringAssert.Contains(xaml, $"<Condition Binding=\"{{Binding AnalysisResultContent}}\" Value=\"{c}\"/>", $"リボン: {c}");
                CollectionAssert.Contains(inPanel, c, $"結果パネル: {c}");
            }
        }
    }
}
