using System.Windows.Controls;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// 結果を表示する画面の上部の帯: 表示中の結果が、解析のあとに変更された入力の前のものであることを、
    /// 解析した日時と一緒に知らせる (<see cref="ViewModels.MainWindowViewModel.ResultStaleNotice"/>)。
    /// DataContext は MainWindowViewModel。
    /// </summary>
    public partial class ResultStaleBanner : UserControl
    {
        public ResultStaleBanner()
        {
            InitializeComponent();
        }
    }
}
