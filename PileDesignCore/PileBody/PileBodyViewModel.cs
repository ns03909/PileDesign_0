using PileDesignCore.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using static PileDesignCore.Shared.PileTypeOptionsMapper;

namespace PileDesignCore
{
    [Serializable]
    public class PileBodyDataItem : BaseDataItem
    {
        private readonly PileBodyViewModel viewModel;

        // コンストラクタ
        public PileBodyDataItem(PileBodyViewModel viewModel)
        {
            this.viewModel = viewModel;
            PileSection = new PileSectionViewModel();
        }

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private double _segmentLength = 5.0;
        public double SegmentLength
        {
            get => _segmentLength;
            set
            {
                if (SetProperty(ref _segmentLength, value))
                {
                    viewModel.RecalculateDataGridPileBody();
                }
            }
        }

        private double _segmentDepth;
        public double SegmentDepth
        {
            get => _segmentDepth;
            set => SetProperty(ref _segmentDepth, value);
        }

        private PileSectionViewModel _pileSection = new PileSectionViewModel();
        public PileSectionViewModel PileSection
        {
            get => _pileSection;
            set => SetProperty(ref _pileSection, value);
        }
    }

    /// <summary>

    /// </summary>
    [Serializable]
    public class PileBodyViewModel : BaseViewModel
    // PileBodyViewModel変数の初期値
    {
        private ObservableCollection<PileBodyDataItem> _selectedPileBodyCollection;
        public ObservableCollection<PileBodyDataItem> SelectedPileBodyCollection
        {
            get => _selectedPileBodyCollection;
            set => SetProperty(ref _selectedPileBodyCollection, value);
        }

        public ObservableCollection<ObservableCollection<PileBodyDataItem>> DataGridPileBody { get; } = new ObservableCollection<ObservableCollection<PileBodyDataItem>>();

        private int _selectedPileBodyNo = 1;
        public int SelectedPileBodyNo
        {
            get => _selectedPileBodyNo;
            set => SetProperty(ref _selectedPileBodyNo, value);
        }

        public ObservableCollection<string> PileBodyRefs { get; } = new ObservableCollection<string>();
        private string _pileBodyRef;
        public string PileBodyRef
        {
            get => _pileBodyRef;
            set => SetProperty(ref _pileBodyRef, value);
        }

        public ObservableCollection<string> SelectedPileBodyTypes { get; } = new ObservableCollection<string>();
        private string _selectedPileBodyType;
        public string SelectedPileBodyType
        {
            get => _selectedPileBodyType;
            set
            {
                if (SetProperty(ref _selectedPileBodyType, value))
                {
                    UpdateOptionsForSelectedPileBodyType();
                }
            }
        }

        /// <summary>
        /// 選択された杭種別に応じて、工法と杭頭接合の選択肢を更新
        /// </summary>
        private void UpdateOptionsForSelectedPileBodyType()
        {
            var update = PileTypeOptionsMapper.UpdateOptionsForPileBodyType(SelectedPileBodyType);
            SelectedPileConstructionTypeOption = update.ConstructionTypeOptions;
            SelectedPileConstructionType = update.DefaultConstructionType;
            SelectedPileTopTypeOption = update.PileTopTypeOptions;
            SelectedPileTopType = update.DefaultPileTopType;
        }

        public ObservableCollection<string> PileBodyTypeOption { get; } = new ObservableCollection<string>()
        {
            "場所打ち鉄筋コンクリート杭",
            "場所打ち鋼管コンクリート杭",
            "既製コンクリート杭",
            "鋼管杭"
        };

        public ObservableCollection<string> SelectedPileConstructionTypes { get; } = new ObservableCollection<string>();

        private ObservableCollection<string> _selectedPileConstructionTypeOption;
        public ObservableCollection<string> SelectedPileConstructionTypeOption
        {
            get => _selectedPileConstructionTypeOption;
            set => SetProperty(ref _selectedPileConstructionTypeOption, value);
        }

        private string _selectedPileConstructionType;
        public string SelectedPileConstructionType
        {
            get => _selectedPileConstructionType;
            set => SetProperty(ref _selectedPileConstructionType, value);
        }

        public ObservableCollection<string> SelectedPileTopTypes { get; } = new ObservableCollection<string>();
        private string _selectedPileTopType;
        public string SelectedPileTopType
        {
            get => _selectedPileTopType;
            set => SetProperty(ref _selectedPileTopType, value);
        }


        private ObservableCollection<string> _selectedPileTopTypeOption;
        public ObservableCollection<string> SelectedPileTopTypeOption
        {
            get => _selectedPileTopTypeOption;
            set => SetProperty(ref _selectedPileTopTypeOption, value);
        }

        public ObservableCollection<double> PileToeDias { get; } = new ObservableCollection<double>();
        private double _pileToeDia;
        public double PileToeDia
        {
            get => _pileToeDia;
            set => SetProperty(ref _pileToeDia, value);
        }

        public ObservableCollection<double> TipNonPermabilities { get; } = new ObservableCollection<double>();
        private double _tipNonPermability;
        public double TipNonPermability
        {
            get => _tipNonPermability;
            set => SetProperty(ref _tipNonPermability, value);
        }

        public ObservableCollection<string> TipStyleOption { get; } = new ObservableCollection<string>()
        {
            "開端杭",
            "閉端杭",
        };

        public ObservableCollection<string> SelectedTipStyles { get; } = new ObservableCollection<string>();
        private string _selectedTipStyle;
        public string SelectedTipStyle
        {
            get => _selectedTipStyle;
            set => SetProperty(ref _selectedTipStyle, value);
        }

        // 支持層への根入れ深さLB(m)
        public ObservableCollection<double> EmbedmentIntoBearingSoils { get; } = new ObservableCollection<double>();
        private double _embedmentIntoBearingSoil;
        public double EmbedmentIntoBearingSoil
        {
            get => _embedmentIntoBearingSoil;
            set => SetProperty(ref _embedmentIntoBearingSoil, value);
        }

        // 杭の内径dI(m)
        public ObservableCollection<double> PileInnerDias { get; } = new ObservableCollection<double>();
        private double _pileInnerDia;
        public double PileInnerDia
        {
            get => _pileInnerDia;
            set => SetProperty(ref _pileInnerDia, value);
        }

        public ObservableCollection<double> SettlePileToeDias { get; } = new ObservableCollection<double>();
        private double _settlePileToeDia;
        public double SettlePileToeDia
        {
            get => _settlePileToeDia;
            set => SetProperty(ref _settlePileToeDia, value);
        }

        public ObservableCollection<double> SettleAlphas { get; } = new ObservableCollection<double>();
        private double _settleAlpha;
        public double SettleAlpha
        {
            get => _settleAlpha;
            set => SetProperty(ref _settleAlpha, value);
        }

        public ObservableCollection<double> SettleNs { get; } = new ObservableCollection<double>();
        private double _settleN;
        public double SettleN
        {
            get => _settleN;
            set => SetProperty(ref _settleN, value);
        }

        // 名前、alphaの値、nの値を格納する構造体
        public struct PileTipSettlementPresetParameter
        {
            public string Name;
            public string SoilType;
            public double Alpha;
            public double N;

            public PileTipSettlementPresetParameter(string name, string soilType, double alpha, double n)
            {
                Name = name;
                SoilType = soilType;
                Alpha = alpha;
                N = n;
            }
        }

        // 構造体を使ってデータのセットを作成
        public List<PileTipSettlementPresetParameter> PileTipSettlementPresetParameters = new List<PileTipSettlementPresetParameter>();
        public List<string> PileTipSettlementPresetParameterNames = new List<string>();

        public event EventHandler RecalculateDataGridPileBodyCompleted;
        protected virtual void OnRecalculateDataGridPileBodyCompleted(EventArgs e)
        {
            RecalculateDataGridPileBodyCompleted?.Invoke(this, e);
        }

        public PileTopViewModel PileTop = new PileTopViewModel();

        // コンストラクタ
        public PileBodyViewModel()
        {
            // Initialize the GroundRefs property with 5 empty strings
            PileBodyRefs = CollectionHelper.CreateStringCollection("(PB1)", "(PB2)", "(PB3)", "(PB4)", "(PB5)");
            SelectedPileTopTypes = CollectionHelper.CreateStringCollection(5);
            SelectedPileBodyTypes = CollectionHelper.CreateStringCollection(5);
            SelectedPileConstructionTypes = CollectionHelper.CreateStringCollection(5);
            PileToeDias = CollectionHelper.CreateDoubleCollection(5);
            TipNonPermabilities = CollectionHelper.CreateDoubleCollection(5);
            EmbedmentIntoBearingSoils = CollectionHelper.CreateDoubleCollection(5);
            PileInnerDias = CollectionHelper.CreateDoubleCollection(5);
            SelectedTipStyles = CollectionHelper.CreateStringCollection(5);
            SettlePileToeDias = CollectionHelper.CreateDoubleCollection(5);
            SettleAlphas = CollectionHelper.CreateDoubleCollection(5);
            SettleNs = CollectionHelper.CreateDoubleCollection(5);
            for (int i = 0; i < 5; i++)
            {
                DataGridPileBody.Add(new ObservableCollection<PileBodyDataItem>());
            }

            // データを読み込む
            LoadPresetSettlementParameters();
        }

        // CSVからデータを読み込む
        private void LoadPresetSettlementParameters()
        {
            string csvFilePath = "../../PileLibrary/PresetSettlementParameterSet.csv";

            PileTipSettlementPresetParameters = CsvLoaderHelper.LoadFromCsv(
                csvFilePath,
                parts =>
                {
                    if (parts.Length == 4)
                    {
                        string name = parts[0].Trim();
                        string soilType = parts[1].Trim();
                        double alpha = double.Parse(parts[2].Trim());
                        double n = double.Parse(parts[3].Trim());
                        return new PileTipSettlementPresetParameter(name, soilType, alpha, n);
                    }
                    return default;
                });

            PileTipSettlementPresetParameterNames = PileTipSettlementPresetParameters
                .Select(p => $"{p.Name}-{p.SoilType}")
                .ToList();
        }


        public void RecalculateDataGridPileBody()
        {
            double _sum = 0;
            for(int i = 0; i< SelectedPileBodyCollection.Count;  i++)
            {
                _sum += SelectedPileBodyCollection[i].SegmentLength;
                SelectedPileBodyCollection[i].SegmentDepth = _sum;
            }
            OnRecalculateDataGridPileBodyCompleted(EventArgs.Empty); // イベントを発生させる
        }
        

    }
}


