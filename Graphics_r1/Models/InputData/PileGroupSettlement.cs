using PileDesign.Constants;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Media3D;


namespace PileDesign.Models.InputData
{
    public class PileGroupSettlement : BaseModel
    {
        // 解析サービスで使用される「現在の」荷重面標高。一回解析 / 反復解析 を実行する直前に
        // 該当ルートの値 (LoadingPlaneAltitudeNonBeam / LoadingPlaneAltitudeBeamAware) からコピーされる。
        // UI 表示・TreeView などのレガシー参照とも互換を保つため残す。
        private double _loadingPlaneAltitude;
        public double LoadingPlaneAltitude
        {
            get => _loadingPlaneAltitude;
            set => SetProperty(ref _loadingPlaneAltitude, value);
        }

        // 一回解析 (基礎梁無し Steinbrenner) 用の荷重面標高。一回解析タブで編集。
        private double _loadingPlaneAltitudeNonBeam = double.NaN;
        public double LoadingPlaneAltitudeNonBeam
        {
            get => _loadingPlaneAltitudeNonBeam;
            set => SetProperty(ref _loadingPlaneAltitudeNonBeam, value);
        }

        // 反復解析 (基礎梁考慮) 用の荷重面標高。反復解析タブで編集。
        private double _loadingPlaneAltitudeBeamAware = double.NaN;
        public double LoadingPlaneAltitudeBeamAware
        {
            get => _loadingPlaneAltitudeBeamAware;
            set => SetProperty(ref _loadingPlaneAltitudeBeamAware, value);
        }

        // 土層上端 (ボーリング孔口レベル相当)。SettlementSoilLayers の第1層上端として使う
        private double _soilLayersTopAltitude;
        public double SoilLayersTopAltitude
        {
            get => _soilLayersTopAltitude;
            set => SetProperty(ref _soilLayersTopAltitude, value);
        }

        public List<string> LoadingTypeOptions { get; set; } = ["任意矩形", "個別矩形", "個別矩形（基礎梁考慮）", "個別十字", "個別十字（基礎梁反力）", "なし"];

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

        // 一般モード (基礎梁無し) のユーザー入力 RectLoads スナップショット。
        // 反復ダイアログを開く直前に pgs.RectLoads を保存し、群杭沈下 ▼ を 反復 → 一般 に
        // 切替えた際 (該当 CaseRecord が無い場合) に復元する。
        // 反復が pgs.RectLoads を収束反力で書き換えても、一般モードに戻るとユーザー入力に戻る。
        [System.Text.Json.Serialization.JsonIgnore]
        public ObservableCollection<RectLoad> NonBeamRectLoadsSnapshot { get; set; }

        private ObservableCollection<SettlementSoilLayer> _settlementSoilLayers;
        public ObservableCollection<SettlementSoilLayer> SettlementSoilLayers
        {
            get => _settlementSoilLayers;
            set => SetProperty(ref _settlementSoilLayers, value);
        }


        /// <summary>
        /// 表示中のケースの沈下グリッド。<b>保存ファイルの互換のために残している複製</b>で、
        /// 正は <see cref="ActiveRecord"/> の側。<b>表示系はここを読まないこと</b>
        /// (<see cref="ActiveSettlementGridData"/> を使う)。
        ///
        /// この複製は消せない。<c>CaseRecords[].SettlementGridData</c> は同じ要素インスタンスを
        /// 指しており、<c>ReferenceHandler.Preserve</c> では要素の <c>$id</c> がこちら側に付き、
        /// レコード側は <c>$ref</c> になる。このプロパティを外すと
        /// <b>既存の保存ファイルが「Reference '6' was not found」で一切開けなくなる</b>。
        /// 外すには先に要素の共有をやめる必要がある (SettlementMirrorTests に実証を残した)。
        /// </summary>
        private ObservableCollection<SettlementGridDataItem> _settlementGridData;
        public ObservableCollection<SettlementGridDataItem> SettlementGridData
        {
            get => _settlementGridData;
            set => SetProperty(ref _settlementGridData, value);
        }

        /// <summary>表示中のケースの結果。未解析・該当なしは null。</summary>
        [JsonIgnore]
        public GroupSettlementCaseRecord? ActiveRecord =>
            CaseRecords != null && ActiveCaseIndex >= 0 && ActiveCaseIndex < CaseRecords.Count
                ? CaseRecords[ActiveCaseIndex]
                : null;

        /// <summary>
        /// 表示中のケースの沈下グリッド。<b>表示系はこちらを読む。</b>
        ///
        /// 複製 (<see cref="SettlementGridData"/>) を読むと、複製の同期を忘れた経路で
        /// 「アクティブケースと画面がずれる」種類の食い違いが出る。
        /// </summary>
        [JsonIgnore]
        public ObservableCollection<SettlementGridDataItem> ActiveSettlementGridData =>
            ActiveRecord?.SettlementGridData ?? _emptyGrid;

