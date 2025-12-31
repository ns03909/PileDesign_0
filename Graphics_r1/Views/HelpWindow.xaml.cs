using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace PileDesign.Views
{
    /// <summary>
    /// WindowHelp.xaml の相互作用ロジック
    /// </summary>
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();

            // WebView2 の初期化
            //HelpWebView.Source = new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Views.help.html"));
            HelpWebView.Source = new Uri(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "Help.html"));

            // ファイルパスの絶対パス指定（file:/// 形式）
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "Help.html");
            HelpWebView.Source = new Uri("file:///" + filePath.Replace("\\", "/"));


            //string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help\\Help.html");
            //MessageBox.Show($"Generated Path: {filePath}");
            //if (File.Exists(filePath))
            //{
            //    MessageBox.Show("ファイルが存在します: " + filePath);
            //}
            //else
            //{
            //    MessageBox.Show("ファイルが存在しません: " + filePath);
            //}
        }

        //private void ChapterTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        //{
        //    if (ChapterTreeView.SelectedItem is TreeViewItem selectedItem)
        //    {
        //        string selectedContent = selectedItem.Header.ToString();
        //        TextPointer targetPointer = null;

        //        switch (selectedContent)
        //        {
        //            case "準拠指針・参考文献":
        //                targetPointer = Section1.ContentStart;
        //                break;
        //            case "杭の定義":
        //                targetPointer = Section2.ContentStart;
        //                break;
        //            case "一般的な杭基礎検討項目":
        //                targetPointer = Section3.ContentStart;
        //                break;
        //            case "データ入力の概要、手順":
        //                targetPointer = Section4.ContentStart;
        //                break;
        //            case "基本設定":
        //                targetPointer = SubSection4_1.ContentStart;
        //                break;
        //            case "荷重ケース":
        //                targetPointer = SubSection4_2.ContentStart;
        //                break;
        //            case "杭体":
        //                targetPointer = SubSection4_3.ContentStart;
        //                break;
        //            case "根入部":
        //                targetPointer = SubSection4_4.ContentStart;
        //                break;
        //            case "杭配置":
        //                targetPointer = SubSection4_5.ContentStart;
        //                break;
        //            case "杭の水平地盤反力係数":
        //                targetPointer = Section5.ContentStart;
        //                break;
        //            case "根入部の水平土圧合力ばね":
        //                targetPointer = Section6.ContentStart;
        //                break;
        //            case "単杭の鉛直地盤ばね":
        //                targetPointer = Section7.ContentStart;
        //                break;
        //            case "群杭の即時沈下モデル":
        //                targetPointer = Section8.ContentStart;
        //                break;
        //            case "荷重・解析条件":
        //                targetPointer = Section9.ContentStart;
        //                break;
        //            case "プログラム開発関連":
        //                targetPointer = Section10.ContentStart;
        //                break;
        //        }

        //        if (targetPointer != null)
        //        {
        //            if (targetPointer.Parent is FrameworkContentElement parent)
        //            {
        //                parent.BringIntoView();
        //            }
        //        }
        //    }
        //}
    }
}
