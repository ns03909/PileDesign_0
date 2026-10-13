using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭要素分割を開く前の「変更前の写し」(元に戻す用) を、UI スレッドでその場で取ること。
    ///
    /// 以前は <c>await Task.Run(() => source.CopyForHistory())</c> で裏で取っていた。その間もメイン画面は
    /// 操作できるので、写しの途中で入力を編集する・Ctrl+Z で入力が差し替わると、途中まで古く途中から新しい写し、
    /// または差し替え前の入力の写しが「分割の前」として履歴に積まれ、元に戻すとその入力が復元された。
    /// 写しは最大の計算例でも数 ms (杭 108 本で約 1.3 ms、39 本で約 2.6 ms。2026-10-13 実測) で、
    /// 編集のたびに UI スレッドで取っている写しと同じ重さなので、待ち合わせをやめた。
    /// </summary>
    [TestClass]
    public class ElementDivisionUndoCopyTests
    {
        private static string OpenMethodBody() => TestSource.MethodBody(
            TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs"),
            "public void OpenElementDivisionWindow()");

        /// <summary>写しを裏のスレッドで取らない (待ち合わせの間に入力が変わりうる)。</summary>
        [TestMethod]
        public void UndoCopyIsNotTakenOnAWorkerThread()
        {
            string body = OpenMethodBody();
            Assert.IsFalse(body.Contains("Task.Run(", StringComparison.Ordinal),
                "変更前の写しを裏のスレッドで取っています (写しの途中で入力が編集・差し替えされうる)");
            Assert.IsFalse(body.Contains("await ", StringComparison.Ordinal),
                "写しを取ってからウィンドウを開くまでの間に待ち合わせがあります (その間に入力が変わりうる)");

            int copy = body.IndexOf("CopyBeforeElementDivision(CurrentInputModel)", StringComparison.Ordinal);
            int open = body.IndexOf("new ElementDivisionWindow(", StringComparison.Ordinal);
            Assert.IsTrue(copy >= 0, "変更前の写しを取っていません");
            Assert.IsTrue(open > copy, "変更前の写しをウィンドウを開く前に取っていません");
        }

        /// <summary>写しはその時点の入力のもので、あとの編集が入り込まない。</summary>
        [TestMethod]
        public void UndoCopyReflectsTheInputAtTheTimeOfTheCall()
        {
            var (input, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive(error); return; }

            var pile = input.PileLayoutItems.First();
            double before = pile.AxialForceVL0;
            var copy = MainWindowViewModel.CopyBeforeElementDivision(input);
            Assert.IsNotNull(copy, "写しを作れませんでした");

            // 写しを取ったあとの編集
            pile.AxialForceVL0 = before + 1234.0;

            Assert.AreEqual(before, copy!.PileLayoutItems.First().AxialForceVL0, 0.0,
                "写しにあとの編集が入っています");
            Assert.AreNotSame(input, copy);
            Assert.IsNull(MainWindowViewModel.CopyBeforeElementDivision(null));
        }
    }
}
