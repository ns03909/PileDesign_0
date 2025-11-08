using System.Collections.Generic;
using System.Reflection;
using System.Windows;

namespace PileDesignCore
{
    public partial class CancellableWindow : Window
    {
        private readonly Dictionary<string, object> previousPropertyValues = new Dictionary<string, object>();

        protected void SavePrevPropertyValues(object viewModel)
        {
            PropertyInfo[] properties = viewModel.GetType().GetProperties();
            foreach (PropertyInfo property in properties)
            {
                if (property.CanRead)
                {
                    object value = property.GetValue(viewModel);
                    previousPropertyValues[property.Name] = value;
                }
            }
        }

        protected void RestorePrevPropertyValues(object viewModel)
        {
            PropertyInfo[] properties = viewModel.GetType().GetProperties();
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
    }
}