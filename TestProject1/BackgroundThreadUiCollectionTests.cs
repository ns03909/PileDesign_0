using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// 背景スレッドから届く通知で、UI にバインドされたコレクションを触らないこと。
    ///
    /// 解析モデルの組立 (<c>AnalysisModelling</c>) は <c>Task.Run</c> で走り、その中で
    /// 杭へ FEM の参照 (<c>PileTopRotationalSpring</c> など) を書き込む。
    /// 杭を選択していると、その <c>PropertyChanged</c> がプロパティパネルの再構築を呼ぶ。
    /// <c>ObservableCollection</c> を背景スレッドから変更すると
    /// <c>CollectionView</c> が <c>NotSupportedException</c> を投げ、<b>解析ごと落ちる</b>。
    ///
    /// 杭を選択しているときだけ再現するので、テストでも実機でも当たりにくい。
    /// 呼ばれる側で Dispatcher へ回すのが確実 (呼ぶ側は背景スレッドで動くのが仕事)。
    /// </summary>
    [TestClass]
    public class BackgroundThreadUiCollectionTests
    {
        /// <summary>
        /// 背景スレッドから来うるハンドラが、UI スレッドへ回してから
        /// バインド済みコレクションを触っていること。
        /// </summary>
        [TestMethod]
        public void HandlersReachedFromTheModellingThreadMarshalToTheUiThread()
        {
            // ファイル名で決め打ちしない。partial の分割は動くので、
            // 「MainWindowViewModel.*.cs のどれかにある」で探す。
            // 実際、MainWindowViewModel.Constructor.cs (4,542 行) を分割したときに
            // このメソッドが PropertyPanel.cs へ移り、決め打ちが外れて落ちた。
            const string Signature = "private void OnSelectedItemPropertyChanged";
            string code = "";
            var candidates = Directory.GetFiles(
                Path.Combine(FindSolutionRoot(), "Graphics_r1", "ViewModels"),
                "MainWindowViewModel*.cs");
            foreach (var f in candidates)
            {
                string text = File.ReadAllText(f);
                if (text.Contains(Signature, StringComparison.Ordinal)) { code = text; break; }
            }

            TestSource.AssertScanned(candidates.Length, 8, "MainWindowViewModel の分割ファイル");

            int start = code.IndexOf(Signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0,
                "OnSelectedItemPropertyChanged が MainWindowViewModel*.cs のどこにもありません。"
                + "名前を変えたなら、ここも直してください");

            int clear = code.IndexOf("SelectedItemProperties.Clear()", start, StringComparison.Ordinal);
            Assert.IsTrue(clear > start, "プロパティ一覧を消す処理が見つかりません");

            string head = code[start..clear];
            StringAssert.Contains(head, "CheckAccess()",
                "背景スレッドから来たときに UI スレッドへ回していません。"
                + "解析モデルの組立 (背景スレッド) が杭へ FEM 参照を書き込むと、"
                + "杭を選択しているだけで解析が NotSupportedException で落ちます");
            StringAssert.Contains(head, "BeginInvoke",
                "UI スレッドへ回す処理がありません");
        }

        /// <summary>
        /// 解析モデルの組立が杭のプロパティを書いていること (この検査の前提)。
        /// 書かなくなったら上の手当ては不要になるので、そのときは一緒に見直す。
        /// </summary>
        [TestMethod]
        public void TheModellingThreadStillWritesPileProperties()
        {
            string root = FindSolutionRoot();
            string code = File.ReadAllText(Path.Combine(
                root, "Graphics_r1", "FEM", "AnalysisModelling.cs"));

            var writes = Regex.Matches(code, @"pile\.\w+\s*=\s*[^=]")
                .Select(m => m.Value.Trim())
                .Distinct()
                .ToList();

            Assert.IsTrue(writes.Count > 0,
                "解析モデルの組立が杭のプロパティを書かなくなりました。"
                + "OnSelectedItemPropertyChanged の UI スレッド回避が要るかを見直してください");
        }

        /// <summary>ソリューションのルート。探し方は <see cref="TestSource.Root"/> に 1 つだけ置いてある。</summary>
        private static string FindSolutionRoot() => TestSource.Root();
    }
}
