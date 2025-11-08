using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace PileDesign.Models.InputData
{
    public class Node : BaseDataItem
    {
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

        // 選択状態
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        // 表示状態
        private bool _isVisible;
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

        // 杭頭Z
        private double _z;
        public double Z
        {
            get => _z;
            set => SetProperty(ref _z, value);
        }

        // 3D座標
        public Point3D Point3D => new() { X = X, Y = Y, Z = Z };

        // コンストラクタ
        public Node()
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

        // デストラクタ
        ~Node()
        {
            _availableIds.Add(Id);
        }
    }
}
