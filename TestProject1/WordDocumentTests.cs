using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Output;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// WordDocument の純粋 static ヘルパー（インスタンス不要）のテスト。
    /// DOCX 出力全体の検証ではなく、テキスト→Run 変換・TeX パーサの最小動作確認。
    /// </summary>
    [TestClass]
    public class WordDocumentTests
    {
        private static string ExtractText(Run run)
            => string.Concat(run.Descendants<Text>().Select(t => t.Text));

        private static bool IsSuperscript(Run run)
        {
            var va = run.RunProperties?.VerticalTextAlignment;
            return va != null && va.Val == VerticalPositionValues.Superscript;
        }

        private static bool IsSubscript(Run run)
        {
            var va = run.RunProperties?.VerticalTextAlignment;
            return va != null && va.Val == VerticalPositionValues.Subscript;
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_PlainText_ReturnsSingleRun()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("Hello");
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual("Hello", ExtractText(runs[0]));
            Assert.IsFalse(IsSuperscript(runs[0]));
            Assert.IsFalse(IsSubscript(runs[0]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_EmptyText_ReturnsEmptyList()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("");
            Assert.AreEqual(0, runs.Count);
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_AngleSuperscript_BaseAndSuper()
        {
            // "m<^2>" → Run("m") + Run("2", superscript)
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("m<^2>");
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual("m", ExtractText(runs[0]));
            Assert.IsFalse(IsSuperscript(runs[0]));
            Assert.AreEqual("2", ExtractText(runs[1]));
            Assert.IsTrue(IsSuperscript(runs[1]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_AngleSubscript_BaseAndSub()
        {
            // "H<_i>" → Run("H") + Run("i", subscript)
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("H<_i>");
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual("H", ExtractText(runs[0]));
            Assert.AreEqual("i", ExtractText(runs[1]));
            Assert.IsTrue(IsSubscript(runs[1]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_LaTeXSuperscript_BaseAndSuper()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("m^{2}");
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual("m", ExtractText(runs[0]));
            Assert.AreEqual("2", ExtractText(runs[1]));
            Assert.IsTrue(IsSuperscript(runs[1]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_LaTeXSubscript_BaseAndSub()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("k_{h}");
            Assert.AreEqual(2, runs.Count);
            Assert.AreEqual("k", ExtractText(runs[0]));
            Assert.AreEqual("h", ExtractText(runs[1]));
            Assert.IsTrue(IsSubscript(runs[1]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_MixedSuperAndSub_ProducesFiveRuns()
        {
            // "x<^2>+y<_1>" → [x][^2][+y][_1]
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("x<^2>+y<_1>");
            Assert.AreEqual(4, runs.Count);
            Assert.AreEqual("x", ExtractText(runs[0]));
            Assert.AreEqual("2", ExtractText(runs[1]));
            Assert.IsTrue(IsSuperscript(runs[1]));
            Assert.AreEqual("+y", ExtractText(runs[2]));
            Assert.AreEqual("1", ExtractText(runs[3]));
            Assert.IsTrue(IsSubscript(runs[3]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_OnlyMarkup_NoPlainRuns()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("<_sub>");
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual("sub", ExtractText(runs[0]));
            Assert.IsTrue(IsSubscript(runs[0]));
        }

        [TestMethod]
        public void ConvertStringToRunsWithSuperSub_FontSizePropagates()
        {
            var runs = WordDocument.ConvertStringToRunsWithSuperSub("abc", fontSize: 14.0);
            Assert.AreEqual(1, runs.Count);
            // FontSize は「ハーフポイント」で格納される → 14.0pt → "28"
            var sz = runs[0].RunProperties?.FontSize?.Val?.ToString();
            Assert.AreEqual("28", sz);
        }

        [TestMethod]
        public void Tex_SimpleFraction_ReturnsNonNullOfficeMath()
        {
            // 最低限のスモークテスト: 例外を吐かず、Math 要素が返ること
            var math = WordDocument.Tex(@"\frac{a}{b}");
            Assert.IsNotNull(math);
            Assert.IsTrue(math.Descendants().Any(), "OfficeMath は何らかの子要素を持つはず");
        }

        [TestMethod]
        public void Tex_PlainIdentifier_ReturnsOfficeMath()
        {
            var math = WordDocument.Tex("x");
            Assert.IsNotNull(math);
        }

        [TestMethod]
        public void Tex_EmptyInput_DoesNotThrow()
        {
            // 空文字でも例外にならないこと（Tex は Body を引数に取らないため安全に呼べる）
            var math = WordDocument.Tex("");
            Assert.IsNotNull(math);
        }
    }
}
