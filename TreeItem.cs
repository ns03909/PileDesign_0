using System;
using System.Collections.ObjectModel;

public class TreeItem
{
    public string Name { get; set; }
    public ObservableCollection<TreeItem> Children { get; set; }

    public TreeItem(string name)
    {
        Name = name;
        Children = new ObservableCollection<TreeItem>();
    }
}

