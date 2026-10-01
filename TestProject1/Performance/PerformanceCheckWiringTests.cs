using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1.Performance
{
    /// <summary>
    /// 性能の確認の配線: 基準があり、リリースの確認が性能の確認を行う。測定そのものは全体テストでは走らせない
    /// (<see cref="PerformanceBaselineTests"/> は PERF_CHECK=1 のときだけ動く)。
    /// </summary>
    [TestClass]
    public class PerformanceCheckWiringTests
    {
        [TestMethod]
        public void TheBaselineExists_AndCoversTheRepresentativeModels()
        {
            var baseline = JsonSerializer.Deserialize<PerformanceBaselineTests.Baseline>(File.ReadAllText(PerformanceBaselineTests.BaselinePath));
            Assert.IsNotNull(baseline);
            Assert.IsFalse(string.IsNullOrWhiteSpace(baseline!.Machine), "基準を取った PC の名前がありません");
            TestSource.AssertScanned(baseline.Cases.Count, 4, "性能の基準の項目");
            foreach (var (name, m) in baseline.Cases)
            {
                Assert.IsTrue(m.Seconds > 0, $"{name}: 時間");
                Assert.IsTrue(m.PeakWorkingSetMb > 0, $"{name}: 最大メモリ");
            }
            foreach (var kind in new[] { "水平解析", "計算書の出力", "保存" })
                Assert.IsTrue(baseline.Cases.Keys.Any(k => k.StartsWith(kind)), $"「{kind}」の基準がありません");
        }

        [TestMethod]
        public void TheReleaseCheckRunsThePerformanceCheck()
        {
            string release = File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "release-check.ps1"));
            StringAssert.Contains(release, "perf-check.ps1");
            StringAssert.Contains(release, "SkipPerformance");

            string path = Path.Combine(TestSource.Dir(), "tools", "perf-check.ps1");
            var raw = File.ReadAllBytes(path);
            Assert.IsTrue(raw.Length > 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF,
                "perf-check.ps1 が BOM 付き UTF-8 ではありません (Windows PowerShell 5.1 で日本語が化けます)");
            string script = File.ReadAllText(path);
            StringAssert.Contains(script, "PerformanceBaselineTests");
            StringAssert.Contains(script, "PERF_CHECK");
        }
    }
}
