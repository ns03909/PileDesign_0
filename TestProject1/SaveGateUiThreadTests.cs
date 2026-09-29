using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TestProject1;

/// <summary>
/// 同期の書き出し (<see cref="FileOperationService.ReplaceAtomically"/>) が保存先の排他を待つとき、
/// 画面のスレッドでは待ち続けないこと。
///
/// 計算書・DXF・CSV などの書き出しは画面のスレッドから呼ばれる。同じ保存先へ別の書き込みが走っていると、
/// 以前は <c>Wait()</c> で終わるまで画面が固まった。いまは上限を超えたら理由を示して失敗させる。
/// バックグラウンドのスレッドは従来どおり待つ。
/// </summary>
[TestClass]
[DoNotParallelize]
public class SaveGateUiThreadTests
{
    private Func<bool> _isUiThread = null!;
    private TimeSpan _timeout;

    [TestInitialize]
    public void Init() { _isUiThread = FileOperationService.IsUiThread; _timeout = FileOperationService.UiThreadGateTimeout; }

    [TestCleanup]
    public void Cleanup() { FileOperationService.IsUiThread = _isUiThread; FileOperationService.UiThreadGateTimeout = _timeout; }

    private static string NewTarget(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "pd_gate_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "out.csv");
        File.WriteAllText(target, "old");
        return target;
    }

    /// <summary><b>本題。</b> 画面のスレッドでは、上限を超えたら理由を示して失敗し、保存先は前の内容のまま。</summary>
    [TestMethod]
    public void OnTheUiThread_ABusyGateFailsAfterTheTimeout()
    {
        string target = NewTarget(out string folder);
        var gate = FileOperationService.GateFor(target);
        gate.Wait();   // 別の書き込みが走っている
        try
        {
            FileOperationService.IsUiThread = () => true;
            FileOperationService.UiThreadGateTimeout = TimeSpan.FromMilliseconds(200);

            var ex = Assert.ThrowsException<IOException>(() =>
                FileOperationService.WriteAtomically(target, s => s.Write("new"u8)));
            StringAssert.Contains(ex.Message, "別の保存がまだ終わっていない");
            Assert.AreEqual("old", File.ReadAllText(target), "失敗したのに保存先が書き換わった");
            var files = Directory.GetFiles(folder);
            TestSource.AssertScanned(files.Length, 1, "書き出し先のフォルダ");   // 保存先そのものは必ずある
            Assert.AreEqual(1, files.Length, "一時ファイルが残っている");
        }
        finally
        {
            gate.Release();
            Directory.Delete(folder, true);
        }
    }

    /// <summary>空いていれば画面のスレッドでもそのまま書く。</summary>
    [TestMethod]
    public void OnTheUiThread_AFreeGateWritesImmediately()
    {
        string target = NewTarget(out string folder);
        try
        {
            FileOperationService.IsUiThread = () => true;
            FileOperationService.WriteAtomically(target, s => s.Write("new"u8));
            Assert.AreEqual("new", File.ReadAllText(target));
        }
        finally { Directory.Delete(folder, true); }
    }

    /// <summary>バックグラウンドのスレッドは、上限を超えても待ち続け、空いたら書く (従来どおり)。</summary>
    [TestMethod]
    public void OffTheUiThread_WaitsUntilTheGateIsFree()
    {
        string target = NewTarget(out string folder);
        var gate = FileOperationService.GateFor(target);
        gate.Wait();
        try
        {
            FileOperationService.IsUiThread = () => false;
            FileOperationService.UiThreadGateTimeout = TimeSpan.FromMilliseconds(50);
            var write = Task.Run(() => FileOperationService.WriteAtomically(target, s => s.Write("new"u8)));
            Thread.Sleep(300);   // 上限を過ぎても失敗しない
            Assert.IsFalse(write.IsCompleted, "排他が空く前に書いている");
            gate.Release();
            Assert.IsTrue(write.Wait(TimeSpan.FromSeconds(10)), "排他が空いても書かない");
            Assert.AreEqual("new", File.ReadAllText(target));
        }
        finally
        {
            if (gate.CurrentCount == 0) gate.Release();
            Directory.Delete(folder, true);
        }
    }
}
