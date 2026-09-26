using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;

namespace TestProject1
{
    /// <summary>
    /// 荷重組合せの複製と、グラフの組合せ選択 (2026-09-26 のレビュー)。
    /// - DeepCopy が MemberwiseClone で PropertyChanged の購読者まで写していた
    /// - グラフの選択肢が係数を丸めた文字列で、表示名の重なる 2 つの組合せを選び分けられなかった
    /// </summary>
    [TestClass]
    public class LoadCombinationChoiceTests
    {
        [TestMethod]
        public void DeepCopyDoesNotCarryTheSubscribers()
        {
            var original = new LoadCombination(3, 0.5, 1.0, 0.25) { IsApplicable = false };
            int raised = 0;
            original.PropertyChanged += (_, _) => raised++;

            var copy = original.DeepCopy();
            copy.Alpha1 = 0.9;
            copy.IsApplicable = true;

            Assert.AreEqual(0, raised, "複製を変えると、元の組合せの購読者へ通知が飛んでいます");
            Assert.AreEqual(0.5, original.Alpha1);
            Assert.AreNotSame(original, copy);
        }

        [TestMethod]
        public void DeepCopyKeepsTheValues()
        {
            var original = new LoadCombination(3, 0.5, 1.0, 0.25) { IsApplicable = false, IsAnalyzed = false };
            var copy = original.DeepCopy();
            Assert.AreEqual((3, 0.5, 1.0, 0.25), (copy.No, copy.Alpha1, copy.Beta1, copy.Beta2));
            Assert.IsFalse(copy.IsApplicable);
            Assert.IsFalse(copy.IsAnalyzed);
        }

        [TestMethod]
        public void ChoicesWithTheSameLabelAreTellApartByNumber()
        {
            var choices = LoadCombinationChoice.Build(
            [
                new LoadCombination(1, 0.999, -1.0, 0.999),
                new LoadCombination(2, 1.0, -0.999, 1.0),
                new LoadCombination(3, 0.5, 1.0, 0.5),
            ]);

            Assert.IsTrue(choices[0].IsAll, "先頭は「すべて」");
            CollectionAssert.AreEqual(new int?[] { null, 1, 2, 3 }, choices.Select(c => c.No).ToList());
            Assert.AreEqual(choices.Count, choices.Select(c => c.Label).Distinct().Count(), "表示名が重なっていて選び分けられません");
            StringAssert.Contains(choices[1].Label, "組合せ1");
            StringAssert.Contains(choices[2].Label, "組合せ2");
            Assert.AreEqual("0.50/1.00/0.50", choices[3].Label, "重ならない組合せにまで番号を添えています");
            Assert.AreNotEqual(new LoadCombinationChoice(1, "x"), new LoadCombinationChoice(2, "x"), "番号の違う選択肢が同じものとして扱われます");
        }

        /// <summary>グラフの選択は番号で組合せを引くこと (表示名の文字列で照合しない)。</summary>
        [TestMethod]
        public void TheGraphSelectsTheCombinationByNumber()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.Options.cs"),
                "GetSelectedLoadCombinations()");
            StringAssert.Contains(body, "SelectedLoadCombinationOption.No");
            Assert.IsFalse(body.Contains("GetName()"), "グラフが組合せを表示名の文字列で引いています");
        }

        [TestMethod]
        public void TheAnalyzedMarkMatchesByNumber()
        {
            var one = new LoadCombination(1, 0.999, -1.0, 0.999);
            var two = new LoadCombination(2, 1.0, -0.999, 1.0);   // 表示名は one と同じ
            var model = new AnaModel { AnalysisStepResults = [new AnalysisStepResult { LoadCase = new LoadCase { Level = 1, No = 1 }, LoadCombination = two }] };
            var converter = new LoadCombinationAnalyzedConverter();
            var choices = LoadCombinationChoice.Build([one, two]);

            Assert.AreEqual(Visibility.Collapsed, converter.Convert([choices[1], PileDesign.Common.UiText.All, model], typeof(Visibility), null!, null!),
                "解析していない組合せ 1 に、表示名の同じ組合せ 2 の解析済みの印が付いています");
            Assert.AreEqual(Visibility.Visible, converter.Convert([choices[2], PileDesign.Common.UiText.All, model], typeof(Visibility), null!, null!));
        }

        // ── メイン画面・表の窓 ─────────────────────────────

        /// <summary>メイン画面の選択肢の文字列 (表示名。重なるときは番号付き) から、選んだ組合せそのものを引くこと。</summary>
        [TestMethod]
        public void TheMainWindowLabelsResolveToTheRightCombination()
        {
            var list = new System.Collections.ObjectModel.ObservableCollection<LoadCombination>
            {
                new(1, 0.999, -1.0, 0.999),
                new(2, 1.0, -0.999, 1.0),
                new(3, 0.5, 1.0, 0.5),
            };
            foreach (var c in list)
            {
                string label = LoadCombinations.LabelOf(list, c)!;
                Assert.AreSame(c, LoadCombinations.GetLoadCombination(list, label), $"組合せ{c.No} の選択肢 {label} が別の組合せに解けます");
            }
            Assert.AreSame(list[2], LoadCombinations.GetLoadCombination(list, "0.50/1.00/0.50"), "係数の文字列でも引けること (従来の呼び方)");
            Assert.IsNull(LoadCombinations.GetLoadCombination(list, "9.99/9.99/9.99"));
        }

        [TestMethod]
        public void TheMainWindowBuildsItsOptionsFromTheChoices()
        {
            string ctor = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.Constructor.cs"),
                "private void UpdateLoadCombinationOption()");
            StringAssert.Contains(ctor, "LoadCombinationChoice.Build(", "メイン画面の選択肢が表示名の重なりを見分けていません");

            string liq = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.DisplayOptions.cs");
            Assert.IsFalse(liq.Contains("r.LoadCombination?.GetName() == SelectedLoadCombinationName"),
                "液状化の自動判定が組合せを表示名で絞っています");
        }

        /// <summary>表の窓は、表が持つ組合せの番号で絞り込む。</summary>
        [TestMethod]
        public void TheTableWindowFiltersByNumber()
        {
            string service = TestSource.Read("Graphics_r1", "Services", "AnalysisResultTableService.cs");
            int names = Regex.Matches(service, @"LoadCombinationName = loadCombination\?\.Name").Count;
            int numbers = Regex.Matches(service, @"LoadCombinationNo = loadCombination\?\.No").Count;
            Assert.IsTrue(names >= 8, $"(前提) 表を作る箇所が見つかりません ({names})");
            Assert.AreEqual(names, numbers, "組合せの番号を持たない表があります (表の窓で絞り込めません)");

            string vm = TestSource.Read("Graphics_r1", "ViewModels", "TableWindowViewModel.cs");
            Assert.IsFalse(vm.Contains("t.LoadCombinationName == SelectedLoadCombinationFilter"), "表の窓が組合せを表示名で絞っています");
            Assert.IsFalse(vm.Contains("c.LoadCombinationName != SelectedLoadCombinationFilter"), "表の窓が行の組合せを表示名で絞っています");
        }
    }
}
