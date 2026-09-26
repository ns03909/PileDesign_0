using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 画面の数値を、環境の地域設定に依らず同じ規則で読むこと (<see cref="NumericText"/>)。
    ///
    /// バインディングは WPF の既定 (en-US) で数値を変換し、数値の入力欄は「.」しか打てない。ところが検証ルールや
    /// セル編集の一部は地域設定のまま読んでいたので、小数点に「,」を使う地域 (ドイツなど) では「1.5」を 15 と読み、
    /// 欄ごとに受け付ける値が違った (セル編集では 15 がそのままモデルに入る)。
    /// </summary>
    [TestClass]
    public class NumericTextCultureTests
    {
        /// <summary>小数点に「,」、桁区切りに「.」を使う地域で <paramref name="action"/> を行う。</summary>
        private static void InCommaDecimalCulture(Action action)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [TestMethod]
        public void ADecimalPointIsReadTheSameInAnyCulture() => InCommaDecimalCulture(() =>
        {
            Assert.IsTrue(NumericText.TryParse("1.5", out double v));
            Assert.AreEqual(1.5, v, "地域設定で「1.5」を読み違えています");
            Assert.IsTrue(NumericText.TryParse(" -2.5E-3 ", out v));
            Assert.AreEqual(-2.5e-3, v);
            Assert.IsFalse(NumericText.TryParse("1,5", out double _), "入力欄では「,」を小数点・桁区切りとして受け付けません");
        });

        [TestMethod]
        public void PastedThousandsAreAcceptedOnlyInAValidGrouping() => InCommaDecimalCulture(() =>
        {
            Assert.IsTrue(NumericText.TryParseAllowingThousands("1,000", out double v));
            Assert.AreEqual(1000.0, v);
            Assert.IsTrue(NumericText.TryParseAllowingThousands("-12,345.5", out v));
            Assert.AreEqual(-12345.5, v);
            Assert.IsTrue(NumericText.TryParseAllowingThousands("1.5", out v));
            Assert.AreEqual(1.5, v, "この表から写した値を地域設定で読み違えています");
            Assert.IsFalse(NumericText.TryParseAllowingThousands("1,5", out v),
                "「1,5」(小数点に「,」を使う地域の 1.5) を 15 と読んでいます。拒むこと");
            Assert.IsFalse(NumericText.TryParseAllowingThousands("12,34", out int _));
        });

        [TestMethod]
        public void ValidationRulesAgreeWithTheInputBox() => InCommaDecimalCulture(() =>
        {
            var range = new RangeValidationRule { Min = 0, Max = 2 };
            Assert.IsTrue(range.Validate("1.5", CultureInfo.CurrentCulture).IsValid, "「1.5」(範囲内) を拒んでいます (15 と読んだ?)");
            Assert.IsFalse(range.Validate("15", CultureInfo.CurrentCulture).IsValid);

            var numeric = new NumericRangeValidationRule { Minimum = 0, Maximum = 2 };
            Assert.IsTrue(numeric.Validate("1.5", CultureInfo.CurrentCulture).IsValid, "「1.5」(範囲内) を拒んでいます (15 と読んだ?)");

            Assert.IsTrue(new FactorValidationRule().Validate("0.5", CultureInfo.CurrentCulture).IsValid, "「0.5」を拒んでいます (5 と読んだ?)");
        });

        /// <summary>画面の数値を地域設定のまま読む書き方が、本体に戻っていないこと。</summary>
        [TestMethod]
        public void NoInputIsParsedWithTheMachineCulture()
        {
            var offenders = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                scanned++;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;   // 説明の中の例
                    // 書式・カルチャを渡さない読み (地域設定で読む) と、地域設定を明示した数値の読み
                    if (Regex.IsMatch(lines[i], @"\b(double|float|decimal)\.TryParse\([^,()]*(\([^()]*\))?[^,()]*, out ")
                        || Regex.IsMatch(lines[i], @"\b(double|float|decimal|int)\.(Try)?Parse\(.*CultureInfo\.CurrentCulture"))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");
            Assert.AreEqual(0, offenders.Count,
                "画面の数値を地域設定のまま読んでいる箇所があります (NumericText を使うこと): " + string.Join(", ", offenders));
        }
    }
}
