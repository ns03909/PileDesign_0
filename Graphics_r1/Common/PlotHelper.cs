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
using System.Windows.Input;


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
                    string legend = nearestScatter.LegendText ?? "";

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
        public static void AddCsvExportMenu(WpfPlot wpfPlot, string defaultFileName = "data")
        {
            wpfPlot.Menu.AddSeparator();
            wpfPlot.Menu.Add("CSVとして保存...", plot => ExportToCsv(plot, defaultFileName));
        }

        /// <summary>
        /// プロットデータをCSVにエクスポート
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
                    var sb = new StringBuilder();
                    bool hasData = false;

                    // Scatterプロットからデータを取得
                    var scatters = plot.GetPlottables()
                        .OfType<Scatter>()
                        .ToList();

                    if (scatters.Count > 0)
                    {
                        hasData = true;
                        // ヘッダー行を作成
                        var headers = new List<string>();
                        for (int i = 0; i < scatters.Count; i++)
                        {
                            string name = scatters[i].LegendText ?? $"Series{i + 1}";
                            // CSVで問題になる文字を置換
                            name = name.Replace(",", "_").Replace("\"", "'");
                            headers.Add($"{name}_X");
                            headers.Add($"{name}_Y");
                        }
                        sb.AppendLine(string.Join(",", headers));

                        // 各シリーズのデータ点数を取得
                        var dataLists = new List<(double[] xs, double[] ys)>();
                        int maxRows = 0;
                        foreach (var scatter in scatters)
                        {
                            var xs = scatter.Data.GetScatterPoints().Select(p => p.X).ToArray();
                            var ys = scatter.Data.GetScatterPoints().Select(p => p.Y).ToArray();
                            dataLists.Add((xs, ys));
                            maxRows = Math.Max(maxRows, xs.Length);
                        }

                        // データ行を出力
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
                            sb.AppendLine(string.Join(",", values));
                        }
                    }

                    // Rectangleプロットからデータを取得
                    var rectangles = plot.GetPlottables()
                        .OfType<ScottPlot.Plottables.Rectangle>()
                        .ToList();

                    if (rectangles.Count > 0)
                    {
                        if (hasData) sb.AppendLine(); // 前のデータがあれば空行を挿入
                        hasData = true;

                        // 矩形データのヘッダー
                        sb.AppendLine("X_Min,X_Max,Y_Min,Y_Max");

                        // 各矩形のデータを出力
                        foreach (var rect in rectangles)
                        {
                            var coord = rect.CoordinateRect;
                            sb.AppendLine($"{coord.Left},{coord.Right},{coord.Bottom},{coord.Top}");
                        }
                    }

                    if (!hasData)
                    {
                        sb.AppendLine("データがありません");
                    }

                    File.WriteAllText(saveFileDialog.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show($"CSVファイルを保存しました:\n{saveFileDialog.FileName}",
                        "エクスポート完了", MessageBoxButton.OK, MessageBoxImage.Information);
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
