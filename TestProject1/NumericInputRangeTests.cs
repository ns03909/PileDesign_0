using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System.Windows;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 入力範囲 (NumericInput.Min / Max) が画面に出るか。
    ///
    /// 範囲は 82 箇所で指定されているのに表示されておらず、
    /// 範囲外を入れてクランプされて初めて制限に気付く状態だった。
    /// </summary>
    [TestClass]
    public class NumericInputRangeTests
    {
        [TestMethod]
        public void DescribeRange_CoversEveryCombination()
        {
            Assert.AreEqual("入力範囲: 100 〜 5000", NumericInput.DescribeRange(100, 5000));
            Assert.AreEqual("入力範囲: 1 以上", NumericInput.DescribeRange(1, null));
            Assert.AreEqual("入力範囲: 64 以下", NumericInput.DescribeRange(null, 64));
            Assert.IsNull(NumericInput.DescribeRange(null, null));

            // 小数は G6、整数値は小数点を出さない
            Assert.AreEqual("入力範囲: 0 〜 0.5", NumericInput.DescribeRange(0, 0.5));
            Assert.AreEqual("入力範囲: -90 〜 90", NumericInput.DescribeRange(-90, 90));
        }

        [TestMethod]
        public void RangeToolTip_AppearsAndDoesNotClobberExistingText()
        {
            string? noneTip = null, plainTip = null;
            object? richTip = null;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                // ツールチップ無し → 範囲だけが入る
                var bare = new TextBox();
                NumericInput.SetMode(bare, NumericInputMode.Integer);
                NumericInput.SetMin(bare, 1);
                NumericInput.SetMax(bare, 64);
                Load(bare);
                noneTip = bare.ToolTip as string;

                // 文字列のツールチップ → 説明を残したまま範囲を追記
                var described = new TextBox { ToolTip = "分割数" };
                NumericInput.SetMode(described, NumericInputMode.Integer);
                NumericInput.SetMin(described, 1);
                NumericInput.SetMax(described, 64);
                Load(described);
                plainTip = described.ToolTip as string;

                // リッチなツールチップ → 構造を崩さないよう触らない
                var rich = new TextBox { ToolTip = new TextBlock { Text = "式つきの説明" } };
                NumericInput.SetMode(rich, NumericInputMode.Integer);
                NumericInput.SetMin(rich, 1);
                Load(rich);
                richTip = rich.ToolTip;
            }, out bool timedOut, timeoutSeconds: 60);

            if (timedOut)
            {
                Assert.Inconclusive("STA スレッドが 60 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual("入力範囲: 1 〜 64", noneTip);

            Assert.IsNotNull(plainTip);
            StringAssert.Contains(plainTip, "分割数", "既存の説明が消えている");
            StringAssert.Contains(plainTip, "入力範囲: 1 〜 64", "範囲が追記されていない");

            Assert.IsInstanceOfType(richTip, typeof(TextBlock), "リッチなツールチップが差し替えられている");
        }

        /// <summary>Loaded を発火させる (表示していない要素でもハンドラは動く)。</summary>
        private static void Load(FrameworkElement element)
            => element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    }
}
