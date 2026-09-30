using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;
using System.Windows;

namespace TestProject1;

/// <summary>書き出しに失敗したとき、中身を持ったまま保存先を選び直してすぐに書き出し直せる。</summary>
[TestClass]
[DoNotParallelize]
public class ExportRetryTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() { MessageService.IsUnattended = _unattended; MessageService.UnattendedAnswer = null; }

    /// <summary><b>本題。</b> 使用中で書けない → 「はい」→ 別の場所を選ぶ → そこへ書ける。元の場所のファイルは壊れない。</summary>
    [TestMethod]
    public void AFailedExport_CanBeRetriedAtAnotherPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"pd_retry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string busy = Path.Combine(dir, "表.csv"), other = Path.Combine(dir, "表-2");
        File.WriteAllText(busy, "old");
        try
        {
            MessageService.UnattendedAnswer = (_, button) => button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : null;
            string? chosenFrom = null;
            using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                string? written = ExportFile.TryWriteText(busy, "new", ExportFormat.Csv, "試験の CSV",
                    (previous, _) => { chosenFrom = previous; return other; });
                Assert.AreEqual(other + ".csv", written, "選び直した場所へ書いていません (拡張子も補う)");
            }
            Assert.AreEqual(busy, chosenFrom, "選び直しの初期値が前の場所になっていません");
            Assert.AreEqual("old", File.ReadAllText(busy));
            Assert.AreEqual("new", File.ReadAllText(other + ".csv"));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>「いいえ」・選び直しをやめたら、何度も訊かずに終わる。</summary>
    [TestMethod]
    public void DecliningTheRetry_Stops()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pd_retry_{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "old");
        try
        {
            int asked = 0;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.IsNull(ExportFile.TryWriteText(path, "new", ExportFormat.Csv, "試験の CSV", (_, _) => { asked++; return null; }));
                MessageService.UnattendedAnswer = (_, _) => MessageBoxResult.Yes;
                Assert.IsNull(ExportFile.TryWriteText(path, "new", ExportFormat.Csv, "試験の CSV", (_, _) => { asked++; return null; }));
            }
            Assert.AreEqual(1, asked, "「いいえ」でも選び直させたか、やめたのにもう一度選ばせています");
        }
        finally { File.Delete(path); }
    }
}
