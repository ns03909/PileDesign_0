using System;
using System.Collections.ObjectModel;

namespace PileDesignCore
{
    [Serializable]
    public class EmbedmentDataItem : BaseDataItem
    {
        private int no;
        public int No
        {
            get => no;
            set => SetProperty(ref no, value);
        }

        private double layerThickness;
        public double LayerThickness
        {
            get => layerThickness;
            set => SetProperty(ref layerThickness, value);
        }

        private double topAltitude;
        public double TopAltitude
        {
            get => topAltitude;
            set => SetProperty(ref topAltitude, value);
        }

        private double bottomAltitude;
        public double BottomAltitude
        {
            get => bottomAltitude;
            set => SetProperty(ref bottomAltitude, value);
        }

        private double x1;
        public double X1
        {
            get => x1;
            set => SetProperty(ref x1, value);
        }

        private double x2;
        public double X2
        {
            get => x2;
            set => SetProperty(ref x2, value);
        }

        private double y1;
        public double Y1
        {
            get => y1;
            set => SetProperty(ref y1, value);
        }

        private double y2;
        public double Y2
        {
            get => y2;
            set => SetProperty(ref y2, value);
        }

        // Add a property for DX and DY
        public double DX => X2 - X1;
        public double DY => Y2 - Y1;
    }

    [Serializable]
    public class EmbedmentViewModel : BaseViewModel
    // EmbedmentViewModel変数の初期値
    {
        private ObservableCollection<EmbedmentDataItem> _embedmentCollection = new ObservableCollection<EmbedmentDataItem>();
        public ObservableCollection<EmbedmentDataItem> EmbedmentCollection
        {
            get => _embedmentCollection;
            set => SetProperty(ref _embedmentCollection, value);
        }

        public EmbedmentViewModel()
        {
            // Initialize the GroundLayerCollection property with an empty ObservableCollection
            EmbedmentCollection = new ObservableCollection<EmbedmentDataItem>();
        }

        private int embedmentNums = 0;
        public int EmbedmentNums
        {
            get => embedmentNums;
            set => SetProperty(ref embedmentNums, value);
        }

        private double topAltitude = 0.00;
        public double TopAltitude
        {
            get => topAltitude;
            set => SetProperty(ref topAltitude, value);
        }

        private object dataContextFundamental;
        public object DataContextFundamental
        {
            get => dataContextFundamental;
            set => SetProperty(ref dataContextFundamental, value);
        }

        private object dataContextGroundLayer;
        public object DataContextGroundLayer
        {
            get => dataContextGroundLayer;
            set => SetProperty(ref dataContextGroundLayer, value);
        }

        private string groundRef;
        public string GroundRef
        {
            get => groundRef;
            set => SetProperty(ref groundRef, value);
        }
    }
}
