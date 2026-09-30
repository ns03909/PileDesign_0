using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System.Globalization;
using System.Threading;

namespace TestProject1;

/// <summary>
/// 地域設定が違う PC でも、数値の表示・読み取り・書き出しが同じになること (<see cref="AppCulture"/>)。
/// 保存ファイル (JSON)・CSV が地域設定に依らないことは FileOperationServiceTests・DataGridCsvExportTests が見る。
/// </summary>
[TestClass]
[DoNotParallelize]
public class RegionalSettingsTests
{
    private CultureInfo? _current, _currentUi, _default, _defaultUi;

    [TestInitialize]
    public void Save()
    {
        _current = CultureInfo.CurrentCulture;
        _currentUi = CultureInfo.CurrentUICulture;
        _default = CultureInfo.DefaultThreadCurrentCulture;
        _defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
    }

    [TestCleanup]
    public void Restore()
    {
        CultureInfo.CurrentCulture = _current!;
        CultureInfo.CurrentUICulture = _currentUi!;
        CultureInfo.DefaultThreadCurrentCulture = _default;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUi;
    }

    /// <summary>
    /// <b>本題。</b> 小数点がカンマの地域 (de-DE) で起動しても、アプリの中は ja-JP の書き方 (小数点はピリオド) になる。
    /// Windows の地域設定で変えた記号は使わない。これから作られるスレッド (解析の並列処理) も同じ。
    /// </summary>
    [TestMethod]
    public void TheAppUsesJapaneseNumbersOnAnyPc()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        Assert.AreEqual("1.234,5", 1234.5.ToString("N1"), "(前提) de-DE では小数点がカンマ");

        var culture = AppCulture.Apply();
        Assert.AreEqual("ja-JP", CultureInfo.CurrentCulture.Name);
        Assert.IsFalse(culture.UseUserOverride, "Windows の地域設定で変えた小数点・桁区切りを使っています");
        Assert.AreEqual("1,234.5", 1234.5.ToString("N1"));
        Assert.AreEqual(1.5, double.Parse("1.5"));

        string? onWorker = null;
        var thread = new Thread(() => onWorker = CultureInfo.CurrentCulture.Name);
        thread.Start();
        thread.Join();
        Assert.AreEqual("ja-JP", onWorker, "あとから作られるスレッドが Windows の地域設定のままです");
    }

    /// <summary>起動の最初 (ログより前) に、地域設定と画面の言語を決めること。</summary>
    [TestMethod]
    public void TheAppSetsTheCultureFirst()
    {
        string ctor = TestSource.MethodBody(TestSource.Read("Graphics_r1", "App.xaml.cs"), "public App()");
        int culture = ctor.IndexOf("AppCulture.Apply();", System.StringComparison.Ordinal);
        int wpf = ctor.IndexOf("AppCulture.ApplyToWpf();", System.StringComparison.Ordinal);
        int log = ctor.IndexOf("AppLog.Initialize();", System.StringComparison.Ordinal);
        Assert.IsTrue(culture >= 0 && wpf >= 0, "起動時に地域設定を決めていません");
        Assert.IsTrue(culture < log && wpf < log, "地域設定を決める前に、ほかの処理が走っています");
    }
}
