using PileDesign.ViewModels;

namespace PileDesign.Models.InputData
{
    public class SettlementGridDataItem : BaseDataItem
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


        private double _settlement;
        public double Settlement
        {
            get => _settlement;
            set => SetProperty(ref _settlement, value);
        }
    }
}

