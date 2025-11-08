using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;


namespace PileDesign.Models.InputData
{
    public class PileGroupSettlement : BaseModel
    {
        private double _loadingPlaneAltitude;
        public double LoadingPlaneAltutude
        {
            get => _loadingPlaneAltitude;
            set => SetProperty(ref _loadingPlaneAltitude, value);
        }

        public List<string> LoadingTypeOptions { get; set; } = ["任意矩形", "個別十字", "なし"];

        private string _loadingType;
        public string LoadingType
        {
            get => _loadingType;
            set => SetProperty(ref _loadingType, value);
        }

        private ObservableCollection<RectLoad> _rectLoads;
        public ObservableCollection<RectLoad> RectLoads
        {
            get => _rectLoads;
            set => SetProperty(ref _rectLoads, value);
        }

        private ObservableCollection<SettlementSoilLayer> _settlementSoilLayers;
        public ObservableCollection<SettlementSoilLayer> SettlementSoilLayers
        {
            get => _settlementSoilLayers;
            set => SetProperty(ref _settlementSoilLayers, value);
        }


        // グリッド
        private ObservableCollection<SettlementGridDataItem> _settlementGridData;
        public ObservableCollection<SettlementGridDataItem> SettlementGridData
        {
            get => _settlementGridData;
            set => SetProperty(ref _settlementGridData, value);
        }

        // グリッドX
        private ObservableCollection<double> _settlementGridX;
        public ObservableCollection<double> SettlementGridX
        {
            get => _settlementGridX;
            set => SetProperty(ref _settlementGridX, value);
        }

        // グリッドY
        private ObservableCollection<double> _settlementGridY;
        public ObservableCollection<double> SettlementGridY
        {
            get => _settlementGridY;
            set => SetProperty(ref _settlementGridY, value);
        }

        //コンストラクタ
        public PileGroupSettlement()
        {
            LoadingPlaneAltutude = -5.0; /// 5m
            LoadingType = "任意矩形";
            RectLoads = [];
            SettlementSoilLayers = [];
            SettlementGridData = [];
        }

        // SettlementGridDataから特定のXまたはYのデータを返す
        public ObservableCollection<SettlementGridDataItem> GetSpecificSettlementDataItems(double position, double angle)
        {
            ObservableCollection<SettlementGridDataItem> specificSettlementDataItems = [];
            foreach (var settlementDataItem in SettlementGridData)
            {
                if (angle == 0) // grid X
                {
                    if (Math.Abs(settlementDataItem.X - position) < 1e-5)
                    {
                        specificSettlementDataItems.Add(settlementDataItem);
                        continue;
                    }
                }
                else if (angle == 90) // grid Y
                {
                    if (Math.Abs(settlementDataItem.Y - position) < 1e-5)
                    {
                        specificSettlementDataItems.Add(settlementDataItem);
                        continue;
                    }
                }
            }
            return specificSettlementDataItems;
        }

        // GridDataSettlementの値を0にするメソッド
        public void RemoveGridDataSettlement()
        {
            foreach (var settlementGridDataItem in SettlementGridData)
            {
                settlementGridDataItem.Settlement = 0;
            }
        }


        //
        public void SetGridX(double xmin, double xmax, double xOffset, double xSpacing, ObservableCollection<GridDataItem> gridItems)
        {
            SettlementGridX = GetCoord(xmin, xmax, xOffset, xSpacing, gridItems);
        }

        //
        public void SetGridY(double ymin, double ymax, double yOffset, double ySpacing, ObservableCollection<GridDataItem> gridItems)
        {
            SettlementGridY = GetCoord(ymin, ymax, yOffset, ySpacing, gridItems);
        }

