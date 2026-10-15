using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace TestProject1
{
    /// <summary>
    /// 表の列の幅と並びを覚える (2026-10-16 のレビュー)。
    /// 保存と復元の仕組み (<see cref="LayoutService"/>) は以前からあったが、どこからも呼ばれていなかった。
    /// </summary>
    [TestClass]
    public class ColumnLayoutMemoryTests
    {
        private static readonly Type Host = typeof(PileDesign.Views.MainWindow);

        [TestMethod]
        public void TheKey_IsWindowNameAndColumnCount_AndOnlyForNamedGrids()
        {
            Assert.AreEqual("MainWindow.DataGridPileLayout.12", EnhancedDataGrid.ColumnLayoutKeyFor(Host, "DataGridPileLayout", 12, true));
            Assert.IsNull(EnhancedDataGrid.ColumnLayoutKeyFor(Host, "", 12, true), "名前の無い表を覚えています");
            Assert.IsNull(EnhancedDataGrid.ColumnLayoutKeyFor(Host, "G", 12, false), "覚えない設定の表を覚えています");
            Assert.IsNull(EnhancedDataGrid.ColumnLayoutKeyFor(Host, "G", 0, true), "列の無い (動的に組み立てる) 表を覚えています");
            Assert.IsNull(EnhancedDataGrid.ColumnLayoutKeyFor(typeof(System.Windows.Window), "G", 12, true),
                "アプリの外の型 (浮動ウィンドウなど) に載った表を覚えています");
        }

        /// <summary>変えた幅と並びを閉じるときに保存し、次に開いた表に当てる。元に戻すと覚えていた分を消す。</summary>
        [TestMethod]
        public void ChangedWidths_AreRestored_AndResetForgetsThem()
        {
            string file = Path.Combine(Path.GetTempPath(), $"pd_columns_{Guid.NewGuid():N}.json");
            OnSta(() =>
            {
                var before = EnhancedDataGrid.ColumnLayoutStore;
                try
                {
                    EnhancedDataGrid.ColumnLayoutStore = new LayoutService(file);
                    var first = Grid();
                    first.RestoreColumnLayoutOnce(Host);
                    Assert.AreEqual("MainWindow.G.3", first.ColumnLayoutKey);
                    first.SaveColumnLayout();
                    Assert.IsFalse(File.Exists(file), "既定のままの表で設定を書いています");

                    first.Columns[1].Width = new DataGridLength(120);
                    first.Columns[2].DisplayIndex = 0;
                    first.SaveColumnLayout();

                    // ファイルから読み直して (次の起動で) 当たる
                    EnhancedDataGrid.ColumnLayoutStore = new LayoutService(file);
                    var second = Grid();
                    second.RestoreColumnLayoutOnce(Host);
                    Assert.AreEqual(120, second.Columns[1].Width.Value, "変えた幅が戻っていません");
                    Assert.AreEqual(0, second.Columns[2].DisplayIndex, "変えた並びが戻っていません");
                    Assert.IsTrue(second.IsColumnLayoutChanged());

                    second.ResetColumnLayout();
                    Assert.AreEqual(50, second.Columns[1].Width.Value, "元に戻していません");
                    CollectionAssert.AreEqual(new[] { 0, 1, 2 }, second.Columns.Select(c => c.DisplayIndex).ToArray());
                    second.SaveColumnLayout();
                    Assert.IsFalse(new LayoutService(file).HasDataGridSettings("MainWindow.G.3"), "元に戻した表の設定が残っています");
                }
                finally
                {
                    EnhancedDataGrid.ColumnLayoutStore = before;
                    File.Delete(file);
                }
            });
        }

        /// <summary>保存先はアプリの起動時にだけ立てる。テストや起動の確認で利用者の設定ファイルを書き換えない。</summary>
        [TestMethod]
        public void TheStore_IsSetOnlyAtAppStartup()
        {
            string app = TestSource.Read("Graphics_r1", "App.xaml.cs");
            StringAssert.Contains(app, "EnhancedDataGrid.ColumnLayoutStore = new PileDesign.Services.LayoutService();");
            var files = Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).ToArray();
            TestSource.AssertScanned(files.Length, 100, "本体のソース");
            int sets = files.Sum(f => Regex.Matches(File.ReadAllText(f), @"ColumnLayoutStore\s*=\s*new").Count);
            Assert.AreEqual(1, sets, "列の設定の保存先を App の起動以外でも立てています");
        }

        /// <summary>列を動的に組み立てる表 (解析結果テーブル) は覚えない。</summary>
        [TestMethod]
        public void TheResultTable_DoesNotRememberColumns()
        {
            string xaml = TestSource.Read("Graphics_r1", "Views", "TableWindow.xaml");
            var tag = Regex.Match(xaml, @"<common:EnhancedDataGrid x:Name=""ResultGrid"".*?>", RegexOptions.Singleline).Value;
            StringAssert.Contains(tag, @"RemembersColumnLayout=""False""");
        }

        private static EnhancedDataGrid Grid()
        {
            var grid = new EnhancedDataGrid { Name = "G" };
            foreach (var h in new[] { "杭No", "深さ", "変位" })
                grid.Columns.Add(new DataGridTextColumn { Header = h, Width = new DataGridLength(50) });
            return grid;
        }

        private static void OnSta(Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            if (timedOut) Assert.Inconclusive("STA スレッドが時間内に終わりませんでした");
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }
    }
}
