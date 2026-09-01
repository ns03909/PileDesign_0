using PileDesign.ViewModels;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class FundamentalInput : BaseViewModel
    {
        // プロジェクト番号
        private string _projectNo;
        public string ProjectNo
        {
            get => _projectNo;
            set => SetProperty(ref _projectNo, value);
        }

        // プロジェクト名
        private string _projectName;
        public string ProjectName
        {
            get => _projectName;
            set => SetProperty(ref _projectName, value);
        }

        // 参照レベル（docx 出力ラベル専用、例: "TP"）
        private string _refLevel;
        public string RefLevel
        {
            get => _refLevel;
            set => SetProperty(ref _refLevel, value);
        }

        // Z=0 が絶対標高で何 m に相当するか（docx 出力でのみ使用）
        private double _referenceAltitude;
        public double ReferenceAltitude
        {
            get => _referenceAltitude;
            set => SetProperty(ref _referenceAltitude, value);
        }

        public double ToAbsolute(double z) => z + ReferenceAltitude;

        // 耐震グレード
        private string _seismicGrade;
        public string SeismicGrade
        {
            get => _seismicGrade;
            set => SetProperty(ref _seismicGrade, value);
        }

        // バイリニア型コンクリートの引張側の降伏応力度を 0 とする（コンクリート引張を無視）
        private bool _ignoreConcreteTensileStrength;
        public bool IgnoreConcreteTensileStrength
        {
            get => _ignoreConcreteTensileStrength;
            set => SetProperty(ref _ignoreConcreteTensileStrength, value);
        }

        // バイリニア型コンクリートの圧縮側の降伏応力度を 0.85·Gsi·Fc とする（既定の Gsi·Fc から低減）
        private bool _useReducedConcreteCompressiveStrength;
        public bool UseReducedConcreteCompressiveStrength
        {
            get => _useReducedConcreteCompressiveStrength;
            set => SetProperty(ref _useReducedConcreteCompressiveStrength, value);
        }

        // 鉄筋（場所打ち RC / 場所打ち鋼管コンクリート杭）を 1.1×F で降伏する完全バイリニア型とする
        private bool _rebarYieldAt11F;
        public bool RebarYieldAt11F
        {
            get => _rebarYieldAt11F;
            set => SetProperty(ref _rebarYieldAt11F, value);
        }

        // 鋼管（場所打ち鋼管コンクリート杭）を 1.1×F で降伏する完全バイリニア型とする
        private bool _steelPipeYieldAt11F;
        public bool SteelPipeYieldAt11F
        {
            get => _steelPipeYieldAt11F;
            set => SetProperty(ref _steelPipeYieldAt11F, value);
        }

        // 鋼材のヤング係数に「基礎部材の強度と変形性能」の値を用いる
        // (既定 false = 製品カタログの値。カタログはメーカーで 200,000 / 205,000 に割れる)
        private bool _useGuideYoungsModulus;
        public bool UseGuideYoungsModulus
        {
            get => _useGuideYoungsModulus;
            set => SetProperty(ref _useGuideYoungsModulus, value);
        }

        // コンクリートのヤング係数 Ec の算定で ξ(=Gsi) を 1.0 として計算する
        private bool _useUnitGsiForConcreteE;
        public bool UseUnitGsiForConcreteE
        {
            get => _useUnitGsiForConcreteE;
            set => SetProperty(ref _useUnitGsiForConcreteE, value);
        }

        // 場所打ち系コンクリートの使用限界・損傷限界の許容圧縮応力度を告示1113(第8)による
        private bool _useNotification1113Compression;
        public bool UseNotification1113Compression
        {
            get => _useNotification1113Compression;
            set => SetProperty(ref _useNotification1113Compression, value);
        }

        // 場所打ちRC杭の安全限界曲げ強度をe関数法で算定する（指針(案)5.4.1準拠。検定の耐力側のみ）
        private bool _useInsituUltimateEFunction;
        public bool UseInsituUltimateEFunction
        {
            get => _useInsituUltimateEFunction;
            set => SetProperty(ref _useInsituUltimateEFunction, value);
        }

        // 場所打ちRC杭の解析用 M-φ 関係をファイバーモデル（断面分割積分）で算定する
        private bool _useFiberMPhi;
        public bool UseFiberMPhi
        {
            get => _useFiberMPhi;
            set => SetProperty(ref _useFiberMPhi, value);
        }

        // 場所打ちRC杭のコンクリート許容せん断応力度を告示1113(第8)による
        private bool _useNotification1113Shear;
        public bool UseNotification1113Shear
        {
            get => _useNotification1113Shear;
            set => SetProperty(ref _useNotification1113Shear, value);
        }

        // 告示1113(第8) 長期許容応力度の区分（圧縮・せん断で共用。圧縮 1: Fc/4、2: min(Fc/4.5, 6)／せん断 1: Fc/40、2: Fc/45）
        private int _notification1113CompressionCase = 1;
        public int Notification1113CompressionCase
        {
            get => _notification1113CompressionCase;
            set => SetProperty(ref _notification1113CompressionCase, value);
        }

        // 【評定範囲外】場所打ち鋼管コンクリート杭の終局圧縮縁ひずみを 5,000μ とする（既定 3,000μ）
        private bool _useUltimateStrain5000ForSteelPipeConcrete;
        public bool UseUltimateStrain5000ForSteelPipeConcrete
        {
            get => _useUltimateStrain5000ForSteelPipeConcrete;
            set => SetProperty(ref _useUltimateStrain5000ForSteelPipeConcrete, value);
        }

        // 【評定範囲外】場所打ち鋼管コンクリート杭の許容時の判定を、コンクリートと鋼管のみで行う
        private bool _excludeRebarFromAllowableLimitForSteelPipeConcrete;
        public bool ExcludeRebarFromAllowableLimitForSteelPipeConcrete
        {
            get => _excludeRebarFromAllowableLimitForSteelPipeConcrete;
            set => SetProperty(ref _excludeRebarFromAllowableLimitForSteelPipeConcrete, value);
        }

        // 場所打ち鋼管コンクリート杭の許容時 N-M を断面分割積分で求める。
        // 既定 true（従来どおり）。false で評定書 5.(3) の単純累加式になる。
        // 旧い保存ファイルにはキーが無いので、この初期値がそのまま効く（挙動を変えないため true）。
        private bool _useFiberNMForSteelPipeConcrete = true;
        public bool UseFiberNMForSteelPipeConcrete
        {
            get => _useFiberNMForSteelPipeConcrete;
            set => SetProperty(ref _useFiberNMForSteelPipeConcrete, value);
        }

        //
        private double _x0;
        public double X0
        {
            get => _x0;
            set => SetProperty(ref _x0, value);
        }

        private double _y0;
        public double Y0
        {
            get => _y0;
            set => SetProperty(ref _y0, value);
        }

        private double _z0;
        public double Z0
        {
            get => _z0;
            set => SetProperty(ref _z0, value);
        }

        // 参考軸中心
        public Point3D Point3D0 => new() { X = X0, Y = Y0, Z = Z0 };


        // コンストラクタ
        public FundamentalInput()
        {
            RefLevel = "TP";
            ReferenceAltitude = 0.0;
            ProjectNo = "J240000-#";
            ProjectName = "プロジェクト名";
            SeismicGrade = "A";
        }

        // 浅いコピーを作成するメソッド
        public FundamentalInput ShallowCopy()
        {
            return (FundamentalInput)this.MemberwiseClone();
        }
    }
}
