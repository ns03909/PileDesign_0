using System;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

using PileDesign.Common;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// 節点のタイプ
    /// </summary>
    public enum NodeType
    {
        Pile,    // 杭頭節点（PileLayoutDataItemと連動）
        General  // 一般節点（自由配置可能）
    }

    /// <summary>
    /// 入力節点クラス - 杭頭節点と一般節点を統一的に扱う
    /// </summary>
    public class InputNode : BaseDataItem
    {
        // 最後に振った番号。Interlocked.Increment で進める (振るたびに 1 増える、1 始まり)。
        //
        // 以前は、ファイナライザ (~InputNode) が回収した番号をロックの無い static List に戻し、
        // コンストラクタがそこから使い回していた。ファイナライザはファイナライザのスレッドで、
        // コンストラクタは読込・履歴の複製 (Task.Run) などのワーカースレッドでも動くので、List を同時に触り、
        // 同じ番号を 2 つの節点に振る・番号を取りこぼす・RemoveAt が範囲外の例外を投げる、が起こり得た。
        // また、ファイナライザがあるだけで全節点 (杭配置の行も) がファイナライズの対象になり、GC の負担になっていた。
        // 番号は実行中だけの表示用で (保存・読込では振り直す。永続の識別は UniqueId)、
        // 小さい番号を使い回す必要は無いので、ただ増やすだけにした。
        private static int _lastId;

        // IDプロパティ（実行中だけの整数ID。永続の識別子は UniqueId）
        public int Id { get; private set; }

        // ユニークID（削除・挿入に影響されない内部識別子）
        private Guid _uniqueId = Guid.NewGuid();
        public Guid UniqueId
        {
            get => _uniqueId;
            set => SetProperty(ref _uniqueId, value);
        }

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private NodeType _type = NodeType.General;
        /// <summary>
        /// 節点のタイプ（Pile: 杭頭節点, General: 一般節点）
        /// </summary>
        public NodeType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    OnPropertyChanged(nameof(DisplayColor));
                }
            }
        }

        private int? _linkedPileNo;
        /// <summary>
        /// Type=Pile の場合、対応するPileLayoutDataItemのNo
        /// </summary>
        public int? LinkedPileNo
        {
            get => _linkedPileNo;
            set => SetProperty(ref _linkedPileNo, value);
        }

        // 選択状態
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // 表示状態
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        // 座標は有限の数値だけを受け付ける。NaN・±∞ は例外 (NonFiniteValueException) で拒否する。
        //
        // 以前は何でも受け入れていた。表への貼り付けやプロパティパネルは入口で弾くようにしたが、
        // ファイルには「NaN」「Infinity」が書けて、そのまま読めてしまう (保存形式が名前付きの
        // 浮動小数点を許している)。座標の NaN は画面でも解析でも静かに壊れるので、
        // 入口がどこでも、ここで止めて何が悪いかを示す。

        // X座標
        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, NonFiniteValueException.RequireFinite(value, CoordinateName("X")));
        }

        // Y座標
        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, NonFiniteValueException.RequireFinite(value, CoordinateName("Y")));
        }

        // Z座標
        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, NonFiniteValueException.RequireFinite(value, CoordinateName("Z")));
        }

        /// <summary>
        /// メッセージに出す欄の名前。読み込みの途中は番号がまだ入っていないことがあるので、そのときは付けない。
        /// </summary>
        private string CoordinateName(string axis)
            => (No > 0 ? $"節点 {No} の " : "節点の ") + axis + " 座標";

        // 3D座標
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public Point3D Point3D => new() { X = X, Y = Y, Z = Z };

        /// <summary>
        /// タイプに応じた表示色
        /// </summary>
        [JsonIgnore]
        public string DisplayColor => Type == NodeType.Pile ? "Blue" : "Orange";

        // コンストラクタ
        public InputNode()
        {
            Id = System.Threading.Interlocked.Increment(ref _lastId);

            IsVisible = true;
        }
    }
}
