using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
