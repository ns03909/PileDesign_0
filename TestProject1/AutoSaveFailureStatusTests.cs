using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1;

/// <summary>
/// 自動保存の失敗が、画面の表示 (連続の回数・次の試行・理由) と一致すること。
///
/// <list type="bullet">
/// <item>最新の作業の状態を取れなかったとき、以前は開始したときの参照で代用して<b>前の作業</b>を書き、
///   「成功」と出していた。いまは失敗として数えて知らせる (緊急保存だけは代用を許す)。</item>
/// <item>失敗の表示は時刻だけで、何回続いているか、次にいつ試すかが分からなかった。</item>
/// </list>
/// </summary>
[TestClass]
[DoNotParallelize]
public class AutoSaveFailureStatusTests
{
    private static AutoSaveService NewService() => new(new FileOperationService(new JsonSerializerOptions
    {
        ReferenceHandler = ReferenceHandler.Preserve,
    }));

    private static void Tick(AutoSaveService auto) => typeof(AutoSaveService)
        .GetMethod("OnAutoSaveTimer", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(auto, [null, EventArgs.Empty]);

    /// <summary><b>本題。</b> 最新の状態を取れなければ、前の作業を書かずに失敗として知らせ、次の試行を添える。</summary>
    [TestMethod]
    public void AFailingStateProvider_IsReportedAsAFailure_NotSavedFromStaleRefs()
    {
        var auto = NewService();
        try
        {
            auto.Start(null, new InputModel(), null);   // 開始したときの参照 (代用されてはいけない)
            auto.LiveStateProvider = () => throw new InvalidOperationException("状態を取れない");
            AutoSaveEventArgs? got = null;
            auto.AutoSaveCompleted += (_, e) => got = e;

            Tick(auto);

            Assert.IsNotNull(got, "失敗が知らされていない");
            Assert.IsFalse(got!.Success, "前の作業で代用して「成功」と出している");
            Assert.AreEqual(1, got.ConsecutiveFailures);
            Assert.AreEqual(1, auto.ConsecutiveFailures);
            Assert.IsNotNull(got.NextAttemptAt, "次の試行の時刻が添えられていない");
            StringAssert.Contains(got.ErrorMessage, "取得できませんでした");
        }
        finally { auto.Stop(); }
    }

    /// <summary>緊急保存 (落ちる直前) は、何も残さないより残すほうがよいので、開始したときの参照で代用する。</summary>
    [TestMethod]
    public void EmergencySave_StillFallsBackToCapturedRefs()
    {
        var auto = NewService();
        string? path = null;
        try
        {
            auto.Start(Path.Combine(Path.GetTempPath(), "EmergencyFallback_" + Guid.NewGuid().ToString("N")[..8] + ".pdj"),
                new InputModel(), null);
            auto.LiveStateProvider = () => throw new InvalidOperationException("状態を取れない");
            path = auto.TryEmergencyAutoSave();
            Assert.IsNotNull(path, "緊急保存が代用せずに諦めている");
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            auto.Stop();
            if (path != null && File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>止まっているときは次の試行が無い。動いていれば直近の発火 (開始) から間隔ぶんあと。</summary>
    [TestMethod]
    public void NextAttempt_FollowsTheTimer()
    {
        var auto = NewService();
        Assert.IsNull(auto.NextAttemptAt, "止まっているのに次の試行がある");
        var before = DateTime.Now;
        auto.Start(null, new InputModel(), null);
        try
        {
            var next = auto.NextAttemptAt;
            Assert.IsNotNull(next);
            Assert.IsTrue(next!.Value >= before.AddMinutes(auto.AutoSaveIntervalMinutes).AddSeconds(-1));
        }
        finally { auto.Stop(); }
        Assert.IsNull(auto.NextAttemptAt);
    }

    /// <summary>表示の文面。失敗は連続の回数と次の試行 (止まっていればそう書く)、説明は理由まで書く。</summary>
    [TestMethod]
    public void StatusText_ShowsCountNextAttemptAndReason()
    {
        var failed = new AutoSaveEventArgs
        {
            Success = false, Timestamp = new DateTime(2026, 9, 29, 10, 0, 5), ConsecutiveFailures = 2,
            NextAttemptAt = new DateTime(2026, 9, 29, 10, 3, 5), ErrorMessage = "ディスクがいっぱいです",
        };
        Assert.AreEqual("自動保存失敗 (10:00:05・2 回連続・次は 10:03 に再試行)", MainWindowViewModel.DescribeAutoSaveStatus(failed));
        StringAssert.Contains(MainWindowViewModel.DescribeAutoSaveToolTip(failed), "ディスクがいっぱいです");
        StringAssert.Contains(MainWindowViewModel.DescribeAutoSaveToolTip(failed), "10:03");

        failed.NextAttemptAt = null;
        StringAssert.Contains(MainWindowViewModel.DescribeAutoSaveStatus(failed), "停止中");

        var ok = new AutoSaveEventArgs { Success = true, Timestamp = new DateTime(2026, 9, 29, 10, 0, 5) };
        Assert.AreEqual("自動保存: 10:00:05", MainWindowViewModel.DescribeAutoSaveStatus(ok));
    }
}
