using System.Collections.ObjectModel;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 基礎梁入力データ
    /// </summary>
    public class FoundationBeamInput : BaseModel
    {
        private FoundationBeamConnectionMode _connectionMode = FoundationBeamConnectionMode.RigidBody;
        public FoundationBeamConnectionMode ConnectionMode
        {
            get => _connectionMode;
            set => SetProperty(ref _connectionMode, value);
        }

        private ObservableCollection<FoundationNode> _nodes = [];
        public ObservableCollection<FoundationNode> Nodes
        {
            get => _nodes;
            set => SetProperty(ref _nodes, value);
        }

        private ObservableCollection<FoundationBeamElement> _beams = [];
        public ObservableCollection<FoundationBeamElement> Beams
        {
            get => _beams;
            set => SetProperty(ref _beams, value);
        }

        public FoundationBeamInput()
        {
            Nodes = [];
            Beams = [];
        }
    }

    /// <summary>
    /// 基礎梁接続モード
    /// </summary>
    public enum FoundationBeamConnectionMode
    {
        /// <summary>全て剛体連結（現状モデル）</summary>
        RigidBody,

        /// <summary>全て基礎梁で接続</summary>
        FoundationBeam,

        /// <summary>混在（杭単位で指定）</summary>
        Mixed
    }

    /// <summary>
    /// 基礎梁節点
    /// </summary>
    public class FoundationNode : BaseModel
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
    }

    /// <summary>
    /// 基礎梁要素
    /// </summary>
    public class FoundationBeamElement : BaseModel
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private int _nodeI_No;
        public int NodeI_No
        {
            get => _nodeI_No;
            set => SetProperty(ref _nodeI_No, value);
        }

        private int _nodeJ_No;
        public int NodeJ_No
        {
            get => _nodeJ_No;
            set => SetProperty(ref _nodeJ_No, value);
        }

        private string _sectionName = "";
        public string SectionName
        {
            get => _sectionName;
            set => SetProperty(ref _sectionName, value);
        }

        // 断面諸元
        private double _width = 0.5;  // デフォルト 0.5m
        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        private double _height = 0.8;  // デフォルト 0.8m
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        private double _youngModulus = 2.5e7;  // デフォルト 2.5×10^7 kN/m² (コンクリート)
        public double YoungModulus
        {
            get => _youngModulus;
            set => SetProperty(ref _youngModulus, value);
        }

        private double _shearModulus = 1.04e7;  // デフォルト 1.04×10^7 kN/m² (E/2.4, ポアソン比0.2)
        public double ShearModulus
        {
            get => _shearModulus;
            set => SetProperty(ref _shearModulus, value);
        }
    }
}
