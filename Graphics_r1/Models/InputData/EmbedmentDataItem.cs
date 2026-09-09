using PileDesign.Common;

namespace PileDesign.Models.InputData
{
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

        // X1/X2 と DX、Y1/Y2 と DY は同じ表に並んで出る。自分の通知だけだと
        // 隣の DX / DY 列が古いまま残るので、ここで知らせる。
        private double x1;
        public double X1
        {
            get => x1;
            set { if (SetProperty(ref x1, value)) OnPropertyChanged(nameof(DX)); }
        }

        private double x2;
        public double X2
        {
            get => x2;
            set { if (SetProperty(ref x2, value)) OnPropertyChanged(nameof(DX)); }
        }

        private double y1;
        public double Y1
        {
            get => y1;
            set { if (SetProperty(ref y1, value)) OnPropertyChanged(nameof(DY)); }
        }

        private double y2;
        public double Y2
        {
            get => y2;
            set { if (SetProperty(ref y2, value)) OnPropertyChanged(nameof(DY)); }
        }

        // Add a property for DX and DY
        public double DX => X2 - X1;
        public double DY => Y2 - Y1;
    }
}