        public static ObservableCollection<double> GetCoord(double min, double max, double offset, double spacing, ObservableCollection<GridDataItem> gridItems)
        {
            // spacingが0以下、またはminとmaxが等しい場合は1点のみ返す
            if (spacing <= 0 /*|| Math.Abs(max - min) < 1e-8*/)
            {
                return [min];
            }

            // 値を集めて昇順ソート＆重複除去
            var xs = new List<double> { min - offset };
            foreach (var item in gridItems)
            {
                xs.Add(item.Coord);
            }
            xs.Add(max + offset);

            xs = [.. xs.Distinct().OrderBy(x => x)];

            // 分割点を追加
            ObservableCollection<double> xsWithDivisions = [];
            for (int i = 0; i < xs.Count - 1; i++)
            {
                xsWithDivisions.Add(xs[i]);
                double gap = xs[i + 1] - xs[i];
                if (gap > spacing)
                {
                    int nDiv = (int)Math.Ceiling(gap / spacing);
                    double step = gap / nDiv;
                    for (int k = 1; k < nDiv; k++)
                    {
                        xsWithDivisions.Add(xs[i] + step * k);
                    }
                }
            }
            xsWithDivisions.Add(xs[^1]); // 最後の点

            // すべて同じ値なら1点だけ返す
            if (xsWithDivisions.Distinct().Count() == 1)
            {
                return [xsWithDivisions[0]];
            }

            return xsWithDivisions;
        }

        // 矩形荷重面の大正方形辺長a, 長方形長辺長b, 長方形短辺長cを返すメソッド
        public static (double, double, double) GetCrossDimensions(double radius)
        {
            double b = Math.Sqrt(Math.PI * Math.Pow(radius, 2) / (4 + 2 * Math.Sqrt(2)));
            double c = b / 4.0;
            double a = b * (1 + Math.Sqrt(2));
            return (a, b, c);
        }

        // 円形荷重面を近似した5矩形の対角頂点の座標を返すメソッド
        public static List<(Point3D, Point3D)> GetFiveRectsPoints(Point3D point, double radius)
        {
            var (a, b, c) = GetCrossDimensions(radius);
            var halfA = a / 2;
            var halfB = b / 2;

            return
            [
                (point + new Vector3D(-halfA, -halfA, 0), point + new Vector3D(halfA, halfA, 0)),
                (point + new Vector3D(-halfB, halfA, 0), point + new Vector3D(halfB, halfA + c, 0)),
                (point + new Vector3D(-halfB, -halfA, 0), point + new Vector3D(halfB, -halfA - c, 0)),
                (point + new Vector3D(halfA, halfB, 0), point + new Vector3D(halfA + c, -halfB, 0)),
                (point + new Vector3D(-halfA, halfB, 0), point + new Vector3D(-halfA - c, -halfB, 0))
            ];
        }

        // 十字断面矩形荷重面を返すメソッド
        public static ObservableCollection<RectLoad> GetCrossRectLoads(Point point, double radius, double qa)
        {
            var (a, b, c) = GetCrossDimensions(radius);
            double denominator = Math.Pow(a, 2) + 4 * b * c;
            double qaA = qa * Math.Pow(a, 2) / denominator;
            double qaBC = qa * b * c / denominator;

            return
            [
                new RectLoad
                {
                    X1 = point.X - a * 0.5,
                    X2 = point.X + a * 0.5,
                    Y1 = point.Y - a * 0.5,
                    Y2 = point.Y + a * 0.5,
                    QA = qaA,
                },
                new RectLoad
                {
                    X1 = point.X - b * 0.5,
                    X2 = point.X + b * 0.5,
                    Y1 = point.Y + a * 0.5,
                    Y2 = point.Y + a * 0.5 + c,
                    QA = qaBC,
                },
                new RectLoad
                {
                    X1 = point.X - b * 0.5,
                    X2 = point.X + b * 0.5,
                    Y1 = point.Y - a * 0.5 - c,
                    Y2 = point.Y - a * 0.5,
                    QA = qaBC,
                },
                new RectLoad
                {
                    X1 = point.X + a * 0.5,
                    X2 = point.X + a * 0.5 + c,
                    Y1 = point.Y - b * 0.5,
                    Y2 = point.Y + b * 0.5,
                    QA = qaBC,
                },
                new RectLoad
                {
                    X1 = point.X - a * 0.5 - c,
                    X2 = point.X - a * 0.5,
                    Y1 = point.Y - b * 0.5,
                    Y2 = point.Y + b * 0.5,
                    QA = qaBC,
                }
            ];
        }
    }

