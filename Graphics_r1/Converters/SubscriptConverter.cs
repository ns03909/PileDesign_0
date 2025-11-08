using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace PileDesign.Converters
{
    public class SubscriptConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text)
            {
                var textBlock = new TextBlock();
                foreach (var part in text.Split(' '))
                {
                    if (part.Contains("αL"))
                    {
                        textBlock.Inlines.Add(new Run("α"));
                        textBlock.Inlines.Add(new Run("L") { BaselineAlignment = BaselineAlignment.Subscript });
                        textBlock.Inlines.Add(new Run(part.Substring(2)));
                    }
                    else if (part.Contains("βU"))
                    {
                        textBlock.Inlines.Add(new Run("β"));
                        textBlock.Inlines.Add(new Run("U") { BaselineAlignment = BaselineAlignment.Subscript });
                        textBlock.Inlines.Add(new Run(part.Substring(2)));
                    }
                    else if (part.Contains("βL"))
                    {
                        textBlock.Inlines.Add(new Run("β"));
                        textBlock.Inlines.Add(new Run("L") { BaselineAlignment = BaselineAlignment.Subscript });
                        textBlock.Inlines.Add(new Run(part.Substring(2)));
                    }
                    else
                    {
                        textBlock.Inlines.Add(new Run(part));
                    }
                    textBlock.Inlines.Add(new Run(" "));
                }
                return textBlock;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}