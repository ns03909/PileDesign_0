using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 表への貼り付けで、有限でない数値 (NaN・±Infinity・∞) を受け付けないこと。
    ///
    /// <c>double.TryParse</c> はこれらを数値として受け付ける。以前は型の変換が通るかだけを見ていたので、
    /// 貼り付けがそのまま通り、一般節点の座標のように有限値を検査しないプロパティに NaN が入った。
    /// 貼り付けは全セルを先に検証してから反映するので、変換で拒否すれば何も書き込まれない。
    /// セルへの直接入力は数値用の入力制限 (NumericInput) で文字が弾かれるため、入口はここだけ。
    /// </summary>
    [TestClass]
    public class PasteNonFiniteTests
    {
        private static readonly Type Grid = typeof(EnhancedDataGrid);

        private static object TextKind()
        {
            var kind = Grid.GetNestedType("ColumnKind", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("ColumnKind が見つかりません");
            return Enum.Parse(kind, "Text");
        }

        private static bool CanConvert(string text, Type target)
            => (bool)(Grid.GetMethod("CanConvert", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("CanConvert が見つかりません"))
                .Invoke(null, [text, target, TextKind()])!;

        private static string Describe(string text, Type target)
            => (string)(Grid.GetMethod("DescribeConversionFailure", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("DescribeConversionFailure が見つかりません"))
                .Invoke(null, [text, target, TextKind()])!;

        [DataTestMethod]
        [DataRow("NaN")]
        [DataRow("nan")]
        [DataRow("Infinity")]
        [DataRow("-Infinity")]
        [DataRow("∞")]
        [DataRow("-∞")]
        [DataRow("1e400")]   // double の範囲を超えて ∞ に丸められる
        public void NonFiniteIsRejected(string text)
        {
            Assert.IsFalse(CanConvert(text, typeof(double)), $"'{text}' が double の列に貼り付けられます");
            Assert.IsFalse(CanConvert(text, typeof(double?)), $"'{text}' が double? の列に貼り付けられます");
            Assert.IsFalse(CanConvert(text, typeof(float)), $"'{text}' が float の列に貼り付けられます");
            StringAssert.Contains(Describe(text, typeof(double)), "有限の数値",
                "拒否の理由が、型の話ではなく「数値として扱えない」ことになっていません");
        }

        [DataTestMethod]
        [DataRow("1.5")]
        [DataRow("-0.25")]
        [DataRow("1,234.5")]
        [DataRow("1.0E-5")]
        [DataRow("0")]
        public void FiniteNumbersStillPaste(string text)
        {
            Assert.IsTrue(CanConvert(text, typeof(double)), $"'{text}' が貼り付けられなくなりました");
            Assert.IsTrue(CanConvert(text, typeof(float)), $"'{text}' が float の列に貼り付けられなくなりました");
        }

        [TestMethod]
        public void EmptyOnNullableStillMeansNull()
            => Assert.IsTrue(CanConvert("", typeof(double?)), "空欄を null として貼る動作が変わりました");
    }
}