    public class RectLoad : BaseModel
    {
        private double _x1;
        public double X1
        {
            get => _x1;
            set
            {
                if (SetProperty(ref _x1, value))
                {
                    _x1 = value;
                    OnPropertyChanged(nameof(X1));
                    OnPropertyChanged(nameof(DX));
                    OnPropertyChanged(nameof(A));
                    OnPropertyChanged(nameof(Q));
                }
            }
        }

        private double _x2;
        public double X2
        {
            get => _x2;
            set
            {
                if (SetProperty(ref _x2, value))
                {
                    _x2 = value;
                    OnPropertyChanged(nameof(X2));
                    OnPropertyChanged(nameof(DX));
                    OnPropertyChanged(nameof(A));
                    OnPropertyChanged(nameof(Q));
                }
            }
        }

        private double _y1;
        public double Y1
        {
            get => _y1;
            set
            {
                if (SetProperty(ref _y1, value))
                {
                    _y1 = value;
                    OnPropertyChanged(nameof(Y1));
                    OnPropertyChanged(nameof(DY));
                    OnPropertyChanged(nameof(A));
                    OnPropertyChanged(nameof(Q));
                }
            }
        }

        private double _y2;
        public double Y2
        {
            get => _y2;
            set
            {
                if (SetProperty(ref _y2, value))
                {
                    _y2 = value;
                    OnPropertyChanged(nameof(Y2));
                    OnPropertyChanged(nameof(DY));
                    OnPropertyChanged(nameof(A));
                    OnPropertyChanged(nameof(Q));
                }
            }
        }

        private double _qA;
        public double QA
        {
            get => _qA;
            set
            {
                if (SetProperty(ref _qA, value))
                {
                    _qA = value;
                    OnPropertyChanged(nameof(QA));
                    OnPropertyChanged(nameof(Q));
                }
            }
        }

        public double DX => X2 - X1;
        public double DY => Y2 - Y1;
        public double A => DX * DY;
        public double Q => A > 0 ? QA / A : 0;
    }


    public class SettlementSoilLayer : BaseModel
    {
        private double _bottomAltitude;
        public double BottomAltitude
        {
            get => _bottomAltitude;
            set => SetProperty(ref _bottomAltitude, value);
        }

        private double _thickness;
        public double Thickness
        {
            get => _thickness;
            set => SetProperty(ref _thickness, value);
        }

        private double _poissonsRatio;
        public double PoissonsRatio
        {
            get => _poissonsRatio;
            set => SetProperty(ref _poissonsRatio, value);
        }

        private double _ek;
        public double Ek
        {
            get => _ek;
            set => SetProperty(ref _ek, value);
        }
    }



    public class Steinnbrener : BaseModel
    {

        // コンストラクタ
        public Steinnbrener()
        { }

