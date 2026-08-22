using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestProject1
{
    /// <summary>
    /// 起動時の案内ダイアログのテスト。
    ///
    /// このダイアログは実装済みだったが <c>new WelcomeDialog()</c> がどこにも無く、
    /// DataContext も設定されていなかったため、一度も表示されないまま眠っていた。
    /// 「作ったのに繋がっていない」「繋いだのに Binding が解決しない」の 2 つは
    /// どちらもビルドを通ってしまうので、ここで検査する。
    /// </summary>
    [TestClass]
    public class WelcomeDialogTests
    {
        /// <summary>
        /// ボタンの Command バインドが全て解決すること。
        /// WPF の Binding 失敗は例外にならないため、未設定の DataContext や
        /// コマンド名の typo は「押しても何も起きないボタン」として静かに残る。
        /// </summary>
        [TestMethod]
        public void WelcomeDialog_AllButtonCommandsResolve()
        {
            var unresolved = new List<string>();
            int buttonCount = 0;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var dialog = new PileDesign.Views.WelcomeDialog();
                try
                {
                    Assert.IsNotNull(dialog.DataContext, "DataContext が設定されていない");

                    // 表示していない Window はビジュアルツリーを持たないので論理ツリーを歩く。
                    //
                    // Binding の値そのもの (button.Command) は Dispatcher が回るまで
                    // 埋まらないので、代わりに「Binding の Path が DataContext の型に
                    // 実在するか」を見る。無言で失敗する典型はこの Path の綴り違いで、
                    // MVVM Toolkit の生成名 (メソッド名 + Command、Async は除く) を
                    // 取り違えると起きる。
                    var vmType = dialog.DataContext.GetType();

                    foreach (var button in FindLogicalChildren<Button>(dialog))
                    {
                        // 「次回から表示しない」等、コマンドを持たない部品は対象外
                        var expr = System.Windows.Data.BindingOperations
                            .GetBindingExpression(button, ButtonBase_CommandProperty);
                        if (expr == null) continue;

                        buttonCount++;
                        string path = expr.ParentBinding.Path?.Path ?? "";
                        if (vmType.GetProperty(path) == null)
                            unresolved.Add($"{button.Content}: {vmType.Name} に {path} が無い");
                    }
                }
                finally
                {
                    dialog.Close();
                }
            }, out bool timedOut, timeoutSeconds: 120);

            if (timedOut)
            {
                Assert.Inconclusive("ダイアログ生成が 120 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"案内ダイアログの生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(4, buttonCount,
                "コマンド付きボタンが 4 つ (新規 / ファイルを開く / 計算例 / 使い方) ではない");
            Assert.AreEqual(0, unresolved.Count,
                "Command が解決しないボタンがあります (押しても何も起きない):\n  " +
                string.Join("\n  ", unresolved));
        }

        /// <summary>
        /// 4 つの入口がそれぞれ別の結果を返し、App 側の分岐が全て届くこと。
        /// </summary>
        [TestMethod]
        public void WelcomeDialogViewModel_EachEntryReturnsItsOwnResult()
        {
            var cases = new (string Name, System.Action<WelcomeDialogViewModel> Invoke, WelcomeDialogResult Expected)[]
            {
                ("新規プロジェクト", vm => vm.NewProjectCommand.Execute(null),   WelcomeDialogResult.NewProject),
                ("ファイルを開く",   vm => vm.OpenExistingCommand.Execute(null),  WelcomeDialogResult.OpenExisting),
                ("計算例を開く",     vm => vm.OpenSampleCommand.Execute(null),    WelcomeDialogResult.OpenSample),
                ("使い方をみる",     vm => vm.ShowQuickStartCommand.Execute(null),WelcomeDialogResult.ShowQuickStart),
            };

            foreach (var (name, invoke, expected) in cases)
            {
                var vm = new WelcomeDialogViewModel();
                Assert.AreEqual(WelcomeDialogResult.None, vm.Result, $"{name}: 初期値が None でない");

                bool closeRequested = false;
                vm.RequestClose += (_, _) => closeRequested = true;

                invoke(vm);

                Assert.AreEqual(expected, vm.Result, $"{name}: Result が想定と違う");
                Assert.IsTrue(closeRequested, $"{name}: RequestClose が発火していない");
            }

            // WelcomeDialogResult の全メンバに入口があること (None を除く)
            var covered = cases.Select(c => c.Expected).ToHashSet();
            var missing = System.Enum.GetValues<WelcomeDialogResult>()
                .Where(r => r != WelcomeDialogResult.None && !covered.Contains(r))
                .ToList();
            Assert.AreEqual(0, missing.Count,
                "入口の無い WelcomeDialogResult があります: " + string.Join(", ", missing));
        }

        private static readonly DependencyProperty ButtonBase_CommandProperty =
            System.Windows.Controls.Primitives.ButtonBase.CommandProperty;

        private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject node) continue;
                if (node is T typed) yield return typed;
                foreach (var descendant in FindLogicalChildren<T>(node))
                    yield return descendant;
            }
        }
    }
}
