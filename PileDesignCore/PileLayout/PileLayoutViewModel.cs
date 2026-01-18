using PileDesignCore.Shared;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace PileDesignCore
{

    [Serializable]
    public class PileLayoutDataItem : BaseDataItem
    {
        private int pileNumber;
        public int PileNumber
        {
            get => pileNumber;
            set => SetProperty(ref pileNumber, value);
        }

        private double x;
        public double X
        {
            get => x;
            set => SetProperty(ref x, value);
        }

        private double y;
        public double Y
        {
            get => y;
            set => SetProperty(ref y, value);
        }

        private double pileTopAltitude;
        public double PileTopAltitude
        {
            get => pileTopAltitude;
            set => SetProperty(ref pileTopAltitude, value);
        }

        private int pileBodyNo = 1;
        public int PileBodyNo
        {
            get => pileBodyNo;
            set => SetProperty(ref pileBodyNo, value);
        }

        private int groundNo = 1;
        public int GroundNo
        {
            get => groundNo;
            set => SetProperty(ref groundNo, value);
        }

        //群杭係数
        private double groupPileFactor = 1.0;
        public double GroupPileFactor
        {
            get => groupPileFactor;
            set => SetProperty(ref groupPileFactor, value);
        }

        // 杭間隔比
        private double distanceFactor;
        public double DistanceFactor
        {
            get => distanceFactor;
            set => SetProperty(ref distanceFactor, value);
        }

        // 常時軸力
        private double axialForceVL;
        public double AxialForceVL
        {
            get => axialForceVL;
            set => SetProperty(ref axialForceVL, value);
        }

        // 追加軸力
        private double axialForceVLAdditional;
        public double AxialForceVLAdditional
        {
            get => axialForceVLAdditional;
            set => SetProperty(ref axialForceVLAdditional, value);
        }

        // レベル1地震時変動軸力
        private double axialForceEX;
        public double AxialForceEX
        {
            get => axialForceEX;
            set => SetProperty(ref axialForceEX, value);
        }

        // レベル1地震時変動軸力
        private double axialForceEY;
        public double AxialForceEY
        {
            get => axialForceEY;
            set => SetProperty(ref axialForceEY, value);
        }

        // レベル1地震時軸力
        public ObservableCollection<double> AxialForceLevel1s { get; set; }

        // レベル2地震時軸力
        public ObservableCollection<double> AxialForceLevel2s { get; set; }

        // 前方杭
        public ObservableCollection<bool> IsFrontPiles { get; set; }


        // コンストラクタ初期化
        public PileLayoutDataItem()
        {
            AxialForceLevel1s = new ObservableCollection<double>();
            AxialForceLevel2s = new ObservableCollection<double>();
            IsFrontPiles = new ObservableCollection<bool>();

            // 4項目ずつ初期化
            for (int i = 0; i < 4; i++)
            {
                AxialForceLevel1s.Add(0.0);
                AxialForceLevel2s.Add(0.0);
                IsFrontPiles.Add(true);
            }
        }
    }

    [Serializable]
    public class GridDataItem : BaseDataItem
    {
        private int _no;
        public int No
        {
            get => _no;
            set => SetProperty(ref _no, value);
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _coord;
        public double Coord
        {
            get => _coord;
            set => SetProperty(ref _coord, value);
        }

        private double _spacing;
        public double Spacing
        {
            get => _spacing;
            set => SetProperty(ref _spacing, value);
        }
    }

    [Serializable]
    public class PileLayoutViewModel : BaseViewModel
    {
        private ObservableCollection<PileLayoutDataItem> _pileLayoutCollection = new ObservableCollection<PileLayoutDataItem>();
        public ObservableCollection<PileLayoutDataItem> PileLayoutCollection
        {
            get => _pileLayoutCollection;
            set => SetProperty(ref _pileLayoutCollection, value);
        }

        private PileLayoutDataItem _selectedItem;
        public PileLayoutDataItem SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                }
            }
        }

        // レベル1地震時軸力
        public bool IsElastic { get; set; }

        private ObservableCollection<GridDataItem> _gridX = new ObservableCollection<GridDataItem>();
        public ObservableCollection<GridDataItem> GridX
        {
            get => _gridX;
            set => SetProperty(ref _gridX, value);
        }

        private ObservableCollection<GridDataItem> _gridY = new ObservableCollection<GridDataItem>();
        public ObservableCollection<GridDataItem> GridY
        {
            get => _gridY;
            set => SetProperty(ref _gridY, value);
        }

        // コンストラクタ
        public PileLayoutViewModel()
        {
            PileLayoutCollection = new ObservableCollection<PileLayoutDataItem>();

            GridX = new ObservableCollection<GridDataItem>();
            GridY = new ObservableCollection<GridDataItem>();

        }

    }
}



