//using System;
//using System.Collections.Generic;
//using System.ComponentModel;

//namespace PileDesign.Models.InputData
//{
//    class GridDataItemInput : INotifyPropertyChanged
//    {
//        // プロパティ変更イベント
//        public event PropertyChangedEventHandler PropertyChanged;

//        protected virtual void OnPropertyChanged(string propertyName)
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
//        }

//        // グリッド番号
//        private int _gridNo;
//        public int GridNo
//        {
//            get => _gridNo;
//            set
//            {
//                if (_gridNo != value)
//                {
//                    _gridNo = value;
//                    OnPropertyChanged(nameof(GridNo));
//                }
//            }
//        }

//        // グリッド名
//        private string _gridName;
//        public string GridName
//        {
//            get => _gridName;
//            set
//            {
//                if (_gridName != value)
//                {
//                    _gridName = value;
//                    OnPropertyChanged(nameof(GridName));
//                }
//            }
//        }

//        // 位置
//        private double _position;
//        public double Position
//        {
//            get => _position;
//            set
//            {
//                if (_position != value)
//                {
//                    _position = value;
//                    OnPropertyChanged(nameof(Position));
//                }
//            }
//        }

//        // 間隔
//        private double _spacing;
//        public double Spacing
//        {
//            get => _spacing;
//            set
//            {
//                if (_spacing != value)
//                {
//                    _spacing = value;
//                    OnPropertyChanged(nameof(Spacing));
//                }
//            }
//        }

//        // nodes
//        public List<PileLayoutDataItem> Piles = [];

//        // コンストラクタ
//        public GridDataItemInput()
//        {
//            GridNo = 0;
//            GridName = "";
//            Position = 0.0;
//            Spacing = 0.0;
//        }

//        public void SetPiles(List<PileLayoutDataItem> piles, double axisAngle, double distance = 0.1)
//        {
//            // xGridの場合はy座標で、yGridの場合はx座標で位置を設定
//            Piles = [];
//            if (axisAngle == 0) // X
//            {
//                foreach (var pile in piles)
//                {
//                    if (Math.Abs(pile.Y - Position) <= distance)
//                    {
//                        Piles.Add(pile);
//                    }
//                }
//            }
//            else if (axisAngle == 90) // Y
//            {
//                foreach (var pile in piles)
//                {
//                    if (Math.Abs(pile.X - Position) <= distance)
//                    {
//                        Piles.Add(pile);
//                    }
//                }
//            }
//        }
//    }
//}
