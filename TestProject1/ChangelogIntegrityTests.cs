using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// CHANGELOG.md が消えていないこと。
    ///
    /// <b>2 回、空にした。</b> どちらも同じ形で、Python の 1 行:
    ///
    /// <code>open(p, 'wb').write(open(p, 'rb').read().replace(...))</code>
    ///
    /// は呼び出す側の <c>open(p, 'wb')</c> を先に評価する。読む前にファイルが
    /// 切り詰められ、空の内容を書き戻す。2 文に分ければ起きない。
    ///
    /// そのうえ <c>git commit</c> は通る。CHANGELOG は他のどのテストも見ないので、
    /// 気づけるのは commit の「N deletions」を目で見たときだけだった。
    /// ここで下限を置く。書き足すときは <c>tools/add-changelog.py</c> を使う
    /// (控えを取り、短くなる書き換えを断る)。
    /// </summary>
    [TestClass]
    public class ChangelogIntegrityTests
    {
        /// <summary>
        /// 行数の下限。<b>増やすのはよい。減らすときは理由をコミットに書くこと。</b>
        /// 版を切り出して別ファイルへ移すなら、ここも下げる。
        /// </summary>
        private const int MinimumLines = 3000;

        [TestMethod]
        public void TheChangelog_IsNotTruncated()
        {
            var path = Path.Combine(TestSource.Root(), "CHANGELOG.md");
            Assert.IsTrue(File.Exists(path), $"CHANGELOG.md がありません: {path}");

            var lines = File.ReadAllLines(path);
            Assert.IsTrue(lines.Length >= MinimumLines,
                $"CHANGELOG.md が {lines.Length} 行しかありません（最低 {MinimumLines} 行のはず）。"
                + Environment.NewLine
                + "書き換えで消していないか確認してください。git show HEAD:CHANGELOG.md で戻せます。"
                + Environment.NewLine
                + "意図して減らしたのなら、MinimumLines を下げて理由をコミットに書いてください。");
        }

        /// <summary>
        /// 骨格が残っていること。行数だけだと、中身が別物に置き換わっても通る。
        /// </summary>
        [TestMethod]
        public void TheChangelog_KeepsItsStructure()
        {
            var text = File.ReadAllText(Path.Combine(TestSource.Root(), "CHANGELOG.md"));

            StringAssert.Contains(text, "# CHANGELOG", "見出しがありません");
            StringAssert.Contains(text, "## [Unreleased]", "未リリースの節がありません");

            int versions = text.Split('\n').Count(l => l.StartsWith("## [1.0.", StringComparison.Ordinal));
            Assert.IsTrue(versions >= 25,
                $"版の節が {versions} 個しかありません。1.0.0 まで遡って再構成したものが消えていないか確認してください");
        }
    }
}
