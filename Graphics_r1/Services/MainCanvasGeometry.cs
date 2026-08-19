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
        public PathGeometry PathGeoEmbedmentNodes { get; set; } = new();

        public PathGeometry PathGeoPileElems { get; set; } = new();
        public PathGeometry PathGeoPileDividedElems { get; set; } = new();

        public PathGeometry PathGeoPileDias { get; set; } = new();
        public PathGeometry PathGeoPileDividedDias { get; set; } = new();
        // PHC節杭 / PRC節杭 の節の稜線 (立上り開始・終了位置の楕円など)。
        // 杭体の輪郭より細い線で描くため別パスに分けている。
        public PathGeometry PathGeoPileNodeDetails { get; set; } = new();
        public PathGeometry PathGeoPileDividedNodeDetails { get; set; } = new();
        public PathGeometry PathGeoPileToeInnerDashed { get; set; } = new(); // 根固め部内部の杭体（破線）
        public PathGeometry PathGeoPileToeInnerDashedDivided { get; set; } = new(); // 根固め部内部の杭体（破線・杭要素分割後）

        public PathGeometry PathGeoPileSoils { get; set; } = new();
        public PathGeometry PathGeoClay { get; set; } = new();
        public PathGeometry PathGeoSand { get; set; } = new();
        public PathGeometry PathGeoGravel { get; set; } = new();
        public PathGeometry PathGeoPileGroundWater { get; set; } = new();

        public PathGeometry PathGeoNValues { get; set; } = new();
        public PathGeometry PathGeoNValueGrids { get; set; } = new();

        // 土層・土質点パラメータグラフ背景の土層別塗りつぶし (粘性土=茶, 砂質土=橙, 礫質土=緑、半透明)
        // FillRule = Nonzero — 複数の閉ポリゴンが同じ PathGeometry に乗っても塗り抜けが起きない
        public PathGeometry PathGeoSoilParamFillClay { get; set; } = new() { FillRule = FillRule.Nonzero };
        public PathGeometry PathGeoSoilParamFillSand { get; set; } = new() { FillRule = FillRule.Nonzero };
        public PathGeometry PathGeoSoilParamFillGravel { get; set; } = new() { FillRule = FillRule.Nonzero };

        // 群杭沈下用土層 (水平視) の層別塗りつぶし。6 色のパレットを層インデックスでサイクル。
        public PathGeometry[] PathGeoSettlementSoilFills { get; } =
        [
            new() { FillRule = FillRule.Nonzero },
            new() { FillRule = FillRule.Nonzero },
            new() { FillRule = FillRule.Nonzero },
            new() { FillRule = FillRule.Nonzero },
            new() { FillRule = FillRule.Nonzero },
            new() { FillRule = FillRule.Nonzero },
        ];

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

        // 基礎梁
        public PathGeometry PathGeoFoundationBeams { get; set; } = new();
        public PathGeometry PathGeoFoundationNodes { get; set; } = new();
        public PathGeometry PathGeoConnectionNodes { get; set; } = new(); // 接合節点（杭頭+ΔZc）
        public PathGeometry PathGeoRigidConnections { get; set; } = new(); // 杭頭と接合節点を結ぶ剛体連結線
        public PathGeometry PathGeoEmbedmentRigidConnections { get; set; } = new(); // 代表節点と土圧合力節点を結ぶ剛体連結線
        public PathGeometry PathGeoInputNodesPile { get; set; } = new(); // 一般節点（Pile型・青）
        public PathGeometry PathGeoInputNodesGeneral { get; set; } = new(); // 一般節点（General型・オレンジ）
        public PathGeometry PathGeoBeamSections { get; set; } = new(); // 梁要素断面形状

        // ホバーハイライト（Clear()では消さない。MouseMoveで直接更新する）
        public PathGeometry PathGeoHoverNode { get; set; } = new();
        public PathGeometry PathGeoHoverElement { get; set; } = new();

        // クリアメソッド
        public void Clear()
        {
            PathGeoActPoint.Figures.Clear();
            PathGeoPileTopNodes.Figures.Clear();
            PathGeoSelectedPileNodes.Figures.Clear();
            PathGeoSelectedElements.Figures.Clear();
            PathGeoPileNonTopNodes.Figures.Clear();
            PathGeoPileDividedNonTopNodes.Figures.Clear();
            PathGeoEmbedmentNodes.Figures.Clear();

            PathGeoPileElems.Figures.Clear();
            PathGeoPileDividedElems.Figures.Clear();

            PathGeoPileDias.Figures.Clear();
            PathGeoPileNodeDetails.Figures.Clear();
            PathGeoPileDividedNodeDetails.Figures.Clear();
            PathGeoPileDividedDias.Figures.Clear();
            PathGeoPileToeInnerDashed.Figures.Clear();
            PathGeoPileToeInnerDashedDivided.Figures.Clear();

            PathGeoPileSoils.Figures.Clear();
            PathGeoClay.Figures.Clear();
            PathGeoSand.Figures.Clear();
            PathGeoGravel.Figures.Clear();
            PathGeoPileGroundWater.Figures.Clear();

            PathGeoNValues.Figures.Clear();
            PathGeoNValueGrids.Figures.Clear();

            PathGeoSoilParamFillClay.Figures.Clear();
            PathGeoSoilParamFillSand.Figures.Clear();
            PathGeoSoilParamFillGravel.Figures.Clear();

            foreach (var p in PathGeoSettlementSoilFills) p.Figures.Clear();

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
            PathGeoFoundationBeams.Figures.Clear();
            PathGeoFoundationNodes.Figures.Clear();
            PathGeoConnectionNodes.Figures.Clear();
            PathGeoRigidConnections.Figures.Clear();
            PathGeoEmbedmentRigidConnections.Figures.Clear();
            PathGeoInputNodesPile.Figures.Clear();
            PathGeoInputNodesGeneral.Figures.Clear();
            PathGeoBeamSections.Figures.Clear();
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

        // 空 PathGeometry の Path 追加をスキップするヘルパー (描画コスト削減)。
        // PathGeometry.Figures.Count == 0 の場合は WPF が見えない Path をレイアウト/描画
        // パスに通すコストが無駄。図形が無いセクション (例: 根入れ非表示時の PathGeoEmbedment*)
        // はそのまま追加すらしない。
        private static void AddPath(Canvas canvas, PathGeometry data, Brush stroke,
            double thickness, string name = "Path", DoubleCollection dashArray = null,
            Brush fill = null, bool isHitTestVisible = true)
        {
            if (data == null || data.Figures.Count == 0) return;
            var p = new Path
            {
                Stroke = stroke,
                StrokeThickness = thickness,
                Data = data,
                Name = name,
            };
            if (dashArray != null) p.StrokeDashArray = dashArray;
            if (fill != null) p.Fill = fill;
            if (!isHitTestVisible) p.IsHitTestVisible = false;
            canvas.Children.Add(p);
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

            // 土圧合力節点（根入れ部上下面）: 根入れ非表示時に空 → AddPath で skip
            AddPath(canvas, PathGeoEmbedmentNodes, NikkenBrush.SkyBlue, 0.5, "EmbedmentNode");

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

            // 根入部の対角線 (根入れ非表示時に空)
            AddPath(canvas, PathGeoEmbedmentDiagonals, NikkenBrush.Yellow, 0.5,
                "EmbedmentDiagonal", dashArray: new DoubleCollection { 4, 2 });
            AddPath(canvas, PathGeoDividedEmbedmentDiagonals, NikkenBrush.SkyBlue, 0.5,
                "EmbedmentDiagonal", dashArray: new DoubleCollection { 4, 2 });

            // 杭径
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Orange,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoPileDias,
                Name = "Node"
            });

            // 杭要素分割後杭径
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = pileStrokeThickness,
                Data = PathGeoPileDividedDias,
                Name = "Node"
            });

            // 節杭の節の稜線 (杭体輪郭より細い線)
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Orange,
                StrokeThickness = pileStrokeThickness * 0.5,
                Data = PathGeoPileNodeDetails,
                Name = "Node"
            });
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = pileStrokeThickness * 0.5,
                Data = PathGeoPileDividedNodeDetails,
                Name = "Node"
            });

            // 根固め部内部の杭体（破線）
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Orange,
                StrokeThickness = pileStrokeThickness,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Data = PathGeoPileToeInnerDashed,
                Name = "Node"
            });

            // 根固め部内部の杭体（破線・杭要素分割後）
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.SkyBlue,
                StrokeThickness = pileStrokeThickness,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Data = PathGeoPileToeInnerDashedDivided,
                Name = "Node"
            });

            // 群杭沈下用土層 (水平視) の層別塗りつぶし — 線より背面に配置して透ける形にする
            // パレット: 6 色サイクル (alpha=72 で控えめな半透明)
            Color[] settlementFillPalette =
            [
                Color.FromArgb(72, 210, 180, 140), // light beige (clay)
                Color.FromArgb(72, 255, 200, 120), // light orange (sand)
                Color.FromArgb(72, 170, 220, 170), // light green (gravel)
                Color.FromArgb(72, 170, 200, 230), // light blue
                Color.FromArgb(72, 220, 180, 220), // light purple
                Color.FromArgb(72, 240, 230, 160), // light yellow
            ];
            for (int i = 0; i < PathGeoSettlementSoilFills.Length; i++)
            {
                canvas.Children.Add(new Path()
                {
                    Fill = new SolidColorBrush(settlementFillPalette[i]),
                    Data = PathGeoSettlementSoilFills[i],
                    Name = "SettlementSoilFill",
                    IsHitTestVisible = false,
                });
            }

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

            // 土層パラメータグラフ背景の土層別塗りつぶし
            // (粘性土=薄茶、砂質土=薄橙、礫質土=薄緑、各 alpha=64)
            // N値ポリラインより手前に追加 → ポリライン・数値が下から透けず読みやすくなる
            canvas.Children.Add(new Path()
            {
                Fill = new SolidColorBrush(Color.FromArgb(64, 210, 180, 140)),
                Data = PathGeoSoilParamFillClay,
                Name = "SoilParamFill",
                IsHitTestVisible = false,
            });
            canvas.Children.Add(new Path()
            {
                Fill = new SolidColorBrush(Color.FromArgb(64, 255, 165, 0)),
                Data = PathGeoSoilParamFillSand,
                Name = "SoilParamFill",
                IsHitTestVisible = false,
            });
            canvas.Children.Add(new Path()
            {
                Fill = new SolidColorBrush(Color.FromArgb(64, 144, 238, 144)),
                Data = PathGeoSoilParamFillGravel,
                Name = "SoilParamFill",
                IsHitTestVisible = false,
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

            // 矩形荷重 (沈下解析未設定時に空)
            AddPath(canvas, PathGeoRectLoads, Brushes.DarkGreen, 0.5, "RectLoads");

            // 沈下グリッド (沈下解析未設定時に空)
            AddPath(canvas, PathGeoSettlementGrid, Brushes.Gray, 0.51, "SettlementGrid",
                dashArray: new DoubleCollection { 8, 4 });


            // 要素
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Gold,
                StrokeThickness = 0.5,
                Data = PathElements,
                Name = "Elements",
            });

            // 基礎梁 (基礎梁未定義時に空)
            AddPath(canvas, PathGeoFoundationBeams, Brushes.DarkOrange, 1.0, "FoundationBeam");

            // 梁要素断面形状
            AddPath(canvas, PathGeoBeamSections,
                new SolidColorBrush(Color.FromArgb(180, 139, 69, 19)), 0.8, "BeamSection");

            // 基礎梁節点
            AddPath(canvas, PathGeoFoundationNodes, Brushes.Orange, 0.5,
                "FoundationNode", fill: Brushes.Orange);

            // 接合節点（杭頭+ΔZc位置）
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Green,
                Fill = Brushes.White,
                StrokeThickness = 1.0,
                Data = PathGeoConnectionNodes,
                Name = "ConnectionNode"
            });

            // 杭頭と接合節点を結ぶ剛体連結線（細い灰色破線）
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Gray,
                StrokeThickness = 0.5,
                StrokeDashArray = [2, 2], // 破線パターン
                Data = PathGeoRigidConnections,
                Name = "RigidConnection"
            });

            // 代表節点と土圧合力節点を結ぶ剛体連結線（緑）
            canvas.Children.Add(new Path()
            {
                Stroke = NikkenBrush.Green,
                StrokeThickness = 0.5,
                Data = PathGeoEmbedmentRigidConnections,
                Name = "EmbedmentRigidConnection"
            });

            // 一般節点（Pile型・青）
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Blue,
                Fill = Brushes.LightBlue,
                StrokeThickness = 2.0,
                Data = PathGeoInputNodesPile,
                Name = "InputNodePile"
            });

            // 一般節点（General型・オレンジ）
            canvas.Children.Add(new Path()
            {
                Stroke = Brushes.Orange,
                Fill = Brushes.LightYellow,
                StrokeThickness = 2.0,
                Data = PathGeoInputNodesGeneral,
                Name = "InputNodeGeneral"
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

            // ホバーハイライト (節点 / 要素): 通常空、マウスオーバー時のみ figure あり。
            // AddPath で空時の追加をスキップ → 通常時は不要な Path 2 件分の追加コスト削減
            AddPath(canvas, PathGeoHoverNode,
                stroke: new SolidColorBrush(Color.FromArgb(200, 0, 120, 215)),
                thickness: 1.5,
                fill: new SolidColorBrush(Color.FromArgb(40, 0, 120, 215)),
                isHitTestVisible: false,
                name: "HoverNode");

            AddPath(canvas, PathGeoHoverElement,
                stroke: new SolidColorBrush(Color.FromArgb(200, 0, 120, 215)),
                thickness: 2.5,
                isHitTestVisible: false,
                name: "HoverElement");
        }
    }

    public class TextBlockInfo
    {
        public TextBlock TextBlock { get; set; }
        // 軽量フィールド（TextBlock不要時に使用）
        public string Text { get; set; }
        public double FontSize { get; set; }
        public Brush Foreground { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double TextAngle { get; set; }
        public double ScaleY { get; set; }
    }
}







