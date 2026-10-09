using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;

namespace TestProject1
{
    /// <summary>
    /// 表のセルを触っただけで、表示用に丸めた値が入力に書き戻されないこと。
    ///
    /// <para>数値の列は StringFormat (N1・0.000 など) で丸めて表示している。WPF の表は編集用の欄にも同じ書式の
    /// 文字を入れ、確定するとその文字を読み戻して書き込むので、セルに入って何も打たずに抜けただけで
    /// 123.456 が 123.5 に変わっていた。以前は沈下用土層の表と荷重面等価径の列だけが個別に対策していた。
    /// いまは <see cref="EnhancedDataGrid"/> がすべての表で行う。</para>
    /// </summary>
    [TestClass]
    public class GridRoundTrip20261009Tests
    {
        public sealed class Inner { public double Depth { get; set; } }

        public sealed class Row
        {
            public double Value { get; set; }
            public double? Optional { get; set; }
            public int Count { get; set; }
            public Inner Inner { get; set; } = new();
            public double LostFocusValue { get; set; }
        }

        private static T? FindVisual<T>(DependencyObject root, Func<T, bool>? pred = null) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t && (pred == null || pred(t))) return t;
                if (FindVisual(child, pred) is { } found) return found;
            }
            return null;
        }

        private static DataGridTextColumn Column(string path, string format, UpdateSourceTrigger trigger = UpdateSourceTrigger.Default)
            => new() { Header = path, Binding = new Binding(path) { StringFormat = format, UpdateSourceTrigger = trigger, TargetNullValue = "" } };

        /// <summary>
        /// 表を作り、指定のセルで編集を始め、<paramref name="type"/> を (null でなければ) 打ってから確定する。
        /// 確定のあとの行を返す。
        /// </summary>
        private static Row EditCell(Row row, string path, string format, string? type, UpdateSourceTrigger trigger = UpdateSourceTrigger.Default)
        {
            Exception? inner = null;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var rows = new ObservableCollection<Row> { row };
                var grid = new EnhancedDataGrid { AutoGenerateColumns = false, ItemsSource = rows, CanUserAddRows = false };
                var column = Column(path, format, trigger);
                grid.Columns.Add(column);
                // 画面の多くは確定の処理で書き戻しを自分で行う (GroundWindow・PileBodyWindow・LoadCaseWindow など)。
                // 書き戻しは欄の文字が変わっていなくても行われるので、丸めた表示がそのまま入る。それと同じにする
                grid.CellEditEnding += (_, e) =>
                {
                    if (e.EditAction == DataGridEditAction.Commit)
                        BindingOperations.GetBindingExpression(e.EditingElement, TextBox.TextProperty)?.UpdateSource();
                };
                var w = new Window
                {
                    Content = grid, ShowActivated = false, Width = 400, Height = 300,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                w.Show();
                try
                {
                    w.UpdateLayout();
                    grid.CurrentCell = new DataGridCellInfo(row, column);
                    Assert.IsTrue(grid.BeginEdit(), "前提: セルの編集を始められる");
                    var box = FindVisual<TextBox>(grid);
                    Assert.IsNotNull(box, "前提: 編集用の欄がある");
                    if (type != null) box!.Text = type;
                    Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true), "セルを確定できません");
                    grid.CommitEdit(DataGridEditingUnit.Row, true);
                }
                catch (Exception ex) { inner = ex; }
                finally { w.Close(); }
            }, out bool timedOut);
            if (timedOut) Assert.Inconclusive("60 秒以内に終わらなかったためスキップ");
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            if (inner != null) throw inner;
            return row;
        }

        [DataTestMethod]
        [DataRow("N1", DisplayName = "N1 (小数 1 桁・桁区切り)")]
        [DataRow("{0:0.000}", DisplayName = "0.000")]
        [DataRow("N0", DisplayName = "N0 (整数・桁区切り)")]
        public void UntouchedCell_KeepsTheUnroundedValue(string format)
        {
            var row = EditCell(new Row { Value = 12345.6789 }, nameof(Row.Value), format, type: null);
            Assert.AreEqual(12345.6789, row.Value, 0.0, "触っただけのセルで、表示用に丸めた値が書き戻されました");
        }

        [TestMethod]
        public void UntouchedNullableCell_KeepsTheValue_AndNullStaysNull()
        {
            var row = EditCell(new Row { Optional = 0.123456 }, nameof(Row.Optional), "N2", type: null);
            Assert.AreEqual(0.123456, row.Optional!.Value, 0.0, "null を許す列で、丸めた値が書き戻されました");

            var empty = EditCell(new Row { Optional = null }, nameof(Row.Optional), "N2", type: null);
            Assert.IsNull(empty.Optional, "空欄のセルに値が入りました");
        }

        [TestMethod]
        public void UntouchedCell_WithANestedPath_KeepsTheValue()
        {
            var row = new Row(); row.Inner.Depth = -3.14159;
            EditCell(row, "Inner.Depth", "{0:+0.00;-0.00;+0.00}", type: null);
            Assert.AreEqual(-3.14159, row.Inner.Depth, 0.0, "入れ子のパスの列で、丸めた値が書き戻されました");
        }

        [TestMethod]
        public void UntouchedCell_BoundOnLostFocus_KeepsTheValue()
        {
            var row = EditCell(new Row { LostFocusValue = 7.77777 }, nameof(Row.LostFocusValue), "N1", type: null, UpdateSourceTrigger.LostFocus);
            Assert.AreEqual(7.77777, row.LostFocusValue, 0.0, "外れたときに書き戻す列で、丸めた値が書き戻されました");
        }

        [TestMethod]
        public void IntegerWithThousandsSeparator_UntouchedCommitDoesNotFail()
        {
            // N0 の「12,345」は整数としては読めない (桁区切り)。以前は触っただけで確定に失敗した
            var row = EditCell(new Row { Count = 12345 }, nameof(Row.Count), "N0", type: null);
            Assert.AreEqual(12345, row.Count);
        }

        [DataTestMethod]
        [DataRow("200.25", 200.25, DisplayName = "別の値を打つ")]
        [DataRow("123.4", 123.4, DisplayName = "表示 (123.5) と違う近い値を打つ")]
        [DataRow("0", 0.0, DisplayName = "0 を打つ")]
        public void TypedValue_IsWritten(string typed, double expected)
        {
            var row = EditCell(new Row { Value = 123.456 }, nameof(Row.Value), "N1", type: typed);
            Assert.AreEqual(expected, row.Value, 1e-12, "打ち込んだ値が書き込まれていません");
        }

        [TestMethod]
        public void RetypingTheDisplayedText_KeepsTheOriginalValue()
        {
            // 表示と同じ文字を打つのは、値を変えるつもりがないときと区別できない。元の値を残す (仕様)
            var row = EditCell(new Row { Value = 123.456 }, nameof(Row.Value), "N1", type: "123.5");
            Assert.AreEqual(123.456, row.Value, 0.0);
        }

        /// <summary>実際の画面 (地盤の土層の表、N 値は N1 で表示) で、セルを触っただけでは N 値が丸まらない。</summary>
        [TestMethod]
        public void GroundWindow_UntouchedNValueCell_KeepsTheValue()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }
            double after = double.NaN;
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                var main = new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = model };
                var vm = new PileDesign.ViewModels.GroundLayerViewModel(main);
                var w = new PileDesign.Views.GroundWindow
                {
                    DataContext = vm, ShowActivated = false, WindowState = WindowState.Normal, Width = 1600, Height = 1000,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                w.Show();
                try
                {
                    w.UpdateLayout();
                    var grid = w.DataGridGroundLayer;
                    var layer = grid.Items.OfType<PileDesign.Models.InputData.GroundLayerInput>().FirstOrDefault();
                    Assert.IsNotNull(layer, "前提: 土層がある");
                    layer!.NValue = 12.345;   // 表示 (N1) は 12.3
                    var column = grid.Columns.OfType<DataGridTextColumn>()
                        .First(c => (c.Binding as Binding)?.Path.Path == "NValue");
                    grid.CurrentCell = new DataGridCellInfo(layer, column);
                    Assert.IsTrue(grid.BeginEdit(), "前提: N 値のセルは編集できる");
                    Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                    after = layer.NValue;
                }
                finally { w.Close(); }
            }, out bool timedOut);
            if (timedOut) { Assert.Inconclusive("60 秒以内に終わらなかったためスキップ"); return; }
            if (captured != null) Assert.Fail($"{captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");
            Assert.AreEqual(12.345, after, 0.0, "地盤の表で、触っただけの N 値が表示 (12.3) に丸められました");
        }

        [TestMethod]
        public void ExactText_RoundTrips()
        {
            var ci = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");
            foreach (double v in new[] { 0.1 + 0.2, 1.0 / 3.0, 1e-9, 123456789.123, -0.0001 })
            {
                string? text = CellDisplayRoundTrip.ExactText(v, ci);
                Assert.IsNotNull(text);
                Assert.AreEqual(v, double.Parse(text!, ci), 0.0, $"{v:R} が {text} になり、読み戻すと変わります");
            }
            Assert.IsNull(CellDisplayRoundTrip.ExactText(double.NaN, ci), "数値でない値は扱わない");
            Assert.IsNull(CellDisplayRoundTrip.ExactText("文字", ci));
        }

        // ── 走査: 入力の表で丸める書式の列は、すべて EnhancedDataGrid の中にある ──

        private static readonly Regex StringFormatInBinding = new(@"StringFormat\s*=", RegexOptions.Compiled);

        /// <summary>
        /// 書き込める列で書式が丸めるもの (DataGridTextColumn の Binding に StringFormat) は、
        /// すべて <see cref="EnhancedDataGrid"/> の中にあること。素の DataGrid に置くと、触っただけで丸めた値が書き戻される。
        /// また、編集用のテンプレートの欄 (CellEditingTemplate の TextBox) に丸める書式を付けないこと
        /// (表の確定とは別に書き戻すので、この仕組みでは守れない)。
        /// </summary>
        [TestMethod]
        public void EveryEditableRoundingColumn_IsInAnEnhancedDataGrid()
        {
            var files = Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.xaml", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                .ToList();

            int scanned = 0;
            var problems = new List<string>();
            foreach (var file in files)
            {
                XDocument doc;
                try { doc = XDocument.Load(file, LoadOptions.SetLineInfo); }
                catch (System.Xml.XmlException) { continue; }

                foreach (var col in doc.Descendants().Where(e => e.Name.LocalName == "DataGridTextColumn"))
                {
                    string binding = (string?)col.Attribute("Binding") ?? "";
                    if (!StringFormatInBinding.IsMatch(binding)) continue;
                    if (IsTrue(col, "IsReadOnly") || Regex.IsMatch(binding, @"Mode\s*=\s*OneWay\b")) continue;
                    var grid = col.Ancestors().FirstOrDefault(a => a.Name.LocalName is "DataGrid" or "EnhancedDataGrid");
                    if (grid == null || IsTrue(grid, "IsReadOnly")) continue;
                    scanned++;
                    if (grid.Name.LocalName != "EnhancedDataGrid")
                        problems.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)col).LineNumber} {binding}");
                }

                foreach (var tmpl in doc.Descendants().Where(e => e.Name.LocalName == "DataGridTemplateColumn.CellEditingTemplate"))
                {
                    foreach (var tb in tmpl.Descendants().Where(e => e.Name.LocalName == "TextBox"))
                    {
                        string text = (string?)tb.Attribute("Text") ?? "";
                        if (StringFormatInBinding.IsMatch(text) && !Regex.IsMatch(text, @"Mode\s*=\s*OneWay\b"))
                            problems.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)tb).LineNumber} 編集テンプレートの欄 {text}");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 60, "丸める書式で書き込める表の列");
            Assert.AreEqual(0, problems.Count,
                "丸める書式の列が守られていません (EnhancedDataGrid に置くか、書式を外す):\n" + string.Join("\n", problems));
        }

        private static bool IsTrue(XElement e, string attribute)
            => string.Equals((string?)e.Attribute(attribute), "True", StringComparison.OrdinalIgnoreCase);
    }
}