        // 多層地盤における矩形載荷面隅角部の沈下量の計算
        private static double Steinbrenner(ObservableCollection<SettlementSoilLayer> soilLayers, double q, double dx, double dy)
        {
            double sE = 0; ;

            double b = Math.Min(dx, dy);
            if (b < 1e-10)
            {
                return 0;
            }

            double l = Math.Max(dx, dy) / b;
            double d;
            double h;
            double f1;
            double f2;
            double nu;
            double sqrtl2plus1;
            double sqrtl2plusd2;
            double sqrt1plusd2;
            double sqrtl2plusd2plus1;
            double Is1;
            double Is2;

            for (int i = 0; i < soilLayers.Count; i++)
            {
                h = 0;
                for (int j = 0; j <= i; j++)
                {
                    h += soilLayers[j].Thickness;
                }

                d = h / b;
                sqrtl2plus1 = Math.Sqrt(Math.Pow(l, 2) + 1.0);
                sqrtl2plusd2 = Math.Sqrt(Math.Pow(l, 2) + Math.Pow(d, 2));
                sqrt1plusd2 = Math.Sqrt(1.0 + Math.Pow(d, 2));
                sqrtl2plusd2plus1 = Math.Sqrt(Math.Pow(l, 2) + Math.Pow(d, 2) + 1.0);

                f1 = 1.0 / Math.PI * (l * Math.Log((1.0 + sqrtl2plus1) * sqrtl2plusd2 / l / (1.0 + sqrtl2plusd2plus1))
                    + Math.Log((l + sqrtl2plus1) * sqrt1plusd2 / (l + sqrtl2plusd2plus1)));
                f2 = d / 2 / Math.PI * Math.Atan(l / d / sqrtl2plusd2plus1);
                nu = soilLayers[i].PoissonsRatio;
                Is1 = (1 - Math.Pow(nu, 2)) * f1 + (1 - nu - 2 * Math.Pow(nu, 2)) * f2;
                sE += q * b * Is1 / soilLayers[i].Ek;

                if (i != 0)
                {
                    h = 0;
                    for (int j = 0; j <= i - 1; j++)
                    {
                        h += soilLayers[j].Thickness;
                    }

                    d = h / b;
                    sqrtl2plus1 = Math.Sqrt(Math.Pow(l, 2) + 1.0);
                    sqrtl2plusd2 = Math.Sqrt(Math.Pow(l, 2) + Math.Pow(d, 2));
                    sqrt1plusd2 = Math.Sqrt(1.0 + Math.Pow(d, 2));
                    sqrtl2plusd2plus1 = Math.Sqrt(Math.Pow(l, 2) + Math.Pow(d, 2) + 1.0);

                    f1 = 1.0 / Math.PI * (l * Math.Log((1.0 + sqrtl2plus1) * sqrtl2plusd2 / l / (1.0 + sqrtl2plusd2plus1))
                    + Math.Log((l + sqrtl2plus1) * sqrt1plusd2 / (l + sqrtl2plusd2plus1)));
                    f2 = d / 2 / Math.PI * Math.Atan(l / d / sqrtl2plusd2plus1);
                    nu = soilLayers[i].PoissonsRatio;
                    Is2 = (1 - Math.Pow(nu, 2)) * f1 + (1 - nu - 2 * Math.Pow(nu, 2)) * f2;
                    sE -= q * b * Is2 / soilLayers[i].Ek;
                }
            }
            return sE;
        }

        public static double CalcSettlement(Point point, ObservableCollection<RectLoad> rectLoads, ObservableCollection<SettlementSoilLayer> soilLayers)
        {
            //'矩形載荷面の隅角部の沈下の組み合わせ
            //'B:基礎の短辺長さ　(m)
            //'L:基礎の長辺長さ　(m)

            //'沈下を求める点
            double s = 0;
            double sa;
            double sb;
            double sc;
            double sd;
            double ds;
            double q;
            double x0;
            double y0;
            double x1;
            double x2;
            double y1;
            double y2;

            foreach (RectLoad rectLoad in rectLoads)
            {
                x0 = point.X;//'x0:沈下量を求める点
                y0 = point.Y;//'y0:沈下量を求める点
                q = rectLoad.Q;
                x1 = rectLoad.X1;//'x1:矩形載荷面
                x2 = rectLoad.X2;//'x2:矩形載荷面
                y1 = rectLoad.Y1;//'y1:矩形載荷面
                y2 = rectLoad.Y2;//'y2:矩形載荷面
                if (Math.Abs(x1 - x2) < 1e-10 || Math.Abs(y1 - y2) < 1e-10)
                {
                    ds = 0;
                }
                //'下左'沈下を求める点が載荷面の外側にある場合
                else if (x0 < x1 && y0 < y1)
                {
                    sa = Steinbrenner(soilLayers, q, x2 - x0, y2 - y0);
                    sb = Steinbrenner(soilLayers, q, x1 - x0, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x2 - x0, y1 - y0);
                    sd = Steinbrenner(soilLayers, q, x1 - x0, y1 - y0);
                    ds = sa - sb - sc + sd;
                }

                //'下中'沈下を求める点が載荷面の外側にある場合
                else if ((x1 <= x0 && x0 <= x2) && y0 < y1)
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y2 - y0);
                    sb = Steinbrenner(soilLayers, q, x2 - x0, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x0 - x1, y1 - y0);
                    sd = Steinbrenner(soilLayers, q, x2 - x0, y1 - y0);
                    ds = sa + sb - sc - sd;
                }

