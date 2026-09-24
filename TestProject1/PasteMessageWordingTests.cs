using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 表への貼り付けのエラーの文言に、内部の名前が出ないこと。
    ///
    /// 以前は「値 'abc' は列の型(Double)に変換できません」「対象列へのバインディング情報を取得できません」
    /// 「バインディングのパスに該当するプロパティ/インデクサが見つかりません」のように、
    /// 型名や仕組みの名前がそのまま出ていた。読んでも次に何をすればよいかが分からない。
    /// これらの文言は <c>MessageService.Show</c> と別の行で組み立てるので、
    /// <see cref="UserFacingMessageTests"/> の見張りを素通りしていた。
    /// </summary>
    [TestClass]
    public class PasteMessageWordingTests
    {
        private static string Describe(string text, Type target)
        {
            var kind = typeof(EnhancedDataGrid).GetNestedType("ColumnKind", BindingFlags.NonPublic)!;
            return (string)typeof(EnhancedDataGrid)
                .GetMethod("DescribeConversionFailure", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [text, target, Enum.Parse(kind, "Text")])!;
        }

        [DataTestMethod]
        [DataRow("abc", typeof(double), "数値")]
        [DataRow("abc", typeof(double?), "数値")]
        [DataRow("1.5", typeof(int), "整数")]
        [DataRow("maybe", typeof(bool), "「はい」「いいえ」")]
        public void ConversionFailureSpeaksInTheUsersTerms(string text, Type target, string expected)
        {
            string message = Describe(text, target);
            StringAssert.Contains(message, expected, $"値の種類が利用者の言葉になっていません: {message}");
            foreach (var internalName in new[] { "Double", "Int32", "Boolean", "型(" })
                Assert.IsFalse(message.Contains(internalName, StringComparison.Ordinal),
                    $"内部の型名「{internalName}」が出ています: {message}");
        }

        [TestMethod]
        public void EmptyIntoARequiredNumberSaysItCannotBeEmpty()
            => StringAssert.Contains(Describe("", typeof(double)), "空欄にできません");

        /// <summary>貼り付け処理の文字列に、仕組みの名前が戻ってこないこと。</summary>
        [TestMethod]
        public void PasteSourceHasNoInternalTermsInMessages()
        {
            string source = TestSource.Read("Graphics_r1", "Common", "EnhancedDataGrid.cs");
            // コメントを落としてから、文字列リテラルだけを見る
            string code = Regex.Replace(source, @"//[^\n]*", "");
            var literals = Regex.Matches(code, @"\$?""(?:[^""\\\n]|\\.)*""")
                .Select(m => m.Value)
                .Where(l => l.Any(ch => ch > 0x3000))   // 日本語を含むもの (画面に出す文)
                .ToList();

            TestSource.AssertScanned(literals.Count, 10, "貼り付け処理の画面に出す文");
            string[] forbidden = ["バインディング", "インデクサ", "プロパティ", "型へ", "型(", "Nullable", "ペースト"];
            var hits = literals.Where(l => forbidden.Any(f => l.Contains(f, StringComparison.Ordinal))).ToList();
            Assert.AreEqual(0, hits.Count,
                "貼り付けの文言に内部の用語が入っています:\n  " + string.Join("\n  ", hits));
        }
    }
}
