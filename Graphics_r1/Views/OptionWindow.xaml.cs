using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views
{
    /// <summary>
    /// OptionWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class OptionWindow : Window
    {
        public OptionWindow()
        {
            InitializeComponent();
            this.DataContext = ((MainWindow)Application.Current.MainWindow).DataContext;
            this.Owner = Application.Current.MainWindow; // オーナーを設定
        }

        private void CheckBoxAnalysisResult_Unchecked(object sender, RoutedEventArgs e)
        {
            // MainWindowのインスタンスを取得してUpdateCanvas3Dを呼び出す
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            mainWindow?.UpdateWindow();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // MainWindowのインスタンスを取得してUpdateCanvas3Dを呼び出す
            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            mainWindow?.UpdateCanvas3D();
        }
    }
}
