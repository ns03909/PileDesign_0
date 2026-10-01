using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using TestProject1.ConvergenceRegression;

namespace TestProject1.Performance
{
    /// <summary>
    /// 代表モデルの性能の基準: 水平解析・計算書の出力・保存の所要時間と最大メモリを測り、基準 (tools/perf-baseline.json) と比べる。
    /// 機能を足して大きく遅くなった・メモリを食うようになったことを検知する。
    ///
    /// <para>時間は PC に依存するので、基準を取った PC と同じ PC のときだけ比べる (違う PC では測って記録するだけ)。
    /// 全体テストの中では走らせない (他の試験と同じプロセスではメモリの最大値が測れず、時間もかかる)。
    /// tools/perf-check.ps1 が <c>PERF_CHECK=1</c> を付けて、このクラスだけを別のプロセスで走らせる。
    /// 基準を取り直すときは tools/perf-check.ps1 -Update。</para>
    /// </summary>
    [TestClass]
    public class PerformanceBaselineTests
    {
        /// <summary>時間の許容: 基準の 1.5 倍 + 2 秒 (反復の経路・PC の負荷で揺れる)。</summary>
        private const double TimeFactor = 1.5, TimeSlackSeconds = 2.0;

        /// <summary>メモリの許容: 基準の 1.3 倍 + 150 MB。</summary>
        private const double MemoryFactor = 1.3, MemorySlackMb = 150.0;

        internal static string BaselinePath => Path.Combine(TestSource.Dir(), "tools", "perf-baseline.json");
        internal static string ResultPath => Path.Combine(TestSource.Dir("TestProject1"), "TestResults", "perf", "perf-result.json");

        public sealed class Measurement
        {
            public double Seconds { get; set; }
            public double PeakWorkingSetMb { get; set; }
        }

        public sealed class Baseline
        {
            public string Machine { get; set; } = "";
            public int ProcessorCount { get; set; }
            public string Recorded { get; set; } = "";
            public SortedDictionary<string, Measurement> Cases { get; set; } = new(StringComparer.Ordinal);
        }

