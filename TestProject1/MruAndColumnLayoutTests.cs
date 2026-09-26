using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 最近使ったファイルの一覧と、表の列設定の復元。
    /// </summary>
    [TestClass]
    public class MruAndColumnLayoutTests
    {
        /// <summary>
        /// 起動時に見つからないファイルを一覧から消さず、「現在アクセスできません」と示すこと。
        /// 以前は消して保存していたので、ネットワークドライブなどが一時的に使えないだけで履歴が恒久的に消えた。
        /// </summary>
        [TestMethod]
        public void MissingFilesStayInTheListMarkedInaccessible()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"Mru_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                string existing = Path.Combine(dir, "a.pdj");
                string missing = Path.Combine(dir, "gone", "b.pdj");
                File.WriteAllText(existing, "{}");
                string list = Path.Combine(dir, "mru.json");

                var first = new MruService(list);
                first.AddFile(missing);
                first.AddFile(existing);

                var reopened = new MruService(list);
                Assert.AreEqual(2, reopened.Items.Count, "見つからないファイルを起動時に一覧から消しています");
                var gone = reopened.Items.Single(i => i.FilePath == Path.GetFullPath(missing));
                Assert.IsFalse(gone.IsAccessible);
                StringAssert.Contains(gone.AccessNote, "現在アクセスできません");
                Assert.AreEqual("", reopened.Items.Single(i => i.FilePath == Path.GetFullPath(existing)).AccessNote);
                Assert.IsFalse(File.ReadAllText(list).Contains("IsAccessible", StringComparison.Ordinal), "表示用の印を一覧ファイルに書いています");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }

            // 開けなかったときに黙って消さず、利用者に選んでもらうこと
            string src = TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.cs");
            StringAssert.Contains(TestSource.MethodBody(src, "public async Task OpenFromMru(string filePath)"), "AskToRemoveMissingFromMru(");
        }

        private static void OnSta(Action action)
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            Assert.IsFalse(timedOut);
            if (ex != null) throw new AssertFailedException(ex.ToString());
        }

        private static DataGrid Grid(params string[] headers)
        {
            var grid = new DataGrid { AutoGenerateColumns = false };
            foreach (var h in headers) grid.Columns.Add(new DataGridTextColumn { Header = h, Width = new DataGridLength(50) });
            return grid;
        }

        private static DataGridColumnSetting Setting(int index, string header, double width, int displayIndex)
            => new() { Index = index, Header = header, Width = width, DisplayIndex = displayIndex };

        /// <summary>
        /// 列設定は列番号と見出しの両方が一致する列にだけ当てること。
        /// 以前は列数が同じなら列番号だけで当てたので、更新で列の中身が入れ替わると別の列に幅と表示順が当たった。
        /// </summary>
        [TestMethod]
        public void ColumnSettingsAreMatchedByHeaderAsWellAsNumber()
        {
            OnSta(() =>
            {
                // 保存したときの並び: 杭No / 深さ / 変位 → いまは 杭No / 変位 / 深さ (列数は同じ)
                var saved = new[] { Setting(0, "杭No", 40, 0), Setting(1, "深さ", 120, 2), Setting(2, "変位", 90, 1) };
                var grid = Grid("杭No", "変位", "深さ");
                var defaultOrder = grid.Columns.Select(c => c.DisplayIndex).ToArray();

                Assert.IsTrue(LayoutService.ApplyColumnSettings(saved, grid));
                Assert.AreEqual(40, grid.Columns[0].Width.Value, "見出しの一致する列に幅が当たっていません");
                Assert.AreEqual(50, grid.Columns[1].Width.Value, "見出しの違う列に、別の列の幅が当たっています");
                Assert.AreEqual(50, grid.Columns[2].Width.Value, "見出しの違う列に、別の列の幅が当たっています");
                CollectionAssert.AreEqual(defaultOrder, grid.Columns.Select(c => c.DisplayIndex).ToArray(),
                    "一部の列しか一致しないのに表示順を動かしています");

                // 全列が一致すれば、幅も表示順も当てる
                var same = Grid("杭No", "深さ", "変位");
                Assert.IsTrue(LayoutService.ApplyColumnSettings(saved, same));
                CollectionAssert.AreEqual(new[] { 0, 2, 1 }, same.Columns.Select(c => c.DisplayIndex).ToArray());
                Assert.AreEqual(120, same.Columns[1].Width.Value);

                Assert.IsFalse(LayoutService.ApplyColumnSettings(saved, Grid("A", "B", "C")), "一致する列が無いのに当てたことにしています");
            });
        }
    }
}
