using PileDesign.ViewModels;
using System.Threading.Tasks;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// スプラッシュスクリーン: NIKKENロゴを2秒表示して自動で閉じる
    /// </summary>
    public partial class LoadingMainWindow : Window
    {
        public LoadingMainWindow()
        {
            InitializeComponent();

            // バージョン表示
            VersionText.Text = $"v{MainWindowViewModel.AppVersion}";

            // 2秒後に自動で閉じる
            Task.Delay(2000).ContinueWith(t =>
            {
                Dispatcher.Invoke(() => Close());
            });
        }
    }
}
