using Graphics_r1.Constants;
using PileDesign.FEM;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    public class PileLayoutDataItem : InputNode
    {
        private MainWindowViewModel _mainWindowViewModel;

        [JsonIgnore]
        public InputModel InputModel => _mainWindowViewModel?.CurrentInputModel;

        private const double ZMatchTolerance = 1e-6;

        [JsonIgnore]
        public SoilPile? SoilPile
        {
            get
            {
                // Phase 1: キャッシュ検索に置き換え
                var inputModel = InputModel;
                if (inputModel == null) return CreateTemporarySoilPile();

                var found = inputModel.LookupSoilPile(GroundNo, PileBodyNo, this.Point3D.Z);
                return found ?? CreateTemporarySoilPile();
            }
        }

        private SoilPile? CreateTemporarySoilPile()
        {
            var input = InputModel;
            if (input == null) return null;

            int pileBodyIndex = PileBodyNo - 1;
            int groundIndex = GroundNo - 1;
            if (pileBodyIndex < 0 || input.PileBodies == null || pileBodyIndex >= input.PileBodies.Count) return null;
            if (groundIndex < 0 || input.GroundsInput == null || groundIndex >= input.GroundsInput.Count) return null;

            var pileBody = input.PileBodies[pileBodyIndex];
            var ground = input.GroundsInput[groundIndex];

            pileBody.PileBodySegmentsUpdate();
            var segments = pileBody.PileBodySegments;
            var layers = ground.GroundLayers;

            var zs = new ObservableCollection<double> { this.Point3D.Z };
            foreach (var seg in segments)
                zs.Add(this.Point3D.Z - seg.SegmentDepth);

            double bottomZ = zs[^1];

            foreach (var gl in layers)
            {
                if (this.Point3D.Z > gl.BottomAltitude && gl.BottomAltitude > bottomZ)
                {
                    for (int i = zs.Count - 1; i >= 0; i--)
                    {
                        if (zs[i] < gl.BottomAltitude)
                        {
                            zs.Insert(i, gl.BottomAltitude);
                            break;
                        }
                    }
                }
            }

            // トレランス付き重複除去（杭区間境界と地層境界が微小差で重複するケースを防止）
            var tempZs = zs.OrderByDescending(v => v).ToList();
            var sortedZs = new List<double>(tempZs.Count);
            foreach (var v in tempZs)
            {
                if (sortedZs.Count == 0 || Math.Abs(sortedZs[^1] - v) > NumericalConstants.COORDINATE_TOLERANCE)
                    sortedZs.Add(v);
            }
            var zItems = new ObservableCollection<PileZDataItem>();
            foreach (var z in sortedZs)
            {
                var zi = new PileZDataItem
                {
                    Z = z,
                    GroundInput = ground
                };
                zi.SetSoilDisplacement();
                zItems.Add(zi);
            }

            var temp = new SoilPile();
            temp.Initialize(
                no: 0,
                groundNo: GroundNo,
                groundInput: ground,
                pileBodyNo: PileBodyNo,
                pileBodyInput: pileBody,
                z: this.Point3D.Z,
                zDataItems: zItems);

            temp.UpdateProperties();
            return temp;
        }

        // 杭番号
        private int _pileNo;
        public int PileNo
        {
            get => _pileNo;
            set => SetProperty(ref _pileNo, value);
        }

        // 杭体番号
        private int _pileBodyNo;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set => SetProperty(ref _pileBodyNo, value);
        }

        // 地盤番号
        private int _groundNo;
        public int GroundNo
        {
            get => _groundNo;
            set
            {
                if (SetProperty(ref _groundNo, value))
                {
                    OnPropertyChanged(nameof(SoilPile));
                }
            }
        }

        // 地盤杭高さ番号
        private int _soilPileAltNo;
        public int SoilPileAltNo
        {
            get => _soilPileAltNo;
            set
            {
                if (SetProperty(ref _soilPileAltNo, value))
                {
                    OnPropertyChanged(nameof(SoilPile));
                }
            }
        }

        // 接続先の基礎梁節点番号（剛床連結の場合）
        private int? _connectedFoundationNodeNo;
        public int? ConnectedFoundationNodeNo
        {
            get => _connectedFoundationNodeNo;
            set => SetProperty(ref _connectedFoundationNodeNo, value);
        }

        // 基礎梁接続節点の相対高さ（杭頭からの鉛直オフセット）
        private double _foundationBeamDeltaZc = 1.0;
        public double FoundationBeamDeltaZc
        {
            get => _foundationBeamDeltaZc;
            set => SetProperty(ref _foundationBeamDeltaZc, value);
        }

        //群杭係数
        private double _groupPileFactor;
        public double GroupPileFactor
        {
            get => _groupPileFactor;
            set => SetProperty(ref _groupPileFactor, value);
        }

        // 杭間隔比
        private double _pileSpacingFactor;
        public double PileSpacingFactor
        {
            get => _pileSpacingFactor;
            set => SetProperty(ref _pileSpacingFactor, value);
        }

        // 常時軸力
        private double _axialForceVL0;
        public double AxialForceVL0
        {
            get => _axialForceVL0;
            set
            {
                if (SetProperty(ref _axialForceVL0, value))
                {
                    OnPropertyChanged(nameof(AxialForceVL)); // 追加: VL再計算通知
                }
            }
        }

        // 追加軸力
        private double _axialForceVLAdditional;
        public double AxialForceVLAdditional
        {
            get => _axialForceVLAdditional;
            set
            {
                if (SetProperty(ref _axialForceVLAdditional, value))
                {
                    OnPropertyChanged(nameof(AxialForceVL)); // 追加: VL再計算通知
                }
            }
        }

        public double AxialForceVL => AxialForceVL0 + AxialForceVLAdditional;

        // レベル1地震時軸力
        private ObservableCollection<double> _axialForceLevel1s;
        public ObservableCollection<double> AxialForceLevel1s
        {
            get => _axialForceLevel1s;
            set => SetProperty(ref _axialForceLevel1s, value);
        }

        // レベル2地震時軸力
        private ObservableCollection<double> _axialForceLevel2s;
        public ObservableCollection<double> AxialForceLevel2s
        {
            get => _axialForceLevel2s;
            set => SetProperty(ref _axialForceLevel2s, value);
        }

        // 常時単杭沈下
        private double _singlePileSettlementVL;
        public double SinglePileSettlementVL
        {
            get => _singlePileSettlementVL;
            set => SetProperty(ref _singlePileSettlementVL, value);
        }

        // レベル1地震時単杭沈下
        private ObservableCollection<double> _singlePileSettlementLevel1s;
        public ObservableCollection<double> SinglePileSettlementLevel1s
        {
            get => _singlePileSettlementLevel1s;
            set => SetProperty(ref _singlePileSettlementLevel1s, value);
        }

        // レベル1地震時単杭沈下
        private ObservableCollection<double> _singlePileSettlementLevel2s;
        public ObservableCollection<double> SinglePileSettlementLevel2s
        {
            get => _singlePileSettlementLevel2s;
            set => SetProperty(ref _singlePileSettlementLevel2s, value);
        }

        // 前方杭後方杭
        private ObservableCollection<bool> _isFrontPiles;
        public ObservableCollection<bool> IsFrontPiles
        {
            get => _isFrontPiles;
            set => SetProperty(ref _isFrontPiles, value);
        }

        // 杭先端N値
        private double _pileTipNValue;
        public double PileTipNValue
        {
            get => _pileTipNValue;
            set => SetFiniteClampedDouble(ref _pileTipNValue, value, min: 0.0, max: 1000.0, fallback: 0.0);
        }

        // 極限周面抵抗力
        private double _rf;
        public double Rf
        {
            get => _rf;
            set => SetProperty(ref _rf, value);
        }

        // 極限先端支持力
        private double _rp;
        public double Rp
        {
            get => _rp;
            set => SetProperty(ref _rp, value);
        }

        // 極限鉛直支持力
        private double _ru;
        public double Ru
        {
            get => _ru;
            set => SetProperty(ref _ru, value);
        }

        // 長期群杭沈下
        private double _groupPileSettlement;
        public double GroupPileSettlement
        {
            get => _groupPileSettlement;
            set => SetProperty(ref _groupPileSettlement, value);
        }

        // 解析杭節点
        private ObservableCollection<FEM.Node> pileNodes = [];
        public ObservableCollection<FEM.Node> PileNodes
        {
            get => pileNodes;
            set => SetProperty(ref pileNodes, value);
        }

        // 解析土節点
        private ObservableCollection<FEM.Node> soilNodes = [];
        public ObservableCollection<FEM.Node> SoilNodes
        {
            get => soilNodes;
            set => SetProperty(ref soilNodes, value);
        }


        // 解析水平地盤ばね
        private ObservableCollection<HorizontalSoilSpring> horizontalSoilSprings = [];
        public ObservableCollection<HorizontalSoilSpring> HorizontalSoilSprings
        {
            get => horizontalSoilSprings;
            set => SetProperty(ref horizontalSoilSprings, value);
        }

        // 解析杭頭回転ばね
        private RotationalSpring pileTopRotationalSpring;
        public RotationalSpring PileTopRotationalSpring
        {
            get => pileTopRotationalSpring;
            set => SetProperty(ref pileTopRotationalSpring, value);
        }

        // 解析要素
        private ObservableCollection<FEM.Beam> beams = [];
        public ObservableCollection<FEM.Beam> Beams
        {
            get => beams;
            set => SetProperty(ref beams, value);
        }

        // 解析軸力
        private double _axialForce;
        public double AxialForce
        {
            get => _axialForce;
            set => SetProperty(ref _axialForce, value);
        }

        // 解析軸力増分
        private double _axialForceIncrement;
        public double AxialForceIncrement
        {
            get => _axialForceIncrement;
            set => SetProperty(ref _axialForceIncrement, value);
        }

        // コンストラクタ初期化
        public PileLayoutDataItem()
        {
            IsVisible = true;

            GroundNo = 1;
            PileBodyNo = 1;
            SoilPileAltNo = 1;
            GroupPileFactor = 1;

            AxialForceLevel1s = [];
            AxialForceLevel2s = [];

            IsFrontPiles = [];

            SinglePileSettlementLevel1s = [];
            SinglePileSettlementLevel2s = [];
            // 4方向ずつ初期化
            for (int i = 0; i < 4; i++)
            {
                AxialForceLevel1s.Add(0.0);
                AxialForceLevel2s.Add(0.0);

                SinglePileSettlementLevel1s.Add(0.0);
                SinglePileSettlementLevel2s.Add(0.0);

                IsFrontPiles.Add(true);
            }

            // Z変更時にも SoilPile 再評価を通知
            this.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Z")
                {
                    OnPropertyChanged(nameof(SoilPile));
                }
            };
        }

        public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)
        {
            // readonlyを外して、後からセットできるようにする
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        }

        public double GetSeismicAxialForce(int loadCaseNo, int level)
        {
            if (level == 1)
            {
                if (loadCaseNo < 0 || AxialForceLevel1s.Count < loadCaseNo)
                    throw new ArgumentOutOfRangeException(nameof(loadCaseNo), "Invalid load case index for Level 1.");
                return AxialForceLevel1s[loadCaseNo - 1];
            }
            else // level == 2
            {
                if (loadCaseNo < 0 || AxialForceLevel1s.Count < loadCaseNo)
                    throw new ArgumentOutOfRangeException(nameof(loadCaseNo), "Invalid load case index for Level 2.");
                return AxialForceLevel2s[loadCaseNo - 1];
            }
        }


        // DataGrid再描画用: SoilPile再評価通知
        public void NotifySoilPileChanged() => OnPropertyChanged(nameof(SoilPile));
    }
}
