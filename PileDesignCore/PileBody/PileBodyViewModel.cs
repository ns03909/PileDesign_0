using PileDesignCore.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Media;

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
        private string _selectedPileBodyType; //= "場所打ち杭";
        public string SelectedPileBodyType
        {
            get => _selectedPileBodyType;
            set => SetProperty(ref _selectedPileBodyType, value);
        }

        public ObservableCollection<string> PileBodyTypeOption { get; } = new ObservableCollection<string>()
        {
            "場所打ち鉄筋コンクリート杭",
            "場所打ち鋼管コンクリート杭",
            "既製コンクリート杭",
            "鋼管杭"
        };


        //private void ResetSectionProperties()
        //{
        //    if (SelectedPileBodyType == "場所打ち鉄筋コンクリート杭")
        //    {
        //        PileSection.PileDiameter = 1200.0;
        //        PileSection.SelectedPileSectionType = "鉄筋コンクリート";

        //    }
            //else if (SelectedPileBodyType == "場所打ち鋼管コンクリート杭")
            //{
            //    ConcreteOutDia = 0.0;
            //    PipeDia = 1200.0;
            //    PipeTs = 16.0;

            //}
            //else if (SelectedPileBodyType == "既製コンクリート杭")
            //{
            //    ConcreteOutDia = 0.0;
            //    MainBarNum = 0;
            //    PipeDia = 0.0;
            //    PipeTs = 0.0;
            //}
            //else if (SelectedPileBodyType == "鋼管杭")
            //{
            //    ConcreteOutDia = 0.0;
            //    MainBarNum = 0;
            //    PipeDia = 0.0;
            //    PipeTs = 0.0;
            //}
        //}


        public ObservableCollection<string> SelectedPileConstructionTypes { get; } = new ObservableCollection<string>();
        private string _selectedPileConstructionType;
        public string SelectedPileConstructionType
        {
            get => _selectedPileConstructionType;
            set => SetProperty(ref _selectedPileConstructionType, value);
        }
        public ObservableCollection<string> InsituPileConstructionTypeOption { get; } = new ObservableCollection<string>()
        {
            "場所打ちコンクリート杭",
        };
        public ObservableCollection<string> PrecastPileConstructionTypeOption { get; } = new ObservableCollection<string>()
        {
            "埋込み杭（プレボーリング）",
            "埋込み杭（中掘り）",
            "打込み杭",
        };
        public ObservableCollection<string> SteelPileConstructionTypeOption { get; } = new ObservableCollection<string>()
        {
            "埋込み杭（プレボーリング）",
            "埋込み杭（中掘り）",
            "回転貫入杭",
        };

        public ObservableCollection<string> SelectedPileTopTypes { get; } = new ObservableCollection<string>();
        private string _selectedPileTopType;
        public string SelectedPileTopType
        {
            get => _selectedPileTopType;
            set => SetProperty(ref _selectedPileTopType, value);
        }

        public ObservableCollection<string> InsituReinforcedConcretePileTopTypeOption { get; } = new ObservableCollection<string>()
        {
            "鉄筋定着工法",
            "キャプテンパイル工法",
        };

        public ObservableCollection<string> InsituSteelPipedConcretePileTopTypeOption { get; } = new ObservableCollection<string>()
        {
            "鉄筋定着工法",
        };

        public ObservableCollection<string> PrecastConcretePileTopTypeOption { get; } = new ObservableCollection<string>()
        {
            "鉄筋定着工法",
            "FT-Pile構法"
        };

        public ObservableCollection<string> SteelPileTopTypeOption { get; } = new ObservableCollection<string>()
        {
            "鉄筋定着工法",
        };

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

        //チャート関連()
        //[NonSerialized]
        //public Chart chart1;
        //[NonSerialized]
        //ChartArea chartarea1;
        //[NonSerialized]
        //Series series1;

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
            PileBodyRefs = new ObservableCollection<string>(new string[] {"(PB1)", "(PB2)", "(PB3)", "(PB4)", "(PB5)"});
            SelectedPileTopTypes = new ObservableCollection<string>(new string[5]);
            SelectedPileBodyTypes = new ObservableCollection<string>(new string[5]);
            SelectedPileConstructionTypes = new ObservableCollection<string>(new string[5]);
            PileToeDias = new ObservableCollection<double>(new double[5]);
            TipNonPermabilities = new ObservableCollection<double>(new double[5]);
            EmbedmentIntoBearingSoils = new ObservableCollection<double>(new double[5]);
            PileInnerDias = new ObservableCollection<double>(new double[5]);
            SelectedTipStyles = new ObservableCollection<string>(new string[5]);
            SettlePileToeDias = new ObservableCollection<double>(new double[5]);
            SettleAlphas = new ObservableCollection<double>(new double[5]);
            SettleNs = new ObservableCollection<double>(new double[5]);
            for (int i = 0; i < 5; i++)
            {
                DataGridPileBody.Add(new ObservableCollection<PileBodyDataItem>());
            }

            //チャート初期化
            ChartInitialize();

            // データを読み込む
            LoadPresetSettlementParameters();
        }

        // CSVからデータを読み込む
        private void LoadPresetSettlementParameters()
        {
            string csvFilePath = "../../PileLibrary/PresetSettlementParameterSet.csv";
            using (StreamReader reader = new StreamReader(csvFilePath, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 4)
                    {
                        string name = parts[0].Trim();
                        string soilType = parts[1].Trim();
                        double alpha = double.Parse(parts[2].Trim());
                        double n = double.Parse(parts[3].Trim());

                        PileTipSettlementPresetParameters.Add(new PileTipSettlementPresetParameter(name, soilType, alpha, n));
                        PileTipSettlementPresetParameterNames.Add(name + "-" + soilType);
                    }
                }
            }
        }

        //チャート初期化
        public void ChartInitialize()
        {

            ////チャート関連

            //chart1 = new Chart();
            //// Chart コントロールの Y軸の上下逆
            ////title = new Title("杭の先端抵抗-先端変位関係");
            //chartarea1 = chart1.ChartAreas.Add("Area1");

            //chart1.ChartAreas[0].AxisY.IsReversed = true;
            //chart1.ChartAreas[0].AxisX.Minimum = 0.0;
            //chart1.ChartAreas[0].AxisX.Maximum = 1.0;
            //chart1.ChartAreas[0].AxisY.Minimum = 0.0;
            //chart1.ChartAreas[0].AxisY.Maximum = 0.1;
            //series1 = new Series();

            ////ChartAreaの設定(グラフタイトル、軸ラベル)
            //chartarea1.AxisX.Title = "(Rp/Ap)/(Rp/Ap)u";
            //chartarea1.AxisY.Title = "Sp/dp";

            ////Seriesの初期設定(グラフの種類、線の太さ、凡例)
            //series1.ChartType = SeriesChartType.Line;
            //series1.BorderWidth = 1;
            //series1.Color = NikkenDrawingColors.SkyBlue;
            //series1.LegendText = "Number:1";

            ////ChartにTitle,Seriesを追加
            //chart1.Series.Add(series1);
        }

        //杭先端沈下チャート要素クリアコマンド
        public void ChartClearCmd()
        {
            //series1.Points.Clear();
        }

        //杭先端沈下チャート要素追加コマンド
        public void AddComponent(double alpha, double n)
        {
            //    //グラフ要素を追加する
            //    for (double RponApRatio = 0; RponApRatio <= 1 + 0.01; RponApRatio += 0.01)
            //    {
            //        double SponDp = 0.1 * (alpha * RponApRatio + (1 - alpha) * Math.Pow(RponApRatio, n));
            //        series1.Points.AddXY(RponApRatio, SponDp);
            //    }
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


