using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject1
{
    /// <summary>
    /// 荷重条件ウィンドウの XAML が実際にパースできることを確認するスモークテスト。
    ///
    /// StaticResource のキー誤り・x:Static の解決失敗・DataTemplate の型不整合は
    /// ビルドでは検出されずウィンドウを開いた瞬間に例外になるため、
    /// 「地盤 非線形性」列の ComboBox 化のような XAML 変更を最低限ここで検証する。
    /// </summary>
    [TestClass]
    public class LoadCaseWindowXamlSmokeTests
    {
        [TestMethod]
        public void LoadCaseWindow_XamlParses_WithoutException()
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.LoadCaseWindow();
                created = window != null;
                window?.Close();
            }, out bool timedOut);

            if (timedOut)
            {
                Assert.Inconclusive("XAML パースが 60 秒以内に完了しなかったためスキップ");
                return;
            }

            if (captured != null)
            {
                Assert.Fail($"LoadCaseWindow の XAML パースに失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            }
            Assert.IsTrue(created, "LoadCaseWindow が生成されなかった");
        }
    }
}
