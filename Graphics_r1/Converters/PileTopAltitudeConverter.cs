using PileDesign.Models.InputData;
using PileDesign.Views;
using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    //
    public class PileTopAltitudeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            //var inputModel = MainWindowViewModel.InputModel;

            InputModel InputModel = App.InputModel;
            if (double.TryParse(value.ToString(), out double altitude))
            {
                if (altitude > 0)
                    return $"{InputModel.FundamentalInput.RefLevel}+{altitude:0.00}";
                else if (altitude < 0)
                    return $"{InputModel.FundamentalInput.RefLevel}{altitude:0.00}";
                else
                    return $"{InputModel.FundamentalInput.RefLevel}±{altitude:0.00}";
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is MainWindow)
            {
                string input = value.ToString();

                // Assuming the input format is "{RefLevel} {Value}"
                string[] parts = input.Split(' ');

                if (parts.Length == 2 && double.TryParse(parts[1], out double altitude))
                {
                    // Update your variable with the altitude value
                    // For example: pileLayoutWindow.DataContextFundamental.RefLevelVariable = altitude;
                    return altitude;
                }
            }
            return value;
        }
    }
}
