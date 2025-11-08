using PileDesign.Models.InputData;
using PileDesign.ViewModels;

namespace PileDesign.Models.Analysis
{
    public class SettlementPileCircumDataItem : BaseDataItem
    {
        SoilPile SoilPile { get; set; }

        // 杭要素径
        private double _b;
        public double B
        {
            get => _b;
            set => SetProperty(ref _b, value);
        }

        // 杭区間長
        private double _l;
        public double L
        {
            get => _l;
            set => SetProperty(ref _l, value);
        }

        // 杭圧縮時杭体剛性
        private double _eAC;
        public double EAC
        {
            get => _eAC;
            set => SetProperty(ref _eAC, value);
        }

        // 杭引張時杭体剛性
        private double _eAT;
        public double EAT
        {
            get => _eAT;
            set => SetProperty(ref _eAT, value);
        }

        // 杭の単位長さ重量
        private double _w;
        public double W
        {
            get => _w;
            set => SetProperty(ref _w, value);
        }

        // 土層分類
        private string _selectedLayerClass;
        public string SelectedLayerClass
        {
            get => _selectedLayerClass;
            set => SetProperty(ref _selectedLayerClass, value);
        }

        // N値
        private double _nValue; // N値
        public double NValue
        {
            get => _nValue;
            set => SetProperty(ref _nValue, value);
        }

        // せん断強さ
        private double _cohesive = 0.0; // 粘着力
        public double Cohesive
        {
            get => _cohesive;
            set => SetProperty(ref _cohesive, value);
        }

        // 押込み方向周面抵抗
        private bool _isPositiveCircumResistance;
        public bool IsPositiveCircumResistance
        {
            get => _isPositiveCircumResistance;
            set => SetProperty(ref _isPositiveCircumResistance, value);
        }

        // 引抜き方向周面抵抗
        private bool _isNegativeCircumResistance;
        public bool IsNegativeCircumResistance
        {
            get => _isNegativeCircumResistance;
            set => SetProperty(ref _isNegativeCircumResistance, value);
        }

        // 第1折点変位
        private double _s1;
        public double S1
        {
            get => _s1;
            set => SetProperty(ref _s1, value);
        }

        // 第2折点変位
        private double _s2;
        public double S2
        {
            get => _s2;
            set => SetProperty(ref _s2, value);
        }

        // 第1折点周面抵抗
        private double _tau1;
        public double Tau1
        {
            get => _tau1;
            set => SetProperty(ref _tau1, value);
        }

        // 第2折点周面抵抗
        private double _tau2;
        public double Tau2
        {
            get => _tau2;
            set => SetProperty(ref _tau2, value);
        }

        // コンストラクタ
        public SettlementPileCircumDataItem(SoilPile soilPile, double z)
        {
            SoilPile = soilPile;

        }
    }
}
