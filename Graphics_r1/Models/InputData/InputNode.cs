using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Media.Media3D;

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
        private static int _nextId = 1; // 次のIDを保持する静的フィールド
        private static List<int> _availableIds = []; // 利用可能なIDのリスト

        // IDプロパティ（既存の整数ID）
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

        // X座標
        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        // Y座標
        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        // Z座標
        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        // 3D座標
        public Point3D Point3D => new() { X = X, Y = Y, Z = Z };

        /// <summary>
        /// タイプに応じた表示色
        /// </summary>
        [JsonIgnore]
        public string DisplayColor => Type == NodeType.Pile ? "Blue" : "Orange";

        // コンストラクタ
        public InputNode()
        {
            if (_availableIds.Count > 0)
            {
                Id = _availableIds[0];
                _availableIds.RemoveAt(0);
            }
            else
            {
                Id = _nextId++;
            }

            IsVisible = true;
        }

        // デストラクタ
        ~InputNode()
        {
            _availableIds.Add(Id);
        }
    }
}
