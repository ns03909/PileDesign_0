using System.Threading.Tasks;
using System.Windows;


namespace PileDesign.Views
{
    /// <summary>
    /// LoadingMainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class LoadingMainWindow : Window
    {
        public LoadingMainWindow()
        {
            InitializeComponent();

            //// フェードアウトアニメーションを開始
            //fadeOutStoryboard.Begin();
            // 3秒間待機してからウィンドウを閉じる
            Task.Delay(3000).ContinueWith(t =>
            {
                Dispatcher.Invoke(() =>
                {
                    Close();
                });
            });
        }
    }
}
