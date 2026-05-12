using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 基礎梁入力データ
    /// </summary>
    public class FoundationBeamInput : BaseModel
    {
        private FoundationBeamConnectionMode _connectionMode = FoundationBeamConnectionMode.RigidFloor;
        public FoundationBeamConnectionMode ConnectionMode
        {
            get => _connectionMode;
            set => SetProperty(ref _connectionMode, value);
        }

        private ObservableCollection<BeamMaterial> _materials = [];
        public ObservableCollection<BeamMaterial> Materials
        {
            get => _materials;
            set
            {
                if (_materials != null) _materials.CollectionChanged -= OnMaterialsChanged;
                SetProperty(ref _materials, value);
                if (_materials != null) _materials.CollectionChanged += OnMaterialsChanged;
                RefreshMaterialNoOptions();
            }
        }

        private ObservableCollection<BeamSection> _sections = [];
        public ObservableCollection<BeamSection> Sections
        {
            get => _sections;
            set
            {
                if (_sections != null) _sections.CollectionChanged -= OnSectionsChanged;
                SetProperty(ref _sections, value);
                if (_sections != null) _sections.CollectionChanged += OnSectionsChanged;
                RefreshSectionNoOptions();
            }
        }

        // ComboBox 用 No 一覧 (1..Materials.Count / 1..Sections.Count)。
        // Materials/Sections の追加・削除に追従する。永続化対象外なので JSON は無視。
        [JsonIgnore]
        public ObservableCollection<int> MaterialNoOptions { get; } = [];

        [JsonIgnore]
        public ObservableCollection<int> SectionNoOptions { get; } = [];

        private void OnMaterialsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshMaterialNoOptions();
        private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSectionNoOptions();

        private void RefreshMaterialNoOptions()
        {
            MaterialNoOptions.Clear();
            for (int i = 1; i <= Materials.Count; i++) MaterialNoOptions.Add(i);
        }

        private void RefreshSectionNoOptions()
        {
            SectionNoOptions.Clear();
            for (int i = 1; i <= Sections.Count; i++) SectionNoOptions.Add(i);
        }

        private ObservableCollection<FoundationNode> _nodes = [];
        public ObservableCollection<FoundationNode> Nodes
        {
            get => _nodes;
            set => SetProperty(ref _nodes, value);
        }

        private ObservableCollection<FoundationBeam> _beams = [];
        public ObservableCollection<FoundationBeam> Beams
        {
            get => _beams;
            set => SetProperty(ref _beams, value);
        }

        // 位置ベースの No 取得 (1-based)。No プロパティ廃止に伴い、表示・識別用にこちらを使う。
        public int GetMaterialNo(BeamMaterial m) => m == null ? 0 : Materials.IndexOf(m) + 1;
        public int GetSectionNo(BeamSection s) => s == null ? 0 : Sections.IndexOf(s) + 1;
        public int GetBeamNo(FoundationBeam b) => b == null ? 0 : Beams.IndexOf(b) + 1;

        /// <summary>
        /// Materials / Sections が空のとき、No.1 のデフォルトエントリを自動追加する。
        /// 自動生成梁は MaterialNo=1 / SectionNo=1 を参照するが、ロード元（設計例 JSON 等）に
        /// 材料・断面データが含まれない場合に参照先を保証するために使う。
        /// 既にエントリが 1 件以上ある場合は何もしない。
        /// </summary>
        public void EnsureDefaultMaterialAndSection()
        {
            if (Materials.Count == 0)
            {
                Materials.Add(new BeamMaterial
                {
                    Name = "FC30 (自動)",
                });
            }

            if (Sections.Count == 0)
            {
                var section = new BeamSection
                {
                    Name = "800×2000 (自動)",
                    Width = 0.8,
                    Height = 2.0,
                };
                section.RecalculateProperties();
                Sections.Add(section);
            }
        }

        public FoundationBeamInput()
        {
            Materials = [];  // setter が CollectionChanged 購読＋ Options 同期を行う
            Sections = [];
            Nodes = [];
            Beams = [];
        }
    }

    /// <summary>
    /// 基礎梁接続モード
    /// </summary>
    public enum FoundationBeamConnectionMode
    {
        /// <summary>剛体連結（全杭を代表点に全6DOFで剛体連結）</summary>
        RigidBody,

        /// <summary>剛床連結（Ux,Uy,Rz拘束、Uz,Rx,Ry自由。基礎梁が剛性を負担）</summary>
        RigidFloor
    }

    /// <summary>
    /// 節点参照タイプ
    /// </summary>
    public enum NodeReferenceType
    {
        /// <summary>専用節点（FoundationNode）</summary>
        FoundationNode = 0,

        /// <summary>一般節点（InputNode）</summary>
        GeneralNode = 1,

        /// <summary>杭配置（杭頭+ΔZc位置）</summary>
        PileLayout = 2
    }

    /// <summary>
    /// 節点参照情報（Type + Guid のペア）
    /// </summary>
    public class NodeReference
    {
        public NodeReferenceType Type { get; set; }
        public Guid Id { get; set; }

        public NodeReference(NodeReferenceType type, Guid id)
        {
            Type = type;
            Id = id;
        }
    }

    /// <summary>
    /// ComboBox 用の節点参照オプション (Type + Id + 表示用 Display)。
    /// SelectedValuePath="Key" + SelectedValue で マッチングするため Key プロパティを持つ。
    /// </summary>
    public sealed class NodeReferenceOption : IEquatable<NodeReferenceOption>
    {
        public NodeReferenceType Type { get; init; }
        public Guid Id { get; init; }
        public string Display { get; init; } = "";

        /// <summary>WPF ComboBox SelectedValuePath 用のユニークキー。Type と Id の組合せ。</summary>
        public string Key => $"{(int)Type}:{Id}";

        public override string ToString() => Display;
        public bool Equals(NodeReferenceOption? other)
            => other != null && Type == other.Type && Id == other.Id;
        public override bool Equals(object? obj) => Equals(obj as NodeReferenceOption);
        public override int GetHashCode() => System.HashCode.Combine(Type, Id);
        public static bool operator ==(NodeReferenceOption? a, NodeReferenceOption? b)
            => a is null ? b is null : a.Equals(b);
        public static bool operator !=(NodeReferenceOption? a, NodeReferenceOption? b) => !(a == b);
    }

    /// <summary>
    /// 基礎梁節点
    /// </summary>
    public class FoundationNode : BaseModel
    {
        // ユニークID（削除・挿入に影響されない内部識別子）
        private Guid _id = Guid.NewGuid();
        public Guid Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

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

        // 選択状態
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // 表示状態（アクティブ/非アクティブ）
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
    }

    /// <summary>
    /// 基礎梁
    /// </summary>
    public class FoundationBeam : BaseModel
    {
        // No プロパティは廃止。番号は所属コレクション (Beams) 内の位置 (1-based) として扱う。
        // 必要なときは FoundationBeamInput.GetBeamNo(this) で取得する。

        // 節点I参照（新方式）
        private NodeReferenceType _nodeI_Type = NodeReferenceType.FoundationNode;
        public NodeReferenceType NodeI_Type
        {
            get => _nodeI_Type;
            set
            {
                if (SetProperty(ref _nodeI_Type, value))
                    OnPropertyChanged(nameof(NodeI_Key));
            }
        }

        private Guid _nodeI_Id = Guid.Empty;
        public Guid NodeI_Id
        {
            get => _nodeI_Id;
            set
            {
                if (SetProperty(ref _nodeI_Id, value))
                    OnPropertyChanged(nameof(NodeI_Key));
            }
        }

        // 節点J参照（新方式）
        private NodeReferenceType _nodeJ_Type = NodeReferenceType.FoundationNode;
        public NodeReferenceType NodeJ_Type
        {
            get => _nodeJ_Type;
            set
            {
                if (SetProperty(ref _nodeJ_Type, value))
                    OnPropertyChanged(nameof(NodeJ_Key));
            }
        }

        private Guid _nodeJ_Id = Guid.Empty;
        public Guid NodeJ_Id
        {
            get => _nodeJ_Id;
            set
            {
                if (SetProperty(ref _nodeJ_Id, value))
                    OnPropertyChanged(nameof(NodeJ_Key));
            }
        }

        /// <summary>
        /// 節点I の ComboBox SelectedValue 用キー (Type + Id の文字列表現)。
        /// NodeReferenceOption.Key と一致するため、SelectedValuePath="Key" で値ベースのマッチングが可能。
        /// setter は "{type}:{guid}" を解析して Type / Id を更新する。
        /// </summary>
        [JsonIgnore]
        public string NodeI_Key
        {
            get => $"{(int)NodeI_Type}:{NodeI_Id}";
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                int colon = value.IndexOf(':');
                if (colon <= 0) return;
                if (!int.TryParse(value.AsSpan(0, colon), out int typeInt)) return;
                if (!Guid.TryParse(value.AsSpan(colon + 1), out Guid id)) return;
                NodeI_Type = (NodeReferenceType)typeInt;
                NodeI_Id = id;
            }
        }

        /// <summary>節点J の ComboBox SelectedValue 用キー (NodeI_Key と同じ仕組み)。</summary>
        [JsonIgnore]
        public string NodeJ_Key
        {
            get => $"{(int)NodeJ_Type}:{NodeJ_Id}";
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                int colon = value.IndexOf(':');
                if (colon <= 0) return;
                if (!int.TryParse(value.AsSpan(0, colon), out int typeInt)) return;
                if (!Guid.TryParse(value.AsSpan(colon + 1), out Guid id)) return;
                NodeJ_Type = (NodeReferenceType)typeInt;
                NodeJ_Id = id;
            }
        }

        // 材料・断面参照（新規）
        private int _materialNo = 1;  // 材料番号（デフォルト1）
        public int MaterialNo
        {
            get => _materialNo;
            set => SetProperty(ref _materialNo, value);
        }

        private int _sectionNo = 1;  // 断面番号（デフォルト1）
        public int SectionNo
        {
            get => _sectionNo;
            set => SetProperty(ref _sectionNo, value);
        }

        private string _sectionName = "";  // 後方互換性のため残す
        public string SectionName
        {
            get => _sectionName;
            set => SetProperty(ref _sectionName, value);
        }

        // 断面諸元（後方互換性のため残す）
        private double _width = 0.8;  // デフォルト 0.8m
        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        private double _height = 2.0;  // デフォルト 2.0m
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

        // 要素座標系の向き β (度)
        private double _angleBeta = 0.0;
        public double AngleBeta
        {
            get => _angleBeta;
            set => SetProperty(ref _angleBeta, value);
        }

        // 解析結果: 部材角（水平解析完了後に設定）
        private double? _memberAngle;
        public double? MemberAngle
        {
            get => _memberAngle;
            set => SetProperty(ref _memberAngle, value);
        }

        // 選択状態
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // 表示状態（アクティブ/非アクティブ）
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
    }
}
