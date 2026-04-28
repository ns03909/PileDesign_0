
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.Models.InputData
{
    /// <summary>
    /// [Obsolete] 旧梁要素クラス。FoundationBeamElement に完全に置き換え済み。
    /// デシリアライズ（旧ファイル読み込み）用にのみ残している。
    /// MigrateElementsToFoundationBeams() で新形式に変換される。
    /// </summary>
    [Obsolete("旧形式。FoundationBeamElement を使用してください。")]
    public class Element : BaseModel
    {
        public ObservableCollection<InputNode> Nodes { get; set; } = [];

        private static int _nextId = 1; // 次のIDを保持する静的フィールド
        private static List<int> _availableIds = []; // 利用可能なIDのリスト

        // IDプロパティ
        public int Id { get; private set; }

        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        private bool _isSelected = false;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private string _elementType;
        public string ElementType
        {
            get => _elementType;
            set => SetProperty(ref _elementType, value);
        }

        // 無引数コンストラクタ（デシリアライズで使用される）
        public Element()
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
        }

        // コンストラクタ
        public Element(string elementType, params PileLayoutDataItem[] nodes)
        {
            ElementType = elementType;
            foreach (var node in nodes)
            {
                Nodes.Add(node);
            }

            if (_availableIds.Count > 0)
            {
                Id = _availableIds[0];
                _availableIds.RemoveAt(0);
            }
            else
            {
                Id = _nextId++;
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Element other) return false;

            return ElementType == other.ElementType && Nodes.SequenceEqual(other.Nodes);
        }

        public override int GetHashCode()
        {
            int hash = ElementType.GetHashCode();
            foreach (var node in Nodes)
            {
                hash = hash * 31 + node.GetHashCode();
            }
            return hash;
        }

        // デストラクタ
        ~Element()
        {
            _availableIds.Add(Id);
        }
    }
}

