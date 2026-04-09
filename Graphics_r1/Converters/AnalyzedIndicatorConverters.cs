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
                if (loadCaseName is "ALL" or "All")
                    return Visibility.Collapsed;

                bool isAnalyzed = model.AnalysisStepResults
                    .Any(r => r.LoadCase?.LoadName == loadCaseName);
                return isAnalyzed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    /// <summary>
    /// 荷重組み合わせ名が解析済みかどうかを Visibility に変換する。
    /// values[0]: string loadCombinationName (GetName() or Name 形式)
    /// values[1]: string selectedLoadCaseName (省略時 or "All"/"ALL" は全荷重ケース対象)
    /// values[2]: AnaModel currentModel
    /// </summary>
    public class LoadCombinationAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is string loadCombinationName &&
                values[2] is AnaModel model &&
                model.AnalysisStepResults != null)
            {
                // "ALL" / "All" は全荷重組み合わせ対象
                if (loadCombinationName is "ALL" or "All")
                    return Visibility.Collapsed;

                string? selectedLoadCase = values[1] as string;
                bool filterByLoadCase = !string.IsNullOrEmpty(selectedLoadCase) &&
                                        selectedLoadCase != "ALL" && selectedLoadCase != "All";

                bool isAnalyzed = model.AnalysisStepResults
                    .Any(r => (!filterByLoadCase || r.LoadCase?.LoadName == selectedLoadCase) &&
                              (r.LoadCombination?.GetName() == loadCombinationName ||
                               r.LoadCombination?.Name == loadCombinationName));
                return isAnalyzed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    /// <summary>
    /// 単杭沈下解析が完了しているかどうかを Visibility に変換する。
    /// values[0]: string loadCaseName (未使用だが統一性のため)
    /// values[1]: bool IsVerticalAnalysisDone
    /// </summary>
    public class SettlementAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[1] is bool isSettlementDone &&
                isSettlementDone)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    /// <summary>
    /// 群杭沈下解析が完了しているかどうかを Visibility に変換する（VL荷重ケースのみ）。
    /// values[0]: string loadCaseName
    /// values[1]: bool IsGroupPileSettlementAnalysisDone
    /// </summary>
    public class GroupSettlementAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string loadCaseName &&
                values[1] is bool isGroupSettlementDone &&
                isGroupSettlementDone &&
                loadCaseName == "VL")
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }

    /// <summary>
    /// 液状化条件が解析済みかどうかを Visibility に変換する。
    /// values[0]: string liquefactionLabel (e.g. "液状化考慮", "液状化なし")
    /// values[1]: AnaModel currentModel
    /// </summary>
    public class LiquefactionAnalyzedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string liqLabel &&
                values[1] is AnaModel model &&
                model.AnalysisStepResults != null)
            {
                // "ALL" は常にインジケータ非表示
                if (liqLabel is "ALL" or "All")
                    return Visibility.Collapsed;

                bool targetIsLiq = liqLabel.Contains("考慮") || liqLabel == "有" ||
                                   liqLabel == "True" || liqLabel == "true";
                bool isAnalyzed = model.AnalysisStepResults
                    .Any(r => r.IsLiquefaction == targetIsLiq);
                return isAnalyzed ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => null;
    }
}
