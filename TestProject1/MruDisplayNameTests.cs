using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;

namespace TestProject1
{
    /// <summary>
    /// 最近使ったファイルの表示名 (<see cref="MruService.GetDisplayName"/>)。
    ///
    /// 以前は「ファイル名 + 10 文字が入らない」ときに末尾 (maxLength − 3) 文字を切り出していたが、
    /// ファイル名がそれより短い (既定の 60 で 51〜56 文字) と切り出し位置が負になり例外になった。
    /// 長さの境界で起きるので、ファイル名の長さを総当たりで見る。
    /// </summary>
    [TestClass]
    public class MruDisplayNameTests
    {
        private static readonly string LongDirectory =
            Path.Combine(@"C:\", "Users", "someone", "Documents", "projects", "foundation-design", "2026");

        [TestMethod]
        public void EveryFileNameLength_FitsAndDoesNotThrow()
        {
            for (int maxLength = 20; maxLength <= 80; maxLength += 20)
            {
                for (int nameLength = 5; nameLength <= 90; nameLength++)
                {
                    string fileName = new string('a', nameLength - 4) + ".pdj";
                    string path = Path.Combine(LongDirectory, fileName);

                    string shown;
                    try
                    {
                        shown = MruService.GetDisplayName(path, maxLength);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"ファイル名 {nameLength} 文字 (上限 {maxLength}) で例外: {ex.GetType().Name}: {ex.Message}");
                        return;
                    }

                    Assert.IsTrue(shown.Length <= maxLength,
                        $"ファイル名 {nameLength} 文字 (上限 {maxLength}) で表示名が {shown.Length} 文字になりました: {shown}");
                    Assert.IsTrue(shown.EndsWith(fileName[^Math.Min(fileName.Length, maxLength - 3)..], StringComparison.Ordinal),
                        $"表示名がファイル名の末尾を残していません: {shown}");
                }
            }
        }

        /// <summary>
        /// 「...」を付ける余地が無い短い幅でも、最大文字数を超えないこと。
        /// 以前は幅が 3 未満でも「...」(3 文字) を返していた。
        /// </summary>
        [TestMethod]
        public void VeryShortWidthsStillFit()
        {
            string path = Path.Combine(LongDirectory, "sample_project.pdj");
            for (int maxLength = 1; maxLength <= 6; maxLength++)
            {
                string shown = MruService.GetDisplayName(path, maxLength);
                Assert.IsTrue(shown.Length <= maxLength, $"幅 {maxLength} で表示名が {shown.Length} 文字です: {shown}");
                Assert.IsTrue(shown.Length > 0, $"幅 {maxLength} で表示名が空です");
                StringAssert.EndsWith("sample_project.pdj", shown.TrimStart('.'), $"幅 {maxLength}: ファイル名の末尾を残していません: {shown}");
            }
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void ZeroOrNegativeWidthIsRejected(int maxLength)
            => Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => MruService.GetDisplayName(@"C:\a\b.pdj", maxLength),
                "0 文字以下の幅を受け付けています (どんな文字列も収まりません)");

        [TestMethod]
        public void ShortPathIsShownAsIs()
        {
            Assert.AreEqual(@"C:\a\b.pdj", MruService.GetDisplayName(@"C:\a\b.pdj"));
            Assert.AreEqual("", MruService.GetDisplayName(""));
        }

        [TestMethod]
        public void LongPathKeepsTheDriveAndTheFileName()
        {
            string path = Path.Combine(LongDirectory, "more", "folders", "to", "make", "it", "long", "sample.pdj");
            Assert.AreEqual(@"C:\...\sample.pdj", MruService.GetDisplayName(path));
        }
    }
}
