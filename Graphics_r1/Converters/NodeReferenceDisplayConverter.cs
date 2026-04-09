using PileDesign.Models.InputData;
using System;
using System.Globalization;
using System.Windows.Data;

namespace PileDesign.Converters
{
    /// <summary>
    /// 節点参照（Type + Id）を表示用文字列に変換するコンバーター
    /// 例: "G:3" (一般節点3), "P:5" (杭配置5), "F:2" (基礎梁節点2)
    /// </summary>
    public class NodeReferenceDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0]: NodeReferenceType (Type)
            // values[1]: Guid (Id)
            // values[2]: InputModel

            if (values.Length != 3) return "?";
            if (values[0] == null || values[1] == null || values[2] == null) return "?";
            if (values[0] is not NodeReferenceType type) return "?";
            if (values[1] is not Guid id) return "?";
            if (values[2] is not InputModel inputModel) return "?";

            return inputModel.GetNodeReferenceDisplayString(type, id);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
