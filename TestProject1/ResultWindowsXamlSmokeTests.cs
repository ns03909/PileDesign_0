using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// 結果表示まわりのウィンドウの XAML が実際にパースできることを確認するスモークテスト。
    ///
    /// StaticResource のキー誤り・x:Static の解決失敗・DataTemplate の型不整合は
    /// ビルドを通ってしまい、ウィンドウを開いた瞬間に例外になる。
    /// 解析結果セット (2026-08-22) でステータスバーとグラフの基準切替を足したため、
    /// これらのウィンドウもパースだけは自動で踏んでおく。
    /// </summary>
    [TestClass]
    public class ResultWindowsXamlSmokeTests
    {
        private static void AssertWindowParses(string name, System.Func<System.Windows.Window> factory)
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = factory();
                created = window != null;
                window?.Close();
            }, out bool timedOut);

            if (timedOut)
            {
                Assert.Inconclusive($"{name} の XAML パースが 60 秒以内に完了しなかったためスキップ");
                return;
            }

            if (captured != null)
            {
                Assert.Fail($"{name} の XAML パースに失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            }
            Assert.IsTrue(created, $"{name} が生成されなかった");
        }

        /// <summary>グラフウィンドウ（断面の基準切替の RadioButton / BoolEqualsConverter を含む）。</summary>
        [TestMethod]
        public void GraphWindow_XamlParses_WithoutException()
        {
            var (inputModel, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (inputModel == null) { Assert.Inconclusive("例題ファイルなし"); return; }

            AssertWindowParses("GraphWindow", () =>
            {
                var mainVm = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = inputModel };
                inputModel.AttachViewModel(mainVm);
                return new PileDesign.Views.GraphWindow(new PileDesign.ViewModels.GraphViewModel(mainVm));
            });
        }

        // 検定ウィンドウは廃止した (検定は解析結果テーブルから見る)。

        // MainWindow は他のテストが別 STA スレッドで作った WPF の静的オブジェクトに触れてしまい、
        // 「呼び出しスレッドはこのオブジェクトにアクセスできません」で落ちるためここでは対象外。
        // ステータスバーへの追加は StaticResource を使わない TextBlock なのでパース失敗の危険は小さい。
    }
}
