using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestProject1
{
    /// <summary>
    /// 荷重ケースの一覧 (レベル 1・2) の変更通知が、一覧の差し替え・Reset の後も過不足なく届くこと。
    ///
    /// 2026-09-26 のレビューで 2 件:
    /// - 同じ一覧を設定し直すと、購読を外したあと SetProperty が「変更なし」で抜け、再購読されなかった
    /// - Clear() (Reset) では OldItems が無いので、除かれたケースの購読が残った
    /// </summary>
    [TestClass]
    public class LoadCaseSubscriptionTests
    {
        private static (LoadCasesInput input, List<string> raised) Watch(LoadCasesInput input)
        {
            var raised = new List<string>();
            input.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
            return (input, raised);
        }

        private static LoadCase Case(int no) => new() { Level = 1, No = no, IsApplicable = true };

        [TestMethod]
        public void SettingTheSameCollectionAgainKeepsTheNotifications()
        {
            var a = Case(1);
            var list = new ObservableCollection<LoadCase> { a };
            var (input, raised) = Watch(new LoadCasesInput { LoadCasesLevel1 = list });

            input.LoadCasesLevel1 = list;   // 同じ一覧を設定し直す
            raised.Clear();

            list.Add(Case(2));
            CollectionAssert.Contains(raised, nameof(LoadCasesInput.AllLoadCases), "同じ一覧の再設定で追加の通知が外れています");

            raised.Clear();
            a.IsApplicable = false;
            CollectionAssert.Contains(raised, nameof(LoadCasesInput.AllSeismicLoadCases), "同じ一覧の再設定でケースの通知が外れています");
            Assert.AreEqual(2, input.SubscribedLoadCaseCount);
        }

        [TestMethod]
        public void ClearReleasesTheRemovedCases()
        {
            var a = Case(1);
            var list = new ObservableCollection<LoadCase> { a, Case(2) };
            var (input, raised) = Watch(new LoadCasesInput { LoadCasesLevel1 = list });
            Assert.AreEqual(2, input.SubscribedLoadCaseCount);

            list.Clear();
            Assert.AreEqual(0, input.SubscribedLoadCaseCount, "Clear() で除いたケースの購読が残っています");

            raised.Clear();
            a.IsApplicable = false;
            Assert.AreEqual(0, raised.Count, "除いたケースの変更が通知されています");

            var b = Case(3);
            list.Add(b);
            raised.Clear();
            b.IsApplicable = false;
            CollectionAssert.Contains(raised, nameof(LoadCasesInput.AllSeismicLoadCases), "Clear() の後に足したケースが購読されていません");
        }

        [TestMethod]
        public void ReplacingTheCollectionMovesTheSubscriptions()
        {
            var a = Case(1);
            var old = new ObservableCollection<LoadCase> { a };
            var (input, raised) = Watch(new LoadCasesInput { LoadCasesLevel1 = old });

            var b = Case(2);
            input.LoadCasesLevel1 = new ObservableCollection<LoadCase> { b };
            raised.Clear();

            a.IsApplicable = false;
            old.Add(Case(9));
            Assert.AreEqual(0, raised.Count, "差し替え前の一覧・ケースの変更が通知されています");

            b.IsApplicable = false;
            CollectionAssert.Contains(raised, nameof(LoadCasesInput.AllSeismicLoadCases));
            Assert.AreEqual(1, input.SubscribedLoadCaseCount);

            input.LoadCasesLevel1 = null!;
            Assert.AreEqual(0, input.SubscribedLoadCaseCount);
        }

        /// <summary>複製の一覧の差し替えが、元の購読を書き換えないこと (MemberwiseClone で控えを共有しない)。</summary>
        [TestMethod]
        public void ACopyDoesNotDisturbTheOriginal()
        {
            var a = Case(1);
            var (input, raised) = Watch(new LoadCasesInput { LoadCasesLevel1 = new ObservableCollection<LoadCase> { a } });

            var copy = input.DeepCopy();
            Assert.AreEqual(1, copy.SubscribedLoadCaseCount);
            Assert.AreEqual(1, input.SubscribedLoadCaseCount, "複製で元の購読の控えが書き換わっています");

            raised.Clear();
            a.IsApplicable = false;
            CollectionAssert.Contains(raised, nameof(LoadCasesInput.AllSeismicLoadCases), "複製の後に元のケースの通知が外れています");
        }
    }
}
