namespace PileDesign.Models.InputData
{
    public class PileZDataItem : ZDataItem
    {

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

        public PileZDataItem DeepCopy()
        {
            return new PileZDataItem
            {
                Z = this.Z,
                //GroundLayerNo = this.GroundLayerNo,
                GroundInput = this.GroundInput,
                GroundDisp1 = this.GroundDisp1,
                GroundDisp2 = this.GroundDisp2,
                GroundDisp1L = this.GroundDisp1L,
                GroundDisp2L = this.GroundDisp2L,
                IsChangeable = this.IsChangeable,
                B = this.B,
                L = this.L,
                EAC = this.EAC,
                EAT = this.EAT,
                W = this.W,
                NValue = this.NValue,
                Cohesive = this.Cohesive,
            };
        }
    }
}