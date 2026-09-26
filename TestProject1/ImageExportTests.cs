using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;

namespace TestProject1
{
    /// <summary>
    /// モデル図の画像の保存・コピー (<c>MainWindowViewModel.ImageSave</c> / <c>ImageCopy</c>)。
    /// </summary>
    [TestClass]
    public class ImageExportTests
    {
        /// <summary>
        /// 倍率は正の有限の数で上限以下だけを受け付けること。以前は読めた数をそのまま使い、
        /// 0・負数では 0 画素の画像で例外に、極端に大きい値ではメモリを使い切る大きさの画像を作ろうとした。
        /// </summary>
        [TestMethod]
        public void TheScaleMustBeAPositiveFiniteNumberWithinTheLimit()
        {
            Assert.IsTrue(MainWindowViewModel.TryResolveImageScale(null, out double s, out _));
            Assert.AreEqual(1.0, s);
            Assert.IsTrue(MainWindowViewModel.TryResolveImageScale("2", out s, out _));
            Assert.AreEqual(2.0, s);

            foreach (var bad in new[] { "0", "-1", "NaN", "∞", "abc", "1000" })
                Assert.IsFalse(MainWindowViewModel.TryResolveImageScale(bad, out _, out string? error) || error == null,
                    $"倍率「{bad}」を受け付けています");
        }

        [TestMethod]
        public void ThePixelSizeIsCheckedBeforeTheImageIsMade()
        {
            Assert.IsTrue(MainWindowViewModel.TryImagePixelSize(800, 600, 96, 96, 2, out int w, out int h, out _));
            Assert.AreEqual((1600, 1200), (w, h));

            Assert.IsFalse(MainWindowViewModel.TryImagePixelSize(0, 600, 96, 96, 1, out _, out _, out string? empty),
                "表示領域の大きさが 0 なのに画像を作ろうとしています");
            StringAssert.Contains(empty, "大きさが 0");

            Assert.IsFalse(MainWindowViewModel.TryImagePixelSize(4000, 3000, 192, 192, 8, out _, out _, out string? huge),
                "上限を超える大きさの画像を作ろうとしています");
            StringAssert.Contains(huge, "大きすぎます");
        }

        /// <summary>
        /// クリップボードへ書き込めなかったときに「コピーしました」と出さないこと。
        /// 以前は書き込みの結果を見ずに成功と表示した。
        /// </summary>
        [TestMethod]
        public void TheCopyResultReflectsWhetherTheClipboardWasWritten()
        {
            StringAssert.Contains(MainWindowViewModel.DescribeImageCopyResult(true, 10, 20), "コピーしました (10x20)");
            string failed = MainWindowViewModel.DescribeImageCopyResult(false, 10, 20);
            Assert.IsFalse(failed.Contains("コピーしました", StringComparison.Ordinal), "書き込めなかったのに成功と出しています");
            StringAssert.Contains(failed, "コピーできませんでした");

            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ImageExport.cs");
            string copy = System.Text.RegularExpressions.Regex.Replace(
                TestSource.MethodBody(src, "private void ImageCopy(string scaleParam)"), @"\s+", " ");
            StringAssert.Contains(copy, "DescribeImageCopyResult( Common.ClipboardHelper.TrySetDataObject(",
                "クリップボードへの書き込みの結果で表示を分けていません");
        }

        /// <summary>画像の保存は一時ファイルに書き切ってから差し替えること (失敗しても前の画像を失わない)。</summary>
        [TestMethod]
        public void ImagesAreSavedThroughATemporaryFile()
        {
            string body = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.ImageExport.cs"),
                "private void ImageSave(string scaleParam)");
            StringAssert.Contains(body, "WriteAtomically(dialog.FileName");
            Assert.IsFalse(body.Contains("new System.IO.FileStream(dialog.FileName", StringComparison.Ordinal), "保存先を直接作り直しています");
        }
    }
}
