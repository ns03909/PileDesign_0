
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Common
{
    public partial class ShapeDrawer
    {
        public static void DrawPileElevation(
            Canvas canvas,
            ObservableCollection<PileBodySegment> pileBodySegments,
            double pileToeDia,
            double insituPileToeHeight,
            double insituPileToeAngle,
            double precastConcretePileToeHeightRatio,
            string pileConstructionType,
            double pileTopAltitude = 0,
            GroundInput groundInput = null,
            bool isElementDivision = false,
            List<double> zs = null,
            double? selectedZ = null,
            double? selectedTop = null,
            double? selectedBottom = null,
            bool showLiquefactionFL = false,
            bool showGroundDisplacement = false,
            int seismicLevelIndex = 0,
            bool displacementWithLiquefaction = false,
            bool showUnconfinedCompressiveStrength = false)

        {
            if (canvas == null || canvas.ActualWidth == 0 || canvas.ActualHeight == 0) return;

            canvas.Children.Clear();

            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;

            if (pileBodySegments.Count == 0) return;

            // 地表面標高を取得（groundInputがない場合は杭頭標高を使用）
            double groundTopAltitude = groundInput?.GroundTopAltitude ?? pileTopAltitude;

            // 杭頭から地表面までの距離（杭頭が地表面より下の場合は正の値）
            double abovePileTop = groundTopAltitude - pileTopAltitude;
            if (abovePileTop < 0) abovePileTop = 0;

            // 杭体セグメントからの杭長
            double pileLengthFromSegments = pileBodySegments[^1].SegmentDepth;

            // 杭の深さを複数のソースから計算
            double pileDepth = pileLengthFromSegments;

            // 杭要素分割点がある場合は、分割点の範囲も考慮
            if (zs != null && zs.Count >= 2)
            {
                // zsの範囲（最浅点から最深点までの距離）
                double zsRange = zs.Max() - zs.Min();

                // 最大値を採用（セグメントの杭長 vs 分割点の範囲）
                if (zsRange > pileDepth)
                {
                    pileDepth = zsRange;
                }
            }

            // 総描画範囲 = 地表面から杭下端まで + 上下マージン（メートル単位で2m + 2m）
            double totalDisplayLength = abovePileTop + pileDepth + 4.0; // 4mは上下マージン分
            if (totalDisplayLength <= 0) totalDisplayLength = pileLengthFromSegments + 4.0;

            // 縦横それぞれのratioを計算し、小さい方を採用
            double pileToeDiaInMeters = pileToeDia / 1000.0;
            double ratioHeight = canvasHeight / totalDisplayLength;
            double ratioWidth = (canvasWidth - 20) / pileToeDiaInMeters; // 横は20px余裕
            double ratio = Math.Min(ratioHeight, ratioWidth);

            // ratioが小さすぎる場合の最小値を設定
            if (ratio <= 0) ratio = 1.0;

            // topMargin = 杭頭の描画Y位置（上マージン2m + 地表面から杭頭までの距離）
            double topMargin = (2.0 + abovePileTop) * ratio;

            if (groundInput != null)
            {
                DrawSoilLayers(canvas, groundInput, pileTopAltitude, ratio, topMargin);

                if (showLiquefactionFL)
                    DrawFLValues(canvas, groundInput, pileTopAltitude, ratio, topMargin, seismicLevelIndex);

                if (showGroundDisplacement)
                    DrawDisplacementValues(canvas, groundInput, pileTopAltitude, ratio, topMargin, seismicLevelIndex, displacementWithLiquefaction);

                if (showUnconfinedCompressiveStrength)
                    DrawQuValues(canvas, groundInput, pileTopAltitude, ratio, topMargin);
            }

            foreach (var segment in pileBodySegments)
            {
                double segmentLength = segment.SegmentLength;
                double segmentDepth = segment.SegmentDepth;
                double pileDiameter = segment.PileSection?.PileDiameter / 1000.0 ?? 0.0;

                if (pileDiameter < 0.01) // 杭径が極小の場合
                {
                    DrawLine(canvas,
                        canvasWidth * 0.5, segmentDepth * ratio + topMargin,
                        canvasWidth * 0.5, (segmentDepth - segmentLength) * ratio + topMargin,
                        Brushes.SkyBlue, 0, 1);
                }
                else // 杭径が一般の場合
                {
                    var rectangle = new Rectangle
                    {
                        Width = pileDiameter * ratio,
                        Height = segmentLength * ratio,
                        Stroke = Brushes.SkyBlue,
                        Fill = Brushes.White,
                    };
                    Canvas.SetLeft(rectangle, canvasWidth * 0.5 - pileDiameter * 0.5 * ratio);
                    Canvas.SetTop(rectangle, (segmentDepth - segmentLength) * ratio + topMargin);
                    canvas.Children.Add(rectangle);
                }
            }

            var lastSegment = pileBodySegments[^1];
            double bottomSegmentDia = lastSegment.PileSection?.PileDiameter / 1000.0 ?? 0.0;

            if (pileConstructionType == "場所打ちコンクリート杭" && pileToeDiaInMeters > bottomSegmentDia)
            {
                double angle = 90 - insituPileToeAngle;
                double pileToeElevation = insituPileToeHeight / 1000;
                DrawConeToeShape(canvas, canvasWidth, /*canvasHeight,*/ pileLengthFromSegments, ratio, topMargin, bottomSegmentDia, pileToeDiaInMeters, pileToeElevation, angle);
            }
            else if ((pileConstructionType == "埋込み杭（プレボーリング）" || pileConstructionType == "埋込み杭（中掘り）") && pileToeDiaInMeters > bottomSegmentDia)
            {

                DrawCylinderToeShape(canvas, canvasWidth, /*canvasHeight,*/ pileLengthFromSegments, ratio, topMargin, bottomSegmentDia, pileToeDiaInMeters, precastConcretePileToeHeightRatio);
            }
            else if (pileConstructionType == "回転貫入杭" && pileToeDiaInMeters > bottomSegmentDia)
            {
                // 回転貫入杭: 1巻きの螺旋羽根 (羽根径=pileToeDia, 羽根ピッチ=杭径Dp/6)
                DrawHelicalBladeToeShape(canvas, canvasWidth, pileLengthFromSegments, ratio, topMargin,
                    bottomSegmentDia, pileToeDiaInMeters);
            }

            if (isElementDivision)
            {
                DrawElementDivision(canvas, zs, ratio, topMargin);
            }

            if (selectedZ != null && zs != null && zs.Count > 0)
            {
                DrawSelectedZ(canvas, zs[0], selectedZ, ratio, topMargin);
            }

            // 選択区間の強調表示
            if (selectedTop.HasValue && selectedBottom.HasValue)
            {
                double topDepth = pileTopAltitude - selectedTop.Value;
                double bottomDepth = pileTopAltitude - selectedBottom.Value;

                double y1 = topDepth * ratio + topMargin;
                double y2 = bottomDepth * ratio + topMargin;

                // 杭径を取得して幅を計算
                double maxDia = pileBodySegments.Max(s => s.PileSection?.PileDiameter ?? 0) / 1000.0;
                double highlightWidth = maxDia * ratio + 10;

                var highlightRect = new Rectangle
                {
                    Stroke = Brushes.Red,
                    StrokeThickness = 3,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 0, 0)),
                    Width = highlightWidth,
                    Height = y2 - y1,
                };
                Canvas.SetLeft(highlightRect, canvasWidth * 0.5 - highlightWidth / 2);
                Canvas.SetTop(highlightRect, y1);
                canvas.Children.Add(highlightRect);
            }
        }

        // 拡大根固め杭の描画メソッド
        private static void DrawCylinderToeShape(
            Canvas canvas,
            double canvasWidth,
            //double canvasHeight,
            double pileLength,
            double ratio,
            double topMargin,
            double bottomSegmentDia,
            double pileToeDiaInMeters,
            double precastConcretePileToeHeightRatio)
        {
            // 拡大根固め球根の点群を生成
            double height = pileToeDiaInMeters * precastConcretePileToeHeightRatio;
            double width = pileToeDiaInMeters;
            var points = new PointCollection
            {
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + width * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - width * 0.5 * ratio, (pileLength - height) * ratio + topMargin),
            };
            DrawPolygon(canvas, points, Brushes.SkyBlue, Brushes.White, 1);

            // ダッシュライン
            for (int i = -1; i < 2; i += 2)
            {
                DrawLine(canvas,
                    canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    (pileLength - pileToeDiaInMeters * precastConcretePileToeHeightRatio) * ratio + topMargin,
                    canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    pileLength * ratio + topMargin,
                    Brushes.SkyBlue, 2, 1);
            }
        }

        /// <summary>
        /// 回転貫入杭の螺旋羽根 (1巻き、羽根ピッチ=杭径Dp/6) を側面投影として描画する。
        /// 螺旋面 (内径Dp、外径Dw) を側面から見ると、3つのクレセント (右上 / 左中 / 右下) が現れる:
        ///   right-top    : θ ∈ [0, π/2]      , z ∈ [0, pitch/4]      ← cosθ > 0 ⇒ 右側
        ///   left-middle  : θ ∈ [π/2, 3π/2]   , z ∈ [pitch/4, 3pitch/4] ← cosθ < 0 ⇒ 左側
        ///   right-bottom : θ ∈ [3π/2, 2π]    , z ∈ [3pitch/4, pitch] ← cosθ > 0 ⇒ 右側
        /// </summary>
        private static void DrawHelicalBladeToeShape(
            Canvas canvas,
            double canvasWidth,
            double pileLength,
            double ratio,
            double topMargin,
            double bottomSegmentDia,    // Dp [m] (杭径 = 最下段杭区間径)
            double bladeDiaInMeters)    // Dw [m] (羽根径 = PileToeDia)
        {
            double Dp = bottomSegmentDia;
            double Dw = bladeDiaInMeters;
            if (Dw <= Dp || Dp <= 0) return;

            double R = Dw * 0.5;
            double r = Dp * 0.5;
            // 物理ピッチ = 杭径 Dp の 1/5 (1 巻きの軸方向長さ)
            double pitch = Dp / 5.0;

            double cx = canvasWidth * 0.5;
            // 羽根は杭先端 (pileLength) から下方に 1 巻き分延ばす
            double yTop = pileLength * ratio + topMargin;             // 杭先端 = 羽根上端
            double yBot = (pileLength + pitch) * ratio + topMargin;   // 羽根下端

            // 杭体を羽根領域まで延長 (Dp 幅の矩形を yTop → yBot 区間に重ねる)
            var pileExt = new Rectangle
            {
                Width = Dp * ratio,
                Height = pitch * ratio,
                Stroke = Brushes.SkyBlue,
                Fill = Brushes.White,
            };
            Canvas.SetLeft(pileExt, cx - Dp * 0.5 * ratio);
            Canvas.SetTop(pileExt, yTop);
            canvas.Children.Add(pileExt);

            const int n = 24;
            // 杭体と同じ SkyBlue を用い、塗りは半透明で輪郭は実線
            var skyBlue = Color.FromRgb(135, 206, 235); // SkyBlue
            var fill = new SolidColorBrush(Color.FromArgb(160, skyBlue.R, skyBlue.G, skyBlue.B));
            var stroke = Brushes.SkyBlue;

            // クレセント領域を1つ生成 ([thetaStart, thetaEnd] の範囲)
            void DrawCrescent(double thetaStart, double thetaEnd)
            {
                var pts = new PointCollection();
                // 外側 (R) を thetaStart → thetaEnd
                for (int i = 0; i <= n; i++)
                {
                    double theta = thetaStart + (double)i / n * (thetaEnd - thetaStart);
                    double zRel = theta / (2.0 * Math.PI);  // 0..1 で 0..pitch をパラメータ化
                    double y = yTop + zRel * (yBot - yTop);
                    double xOuter = R * Math.Cos(theta);
                    pts.Add(new Point(cx + xOuter * ratio, y));
                }
                // 内側 (r) を thetaEnd → thetaStart で戻る
                for (int i = n; i >= 0; i--)
                {
                    double theta = thetaStart + (double)i / n * (thetaEnd - thetaStart);
                    double zRel = theta / (2.0 * Math.PI);
                    double y = yTop + zRel * (yBot - yTop);
                    double xInner = r * Math.Cos(theta);
                    pts.Add(new Point(cx + xInner * ratio, y));
                }
                DrawPolygon(canvas, pts, stroke, fill, 1.5);
            }

            DrawCrescent(0.0,                Math.PI / 2.0);          // right-top
            DrawCrescent(Math.PI / 2.0,      3.0 * Math.PI / 2.0);    // left-middle
            DrawCrescent(3.0 * Math.PI / 2.0, 2.0 * Math.PI);          // right-bottom
        }

        // 場所打ち拡底杭の描画メソッド
        private static void DrawConeToeShape(
            Canvas canvas,
            double canvasWidth,
            //double canvasHeight,
            double pileLength,
            double ratio,
            double topMargin,
            double bottomSegmentDia,
            double pileToeDiaInMeters,
            double pileToeElevation,
            double angle)
        {
            // 場所打ち拡底杭の点群を生成
            double height = (pileToeDiaInMeters - bottomSegmentDia) * 0.5 * Math.Tan(angle * Math.PI / 180);
            var points = new PointCollection
            {
                new Point(canvasWidth * 0.5 - bottomSegmentDia * 0.5 * ratio, (pileLength - pileToeElevation - height) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio, (pileLength - pileToeElevation) * ratio + topMargin),
                new Point(canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio, pileLength * ratio + topMargin),
                new Point(canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio, (pileLength - pileToeElevation) * ratio + topMargin),
                new Point(canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio, (pileLength - pileToeElevation - height) * ratio + topMargin)
            };
            DrawPolygon(canvas, points, Brushes.SkyBlue, Brushes.White, 1);

            // 上辺の直線
            var line = new Line
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = 1,
                X1 = canvasWidth * 0.5 + pileToeDiaInMeters * 0.5 * ratio,
                X2 = canvasWidth * 0.5 - pileToeDiaInMeters * 0.5 * ratio,
                Y1 = (pileLength - pileToeElevation) * ratio + topMargin,
                Y2 = (pileLength - pileToeElevation) * ratio + topMargin
            };
            canvas.Children.Add(line);

            // ダッシュライン
            for (int i = -1; i < 2; i += 2)
            {
                var dashedLine = new Line
                {
                    Stroke = Brushes.SkyBlue,
                    StrokeThickness = 1,
                    StrokeDashArray = [2],
                    X1 = canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    X2 = canvasWidth * 0.5 + bottomSegmentDia * 0.5 * ratio * i,
                    Y1 = (pileLength - pileToeElevation - (pileToeDiaInMeters - bottomSegmentDia) * 0.5 * Math.Tan(angle * Math.PI / 180)) * ratio + topMargin,
                    Y2 = pileLength * ratio + topMargin
                };
                canvas.Children.Add(dashedLine);
            }
        }

        // 土層描画メソッド
        private static void DrawSoilLayers(Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            double canvasHeight = canvas.ActualHeight;
            double groundTopDepth = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;

            DrawLine(canvas, 0, groundTopDepth, canvasWidth, groundTopDepth, Brushes.Gray, 0, 2);

            // 土層の描画
            foreach (var layer in groundInput.GroundLayers)
            {
                double groundBottomDepth = (-layer.BottomAltitude + pileTopAltitude) * ratio + topMargin;
                if (0 <= groundBottomDepth && groundBottomDepth <= canvasHeight)
                {
                    DrawLine(canvas, 0, groundBottomDepth, canvasWidth, groundBottomDepth, Brushes.Gray, 0, 1);
                }
            }

            // 土層の描画
            for (int i = 0; i < groundInput.GroundLayers.Count; i++)
            {
                double yT = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
                if (i != 0) { yT = (-groundInput.GroundLayers[i - 1].BottomAltitude + pileTopAltitude) * ratio + topMargin; }
                double yB = (-groundInput.GroundLayers[i].BottomAltitude + pileTopAltitude) * ratio + topMargin;

                SolidColorBrush fill = new(Color.FromArgb(64, 0, 255, 255)); // 半透明のAqua色
                if (groundInput.GroundLayers[i].GranularityClass == "粘性土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 210, 180, 140)); // 半透明の薄い茶色
                }
                else if (groundInput.GroundLayers[i].GranularityClass == "砂質土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 255, 165, 0)); // 半透明の薄いオレンジ
                }
                else if (groundInput.GroundLayers[i].GranularityClass == "礫質土")
                {
                    fill = new SolidColorBrush(Color.FromArgb(64, 144, 238, 144)); // 半透明の薄い緑
                }

                var rectangle = new Rectangle
                {
                    Width = canvasWidth,
                    Height = yB - yT,
                    Fill = fill,
                    StrokeThickness = 0.5,
                    Stroke = Brushes.White
                };

                Canvas.SetLeft(rectangle, 0);
                Canvas.SetTop(rectangle, yT);
                canvas.Children.Add(rectangle);

                DrawLine(canvas, 0, yB, canvasWidth, yB, Brushes.Black); //描けない
            }

            // Zメモリ描画
            for (int j = 0; j < 1000; j++)
            {
                double altitude = Math.Ceiling(groundInput.GroundTopAltitude - j);
                double y = (-altitude + pileTopAltitude) * ratio + topMargin;
                if (y > canvasHeight) break;

                double dx = 10;
                if (y % 5 == 0) dx *= 2;

                DrawLine(canvas, canvasWidth - dx, y, canvasWidth, y, Brushes.LightGray, 0, 1);

                // 文字を追加
                var text = new TextBlock
                {
                    Text = altitude.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = text.DesiredSize.Width;
                double textHeight = text.DesiredSize.Height;

                Canvas.SetLeft(text, canvasWidth - textWidth); // 文字の位置を調整
                Canvas.SetTop(text, y - textHeight / 2); // 文字の中央を深度位置に配置
                canvas.Children.Add(text);
            }
            var symbol = new TextBlock
            {
                Text = "Z",
                Foreground = Brushes.Black,
                FontSize = 10
            };
            symbol.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double symbolWidth = symbol.DesiredSize.Width;
            double symbolHeight = symbol.DesiredSize.Height;

            Canvas.SetLeft(symbol, canvasWidth - symbolWidth); // 文字の右端を調整
            Canvas.SetTop(symbol, groundTopDepth - symbolHeight); // 文字の下端を調整
            canvas.Children.Add(symbol);

            DrawWaterTable(canvas, groundInput, pileTopAltitude, ratio, topMargin);

            DrawNValues(canvas, groundInput, pileTopAltitude, ratio, topMargin);
        }

        // 地下水位描画メソッド (実線 1 本 + 直下 3 本の半透明ラインで水位を表現)
        private static void DrawWaterTable(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            if (groundInput == null) return;

            double canvasWidth = canvas.ActualWidth;
            // 地下水位 GL 深度 → 標高 = TopAltitude + GLDepth, 描画 Y = (-Altitude + pileTopAltitude) * ratio + topMargin
            double waterAltitude = groundInput.GroundTopAltitude + groundInput.GroundWaterGLDepth;
            double yWater = (-waterAltitude + pileTopAltitude) * ratio + topMargin;

            // メインライン (Nikken DeepBlue、▽ の下端頂点が乗る位置)
            var mainColor = Color.FromRgb(0x32, 0x71, 0xAD); // NikkenDeepBlueColor #3271AD
            DrawLine(canvas, 0, yWater, canvasWidth, yWater, new SolidColorBrush(mainColor), 0, 1.0);

            // 直下サブラインの間隔は杭/地盤の深さに依存しない一定の px 値で描画
            const double lineGap = 7.0;

            // 主線 + サブ 3 本 = 計 4 本のラインで地下水位を表現。
            // すべて細め (1.0 px) で統一し、4 本線として視認できるようにする。
            // サブラインは下に行くほど薄くなる漸減アルファ。
            byte[] alphas = [220, 170, 120];
            for (int i = 0; i < alphas.Length; i++)
            {
                double y = yWater + (i + 1) * lineGap;
                if (y > canvas.ActualHeight) break;
                var transBrush = new SolidColorBrush(Color.FromArgb(alphas[i], mainColor.R, mainColor.G, mainColor.B));
                var subLine = new Line
                {
                    Stroke = transBrush,
                    StrokeThickness = 1.0,
                    X1 = 0, Y1 = y,
                    X2 = canvasWidth, Y2 = y,
                    SnapsToDevicePixels = true
                };
                System.Windows.Media.RenderOptions.SetEdgeMode(subLine, System.Windows.Media.EdgeMode.Aliased);
                canvas.Children.Add(subLine);
            }

            // 「▽GWT」マーカー (左端)。▽ の下側頂点 ≒ フォントの baseline 位置に来るため、
            // baseline を主線 (yWater) に合わせて配置する (TextBlock の bottom 揃えだと
            // descender 分だけ ▽ 先端が主線より上にズレる)。
            // 背景は透明に: 半透明白を入れると baseline 揃え時に主線・サブ線の上を覆い
            // 「▽直下に青線が見えない」現象が発生する。
            var label = new TextBlock
            {
                Text = "▽GWT",
                Foreground = new SolidColorBrush(mainColor),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = null
            };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double baseline = label.BaselineOffset;
            if (double.IsNaN(baseline) || baseline <= 0)
                baseline = label.DesiredSize.Height * 0.8; // フォールバック (heuristic)
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, yWater - baseline);
            canvas.Children.Add(label);
        }

        // 各データ点に半透明のホバーマーカーを追加 (ToolTip 表示用)
        // invisible=true のときはマーカー本体を非表示にし、ヒット領域だけを確保する (例: qu 階段グラフ)。
        private static void AddHoverMarker(Canvas canvas, double x, double y, string tooltipText, Brush color, bool invisible = false)
        {
            double markerSize = invisible ? 12.0 : 5.0;
            var ellipse = new System.Windows.Shapes.Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                ToolTip = new System.Windows.Controls.ToolTip { Content = tooltipText },
                Cursor = System.Windows.Input.Cursors.Hand,
                IsHitTestVisible = true
            };
            if (invisible)
            {
                // Brushes.Transparent はヒットテスト可能 (alpha=0 でも入力捕捉される)。
                ellipse.Fill = System.Windows.Media.Brushes.Transparent;
                ellipse.Stroke = null;
            }
            else
            {
                var srcColor = ((SolidColorBrush)color).Color;
                // 透過すぎると Hit-Test が抜けるため不透明度を上げる
                ellipse.Fill = new SolidColorBrush(Color.FromArgb(120, srcColor.R, srcColor.G, srcColor.B));
                ellipse.Stroke = color;
                ellipse.StrokeThickness = 1.0;
            }
            // ToolTip を素早く表示
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(ellipse, 100);
            System.Windows.Controls.ToolTipService.SetBetweenShowDelay(ellipse, 0);
            System.Windows.Controls.ToolTipService.SetShowDuration(ellipse, 30000);
            Canvas.SetLeft(ellipse, x - markerSize / 2);
            Canvas.SetTop(ellipse, y - markerSize / 2);
            canvas.Children.Add(ellipse);
        }


        // 土層 N 値のステップ図描画用ブラシ (土質点N値の灰色折れ線と区別)
        private static readonly SolidColorBrush LayerNStrokeBrush = new(Color.FromRgb(60, 60, 60)); // 濃い灰

        // N値描画メソッド (土質点 N 値の折れ線 + 土層 N 値の階段図)
        private static void DrawNValues(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;

            var points = new PointCollection();
            var pointInfo = new List<(double x, double y, double value, double depth)>();

            foreach (var massdata in groundInput.GroundMassesData)
            {
                double groundDepth = (-massdata.AltitudeDepth + pileTopAltitude) * ratio + topMargin;
                double groundNvalue = massdata.NValue / 4.0 / 60.0 * canvasWidth;
                points.Add(new Point(groundNvalue, groundDepth));
                pointInfo.Add((groundNvalue, groundDepth, massdata.NValue, massdata.GLDepth));
            }

            var polyline = new Polyline
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
                Points = points,
            };

            canvas.Children.Add(polyline);

            // 土層 N 値の階段図 (GroundLayer.NValue を層境で階段状に描画)
            if (groundInput.GroundLayers != null && groundInput.GroundLayers.Count > 0)
            {
                var stepPoints = new PointCollection();
                double topAlt = groundInput.GroundTopAltitude;
                double prevN = Math.Min(Math.Max(groundInput.GroundLayers[0].NValue, 0.0), 60.0);
                double yTop0 = (-topAlt + pileTopAltitude) * ratio + topMargin;
                stepPoints.Add(new Point(prevN / 60.0 / 4.0 * canvasWidth, yTop0));

                foreach (var layer in groundInput.GroundLayers)
                {
                    double n = Math.Min(Math.Max(layer.NValue, 0.0), 60.0);
                    double yBot = (-layer.BottomAltitude + pileTopAltitude) * ratio + topMargin;
                    if (Math.Abs(n - prevN) > 1e-9)
                    {
                        // 層境で水平セグメント (前のN値→現在のN値) を挿入
                        double yLayerTop = stepPoints[^1].Y;
                        stepPoints.Add(new Point(n / 60.0 / 4.0 * canvasWidth, yLayerTop));
                    }
                    stepPoints.Add(new Point(n / 60.0 / 4.0 * canvasWidth, yBot));
                    prevN = n;
                }

                canvas.Children.Add(new Polyline
                {
                    Stroke = LayerNStrokeBrush,
                    StrokeThickness = 1.2,
                    Points = stepPoints,
                });

                // 各層中心に不可視ホバーマーカー (層 N 値の数値表示)
                double cumTop = topAlt;
                foreach (var layer in groundInput.GroundLayers)
                {
                    double n = Math.Min(Math.Max(layer.NValue, 0.0), 60.0);
                    double yMid = (-(cumTop + layer.BottomAltitude) / 2.0 + pileTopAltitude) * ratio + topMargin;
                    double xMark = n / 60.0 / 4.0 * canvasWidth;
                    AddHoverMarker(canvas, xMark, yMid,
                        $"層: {layer.Name}\n土層 N値 = {layer.NValue:0.#}", LayerNStrokeBrush, invisible: true);
                    cumTop = layer.BottomAltitude;
                }
            }

            // 各点にホバーマーカー (N値の数値表示)
            foreach (var (x, y, val, dep) in pointInfo)
            {
                AddHoverMarker(canvas, x, y,
                    $"GL{dep:0.00}m\nN値 = {val:0.#}", Brushes.Gray);
            }

            double y1 = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
            double y2 = (-groundInput.GroundMassesData[^1].AltitudeDepth + pileTopAltitude) * ratio + topMargin;
            for (int i = 0; i <= 60; i += 10)
            {
                double x = i / 4.0 / 60.0 * canvasWidth;
                var line = new Line
                {
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1,
                    X1 = x,
                    X2 = x,
                    Y1 = y1,
                    Y2 = y2
                };
                canvas.Children.Add(line);

                // 文字を追加
                var text = new TextBlock
                {
                    Text = i.ToString(),
                    Foreground = Brushes.Black,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double textHeight = text.DesiredSize.Height;
                Canvas.SetLeft(text, x); // 文字の位置を調整
                Canvas.SetTop(text, y1 - textHeight); // 文字の位置を調整
                canvas.Children.Add(text);
            }

            // 軸タイトル「N値」(qu (= 2Cu) と対になる位置: スケール目盛の上、左寄せ)
            var nTitle = new TextBlock
            {
                Text = "N値",
                Foreground = Brushes.Black,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            nTitle.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(nTitle, 0);
            Canvas.SetTop(nTitle, y1 - nTitle.DesiredSize.Height * 2.0 - 2);
            canvas.Children.Add(nTitle);
        }

        // 落ち着いた色のブラシ (再利用)
        private static readonly SolidColorBrush FLStrokeBrush = new(Color.FromRgb(160, 80, 70));     // 落ち着いた赤茶 (Sienna 系)
        private static readonly SolidColorBrush FLGridBrush   = new(Color.FromArgb(48, 160, 80, 70));
        private static readonly SolidColorBrush DispStrokeBrush = new(Color.FromRgb(70, 110, 150));  // 落ち着いた青 (SteelBlue 系)
        private static readonly SolidColorBrush DispGridBrush   = new(Color.FromArgb(48, 70, 110, 150));

        // 液状化FL描画メソッド (杭姿図右側に重ね描き、FL=0..2 スケール、左→右で値が増加)
        private static void DrawFLValues(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin, int levelIndex)
        {
            if (groundInput?.GroundMassesData == null || groundInput.GroundMassesData.Count == 0) return;

            // データ未算定 (全値0または欠損) の場合は何も描画しない
            bool hasData = groundInput.GroundMassesData.Any(m =>
                m.FL != null && m.FL.Count > levelIndex && m.FL[levelIndex].HasValue && m.FL[levelIndex].Value != 0.0);
            if (!hasData) return;

            double canvasWidth = canvas.ActualWidth;
            const double FL_MAX = 2.0;
            // 左→右で値が増加 (左端 FL=0, 右端 FL=FL_MAX)、右側に少しマージン
            double leftOrigin = canvasWidth * 0.72;       // FL=0
            double rightEnd   = canvasWidth * 0.96;       // FL=FL_MAX
            double xScale = (rightEnd - leftOrigin) / FL_MAX;

            var points = new PointCollection();
            var pointInfo = new List<(double x, double y, double value, double depth)>();
            foreach (var m in groundInput.GroundMassesData)
            {
                if (m.FL == null || m.FL.Count <= levelIndex) continue;
                var fl = m.FL[levelIndex];
                if (!fl.HasValue) continue;
                double y = (-m.AltitudeDepth + pileTopAltitude) * ratio + topMargin;
                double flClamped = Math.Min(Math.Max(fl.Value, 0.0), FL_MAX);
                double x = leftOrigin + flClamped * xScale;
                points.Add(new Point(x, y));
                pointInfo.Add((x, y, fl.Value, m.GLDepth));
            }

            canvas.Children.Add(new Polyline
            {
                Stroke = FLStrokeBrush,
                StrokeThickness = 1.5,
                Points = points
            });

            foreach (var (x, y, val, dep) in pointInfo)
            {
                AddHoverMarker(canvas, x, y,
                    $"GL{dep:0.00}m\nFL (L{levelIndex + 1}) = {val:0.000}", FLStrokeBrush);
            }

            double y1 = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
            double y2 = (-groundInput.GroundMassesData[^1].AltitudeDepth + pileTopAltitude) * ratio + topMargin;
            for (double fl = 0.0; fl <= FL_MAX + 1e-6; fl += 0.5)
            {
                double x = leftOrigin + fl * xScale;
                canvas.Children.Add(new Line
                {
                    Stroke = FLGridBrush,
                    StrokeThickness = 1,
                    X1 = x, X2 = x, Y1 = y1, Y2 = y2
                });

                var text = new TextBlock
                {
                    Text = fl.ToString("0.#"),
                    Foreground = FLStrokeBrush,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(text, x - text.DesiredSize.Width / 2);
                Canvas.SetTop(text, y1 - text.DesiredSize.Height);
                canvas.Children.Add(text);
            }

            var title = new TextBlock
            {
                Text = $"FL (L{levelIndex + 1})",
                Foreground = FLStrokeBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            title.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            // 右揃え (右端 = rightEnd)
            Canvas.SetLeft(title, rightEnd - title.DesiredSize.Width);
            Canvas.SetTop(title, y1 - title.DesiredSize.Height * 2.0 - 2);
            canvas.Children.Add(title);
        }

        // 地盤変位描画メソッド (杭姿図右側に重ね描き、auto-scale、左→右で値が増加)
        // withLiquefaction=true: DmaxUStarSigmaGammaCyH (液状化考慮)、false: DmaxUStar (非考慮)
        private static void DrawDisplacementValues(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin,
            int levelIndex, bool withLiquefaction)
        {
            if (groundInput?.GroundMassesData == null || groundInput.GroundMassesData.Count == 0) return;

            // 値取得関数 (考慮/非考慮 切替)
            double Get(GroundMassDataInput m)
            {
                if (withLiquefaction)
                {
                    if (m.DmaxUStarSigmaGammaCyH == null || m.DmaxUStarSigmaGammaCyH.Count <= levelIndex) return 0.0;
                    return m.DmaxUStarSigmaGammaCyH[levelIndex];
                }
                if (m.DmaxUStar == null || m.DmaxUStar.Count <= levelIndex) return 0.0;
                return m.DmaxUStar[levelIndex];
            }

            bool hasData = groundInput.GroundMassesData.Any(m => Math.Abs(Get(m)) > 1e-9);
            if (!hasData) return;

            double maxAbs = groundInput.GroundMassesData
                .Select(m => Math.Abs(Get(m)))
                .DefaultIfEmpty(0)
                .Max();
            if (maxAbs <= 0) return;

            double scaleMax = NiceCeil(maxAbs);

            double canvasWidth = canvas.ActualWidth;
            double leftOrigin = canvasWidth * 0.72;       // 変位=0
            double rightEnd   = canvasWidth * 0.96;       // 変位=scaleMax
            double xScale = (rightEnd - leftOrigin) / scaleMax;

            var points = new PointCollection();
            var pointInfo = new List<(double x, double y, double value, double depth)>();
            foreach (var m in groundInput.GroundMassesData)
            {
                double v = Math.Abs(Get(m));
                double y = (-m.AltitudeDepth + pileTopAltitude) * ratio + topMargin;
                double x = leftOrigin + v * xScale;
                points.Add(new Point(x, y));
                pointInfo.Add((x, y, Get(m), m.GLDepth));
            }

            canvas.Children.Add(new Polyline
            {
                Stroke = DispStrokeBrush,
                StrokeThickness = 1.5,
                Points = points
            });

            string labelPrefix = withLiquefaction ? "変位（液状化含む）" : "変位";
            foreach (var (x, y, val, dep) in pointInfo)
            {
                AddHoverMarker(canvas, x, y,
                    $"GL{dep:0.00}m\n{labelPrefix} (L{levelIndex + 1}) = {val:0.0} mm", DispStrokeBrush);
            }

            double y1 = (-groundInput.GroundTopAltitude + pileTopAltitude) * ratio + topMargin;
            double y2 = (-groundInput.GroundMassesData[^1].AltitudeDepth + pileTopAltitude) * ratio + topMargin;
            const int tickCount = 4;
            for (int k = 0; k <= tickCount; k++)
            {
                double v = scaleMax * k / tickCount;
                double x = leftOrigin + v * xScale;
                canvas.Children.Add(new Line
                {
                    Stroke = DispGridBrush,
                    StrokeThickness = 1,
                    X1 = x, X2 = x, Y1 = y1, Y2 = y2
                });

                var text = new TextBlock
                {
                    Text = v.ToString("0"),
                    Foreground = DispStrokeBrush,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(text, x - text.DesiredSize.Width / 2);
                Canvas.SetTop(text, y1 - text.DesiredSize.Height);
                canvas.Children.Add(text);
            }

            string titleLabel = withLiquefaction ? "変位（液状化含む）" : "変位";
            var title = new TextBlock
            {
                Text = $"{titleLabel} mm (L{levelIndex + 1})",
                Foreground = DispStrokeBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            title.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            // 右揃え (右端 = rightEnd)
            Canvas.SetLeft(title, rightEnd - title.DesiredSize.Width);
            Canvas.SetTop(title, y1 - title.DesiredSize.Height * 2.0 - 2);
            canvas.Children.Add(title);
        }

        // 一軸圧縮強度 qu (= 2 Cu) 描画メソッド (杭姿図右側に重ね描き、auto-scale、左→右で値が増加)
        // 粘性土層の Cu (kN/m²) を 2 倍した qu を、層境で階段状に表示する。
        private static readonly SolidColorBrush QuStrokeBrush = new(Color.FromRgb(120, 80, 130)); // 落ち着いた紫系
        private static readonly SolidColorBrush QuGridBrush   = new(Color.FromArgb(48, 120, 80, 130));

        private static void DrawQuValues(
            Canvas canvas, GroundInput groundInput, double pileTopAltitude, double ratio, double topMargin)
        {
            if (groundInput?.GroundLayers == null || groundInput.GroundLayers.Count == 0) return;

            // Cu>0 の層が一つもなければ描画しない
            double maxQu = groundInput.GroundLayers.Max(l => 2.0 * Math.Max(0.0, l.Cohesive));
            if (maxQu <= 0) return;

            double scaleMax = NiceCeil(maxQu);

            double canvasWidth = canvas.ActualWidth;
            double leftOrigin = canvasWidth * 0.72;       // qu = 0
            double rightEnd   = canvasWidth * 0.96;       // qu = scaleMax
            double xScale = (rightEnd - leftOrigin) / scaleMax;

            // 階段状ポリラインを構築 (各層: 上端→下端で qu 一定)
            var points = new PointCollection();
            double topAlt = groundInput.GroundTopAltitude;
            double prevQu = 2.0 * Math.Max(0.0, groundInput.GroundLayers[0].Cohesive);
            double yTop = (-topAlt + pileTopAltitude) * ratio + topMargin;
            points.Add(new Point(leftOrigin + prevQu * xScale, yTop));

            foreach (var layer in groundInput.GroundLayers)
            {
                double qu = 2.0 * Math.Max(0.0, layer.Cohesive);
                double yBot = (-layer.BottomAltitude + pileTopAltitude) * ratio + topMargin;
                // 層上端 (前層との境界) で qu が変わる場合は水平セグメントを挿入
                if (Math.Abs(qu - prevQu) > 1e-9)
                {
                    double yLayerTop = points[^1].Y;
                    points.Add(new Point(leftOrigin + qu * xScale, yLayerTop));
                }
                points.Add(new Point(leftOrigin + qu * xScale, yBot));
                prevQu = qu;
            }

            canvas.Children.Add(new Polyline
            {
                Stroke = QuStrokeBrush,
                StrokeThickness = 1.5,
                Points = points
            });

            // 各層中心にホバーマーカー (qu 値表示)
            double cumTop = topAlt;
            foreach (var layer in groundInput.GroundLayers)
            {
                double qu = 2.0 * Math.Max(0.0, layer.Cohesive);
                if (qu <= 0) { cumTop = layer.BottomAltitude; continue; }
                double yMid = (-(cumTop + layer.BottomAltitude) / 2.0 + pileTopAltitude) * ratio + topMargin;
                double xMark = leftOrigin + qu * xScale;
                AddHoverMarker(canvas, xMark, yMid,
                    $"層: {layer.Name}\nCu = {layer.Cohesive:0.#} kN/m²\nqu (= 2Cu) = {qu:0.#} kN/m²", QuStrokeBrush, invisible: true);
                cumTop = layer.BottomAltitude;
            }

            double y1 = (-topAlt + pileTopAltitude) * ratio + topMargin;
            double y2 = (-groundInput.GroundLayers[^1].BottomAltitude + pileTopAltitude) * ratio + topMargin;
            const int tickCount = 4;
            for (int k = 0; k <= tickCount; k++)
            {
                double v = scaleMax * k / tickCount;
                double x = leftOrigin + v * xScale;
                canvas.Children.Add(new Line
                {
                    Stroke = QuGridBrush,
                    StrokeThickness = 1,
                    X1 = x, X2 = x, Y1 = y1, Y2 = y2
                });

                var text = new TextBlock
                {
                    Text = v.ToString("0"),
                    Foreground = QuStrokeBrush,
                    FontSize = 10
                };
                text.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(text, x - text.DesiredSize.Width / 2);
                Canvas.SetTop(text, y1 - text.DesiredSize.Height);
                canvas.Children.Add(text);
            }

            var title = new TextBlock
            {
                Text = "qu (= 2Cu) kN/m²",
                Foreground = QuStrokeBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            title.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(title, rightEnd - title.DesiredSize.Width);
            Canvas.SetTop(title, y1 - title.DesiredSize.Height * 2.0 - 2);
            canvas.Children.Add(title);
        }

        // 値の上限を 1/2/5×10^k に切り上げ (グラフスケール用)
        private static double NiceCeil(double x)
        {
            if (x <= 0) return 1.0;
            double exp = Math.Pow(10, Math.Floor(Math.Log10(x)));
            double mantissa = x / exp;
            double niceMantissa = mantissa <= 1 ? 1 : mantissa <= 2 ? 2 : mantissa <= 5 ? 5 : 10;
            return niceMantissa * exp;
        }

        // ElementDivision描画メソッド
        private static void DrawElementDivision(
            Canvas canvas, List<double>? zs, double ratio, double topMargin)
        {
            if (zs == null || zs.Count == 0) return;
            double canvasWidth = canvas.ActualWidth;
            //double canvasHeight = canvas.ActualHeight;

            foreach (var z in zs)
            {
                double dia = 5;
                var ellipse = new Ellipse
                {
                    Width = dia,
                    Height = dia,
                    Fill = Brushes.Red,
                };
                Canvas.SetLeft(ellipse, canvasWidth * 0.5 - dia * 0.5);
                Canvas.SetTop(ellipse, topMargin + (zs[0] - z) * ratio - dia * 0.5);
                canvas.Children.Add(ellipse);
            }
        }

        private static void DrawSelectedZ(
            Canvas canvas, double z0, double? selectedZ, double ratio, double topMargin)
        {
            double canvasWidth = canvas.ActualWidth;
            //double canvasHeight = canvas.ActualHeight;

            double dia = 15;
            var ellipse = new Ellipse
            {
                Width = dia,
                Height = dia,
                Fill = null,
                Stroke = Brushes.Red,
                StrokeThickness = 1,

            };
            Canvas.SetLeft(ellipse, canvasWidth * 0.5 - dia * 0.5);
            Canvas.SetTop(ellipse, (topMargin + (z0 - (double)selectedZ) * ratio - dia * 0.5));
            canvas.Children.Add(ellipse);
        }

        private static void DrawPolygon(
            Canvas canvas, PointCollection points, Brush stroke, Brush fill, double thickness = 1)
        {
            var polygon = new Polygon
            {
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = thickness,
                Points = points
            };
            canvas.Children.Add(polygon);
        }

        private static void DrawLine(
            Canvas canvas, double x1, double y1, double x2, double y2,
            Brush stroke, int dashArray = 0, double thickness = 1)
        {
            var line = new Line
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeDashArray = [dashArray],
                X1 = x1,
                X2 = x2,
                Y1 = y1,
                Y2 = y2
            };
            canvas.Children.Add(line);
        }
    }
}
