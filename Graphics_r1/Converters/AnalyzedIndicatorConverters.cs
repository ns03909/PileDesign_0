using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using PileDesign.FEM;

namespace PileDesign.Converters
{
    /// <summary>
    /// 荷重ケース名が解析済みかどうかを Visibility に変換する。
    /// values[0]: string loadCaseName (ComboBox item)
    /// values[1]: AnaModel currentModel
    /// </summary>
    public class LoadCaseAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string loadCaseName &&
                values[1] is AnaModel model &&
                model.AnalysisStepResults != null)
            {
                bool isAnalyzed = model.AnalysisStepResults
                    .Any(r => r.LoadCase?.LoadName == loadCaseName);
                return isAnalyzed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 荷重組み合わせ名が解析済みかどうかを Visibility に変換する。
    /// values[0]: string loadCombinationName (ComboBox item, GetName() 形式)
    /// values[1]: string selectedLoadCaseName
    /// values[2]: AnaModel currentModel
    /// </summary>
    public class LoadCombinationAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is string loadCombinationName &&
                values[1] is string selectedLoadCaseName &&
                values[2] is AnaModel model &&
                model.AnalysisStepResults != null)
            {
                bool isAnalyzed = model.AnalysisStepResults
                    .Any(r => r.LoadCase?.LoadName == selectedLoadCaseName &&
                              r.LoadCombination?.GetName() == loadCombinationName);
                return isAnalyzed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
