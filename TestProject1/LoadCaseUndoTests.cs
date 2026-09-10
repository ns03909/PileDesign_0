using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common.Undo;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 荷重条件ウィンドウの Undo / Redo が、<b>モデルの実体</b>に届くこと。
    ///
    /// この画面は <c>InputModel.LoadCasesInput</c> が持つコレクションの実体を
    /// そのまま編集する。OK が書き戻すのは割増係数と荷重組合せだけで、
    /// <b>荷重ケース本体は書き戻さない</b>。
    ///
    /// ところが <c>RestoreState</c> はコレクションを<b>差し替えて</b>いたため、
    /// Undo した瞬間に画面が実体から外れ、
    ///
    /// <list type="number">
    /// <item>Undo が実体に届かない (実測: 実体 1111 / 画面 1000)</item>
    /// <item><b>そのあとに打った値も実体に届かない</b> (777 を打っても実体は 1111)</item>
    /// <item>OK で閉じると、画面に出ていない値がそのまま解析へ行く</item>
    /// </list>
    ///
    /// 杭断面ウィンドウと同じ形。中身だけを写す形に揃えた。
    ///
    /// <para>あわせて、<b>コンストラクタが控えを積んでいた</b>のも直した。
    /// 組み立ての途中 (<c>LoadCombinations</c> の代入時) に積まれていたので、
    /// 履歴の先頭が「半分できた状態」になっていた。</para>
    /// </summary>
    [TestClass]
    public class LoadCaseUndoTests
    {
        [TestMethod]
        public void Undo_ReachesTheModelAndKeepsTheLink()
        {
            var mainVm = new MainWindowViewModel();
            var model = mainVm.CurrentInputModel;
            Assert.IsNotNull(model?.LoadCasesInput, "既定モデルに荷重条件がありません");

            var vm = new LoadCaseViewModel(mainVm);

            var liveCases = model!.LoadCasesInput.LoadCasesLevel1;
            Assert.AreSame(liveCases, vm.LoadCasesLevel1,
                "画面が最初からモデルの実体を見ていません (前提が変わった)");

            double before = vm.LoadCasesLevel1.First().UpperMassForce;

            // 画面は編集の直前に控えを取る
            vm.PushUndoState();
            vm.LoadCasesLevel1.First().UpperMassForce = before + 111.0;
            Assert.AreEqual(before + 111.0,
                model.LoadCasesInput.LoadCasesLevel1.First().UpperMassForce, 1e-9,
                "編集が実体に届いていません (揺らせていない)");

            vm.UndoCommand.Execute(null);

            Assert.AreSame(model.LoadCasesInput.LoadCasesLevel1, vm.LoadCasesLevel1,
                "Undo で画面がモデルの実体から外れました。"
                + "以後の入力がどこにも届かず、OK で閉じると画面に出ていない値が解析へ行きます");

            Assert.AreEqual(before,
                model.LoadCasesInput.LoadCasesLevel1.First().UpperMassForce, 1e-9,
                "Undo が実体に届いていません");
        }

        [TestMethod]
        public void EditsStillReachTheModel_AfterAnUndo()
        {
            var mainVm = new MainWindowViewModel();
            var model = mainVm.CurrentInputModel;
            var vm = new LoadCaseViewModel(mainVm);

            double before = vm.LoadCasesLevel1.First().UpperMassForce;

            vm.PushUndoState();
            vm.LoadCasesLevel1.First().UpperMassForce = before + 111.0;
            vm.UndoCommand.Execute(null);

            // Undo のあとに打つ
            vm.LoadCasesLevel1.First().UpperMassForce = 777.0;

            Assert.AreEqual(777.0,
                model!.LoadCasesInput.LoadCasesLevel1.First().UpperMassForce, 1e-9,
                "Undo のあとに打った値が実体へ届いていません。"
                + "Undo がコレクションを差し替えて、モデルの実体から外れています");
        }

        [TestMethod]
        public void Redo_ReachesTheModel()
        {
            var mainVm = new MainWindowViewModel();
            var model = mainVm.CurrentInputModel;
            var vm = new LoadCaseViewModel(mainVm);

            double before = vm.LoadCasesLevel1.First().UpperMassForce;
            double edited = before + 111.0;

            vm.PushUndoState();
            vm.LoadCasesLevel1.First().UpperMassForce = edited;
            vm.UndoCommand.Execute(null);
            vm.RedoCommand.Execute(null);

            Assert.AreEqual(edited,
                model!.LoadCasesInput.LoadCasesLevel1.First().UpperMassForce, 1e-9,
                "Redo が実体に届いていません。"
                + "戻す処理の中で控えを積み直していると、Undo した瞬間に履歴の後ろが"
                + "切り捨てられて Redo が消えます");
        }

        /// <summary>
        /// 組み立て中の状態が履歴に残らないこと。
        /// 残ると、開いた直後の Ctrl+Z が「半分できた状態」へ戻す。
        /// </summary>
        [TestMethod]
        public void TheHistory_StartsWithTheOpenedStateOnly()
        {
            var mainVm = new MainWindowViewModel();
            var vm = new LoadCaseViewModel(mainVm);

            var manager = Manager(vm);
            Assert.AreEqual(1, manager.History.Count,
                $"開いた直後の履歴が {manager.History.Count} 段あります。"
                + "組み立て中に控えを積んでいると、半分できた状態が履歴に入ります");

            // 先頭は「開いた時点の姿」= いまの値と一致していること
            Assert.IsFalse(manager.CanRedo, "開いた直後に Redo が可能になっています");
        }

        /// <summary>
        /// 戻す処理が、実体のコレクションを差し替えていないこと。
        /// </summary>
        [TestMethod]
        public void TheRestorePath_DoesNotSwapTheLiveCollections()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "LoadCaseViewModel.cs");
            string body = Regex.Replace(
                TestSource.MethodBody(src, "private void RestoreState(LoadCaseState state)"), "//.*", "");

            foreach (string name in new[]
            {
                "LoadCasesLevel1", "LoadCasesLevel2",
                "LoadCasesLevel1Common", "LoadCasesLevel2Common",
            })
            {
                Assert.IsFalse(Regex.IsMatch(body, $@"\b{name}\s*=\s*new\b"),
                    $"{name} をオブジェクトごと差し替えています。"
                    + "モデルの実体から外れ、Undo も以後の入力も届かなくなります");
            }

            StringAssert.Contains(body, "_suppressUndoSave = true",
                "戻している間の控えを抑止していません。"
                + "戻したあとの再計算が控えを積み、Redo が消えます");
            StringAssert.Contains(body, "finally",
                "抑止フラグを finally で戻していません");
        }

        /// <summary>
        /// 荷重ケースの持ち物が<b>写しの届く範囲</b>に収まっていること。
        ///
        /// 戻すのはスカラー (値型と文字列) だけ。参照型は写さない
        /// （写すと実体が控え側と入れ替わり、他所の参照と保存グラフの $ref が外れる）。
        /// だから参照型の持ち物が増えたら、そこは Undo で戻らなくなる。
        ///
        /// いまは <c>Point3D</c> が struct なので作用点も写る。
        /// 参照型に変えたり、子コレクションを持たせたりしたらここで落ちる。
        /// </summary>
        [TestMethod]
        public void EverythingTheyHold_IsWithinReachOfTheRestore()
        {
            var offenders = new System.Collections.Generic.List<string>();
            int scanned = 0;

            foreach (Type type in new[]
            {
                typeof(PileDesign.Models.InputData.LoadCase),
                typeof(PileDesign.Models.InputData.LoadCaseCommon),
            })
            {
                for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsInitOnly) continue;                     // 購読やロックの入れ物
                        if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;

                        scanned++;

                        if (f.FieldType.IsValueType || f.FieldType == typeof(string)) continue;

                        // 画面との配線は入力ではないので戻す対象ではない
                        if (f.FieldType == typeof(MainWindowViewModel)) continue;

                        offenders.Add($"{type.Name}.{f.Name} ({f.FieldType.Name})");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 10, "荷重ケースの持ち物");

            Assert.AreEqual(0, offenders.Count,
                "荷重ケースが参照型の持ち物を持っています。"
                + "中身だけを写す復元では戻らないので、Undo が取り残します:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        private static UndoManager Manager(LoadCaseViewModel vm)
        {
            var f = typeof(LoadCaseViewModel).GetField("_undoManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "_undoManager が見つかりません (名前が変わった?)");
            var m = f!.GetValue(vm) as UndoManager;
            Assert.IsNotNull(m, "UndoManager を取り出せません");
            return m!;
        }
    }
}
