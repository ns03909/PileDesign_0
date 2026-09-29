using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System;
using System.Linq;

namespace TestProject1;

/// <summary>
/// 操作ごとの所要時間と件数の記録 (<see cref="PerfLog"/>)。動きは変えず、集計だけを残すこと。
/// </summary>
[TestClass]
[DoNotParallelize]
public class PerfLogTests
{
    [TestInitialize] public void Init() => PerfLog.Reset();
    [TestCleanup] public void Cleanup() => PerfLog.Reset();

    [TestMethod]
    public void Records_CallsTotalAndTheSizeAtTheSlowestCall()
    {
        PerfLog.Record("描画", TimeSpan.FromMilliseconds(10), 50, "本の杭");
        PerfLog.Record("描画", TimeSpan.FromMilliseconds(30), 120, "本の杭");
        PerfLog.Record("保存", TimeSpan.FromMilliseconds(5), 50, "本の杭");

        var summary = PerfLog.Summary();
        Assert.AreEqual("描画", summary[0].Operation, "合計の多い順になっていない");
        Assert.AreEqual(2, summary[0].Calls);
        Assert.AreEqual(40, summary[0].TotalMs, 1e-9);
        Assert.AreEqual(30, summary[0].MaxMs, 1e-9);
        Assert.AreEqual(120, summary[0].CountAtMax, "最大のときの件数を残していない (遅さと件数の関係が分からない)");
    }

    [TestMethod]
    public void Measure_RecordsWhenDisposed_AndDefaultScopeIsHarmless()
    {
        using (PerfLog.Measure("結果の表の作成", 3, "要素・節点")) { }
        Assert.AreEqual(1, PerfLog.Summary().Single(s => s.Operation == "結果の表の作成").Calls);

        default(PerfLog.Scope).Dispose();   // 何も記録しない・例外にしない
        Assert.AreEqual(1, PerfLog.Summary().Count);
    }

    /// <summary>集計を書いても例外にならない (終了の処理の中で呼ぶ)。</summary>
    [TestMethod]
    public void WriteSummary_DoesNotThrow()
    {
        PerfLog.WriteSummary();   // 空
        PerfLog.Record("選択 (杭配置の表)", TimeSpan.FromMilliseconds(250), 108, "行");
        PerfLog.WriteSummary();
    }
}
