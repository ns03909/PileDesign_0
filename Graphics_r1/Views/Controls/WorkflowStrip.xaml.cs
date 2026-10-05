using System.Windows.Controls;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// 工程の帯 (入力 → 杭要素分割 → 解析 → 検定 → 計算書)。押すとその工程の画面を開く。
    /// DataContext は MainWindowViewModel (<see cref="ViewModels.MainWindowViewModel.WorkflowSteps"/>)。
    /// </summary>
    public partial class WorkflowStrip : UserControl
    {
        public WorkflowStrip()
        {
            InitializeComponent();
        }
    }
}
