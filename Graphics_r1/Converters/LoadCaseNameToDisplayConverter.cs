using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;
using PileDesign.Models.InputData;

namespace PileDesign.Converters
{
    /// <summary>
    /// 荷重ケース名 + AllSeismicLoadCases (もしくは LoadCasesLevel1 + LoadCasesLevel2) から、
    /// ComboBox 表示用の正規ラベル ("L1-1", "L2-3" 等) を返す MultiValueConverter。
    /// OTM 表/軸力 DataGrid 列ヘッダの "L1-1〜L2-4" 表記と一貫させ、ユーザがリネームしても
    /// 構造的識別子としては不変な表現を提供する。元の LoadName は ToolTip で確認可能。
    ///
    /// データ (LoadName) は変更しないため、内部 binding / Save-Load / Docx 出力は従来どおり動作する。
    /// VL/VL0/VLadd 等の常時系 (Level=0) は code 化せず、生の name を返す。
    /// </summary>
    public class LoadCaseNameToDisplayConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values is null || values.Length < 1 || values[0] is not string name)
                return string.Empty;

            // 2 引数モード (推奨): values[1] = AllSeismicLoadCases (Level1+Level2 混在)
            // LoadCase.Level で振り分け、Level 内位置を 1-based でカウント。
            if (values.Length == 2 && values[1] is IEnumerable allCases)
            {
                int idxL1 = 0, idxL2 = 0;
                foreach (var item in allCases)
                {
                    if (item is not LoadCase lc) continue;
                    if (lc.Level == 1)
                    {
                        idxL1++;
                        if (lc.LoadName == name) return $"L1-{idxL1}";
                    }
                    else if (lc.Level == 2)
                    {
                        idxL2++;
                        if (lc.LoadName == name) return $"L2-{idxL2}";
                    }
                }
                return name; // VL/VL0/VLadd 等の常時系は prefix なし
            }

            // 3 引数モード (旧式): values[1]=Level1, values[2]=Level2
            if (values.Length >= 3)
            {
                if (values[1] is IEnumerable level1)
                {
                    int i = 0;
                    foreach (var item in level1)
                    {
                        if (item is LoadCase lc && lc.LoadName == name) return $"L1-{i + 1}";
                        i++;
                    }
                }
                if (values[2] is IEnumerable level2)
                {
                    int i = 0;
                    foreach (var item in level2)
                    {
                        if (item is LoadCase lc && lc.LoadName == name) return $"L2-{i + 1}";
                        i++;
                    }
                }
            }

            return name;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
