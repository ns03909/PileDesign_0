using Newtonsoft.Json;
using PileDesign.ViewModels;
using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    public class GroundMassDataInput : BaseDataItem
    {
        // 入力データのエラーチェック (行レベル、互換性のため残置)
        private bool _isError;
        public bool IsError
        {
            get => _isError;
            set
            {
                if (_isError != value)
                {
                    _isError = value;
                    OnPropertyChanged(nameof(IsError));
                }
            }
        }

        // 参考NPT深度GL (GLDepth 列) の順序エラー
        private bool _isErrorGLDepth;
        public bool IsErrorGLDepth
        {
            get => _isErrorGLDepth;
            set => SetProperty(ref _isErrorGLDepth, value);
        }

        // 下端深度GL (LayerBottomDepth 列) の順序エラー
        private bool _isErrorLayerBottomDepth;
        public bool IsErrorLayerBottomDepth
        {
            get => _isErrorLayerBottomDepth;
            set => SetProperty(ref _isErrorLayerBottomDepth, value);
        }

        // 番号
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        // 深さ
        private double _glDepth;
        public double GLDepth
        {
            get => _glDepth;
            set => SetProperty(ref _glDepth, value);
        }

        // Z深さ
        private double _altitudeDepth;
        public double AltitudeDepth
        {
            get => _altitudeDepth;
            set => SetProperty(ref _altitudeDepth, value);
        }

        // 間隔
        private double _spacing = 1.0;
        public double Spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }

        // 土質点厚
        private double? _h;
        public double? H
        {
            get => _h;
            set => SetProperty(ref _h, value);
        }

        // 工学的基盤か否か
        private bool _isEngineeringBedrock;
        public bool IsEngineeringBedrock
        {
            get => _isEngineeringBedrock;
            set => SetProperty(ref _isEngineeringBedrock, value);
        }

        // 土層番号
        private int? _layerNo;
        public int? LayerNo
        {
            get => _layerNo;
            set => SetProperty(ref _layerNo, value);
        }

        // 名称
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        // N値 15;
        private double _nValue = 15;
        public double NValue
        {
            get => _nValue;
            set => SetProperty(ref _nValue, value);
        }

        // 年代分類
        private string _ageCategory;
        public string AgeCategory
        {
            get => _ageCategory;
            set => SetProperty(ref _ageCategory, value);
        }

        //土層分類
        private string _granularityClass;
        public string GranularityClass
        {
            get => _granularityClass;
            set => SetProperty(ref _granularityClass, value);
        }

        // 細粒分含有率 35.0;
        private double _fc = 35.0;
        public double Fc
        {
            get => _fc;
            set => SetProperty(ref _fc, value);
        }

        // 全応力
        private double _sigmaZ;
        public double SigmaZ
        {
            get => _sigmaZ;
            set => SetProperty(ref _sigmaZ, value);
        }

        // 有効応力
        private double _sigmaZPrime;
        public double SigmaZPrime
        {
            get => _sigmaZPrime;
            set => SetProperty(ref _sigmaZPrime, value);
        }

        // 液状化対象層か否か
        private bool _isLiquefactionLayer;
        public bool IsLiquefactionLayer
        {
            get => _isLiquefactionLayer;
            set => SetProperty(ref _isLiquefactionLayer, value);
        }

        // 低減係数
        private double? _rd;
        public double? RD
        {
            get => _rd;
            set => SetProperty(ref _rd, value);
        }

        // 換算N値
        private double? _n1;
        public double? N1
        {
            get => _n1;
            set => SetProperty(ref _n1, value);
        }

        // N値増分
        private double? _deltaNf;
        public double? DeltaNf
        {
            get => _deltaNf;
            set => SetProperty(ref _deltaNf, value);
        }

        // 補正N値
        private double? _nL;
        public double? NL
        {
            get => _nL;
            set => SetProperty(ref _nL, value);
        }

        // 液状化抵抗比
        private double? _tauLonSigmaZPrime;
        public double? TauLonSigmaZPrime
        {
            get => _tauLonSigmaZPrime;
            set => SetProperty(ref _tauLonSigmaZPrime, value);
        }

        // 繰り返しせん断応力比
        private ObservableCollection<double?> _tauDonSigmaZPrime;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double?> TauDonSigmaZPrime
        {
            get => _tauDonSigmaZPrime;
            set => SetProperty(ref _tauDonSigmaZPrime, value);
        }

        // 液状化安全率;
        private ObservableCollection<double?> _fL;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double?> FL
        {
            get => _fL;
            set => SetProperty(ref _fL, value);
        }

        // 低減率
        private ObservableCollection<double?> _betaL;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double?> BetaL
        {
            get => _betaL;
            set => SetProperty(ref _betaL, value);
        }

        // 繰り返しせん断ひずみ
        private ObservableCollection<double?> _gammaCy;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double?> GammaCy
        {
            get => _gammaCy;
            set => SetProperty(ref _gammaCy, value);
        }

        // 液状化水平変位
        private ObservableCollection<double> _sigmaGammaCyH;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> SigmaGammaCyH
        {
            get => _sigmaGammaCyH;
            set => SetProperty(ref _sigmaGammaCyH, value);
        }

        // 単位体積重量
        private double _density;
        public double Density
        {
            get => _density;
            set => SetProperty(ref _density, value);
        }

        // 質量
        private double _mass;
        public double Mass
        {
            get => _mass;
            set => SetProperty(ref _mass, value);
        }

        // 初期S波速度
        private double _vs0;
        public double VS0
        {
            get => _vs0;
            set => SetProperty(ref _vs0, value);
        }

        // Hardin-Drnevich モデル: 基準せん断ひずみ γ₀.₅ (G/G0=0.5 となる γ)
        // 応答スペクトル法選択時のみ参照される。a1/a2 では使われない。
        private double _gamma05;
        public double Gamma05
        {
            get => _gamma05;
            set => SetProperty(ref _gamma05, value);
        }

        // Hardin-Drnevich モデル: 履歴減衰の上限 h_max
        // 応答スペクトル法選択時のみ参照される。a1/a2 では使われない。
        private double _hMax;
        public double HMax
        {
            get => _hMax;
            set => SetProperty(ref _hMax, value);
        }

        // 等価S波速度
        private ObservableCollection<double> _vSE;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> VSE
        {
            get => _vSE;
            set => SetProperty(ref _vSE, value);
        }

        // 等価せん断ばね剛性
        private ObservableCollection<double> _k;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> K
        {
            get => _k;
            set => SetProperty(ref _k, value);
        }

        // 仮の無次元化水平変位
        private ObservableCollection<double> _u;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> U
        {
            get => _u;
            set => SetProperty(ref _u, value);
        }

        // 調整後無次元化水平変位
        private ObservableCollection<double> _uStar;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> UStar
        {
            get => _uStar;
            set => SetProperty(ref _uStar, value);
        }

        // 水平変位
        private ObservableCollection<double> _dmaxUStar;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> DmaxUStar
        {
            get => _dmaxUStar;
            set => SetProperty(ref _dmaxUStar, value);
        }

        // 層下面Z（層厚の累積から算出）
        private double? _layerBottomZ;
        public double? LayerBottomZ
        {
            get => _layerBottomZ;
            set => SetProperty(ref _layerBottomZ, value);
        }

        // 層下面深度（GroundTopAltitude を基準とした下向き正の深さ。入力可。H と LayerBottomZ と同期）
        private double? _layerBottomDepth;
        public double? LayerBottomDepth
        {
            get => _layerBottomDepth;
            set => SetProperty(ref _layerBottomDepth, value);
        }

        // 水平変位
        private ObservableCollection<double> _dmaxUStarSigmaGammaCyH;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<double> DmaxUStarSigmaGammaCyH
        {
            get => _dmaxUStarSigmaGammaCyH;
            set => SetProperty(ref _dmaxUStarSigmaGammaCyH, value);
        }

        // コンストラクタ
        public GroundMassDataInput()
        {
            GLDepth = -1.0;
            //AltitudeDepth = 0.0;
            //Spacing = 1.0;
            //H = 0.0;
            IsEngineeringBedrock = false;
            Name = "";
            NValue = 15.0;
            Fc = 0.0;
            SigmaZ = 0.0;
            SigmaZPrime = 0.0;
            IsLiquefactionLayer = false;
            RD = 0.0;
            N1 = 0.0;
            DeltaNf = 0.0;
            NL = 0.0;
            TauLonSigmaZPrime = 0.0;
            TauDonSigmaZPrime = [0.0, 0.0];
            FL = [0.0, 0.0];
            BetaL = [0.0, 0.0];
            GammaCy = [0.0, 0.0];
            SigmaGammaCyH = [0.0, 0.0];
            Density = 0.0;
            Mass = 0.0;
            VS0 = 250.0;
            Gamma05 = 7e-4;  // 砂質土相当のデフォルト (応答スペクトル法用)
            HMax = 0.20;     // h_max デフォルト
            VSE = [0.0, 0.0];
            K = [0.0, 0.0];
            U = [0.0, 0.0];
            UStar = [0.0, 0.0];
            DmaxUStar = [0.0, 0.0];
            DmaxUStarSigmaGammaCyH = [0.0, 0.0];
        }

        // 深いコピーを作成するメソッド
        public GroundMassDataInput DeepCopy()
        {
            var copy = (GroundMassDataInput)this.MemberwiseClone();
            copy.TauDonSigmaZPrime = new ObservableCollection<double?>(this.TauDonSigmaZPrime);
            copy.FL = new ObservableCollection<double?>(this.FL);
            copy.BetaL = new ObservableCollection<double?>(this.BetaL);
            copy.GammaCy = new ObservableCollection<double?>(this.GammaCy);
            copy.SigmaGammaCyH = new ObservableCollection<double>(this.SigmaGammaCyH);
            copy.VSE = new ObservableCollection<double>(this.VSE);
            copy.K = new ObservableCollection<double>(this.K);
            copy.U = new ObservableCollection<double>(this.U);
            copy.UStar = new ObservableCollection<double>(this.UStar);
            copy.DmaxUStar = new ObservableCollection<double>(this.DmaxUStar);
            copy.DmaxUStarSigmaGammaCyH = new ObservableCollection<double>(this.DmaxUStarSigmaGammaCyH);
            return copy;
        }
    }
}
