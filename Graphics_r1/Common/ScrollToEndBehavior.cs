using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PileDesign.Common
{
    public class ScrollToEndBehavior : Behavior<ListBox>
    {
        private INotifyCollectionChanged _collection;

        protected override void OnAttached()
        {
            base.OnAttached();
            AttachToCollection();
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            DetachFromCollection();
        }

        protected override void OnPropertyChanged(System.Windows.DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property.Name == "ItemsSource")
            {
                DetachFromCollection();
                AttachToCollection();
            }
        }

        private void AttachToCollection()
        {
            if (AssociatedObject?.ItemsSource is INotifyCollectionChanged collection)
            {
                _collection = collection;
                _collection.CollectionChanged += Collection_CollectionChanged;
            }
        }

        private void DetachFromCollection()
        {
            if (_collection != null)
            {
                _collection.CollectionChanged -= Collection_CollectionChanged;
                _collection = null;
            }
        }

        private void Collection_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (AssociatedObject.Items.Count > 0)
            {
                AssociatedObject.Dispatcher.BeginInvoke(
                    DispatcherPriority.ContextIdle,
                    new Action(() =>
                    {
                        var scrollViewer = FindScrollViewer((DependencyObject)AssociatedObject); // ←キャストを追加
                        if (scrollViewer != null)
                        {
                            scrollViewer.ScrollToEnd();
                        }
                        else
                        {
                            AssociatedObject.UpdateLayout();
                            AssociatedObject.ScrollIntoView(AssociatedObject.Items[^1]);
                        }
                    }));
            }
        }

        private ScrollViewer FindScrollViewer(DependencyObject d)
        {
            if (d is ScrollViewer sv)
                return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
