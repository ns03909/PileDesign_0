
using Microsoft.Win32;
using PileDesign;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace PileDesign.Output
{
    internal class DataGridCsv
    {
        // 桁区切りコンマを含む数値パターン: -?1,234(,567)*(.89)?
        // Excel 貼付け時にセル内のカンマが列区切りとして解釈される問題を避けるため、
        // このパターンにマッチするセル値はコンマを除去する。
        private static readonly Regex ThousandGroupedNumberPattern = new(
            @"^-?\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.Compiled);

        private static string StripThousandSeparator(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            return ThousandGroupedNumberPattern.IsMatch(text) ? text.Replace(",", string.Empty) : text;
        }

        /// <summary>
        /// 画面の書式に使われるカルチャ。
        ///
        /// WPF は変換器にも StringFormat にも、OS のカルチャではなく要素の Language
        /// (既定は en-US) を渡す。本アプリは Language を設定していないので en-US になる。
        /// 画面外のセルもこれに揃えないと、OS の地域設定しだいで小数点がずれる。
        /// </summary>
        private static CultureInfo DisplayCulture(BindingBase binding)
            => binding switch
            {
                Binding b when b.ConverterCulture != null => b.ConverterCulture,
                MultiBinding m when m.ConverterCulture != null => m.ConverterCulture,
                _ => CultureInfo.GetCultureInfo("en-US"),
            };

        /// <summary>
        /// Binding.StringFormat を WPF と同じ解釈で当てる。
        ///
        /// XAML の <c>StringFormat=N3</c> は「{0:N3}」の省略形で、中括弧を含まない書式は
        /// 書式指定子として扱われる。<c>string.Format("N3", value)</c> にそのまま渡すと
        /// 値ではなく「N3」という文字が出る (以前はそうなっていた)。
        /// </summary>
        internal static string ApplyStringFormat(string? format, object value, CultureInfo culture)
        {
            if (string.IsNullOrEmpty(format))
                return Convert.ToString(value, culture) ?? string.Empty;
            string composite = format.Contains('{') ? format : "{0:" + format + "}";
            return string.Format(culture, composite, value);
        }

        /// <summary>
        /// CSV の 1 欄。カンマ・引用符・改行を含むときだけ引用符で囲み、中の引用符は二重にする (RFC 4180)。
        /// 名称や見出しにカンマや改行が入ると、囲まなければ列や行がずれる。
        /// </summary>
        internal static string EscapeCsvField(string? field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            field = GuardAgainstFormula(field);
            if (field.IndexOfAny([',', '"', '\r', '\n']) < 0) return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Excel で開いたときに数式として解釈される文字 (先頭が =・+・-・@) を、文字のまま保つ。
        ///
        /// 名称などが「=」「+」「-」「@」で始まると、Excel は CSV のセルを数式として評価し、
        /// 表示が変わる (「#NAME?」になる、計算した値が出る)。先頭にアポストロフィ「'」を付けて文字として扱わせる
        /// (OWASP の CSV インジェクション対策と同じ)。先頭の 1 文字を除けば元の文字に戻る。
        /// <b>数 (「-1.5」「+3」) と、1 文字だけの「-」などは付けない</b>。数は数として開かれ、1 文字だけでは式にならない。
        /// クリップボード (タブ区切り) には付けない。この表へ貼り戻す形なので、元の文字のまま写す。
        /// </summary>
        internal static string GuardAgainstFormula(string field)
        {
            if (field.Length < 2 || field[0] is not ('=' or '+' or '-' or '@')) return field;
            if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return field;
            return "'" + field;
        }

        private static string CsvLine(IEnumerable<string> fields)
            => string.Join(",", fields.Select(EscapeCsvField));

        /// <summary>
        /// タブ区切り (クリップボード) の 1 欄。タブ・改行を含むか、引用符で始まるときだけ引用符で囲み、
        /// 中の引用符は二重にする (Excel がセルをコピーするときと同じ形)。
        ///
        /// 以前は値をそのまま繋いでいたので、名称などにタブや改行が入ると、貼り付け先で列や行がずれた。
        /// 表への貼り付け (<c>EnhancedDataGrid.SplitPastedRows</c>) は同じ規則で読み戻す。
        /// </summary>
        internal static string EscapeTsvField(string? field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;
            if (field.IndexOfAny(['\t', '\r', '\n']) < 0 && field[0] != '"') return field;
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }

        private static string TsvLine(IEnumerable<string> fields)
            => string.Join("\t", fields.Select(EscapeTsvField));

        /// <summary>
        /// 選択セルをタブ区切りでクリップボードへ写す（Excel にそのまま貼れる形）。
        ///
        /// <b>行番号の列は入れない。</b> 入れると貼り付け先で列がずれる。
        ///
        /// 以前は同じ 15 行が 4 か所にあった（<c>BaseViewModel</c>・
        /// <c>MainWindowViewModel</c>・<c>SettlementViewModel</c>・
        /// <c>GroupSettlementWithBeamCalculationViewModel</c>）。どれも同じだったが、
        /// 形を直すときに 1 つ直し忘れれば、その画面だけ貼り付けがずれる。
        /// セルの値の取り方 (<see cref="GetCellValue"/>) と同じ場所に置く。
        /// </summary>
        public static void CopySelectionToClipboard(DataGrid dataGrid)
        {
            string? text = BuildSelectionText(dataGrid, withTitleRow: false);
            if (text != null)
                PileDesign.Common.ClipboardHelper.TrySetText(text);
        }

        /// <summary>
        /// 選択セルをタブ区切りの文字にする。選択が無ければ null。
        /// 右クリックの「選択セルをコピー」(見出し行なし) と Ctrl+C (見出し行あり) の両方がこれを使う。
        ///
        /// 以前は Ctrl+C (EnhancedDataGrid) にも同じ並べ方の写しがあり、右クリックの側だけ
        /// 行ごとに選ばれたセルを詰めて繋いでいた (列がずれた)。並べ方を 1 か所にまとめた。
        /// </summary>
        /// <param name="withTitleRow">先頭に、選択に含まれる列の見出しを 1 行付けるか (Ctrl+C の形)。</param>
        internal static string? BuildSelectionText(DataGrid dataGrid, bool withTitleRow)
        {
            if (dataGrid == null || dataGrid.SelectedCells.Count == 0) return null;

            var (columns, rows) = SelectedRowsAsDisplayed(dataGrid);
            if (rows.Count == 0) return null;

            var sb = new StringBuilder();
            if (withTitleRow)
                sb.AppendLine(TsvLine(columns.Select(PileDesign.Common.DataGridHeaderText.From)));
            foreach (var row in rows)
                sb.AppendLine(TsvLine(row.Select(cell => cell is { } c ? GetCellValue(c, dataGrid) : "")));
            return sb.ToString();
        }

        /// <summary>
        /// 画面に出ている列を、画面の並び (DisplayIndex) の順に返す。
        ///
        /// 以前は <c>Columns</c> の登録順 (XAML に書いた順) で出していた。列を並べ替えても
        /// コピー・CSV は元の順のままで、画面と同じ並びのつもりで転記すると列を取り違える。
        /// 見出し・R/WR の行・データを同じ列の並びから作るので、行どうしがずれることもない。
        /// </summary>
        internal static IReadOnlyList<DataGridColumn> ColumnsAsDisplayed(DataGrid dataGrid)
            => [.. dataGrid.Columns
                .Where(column => column.Visibility == Visibility.Visible)
                .OrderBy(column => column.DisplayIndex)];

        /// <summary>
        /// 選択セルを、画面の行の順・列の順に並べ直して行ごとに返す。
        /// <c>SelectedCells</c> は選んだ順なので、下から選ぶと行が逆さに、列も選んだ順に並んでいた。
        ///
        /// <b>列は選択に含まれる列を全行でそろえる。</b>その行で選ばれていない列は null (空欄) を入れる。
        /// 以前は行ごとに選ばれたセルだけを繋いでいたので、行によって選んだ列が違うと
        /// 空欄が入らず、貼り付け先で後ろの値が左へ寄った。Ctrl+C のコピーはもともとこの形。
        /// </summary>
        private static (IReadOnlyList<DataGridColumn> Columns, IReadOnlyList<IReadOnlyList<DataGridCellInfo?>> Rows)
            SelectedRowsAsDisplayed(DataGrid dataGrid)
        {
            var rowOrder = new Dictionary<object, int>();
            int position = 0;
            foreach (var item in dataGrid.Items)
                if (item != null) rowOrder.TryAdd(item, position++);

            var columns = dataGrid.SelectedCells
                .Where(cell => cell.Column?.Visibility == Visibility.Visible)
                .Select(cell => cell.Column)
                .Distinct()
                .OrderBy(column => column.DisplayIndex)
                .ToList();

            var rows = dataGrid.SelectedCells
                .Where(cell => cell.Column?.Visibility == Visibility.Visible)
                .GroupBy(cell => cell.Item)
                .OrderBy(row => row.Key != null && rowOrder.TryGetValue(row.Key, out int at) ? at : int.MaxValue)
                .Select(row =>
                {
                    var byColumn = row.GroupBy(cell => cell.Column).ToDictionary(g => g.Key, g => g.First());
                    return (IReadOnlyList<DataGridCellInfo?>)columns
                        .Select(column => byColumn.TryGetValue(column, out var cell) ? cell : (DataGridCellInfo?)null)
                        .ToList();
                })
                .ToList();
            return (columns, rows);
        }

        /// <summary>
        /// データグリッドを CSV ファイルに書き出す。
        /// </summary>
        /// <param name="includeEditableRow">
        /// 先頭に列ごとの「R」(読み取り専用) /「WR」(編集可) の行を置くか。
        /// Chang の画面の CSV は以前からこの行を持たないので、その形を保つために false を渡す。
        /// </param>
        public static void CreateCsv(IEnumerable<object> data, DataGrid dataGrid, string filePath, bool includeEditableRow = true)
        {
            var columns = ColumnsAsDisplayed(dataGrid);

            // 一時ファイルへ 1 行ずつ書き、書き切ってから差し替える (失敗しても前のファイルを壊さない)。
            // 以前は全行を StringBuilder に積み、さらに全体を UTF-8 のバイト列にしてから書いたので、
            // 行の多い結果表では同じ中身が 2 つ同時にメモリを占めた。
            // 中身は従来と同じ (BOM 付き UTF-8。Excel が文字コードを判別できる。改行は CRLF)。
            PileDesign.Services.FileOperationService.WriteAtomically(filePath, stream =>
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 64 * 1024, leaveOpen: true)
                {
                    NewLine = "\r\n",
                };

                // 先頭行に「R」または「WR」を追加
                if (includeEditableRow)
                    writer.WriteLine(CsvLine(columns.Select(column => column.IsReadOnly ? "R" : "WR")));

                // ヘッダー行を追加
                writer.WriteLine(CsvLine(columns.Select(PileDesign.Common.DataGridHeaderText.From)));

                // データ行を追加（仮想化で画面外のセルも取得できるようバインディングから直接値を取得）
                foreach (var item in data)
                {
                    if (item is not null)
                        writer.WriteLine(CsvLine(columns.Select(column => GetCellValue(column, item, dataGrid))));
                }
            });
        }

        /// <summary>
        /// 表の行を、<b>画面に出ている順</b> (並べ替え・絞り込みのあと) に返す。新規行の置き場 (空の最終行) は除く。
        ///
        /// 以前は CSV もコピーも表の元データ (ItemsSource) を順に読んでいたので、画面で並べ替えたり絞り込んだりしても、
        /// 出力は元の順・全行のままだった。列の並び (<see cref="ColumnsAsDisplayed"/>) と同じく画面に揃える。
        /// </summary>
        internal static IEnumerable<object> RowsAsDisplayed(DataGrid dataGrid)
            => dataGrid.Items.Cast<object>().Where(item => item != null && !Equals(item, CollectionView.NewItemPlaceholder));

        /// <summary>
        /// DataGridの内容をタブ区切りテキストとしてクリップボードにコピーする
        /// </summary>
        public static void CopyToClipboard(DataGrid dataGrid)
        {
            var sb = new StringBuilder();
            var columns = ColumnsAsDisplayed(dataGrid);

            // ヘッダー行
            var headers = columns.Select(PileDesign.Common.DataGridHeaderText.From).ToArray();
            sb.AppendLine(TsvLine(headers));

            // データ行（仮想化で画面外のセルも取得できるようバインディングから直接値を取得）。行は画面の順
            foreach (var item in RowsAsDisplayed(dataGrid))
            {
                if (item is not null)
                {
                    var row = columns.Select(column => GetCellValue(column, item, dataGrid)).ToArray();
                    sb.AppendLine(TsvLine(row));
                }
            }

            Common.ClipboardHelper.TrySetText(sb.ToString());
        }

        /// <summary>
        /// 表を CSV ファイルに書き出す (保存先を訊く)。行は画面に出ている順 (<see cref="RowsAsDisplayed"/>)。
        /// </summary>
        public static void Export(DataGrid dataGrid)
        {
            var dataGridName = dataGrid.Name;
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSVファイル (*.csv)|*.csv",
                FileName = $"{dataGridName}_Export.csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                CreateCsv(RowsAsDisplayed(dataGrid), dataGrid, saveFileDialog.FileName);
            }
        }
        /// <summary>
        /// DataGridColumnからセル値を取得する。
        /// 画面に表示されているセルはGetCellContentで、仮想化で未生成のセルはバインディングパスから直接取得する。
        /// </summary>
        /// <summary>
        /// 選択セルからセル値を取得する（仮想化対応）。SelectedCells用ヘルパー。
        /// </summary>
        public static string GetCellValue(DataGridCellInfo cell, DataGrid? owner = null)
        {
            return GetCellValue(cell.Column, cell.Item, owner);
        }

        /// <param name="owner">
        /// 列を載せている表。<c>RelativeSource</c> のバインディング
        /// (例: 杭頭の画面の <c>{Binding DataContext.PileTop..., RelativeSource={RelativeSource AncestorType=Window}}</c>)
        /// は、ここから祖先をたどって読む。渡さなければ、その種のバインディングは空欄になる。
        /// </param>
        public static string GetCellValue(DataGridColumn column, object item, DataGrid? owner = null)
        {
            DependencyObject? host = owner;
            // テンプレート列は、画面に出ているセルも出ていないセルも同じ経路で読む。
            // 出ているセルの中身は ContentPresenter で、TextBlock を探して読むと
            // 画面外の行と別の経路になり、スクロール位置で結果が変わる。
            if (column is DataGridTemplateColumn templateColumn)
                return EvaluateTemplateColumn(templateColumn, item, host);

            // まず表示済みセルから取得を試みる
            if (column.GetCellContent(item) is TextBlock tb)
                return StripThousandSeparator(tb.Text);

            // バインディングを取得（仮想化対応）
            BindingBase? bindingBase = (column as DataGridBoundColumn)?.Binding;
            return EvaluateBinding(bindingBase, item, host);
        }

        private static string EvaluateBinding(BindingBase? bindingBase, object item, DependencyObject? host) => bindingBase switch
        {
            Binding binding => EvaluateSingleBinding(binding, item, host),
            MultiBinding multi => EvaluateMultiBinding(multi, item, host),
            _ => string.Empty,
        };

        // ── テンプレート列 ────────────────────────────────────────────
        //
        // 以前は「バインディングが取れない」として常に空欄を返していた。
        // 地盤ウィンドウの液状化安全率 FL (レベル1・2) や、要素分割の上端/下端の値など、
        // 画面では数値が見えている列が CSV・コピーでは全行空欄になっていた。
        //
        // テンプレートを 1 度だけ実体化し、中の表示要素のバインディングを取り出して、
        // 各行にはそのバインディングを上と同じ手順 (値 → 変換器 → 書式) で当てる。
        // 実体化した要素に DataContext は渡さないので、バインディングは動かず、
        // テンプレートに書かれたイベント (SelectionChanged など) も起きない。

        /// <summary>
        /// テンプレートの中の、値を表す 1 要素。
        /// ComboBox は選んだ項目を <see cref="DisplayMemberPath"/> か <see cref="ItemTemplate"/> で文字にする。
        /// 荷重ケースの「地盤の非線形性」は選択値が enum で、画面は項目テンプレートの変換器で日本語にしている。
        /// 選択値をそのまま文字にすると、画面に無い英語の内部名が出る。
        /// </summary>
        private sealed record TemplateValue(
            BindingBase Binding, IReadOnlyList<BindingBase> VisibilityGates, string? DisplayMemberPath, DataTemplate? ItemTemplate);

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DataTemplate, IReadOnlyList<TemplateValue>> _templateValues = new();

        private static string EvaluateTemplateColumn(DataGridTemplateColumn column, object item, DependencyObject? host)
            => EvaluateTemplate(column.CellTemplate, item, host);

        private static string EvaluateTemplate(DataTemplate? template, object item, DependencyObject? host)
        {
            var texts = new List<string>();
            foreach (var v in GetTemplateValues(template))
            {
                // 行ごとに非表示にしている部分 (例: 杭頭の工法で切り替える欄) は、その行では出さない
                if (v.VisibilityGates.Any(g => EvaluateToObject(g, item, host) is Visibility vis && vis != Visibility.Visible))
                    continue;

                string text;
                if (v.DisplayMemberPath is { Length: > 0 } member)
                {
                    object? selected = EvaluateToObject(v.Binding, item, host);
                    object? shown = selected == null ? null : Common.BindingPath.Resolve(selected, member);
                    text = shown == null ? string.Empty : ApplyStringFormat(null, shown, DisplayCulture(v.Binding));
                }
                else if (v.ItemTemplate != null)
                {
                    object? selected = EvaluateToObject(v.Binding, item, host);
                    text = selected == null ? string.Empty : EvaluateTemplate(v.ItemTemplate, selected, host);
                }
                else
                {
                    text = EvaluateBinding(v.Binding, item, host);
                }
                if (text.Length > 0) texts.Add(text);
            }
            // 上端/下端のように 1 セルに 2 つ並べている列は、画面の縦並びを「 / 」で繋ぐ
            return string.Join(" / ", texts);
        }

        private static IReadOnlyList<TemplateValue> GetTemplateValues(DataTemplate? template)
        {
            if (template == null) return [];
            if (_templateValues.TryGetValue(template, out var cached)) return cached;

            var found = new List<TemplateValue>();
            try
            {
                if (template.LoadContent() is DependencyObject root)
                    CollectTemplateValues(root, [], found);
            }
            catch (Exception ex)
            {
                // 実体化できないテンプレートは、以前と同じく空欄にする (記録は残す)
                Serilog.Log.Debug($"[DataGridCsv] CellTemplate load failed: {ex.GetType().Name}: {ex.Message}");
                found.Clear();
            }
            _templateValues.AddOrUpdate(template, found);
            return found;
        }

        private static void CollectTemplateValues(DependencyObject node, List<BindingBase> gates, List<TemplateValue> found)
        {
            // ボタンは操作であって値ではない (「削除」「適用」)。
            // CheckBox も ButtonBase の派生なので、Button だけを除く
            if (node is Button) return;

            var visibility = BindingOperations.GetBindingBase(node, UIElement.VisibilityProperty);
            if (visibility != null) gates = [.. gates, visibility];
            if (node is UIElement { Visibility: not Visibility.Visible } && visibility == null) return;

            if (node is ComboBox combo)
            {
                var selectedItem = BindingOperations.GetBindingBase(node, Selector.SelectedItemProperty);
                if (selectedItem != null)
                {
                    bool byMember = !string.IsNullOrEmpty(combo.DisplayMemberPath);
                    found.Add(new TemplateValue(selectedItem, gates,
                        byMember ? combo.DisplayMemberPath : null, byMember ? null : combo.ItemTemplate));
                    return;
                }
            }

            BindingBase? value = node switch
            {
                TextBlock => BindingOperations.GetBindingBase(node, TextBlock.TextProperty),
                TextBox => BindingOperations.GetBindingBase(node, TextBox.TextProperty),
                ComboBox => BindingOperations.GetBindingBase(node, Selector.SelectedValueProperty)
                            ?? BindingOperations.GetBindingBase(node, ComboBox.TextProperty),
                ToggleButton => BindingOperations.GetBindingBase(node, ToggleButton.IsCheckedProperty),
                _ => null,
            };
            if (value != null)
            {
                found.Add(new TemplateValue(value, gates, null, null));
                return;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node))
                if (child is DependencyObject d)
                    CollectTemplateValues(d, gates, found);
        }

        /// <summary>変換器まで通した値 (書式は当てない)。取れなければ null。</summary>
        private static object? EvaluateToObject(BindingBase bindingBase, object item, DependencyObject? host)
        {
            if (bindingBase is not Binding binding) return null;
            string path = binding.Path?.Path ?? string.Empty;
            try
            {
                object? value = ResolveBindingPath(binding, path, item, host);
                if (binding.Converter != null)
                    value = binding.Converter.Convert(value, typeof(object), binding.ConverterParameter, DisplayCulture(binding));
                return value == DependencyProperty.UnsetValue || value == Binding.DoNothing ? null : value;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[DataGridCsv] Binding '{path}' eval failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 画面に出ていないセルの文字を、画面と同じ手順で作る。
        ///
        /// WPF の順序は「値 → 変換器 (Converter) → StringFormat」。変換器を飛ばすと、
        /// 表示だけ単位を替えている列 (杭頭変位の ×1000、ヤング係数の kN/m² → N/mm² など) が
        /// 画面外の行だけ内部の単位で出る。画面に出ているセルは TextBlock から読むので、
        /// <b>スクロール位置によって同じ列の単位が混ざる</b>。
        /// </summary>
        private static string EvaluateSingleBinding(Binding binding, object item, DependencyObject? host)
        {
            // パスが無ければ項目そのもの (項目テンプレートの {Binding Converter=...} がこの形)
            string path = binding.Path?.Path ?? string.Empty;

            try
            {
                var culture = DisplayCulture(binding);
                object? value = ResolveBindingPath(binding, path, item, host);
                if (binding.Converter != null)
                    value = binding.Converter.Convert(value, typeof(string), binding.ConverterParameter, culture);
                if (value == null || value == DependencyProperty.UnsetValue || value == Binding.DoNothing)
                    return string.Empty;

                return StripThousandSeparator(ApplyStringFormat(binding.StringFormat, value, culture));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[DataGridCsv] Binding '{path}' eval failed: {ex.GetType().Name}: {ex.Message}");
                return string.Empty;
            }
        }

        private static string EvaluateMultiBinding(MultiBinding multi, object item, DependencyObject? host)
        {
            // 各子 Binding を解決: item の直接プロパティ または RelativeSource の祖先要素から
            var values = new object?[multi.Bindings.Count];
            for (int i = 0; i < multi.Bindings.Count; i++)
            {
                if (multi.Bindings[i] is not Binding b || b.Path?.Path is not string p) { values[i] = null; continue; }
                values[i] = ResolveBindingPath(b, p, item, host);
            }

            try
            {
                if (multi.Converter is IMultiValueConverter conv)
                {
                    var culture = DisplayCulture(multi);
                    var converted = conv.Convert(values, typeof(string), multi.ConverterParameter, culture);
                    if (converted == null || converted == DependencyProperty.UnsetValue || converted == Binding.DoNothing)
                        return string.Empty;
                    return StripThousandSeparator(ApplyStringFormat(multi.StringFormat, converted, culture));
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[DataGridCsv] MultiBinding converter '{multi.Converter?.GetType().Name ?? "<null>"}' failed: {ex.GetType().Name}: {ex.Message}");
            }
            return string.Empty;
        }

        private static object? ResolveBindingPath(Binding b, string path, object item, DependencyObject? host)
        {
            // パスの無いバインディング ({Binding Converter=...}) は項目そのものを指す (WPF と同じ)
            if (string.IsNullOrEmpty(path) || path == ".")
            {
                if (b.Source != null) return b.Source;
                if (b.RelativeSource != null) return FindRelativeSource(b.RelativeSource, host);
                return b.ElementName == null ? item : null;
            }
            try
            {
                // RelativeSource (例: AncestorType=Window) は、表から祖先要素をたどってそこから読む。
                // 以前は「DataContext.*」を一律に App.InputModel から読んでいた。画面の DataContext は
                // 画面ごとに違う (杭頭の画面は ChangViewModel) ので、InputModel に無い PileTop などは
                // 読めずに空欄になっていた。DataContext は FrameworkElement の公開プロパティなので、
                // 祖先要素を起点にすればパスをそのままたどれる。
                if (b.RelativeSource != null)
                {
                    var source = FindRelativeSource(b.RelativeSource, host);
                    return source == null ? null : Common.BindingPath.Resolve(source, path);
                }
                if (b.Source != null) return Common.BindingPath.Resolve(b.Source, path);
                if (b.ElementName != null) return null;   // 名前で引く要素はセルを作らないと見つからない
                return Common.BindingPath.Resolve(item, path);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug($"[DataGridCsv] Path '{path}' resolve failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// RelativeSource の FindAncestor を、表 (<paramref name="host"/>) から上へたどって探す。
        /// セルは表の子孫なので、表自身も祖先に数える。Self・TemplatedParent などセル自身を
        /// 起点にするものは、セルを作らずに読んでいるので取れない (null)。
        /// </summary>
        private static DependencyObject? FindRelativeSource(RelativeSource relativeSource, DependencyObject? host)
        {
            if (host == null || relativeSource.Mode != RelativeSourceMode.FindAncestor || relativeSource.AncestorType == null)
                return null;

            int remaining = Math.Max(1, relativeSource.AncestorLevel);
            for (DependencyObject? node = host; node != null; node = Parent(node))
            {
                if (relativeSource.AncestorType.IsInstanceOfType(node) && --remaining == 0)
                    return node;
            }
            return null;

            static DependencyObject? Parent(DependencyObject node)
                => (node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                        ? System.Windows.Media.VisualTreeHelper.GetParent(node) : null)
                   ?? LogicalTreeHelper.GetParent(node);
        }
    }
}