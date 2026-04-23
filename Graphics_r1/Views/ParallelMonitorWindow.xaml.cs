using System.Windows;
using PileDesign.ViewModels;

namespace PileDesign.Views
{
    /// <summary>
    /// 並列解析モニタウィンドウ。
    /// MDOP>=2 時に水平解析ウィンドウが開き、解析終了時に閉じる。
    /// HorizontalCalculationViewModel の ActiveCases / CompletedCaseCount 等にバインド。
    /// </summary>
    public partial class ParallelMonitorWindow : Window
    {
        public ParallelMonitorWindow(HorizontalCalculationViewModel vm, Window? owner = null)
        {
            InitializeComponent();
            DataContext = vm;
            if (owner != null)
            {
                Owner = owner;
                // Owner の右上の少し外側に配置
                Left = owner.Left + owner.ActualWidth - Width - 20;
                Top = owner.Top + 80;
            }
        }
    }
}
