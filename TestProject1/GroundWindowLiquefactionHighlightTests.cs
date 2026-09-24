using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 地盤ウィンドウの液状化安全率 FL の列で、FL &lt; 1 の層を太字にする条件が、そのセルに出している値を見ていること。
    ///
    /// 以前は条件のバインディングが <c>FL</c> (レベル1・2 の値を持つ配列そのもの) になっていた。
    /// 変換器 (<c>DoubleLessThanConverter</c>) は値を文字にしてから数値に読むので、配列は常に読めずに
    /// false が返り、<b>太字は一度も付いていなかった</b>。例外にも警告にもならない。
    /// </summary>
    [TestClass]
    public class GroundWindowLiquefactionHighlightTests
    {
        [TestMethod]
        public void HighlightTriggerWatchesTheValueTheCellShows()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "GroundWindow.xaml");
            var templates = Regex.Matches(xaml, @"<DataTemplate>.*?</DataTemplate>", RegexOptions.Singleline)
                .Select(m => m.Value)
                .Where(t => t.Contains("DoubleLessThanConverter"))
                .ToList();

            TestSource.AssertScanned(templates.Count, 2, "FL を強調するセルのテンプレート (レベル1・2)");
            foreach (var t in templates)
            {
                string shown = Regex.Match(t, @"<TextBlock Text=""\{Binding ([^,}]+)").Groups[1].Value;
                string watched = Regex.Match(t, @"<DataTrigger Binding=""\{Binding ([^,}]+)").Groups[1].Value;
                Assert.IsTrue(shown.StartsWith("FL["), $"FL の列のセルが表示している値が想定と違います: {shown}");
                Assert.AreEqual(shown, watched,
                    "太字の条件が、セルに出している値と別のものを見ています。"
                    + "配列そのものを渡すと変換器が数値に読めず、太字が付きません");
            }
        }
    }
}
