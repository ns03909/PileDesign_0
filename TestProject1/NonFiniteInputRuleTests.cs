using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.ViewModels;
using System.Globalization;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 入力欄の範囲の検証が NaN・無限大を通さないこと。
    ///
    /// 範囲の検証は「Min より小さい、または Max より大きいなら拒む」と書いていたので、NaN は
    /// どちらの比較も偽になり、有効な入力として通った (「NaN」と打てば値が NaN になる)。
    /// 同じルールを多くの入力欄で使っている。
    /// </summary>
    [TestClass]
    public class NonFiniteInputRuleTests
    {
        private static CultureInfo _previous = CultureInfo.CurrentCulture;

        [TestInitialize]
        public void Setup()
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");   // 実行環境の地域設定に依らないように
        }

        [TestCleanup]
        public void Cleanup() => CultureInfo.CurrentCulture = _previous;

        [DataTestMethod]
        [DataRow("NaN")]
        [DataRow("Infinity")]
        [DataRow("-Infinity")]
        [DataRow("∞")]
        [DataRow("-∞")]
        public void TheRangeRuleRejectsNonFiniteValues(string text)
        {
            // 日本の地域設定では「NaN」「∞」「-∞」が数値として読める (「Infinity」は読めず、元から無効な数値として拒まれる)
            bool readsAsNonFinite = double.TryParse(text, out double parsed) && !double.IsFinite(parsed);

            var rule = new RangeValidationRule { Min = -1e9, Max = 1e9 };
            var result = rule.Validate(text, CultureInfo.CurrentCulture);
            Assert.IsFalse(result.IsValid, $"「{text}」を有効な入力として通しています");
            if (readsAsNonFinite)
                StringAssert.Contains(result.ErrorContent as string, "NaN", "数値として読める NaN・無限大に、理由を示していません");

            var old = new NumericRangeValidationRule { Minimum = -1e9, Maximum = 1e9 };
            Assert.IsFalse(old.Validate(text, CultureInfo.CurrentCulture).IsValid,
                $"もう一方の範囲の検証ルールが「{text}」を通しています");

            Assert.IsFalse(new FactorValidationRule().Validate(text, CultureInfo.CurrentCulture).IsValid,
                $"0〜1 の係数の検証が「{text}」を通しています");
        }

        /// <summary>前提: 日本の地域設定で NaN・無限大として読める文字があること (無いと上の検査が空振りになる)。</summary>
        [TestMethod]
        public void NaNAndInfinityReadAsNumbersInTheJapaneseCulture()
        {
            foreach (var text in new[] { "NaN", "∞", "-∞" })
                Assert.IsTrue(double.TryParse(text, out double v) && !double.IsFinite(v), $"「{text}」が NaN・無限大として読めません");
        }

        [TestMethod]
        public void TheRangeRuleStillAcceptsAndRejectsOrdinaryValues()
        {
            var rule = new RangeValidationRule { Min = 0, Max = 100 };
            Assert.IsTrue(rule.Validate("50", CultureInfo.CurrentCulture).IsValid);
            Assert.IsTrue(rule.Validate("0", CultureInfo.CurrentCulture).IsValid);
            Assert.IsTrue(rule.Validate("100", CultureInfo.CurrentCulture).IsValid);
            Assert.IsFalse(rule.Validate("150", CultureInfo.CurrentCulture).IsValid);
            Assert.IsFalse(rule.Validate("abc", CultureInfo.CurrentCulture).IsValid);
            Assert.IsFalse(rule.Validate(null!, CultureInfo.CurrentCulture).IsValid, "空の値で例外にせず、無効として返すこと");
        }

        /// <summary>画面に出る検証の文言が日本語であること (以前は 0〜1 の係数・文字数の検証が英語だった)。</summary>
        [TestMethod]
        public void TheMessagesAreInJapanese()
        {
            var factor = new FactorValidationRule().Validate("2", CultureInfo.CurrentCulture);
            StringAssert.Contains(factor.ErrorContent as string, "以下の数値を入力してください");

            var length = new StringMaxLengthValidationRule { MaxLength = 3 }.Validate("abcd", CultureInfo.CurrentCulture);
            StringAssert.Contains(length.ErrorContent as string, "文字以内");
        }

        /// <summary>
        /// ↑↓キーの増減が、NaN・無限大の入力を数値として扱わないこと
        /// (NaN に足しても NaN のままで、範囲の検証ルールの無い欄ではそのまま値として書き込まれる)。
        /// </summary>
        [TestMethod]
        public void TheUpDownKeysDoNotTreatNonFiniteTextAsANumber()
        {
            var error = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                foreach (var text in new[] { "NaN", "Infinity", "-Infinity" })
                {
                    var box = new TextBox { Text = text };
                    Assert.IsFalse(NumericUpDownBehavior.TryParseCurrentValue(box, out _),
                        $"↑↓キーの増減が「{text}」を数値として扱っています");
                }
                Assert.IsTrue(NumericUpDownBehavior.TryParseCurrentValue(new TextBox { Text = "1.5" }, out double v) && v == 1.5);
            }, out bool timedOut);
            Assert.IsFalse(timedOut);
            if (error != null) throw error;
        }
    }
}
