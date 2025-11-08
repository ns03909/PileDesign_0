using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel; // 追加
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class InputModel : BaseModel
    {
        private MainWindowViewModel _mainWindowViewModel;

        // 許容差（Z一致判定用）
        private const double ZMatchTolerance = 1e-6;

        // 基本設定
        private FundamentalInput _foundationInput;
        public FundamentalInput FundamentalInput
        {
            get => _foundationInput;
            set => SetProperty(ref _foundationInput, value);
        }

        // 荷重ケース
        private LoadCasesInput _loadCasesInput;
        public LoadCasesInput LoadCasesInput
        {
            get => _loadCasesInput;
            set => SetProperty(ref _loadCasesInput, value);
        }

        // 地盤条件
        private ObservableCollection<GroundInput> _groundsInput;
        public ObservableCollection<GroundInput> GroundsInput
        {
            get => _groundsInput;
            set
            {
                if (SetProperty(ref _groundsInput, value))
                {
                    OnPropertyChanged(nameof(GroundsInputCountList));
                }
            }
        }

        // 地盤数リスト
        public ObservableCollection<int> GroundsInputCountList => new(Enumerable.Range(1, GroundsInput.Count));

        // 杭体
        private ObservableCollection<PileBodyInput> _pileBodies;
        public ObservableCollection<PileBodyInput> PileBodies
        {
            get => _pileBodies;
            set
            {
                if (SetProperty(ref _pileBodies, value))
                {
                    OnPropertyChanged(nameof(PileBodiesCountList));
                }
            }
        }

        // 杭体数リスト
        public ObservableCollection<int> PileBodiesCountList => new(Enumerable.Range(1, PileBodies.Count));

        // 杭配置
        private ObservableCollection<PileLayoutDataItem> _pileLayoutItems;
        public ObservableCollection<PileLayoutDataItem> PileLayoutItems
        {
            get => _pileLayoutItems;
            set
            {
                if (_pileLayoutItems == value) return;
                _pileLayoutItems = value;
                OnPropertyChanged(nameof(PileLayoutItems));
                WirePileLayoutItemsHandlers(); // 追加: ハンドラ再配線
            }
        }

        // 根入
        private EmbedmentInput _embedmentInput;
        public EmbedmentInput EmbedmentInput
        {
            get => _embedmentInput;
            set => SetProperty(ref _embedmentInput, value);
        }

        // クラス内フィールドに追加
        private bool _suppressSoilPileNotify;

        // 要素分割
        private ElementDivision _elementDivision;
        public ElementDivision ElementDivision
        {
            get => _elementDivision;
            set
            {
                if (SetProperty(ref _elementDivision, value))
                {
                    AttachElementDivisionHandlers();
                }
            }
        }

        // SoilPiles の変更を購読
        private void AttachElementDivisionHandlers()
        {
            if (ElementDivision?.SoilPiles == null) return;
            ElementDivision.SoilPiles.CollectionChanged -= SoilPiles_CollectionChanged;
            ElementDivision.SoilPiles.CollectionChanged += SoilPiles_CollectionChanged;
        }

        private void SoilPiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return;
            NotifySoilPileChangedForAll();
        }

        public void NotifySoilPileChangedForAll()
        {
            if (PileLayoutItems == null) return;
            foreach (var item in PileLayoutItems)
                item.NotifySoilPileChanged();
        }


        // ---- PileLayoutItems の追加/削除/編集 監視 ----

        private void WirePileLayoutItemsHandlers()
        {
            if (PileLayoutItems == null) return;

            PileLayoutItems.CollectionChanged -= PileLayoutItems_CollectionChanged;
            PileLayoutItems.CollectionChanged += PileLayoutItems_CollectionChanged;

            foreach (var item in PileLayoutItems)
            {
                item.PropertyChanged -= OnPileLayoutItemPropertyChanged;
                item.PropertyChanged += OnPileLayoutItemPropertyChanged;
            }
        }


        private void PileLayoutItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<PileLayoutDataItem>())
                {
                    it.SetMainWindowViewModel(_mainWindowViewModel);
                    it.PropertyChanged += OnPileLayoutItemPropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<PileLayoutDataItem>())
                {
                    it.PropertyChanged -= OnPileLayoutItemPropertyChanged;
                }
            }

            // 追加/削除でもセット全体を再計算
            RegenerateSoilPilesAndNotify();
        }

        private void OnPileLayoutItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // SoilPile 再生成が必要な変更のみ
            if (e.PropertyName is nameof(PileLayoutDataItem.GroundNo)
                || e.PropertyName is nameof(PileLayoutDataItem.PileBodyNo)
                || e.PropertyName == "Z")
            {
                RegenerateSoilPilesAndNotify();
            }
        }


        // ---- Ground の変更監視（土層境界や層数が変わったら再生成） ----

        private void AttachGroundsHandlers()
        {
            if (GroundsInput == null) return;

            GroundsInput.CollectionChanged -= GroundsInput_CollectionChanged;
            GroundsInput.CollectionChanged += GroundsInput_CollectionChanged;

            foreach (var g in GroundsInput)
                HookGround(g);
        }

        private void GroundsInput_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var g in e.NewItems.OfType<GroundInput>())
                    HookGround(g);

            if (e.OldItems != null)
                foreach (var g in e.OldItems.OfType<GroundInput>())
                    UnhookGround(g);

            // 追加: 件数リストの再通知（ComboBox ItemsSource を更新させる）
            OnPropertyChanged(nameof(GroundsInputCountList));

            RegenerateSoilPilesAndNotify();
        }

        private void HookGround(GroundInput g)
        {
            if (g == null) return;
            g.PropertyChanged -= OnGroundChanged;
            g.PropertyChanged += OnGroundChanged;

            if (g.GroundLayers != null)
            {
                g.GroundLayers.CollectionChanged -= GroundLayers_CollectionChanged;
                g.GroundLayers.CollectionChanged += GroundLayers_CollectionChanged;
                foreach (var layer in g.GroundLayers)
                {
                    layer.PropertyChanged -= OnGroundLayerChanged;
                    layer.PropertyChanged += OnGroundLayerChanged;
                }
            }
        }

        private void UnhookGround(GroundInput g)
        {
            if (g == null) return;
            g.PropertyChanged -= OnGroundChanged;
            if (g.GroundLayers != null)
            {
                g.GroundLayers.CollectionChanged -= GroundLayers_CollectionChanged;
                foreach (var layer in g.GroundLayers)
                    layer.PropertyChanged -= OnGroundLayerChanged;
            }
        }

        private void OnGroundChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            // 地盤上端/水位/液状化設定・層更新など、基本的に全て再生成で安全
            RegenerateSoilPilesAndNotify();
        }

        private void GroundLayers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var layer in e.NewItems.OfType<GroundLayerInput>())
                {
                    layer.PropertyChanged -= OnGroundLayerChanged;
                    layer.PropertyChanged += OnGroundLayerChanged;
                }
            if (e.OldItems != null)
                foreach (var layer in e.OldItems.OfType<GroundLayerInput>())
                    layer.PropertyChanged -= OnGroundLayerChanged;

            RegenerateSoilPilesAndNotify();
        }

        private void OnGroundLayerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            RegenerateSoilPilesAndNotify();
        }

        // ---- PileBody の変更監視（セグメント長などが変わったら再生成） ----

        private void AttachPileBodiesHandlers()
        {
            if (PileBodies == null) return;

            PileBodies.CollectionChanged -= PileBodies_CollectionChanged;
            PileBodies.CollectionChanged += PileBodies_CollectionChanged;

            foreach (var pb in PileBodies)
                HookPileBody(pb);
        }

        private void PileBodies_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (var pb in e.NewItems.OfType<PileBodyInput>())
                    HookPileBody(pb);

            if (e.OldItems != null)
                foreach (var pb in e.OldItems.OfType<PileBodyInput>())
                    UnhookPileBody(pb);

            // 追加: 件数リストの再通知（ComboBox ItemsSource を更新させる）
            OnPropertyChanged(nameof(PileBodiesCountList));

            RegenerateSoilPilesAndNotify();
        }

        private void HookPileBody(PileBodyInput pb)
        {
            if (pb == null) return;
            pb.PropertyChanged -= OnPileBodyChanged;
            pb.PropertyChanged += OnPileBodyChanged;

            if (pb.PileBodySegments != null)
            {
                pb.PileBodySegments.CollectionChanged -= PileBodySegments_CollectionChanged;
                pb.PileBodySegments.CollectionChanged += PileBodySegments_CollectionChanged;
                foreach (var seg in pb.PileBodySegments)
                {
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
                    seg.PropertyChanged += OnPileBodySegmentChanged;
                }
            }
        }

        private void UnhookPileBody(PileBodyInput pb)
        {
            if (pb == null) return;
            pb.PropertyChanged -= OnPileBodyChanged;
            if (pb.PileBodySegments != null)
            {
                pb.PileBodySegments.CollectionChanged -= PileBodySegments_CollectionChanged;
                foreach (var seg in pb.PileBodySegments)
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
            }
        }

        private void OnPileBodyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加
            RegenerateSoilPilesAndNotify();
        }

        private void PileBodySegments_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            if (e.NewItems != null)
                foreach (var seg in e.NewItems.OfType<PileBodySegment>())
                {
                    seg.PropertyChanged -= OnPileBodySegmentChanged;
                    seg.PropertyChanged += OnPileBodySegmentChanged;
                }
            if (e.OldItems != null)
                foreach (var seg in e.OldItems.OfType<PileBodySegment>())
                    seg.PropertyChanged -= OnPileBodySegmentChanged;

            RegenerateSoilPilesAndNotify();
        }

        private void OnPileBodySegmentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressSoilPileNotify) return; // 再入抑止を追加

            RegenerateSoilPilesAndNotify();
        }

        // ---- 再生成＋通知の共通ハブ ----

        private void RegenerateSoilPilesAndNotify()
        {
            try
            {
                _suppressSoilPileNotify = true;
                GenerateSoilPiles(); // SoilPileAltNo も再付与
            }
            finally
            {
                _suppressSoilPileNotify = false;
            }

            // DataGrid 列の SoilPile.* バインディングを即時更新
            NotifySoilPileChangedForAll();

            // 3D/ビュー更新が必要なら実行
            _mainWindowViewModel?.UpdateViewCommand?.Execute(null);
        }






        // Reset/Set/Attach の最後でハンドラ張り直し
        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            if (PileLayoutItems != null)
            {
                foreach (var item in PileLayoutItems)
                {
                    item.SetMainWindowViewModel(mainWindowViewModel);
                }
            }

            if (Elements != null)
            {
                foreach (var element in Elements)
                {
                    if (element.Nodes != null)
                    {
                        foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
                        {
                            node.SetMainWindowViewModel(mainWindowViewModel);
                        }
                    }
                }
            }

            Reset(); // ElementDivision等を初期化

            // 初期化後に各種購読を張る
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }

        // 軽量版
        public void AttachViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            if (PileLayoutItems != null)
            {
                foreach (var item in PileLayoutItems)
                {
                    item.SetMainWindowViewModel(mainWindowViewModel);
                }
            }

            if (Elements != null)
            {
                foreach (var element in Elements)
                {
                    if (element.Nodes != null)
                    {
                        foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
                        {
                            node.SetMainWindowViewModel(mainWindowViewModel);
                        }
                    }
                }
            }
            // 重要: DeepCopy 後に null になり得るコレクションを補正
            GridXItems ??= [];
            GridYItems ??= [];

            // 各種購読
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }

        // Reset 内の ElementDivision 生成後に購読をセット
        public void Reset()
        {
            FundamentalInput = new FundamentalInput();
            LoadCasesInput = new LoadCasesInput();
            LoadCasesInput.SetMainWindowViewModel(_mainWindowViewModel);
            GroundsInput = [new GroundInput()];
            PileBodies = [new PileBodyInput()];
            PileLayoutItems = [];
            EmbedmentInput = new EmbedmentInput();
            ElementDivision = new ElementDivision()
            {
                SoilPiles = [],
                SoilEmbedment = new SoilEmbedment(1, 0.0, [])
            };
            PileGroupSettlement = new PileGroupSettlement();
            Elements = [];
            GridXItems = [];
            GridYItems = [];

            // 初期コレクションにも購読を張る
            AttachElementDivisionHandlers();
            WirePileLayoutItemsHandlers();
            AttachGroundsHandlers();
            AttachPileBodiesHandlers();
        }


        private PileGroupSettlement _pileGroupSettlement;
        public PileGroupSettlement PileGroupSettlement
        {
            get => _pileGroupSettlement;
            set => SetProperty(ref _pileGroupSettlement, value);
        }

        // 要素
        private ObservableCollection<Element> _elements;
        public ObservableCollection<Element> Elements
        {
            get => _elements;
            set => SetProperty(ref _elements, value);
        }

        // グリッド
        private ObservableCollection<GridDataItem> _gridXItems;
        //[JsonIgnore]
        public ObservableCollection<GridDataItem> GridXItems
        {
            get => _gridXItems;
            set => SetProperty(ref _gridXItems, value);
        }

        private ObservableCollection<GridDataItem> _gridYItems;
        //[JsonIgnore]
        public ObservableCollection<GridDataItem> GridYItems
        {
            get => _gridYItems;
            set => SetProperty(ref _gridYItems, value);
        }

        // コンストラクタ
        public InputModel()
        {
        }

        //public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        //{
        //    _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        //    if (PileLayoutItems != null)
        //    {
        //        foreach (var item in PileLayoutItems)
        //        {
        //            item.SetMainWindowViewModel(mainWindowViewModel);
        //        }
        //    }

        //    // Elements内のNodesにもViewModelを再セット
        //    if (Elements != null)
        //    {
        //        foreach (var element in Elements)
        //        {
        //            if (element.Nodes != null)
        //            {
        //                foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
        //                {
        //                    node.SetMainWindowViewModel(mainWindowViewModel);
        //                }
        //            }
        //        }
        //    }

        //    Reset();
        //}

        //// 追加: 復元・読込時などに使う「Reset しない」軽量版
        //public void AttachViewModel(MainWindowViewModel mainWindowViewModel)
        //{
        //    _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        //    if (PileLayoutItems != null)
        //    {
        //        foreach (var item in PileLayoutItems)
        //        {
        //            item.SetMainWindowViewModel(mainWindowViewModel);
        //        }
        //    }

        //    if (Elements != null)
        //    {
        //        foreach (var element in Elements)
        //        {
        //            if (element.Nodes != null)
        //            {
        //                foreach (var node in element.Nodes.OfType<PileLayoutDataItem>())
        //                {
        //                    node.SetMainWindowViewModel(mainWindowViewModel);
        //                }
        //            }
        //        }
        //    }
        //    // 重要: DeepCopy 後に null になり得るコレクションを補正
        //    GridXItems ??= [];
        //    GridYItems ??= [];
        //}

        // 
        //public void Reset()
        //{
        //    FundamentalInput = new FundamentalInput();
        //    LoadCasesInput = new LoadCasesInput();
        //    LoadCasesInput.SetMainWindowViewModel(_mainWindowViewModel);
        //    GroundsInput = [new GroundInput()];
        //    PileBodies = [new PileBodyInput()];
        //    PileLayoutItems = [];
        //    EmbedmentInput = new EmbedmentInput();
        //    ElementDivision = new ElementDivision()
        //    {
        //        SoilPiles = [],
        //        SoilEmbedment = new SoilEmbedment(1, 0.0, [])
        //    };
        //    PileGroupSettlement = new PileGroupSettlement();
        //    Elements = [];
        //    GridXItems = [];
        //    GridYItems = [];
        //}

        public double GetSumVL()
        {
            return PileLayoutItems.Sum(item => item.AxialForceVL0);
        }

        public double GetSumVLadd()
        {
            return PileLayoutItems.Sum(item => item.AxialForceVLAdditional);
        }

        public double GetSumVLplusVLadd()
        {
            return GetSumVL() + GetSumVLadd();
        }



        // VLadd重心を返すメソッド
        public Point3D GetVLaddGravityCenter()
        {
            double sumW = 0;
            double sumMX = 0, sumMY = 0, sumMZ = 0;
            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVLAdditional;
                sumMX += item.X * item.AxialForceVLAdditional;
                sumMY += item.Y * item.AxialForceVLAdditional;
                sumMZ += item.Z * item.AxialForceVLAdditional;
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        // VL重心を返すメソッド
        public Point3D GetVLGravityCenter()
        {
            double sumW = 0;

            double sumMX = 0;
            double sumMY = 0;
            double sumMZ = 0;

            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVL0;

                sumMX += item.X * item.AxialForceVL0;
                sumMY += item.Y * item.AxialForceVL0;
                sumMZ += item.Z * item.AxialForceVL0;
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        public Point3D GetVLplusVLaddGravityCenter()
        {
            double sumW = 0;

            double sumMX = 0;
            double sumMY = 0;
            double sumMZ = 0;

            foreach (var item in PileLayoutItems)
            {
                sumW += item.AxialForceVL0 + item.AxialForceVLAdditional;

                sumMX += item.X * (item.AxialForceVL0 + item.AxialForceVLAdditional);
                sumMY += item.Y * (item.AxialForceVL0 + item.AxialForceVLAdditional);
                sumMZ += item.Z * (item.AxialForceVL0 + item.AxialForceVLAdditional);
            }
            if (PileLayoutItems.Count == 0 || sumW == 0)
            {
                // 要素が無い、または合計荷重が0の場合は原点を返す
                return new Point3D(0, 0, 0);
            }
            return new Point3D(sumMX / sumW, sumMY / sumW, sumMZ / sumW);
        }

        // 図心を返すメソッド
        public Point3D GetCentroid()
        {
            double sumX = 0;
            double sumY = 0;
            double sumZ = 0;
            foreach (var item in PileLayoutItems)
            {
                sumX += item.X;
                sumY += item.Y;
                sumZ += item.Z;
            }
            return new Point3D(sumX / PileLayoutItems.Count, sumY / PileLayoutItems.Count, sumZ / PileLayoutItems.Count);
        }

        // 軸力の合計を返すメソッド
        public double GetSum(int level, int index)
        {
            double sumLoads = 0;

            if (level == 1)
            {
                sumLoads += PileLayoutItems.Sum(item => item.AxialForceLevel1s[index]);
            }
            else if (level == 2)
            {
                sumLoads += PileLayoutItems.Sum(item => item.AxialForceLevel2s[index]);
            }

            return sumLoads;
        }

        // 転倒モーメント(MNm)を返すメソッド
        public (double, double) GetOverturningMoment(int level, int index) // degree
        {
            double otmX = 0;
            double otmY = 0;

            Point3D gravityCenter = GetVLplusVLaddGravityCenter();

            if (level == 1)
            {
                foreach (var item in PileLayoutItems)
                {
                    double verticalForce = item.AxialForceLevel1s[index];
                    otmX += verticalForce * (item.X - gravityCenter.X) * 0.001;
                    otmY += verticalForce * (item.Y - gravityCenter.Y) * 0.001;
                }
            }
            else if (level == 2)
            {
                foreach (var item in PileLayoutItems)
                {
                    double verticalForce = item.AxialForceLevel2s[index];
                    otmX += verticalForce * (item.X - gravityCenter.X) * 0.001;
                    otmY += verticalForce * (item.Y - gravityCenter.Y) * 0.001;
                }
            }
            return (otmX, otmY);
        }

        // 単位転倒モーメントに対する反力を返すメソッド
        public List<double> GetReactionForUnitMoment(double angle) // degree
        {
            double radian = angle * Math.PI / 180;
            double c = Math.Cos(radian);
            double s = Math.Sin(radian);
            Point3D centroid = GetCentroid();

            var raw = new List<double>();
            double otm = 0;
            foreach (var item in PileLayoutItems)
            {
                double arm = c * (item.X - centroid.X) + s * (item.Y - centroid.Y);
                raw.Add(arm);
                otm += arm * arm;
            }

            if (otm <= 1e-12) // ほぼゼロ
                return [.. raw.Select(_ => 0.0)];

            return [.. raw.Select(r => r / otm)];

            //List<double> reactions = [];
            //double otm = 0;
            //foreach (var item in PileLayoutItems)
            //{
            //    double momentArm = c * (item.X - centroid.X) + s * (item.Y - centroid.Y);
            //    reactions.Add(momentArm);
            //    otm += momentArm * momentArm;
            //}

            //reactions = [.. reactions.Select(r => r / otm)];
            //return reactions;
        }

        // JsonSerializerOptionsをキャッシュ
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            ReferenceHandler = ReferenceHandler.Preserve, // 循環参照対応を追加
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals // 
        };

        // データの保存
        public void SaveToFile(string filePath)
        {
            string jsonString = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(filePath, jsonString);
        }


        // データの読み込み
        public static InputModel LoadFromFile(string filePath, MainWindowViewModel mainWindowViewModel)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("指定されたファイルが存在しません。", filePath);

            string json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<InputModel>(json, _jsonOptions)
                ?? throw new InvalidOperationException("ファイルの内容をデシリアライズできませんでした。");


            // MainWindowViewModelをセット
            loaded.SetMainWindowViewModel(mainWindowViewModel);

            return loaded;
        }


        // 指定の名称のPileBodyを返すメソッド
        public PileBodyInput? GetPileBodyByPileBodyRef(string pileBodyRef)
        {
            foreach (var pileBody in this.PileBodies)
            {
                if (pileBody.PileBodyRef == pileBodyRef)
                {
                    return pileBody;
                }
            }

            // 見つからなかった場合はnullを返す
            return null;
        }

        //　SoilPiles生成メソッド
        // GenerateSoilPiles 完了後に一括通知（イベント多発を抑制）
        public void GenerateSoilPiles()
        {
            _suppressSoilPileNotify = true;
            try
            {
                ElementDivision.SoilPiles.Clear();

                ObservableCollection<(int, int, double)> usedGroundNosPileBodyNosPileTopAltitudes = [];

                foreach (PileLayoutDataItem pileLayoutDataItem in PileLayoutItems)
                {
                    int pileBodyNo = pileLayoutDataItem.PileBodyNo;
                    int groundNo = pileLayoutDataItem.GroundNo;
                    double pileTopAltitude = pileLayoutDataItem.Point3D.Z;

                    if (pileBodyNo - 1 < 0 || pileBodyNo - 1 >= PileBodies.Count) continue;
                    if (groundNo - 1 < 0 || groundNo - 1 >= GroundsInput.Count) continue;

                    var pileBodySegments = PileBodies[pileBodyNo - 1].PileBodySegments;
                    PileBodies[pileBodyNo - 1].PileBodySegmentsUpdate();
                    pileBodySegments = PileBodies[pileBodyNo - 1].PileBodySegments;
                    var groundLayerDataItems = GroundsInput[groundNo - 1].GroundLayers;

                    // 許容差付きで重複チェック
                    bool exists = usedGroundNosPileBodyNosPileTopAltitudes.Any(t =>
                        t.Item1 == groundNo &&
                        t.Item2 == pileBodyNo &&
                        Math.Abs(t.Item3 - pileTopAltitude) <= ZMatchTolerance);

                    if (!exists)
                    {
                        usedGroundNosPileBodyNosPileTopAltitudes.Add((groundNo, pileBodyNo, pileTopAltitude));

                        ObservableCollection<double> zs = [pileTopAltitude];
                        foreach (PileBodySegment pileBodySegment in pileBodySegments)
                            zs.Add(pileTopAltitude - pileBodySegment.SegmentDepth);

                        double pileBottomAltitude = zs[^1];

                        foreach (GroundLayerInput groundLayerDataItem in groundLayerDataItems)
                        {
                            if (pileTopAltitude > groundLayerDataItem.BottomAltitude && groundLayerDataItem.BottomAltitude > pileBottomAltitude)
                            {
                                for (int i = zs.Count - 1; i >= 0; i--)
                                {
                                    double z = zs[i];
                                    if (z < groundLayerDataItem.BottomAltitude)
                                    {
                                        zs.Insert(i, groundLayerDataItem.BottomAltitude);
                                        break;
                                    }
                                }
                            }
                        }

                        List<double> sortedZs = [.. zs.Distinct().OrderByDescending(z => z)];
                        ObservableCollection<PileZDataItem> pilezDataItems = [];
                        foreach (double sortedZ in sortedZs)
                        {
                            PileZDataItem pileZDataItem = new()
                            {
                                Z = sortedZ,
                                GroundInput = GroundsInput[pileLayoutDataItem.GroundNo - 1],
                            };
                            pileZDataItem.SetSoilDisplacement();
                            pilezDataItems.Add(pileZDataItem);
                        }

                        var sp = new SoilPile()
                        {
                            No = ElementDivision.SoilPiles.Count + 1,
                            GroundNo = groundNo,
                            GroundInput = GroundsInput[groundNo - 1],
                            PileBodyNo = pileBodyNo,
                            PileBodyInput = PileBodies[pileBodyNo - 1],
                            Z = pileTopAltitude,
                            ZDataItems = pilezDataItems
                        };

                        // 追加: R_* 等の特性を再計算
                        sp.UpdateProperties();

                        ElementDivision.SoilPiles.Add(sp);
                    }
                }

                ElementDivision.UpdateSoilPileNumberOption();

                // 許容差付きで SoilPileAltNo を付与
                foreach (PileLayoutDataItem pileLayoutDataItem in PileLayoutItems)
                {
                    for (int i = 0; i < ElementDivision.SoilPiles.Count; i++)
                    {
                        var sp = ElementDivision.SoilPiles[i];
                        if (pileLayoutDataItem.GroundNo == sp.GroundNo
                            && pileLayoutDataItem.PileBodyNo == sp.PileBodyNo
                            && Math.Abs(pileLayoutDataItem.Point3D.Z - sp.Z) <= ZMatchTolerance)
                        {
                            pileLayoutDataItem.SoilPileAltNo = i + 1;
                            break; // 見つかったら抜ける
                        }
                    }
                }
            }
            finally
            {
                _suppressSoilPileNotify = false;
            }

            // 一括通知は RegenerateSoilPilesAndNotify 側で実施
            // NotifySoilPileChangedForAll();
        }

        // 地盤根入れ生成メソッド
        public void GenerateSoilEmbedment()
        {
            if (ElementDivision.SoilEmbedment == null) { return; }

            int groundNo = EmbedmentInput.GroundNo;
            double embedmentTopAltitude;
            if (EmbedmentInput.EmbedmentLayers.Count != 0)
            {
                embedmentTopAltitude = EmbedmentInput.EmbedmentLayers[0].TopAltitude;
            }
            else
            {
                embedmentTopAltitude = EmbedmentInput.BottomAltitude; // Bottom Altitudeとする。
            }

            ObservableCollection<double> zs = [embedmentTopAltitude];

            foreach (EmbedmentDataItem embedmentDataItem in EmbedmentInput.EmbedmentLayers)
            {
                zs.Add(embedmentDataItem.BottomAltitude);
            }

            double embedmentBottomAltitude = zs[^1];

            if (groundNo - 1 < 0 || groundNo - 1 >= GroundsInput.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(groundNo), "Ground number is out of range.");
            }

            ObservableCollection<GroundLayerInput> groundLayerDataItems = GroundsInput[groundNo - 1].GroundLayers;

            foreach (GroundLayerInput groundLayerDataItem in groundLayerDataItems)
            {
                if (embedmentTopAltitude > groundLayerDataItem.BottomAltitude && groundLayerDataItem.BottomAltitude > embedmentBottomAltitude)
                {
                    // zsが降順に並んでいるため、逆順で反復処理を行います。
                    for (int i = zs.Count - 1; i >= 0; i--)
                    {
                        double z = zs[i];
                        // zがgroundLayerDataItem.BottomAltitudeよりも小さくなった場合
                        if (z < groundLayerDataItem.BottomAltitude)
                        {
                            // zの手前にgroundLayerDataItem.BottomAltitudeを挿入
                            zs.Insert(i, groundLayerDataItem.BottomAltitude);
                            break;
                        }
                    }
                }
            }

            // zsを降順に並び替え
            List<double> sortedZs = [.. zs.Distinct().OrderByDescending(z => z)];
            ObservableCollection<EmbedmentZDataItem> zDataItems = [];
            foreach (double sortedZ in sortedZs)
            {
                EmbedmentZDataItem embedmentZDataItem = new()
                {
                    Z = sortedZ,
                    GroundInput = GroundsInput[EmbedmentInput.GroundNo - 1],
                };
                embedmentZDataItem.SetSoilDisplacement();
                zDataItems.Add(embedmentZDataItem);
            }
            ElementDivision.SoilEmbedment = new(EmbedmentInput.GroundNo, embedmentTopAltitude, zDataItems);
        }

        //public InputModel DeepCopy()
        //{
        //    var options = new JsonSerializerOptions
        //    {
        //        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
        //        WriteIndented = false
        //    };
        //    string json = JsonSerializer.Serialize(this, options);
        //    return JsonSerializer.Deserialize<InputModel>(json, options);
        //}
        // 既存: private static readonly JsonSerializerOptions _jsonOptions = new() { ... AllowNamedFloatingPointLiterals };
        // DeepCopy 修正 + 特殊値クリーン処理を追加
        public InputModel DeepCopy()
        {
            // 特殊値を一時コピー上で正規化したくない場合は this を直接渡さず CloneWorking を作る
            // ここでは簡易に this をクリーンしてからシリアライズ（Undo 前などで呼ぶなら呼び出し側で Clone を取る運用でも可）
            CleanFloatingPointSpecials(this);

            string json = JsonSerializer.Serialize(this, _jsonOptions); // _jsonOptions を使う
            var clone = JsonSerializer.Deserialize<InputModel>(json, _jsonOptions)
                        ?? throw new InvalidOperationException("DeepCopy 失敗");
            // ViewModel 再接続
            clone.AttachViewModel(_mainWindowViewModel);
            return clone;
        }

        // Infinity / NaN を 0 (または任意の代替値) に置換
        private static void CleanFloatingPointSpecials(object obj, int depth = 0, int maxDepth = 4)
        {
            if (obj == null || depth > maxDepth) return;

            var type = obj.GetType();

            // IEnumerable (string 除外) の場合は列挙
            if (obj is System.Collections.IEnumerable en && obj is not string)
            {
                foreach (var item in en)
                {
                    if (item != null && !item.GetType().IsPrimitive)
                        CleanFloatingPointSpecials(item, depth + 1, maxDepth);
                }
                return;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;

                var pt = prop.PropertyType;

                try
                {
                    if (pt == typeof(double) && prop.CanWrite)
                    {
                        double v = (double)(prop.GetValue(obj) ?? 0d);
                        if (double.IsNaN(v) || double.IsInfinity(v))
                        {
                            prop.SetValue(obj, 0d); // 必要なら他の既定値へ
                        }
                    }
                    else if (pt == typeof(float) && prop.CanWrite)
                    {
                        float v = (float)(prop.GetValue(obj) ?? 0f);
                        if (float.IsNaN(v) || float.IsInfinity(v))
                        {
                            prop.SetValue(obj, 0f);
                        }
                    }
                    // 再帰 (クラス/レコード/コレクション)
                    else if (!pt.IsPrimitive && pt != typeof(string))
                    {
                        var child = prop.GetValue(obj);
                        if (child != null)
                            CleanFloatingPointSpecials(child, depth + 1, maxDepth);
                    }
                }
                catch
                {
                    // 失敗は無視（読み込み専用/インデクサ等）
                }
            }
        }

        // デバッグ用: どこに特殊値があるか列挙 (必要時のみ呼ぶ)
        public IEnumerable<string> FindSpecialNumberLocations()
        {
            var list = new List<string>();
            void Scan(object target, string path, int depth)
            {
                if (target == null || depth > 5) return;
                var t = target.GetType();

                if (target is System.Collections.IEnumerable en && target is not string)
                {
                    int i = 0;
                    foreach (var item in en)
                    {
                        Scan(item, $"{path}[{i}]", depth + 1);
                        i++;
                    }
                    return;
                }

                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead) continue;
                    object val;
                    try { val = p.GetValue(target); } catch { continue; }

                    if (val is double d)
                    {
                        if (double.IsNaN(d) || double.IsInfinity(d))
                            list.Add($"{path}.{p.Name} = {d}");
                    }
                    else if (val is float f)
                    {
                        if (float.IsNaN(f) || float.IsInfinity(f))
                            list.Add($"{path}.{p.Name} = {f}");
                    }
                    else if (val != null && !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string))
                    {
                        Scan(val, $"{path}.{p.Name}", depth + 1);
                    }
                }
            }
            Scan(this, "InputModel", 0);
            return list;
        }
    }
}

