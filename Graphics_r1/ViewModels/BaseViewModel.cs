using PileDesign.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 画面側の土台。<see cref="ObservableModel"/> に、表のコピー命令を足したもの。
    ///
    /// 変更通知と数値の検査は <see cref="ObservableModel"/> にある。
    /// 入力データ側のクラスはそちらを継承すること。ここを継承すると、
    /// 表 (DataGrid) を触る命令まで一緒に付いてくる。
    /// </summary>
    [Serializable]
    public partial class BaseViewModel : ObservableModel
    {
        public ICommand CopyDataGridSelectionCommand { get; } = new RelayCommand(p =>
        {
            if (p is DataGrid dg) CopyDataGridSelection(dg);
        });

        private static void CopyDataGridSelection(DataGrid dataGrid)
            => Output.DataGridCsv.CopySelectionToClipboard(dataGrid);
    }
}
