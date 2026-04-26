using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;


namespace PileDesign.Common
{
    public static class PlotHelper
    {
        public static Crosshair InitCrosshair(WpfPlot wpfPlot, ScottPlot.Color? markerColor = null)
        {
            // 既存のクロスヘアを削除（重複防止）
            foreach (var ch in wpfPlot.Plot.GetPlottables().OfType<Crosshair>().ToList())
                wpfPlot.Plot.Remove(ch);

            var crosshair = wpfPlot.Plot.Add.Crosshair(0, 0);
            crosshair.IsVisible = false;
            crosshair.MarkerShape = MarkerShape.OpenCircle;
            crosshair.MarkerSize = 15;
            if (markerColor != null)
            {
                crosshair.MarkerColor = markerColor.Value;
                crosshair.MarkerFillColor = markerColor.Value;
                crosshair.MarkerLineColor = markerColor.Value;
            }
            return crosshair;
        }

        public static void WpfPlot_MouseMove(object sender, MouseEventArgs e,
            string positionPropertyName = "CrosshairPositionText",
            string xAxis = "X", string yAxis = "Y",
            int decimalPlacesX = 3, int decimalPlacesY = 3)
        {
            string formatX = "#,##0" + (decimalPlacesX > 0 ? "." + new string('0', decimalPlacesX) : "");
            string formatY = "#,##0" + (decimalPlacesY > 0 ? "." + new string('0', decimalPlacesY) : "");

            if (sender is not WpfPlot wpfPlot) return;

            var scatters = wpfPlot.Plot.GetPlottables()
                .OfType<Scatter>()
                .ToList();
            if (scatters.Count == 0) return;

            var p = e.GetPosition(wpfPlot);
            var mousePixel = new Pixel(p.X * wpfPlot.DisplayScale, p.Y * wpfPlot.DisplayScale);
            var mouseLocation = wpfPlot.Plot.GetCoordinates(mousePixel);

            double minDist = double.MaxValue;
            DataPoint? nearest = null;
            Scatter? nearestScatter = null;
            int nearestIndex/* = -1*/;

            foreach (var scatter in scatters)
            {
                var pt = scatter.Data.GetNearest(mouseLocation, wpfPlot.Plot.LastRender);
                double dx = pt.Coordinates.X - mouseLocation.X;
                double dy = pt.Coordinates.Y - mouseLocation.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (pt.IsReal && dist < minDist)
                {
                    minDist = dist;
                    nearest = pt;

                    nearestScatter = scatter;
                    nearestIndex = pt.Index;
                }
            }

            if (wpfPlot.Plot.GetPlottables().OfType<Crosshair>().FirstOrDefault() is Crosshair crosshair)
            {
                if (nearest is { IsReal: true })
                {
                    crosshair.IsVisible = true;
                    crosshair.Position = nearest.Value.Coordinates;
                    wpfPlot.Refresh();

                    // Legend取得
                    //string legend = "";
                    //if (nearestScatter is ILegendItem legendItem)
                    string legend = nearestScatter?.LegendText ?? "";

                    // DataContextのプロパティに座標＋Legendをセット
                    var dc = wpfPlot.DataContext;
                    var prop = dc?.GetType().GetProperty(positionPropertyName);

                    //prop?.SetValue(dc, $"{xAxis}={nearest.Value.Coordinates.X:0.###}, {yAxis}={nearest.Value.Coordinates.Y:0.###}");

                    prop?.SetValue(dc,
                   $"{legend} || " +
                   $"{xAxis}={nearest.Value.Coordinates.X.ToString(formatX)}, " +
                   $"{yAxis}={nearest.Value.Coordinates.Y.ToString(formatY)}");

                }
                else if (crosshair.IsVisible)
                {
                    crosshair.IsVisible = false;
                    wpfPlot.Refresh();
                }
            }
        }

