using PileDesign.Converters;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// BoolEqualsConverter (RadioButton ペア用) の規約検証。
    /// 「アンチェック時は Binding.DoNothing」が破られると、正負 2 ライターの
    /// ラジオペアがウィンドウ再表示時に無限ループ → StackOverflow を起こす
    /// (2026-08-11 docx 出力設定ウィンドウで実発生) ため、回帰ガードする。
    /// </summary>
    [TestClass]
    public class BoolEqualsConverterTests
    {
        private static readonly BoolEqualsConverter C = new();
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        [TestMethod]
        public void Convert_MatchesParameter()
        {
            Assert.AreEqual(true, C.Convert(true, typeof(bool?), "True", Ci));
            Assert.AreEqual(false, C.Convert(true, typeof(bool?), "False", Ci));
            Assert.AreEqual(true, C.Convert(false, typeof(bool?), "False", Ci));
            Assert.AreEqual(false, C.Convert(false, typeof(bool?), "True", Ci));
        }

        [TestMethod]
        public void ConvertBack_Checked_WritesParameter()
        {
            Assert.AreEqual(true, C.ConvertBack(true, typeof(bool), "True", Ci));
            Assert.AreEqual(false, C.ConvertBack(true, typeof(bool), "False", Ci));
        }

        [TestMethod]
        public void ConvertBack_Unchecked_DoesNothing()
        {
            // アンチェック (false / null) では絶対に書き戻さないこと — ループ防止の要
            Assert.AreEqual(Binding.DoNothing, C.ConvertBack(false, typeof(bool), "True", Ci));
            Assert.AreEqual(Binding.DoNothing, C.ConvertBack(false, typeof(bool), "False", Ci));
            Assert.AreEqual(Binding.DoNothing, C.ConvertBack(null, typeof(bool), "True", Ci));
        }

        [TestMethod]
        public void InvalidParameter_IsInert()
        {
            Assert.AreEqual(DependencyProperty.UnsetValue, C.Convert(true, typeof(bool?), null, Ci));
            Assert.AreEqual(Binding.DoNothing, C.ConvertBack(true, typeof(bool), "abc", Ci));
        }
    }
}
