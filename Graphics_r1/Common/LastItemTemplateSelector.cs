using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Common
{
    public class LastItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate DefaultTemplate { get; set; }
        public DataTemplate LastItemTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var itemsControl = ItemsControl.ItemsControlFromItemContainer(container);
            if (itemsControl != null && item != null)
            {
                var items = itemsControl.Items;
                if (items.Count > 0)
                {
                    var lastItem = items[^1];
                    // 値型対応: Equalsで比較
                    if (Equals(item, lastItem))
                    {
                        return LastItemTemplate;
                    }
                }
            }
            return DefaultTemplate;
        }
    }
}
