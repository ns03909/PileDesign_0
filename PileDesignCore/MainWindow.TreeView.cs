using PileDesignCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PileDesignCore
{
    public partial class MainWindow : Window
    {

        private void SetupTreeView()
        {
            CTreeViewData data1 = new CTreeViewData
            {
                Name = "基本事項",
                Children = new List<CTreeViewData>
                {
                    new CTreeViewData
                    {
                        Name = "TP",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                    new CTreeViewData
                    {
                        Name = "asdf",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                },
            };

            CTreeViewDatas.Add(data1);

            CTreeViewData data2 = new CTreeViewData
            {
                Name = "荷重ケース",
                Children = new List<CTreeViewData>
                {
                    new CTreeViewData
                    {
                        Name = "TP",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                    new CTreeViewData
                    {
                        Name = "asdf",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                },
            };

            CTreeViewDatas.Add(data2);

            CTreeViewData data3 = new CTreeViewData
            {
                Name = "地盤",
                Children = new List<CTreeViewData>
                {
                    new CTreeViewData
                    {
                        Name = "TP",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                    new CTreeViewData
                    {
                        Name = "asdf",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                },
            };

            CTreeViewDatas.Add(data3);

            CTreeViewData data4 = new CTreeViewData
            {
                Name = "杭配置・軸力",
                Children = new List<CTreeViewData>
                {
                    new CTreeViewData
                    {
                        Name = "TP",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                    new CTreeViewData
                    {
                        Name = "asdf",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                },
            };

            CTreeViewDatas.Add(data4);

            CTreeViewData data5 = new CTreeViewData
            {
                Name = "根入部",
                Children = new List<CTreeViewData>
                {
                    new CTreeViewData
                    {
                        Name = "TP",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                    new CTreeViewData
                    {
                        Name = "asdf",
                        Children = new List<CTreeViewData>
                        {
                            new CTreeViewData { Name = "ノード1-1-1" },
                            new CTreeViewData { Name = "ノード1-1-2" }
                        },
                    },
                },
            };

            CTreeViewDatas.Add(data5);

            treeView.ItemsSource = CTreeViewDatas;
        }
    }

    // CTreeViewクラス
    public class CTreeViewData
    {
        public string Name { get; set; }
        public IEnumerable<CTreeViewData> Children { get; set; }
    }
}

