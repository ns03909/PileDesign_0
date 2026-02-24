using System;
using System.IO;
using System.Windows;

namespace PileDesign.Views
{
    public partial class VerificationWindow : Window
    {
        public VerificationWindow()
        {
            InitializeComponent();

            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "verification.html");
            VerificationWebView.Source = new Uri("file:///" + filePath.Replace("\\", "/"));
        }
    }
}
