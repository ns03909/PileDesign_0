using PileDesign.FEM;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    // SoilPileクラス
    public class SoilPile : BaseModel
    {
        public int No { get; set; }
        //private readonly InputModel inputModel = InputModel.Instance;
        public int GroundNo { get; set; }
        public int PileBodyNo { get; set; }


        // 地盤
        private GroundInput _groundInput;
        public GroundInput GroundInput
        {
            get => _groundInput;
            set => SetProperty(ref _groundInput, value);
        }

        // 杭体
        private PileBodyInput _pileBodyInput;
        public PileBodyInput PileBodyInput
        {
            get => _pileBodyInput;
            set => SetProperty(ref _pileBodyInput, value);
        }

        // 杭頭Z
        private double _z;
        public double Z
        {
            get => _z;
            set
            {
                if (SetProperty(ref _z, value))
                {
                    UpdatePileBottomAltitude();
                }
            }
        }

        // 杭先端標高
        private double _pileBottomAltitude;
        public double PileBottomAltitude
        {
            get => _pileBottomAltitude;
            set => SetProperty(ref _pileBottomAltitude, value);
        }

        // 節点 (ZDataItems.Z: Z)
        private ObservableCollection<PileZDataItem> _zDataItems;
        public ObservableCollection<PileZDataItem> ZDataItems
        {
            get => _zDataItems;
            set
            {
                if (SetProperty(ref _zDataItems, value))
                {
                    foreach (var zDataItem in _zDataItems)
                    {
                        zDataItem.SetSoilDisplacement(/*InputModel.Instance*/);
                    }
                }
            }
        }

        // 土層(総数：節点数-1 = 要素数分)
        //public ObservableCollection<GroundLayerInput> GroundLayers { get; set; }
        private ObservableCollection<GroundLayerInput> _groundLayers;
        public ObservableCollection<GroundLayerInput> GroundLayers
        {
            get => _groundLayers;
            set => SetProperty(ref _groundLayers, value);
        }

        // 杭断面(総数：節点数-1 = 要素数分)
        private ObservableCollection<PileBodySegment> _pileBodySegments;
        public ObservableCollection<PileBodySegment> PileBodySegments
        {
            get => _pileBodySegments;
            set
            {
                if (SetProperty(ref _pileBodySegments, value))
                {
                    _pileBodySegments.CollectionChanged += (s, e) => UpdatePileBottomAltitude();
                    UpdatePileBottomAltitude();
                }
            }
        }

        // 水平地盤反力(総数：節点数-1 = 要素数分)
        private ObservableCollection<HorizontalSoilReactionItem> _horizontalSoilReactions = [];
        public ObservableCollection<HorizontalSoilReactionItem> HorizontalSoilReactions
        {
            get => _horizontalSoilReactions;
            set => SetProperty(ref _horizontalSoilReactions, value);
        }

        // 杭周鉛直力
        public ObservableCollection<PileCircumVertical> PileCircumVerticals { get; set; }


        // 群杭荷重円径
        private double _groupPileLoadDia;
        public double GroupPileLoadDia
        {
            get => _groupPileLoadDia;
            set => SetProperty(ref _groupPileLoadDia, value);
        }

        // 杭先端N値
        public double PileToeNValue
        {
            //get
            //{
            //    var groundMassesData = InputModel.Instance.GroundsInput[GroundNo - 1].GroundMassesData;
            //    var relevantMasses = groundMassesData
            //        .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude && 
            //        data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
            //        .ToList();

            //    if (relevantMasses.Count == 0)
            //    {
            //        return 0; // 適切なデフォルト値を返す
            //    }

            //    return Math.Min(relevantMasses.Average(data => Math.Min(data.NValue, 100)), 60);
            //}
            get
            {
                var groundMassesData = GroundInput.GroundMassesData;
                var relevantMasses = groundMassesData
                    .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude &&
                                   data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
                    .ToList();

                if (relevantMasses.Count == 0) return 0;
                return Math.Min(relevantMasses.Average(data => Math.Min(data.NValue, 100)), 60);
            }
        }

        // 杭先端平均N値用N値
        public string NValuesForAverage
        {
            //get
            //{
            //    var groundMassesData = InputModel.Instance.GroundsInput[GroundNo - 1].GroundMassesData;
            //    var relevantMasses = groundMassesData
            //        .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude && 
            //        data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
            //        .Select(data => Math.Min(data.NValue, 100))
            //        .ToArray();

            //    if (relevantMasses.Length == 0)
            //    {
            //        return string.Empty; // 適切なデフォルト値を返す
            //    }

            //    return string.Join(", ", relevantMasses);
            //}
            get
            {
                var groundMassesData = GroundInput.GroundMassesData;
                var relevantMasses = groundMassesData
                    .Where(data => data.AltitudeDepth <= PileToeNValueAverageRangeUpperAltitude &&
                                   data.AltitudeDepth >= PileToeNValueAverageRangeLowerAltitude)
                    .Select(data => Math.Min(data.NValue, 100))
                    .ToArray();

                if (relevantMasses.Length == 0) return string.Empty;
                return string.Join(", ", relevantMasses);
            }
        }


        // 杭先端N値平均範囲上端
        public double PileToeNValueAverageRangeUpperAltitude
        {
            get => PileBottomAltitude + D;
        }

        // 杭先端N値平均範囲下端
        public double PileToeNValueAverageRangeLowerAltitude
        {
            get => PileBottomAltitude - D;
        }

        // 杭先端粘着力
        private double _pileToeCohesive;
        public double PileToeCohesive
        {
            get => _pileToeCohesive;
            set => SetProperty(ref _pileToeCohesive, value);
        }

        // 杭先端閉塞率
        private double _pileToeEta;
        public double PileToeEta
        {
            get => _pileToeEta;
            set => SetProperty(ref _pileToeEta, value);
        }

        // 極限先端支持力度
        private double _qpu;
        public double Qpu
        {
            get => _qpu;
            set => SetProperty(ref _qpu, value);
        }

        // 沈下検討用極限先端支持力度
        private double _settleQpu;
        public double SettleQpu
        {
            get => _settleQpu;
            set => SetProperty(ref _settleQpu, value);
        }

        // 沈下検討用極限先端支持力（沈下計算で使用）
        public double SettleRpu => SettleQpu * Ap;

        // 杭工法
        private string _pileConstructionType;
        public string PileConstructionType
        {
            get => _pileConstructionType;
            set => SetProperty(ref _pileConstructionType, value);
        }

        // 杭先端土質
        private string _pileToeGranularityClass;
        public string PileToeGranularityClass
        {
            get => _pileToeGranularityClass;
            set => SetProperty(ref _pileToeGranularityClass, value);
        }

        // 杭先端径 m
        private double _d;
        public double D
        {
            get => _d;
            set => SetProperty(ref _d, value);
        }

        // 杭先端径 m
        private double _dp;
        public double Dp
        {
            get => _dp;
            set => SetProperty(ref _dp, value);
        }

        // 杭先端面積 m2
        public double Ap => Math.PI * Math.Pow(D, 2) * 0.25;

        // 極限先端支持力 kN
        public double Rpu => Qpu * Ap;

        // 極限周面抵抗力 kN
        private double _rfu;
        public double Rfu
        {
            get => _rfu;
            set => SetProperty(ref _rfu, value);
        }

        // 極限鉛直支持力 kN
        public double Ru => Rpu + Rfu;

        // 使用限界支持力 kN
        public double R_SLS => (1.0 / 3.0) * Ru;

        // 損傷限界支持力 kN
        public double R_DLS => (1.0 / 1.5) * Ru;

        // 終局限界支持力 kN
        public double R_ULS => Ru;

        // 使用限界引抜力 kN
        //public double Rt_SLS => (1.0 / 3.0) * Rtu;
        private double _rt_SLS;
        public double Rt_SLS
        {
            get => _rt_SLS;
            set => SetProperty(ref _rt_SLS, value);
        }

        // 損傷限界引抜力 kN
        //public double Rt_DLS => Rty;
        private double _rt_DLS;
        public double Rt_DLS
        {
            get => _rt_DLS;
            set => SetProperty(ref _rt_DLS, value);
        }

        // 終局限界引抜力 kN
        private double _rt_ULS;
        public double Rt_ULS
        {
            get => _rt_ULS;
            set => SetProperty(ref _rt_ULS, value);
        }

        // 最大引き抜き抵抗力 kN
        private double _rtu;
        public double Rtu
        {
            get => _rtu;
            set => SetProperty(ref _rtu, value);
        }

        // 残留引抜き抵抗力 kN
        private double _rtr;
        public double Rtr
        {
            get => _rtr;
            set => SetProperty(ref _rtr, value);
        }

        // 降伏引抜き抵抗力 kN
        private double _rty;
        public double Rty
        {
            get => _rty;
            set => SetProperty(ref _rty, value);
        }


        // 解析結果（荷重-沈下曲線）
        public ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacements { get; set; } = [];
        public ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> LoadDisplacementsLimit { get; set; } = [];


        const double epsilon = 1e-5; // 許容誤差（必要に応じて調整）

        // コンストラクタ
        public SoilPile()
        {
            GroundLayers = [];
            PileBodySegments = [];
            PileCircumVerticals = [];
            HorizontalSoilReactions = [];
            LoadDisplacements = [];
            LoadDisplacementsLimit = [];
        }

        // 追加: 初期化メソッド
        public void Initialize(
            int no,
            int groundNo,
            GroundInput groundInput,
            int pileBodyNo,
            PileBodyInput pileBodyInput,
            double z,
            ObservableCollection<PileZDataItem> zDataItems)
        {
            No = no;
            GroundNo = groundNo;
            PileBodyNo = pileBodyNo;
            GroundInput = groundInput;
            PileBodyInput = pileBodyInput;
            Z = z;
            ZDataItems = zDataItems;

            foreach (var zDataItem in zDataItems)
            {
                zDataItem.GroundInput = groundInput;
                zDataItem.SetSoilDisplacement();
            }

            PileBodySegments.CollectionChanged += (s, e) => UpdatePileBottomAltitude();
            UpdateProperties();

            //InputModel inputModel = InputModel.Instance;
            //Dp = inputModel.PileBodies[PileBodyNo - 1].SettlePileToeDia;
            Dp = PileBodyInput.SettlePileToeDia;
        }

        // メソッド //

        public void NotifyAllPropertiesChanged()
        {
            // バックフィールドを持つプロパティ
            OnPropertyChanged(nameof(No));
            OnPropertyChanged(nameof(GroundInput));
            OnPropertyChanged(nameof(PileBodyInput));
            OnPropertyChanged(nameof(Z));
            OnPropertyChanged(nameof(PileBottomAltitude));
            OnPropertyChanged(nameof(ZDataItems));
            OnPropertyChanged(nameof(GroundLayers));
            OnPropertyChanged(nameof(PileBodySegments));
            OnPropertyChanged(nameof(HorizontalSoilReactions));
            OnPropertyChanged(nameof(PileCircumVerticals));
            OnPropertyChanged(nameof(GroupPileLoadDia));
            OnPropertyChanged(nameof(PileToeCohesive));
            OnPropertyChanged(nameof(PileToeEta));
            OnPropertyChanged(nameof(Qpu));
            OnPropertyChanged(nameof(PileConstructionType));
            OnPropertyChanged(nameof(PileToeGranularityClass));
            OnPropertyChanged(nameof(D));
            OnPropertyChanged(nameof(Dp));
            OnPropertyChanged(nameof(Rfu));
            OnPropertyChanged(nameof(Rt_SLS));
            OnPropertyChanged(nameof(Rt_DLS));
            OnPropertyChanged(nameof(Rt_ULS));
            OnPropertyChanged(nameof(Rtu));
            OnPropertyChanged(nameof(Rtr));
            OnPropertyChanged(nameof(Rty));
            OnPropertyChanged(nameof(LoadDisplacements));
            OnPropertyChanged(nameof(LoadDisplacementsLimit));

            // 計算プロパティ（getのみ）
            OnPropertyChanged(nameof(PileToeNValue));
            OnPropertyChanged(nameof(NValuesForAverage));
            OnPropertyChanged(nameof(PileToeNValueAverageRangeUpperAltitude));
            OnPropertyChanged(nameof(PileToeNValueAverageRangeLowerAltitude));
            OnPropertyChanged(nameof(Ap));
            OnPropertyChanged(nameof(Rpu));
            OnPropertyChanged(nameof(Ru));
            OnPropertyChanged(nameof(R_SLS));
            OnPropertyChanged(nameof(R_DLS));
            OnPropertyChanged(nameof(R_ULS));
        }

        // PileBottomAltitude更新メソッド
        private void UpdatePileBottomAltitude()
        {
            if (PileBodySegments != null && PileBodySegments.Count > 0)
            {
                PileBottomAltitude = Z - PileBodySegments[^1].SegmentDepth; // 杭頭標高 - 最下段区間深さ
            }
        }

        // ZDataItemsの更新後に実行するメソッド
        public void OnZDataItemsChanged(
            ObservableCollection<PileZDataItem> zDataItems
            )
        {
            ZDataItems = zDataItems;
            UpdateProperties();
        }

        // プロパティ更新メソッド
        public void UpdateProperties()
        {

            foreach (var zDataItem in ZDataItems)
            {
                zDataItem.SetSoilDisplacement();
            }

            Dp = PileBodyInput.SettlePileToeDia;

            double groundTopAltitude = GroundInput.GroundTopAltitude;
            var groundLayers = GroundInput.GroundLayers;
            var pileBodySegments = PileBodyInput.PileBodySegments;

            MatchGroundLayersAndPileBodySegments(/*groundTopAltitude,*/ groundLayers, pileBodySegments);
            CreatePileCircumVerticals();
            UpdatePileProperties();
            UpdatePileToeProperties();
            UpdatePileCircumVerticalProperties();
            CalculateResistances();
            SetHorizontalSoilReaction();
        }

        // GroundLayers, PileBodySegmentsの更新
        private void MatchGroundLayersAndPileBodySegments(
            //double groundTopAltitude,
            ObservableCollection<GroundLayerInput> groundLayers,
            ObservableCollection<PileBodySegment> pileBodySegments
            )
        {
            // Nullチェックと初期化
            if (groundLayers == null || pileBodySegments == null) return;

            if (GroundLayers == null) return;
            if (PileBodySegments == null) return;

            GroundLayers.Clear();
            PileBodySegments.Clear();

            for (int i = 1; i < ZDataItems.Count; i++)
            {
                GroundLayerInput matchedGroundLayer/* = null*/;
                for (int j = 0; j < groundLayers.Count; j++)
                {
                    if (/*Z +*/ ZDataItems[i].Z >= groundLayers[j].BottomAltitude)
                    {
                        matchedGroundLayer = groundLayers[j];
                        GroundLayers.Add(matchedGroundLayer);
                        break;
                    }
                }

                for (int k = 0; k < pileBodySegments.Count; k++)
                {
                    if (ZDataItems[i].Z >= Z - pileBodySegments[k].SegmentDepth ||
                        k == pileBodySegments.Count - 1)
                    {
                        var copiedSegment = pileBodySegments[k].DeepCopy();
                        copiedSegment.SegmentLength = ZDataItems[i - 1].Z - ZDataItems[i].Z;
                        copiedSegment.SegmentDepth = Z - ZDataItems[i].Z; // 杭頭からの相対深さ
                        PileBodySegments.Add(copiedSegment); // SoilPileのPileBodySegments

                        break;
                    }
                }
            }
        }

        // PileCircumVerticals
        private void CreatePileCircumVerticals()
        {
            if (PileCircumVerticals == null) return;
            PileCircumVerticals.Clear();

            for (int j = 0; j < GroundLayers.Count; j++)
            {
                if (j + 1 < ZDataItems.Count && j < PileBodySegments.Count)
                {
                    PileCircumVerticals.Add(new PileCircumVertical()
                    {
                        Top = ZDataItems[j].Z,
                        Bottom = ZDataItems[j + 1].Z,
                        PileBodySegment = PileBodySegments[j],
                        GroundLayer = GroundLayers[j],
                        IsPositiveCircumResistance = GroundLayers[j].IsPositiveCircumResistance,
                        IsNegativeCircumResistance = GroundLayers[j].IsNegativeCircumResistance,
                    });
                }
            }
        }

        // PileProperties
        private void UpdatePileProperties()
        {
            var selectedPileBody = PileBodyInput;
            PileConstructionType = selectedPileBody.PileConstructionType;
            PileToeEta = selectedPileBody.TipNonPermability;
            D = selectedPileBody.PileToeDia / 1000.0; // mm -> m 
        }

        // 杭先端プロパティの更新メソッド
        private void UpdatePileToeProperties()
        {
            if (GroundLayers == null || GroundLayers.Count == 0) return;

            var lastGroundLayer = GroundLayers[^1];
            PileToeGranularityClass = lastGroundLayer.GranularityClass;
            PileToeCohesive = lastGroundLayer.Cohesive;

            Qpu = PileConstructionType switch
            {
                "場所打ちコンクリート杭" => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(120 * PileToeNValue, 7500),
                    "礫質土" => Math.Min(120 * PileToeNValue, 7500),
                    "粘性土" => Math.Min(6 * PileToeCohesive, 7500),
                    _ => Qpu
                },
                "埋込み杭（プレポーリング）" => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeNValue, 9000),
                    "礫質土" => Math.Min(150 * PileToeNValue, 9000),
                    "粘性土" => Math.Min(150 * PileToeNValue, 9000),
                    _ => Qpu
                },
                "埋込み杭（中掘り）" => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeNValue, 9000),
                    "礫質土" => Math.Min(150 * PileToeNValue, 9000),
                    "粘性土" => Math.Min(6 * PileToeCohesive, 9000),
                    _ => Qpu
                },
                "回転貫入杭" => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    "礫質土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    "粘性土" => Math.Min(150 * PileToeEta * PileToeNValue, 9000 * PileToeEta),
                    _ => Qpu
                },
                "打込み杭" => PileToeGranularityClass switch
                {
                    "砂質土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    "礫質土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    "粘性土" => Math.Min(3 * PileToeEta * PileToeNValue, 18000),
                    _ => Qpu
                },
                _ => Qpu
            };

            // 沈下検討用極限先端支持力度をデフォルトで極限先端支持力度と同じ値に設定
            SettleQpu = Qpu;
        }

        // 杭周面抵抗プロパティの更新メソッド
        private void UpdatePileCircumVerticalProperties()
        {
            if (PileCircumVerticals == null) return;
            foreach (var pileCircumVertical in PileCircumVerticals)
            {
                var granularityClass = pileCircumVertical.GroundLayer.GranularityClass;
                var cohesive = pileCircumVertical.GroundLayer.Cohesive;
                var nValue = pileCircumVertical.GroundLayer.NValue;

                pileCircumVertical.Tau2 = PileConstructionType switch
                {
                    "場所打ちコンクリート杭" => granularityClass switch
                    {
                        "砂質土" => Math.Min(3.3 * nValue, 165),
                        "礫質土" => Math.Min(3.3 * nValue, 165),
                        "粘性土" => Math.Min(cohesive, 100),
                        _ => pileCircumVertical.Tau2
                    },
                    "埋込み杭（プレポーリング）" => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.5 * nValue, 125),
                        "礫質土" => Math.Min(2.5 * nValue, 125),
                        "粘性土" => Math.Min(cohesive, 125),
                        _ => pileCircumVertical.Tau2
                    },
                    "埋込み杭（中掘り）" => granularityClass switch
                    {
                        "砂質土" => Math.Min(1.5 * nValue, 75),
                        "礫質土" => Math.Min(1.5 * nValue, 75),
                        "粘性土" => Math.Min(0.4 * cohesive, 50),
                        _ => pileCircumVertical.Tau2
                    },
                    "回転貫入杭" => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.0 * nValue, 100),
                        "礫質土" => Math.Min(2.0 * nValue, 100),
                        "粘性土" => Math.Min(0.5 * cohesive, 62.5),
                        _ => pileCircumVertical.Tau2
                    },
                    "打込み杭" => granularityClass switch
                    {
                        "砂質土" => Math.Min(2.0 * nValue, 100),
                        "礫質土" => Math.Min(2.0 * nValue, 100),
                        "粘性土" => Math.Min(0.8 * cohesive, 100),
                        _ => pileCircumVertical.Tau2
                    },
                    _ => pileCircumVertical.Tau2
                };

                (pileCircumVertical.Tau1, pileCircumVertical.S1, pileCircumVertical.S2, pileCircumVertical.TauT) = granularityClass switch
                {
                    "砂質土" => (0.8 * pileCircumVertical.Tau2, 5.0, 20.0, -2.0 / 3.0 * pileCircumVertical.Tau2),
                    "礫質土" => (0.7 * pileCircumVertical.Tau2, 10.0, 30.0, -2.0 / 3.0 * pileCircumVertical.Tau2),
                    "粘性土" => (0.8 * pileCircumVertical.Tau2, 3.0, 10.0, -4.0 / 5.0 * pileCircumVertical.Tau2),
                    _ => (pileCircumVertical.Tau1, pileCircumVertical.S1, pileCircumVertical.S2, pileCircumVertical.TauT)
                };

                if (!pileCircumVertical.IsPositiveCircumResistance)
                {
                    pileCircumVertical.Tau1 = 0.0;
                    pileCircumVertical.Tau2 = 0.0;
                }

                if (!pileCircumVertical.IsNegativeCircumResistance)
                {
                    pileCircumVertical.TauT = 0.0;
                }
            }
        }

        // 抵抗力の計算メソッド
        public void CalculateResistances()
        {
            if (PileCircumVerticals == null) return;

            // PileCircumVerticalのフラグを使用（チェックボックスで編集可能）
            Rfu = PileCircumVerticals.Where(pcv => pcv.IsPositiveCircumResistance).Sum(pcv => pcv.Rf);
            Rtu = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rtu);
            Rtr = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rtr);
            Rty = PileCircumVerticals.Where(pcv => pcv.IsNegativeCircumResistance).Sum(pcv => pcv.Rty);

            double w = PileCircumVerticals.Sum(pcv => pcv.PileBodySegment.PileSection.W * pcv.L);

            Rtu -= w;
            Rty -= w;
            Rtr -= w;

            Rt_SLS = (1.0 / 3.0) * Rtu;
            Rt_DLS = Rty;
            Rt_ULS = Rtr;
        }

        // HorizontalSoilReactionの更新
        private void SetHorizontalSoilReaction()
        {
            if (ZDataItems == null || ZDataItems.Count < 2) return;

            if (PileBodySegments == null || GroundLayers == null) return;

            HorizontalSoilReactions.Clear();

            double zTop = ZDataItems[0].Z;
            for (int i = 0; i < ZDataItems.Count - 1; i++)
            {
                var zDataTop = ZDataItems[i].Z;
                var zDataBtm = ZDataItems[i + 1].Z;

                double b = 0;
                double cohesive = 0;
                double nValue = 0;
                double gamma = 0;
                double phi = 0;
                double stressTop = 0;
                double stressBtm = 0;
                string soilType = string.Empty;
                double e0 = 0;
                string name = string.Empty;
                double xi = 1;
                double rOnB = 10_000;

                foreach (PileBodySegment pileBodySegment in PileBodyInput.PileBodySegments)
                {
                    double segmentTop = -pileBodySegment.SegmentDepth + pileBodySegment.SegmentLength;
                    double segmentBtm = -pileBodySegment.SegmentDepth;

                    double upper = zDataTop - zTop; // 杭頭基準深さ
                    double lower = zDataBtm - zTop; // 杭頭基準深さ

                    if (segmentBtm - epsilon <= lower && upper <= segmentTop + epsilon)
                    {
                        //if (pileBodySegment.PileSection == null)
                        //{
                        //    System.Diagnostics.Debug.WriteLine("PileSection is null");
                        //}
                        //else
                        //{
                        //    System.Diagnostics.Debug.WriteLine($"B={pileBodySegment.PileSection.PileDiameter}");
                        //}
                        b = pileBodySegment.PileSection?.PileDiameter / 1000.0 ?? 0;
                        break;
                    }
                }

                if (Math.Abs(b) < epsilon)
                {
                    System.Diagnostics.Debug.WriteLine($"No segment found for Z={zDataTop}～{zDataBtm}");
                }

                foreach (GroundLayerInput groundLayer in GroundLayers)
                {
                    //var groundInput = inputModel.GroundsInput[GroundNo - 1];
                    var groundInput = GroundInput;
                    double top = groundLayer.LayerThickness + groundLayer.BottomAltitude;
                    double bottom = groundLayer.BottomAltitude;

                    if (bottom - epsilon <= zDataBtm && zDataTop <= top + epsilon)
                    {
                        cohesive = groundLayer.Cohesive;
                        nValue = groundLayer.NValue;
                        gamma = groundLayer.Density;
                        //phi = Math.Min(Math.Sqrt(20 * groundLayer.NValue) + 15, 40); // 大崎式

                        stressTop = GetEffectiveStress(groundInput, zDataTop);
                        stressBtm = GetEffectiveStress(groundInput, zDataBtm);

                        phi = groundInput.GetFrictionAngle(nValue, (stressTop + stressBtm) * 0.5);
                        soilType = groundLayer.GranularityClass;
                        e0 = groundLayer.Es;
                        name = groundLayer.Name;
                        break;
                    }
                }

                HorizontalSoilReactionItem horizontalSoilReactionItem = new();
                horizontalSoilReactionItem.SetParameters(
                name, soilType, gamma, b, e0,
                zDataTop, zDataBtm,
                xi, rOnB, nValue, phi, cohesive, stressTop, stressBtm);

                HorizontalSoilReactions.Add(horizontalSoilReactionItem);
            }
        }

        // 有効応力を得るメソッド
        private static double GetEffectiveStress(GroundInput groundInput, double z)
        {
            double stressLevel = groundInput.StressAltitude;
            double waterLevel = groundInput.GroundWaterTableAltitude;

            double effectiveStress = 0;

            foreach (var groundLayer in groundInput.GroundLayers)
            {
                double density = groundLayer.Density;
                double topAltitude = groundLayer.BottomAltitude + groundLayer.LayerThickness;
                double layerTopLevel = Math.Min(stressLevel, topAltitude);
                double layerBottomLevel = Math.Max(z, groundLayer.BottomAltitude);

                effectiveStress += Math.Max((layerTopLevel - layerBottomLevel) * density, 0);
            }
            effectiveStress -= Math.Max(waterLevel - z, 0) * 10;

            return effectiveStress;
        }


        // DeepCopy メソッド
        public SoilPile DeepCopy()
        {
            var copy = new SoilPile();
            copy.Initialize(
                this.No,
                this.GroundNo,
                this.GroundInput,
                this.PileBodyNo,
                this.PileBodyInput,
                this.Z,
                new ObservableCollection<PileZDataItem>(this.ZDataItems.Select(item => item.DeepCopy()))
            );
            {
                copy.PileBottomAltitude = this.PileBottomAltitude;
                //PileToeNValue = this.PileToeNValue,
                copy.PileToeCohesive = this.PileToeCohesive;
                copy.PileToeEta = this.PileToeEta;
                copy.Qpu = this.Qpu;
                copy.SettleQpu = this.SettleQpu;
                copy.PileConstructionType = this.PileConstructionType;
                copy.PileToeGranularityClass = this.PileToeGranularityClass;
                copy.D = this.D;
                copy.Dp = this.Dp;
                copy.Rfu = this.Rfu;
                copy.Rt_SLS = this.Rt_SLS;
                copy.Rt_DLS = this.Rt_DLS;
                copy.Rt_ULS = this.Rt_ULS;
                copy.Rtu = this.Rtu;
                copy.Rtr = this.Rtr;
                copy.Rty = this.Rty;
                copy.GroupPileLoadDia = this.GroupPileLoadDia;

                // 解析結果（荷重-沈下曲線）
                copy.LoadDisplacements = this.LoadDisplacements;
                copy.LoadDisplacementsLimit = this.LoadDisplacementsLimit;

                copy.PileBodySegments = new ObservableCollection<PileBodySegment>
                    (this.PileBodySegments.Select(segment => segment.DeepCopy()));
                copy.GroundLayers = new ObservableCollection<GroundLayerInput>
                    (this.GroundLayers.Select(layer => layer.DeepCopy()));
                copy.PileCircumVerticals = new ObservableCollection<PileCircumVertical>
                    (this.PileCircumVerticals.Select(pileCircumVertical => pileCircumVertical.DeepCopy()));
                copy.HorizontalSoilReactions = new ObservableCollection<HorizontalSoilReactionItem>
                    (this.HorizontalSoilReactions.Select(horizontalSoilReaction => horizontalSoilReaction.DeepCopy()));
            }
            ;
            return copy;
        }
    }
}

