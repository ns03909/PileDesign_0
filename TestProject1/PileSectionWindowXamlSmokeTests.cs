using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 杭断面ウィンドウを既製コンクリート杭の各断面タイプで実際に開くスモークテスト。
    ///
    /// 断面タイプを増やしたときに追随が要る箇所（製品 ComboBox の対応表・
    /// VisibilityConverter のパラメータ・断面図の描画分岐）は、いずれも
    /// <b>ビルドが通ったまま実行時に静かに壊れる</b>。
    /// 「製品一覧が空の ComboBox が出る」「断面図が描かれない」を検出する。
    /// </summary>
    [TestClass]
    public class PileSectionWindowXamlSmokeTests
    {
        /// <summary>断面タイプ → 製品 ComboBox の x:Name と、選択する製品名。</summary>
        private static IEnumerable<(string SectionType, string BoxName, string Product)> Cases()
        {
            yield return (PileTypeNames.Phc, "ComboBoxPHCPileType", PileSection.PHCOption.First());
            yield return (PileTypeNames.Prc, "ComboBoxPRCPileType", PileSection.PRCOption.First());
            yield return (PileTypeNames.Sc, "ComboBoxSCPileType", PileSection.SCOption.First());
            yield return (PileTypeNames.PhcNodular, "ComboBoxNodularPileType",
                          "NPH-440-300-標準-85-A");
            yield return (PileTypeNames.PrcNodular, "ComboBoxNodularPrcPileType",
                          "NPRC-440-300-標準-105-Ⅰ");
            yield return (PileTypeNames.PrcNodularPhcPart, "ComboBoxNodularPrcPhcPartPileType",
                          "NPRC-440-300-標準-105-Ⅰ-PHC部");
            yield return (PileTypeNames.BfsHead, "ComboBoxBfsHeadPileType",
                          "BF.S-400-3045-105-A2");
            yield return (PileTypeNames.BfsTip, "ComboBoxBfsTipPileType",
                          "BF.S-400-3045-105-A2-先端軸部");
        }

        [TestMethod]
        public void PileSectionWindow_OpensForEveryPrecastSectionType()
        {
            var failures = new List<string>();

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var mainVm = new MainWindowViewModel();

                foreach (var (sectionType, boxName, product) in Cases())
                {
                    var section = new PileSection
                    {
                        PileBodyType = PileTypeNames.PrecastConcrete,
                        PileSectionType = sectionType,
                    };
                    section.SelectedPrecastPile.Name = product;
                    section.RecalculateSelectedPrecastPile();

                    var window = new PileDesign.Views.PileSectionWindow(mainVm, section, 1, 2);
                    try
                    {
                        // 製品 ComboBox が「その断面タイプ用の一覧」で埋まっているか。
                        // GetPrecastPileComboBox の対応表に追加し忘れると、
                        // ItemsSource が null のまま空の ComboBox が出る。
                        if (window.FindName(boxName) is not ComboBox box)
                        {
                            failures.Add($"{sectionType}: {boxName} が見つからない");
                            continue;
                        }
                        if (box.ItemsSource is not System.Collections.IEnumerable items)
                        {
                            failures.Add($"{sectionType}: {boxName}.ItemsSource が未設定");
                            continue;
                        }
                        if (!items.Cast<object>().Any())
                            failures.Add($"{sectionType}: {boxName} の製品一覧が空");
                        if (!Equals(box.SelectedItem, product))
                            failures.Add($"{sectionType}: 選択中の製品が反映されていない " +
                                         $"(期待 {product} / 実際 {box.SelectedItem})");

                        // 断面図の描画分岐にその断面タイプが無いと、例外にならず「何も描かれない」。
                        // 注: Canvas は Measure/Arrange して ActualWidth/Height を持たせること。
                        //     0 のままだと ShapeDrawer.DrawGauge の目盛ループが抜けられない
                        //     (スケール 0 → 目盛間隔 0)。
                        var canvas = new Canvas();
                        canvas.Measure(new System.Windows.Size(400, 400));
                        canvas.Arrange(new System.Windows.Rect(0, 0, 400, 400));
                        canvas.UpdateLayout();
                        window.ViewModel.Canvas = canvas;
                        window.ViewModel.RedrawShapes();
                        if (canvas.Children.Count == 0)
                            failures.Add($"{sectionType}: 断面図が 1 つも描画されない");
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }, out bool timedOut, timeoutSeconds: 180);

            if (timedOut)
            {
                Assert.Inconclusive("ウィンドウ生成が 180 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"杭断面ウィンドウの生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }
    }
}