                //'下右'沈下を求める点が載荷面の外側にある場合
                else if (x1 < x0 && y0 < y1)
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y2 - y0);
                    sb = Steinbrenner(soilLayers, q, x0 - x2, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x0 - x1, y1 - y0);
                    sd = Steinbrenner(soilLayers, q, x0 - x2, y1 - y0);
                    ds = sa - sb - sc + sd;
                }

                //'中左'沈下を求める点が載荷面の外側にある場合
                else if (x0 < x1 && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(soilLayers, q, x2 - x0, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x2 - x0, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x1 - x0, y0 - y1);
                    sd = Steinbrenner(soilLayers, q, x1 - x0, y2 - y0);
                    ds = sa + sb - sc - sd;
                }

                //'中中 '沈下を求める点が載荷面の内側にある場合
                else if ((x1 <= x0 && x0 <= x2) && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x0 - x1, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x2 - x0, y0 - y1);
                    sd = Steinbrenner(soilLayers, q, x2 - x0, y2 - y0);
                    ds = sa + sb + sc + sd;
                }

                //'中右'沈下を求める点が載荷面の外側にある場合
                else if (x2 < x0 && (y1 <= y0 && y0 <= y2))
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x0 - x1, y2 - y0);
                    sc = Steinbrenner(soilLayers, q, x0 - x2, y0 - y1);
                    sd = Steinbrenner(soilLayers, q, x0 - x2, y2 - y0);
                    ds = sa + sb - sc - sd;
                }

                //'上左'沈下を求める点が載荷面の外側にある場合
                else if (x0 < x1 && y2 < y0)
                {
                    sa = Steinbrenner(soilLayers, q, x2 - x0, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x1 - x0, y0 - y1);
                    sc = Steinbrenner(soilLayers, q, x2 - x0, y0 - y2);
                    sd = Steinbrenner(soilLayers, q, x1 - x0, y0 - y2);
                    ds = sa - sb - sc + sd;
                }

                //'上中'沈下を求める点が載荷面の外側にある場合
                else if ((x1 <= x0 && x0 <= x2) && y2 < y0)
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x2 - x0, y0 - y1);
                    sc = Steinbrenner(soilLayers, q, x0 - x1, y0 - y2);
                    sd = Steinbrenner(soilLayers, q, x2 - x0, y0 - y2);
                    ds = sa + sb - sc - sd;
                }

                //'上右'沈下を求める点が載荷面の外側にある場合
                else if (x1 < x0 && y2 < y0)
                {
                    sa = Steinbrenner(soilLayers, q, x0 - x1, y0 - y1);
                    sb = Steinbrenner(soilLayers, q, x0 - x2, y0 - y1);
                    sc = Steinbrenner(soilLayers, q, x0 - x1, y0 - y2);
                    sd = Steinbrenner(soilLayers, q, x0 - x2, y0 - y2);
                    ds = sa - sb - sc + sd;
                }
                else
                {
                    ds = 0;
                }

                if (double.IsNaN(ds))
                { int a = 0; }

                s += ds;
            }
            return s;
        }
    }
}
