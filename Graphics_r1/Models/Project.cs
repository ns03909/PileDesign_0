using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace PileDesign.Models
{
    public class Input : INotifyPropertyChanged
    {
        // イベントハンドラー
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public FundamentalInput Fundamental { get; set; }
        public ObservableCollection<LoadCase> Level1Loadcases { get; set; }
        public ObservableCollection<LoadCase> Level2Loadcases { get; set; }
        public ObservableCollection<GroundInput> Grounds { get; set; }
        //public ObservableCollection<PileBody> PileBodies { get; set; }
        //public ObservableCollection<PileLayoutItems> PileLayouts { get; set; }
        //public Embedment Embedment { get; set; }

        // コンストラクタ //
        public Input()
        {
            Fundamental = new FundamentalInput();
            Level1Loadcases = [];
            Level2Loadcases = [];
            Grounds = [];
            //PileBodies = [];
            //PileLayouts = [];
            //Embedment = new Embedment();
        }


    }
}