        private readonly ObservableCollection<SettlementGridDataItem> _emptyGrid = [];

        // 群杭沈下解析結果 (ケース別)。基礎梁考慮反復・通常 Steinbrenner どちらでも 1+ レコード保存。
        // 既存単一結果との互換性: 解析実行時は最終的に SettlementGridData / RectLoads / 各杭 GroupPileSettlement
        // を ActiveCaseIndex のレコードからコピーして反映する。
        private ObservableCollection<GroupSettlementCaseRecord> _caseRecords = [];
        public ObservableCollection<GroupSettlementCaseRecord> CaseRecords
        {
            get => _caseRecords;
            set => SetProperty(ref _caseRecords, value ?? []);
        }

        // 現在表示中のケース index (CaseRecords に対応)。-1 = 未選択 or 単一結果
        private int _activeCaseIndex = -1;
        public int ActiveCaseIndex
        {
            get => _activeCaseIndex;
            set => SetProperty(ref _activeCaseIndex, value);
        }

        // 表示中の LoadingType (CaseRecords を LoadingType で絞り込んで表示するため)。
        // 入力設定 LoadingType (次回解析で使う) とは独立。空文字 = 未指定 (旧データ互換)。
        private string _activeLoadingType = "";
        public string ActiveLoadingType
        {
            get => _activeLoadingType;
            set => SetProperty(ref _activeLoadingType, value ?? "");
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
            LoadingPlaneAltitude = -5.0; /// 5m
            LoadingPlaneAltitudeNonBeam = -5.0;
            LoadingPlaneAltitudeBeamAware = -5.0;
            SoilLayersTopAltitude = 0.0;
            LoadingType = "任意矩形";
            RectLoads = [];
            SettlementSoilLayers = [];
            SettlementGridData = [];
        }

        /// <summary>
        /// 解析用に荷重面以下の有効な土層を返す。
        /// 荷重面が土層内にある場合、荷重面が属する層の上端を荷重面まで切り詰めた
        /// 新規 SettlementSoilLayer として最上層を作り、その下の層は元の参照のまま返す。
        /// 荷重面が土層上端と一致する場合は元のコレクションを返す。
        /// </summary>
        public static ObservableCollection<SettlementSoilLayer> GetEffectiveLayersForAnalysis(
            double soilLayersTopAltitude,
            double loadingPlaneAltitude,
            ObservableCollection<SettlementSoilLayer> layers)
        {
            if (layers == null || layers.Count == 0) return layers;

            // 荷重面が土層上端と等しい (許容誤差) → そのまま
            if (Math.Abs(loadingPlaneAltitude - soilLayersTopAltitude) < NumericalConstants.NEAR_ZERO_EPSILON)
                return layers;

            // 荷重面が含まれる最上層を見つけて、それより下の層だけを残す
            // 最初の層の上端は soilLayersTopAltitude、以降は前の層の BottomAltitude
            double prevTop = soilLayersTopAltitude;
            int startIndex = -1;
            for (int i = 0; i < layers.Count; i++)
            {
                double bottom = layers[i].BottomAltitude;
                // 荷重面がこの層内 (top >= load > bottom) にあるか
                if (loadingPlaneAltitude <= prevTop && loadingPlaneAltitude > bottom)
                {
                    startIndex = i;
                    break;
                }
                prevTop = bottom;
            }
            if (startIndex < 0) return layers; // 想定外: 入力外

            var trimmed = new ObservableCollection<SettlementSoilLayer>();
            // 切り詰めた最上層 (新規インスタンスで Thickness を上書き)
            var first = layers[startIndex];
            trimmed.Add(new SettlementSoilLayer
            {
                BottomAltitude = first.BottomAltitude,
                Thickness = loadingPlaneAltitude - first.BottomAltitude,
                PoissonsRatio = first.PoissonsRatio,
                Ek = first.Ek
            });
            // 以降は元の参照を流用
            for (int i = startIndex + 1; i < layers.Count; i++)
                trimmed.Add(layers[i]);

            return trimmed;
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

        /// <summary>
        /// 個別矩形 (LoadingType=個別矩形) で杭と紐づける場合の杭番号 (PileLayoutDataItem.PileNo)。
        /// 0 以下: 紐付けなし (任意矩形 / 個別十字 など)。
        /// </summary>
        private int _linkedPileNo;
        public int LinkedPileNo
        {
            get => _linkedPileNo;
            set => SetProperty(ref _linkedPileNo, value);
        }

        /// <summary>X 中心 (= (X1+X2)/2)。set すると DX を保ったまま X1/X2 を平行移動する。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double CenterX
        {
            get => (X1 + X2) * 0.5;
            set
            {
                double half = DX * 0.5;
                X1 = value - half;
                X2 = value + half;
            }
        }

        /// <summary>Y 中心 (= (Y1+Y2)/2)。set すると DY を保ったまま Y1/Y2 を平行移動する。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public double CenterY
        {
            get => (Y1 + Y2) * 0.5;
            set
            {
                double half = DY * 0.5;
                Y1 = value - half;
                Y2 = value + half;
            }
        }

        /// <summary>幅 DX (= X2 - X1)。set すると中心 (CenterX) を保ったまま X1/X2 を対称に変更。</summary>
        public double DX
        {
            get => X2 - X1;
            set
            {
                if (value <= 0) return;
                double cx = CenterX;
                double half = value * 0.5;
                X1 = cx - half;
                X2 = cx + half;
            }
        }

        /// <summary>奥行 DY (= Y2 - Y1)。set すると中心 (CenterY) を保ったまま Y1/Y2 を対称に変更。</summary>
        public double DY
        {
            get => Y2 - Y1;
            set
            {
                if (value <= 0) return;
                double cy = CenterY;
                double half = value * 0.5;
                Y1 = cy - half;
                Y2 = cy + half;
            }
        }

        /// <summary>
        /// 値だけの複製を作る。
        ///
        /// 矩形荷重は<b>入力</b>なので、ケースの結果と同じインスタンスを共有していると
        /// 画面で荷重を編集したときに、保存済みの結果の中身まで書き換わってしまう。
        /// </summary>
        public RectLoad Clone() => new()
        {
            X1 = X1, X2 = X2, Y1 = Y1, Y2 = Y2,
            QA = QA, LinkedPileNo = LinkedPileNo,
        };

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

        // 備考 (土層コピー時に土層名・分類・Vs を自動記載)
        private string _note;
        public string Note
        {
            get => _note;
            set => SetProperty(ref _note, value);
        }

        // 粒度区分 (粘性土 / 砂質土 / 礫質土)。空文字は「未指定」。
        // メイン画面の沈下土層塗りつぶし色はこの値で決まる。
        private string _granularityClass = "";
        public string GranularityClass
        {
            get => _granularityClass;
            set => SetProperty(ref _granularityClass, value ?? "");
        }
    }



