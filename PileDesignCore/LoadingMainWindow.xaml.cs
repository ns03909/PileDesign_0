using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace PileDesignCore
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
            //Storyboard fadeOutStoryboard = (Storyboard)FindResource("FadeOutAnimation");
            //fadeOutStoryboard.Completed += (sender, e) =>
            //{
            //    Close();
            //};
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
