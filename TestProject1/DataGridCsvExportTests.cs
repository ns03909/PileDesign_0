using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Converters;
using PileDesign.Output;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace TestProject1
{
    /// <summary>
    /// 表のコピー・CSV 出力で、画面に出ていないセルの文字が画面と同じになること。
    ///
    /// 画面に出ているセルは TextBlock の文字を読むが、仮想化で作られていないセルは
    /// バインディングから値を読み直す。以前はこの読み直しが画面と違っていた。
    ///
    /// - <c>StringFormat=N3</c> を <c>string.Format("N3", value)</c> に渡しており、値ではなく「N3」が出た。
    /// - 変換器 (Converter) を通していなかった。杭頭変位 (×1000) やヤング係数 (kN/m² → N/mm²) の列が、
    ///   画面外の行だけ内部の単位で出た。
    ///
    /// どちらも例外にならず、<b>スクロール位置で出力が変わる</b>ので気づきにくい。
    /// ここではセルを作らない (DataGrid に載せない) 列で、画面外の経路だけを通す。
    /// </summary>
    [TestClass]
    public class DataGridCsvExportTests
    {
        private sealed class Row
        {
            public string Name { get; set; } = "";
            public double Displacement { get; set; }
            public Inner Child { get; set; } = new();
            public double[] Values { get; set; } = [];
            public double Top { get; set; }
            public double Bottom { get; set; }
            public bool IsShown { get; set; } = true;
            public bool IsChecked { get; set; }
            public TestMode Mode { get; set; }
            public static double Es => 205_000;
        }

        private sealed class Inner
        {
            public double Value { get; set; }
        }

        private static void OnSta(Action action)
        {
            var ex = XamlSmokeTestSupport.RunOnStaThread(action, out bool timedOut);
            Assert.IsFalse(timedOut, "STA スレッドでの実行が時間内に終わりませんでした");
            if (ex != null) throw new AssertFailedException(ex.ToString());
        }

        [TestMethod]
        public void ShortStringFormat_IsAFormatSpecifier()
        {
            OnSta(() =>
            {
                var column = new DataGridTextColumn { Binding = new Binding(nameof(Row.Displacement)) { StringFormat = "N3" } };
                Assert.AreEqual("1234.568", DataGridCsv.GetCellValue(column, new Row { Displacement = 1234.5678 }),
                    "StringFormat=N3 が書式として当たっていません (桁区切りは除く)");
            });
        }

        [TestMethod]
        public void CompositeStringFormat_IsKept()
        {
            OnSta(() =>
            {
                var column = new DataGridTextColumn { Binding = new Binding(nameof(Row.Displacement)) { StringFormat = "{0:F1} mm" } };
                Assert.AreEqual("2.5 mm", DataGridCsv.GetCellValue(column, new Row { Displacement = 2.5 }));
            });
        }

        [TestMethod]
        public void Converter_IsAppliedBeforeStringFormat()
        {
            OnSta(() =>
            {
                // 杭頭変位の列と同じ形 (ChangWindow.xaml): m で持ち、画面は ×1000 して mm で出す
                var column = new DataGridTextColumn
                {
                    Binding = new Binding(nameof(Row.Displacement))
                    {
                        Converter = new MultiplyConverter(),
                        ConverterParameter = "1000",
                        StringFormat = "N3",
                    },
                };
                Assert.AreEqual("12.346", DataGridCsv.GetCellValue(column, new Row { Displacement = 0.0123456 }),
                    "画面外のセルで変換器が通っていません (画面と単位が違います)");
            });
        }

        [TestMethod]
        public void StressUnitConverter_IsApplied()
        {
            OnSta(() =>
            {
                // メイン画面の材料表のヤング係数の列と同じ形: kN/m² で持ち、N/mm² で出す
                var column = new DataGridTextColumn
                {
                    Binding = new Binding(nameof(Row.Displacement)) { Converter = new StressUnitConverter(), StringFormat = "N2" },
                };
                Assert.AreEqual("24000.00", DataGridCsv.GetCellValue(column, new Row { Displacement = 24_000_000 }));
            });
        }

        [TestMethod]
        public void DottedPath_IsResolved()
        {
            OnSta(() =>
            {
                var column = new DataGridTextColumn { Binding = new Binding("Child.Value") { StringFormat = "F2" } };
                Assert.AreEqual("3.14", DataGridCsv.GetCellValue(column, new Row { Child = new Inner { Value = 3.14159 } }));
            });
        }

        /// <summary>
        /// 添字付きのパス。地盤ウィンドウの液状化判定の列 (<c>BetaL[0]</c> など) がこの形で、
        /// 以前は画面外の行だけ空欄になった。
        /// </summary>
        [TestMethod]
        public void IndexedPath_IsResolved()
        {
            OnSta(() =>
            {
                var column = new DataGridTextColumn { Binding = new Binding("Values[1]") { StringFormat = "{0:F3}" } };
                Assert.AreEqual("0.250", DataGridCsv.GetCellValue(column, new Row { Values = [1.0, 0.25] }),
                    "添字付きのパスが読めていません (画面外の行だけ空欄になります)");
            });
        }

        /// <summary>
        /// 項目経由の静的プロパティ。Chang の画面の鋼板ヤング率 Es がこの形で、画面には出るのにコピーでは空欄だった。
        /// </summary>
        [TestMethod]
        public void StaticProperty_IsResolved()
        {
            OnSta(() =>
            {
                var column = new DataGridTextColumn { Binding = new Binding("Es") { StringFormat = "N0" } };
                Assert.AreEqual("205000", DataGridCsv.GetCellValue(column, new Row()),
                    "静的プロパティが読めていません");
            });
        }

        // ── テンプレート列 ────────────────────────────────────────────
        // 以前は常に空欄だった。画面では値が見えている列 (液状化安全率 FL、要素分割の上端/下端など) が
        // CSV・コピーでは全行空欄になっていた。

        private const string Ns =
            "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
            + "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
            + "xmlns:t='clr-namespace:TestProject1;assembly=TestProject1'";

        private static DataGridTemplateColumn TemplateColumn(string body)
            => new() { CellTemplate = (DataTemplate)XamlReader.Parse($"<DataTemplate {Ns}>{body}</DataTemplate>") };

        [TestMethod]
        public void TemplateColumn_TextBlockIsRead()
        {
            OnSta(() =>
            {
                // 地盤ウィンドウのレベル2 液状化安全率 FL と同じ形
                var column = TemplateColumn("<TextBlock Text=\"{Binding Values[1], StringFormat='{}{0:0.00}'}\"/>");
                Assert.AreEqual("0.85", DataGridCsv.GetCellValue(column, new Row { Values = [1.2, 0.8512] }),
                    "テンプレート列の値が読めていません (画面では見えているのに空欄になります)");
            });
        }

        [TestMethod]
        public void TemplateColumn_StackedValuesAreJoined()
        {
            OnSta(() =>
            {
                // 要素分割の「上端 / 下端」と同じ形
                var column = TemplateColumn(
                    "<StackPanel><TextBlock Text=\"{Binding Top, StringFormat=N2}\"/><TextBlock Text=\"{Binding Bottom, StringFormat=N2}\"/></StackPanel>");
                Assert.AreEqual("1.50 / 2.25", DataGridCsv.GetCellValue(column, new Row { Top = 1.5, Bottom = 2.25 }));
            });
        }

        [TestMethod]
        public void TemplateColumn_ButtonsAreNotValues()
        {
            OnSta(() =>
            {
                // 荷重ケースの「値 + 適用ボタン」と、「削除」だけの列
                var withApply = TemplateColumn(
                    "<StackPanel><TextBox Text=\"{Binding Top, StringFormat=N2}\"/><Button Content='適用'/></StackPanel>");
                Assert.AreEqual("3.00", DataGridCsv.GetCellValue(withApply, new Row { Top = 3 }));

                var deleteOnly = TemplateColumn("<Button Content='削除'/>");
                Assert.AreEqual("", DataGridCsv.GetCellValue(deleteOnly, new Row()));
            });
        }

        [TestMethod]
        public void TemplateColumn_CheckBoxIsRead()
        {
            OnSta(() =>
            {
                // CheckBox も ButtonBase の派生。ボタンと一緒に除かないこと
                var column = TemplateColumn("<CheckBox IsChecked=\"{Binding IsChecked}\"/>");
                Assert.AreEqual("True", DataGridCsv.GetCellValue(column, new Row { IsChecked = true }));
            });
        }

        [TestMethod]
        public void TemplateColumn_HiddenPartIsSkippedPerRow()
        {
            OnSta(() =>
            {
                // 杭頭の工法で欄を出し分けている形 (ContentControl の Visibility をバインド)
                var column = TemplateColumn(
                    "<ContentControl><ContentControl.Resources><BooleanToVisibilityConverter x:Key='b2v'/></ContentControl.Resources>"
                    + "<ContentControl.Visibility><Binding Path='IsShown' Converter='{StaticResource b2v}'/></ContentControl.Visibility>"
                    + "<TextBlock Text=\"{Binding Top, StringFormat=N1}\"/></ContentControl>");
                Assert.AreEqual("4.0", DataGridCsv.GetCellValue(column, new Row { Top = 4, IsShown = true }));
                Assert.AreEqual("", DataGridCsv.GetCellValue(column, new Row { Top = 4, IsShown = false }),
                    "画面で隠している欄の値が出ています");
            });
        }

        /// <summary>
        /// 荷重ケースの「地盤の非線形性」と同じ形。選択値は enum で、画面は項目テンプレートの変換器で日本語にする。
        /// 選択値をそのまま文字にすると、画面に無い英語の内部名が出る。
        /// </summary>
        [TestMethod]
        public void TemplateColumn_ComboBoxUsesItemTemplate()
        {
            OnSta(() =>
            {
                var column = TemplateColumn(
                    "<ComboBox SelectedItem=\"{Binding Mode}\">"
                    + "<ComboBox.Resources><t:TestModeToTextConverter x:Key='m2t'/></ComboBox.Resources>"
                    + "<ComboBox.ItemTemplate><DataTemplate><TextBlock Text=\"{Binding Converter={StaticResource m2t}}\"/></DataTemplate></ComboBox.ItemTemplate>"
                    + "</ComboBox>");
                Assert.AreEqual("非線形", DataGridCsv.GetCellValue(column, new Row { Mode = TestMode.Nonlinear }),
                    "ComboBox の表示が、画面と違う文字 (内部名) で出ています");
            });
        }

        /// <summary>
        /// 画面の DataContext を RelativeSource でたどる欄。杭頭の画面の PC リング・引張定着筋などがこの形で、
        /// 以前は「DataContext.*」を一律に App.InputModel から読んでいた。杭頭の画面の DataContext は
        /// ChangViewModel で、InputModel に PileTop は無いので、画面に出ている値がコピーでは空欄になった。
        /// </summary>
        [TestMethod]
        public void RelativeSourceWindow_ReadsTheWindowsDataContext()
        {
            OnSta(() =>
            {
                var column = TemplateColumn(
                    "<TextBlock Text=\"{Binding DataContext.Child.Value, StringFormat=N1, RelativeSource={RelativeSource AncestorType=Window}}\"/>");
                var grid = new DataGrid();
                grid.Columns.Add(column);
                var window = new Window { DataContext = new Row { Child = new Inner { Value = 7.26 } }, Content = grid };
                try
                {
                    Assert.AreEqual("7.3", DataGridCsv.GetCellValue(column, new Row(), grid),
                        "画面の DataContext から読めていません (App.InputModel から読んでいませんか)");
                    Assert.AreEqual("", DataGridCsv.GetCellValue(column, new Row()),
                        "表を渡さないときに、画面と無関係なものから値を読んでいます");
                }
                finally
                {
                    window.Close();
                }
            });
        }

        // ── 列と行の並び ──────────────────────────────────────────────
        // 以前は Columns の登録順 (XAML に書いた順) で出していた。列を並べ替えても
        // コピー・CSV は元の順のままで、画面と同じ並びのつもりで転記すると列を取り違える。

        private static DataGrid GridWithReorderedColumns(out Row[] rows)
        {
            var grid = new DataGrid { AutoGenerateColumns = false };
            grid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new Binding(nameof(Row.Top)) { StringFormat = "F0" } });
            grid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new Binding(nameof(Row.Bottom)) { StringFormat = "F0" } });
            grid.Columns.Add(new DataGridTextColumn { Header = "C", Binding = new Binding(nameof(Row.Name)), IsReadOnly = true });
            rows = [new Row { Top = 1, Bottom = 2, Name = "x" }, new Row { Top = 3, Bottom = 4, Name = "y" }];
            grid.ItemsSource = rows;

            // 画面で C を先頭へ、A を末尾へ動かした状態 (C, B, A)
            grid.Columns[2].DisplayIndex = 0;
            grid.Columns[0].DisplayIndex = 2;
            return grid;
        }

        [TestMethod]
        public void CreateCsv_FollowsTheDisplayedColumnOrder()
        {
            string path = Path.Combine(Path.GetTempPath(), $"DataGridCsvOrder_{Guid.NewGuid():N}.csv");
            try
            {
                OnSta(() =>
                {
                    var grid = GridWithReorderedColumns(out var rows);
                    DataGridCsv.CreateCsv(rows, grid, path);
                });

                var lines = File.ReadAllLines(path, Encoding.UTF8);
                CollectionAssert.AreEqual(new[] { "R,WR,WR", "C,B,A", "x,2,1", "y,4,3" }, lines,
                    "CSV の列が画面の並びになっていません (見出し・R/WR・データの 3 つとも揃えること)");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 行は画面に出ている順 (並べ替え・絞り込みのあと) に出すこと。以前は元データの順・全行のままだった。
        /// あわせて、ファイルの形 (BOM 付き UTF-8・CRLF) が 1 行ずつ書く形にしても変わらないこと。
        /// </summary>
        [TestMethod]
        public void CreateCsv_FollowsTheDisplayedRowOrderAfterSortingAndFiltering()
        {
            string path = Path.Combine(Path.GetTempPath(), $"DataGridCsvRows_{Guid.NewGuid():N}.csv");
            try
            {
                OnSta(() =>
                {
                    var grid = new DataGrid { AutoGenerateColumns = false };
                    grid.Columns.Add(new DataGridTextColumn { Header = "名前", Binding = new Binding(nameof(Row.Name)) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "上", Binding = new Binding(nameof(Row.Top)) { StringFormat = "F0" } });
                    grid.ItemsSource = new List<Row>
                    {
                        new() { Name = "a", Top = 1 }, new() { Name = "b", Top = 3 }, new() { Name = "c", Top = 2 }, new() { Name = "d", Top = 4 },
                    };
                    grid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(Row.Top), System.ComponentModel.ListSortDirection.Descending));
                    grid.Items.Filter = o => ((Row)o).Name != "b";

                    DataGridCsv.CreateCsv(DataGridCsv.RowsAsDisplayed(grid), grid, path, includeEditableRow: false);
                });

                CollectionAssert.AreEqual(new[] { "名前,上", "d,4", "c,2", "a,1" }, File.ReadAllLines(path, Encoding.UTF8),
                    "CSV の行が画面の順 (並べ替え・絞り込みのあと) になっていません");
                byte[] bytes = File.ReadAllBytes(path);
                CollectionAssert.AreEqual(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray(), "BOM が付いていません (Excel が文字コードを判別できない)");
                StringAssert.Contains(Encoding.UTF8.GetString(bytes), "\r\n", "改行が CRLF ではありません");
            }
            finally
            {
                File.Delete(path);
            }

            // 書き出し・コピーの入口が元データではなく画面の行を読むこと
            string src = TestSource.Read("Graphics_r1", "Output", "DataGridCsv.cs");
            StringAssert.Contains(TestSource.MethodBody(src, "public static void Export(DataGrid dataGrid)"), "RowsAsDisplayed(dataGrid)");
            StringAssert.Contains(TestSource.MethodBody(src, "public static void CopyToClipboard(DataGrid dataGrid)"), "RowsAsDisplayed(dataGrid)");
            Assert.IsFalse(TestSource.MethodBody(src, "public static void CreateCsv(").Contains("new StringBuilder", StringComparison.Ordinal),
                "CSV を全行まとめてメモリに積んでから書いています (1 行ずつ書くこと)");
        }

        /// <summary>
        /// Excel が数式として解釈する文字 (先頭が =・+・-・@) は、アポストロフィを付けて文字のまま保つこと。
        /// 数と 1 文字だけの「-」には付けず、付けたものは先頭の 1 文字を除けば元の文字に戻ること。
        /// </summary>
        [TestMethod]
        public void CsvFieldsThatExcelWouldEvaluateStayText()
        {
            foreach (var text in new[] { "=SUM(A1:A2)", "+81-3", "-杭頭", "@INDIRECT", "=1+1,2" })
            {
                string guarded = DataGridCsv.GuardAgainstFormula(text);
                Assert.AreEqual("'" + text, guarded, $"「{text}」が Excel で数式として評価されます");
                Assert.AreEqual(text, guarded[1..], "先頭の 1 文字を除いても元の文字に戻りません");
            }
            foreach (var text in new[] { "-1.5", "+3", "-2.5E-3", "-", "杭-1", "1=1" })
                Assert.AreEqual(text, DataGridCsv.GuardAgainstFormula(text), $"「{text}」に印を付けています (数や式にならない文字には付けない)");

            Assert.AreEqual("\"'=1+1,2\"", DataGridCsv.EscapeCsvField("=1+1,2"), "印を付けたうえで、カンマを含む欄は引用符で囲むこと");
            Assert.AreEqual("=A1", DataGridCsv.EscapeTsvField("=A1"), "クリップボード (この表へ貼り戻す形) には印を付けないこと");
        }

        [TestMethod]
        public void CreateCsv_CanOmitTheEditableRow()
        {
            string path = Path.Combine(Path.GetTempPath(), $"DataGridCsvNoFlags_{Guid.NewGuid():N}.csv");
            try
            {
                OnSta(() =>
                {
                    var grid = GridWithReorderedColumns(out var rows);
                    DataGridCsv.CreateCsv(rows, grid, path, includeEditableRow: false);
                });
                Assert.AreEqual("C,B,A", File.ReadAllLines(path, Encoding.UTF8)[0],
                    "R/WR の行を出さない指定のとき、先頭が見出しになっていません (Chang の画面の CSV の形)");
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>選択セルのコピーも、選んだ順ではなく画面の行・列の順に並べる。</summary>
        [TestMethod]
        public void SelectedCells_AreOrderedAsDisplayed()
        {
            OnSta(() =>
            {
                var grid = GridWithReorderedColumns(out var rows);
                grid.SelectionUnit = DataGridSelectionUnit.Cell;
                grid.SelectionMode = DataGridSelectionMode.Extended;
                // 下の行から、列も画面と逆の順に選ぶ
                grid.SelectedCells.Add(new DataGridCellInfo(rows[1], grid.Columns[0]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[1], grid.Columns[2]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[1]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[0]));

                var ordered = SelectionLines(grid, withTitleRow: false).Select(l => l.Replace('\t', ',')).ToArray();

                // 行は上から、列は画面の並び (C, B, A)。選択に含まれる列 (C, B, A) を全行でそろえ、
                // その行で選ばれていない列は空欄 (1 行目の C、2 行目の B)
                CollectionAssert.AreEqual(new[] { ",2,1", "y,,3" }, ordered,
                    "選択セルのコピーが、選んだ順のまま並んでいるか、選ばれていない列の空欄が入っていません");
            });
        }

        /// <summary>
        /// 行によって選んだ列が違っても、列がずれないこと。
        /// 以前は行ごとに選ばれたセルだけを繋いでいたので、選ばれていない列の空欄が入らず、
        /// 貼り付け先で後ろの値が左へ寄った (例: 1 行目は A・B・C、2 行目は A・C だけ → 2 行目の C が B の位置に入る)。
        /// </summary>
        [TestMethod]
        public void SelectionCopyKeepsColumnsAlignedAcrossRows()
        {
            OnSta(() =>
            {
                var grid = new DataGrid { AutoGenerateColumns = false, SelectionUnit = DataGridSelectionUnit.Cell, SelectionMode = DataGridSelectionMode.Extended };
                grid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new Binding(nameof(Row.Top)) { StringFormat = "F0" } });
                grid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new Binding(nameof(Row.Bottom)) { StringFormat = "F0" } });
                grid.Columns.Add(new DataGridTextColumn { Header = "C", Binding = new Binding(nameof(Row.Name)) });
                var rows = new[] { new Row { Top = 1, Bottom = 2, Name = "x" }, new Row { Top = 3, Bottom = 4, Name = "y" } };
                grid.ItemsSource = rows;
                for (int i = 0; i < grid.Columns.Count; i++) grid.Columns[i].DisplayIndex = i;

                foreach (var col in grid.Columns) grid.SelectedCells.Add(new DataGridCellInfo(rows[0], col));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[1], grid.Columns[0]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[1], grid.Columns[2]));

                CollectionAssert.AreEqual(new[] { "1\t2\tx", "3\t\ty" }, SelectionLines(grid, withTitleRow: false),
                    "2 行目の C が B の位置に寄っています (選ばれていない列の空欄が入っていない)");

                // Ctrl+C (見出し行つき) も同じ並べ方になること
                CollectionAssert.AreEqual(new[] { "A\tB\tC", "1\t2\tx", "3\t\ty" }, SelectionLines(grid, withTitleRow: true),
                    "Ctrl+C のコピーが、右クリックのコピーと違う並べ方になっています");
            });
        }

        private static string[] SelectionLines(DataGrid grid, bool withTitleRow)
        {
            string? text = DataGridCsv.BuildSelectionText(grid, withTitleRow);
            Assert.IsNotNull(text, "選択セルの文字が作られていません");
            return text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        }

        [TestMethod]
        public void EscapeCsvField_QuotesOnlyWhenNeeded()
        {
            Assert.AreEqual("abc", DataGridCsv.EscapeCsvField("abc"));
            Assert.AreEqual("1234.5", DataGridCsv.EscapeCsvField("1234.5"));
            Assert.AreEqual("", DataGridCsv.EscapeCsvField(null));
            Assert.AreEqual("\"a,b\"", DataGridCsv.EscapeCsvField("a,b"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", DataGridCsv.EscapeCsvField("say \"hi\""));
            Assert.AreEqual("\"上\n下\"", DataGridCsv.EscapeCsvField("上\n下"));
        }

        /// <summary>名称や見出しにカンマ・改行・引用符が入っても、列と行の数が崩れないこと。</summary>
        [TestMethod]
        public void CreateCsv_KeepsColumnsWhenTextHasCommasAndNewlines()
        {
            string path = Path.Combine(Path.GetTempPath(), $"DataGridCsvExportTests_{Guid.NewGuid():N}.csv");
            try
            {
                OnSta(() =>
                {
                    var grid = new DataGrid();
                    grid.Columns.Add(new DataGridTextColumn { Header = "名称, 備考", Binding = new Binding(nameof(Row.Name)) });
                    grid.Columns.Add(new DataGridTextColumn { Header = "変位\n(mm)", Binding = new Binding(nameof(Row.Displacement)) { StringFormat = "N1" } });

                    var rows = new object[]
                    {
                        new Row { Name = "杭A, 北側", Displacement = 1.26 },
                        new Row { Name = "杭\"B\"\n南側", Displacement = 2 },
                    };
                    DataGridCsv.CreateCsv(rows, grid, path);
                });

                string text = File.ReadAllText(path, Encoding.UTF8);
                var records = ParseCsv(text);
                Assert.AreEqual(4, records.Length, "行数が崩れています:\n" + text);
                Assert.IsTrue(records.All(r => r.Length == 2), "列数が崩れています:\n" + text);
                CollectionAssert.AreEqual(new[] { "名称, 備考", "変位\n(mm)" }, records[1]);
                CollectionAssert.AreEqual(new[] { "杭A, 北側", "1.3" }, records[2]);
                CollectionAssert.AreEqual(new[] { "杭\"B\"\n南側", "2.0" }, records[3]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>RFC 4180 の最小限の読み取り (検証用)。</summary>
        private static string[][] ParseCsv(string text)
        {
            var records = new System.Collections.Generic.List<string[]>();
            var fields = new System.Collections.Generic.List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(field.ToString()); field.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n')
                {
                    fields.Add(field.ToString()); field.Clear();
                    records.Add(fields.ToArray()); fields.Clear();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); records.Add(fields.ToArray()); }
            return records.ToArray();
        }
    }

    public enum TestMode { Linear, Nonlinear }

    /// <summary>テンプレート列の試験用。enum を画面の表示名にする。</summary>
    public sealed class TestModeToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is TestMode m ? (m == TestMode.Nonlinear ? "非線形" : "線形") : "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