    /// <summary>
    /// 群杭沈下解析結果のケース別レコード。
    /// 通常 (Steinbrenner 単発) は 1 レコード、基礎梁考慮反復は VL/L1/L2 各 1 レコード。
    /// </summary>
    public class GroupSettlementCaseRecord : BaseModel
    {
        public string LoadCaseName { get; set; } = "";

        /// <summary>このレコードを生成した解析タイプ ("任意矩形" / "個別矩形" / "個別十字" / "個別十字（基礎梁反力）" / "個別矩形（基礎梁考慮）")。空文字 = 旧データ。</summary>
        public string LoadingType { get; set; } = "";

        /// <summary>true: 個別矩形（基礎梁考慮）反復解析の結果。false: 通常 Steinbrenner 単発。</summary>
        public bool IsBeamAware { get; set; }

        /// <summary>このケースの (反復後の) 矩形荷重。</summary>
        public ObservableCollection<RectLoad> RectLoads { get; set; } = [];

        /// <summary>このケースの沈下グリッドデータ (コンタ図描画用)。</summary>
        public ObservableCollection<SettlementGridDataItem> SettlementGridData { get; set; } = [];

        /// <summary>各杭の沈下量 [mm]。Key = PileLayoutDataItem.PileNo</summary>
        public Dictionary<int, double> PileSettlements_mm { get; set; } = [];

        // ── 基礎梁考慮の場合のみ ──
        public bool IsConverged { get; set; }
        public int IterationCount { get; set; }
        public double FinalResidual { get; set; }

        /// <summary>各杭の杭反力 Pi [kN]。</summary>
        public Dictionary<int, double> PileReactions_kN { get; set; } = [];

        /// <summary>各杭の杭頭ばね剛性 ki [kN/m]。</summary>
        public Dictionary<int, double> SpringStiffness { get; set; } = [];

        /// <summary>節点変位 (基礎梁考慮のみ)。</summary>
        public List<FEM.VerticalBeamNodeResult> NodeResults { get; set; } = [];

        /// <summary>梁断面力 (基礎梁考慮のみ)。</summary>
        public List<FEM.VerticalBeamBeamResult> BeamResults { get; set; } = [];

        /// <summary>反復ログ (基礎梁考慮反復のときの履歴。表示は ObservableCollection 化される)。</summary>
        public List<string> IterationLog { get; set; } = [];
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
            if (b < NumericalConstants.NEAR_ZERO_EPSILON)
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
            //'矩形載荷面の隅角部の沈下の組合せ
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
                if (Math.Abs(x1 - x2) < NumericalConstants.NEAR_ZERO_EPSILON ||
                    Math.Abs(y1 - y2) < NumericalConstants.NEAR_ZERO_EPSILON)
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

                // ds が NaN の場合はそのまま加算 (上流で検出する想定、ここでは検査しない)

                s += ds;
            }
            return s;
        }
    }
}
