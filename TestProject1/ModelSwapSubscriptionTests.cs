using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 入力モデルごと差し替えたあとも、中身の変更が拾えること。
    ///
    /// 杭・基礎梁の <c>PropertyChanged</c> は「コレクションに追加されたとき」に張られる。
    /// ファイル読込・Undo・計算例の読み込みではコレクションごと新しいインスタンスに変わり、
    /// <b>中身が入った状態で購読の無い要素が残る</b>。そうなると編集しても集計表示が更新されない。
    ///
    /// 基礎梁は差し替え時に張り直していたが、杭は漏れていた。
    /// </summary>
    [TestClass]
    public class ModelSwapSubscriptionTests
    {
        private static InputModel ModelWithPiles(params double[] axialForces)
        {
            var input = new InputModel
            {
                PileLayoutItems = [.. axialForces.Select((v, i) => new PileLayoutDataItem
                {
                    PileNo = i + 1,
                    AxialForceVL0 = v,
                })],
            };
            return input;
        }

        /// <summary>
        /// 差し替え後の杭を編集したら、ΣVL の表示が更新されること。
        /// </summary>
        [TestMethod]
        public void EditingAPileAfterSwappingTheModel_UpdatesTheTotals()
        {
            var vm = new MainWindowViewModel { CurrentInputModel = ModelWithPiles(100.0, 200.0) };

            // 差し替え (ファイル読込・Undo と同じ形: 中身が入った新インスタンス)
            vm.CurrentInputModel = ModelWithPiles(1000.0, 2000.0);

            var changed = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

            vm.CurrentInputModel.PileLayoutItems[0].AxialForceVL0 = 5000.0;

            Assert.IsTrue(changed.Count > 0,
                "差し替え後の杭を編集しても ViewModel が反応していない "
                + "(杭ごとの PropertyChanged が張り直されていない)");
        }

        /// <summary>
        /// 差し替え前の杭を編集しても、もう反応しないこと（購読が外れていること）。
        /// 張り直しのついでに古いモデルを掴み続けると、破棄したモデルが生き残る。
        /// </summary>
        [TestMethod]
        public void OldPiles_AreNotStillDrivingTheViewModel()
        {
            var first = ModelWithPiles(100.0);
            var vm = new MainWindowViewModel { CurrentInputModel = first };

            vm.CurrentInputModel = ModelWithPiles(1000.0);

            var changed = new System.Collections.Generic.List<string>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? "");

            first.PileLayoutItems[0].AxialForceVL0 = 9999.0;

            Assert.AreEqual(0, changed.Count,
                "差し替え前のモデルの編集に ViewModel が反応している: "
                + string.Join(", ", changed.Distinct()));
        }

        /// <summary>
        /// 二重購読になっていないこと (同じ編集で 2 回集計が走らない)。
        /// </summary>
        [TestMethod]
        public void SwappingTwice_DoesNotSubscribeTwice()
        {
            var model = ModelWithPiles(100.0);
            var vm = new MainWindowViewModel { CurrentInputModel = model };

            // 同じインスタンスを 2 回入れても購読は 1 本のまま
            vm.CurrentInputModel = ModelWithPiles(200.0);
            vm.CurrentInputModel = model;
            vm.CurrentInputModel = model;

            int handlers = CountHandlers(model.PileLayoutItems[0]);
            Assert.AreEqual(1, handlers,
                $"杭 1 本に集計用のハンドラが {handlers} 本張られている (二重購読)");
        }

        /// <summary>
        /// 杭 1 本に張られた <c>PileLayoutItem_PropertyChanged</c> の本数。
        /// 他の購読者 (モデル自身など) も同じイベントに乗るので、名前で絞る。
        /// </summary>
        private static int CountHandlers(PileLayoutDataItem pile)
        {
            // イベントのバッキングフィールドは基底クラス側にあることがあるので階層を辿る
            for (var t = pile.GetType(); t != null; t = t.BaseType)
            {
                var field = t.GetField("PropertyChanged",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (field == null) continue;
                return field.GetValue(pile) is PropertyChangedEventHandler h
                    ? h.GetInvocationList()
                        .Count(d => d.Method.Name == "PileLayoutItem_PropertyChanged")
                    : 0;
            }
            Assert.Fail("PropertyChanged のバッキングフィールドが見つかりません");
            return 0;
        }
    }
}
