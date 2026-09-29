using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestProject1;

/// <summary>
/// 画面の変換器の逆変換・自動保存の重なり・解析結果の控えの失敗の知らせ。
/// </summary>
[TestClass]
[DoNotParallelize]
public class RobustnessFollowUpTests
{
    /// <summary>
    /// 逆変換を使わない変換器は、例外ではなく「書き戻さない」を返すこと。
    /// 以前は NotImplementedException を投げ、束縛を双方向に変えると画面の操作で例外になった。
    /// </summary>
    [TestMethod]
    public void DoubleLessThanConverter_ConvertBackDoesNotThrow()
    {
        var converter = new DoubleLessThanConverter();
        Assert.AreSame(System.Windows.Data.Binding.DoNothing,
            converter.ConvertBack(true, typeof(double), "1", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 前の自動保存の書き出しが終わっていなければ、次の発火は見送る (失敗としては数えない)。
    /// Tick は async void で、書き出しが間隔より長くかかると重なり、同じ中身の自動保存が余分に増えた。
    /// </summary>
    [TestMethod]
    public void AutoSaveTick_IsSkippedWhileThePreviousOneIsRunning()
    {
        var auto = new AutoSaveService(new FileOperationService(new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.Preserve }));
        try
        {
            auto.Start(null, new InputModel(), null);
            auto.LiveStateProvider = () => throw new InvalidOperationException("呼ばれてはいけない");
            int raised = 0;
            auto.AutoSaveCompleted += (_, _) => raised++;

            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(AutoSaveService).GetField("_tickInProgress", flags)!.SetValue(auto, 1);   // 前の書き出しが進行中
            typeof(AutoSaveService).GetMethod("OnAutoSaveTimer", flags)!.Invoke(auto, [null, EventArgs.Empty]);

            Assert.AreEqual(0, raised, "前の書き出しの最中に、次の自動保存を始めている");
            Assert.AreEqual(0, auto.ConsecutiveFailures, "見送りを失敗として数えている");
        }
        finally { auto.Stop(); }
    }

    /// <summary>解析結果の控えを作れなかったとき、知らせに理由を書くこと (以前は「ログに記録しています」だけ)。</summary>
    [TestMethod]
    public void SnapshotFailureMessage_IncludesTheReason()
    {
        string text = MainWindowViewModel.DescribeResultSnapshotFailure(new InvalidOperationException("循環参照を複製できません\n詳細"));
        StringAssert.Contains(text, "理由: 循環参照を複製できません");
        Assert.IsFalse(text.Contains("詳細\n"), "例外の 2 行目以降まで出している");
        Assert.IsFalse(MainWindowViewModel.DescribeResultSnapshotFailure().Contains("理由:"));
    }
}
