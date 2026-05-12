using DocumentFormat.OpenXml.Wordprocessing;
using PileDesign.Output;
using System.Linq;
using DocxMath = DocumentFormat.OpenXml.Math;

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

    /// <summary>
    /// WordDocument.Math.cs の OpenXML Math 構築ヘルパーのテスト。
    /// 純粋関数 (テキスト→OpenXML 構造) のため副作用なし。
    /// </summary>
    [TestClass]
    public class WordDocumentMathHelperTests
    {
        private static string TextOf(DocxMath.Run run)
            => string.Concat(run.Descendants<DocxMath.Text>().Select(t => t.Text));

        [TestMethod]
        public void GetRun_BasicText_HasMathTextChild()
        {
            var r = WordDocument.GetRun("xyz");
            Assert.IsNotNull(r);
            Assert.AreEqual("xyz", TextOf(r));
        }

        [TestMethod]
        public void GetRun_EmptyText_DoesNotThrow()
        {
            var r = WordDocument.GetRun("");
            Assert.IsNotNull(r);
            Assert.AreEqual("", TextOf(r));
        }

        [TestMethod]
        public void GetSuperscript_WrapsBaseAndSuper()
        {
            var b = WordDocument.GetRun("x");
            var s = WordDocument.GetRun("2");
            var sup = WordDocument.GetSuperscript(b, s);
            Assert.IsNotNull(sup);
            // Superscript 要素が子に含まれる
            var supEl = sup.Descendants<DocxMath.Superscript>().FirstOrDefault();
            Assert.IsNotNull(supEl, "Superscript 要素が含まれるはず");
            // Base + SuperArgument の両方を保持
            Assert.IsNotNull(supEl.GetFirstChild<DocxMath.Base>());
            Assert.IsNotNull(supEl.GetFirstChild<DocxMath.SuperArgument>());
        }

        [TestMethod]
        public void GetSubscript_WrapsBaseAndSub()
        {
            var b = WordDocument.GetRun("x");
            var s = WordDocument.GetRun("i");
            var sub = WordDocument.GetSubscript(b, s);
            var subEl = sub.Descendants<DocxMath.Subscript>().FirstOrDefault();
            Assert.IsNotNull(subEl);
            Assert.IsNotNull(subEl.GetFirstChild<DocxMath.Base>());
            Assert.IsNotNull(subEl.GetFirstChild<DocxMath.SubArgument>());
        }

        [TestMethod]
        public void GetSubSuperscript_WrapsBaseAndBoth()
        {
            var b = WordDocument.GetRun("x");
            var sub = WordDocument.GetRun("i");
            var sup = WordDocument.GetRun("2");
            var ss = WordDocument.GetSubSuperscript(b, sub, sup);
            var ssEl = ss.Descendants<DocxMath.SubSuperscript>().FirstOrDefault();
            Assert.IsNotNull(ssEl);
            Assert.IsNotNull(ssEl.GetFirstChild<DocxMath.Base>());
            Assert.IsNotNull(ssEl.GetFirstChild<DocxMath.SubArgument>());
            Assert.IsNotNull(ssEl.GetFirstChild<DocxMath.SuperArgument>());
        }

        [TestMethod]
        public void GetFraction_RunOverload_HasNumeratorAndDenominator()
        {
            var n = WordDocument.GetRun("a");
            var d = WordDocument.GetRun("b");
            var frac = WordDocument.GetFraction(n, d);
            var fracEl = frac.Descendants<DocxMath.Fraction>().FirstOrDefault();
            Assert.IsNotNull(fracEl);
            Assert.IsNotNull(fracEl.GetFirstChild<DocxMath.FractionProperties>());
            Assert.IsNotNull(fracEl.GetFirstChild<DocxMath.Numerator>());
            Assert.IsNotNull(fracEl.GetFirstChild<DocxMath.Denominator>());
        }

        [TestMethod]
        public void GetFraction_OfficeMathOverload_HasNumeratorAndDenominator()
        {
            var n = new DocxMath.OfficeMath(WordDocument.GetRun("a"));
            var d = new DocxMath.OfficeMath(WordDocument.GetRun("b"));
            var frac = WordDocument.GetFraction(n, d);
            Assert.IsInstanceOfType(frac, typeof(DocxMath.OfficeMath));
            var fracEl = frac.Descendants<DocxMath.Fraction>().FirstOrDefault();
            Assert.IsNotNull(fracEl);
        }

        [TestMethod]
        public void GetRadicalRun_ProducesRadicalElement()
        {
            var b = WordDocument.GetRun("2");
            var rad = WordDocument.GetRadicalRun(b);
            var radEl = rad.Descendants<DocxMath.Radical>().FirstOrDefault();
            Assert.IsNotNull(radEl, "Radical 要素 (平方根) を持つ");
            Assert.IsNotNull(radEl.GetFirstChild<DocxMath.Base>());
        }

        [TestMethod]
        public void GetTopBarredRun_ProducesAccentWithMacron()
        {
            var b = WordDocument.GetRun("x");
            var bar = WordDocument.GetTopBarredRun(b);
            // Accent 要素を持つ (上バー)
            var accents = bar.Descendants<DocxMath.Accent>().ToList();
            Assert.IsTrue(accents.Count > 0, "Accent 要素が含まれる");
            // AccentChar が "¯" (macron, U+00AF)
            var accentChars = bar.Descendants<DocxMath.AccentChar>().ToList();
            Assert.IsTrue(accentChars.Count > 0);
            Assert.AreEqual("¯", accentChars[0].Val?.Value);
        }

        [TestMethod]
        public void GetDoubleSubscript_NestsSubscripts()
        {
            var b = WordDocument.GetRun("R");
            var left = WordDocument.GetRun("u");
            var right = WordDocument.GetRun("max");
            var ds = WordDocument.GetDoubleSubscript(b, left, right);
            // ネストされた Subscript が 2 つ存在
            var subs = ds.Descendants<DocxMath.Subscript>().ToList();
            Assert.AreEqual(2, subs.Count, "外側と内側の 2 つの Subscript がある");
        }
    }

    /// <summary>
    /// WordDocumentUtils の小規模 static ヘルパーのテスト。
    /// </summary>
    [TestClass]
    public class WordDocumentUtilsTests
    {
        [TestMethod]
        public void GetJustification_LeftMapping()
        {
            var v = WordDocumentUtils.GetJustification(WordDocumentUtils.DiagramAlignment.Left);
            Assert.AreEqual(JustificationValues.Left, v);
        }

        [TestMethod]
        public void GetJustification_CenterMapping()
        {
            var v = WordDocumentUtils.GetJustification(WordDocumentUtils.DiagramAlignment.Center);
            Assert.AreEqual(JustificationValues.Center, v);
        }

        [TestMethod]
        public void GetJustification_RightMapping()
        {
            var v = WordDocumentUtils.GetJustification(WordDocumentUtils.DiagramAlignment.Right);
            Assert.AreEqual(JustificationValues.Right, v);
        }
    }
}
