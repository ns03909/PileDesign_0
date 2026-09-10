using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace TestProject1
{
    /// <summary>
    /// 杭要素分割が済んでいないと、水平解析のウィンドウが開かないこと。
    ///
    /// 分割前の解析は応答が過大に出る (同梱の計算例で 10〜27%。理由と実測は
    /// <see cref="AnalysisOutputInvariantTests"/> と CHANGELOG)。
    /// アプリはこれを<b>注意で知らせるのではなく、状況そのものを防いで</b>いる。
    /// <c>EnsureElementSplit</c> がウィンドウを作る前の関門になっており、済んでいなければ
    /// その場で訊き、断られても分割画面で取り消しても通さない。
    ///
    /// <para><b>この前提の上に「分割前の注意は要らない」と判断している。</b>
    /// 2026-09-10 に注意を足しかけたが、構造上出ないので取り下げた
    /// (出ない注意は「保護がある」と読めてしまうのが害)。
    /// 関門が外れたら、その判断が誤りになる。だからここで見張る。</para>
    /// </summary>
    [TestClass]
    public class ElementSplitGateTests
    {
        [TestMethod]
        public void TheHorizontalAnalysis_DoesNotOpenWithoutTheElementSplit()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");

            int at = src.IndexOf("OpenLateralLoadAnalysisWindowAsync", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "水平解析ウィンドウを開く処理が見つかりません");

            // ウィンドウを作るより前に関門を通ること
            int gate = src.IndexOf("EnsureElementSplit(\"水平解析\")", at, StringComparison.Ordinal);
            int create = src.IndexOf("new HorizontalCalculationViewModel(this)", at, StringComparison.Ordinal);

            Assert.IsTrue(gate > 0,
                "水平解析が EnsureElementSplit を通っていません。"
                + "分割前に解析でき、応答が過大に出たまま気づけません");
            Assert.IsTrue(create > 0, "水平解析のビューモデルを作る処理が見つかりません");
            Assert.IsTrue(gate < create,
                "関門がウィンドウを作ったあとにあります。分割前に解析が始まります");
        }

        /// <summary>
        /// 関門が「済んだことにしない」こと。
        /// 分割画面で取り消されたのに true を返すと、関門が意味を失う。
        /// </summary>
        [TestMethod]
        public void TheGate_DoesNotAssumeSuccess()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");

            string body = TestSource.MethodBody(src, "private bool EnsureElementSplit(string what)");

            StringAssert.Contains(body, "return IsElementSplit",
                "分割画面を開いたあと、済んだかどうかを確かめずに返しています。"
                + "取り消されても通ってしまいます");
            Assert.IsFalse(body.Contains("return true;") && !body.Contains("if (IsElementSplit) return true;"),
                "無条件に true を返す経路があります");
        }
    }
}
