using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace PileDesignCore
{
    /// <summary>
    /// LoadCaseWindow.xaml の相互作用ロジック
    /// </summary>

    public partial class LoadCaseWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();

        private readonly LoadCaseViewModel viewModel;

        public LoadCaseWindow(LoadCaseViewModel viewModel)
        {
            InitializeComponent();
            // Set the sharedViewModel as the DataContext
            //sharedViewModel = viewModel;
            this.viewModel = viewModel;

            DataContext = this.viewModel;
            //DataContext = viewModel;

            DataGridLoadCase1.ItemsSource = viewModel.LoadCases1;
            DataGridLoadCase2.ItemsSource = viewModel.LoadCases2;

            DataGridCommonLoadCase1.ItemsSource = viewModel.CommonLoadCases1;
            DataGridCommonLoadCase2.ItemsSource = viewModel.CommonLoadCases2;

            // 初期値を保存
            SavePreviousPropertyValues(viewModel);

        }
        private void SavePreviousPropertyValues(LoadCaseViewModel viewModel)
        {
            // 全てのプロパティの前回の値を保存
            PropertyInfo[] properties = typeof(LoadCaseViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        private void RestorePreviousPropertyValues(LoadCaseViewModel viewModel)
        {
            // 全てのプロパティを前回の値に戻す
            PropertyInfo[] properties = typeof(LoadCaseViewModel).GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanWrite)
                {
                    if (previousPropertyValues.TryGetValue(property.Name, out object value))
                    {
                        property.SetValue(viewModel, value);
                    }
                }
            }
        }

        private void ButtonOK_Click(object sender, RoutedEventArgs e)
        {
            // OKボタンがクリックされたときの処理
            this.DialogResult = true;

            this.Close();
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            //// Cancelボタンがクリックされたときの処理

            //// プロパティを前回の保存時の値に戻す
            LoadCaseViewModel viewModel = (LoadCaseViewModel)DataContext;

            // プロパティを前回の保存時の値に戻す
            RestorePreviousPropertyValues(viewModel);
            this.Close();
        }


        private void UpdateCommonLoadCase1Property<T>(Func<CommonLoadCase1, T> getProperty, Action<LoadCase1, T> setProperty)
        {
            CommonLoadCase1 commonLoadCase1 = viewModel.CommonLoadCases1.FirstOrDefault();
            T commonLoadCase1Value = getProperty(commonLoadCase1);

            foreach (LoadCase1 LoadCase1 in viewModel.LoadCases1)
            {
                setProperty(LoadCase1, commonLoadCase1Value);
            }

            DataGridLoadCase1.Items.Refresh();
        }
        private void CommonLoadCase1IsPileNonLinearButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<bool>(c => c.IsPileNonLinear, (lc, val) => lc.IsPileNonLinear = val);
        }

        private void CommonLoadCase1IsSoilNonLinearButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<bool>(c => c.IsSoilNonLinear, (lc, val) => lc.IsSoilNonLinear = val);
        }

        private void CommonLoadCase1UpperMassForceButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<double>(c => c.UpperMassForce, (lc, val) => lc.UpperMassForce = val);
        }

        private void CommonLoadCase1FoudationMassForceButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<double>(c => c.FoundationMassForce, (lc, val) => lc.FoundationMassForce = val);
        }

        private void CommonLoadCase1ForceActionPointXButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<double>(c => c.ForceActionPointX, (lc, val) => lc.ForceActionPointX = val);
        }

        private void CommonLoadCase1ForceActionPointYButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase1Property<double>(c => c.ForceActionPointY, (lc, val) => lc.ForceActionPointY = val);
        }




        private void UpdateCommonLoadCase2Property<T>(Func<CommonLoadCase2, T> getProperty, Action<LoadCase2, T> setProperty)
        {
            CommonLoadCase2 commonLoadCase2 = viewModel.CommonLoadCases2.FirstOrDefault();
            T commonLoadCase2Value = getProperty(commonLoadCase2);

            foreach (LoadCase2 loadCase2 in viewModel.LoadCases2)
            {
                setProperty(loadCase2, commonLoadCase2Value);
            }

            DataGridLoadCase2.Items.Refresh();
        }
        private void CommonLoadCase2IsPileNonLinearButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<bool>(c => c.IsPileNonLinear, (lc, val) => lc.IsPileNonLinear = val);
        }

        private void CommonLoadCase2IsSoilNonLinearButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<bool>(c => c.IsSoilNonLinear, (lc, val) => lc.IsSoilNonLinear = val);
        }

        private void CommonLoadCase2UpperMassForceButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<double>(c => c.UpperMassForce, (lc, val) => lc.UpperMassForce = val);
        }

        private void CommonLoadCase2FoudationMassForceButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<double>(c => c.FoundationMassForce, (lc, val) => lc.FoundationMassForce = val);
        }

        private void CommonLoadCase2ForceActionPointXButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<double>(c => c.ForceActionPointX, (lc, val) => lc.ForceActionPointX = val);
        }

        private void CommonLoadCase2ForceActionPointYButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateCommonLoadCase2Property<double>(c => c.ForceActionPointY, (lc, val) => lc.ForceActionPointY = val);
        }





    }
}
