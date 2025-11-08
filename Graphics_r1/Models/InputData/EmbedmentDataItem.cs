using PileDesign.ViewModels;

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
}