        /// <summary>処理のあいだ 50 ms ごとに作業領域の大きさを見て、最大を取る。</summary>
        private static Measurement Measure(Action action)
        {
            var process = Process.GetCurrentProcess();
            long peak = 0;
            using var stop = new CancellationTokenSource();
            var sampler = new Thread(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    process.Refresh();
                    peak = Math.Max(peak, process.WorkingSet64);
                    Thread.Sleep(50);
                }
            }) { IsBackground = true };
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var sw = Stopwatch.StartNew();
            sampler.Start();
            try { action(); }
            finally
            {
                sw.Stop();
                stop.Cancel();
                sampler.Join();
            }
            process.Refresh();
            peak = Math.Max(peak, process.WorkingSet64);
            return new Measurement { Seconds = Math.Round(sw.Elapsed.TotalSeconds, 2), PeakWorkingSetMb = Math.Round(peak / 1024.0 / 1024.0, 0) };
        }

        private static HeadlessHorizontalRunner.RunOptions Options(int l1, int l2) => new()
        {
            Level1Steps = l1,
            Level2Steps = l2,
            LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
            UseLineSearch = true,
            // 画面の既定と同じく並列で解く (利用者が待つ時間に近い)
            Parallelism = Math.Clamp(Environment.ProcessorCount, 1, 8),
        };

        [TestMethod]
        [Timeout(1800000)]
        public void RepresentativeModels_StayWithinTheBaseline()
        {
            if (Environment.GetEnvironmentVariable("PERF_CHECK") != "1")
                Assert.Inconclusive("性能の基準の確認は tools/perf-check.ps1 から走らせる (PERF_CHECK=1)。全体テストでは走らせない。");

            var cases = new SortedDictionary<string, Measurement>(StringComparer.Ordinal);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // 最大の例題: 設計例集3.8 (場所打ち鋼管コンクリート杭 42 本・地盤 9 セット)
                    MainWindowViewModel? big = null;
                    cases["水平解析 設計例集3.8 (杭42本)"] = Measure(() =>
                        big = HeadlessHorizontalRunner.RunExampleForViewModel("Example3_8_1", "PileExample3_8", Options(4, 8)));
                    big!.ApplyConcreteModelOptions();

                    string report = Path.Combine(Path.GetTempPath(), $"pd_perf_{Guid.NewGuid():N}.docx");
                    string saved = Path.Combine(Path.GetTempPath(), $"pd_perf_{Guid.NewGuid():N}.pdj");
                    try
                    {
                        cases["計算書の出力 設計例集3.8 (全章)"] = Measure(() =>
                        {
                            big.DocxOutput.SelectAllDocxSectionsCommand.Execute(null);
                            new PileDesign.Output.WordDocument(big.ResultInputModel, big.CurrentModel!, big).CreateWordDocument(big.ResultInputModel, report);
                        });
                        var service = new FileOperationService(new JsonSerializerOptions
                        {
                            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
                        });
                        cases["保存 設計例集3.8 (解析結果込み)"] = Measure(() =>
                            service.SaveProjectData(saved, big.CurrentInputModel!, big.CurrentModel!));
                    }
                    finally
                    {
                        File.Delete(report);
                        File.Delete(saved);
                    }

                    // レベル2 のステップが多い例題: 計算例10 (場所打ち杭・液状化)
                    cases["水平解析 計算例10 (L2 16ステップ)"] = Measure(() =>
                        HeadlessHorizontalRunner.RunExampleForViewModel("Example10", "PileExample10", Options(4, 16)));
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) Assert.Fail("測定中に例外:\n" + failure);

            var actual = new Baseline
            {
                Machine = Environment.MachineName,
                ProcessorCount = Environment.ProcessorCount,
                Recorded = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Cases = cases,
            };
            var json = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            File.WriteAllText(ResultPath, JsonSerializer.Serialize(actual, json));
            foreach (var (name, m) in cases) Console.WriteLine($"{name}: {m.Seconds:N1} 秒・最大 {m.PeakWorkingSetMb:N0} MB");

            if (Environment.GetEnvironmentVariable("PERF_UPDATE") == "1")
            {
                File.WriteAllText(BaselinePath, JsonSerializer.Serialize(actual, json));
                Console.WriteLine($"基準を取り直しました: {BaselinePath}");
                return;
            }

            Assert.IsTrue(File.Exists(BaselinePath), $"性能の基準がありません: {BaselinePath} (tools/perf-check.ps1 -Update で作る)");
            var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath))!;
            if (!string.Equals(baseline.Machine, actual.Machine, StringComparison.OrdinalIgnoreCase))
                Assert.Inconclusive($"基準は別の PC ({baseline.Machine}) で取ったので比べません。測った値は {ResultPath} にあります。");

            var problems = new List<string>();
            foreach (var (name, b) in baseline.Cases)
            {
                if (!cases.TryGetValue(name, out var a)) { problems.Add($"{name}: 測っていません"); continue; }
                double timeLimit = b.Seconds * TimeFactor + TimeSlackSeconds;
                double memoryLimit = b.PeakWorkingSetMb * MemoryFactor + MemorySlackMb;
                if (a.Seconds > timeLimit) problems.Add($"{name}: 時間 {b.Seconds:N1} → {a.Seconds:N1} 秒 (許容 {timeLimit:N1} 秒)");
                if (a.PeakWorkingSetMb > memoryLimit) problems.Add($"{name}: 最大メモリ {b.PeakWorkingSetMb:N0} → {a.PeakWorkingSetMb:N0} MB (許容 {memoryLimit:N0} MB)");
            }
            Assert.AreEqual(0, problems.Count, "性能が基準を超えました:\n  " + string.Join("\n  ", problems));
        }
    }
}