        public static void WpfPlot_MouseMove_RectangleVertexFocus(
            object sender, MouseEventArgs e,
            string positionPropertyName = "CrosshairPositionText",
            string xAxis = "X", string yAxis = "Y",
            int decimalPlacesX = 3, int decimalPlacesY = 3)
        {
            string formatX = "#,##0" + (decimalPlacesX > 0 ? "." + new string('0', decimalPlacesX) : "");
            string formatY = "#,##0" + (decimalPlacesY > 0 ? "." + new string('0', decimalPlacesY) : "");

            if (sender is not WpfPlot wpfPlot) return;

            // Rectangle Plottable を取得
            var rectangles = wpfPlot.Plot.GetPlottables()
                .OfType<ScottPlot.Plottables.Rectangle>()
                .ToList();
            if (rectangles.Count == 0) return;

            // マウス座標（データ座標系）
            var p = e.GetPosition(wpfPlot);
            var mousePixel = new Pixel(p.X * wpfPlot.DisplayScale, p.Y * wpfPlot.DisplayScale);
            var mouseLocation = wpfPlot.Plot.GetCoordinates(mousePixel);

            // 右上・右下頂点リストを作成
            var candidatePoints = new List<Coordinates>();
            foreach (var rect in rectangles)
            {
                var c = rect.CoordinateRect;
                candidatePoints.Add(new Coordinates(c.XRange.Value2, c.YRange.Value2)); // 右上
                candidatePoints.Add(new Coordinates(c.XRange.Value2, c.YRange.Value1)); // 右下
            }

            // 最も近い頂点を探索
            double minDist = double.MaxValue;
            Coordinates? nearest = null;
            foreach (var pt in candidatePoints)
            {
                double dx = pt.X - mouseLocation.X;
                double dy = pt.Y - mouseLocation.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = pt;
                }
            }

            // クロスヘアを移動
            if (wpfPlot.Plot.GetPlottables().OfType<Crosshair>().FirstOrDefault() is Crosshair crosshair)
            {
                if (nearest != null)
                {
                    crosshair.IsVisible = true;
                    crosshair.Position = nearest.Value;
                    wpfPlot.Refresh();

                    // DataContextのプロパティに座標をセット
                    var dc = wpfPlot.DataContext;
                    var prop = dc?.GetType().GetProperty(positionPropertyName);
                    prop?.SetValue(dc,
                        $"{xAxis}={nearest.Value.X.ToString(formatX)}, " +
                        $"{yAxis}={nearest.Value.Y.ToString(formatY)}");
                }
                else if (crosshair.IsVisible)
                {
                    crosshair.IsVisible = false;
                    wpfPlot.Refresh();
                }
            }
        }

        /// <summary>
        /// WpfPlotの右クリックメニューにCSVエクスポート項目を追加
        /// </summary>
        /// <param name="wpfPlot">対象のWpfPlotコントロール</param>
        /// <param name="defaultFileName">デフォルトのファイル名（拡張子なし）</param>
        /// <summary>
        /// データ点にラベルを追加する。右端付近の点は左寄せにしてはみ出しを防ぐ。
        /// </summary>
        /// <param name="xMax">X軸データの最大値（アライメント判定に使用）</param>
        public static ScottPlot.Plottables.Text AddText(
            Plot plot, string text, double x, double y, double xMax)
        {
            var label = plot.Add.Text(text, new Coordinates(x, y));
            // 右端70%以上はテキストを左に伸ばす（LowerRight = テキスト右端が点に揃う）
            label.LabelAlignment = (xMax > 0 && x > xMax * 0.70)
                ? Alignment.LowerRight
                : Alignment.LowerLeft;
            return label;
        }

        public static void AddCsvExportMenu(WpfPlot wpfPlot, string defaultFileName = "data")
        {
            var menu = wpfPlot.Menu;
            if (menu == null) return;
            // ScottPlotデフォルトの「Copy Image」はClipboard.SetImage(BitmapImage)を使い
            // BitmapMetadata例外が発生するため、デフォルトメニューをクリアして安全な実装に差し替え
            menu.Clear();
            menu.Add("画像をコピー", plot => SafeCopyImageToClipboard(wpfPlot));
            menu.Add("画像を保存...", plot => SafeSavePlotImage(wpfPlot, defaultFileName));
            menu.AddSeparator();
            menu.Add("CSVコピー", plot => CopyCsvToClipboard(plot));
            menu.Add("CSVとして保存...", plot => ExportToCsv(plot, defaultFileName));
            // 全プロット共通で「別ウィンドウで開く」を追加（モーダル中でも他プロットと独立してリサイズ閲覧可能）
            menu.AddSeparator();
            menu.Add("別ウィンドウで開く", _ => DetachToSeparateWindow(wpfPlot, defaultFileName));
        }

