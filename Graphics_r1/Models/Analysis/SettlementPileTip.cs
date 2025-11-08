//using PileDesign.Models.InputData;

//namespace PileDesign.Models.Analysis
//{
//    public class SettlementPileTip : BaseModel
//    {
//        SoilPile SoilPile { get; set; }

//        // 沈下量算定用先端径
//        public double _dp;
//        public double Dp
//        {
//            get => _dp;
//            set => SetProperty(ref _dp, value);
//        }

//        // 極限先端支持力
//        public double _rpu;
//        public double Rpu
//        {
//            get => _rpu;
//            set => SetProperty(ref _rpu, value);
//        }

//        // 初期接線勾配
//        private string _alpha;
//        public string Alpha
//        {
//            get => _alpha;
//            set => SetProperty(ref _alpha, value);
//        }

//        // 次数
//        private string _n;
//        public string N
//        {
//            get => _n;
//            set => SetProperty(ref _n, value);
//        }

//        // 耐力算定用先端径
//        public double _d;
//        public double D
//        {
//            get => _d;
//            set => SetProperty(ref _d, value);
//        }

//        // 杭種
//        private string _pileConstructionType;
//        public string PileConstructionType
//        {
//            get => _pileConstructionType;
//            set => SetProperty(ref _pileConstructionType, value);
//        }

//        // 土質
//        private string _selectedLayerClass;
//        public string SelectedLayerClass
//        {
//            get => _selectedLayerClass;
//            set => SetProperty(ref _selectedLayerClass, value);
//        }

//        // 平均N値
//        private double _nValue; // N値
//        public double NValue
//        {
//            get => _nValue;
//            set => SetProperty(ref _nValue, value);
//        }

//        // せん断強さ
//        private double _cohesive; // 粘着力
//        public double Cohesive
//        {
//            get => _cohesive;
//            set => SetProperty(ref _cohesive, value);
//        }

//        // η
//        private double _eta;
//        public double Eta
//        {
//            get => _eta;
//            set => SetProperty(ref _eta, value);
//        }

//        // 極限支持力度
//        private double _qp;
//        public double Qp
//        {
//            get => _qp;
//            set => SetProperty(ref _qp, value);
//        }

//        // コンストラクタ
//        public SettlementPileTip(SoilPile soilPile)
//        {
//            SoilPile = soilPile;
//        }
//    }
//}