using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ひずみ度・応力度分布のキャッシュの鍵が、M-φ キャッシュの鍵と同じだけ
    /// 断面の諸元を並べていること。
    ///
    /// どちらも<b>同じ断面</b>を指す鍵です。片方にしか入っていない諸元を変えると、
    /// 入っていない側は鍵が変わらないので、<b>前の諸元で計算した結果をそのまま
    /// 返します。</b>例外にならず、グラフはもっともらしく描かれます。
    ///
    /// 実際に 3 件抜けていました。コンクリート単位体積重量 (→ Ec) と、
    /// テンドンの降伏強度・引張強度です。既製杭のテンドン規格を変えても、
    /// N-M 曲線をクリックしたときの分布図が前の規格のままでした。
    ///
    /// 同じ系統の不具合が <c>CaptainPile</c> にもありました
    /// (鍵がパイルキャップの Fc を含まないのにキャッシュを捨てていなかった)。
    /// </summary>
    [TestClass]
    public class StrainStressProfileSignatureTests
    {
        [TestMethod]
        public void TheProfileKey_CoversWhateverTheMPhiKeyCovers()
        {
            var mphi = PropertiesOfMPhiKey();
            var profile = PropertiesOfProfileKey();

            Assert.IsTrue(mphi.Count >= 15,
                $"M-φ の鍵から諸元を読み出せていません ({mphi.Count} 個)。"
                + "GetMPhiCacheKey の書き方が変わった可能性があります");
            Assert.IsTrue(profile.Count >= 15,
                $"プロファイルの鍵から諸元を読み出せていません ({profile.Count} 個)");

            var missing = mphi.Except(profile).OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.AreEqual(0, missing.Count,
                "M-φ の鍵にあって、ひずみ度・応力度分布の鍵に無い諸元があります。"
                + "この諸元を変えても分布図の鍵が変わらないので、前の諸元で計算した"
                + "分布がそのまま出ます。StrainStressProfileService の sig に足してください:"
                + Environment.NewLine + "  " + string.Join(", ", missing));
        }

        /// <summary><c>PileSection.GetMPhiCacheKey</c> が並べている諸元名。</summary>
        private static System.Collections.Generic.HashSet<string> PropertiesOfMPhiKey()
        {
            string src = TestSource.Read("Graphics_r1", "Models", "InputData", "PileSection.cs");
            string body = TestSource.MethodBody(src, "string GetMPhiCacheKey(double");

            // 文字列補間の {Xxx} を拾う。軸力とオプション署名は断面の諸元ではない
            var names = Regex.Matches(body, @"\{(\w+)\}")
                .Select(m => m.Groups[1].Value)
                .Where(n => char.IsUpper(n[0]))
                .ToHashSet(StringComparer.Ordinal);
            names.Remove("N");
            names.Remove("PileBodyType");     // switch の対象なので鍵の一部ではあるが
            names.Remove("PileSectionType");  // どちらの鍵にも入っている
            return names;
        }

        /// <summary><c>StrainStressProfileService</c> の sig が並べている諸元名。</summary>
        private static System.Collections.Generic.HashSet<string> PropertiesOfProfileKey()
        {
            var file = Directory
                .EnumerateFiles(Path.Combine(TestSource.Root(), "Graphics_r1"),
                                "StrainStressProfileService.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            Assert.IsNotNull(file, "StrainStressProfileService.cs が見つかりません");

            string src = File.ReadAllText(file!);
            int at = src.IndexOf("string sig = string.Join", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "sig の組み立てが見つかりません");
            string body = src[at..(src.IndexOf(");", at, StringComparison.Ordinal))];

            return Regex.Matches(body, @"pileSection\.(\w+)")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }
    }
}