        // 分離したウィンドウを追跡し、重複生成を防ぐ
        private static readonly Dictionary<WpfPlot, Window> _detachedPlotWindows = new();

        /// <summary>
        /// 右クリックメニューに「別ウィンドウで開く」を追加する。
        /// 同じ WpfPlot インスタンスを新しい Window へ再ペアレントするため、
        /// 分離中もライブ更新が継続され、閉じれば元のレイアウトに戻る。
        /// </summary>
        public static void AddPopoutMenu(WpfPlot wpfPlot, string title)
        {
            var menu = wpfPlot.Menu;
            if (menu == null) return;
            menu.AddSeparator();
            menu.Add("別ウィンドウで開く", _ => DetachToSeparateWindow(wpfPlot, title));
        }

        private static void DetachToSeparateWindow(WpfPlot wpfPlot, string title)
        {
            // 既に分離済みなら前面化
            if (_detachedPlotWindows.TryGetValue(wpfPlot, out var existing))
            {
                try { existing.Activate(); return; }
                catch { _detachedPlotWindows.Remove(wpfPlot); }
            }

            // 元の親（Grid 想定）と位置情報を記憶
            if (wpfPlot.Parent is not Grid originalGrid) return;
            int originalRow = Grid.GetRow(wpfPlot);
            int originalColumn = Grid.GetColumn(wpfPlot);
            int originalRowSpan = Grid.GetRowSpan(wpfPlot);
            int originalColumnSpan = Grid.GetColumnSpan(wpfPlot);
            int originalIndex = originalGrid.Children.IndexOf(wpfPlot);

            originalGrid.Children.Remove(wpfPlot);

            var window = new Window
            {
                Title = title,
                Width = 900,
                Height = 700,
                Content = wpfPlot,
                Owner = Window.GetWindow(originalGrid),
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            _detachedPlotWindows[wpfPlot] = window;

            window.Closed += (_, _) =>
            {
                _detachedPlotWindows.Remove(wpfPlot);
                // 新ウィンドウから切り離し
                window.Content = null;
                // 元の Grid 位置へ復帰
                Grid.SetRow(wpfPlot, originalRow);
                Grid.SetColumn(wpfPlot, originalColumn);
                Grid.SetRowSpan(wpfPlot, originalRowSpan);
                Grid.SetColumnSpan(wpfPlot, originalColumnSpan);
                if (!originalGrid.Children.Contains(wpfPlot))
                {
                    int insertAt = Math.Min(originalIndex, originalGrid.Children.Count);
                    originalGrid.Children.Insert(insertAt, wpfPlot);
                }
                wpfPlot.Refresh();
            };

            window.Show();
        }

        /// <summary>
        /// ScottPlotの画像をBitmapMetadata例外を回避してクリップボードにコピーする
        /// </summary>
        private static void SafeCopyImageToClipboard(WpfPlot wpfPlot)
        {
            try
            {
                byte[] bytes = wpfPlot.Plot.GetImageBytes((int)wpfPlot.ActualWidth * 2, (int)wpfPlot.ActualHeight * 2, ImageFormat.Png);

                // PNGストリームをクリップボードに設定
                var pngStream = new MemoryStream(bytes);

                // PngBitmapDecoderでデコード（BitmapImageはMetadata非対応なので使わない）
                var decoder = new PngBitmapDecoder(
                    new MemoryStream(bytes),
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];

                // DIB用にBMPエンコード
                var bmpEnc = new BmpBitmapEncoder();
                bmpEnc.Frames.Add(BitmapFrame.Create(frame));
                var bmpStream = new MemoryStream();
                bmpEnc.Save(bmpStream);
                bmpStream.Position = 14; // BMPファイルヘッダをスキップ
                var dibBytes = new byte[bmpStream.Length - 14];
                bmpStream.Read(dibBytes, 0, dibBytes.Length);

                var dataObject = new DataObject();
                dataObject.SetData("PNG", pngStream, false);
                dataObject.SetData(DataFormats.Dib, new MemoryStream(dibBytes), false);
                Clipboard.SetDataObject(dataObject, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"画像のコピーに失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ScottPlotの画像をファイルに保存する
        /// </summary>
        private static void SafeSavePlotImage(WpfPlot wpfPlot, string defaultFileName)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PNG画像 (*.png)|*.png|すべてのファイル (*.*)|*.*",
                FileName = defaultFileName + ".png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    wpfPlot.Plot.Save(dlg.FileName, (int)wpfPlot.ActualWidth * 2, (int)wpfPlot.ActualHeight * 2);
                    MessageBox.Show($"画像を保存しました:\n{dlg.FileName}", "保存完了",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"画像の保存に失敗しました:\n{ex.Message}", "エラー",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// プロットデータをCSV文字列に変換（区切り文字を指定可能）
        /// </summary>
        private static string BuildCsvString(Plot plot, string separator = ",")
        {
            var sb = new StringBuilder();
            bool hasData = false;

            // Scatterプロットからデータを取得
            var scatters = plot.GetPlottables()
                .OfType<Scatter>()
                .ToList();

            if (scatters.Count > 0)
            {
                hasData = true;
                var headers = new List<string>();
                for (int i = 0; i < scatters.Count; i++)
                {
                    string name = scatters[i].LegendText ?? $"Series{i + 1}";
                    name = name.Replace(",", "_").Replace("\"", "'").Replace("\t", " ");
                    headers.Add($"{name}_X");
                    headers.Add($"{name}_Y");
                }
                sb.AppendLine(string.Join(separator, headers));

                var dataLists = new List<(double[] xs, double[] ys)>();
                int maxRows = 0;
                foreach (var scatter in scatters)
                {
                    var xs = scatter.Data.GetScatterPoints().Select(p => p.X).ToArray();
                    var ys = scatter.Data.GetScatterPoints().Select(p => p.Y).ToArray();
                    dataLists.Add((xs, ys));
                    maxRows = Math.Max(maxRows, xs.Length);
                }

                for (int row = 0; row < maxRows; row++)
                {
                    var values = new List<string>();
                    foreach (var (xs, ys) in dataLists)
                    {
                        if (row < xs.Length)
                        {
                            values.Add(xs[row].ToString());
                            values.Add(ys[row].ToString());
                        }
                        else
                        {
                            values.Add("");
                            values.Add("");
                        }
                    }
                    sb.AppendLine(string.Join(separator, values));
                }
            }

            // Rectangleプロットからデータを取得
            var rectangles = plot.GetPlottables()
                .OfType<ScottPlot.Plottables.Rectangle>()
                .ToList();

            if (rectangles.Count > 0)
            {
                if (hasData) sb.AppendLine();
                hasData = true;
                sb.AppendLine(string.Join(separator, "X_Min", "X_Max", "Y_Min", "Y_Max"));
                foreach (var rect in rectangles)
                {
                    var coord = rect.CoordinateRect;
                    sb.AppendLine(string.Join(separator, coord.Left, coord.Right, coord.Bottom, coord.Top));
                }
            }

            if (!hasData)
                sb.AppendLine("データがありません");

            return sb.ToString();
        }

        /// <summary>
        /// プロットデータをクリップボードにTSV形式でコピー（Excelに直接貼り付け可能）
        /// </summary>
        private static void CopyCsvToClipboard(Plot plot)
        {
            try
            {
                string tsv = BuildCsvString(plot, "\t");
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        Clipboard.SetText(tsv);
                        return;
                    }
                    catch (System.Runtime.InteropServices.ExternalException)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"クリップボードへのコピーに失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// プロットデータをCSVファイルにエクスポート
        /// </summary>
        private static void ExportToCsv(Plot plot, string defaultFileName)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"{defaultFileName}.csv",
                DefaultExt = ".csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    string csv = BuildCsvString(plot, ",");
                    File.WriteAllText(saveFileDialog.FileName, csv, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"CSVの保存に失敗しました:\n{ex.Message}",
                        "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
