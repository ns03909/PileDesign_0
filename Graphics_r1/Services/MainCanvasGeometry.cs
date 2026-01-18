using PileDesign.Common;
using PileDesign.Models;
using PileDesign.ViewModels; // ViewModelのusingを追加
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PileDesign.Services
{
    public class MainCanvasGeometry(MainWindowViewModel viewModel) : BaseModel
    {
        private readonly MainWindowViewModel _viewModel = viewModel;

        // 慣性力作用点
        public PathGeometry PathGeoActPoint { get; set; } = new();

        // 杭頭
        public PathGeometry PathGeoPileTopNodes { get; set; } = new();
        public PathGeometry PathGeoSelectedPileNodes { get; set; } = new() { FillRule = FillRule.Nonzero };
        public PathGeometry PathGeoSelectedElements { get; set; } = new();

        public PathGeometry PathGeoPileNonTopNodes { get; set; } = new();
        public PathGeometry PathGeoPileDividedNonTopNodes { get; set; } = new();

        public PathGeometry PathGeoPileElems { get; set; } = new();
        public PathGeometry PathGeoPileDividedElems { get; set; } = new();

        public PathGeometry PathGeoPileDias { get; set; } = new();
        public PathGeometry PathGeoPileDividedDias { get; set; } = new();

        public PathGeometry PathGeoPileSoils { get; set; } = new();
        public PathGeometry PathGeoClay { get; set; } = new();
        public PathGeometry PathGeoSand { get; set; } = new();
        public PathGeometry PathGeoGravel { get; set; } = new();
        public PathGeometry PathGeoPileGroundWater { get; set; } = new();

        public PathGeometry PathGeoNValues { get; set; } = new();
        public PathGeometry PathGeoNValueGrids { get; set; } = new();

        public PathGeometry PathGeoGroundDisp { get; set; } = new();
        public PathGeometry PathGeoDisp { get; set; } = new();
        public PathGeometry PathGeoAxisX { get; set; } = new();
        public PathGeometry PathGeoAxisY { get; set; } = new();
        public PathGeometry PathGeoAxisZ { get; set; } = new();
        public PathGeometry PathGeoAxisXM { get; set; } = new();
        public PathGeometry PathGeoAxisYM { get; set; } = new();
        public PathGeometry PathGeoAxisZM { get; set; } = new();



        // 根入部
        public PathGeometry PathGeoEmbedmenSides { get; set; } = new();
        public PathGeometry PathGeoDividedEmbedmenSides { get; set; } = new();
        public PathGeometry PathGeoEmbedmentDiagonals { get; set; } = new();
        public PathGeometry PathGeoDividedEmbedmentDiagonals { get; set; } = new();

        // 通り心
        public PathGeometry PathGeoGridLines { get; set; } = new();
        public PathGeometry PathGeoSoildGridLines { get; set; } = new();

        // 目盛り
        public PathGeometry PathGeoTicks { get; set; } = new();

        // 要素
        public PathGeometry PathElements { get; set; } = new();

        // 沈下検討用群杭荷重
        public PathGeometry PathGeoRectLoads { get; set; } = new();

        // 沈下検討用グリッド
        public PathGeometry PathGeoSettlementGrid { get; set; } = new();

        // 剛床仮定
        public PathGeometry PathGeoRigidFloor { get; set; } = new();

        // クリアメソッド
        public void Clear()
        {
            PathGeoActPoint.Figures.Clear();
            PathGeoPileTopNodes.Figures.Clear();
            PathGeoSelectedPileNodes.Figures.Clear();
            PathGeoSelectedElements.Figures.Clear();
            PathGeoPileNonTopNodes.Figures.Clear();
            PathGeoPileDividedNonTopNodes.Figures.Clear();

            PathGeoPileElems.Figures.Clear();
            PathGeoPileDividedElems.Figures.Clear();

            PathGeoPileDias.Figures.Clear();
            PathGeoPileDividedDias.Figures.Clear();

            PathGeoPileSoils.Figures.Clear();
            PathGeoClay.Figures.Clear();
            PathGeoSand.Figures.Clear();
            PathGeoGravel.Figures.Clear();
            PathGeoPileGroundWater.Figures.Clear();

            PathGeoNValues.Figures.Clear();
            PathGeoNValueGrids.Figures.Clear();

            PathGeoGroundDisp.Figures.Clear();

            PathGeoAxisX.Figures.Clear();
            PathGeoAxisY.Figures.Clear();
            PathGeoAxisZ.Figures.Clear();
            PathGeoAxisXM.Figures.Clear();
            PathGeoAxisYM.Figures.Clear();
            PathGeoAxisZM.Figures.Clear();

            PathGeoDisp.Figures.Clear();

            PathGeoEmbedmenSides.Figures.Clear();
            PathGeoDividedEmbedmenSides.Figures.Clear();
            PathGeoEmbedmentDiagonals.Figures.Clear();
            PathGeoDividedEmbedmentDiagonals.Figures.Clear();

            PathGeoGridLines.Figures.Clear();
            PathGeoSoildGridLines.Figures.Clear();
            PathGeoTicks.Figures.Clear();
            PathElements.Figures.Clear();
            PathGeoRectLoads.Figures.Clear();
            PathGeoSettlementGrid.Figures.Clear();
            PathGeoRigidFloor.Figures.Clear();
        }


        // 複数の長方形ジオメトリをpathgeometryに追加するメソッド
        public static void AddRectanglesToPathGeometry(PathGeometry pathgeometry, List<PathFigure> rectangles)
        {
            foreach (PathFigure rectangle in rectangles)
            {
                pathgeometry.Figures.Add(rectangle);
            }
        }

        public void DrawPileTopNodes(Canvas canvas)
        {
            //PathGeoPileTopNodes.Figures.Clear();

            // 杭頭節点
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Data = PathGeoPileTopNodes,
                Name = "Node"
            });
        }
        public void DrawElemPath(Canvas canvas)
        {

            // 杭要素
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = 0.5,
                Data = PathGeoPileElems,
                Name = "Node"
            });

            // 分割後杭要素
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 0.5,
                Data = PathGeoPileDividedElems,
                Name = "Node"
            });

            // 根入部の対角線
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = 0.5,
                Data = PathGeoEmbedmentDiagonals,
                Name = "EmbedmentDiagonal",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 分割後根入部の対角線
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 0.5,
                Data = PathGeoDividedEmbedmentDiagonals,
                Name = "EmbedmentDiagonal",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });
        }

        public void DrawAllPaths(Canvas canvas, double pileStrokeThickness, double soilStrokeThickness)
        {
            if (canvas == null)
            { return; }

            //// 重要: 描画前に Canvas.Children をクリアして重複追加を防ぐ
            //canvas.Children.Clear();
            // ★ Path だけを削除し、Rectangle などオーバーレイは残す
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
            {
                if (canvas.Children[i] is Path)
                {
                    canvas.Children.RemoveAt(i);
                }
            }

            // 杭頭節点
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Data = PathGeoPileTopNodes,
                Name = "Node"
            });

            // 代表節点
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Green,
                StrokeThickness = 0.5,
                Fill = NikkenBrush.Green,
                Data = PathGeoActPoint,
                Name = "ActPoint"
            });

            // 選択節点
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                Fill = Brushes.Red,
                //Fill = new SolidColorBrush(Colors.Red) { Opacity = 0.5 },
                Data = PathGeoSelectedPileNodes,
                Name = "Selection"
            });

            // 選択要素
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Data = PathGeoSelectedElements,
                Name = "Selection"
            });

            // 杭頭以外節点
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = 0.5,
                Data = PathGeoPileNonTopNodes,
                Name = "Node"
            });

            // 分割後杭頭以外節点
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 0.5,
                Data = PathGeoPileDividedNonTopNodes,
                Name = "Node"
            });

            // 杭要素
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = 0.5,
                Data = PathGeoPileElems,
                Name = "Node"
            });

            // 分割後杭要素
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 0.5,
                Data = PathGeoPileDividedElems,
                Name = "Node"
            });

            // 根入部の対角線
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = 0.5,
                Data = PathGeoEmbedmentDiagonals,
                Name = "EmbedmentDiagonal",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 分割後根入部の対角線
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = 0.5,
                Data = PathGeoDividedEmbedmentDiagonals,
                Name = "EmbedmentDiagonal",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 杭径
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Orange,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoPileDias,
                Name = "Node"
            });

            // 要素分割後杭径
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoPileDividedDias,
                Name = "Node"
            });

            // 杭周土層
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Gray, /*new SolidColorBrush(Color.FromRgb(176, 176, 176)),*/
                StrokeThickness = soilStrokeThickness,
                Data = PathGeoPileSoils,
                Name = "Soil",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 粘性土
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SaddleBrown,
                StrokeThickness = soilStrokeThickness,
                Data = PathGeoClay,
                Name = "Soil",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });
            // 砂質土
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SandyBrown,
                StrokeThickness = soilStrokeThickness,
                Data = PathGeoSand,
                Name = "Soil",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });
            // 礫質土
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkGreen,
                StrokeThickness = soilStrokeThickness,
                Data = PathGeoGravel,
                Name = "Soil",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 杭周地下水位
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.DeepBlue,
                StrokeThickness = soilStrokeThickness,
                Data = PathGeoPileGroundWater,
                Name = "Soil",
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // N値
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Black,
                //StrokeThickness = soilStrokeThickness,
                Data = PathGeoNValues,
                StrokeThickness = 0.5,
                Name = "NValue",
                //StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // N値Grids
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Black,
                Data = PathGeoNValueGrids,
                Name = "NValue",
                StrokeThickness = 0.5,
                StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });

            // 根入部
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Yellow,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoEmbedmenSides,
                Name = "Embedment",
            });

            // 分割後根入部
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.SkyBlue,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoDividedEmbedmenSides,
                Name = "Embedment",
            });
            //Canvas3DLayout.Children.Add(path);

            //// 根入部の対角線
            //canvas.Children.Add(new Path()
            //{
            //    Stroke = Brushes.Orange,
            //    StrokeThickness = 0.5,
            //    Data = PathGeoEmbedmentDiagonals,
            //    Name = "EmbedmentDiagonal",
            //    StrokeDashArray = [4, 2] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            //});

            // 矩形荷重
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkGreen,
                StrokeThickness = 0.5,
                Data = PathGeoRectLoads,
                Name = "RectLoads",
            });


            // 沈下グリッド
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 0.51,
                Data = PathGeoSettlementGrid,
                Name = "SettlementGrid",
                StrokeDashArray = [8, 4] // 破線パターンを設定 (例: ダッシュが4、ギャップが2)
            });


            // 要素
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Gold,
                StrokeThickness = 0.5,
                Data = PathElements,
                Name = "Elements",
            });


            //通り心X
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Purple,
                StrokeThickness = 0.5,
                Data = PathGeoGridLines,
                Name = "GridY",
                StrokeDashArray = [100, 5, 5, 5] // 4ユニットの線と2ユニットのスペース
            });

            //通り心Y
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Purple,
                StrokeThickness = 0.5,
                Data = PathGeoSoildGridLines,
                Name = "GridY",
            });

            // 目盛り
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Purple,
                StrokeThickness = 0.5,
                Data = PathGeoTicks,
                Name = "TickMark"
            });

            // 剛床
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Green,
                StrokeThickness = 0.5,
                Data = PathGeoRigidFloor,
                //StrokeDashArray = [10, 10]
                //Name = "TickMark"
            });

            // 
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.RosyBrown,
                StrokeThickness = 0.5,
                Data = PathGeoGroundDisp,
                Name = "GroundDisp"

            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkCyan,
                StrokeThickness = 0.5,
                Data = PathGeoDisp,
                Name = "NodalDisp"
            });


            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                Data = PathGeoAxisX,
                Name = "AxisX"
            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Green,
                StrokeThickness = 1,
                Data = PathGeoAxisY,
                Name = "AxisY"
            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 1,
                Data = PathGeoAxisZ,
                Name = "AxisZ"
            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkRed,
                StrokeThickness = 0.5,
                Data = PathGeoAxisXM,
                Name = "AxisXM"
            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkGreen,
                StrokeThickness = 0.5,
                Data = PathGeoAxisYM,
                Name = "AxisYM"
            });

            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.DarkBlue,
                StrokeThickness = 0.5,
                Data = PathGeoAxisZM,
                Name = "AxisZM"
            });
        }
    }

    public class TextBlockInfo
    {
        public TextBlock TextBlock { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double TextAngle { get; set; }
        public double ScaleY { get; set; }
    }
}







