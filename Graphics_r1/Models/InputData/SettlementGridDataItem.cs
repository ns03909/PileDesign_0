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

        /// <summary>
        /// 値だけの複製を作る。
        ///
        /// ケースの結果と表示用の複製で<b>同じインスタンスを共有しない</b>ために要る。
        /// 共有していると、保存時に片方が <c>$id</c>・もう片方が <c>$ref</c> になり、
        /// 表示用の複製を将来外せなくなる (外すと既存ファイルが開けない)。
        /// 点数が数千になるので JSON 往復ではなく手書きで複製する。
        /// </summary>
        public SettlementGridDataItem Clone() => new()
        {
            No = No,
            Name = Name,
            X = X,
            Y = Y,
            Settlement = Settlement,
        };
    }
}

