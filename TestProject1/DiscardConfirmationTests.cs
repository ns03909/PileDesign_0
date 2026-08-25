using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Threading.Tasks;

namespace TestProject1
{
    /// <summary>
    /// 「現状の入力内容は削除されます」(計算例の読み込み) と
    /// 「現在のデータを保存しますか？」(新規作成・アプリ終了) を出す条件。
    ///
    /// 失うものが無いのに出す確認は、内容を確かめずに押すだけのものになり、
    /// 本当に失うものがあるときにも読まれなくなる。
    /// 3 箇所とも <see cref="MainWindowViewModel.HasUnsavedWork"/> で判定する。
    /// </summary>
    [TestClass]
    public class DiscardConfirmationTests
    {
        /// <summary>
        /// 「はい」で保存したあとに初期化する経路が、<b>保存の完了を待てる</b>形であること。
        ///
        /// 保存は同一インスタンスを Task.Run で直列化するため、await せずに
        /// <c>CurrentInputModel.Reset()</c> へ進むと、空になったモデルがそのまま
        /// 上書き保存され得る。実際に <c>_ = SaveInputModelFile();</c> と書かれており、
        /// この経路だけがアプリ終了時 (正しく await している) と食い違っていた。
        ///
        /// 戻り値を void に戻すと await できなくなるので、型で縛る。
        /// </summary>
        [TestMethod]
        public void NewFile_CanAwaitTheSaveBeforeResetting()
        {
            var method = typeof(MainWindowViewModel).GetMethod(nameof(MainWindowViewModel.NewInputModelFile));

            Assert.IsNotNull(method, "NewInputModelFile が見つからない");
            Assert.AreEqual(typeof(Task), method.ReturnType,
                "NewInputModelFile が void に戻っている。保存の完了を待てず、"
                + "空のモデルで上書き保存され得る");
        }

        [TestMethod]
        public void FreshlyStarted_NeedsNoConfirmation()
        {
            var vm = new MainWindowViewModel();

            Assert.IsFalse(vm.HasUnsavedWork,
                "起動直後で何もしていないのに、確認が出る判定になっている");
        }

        [TestMethod]
        public void AfterEditing_NeedsConfirmation()
        {
            var vm = new MainWindowViewModel();
            Assert.IsFalse(vm.HasUnsavedWork);

            // SaveUndoState は DataGrid のセル確定も含む全編集の集約点
            vm.SaveUndoState("テスト編集");

            Assert.IsTrue(vm.HasUnsavedWork,
                "編集したのに、確認が出ない判定になっている");
        }

        /// <summary>
        /// 解析を実行したら、入力を編集していなくても確認する。
        /// 解析には時間がかかるので、黙って捨ててはいけない。
        /// </summary>
        [TestMethod]
        public void AfterAnalysis_NeedsConfirmationEvenIfUnedited()
        {
            var vm = new MainWindowViewModel();
            Assert.IsFalse(vm.HasUnsavedWork);

            vm.IsHorizontalAnalysisDone = true;   // setter から SetLatestAnalysisCompleted が走る

            Assert.IsTrue(vm.HasUnsavedWork,
                "解析結果があるのに、確認が出ない判定になっている");
        }

        /// <summary>
        /// 沈下解析でも同じであること。
        /// 群杭沈下だけ完了通知の呼び出しが抜けていた。
        /// </summary>
        [TestMethod]
        public void EverySettlementAnalysis_MarksUnsavedWork()
        {
            foreach (var (name, apply) in new (string, System.Action<MainWindowViewModel>)[]
            {
                ("単杭沈下",               v => v.IsVerticalAnalysisDone = true),
                ("単杭沈下(基礎梁考慮)",   v => v.IsVerticalBeamAnalysisDone = true),
                ("群杭沈下",               v => v.IsGroupPileSettlementAnalysisDone = true),
            })
            {
                var vm = new MainWindowViewModel();
                Assert.IsFalse(vm.HasUnsavedWork, $"{name}: 初期状態がおかしい");

                apply(vm);

                Assert.IsTrue(vm.HasUnsavedWork, $"{name}: 解析後に確認が出ない判定になっている");
                Assert.IsNotNull(vm.LastAnalysisTime, $"{name}: 最終解析時刻が更新されていない");
            }
        }

        /// <summary>
        /// 入力ウィンドウを開いたら、編集されたものとして扱うこと。
        ///
        /// 地盤・杭体・荷重などのウィンドウは自前の Undo を持ち、共有の入力モデルを
        /// 直接書き換えるため、SaveUndoState を通らない。ここを取りこぼすと
        /// 「編集したのに確認が出ない」でデータを失う。
        /// </summary>
        [TestMethod]
        public void OpeningAnEditingWindow_CountsAsPossiblyEdited()
        {
            var vm = new MainWindowViewModel();
            Assert.IsFalse(vm.HasUnsavedWork);

            vm.MarkPossiblyEdited();

            Assert.IsTrue(vm.HasUnsavedWork,
                "入力ウィンドウを開いたのに、確認が出ない判定になっている");
        }

        [TestMethod]
        public void AfterMarkingSaved_NeedsNoConfirmationAgain()
        {
            var vm = new MainWindowViewModel();
            vm.SaveUndoState("テスト編集");
            Assert.IsTrue(vm.HasUnsavedWork);

            // 保存・読み込み・新規作成・計算例ロードの直後に呼ばれる
            vm.MarkWorkSaved();

            Assert.IsFalse(vm.HasUnsavedWork,
                "保存した直後なのに、確認が出る判定のままになっている");
        }
    }
}
