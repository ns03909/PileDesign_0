using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの図の代替テキストに、意味の無い仮の文字 (「___」など) を置かないこと (外部レビュー 2026-11-20)。
    ///
    /// <para>画像を表示できないときや読み上げでは、代替テキストだけが図の手がかりになる。
    /// 2026-11-20 まで 39 か所が alt="___" だった。装飾のアイコンは空 (alt="") でよい。</para>
    /// </summary>
    [TestClass]
    public class HelpImageAltTextTests
    {
        [TestMethod]
        public void FiguresHaveMeaningfulAltText()
        {
            string html = File.ReadAllText(Path.Combine(TestSource.Root(), "Graphics_r1", "Help", "help.html"));
            var figures = Regex.Matches(html, "<img[^>]*src=\"images/[^\"]*\"[^>]*>").Select(m => m.Value).ToList();
            TestSource.AssertScanned(figures.Count, 30, "ヘルプの図 (images/)");

            var bad = figures
                .Select(tag => (tag, alt: Regex.Match(tag, "alt=\"([^\"]*)\"")))
                .Where(x => !x.alt.Success || Regex.IsMatch(x.alt.Groups[1].Value, @"^[\s_\-.]*$"))
                .Select(x => x.tag)
                .ToList();
            Assert.AreEqual(0, bad.Count, "代替テキストが無いか仮の文字の図: " + string.Join(" / ", bad.Take(10)));
        }
    }
}
