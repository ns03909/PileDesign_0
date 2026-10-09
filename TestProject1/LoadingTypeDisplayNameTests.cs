using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;

namespace TestProject1
{
    /// <summary>
    /// 荷重タイプは画面では「杭十字」「杭矩形」と呼ぶ。保存ファイルとコードの中の値 (「個別十字」「個別矩形」) は変えない
    /// (変えると、これまでのファイルの荷重タイプが読めなくなる)。
    /// </summary>
    [TestClass]
    public class LoadingTypeDisplayNameTests
    {
        [DataTestMethod]
        [DataRow("個別十字", "杭十字")]
        [DataRow("個別十字（基礎梁反力）", "杭十字（基礎梁反力）")]
        [DataRow("個別矩形", "杭矩形")]
        [DataRow("個別矩形（基礎梁考慮）", "杭矩形（基礎梁考慮）")]
        [DataRow("任意矩形", "任意矩形")]
        [DataRow("なし", "なし")]
        public void TheDisplayName_RenamesOnlyThePileBasedTypes(string value, string shown)
        {
            Assert.AreEqual(shown, PileGroupSettlement.LoadingTypeDisplayName(value));
            Assert.AreEqual(shown, new PileDesign.Converters.LoadingTypeDisplayConverter().Convert(value, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
        }

        [TestMethod]
        public void TheStoredValues_AreUnchanged()
        {
            var options = new PileGroupSettlement().LoadingTypeOptions;
            CollectionAssert.Contains(options, "個別十字", "保存の値を変えると、これまでのファイルが読めなくなります");
            CollectionAssert.Contains(options, "個別矩形");

            // 荷重タイプの選択肢は、表示名に変える部品を通して出す
            foreach (var file in new[] { "GroupSettlementWindow.xaml", "MainWindow.xaml" })
            {
                string xaml = TestSource.Read("Graphics_r1", "Views", file);
                StringAssert.Contains(xaml, "<TextBlock Text=\"{Binding Converter={StaticResource LoadingTypeDisplayConverter}}\"",
                    $"{file}: 荷重タイプの選択肢が保存の値のまま出ます");
            }
        }
    }
}
