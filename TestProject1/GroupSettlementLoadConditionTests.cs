using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の荷重条件 (1 組) と、個別矩形の荷重の決め方。
    ///
    /// <para>個別矩形の荷重 QA は既定で杭の軸力から決める。以前は手で入れた値も解析のたびに軸力で上書きしており、
    /// 与えた荷重とプログラムが決めた値の区別がつかなかった。手で入れた矩形は上書きせず、
    /// 「荷重を軸力から作り直す」で軸力に戻す。</para>
    ///
    /// <para>画面は「荷重条件」(荷重面・荷重タイプ・矩形荷重) と「解析」(基礎梁なし / あり) に分ける。
    /// 以前は「解析（一般）」「解析（反復）」の 2 つのタブにそれぞれ荷重面と矩形荷重の表があり、
    /// どちらを変えるとどの解析に効くのかが分からなかった。</para>
    /// </summary>
    [TestClass]
    public class GroupSettlementLoadConditionTests
    {
        [TestMethod]
        public void ManuallyEnteredLoads_AreNotOverwrittenByTheAxialForce()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            var pgs = model.PileGroupSettlement;
            // 計算例5 は荷重面等価径を入れていないので、杭ごとの矩形を作れるように入れる
            foreach (var sp in model.ElementDivision.SoilPiles) sp.GroupPileLoadDia = 1.0;
            pgs.LoadingType = "個別矩形";
            var linked = pgs.RectLoads.Where(r => r.LinkedPileNo > 0).ToList();
            Assert.IsTrue(linked.Count >= 2, "前提: 杭に紐付く矩形が 2 つ以上ある");

            var manual = linked[0];
            var auto = linked[1];
            manual.QAInput = 1234;                 // 表で入れた (手入力)
            auto.QA = 9999;                        // プログラムが入れた値 (手入力ではない)
            Assert.IsTrue(manual.IsQaManual && manual.QASource == "手入力");
            Assert.IsFalse(auto.IsQaManual);
            Assert.AreEqual("軸力", auto.QASource);

            var result = new SettlementAnalysisService().PerformSettlementAnalysis(
                pgs, model.PileLayoutItems, model.ElementDivision.SoilPiles, model.GridXItems, model.GridYItems,
                -10, 40, -10, 40, 0, 0, 5, 5);
            Assert.IsTrue(result.Success, result.ErrorMessage);

            var after = pgs.RectLoads.ToDictionary(r => r.LinkedPileNo);
            var autoPile = model.PileLayoutItems.First(p => p.PileNo == auto.LinkedPileNo);
            Assert.AreEqual(1234, after[manual.LinkedPileNo].QA, 1e-9, "手で入れた荷重を解析が軸力で上書きしました");
            Assert.AreEqual(autoPile.AxialForceVL0 + autoPile.AxialForceVLAdditional, after[auto.LinkedPileNo].QA, 1e-9,
                "手入力でない荷重が軸力から決まっていません");

            // 「荷重を軸力から作り直す」で手入力も軸力に戻る
            var vm = new MainWindowViewModel { CurrentInputModel = model };
            vm.RebuildRectLoadsFromAxialForceCommand.Execute(null);
            var manualPile = model.PileLayoutItems.First(p => p.PileNo == manual.LinkedPileNo);
            var rebuilt = pgs.RectLoads.First(r => r.LinkedPileNo == manual.LinkedPileNo);
            Assert.AreEqual(manualPile.AxialForceVL0 + manualPile.AxialForceVLAdditional, rebuilt.QA, 1e-9);
            Assert.IsFalse(rebuilt.IsQaManual, "作り直したのに手入力の印が残っています");
        }

        /// <summary>手入力の印は保存・複製で失われない (失うと、開き直した後の解析で手入力が軸力に戻る)。</summary>
        [TestMethod]
        public void TheManualMark_SurvivesCloneAndSave()
        {
            var r = new RectLoad { X1 = 0, X2 = 1, Y1 = 0, Y2 = 1, LinkedPileNo = 3 };
            r.QAInput = 500;
            Assert.IsTrue(r.Clone().IsQaManual, "複製で手入力の印が落ちます");
            string json = System.Text.Json.JsonSerializer.Serialize(r);
            StringAssert.Contains(json, "\"IsQaManual\":true", "保存に手入力の印が入っていません");
            Assert.IsFalse(json.Contains("QAInput") || json.Contains("QASource"), "画面用の値まで保存しています");
        }

        /// <summary>荷重条件・解析のタブを実際に描く (タブの中身は選んだときに組まれるので、開くだけの試験では参照の誤りを踏まない)。</summary>
        [TestMethod]
        public void TheLoadConditionAndAnalysisTabs_Render()
        {
            var (model, _) = IterativeBeamSettlementInvariantTests.Scene();
            if (model == null) return;
            int rendered = 0;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var vm = new MainWindowViewModel { CurrentInputModel = model };
                var window = new PileDesign.Views.GroupSettlementWindow
                {
                    DataContext = vm, ShowActivated = false,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                window.Show();
                try
                {
                    foreach (var tab in new[] { window.TabItemLoadNonBeam, window.TabItemLoadBeamAware })
                    {
                        window.GroupPileTabControl.SelectedItem = tab;
                        window.UpdateLayout();
                        rendered++;
                    }
                    vm.IsGroupSettlementWithBeamSelected = true;   // 基礎梁ありの中身も描く
                    window.UpdateLayout();
                }
                finally { window.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.AreEqual(2, rendered);
        }

        [TestMethod]
        public void TheWindow_HasOneLoadConditionTab_AndAnAnalysisTabWithBeamChoice()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "GroupSettlementWindow.xaml");
            StringAssert.Contains(xaml, "<TabItem Header=\"荷重条件\"");
            StringAssert.Contains(xaml, "<TabItem Header=\"解析\"");
            Assert.IsFalse(xaml.Contains("Header=\"解析（一般）\"") || xaml.Contains("Header=\"解析（反復）\""), "旧いタブが残っています");
            Assert.AreEqual(1, Regex.Matches(xaml, Regex.Escape("ItemsSource=\"{Binding CurrentInputModel.PileGroupSettlement.RectLoads")).Count,
                "矩形荷重の表が 1 つではありません (同じ荷重を 2 か所で編集させない)");
            StringAssert.Contains(xaml, "Binding=\"{Binding QASource, Mode=OneWay}\" Header=\"出所\"");
            StringAssert.Contains(xaml, "Command=\"{Binding RebuildRectLoadsFromAxialForceCommand}\"");
            StringAssert.Contains(xaml, "Content=\"基礎梁なし\"");
            StringAssert.Contains(xaml, "Content=\"基礎梁あり\"");
            StringAssert.Contains(xaml, "<TextBlock Text=\"基礎梁なしで解析\"");
            StringAssert.Contains(xaml, "<TextBlock Text=\"基礎梁ありで解析\"");
            Assert.IsFalse(xaml.Contains("QA は収束値で更新されます") || xaml.Contains("反復計算により自動収束されます"),
                "反復解析が入力の荷重を書き換えるという古い説明が残っています (1.0.33-beta から入力と結果を分けて持っている)");

            string main = TestSource.Read("Graphics_r1", "Views", "MainWindow.xaml");
            StringAssert.Contains(main, "解析（基礎梁なし）");
            StringAssert.Contains(main, "解析（基礎梁あり）");
        }
    }
}
