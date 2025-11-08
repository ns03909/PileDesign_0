using System.Collections.ObjectModel;
using System.Windows.Media;

namespace PileDesign.ViewModels
{
    // CTreeViewクラス
    public class CTreeViewData
    {
        public string Name { get; set; }
        public Brush TextColor { get; set; } = Brushes.Black;
        public ObservableCollection<CTreeViewData> Children { get; set; }

        public CTreeViewData()
        {
            Children = [];
        }
    }
}
