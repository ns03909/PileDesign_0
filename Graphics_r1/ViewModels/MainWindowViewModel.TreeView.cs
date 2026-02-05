using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Media;
using static PileDesign.Services.ModifyString;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel.TreeView.cs
    ///
    /// 責任範囲:
    /// - TreeViewの更新処理
    /// - 各種入力データをTreeView形式に変換（基本設定、荷重、地盤、杭体、杭配置、根入部）
    /// - TreeViewの展開状態の保存・復元
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        public TreeView TreeViewControl { get; set; }
        public void UpdateTreeView()

        {
            // 展開状態を保存
            var expandedNodes = GetExpandedNodes(TreeViewControl);

            if (CurrentInputModel != null && CTreeViewData != null)
            {
                CTreeViewData.Clear();

                UpdateTreeViewFundamental();
                UpdateTreeViewLoadCases();
                UpdateTreeGroundLayers();
                UpdateTreePileBodies();
                UpdateTreeViewPileLocaiton();
                UpdateTreeViewEmbedment();
                UpdateTreeViewSoilPiles();
                UpdateTreeViewSoilEmbedment();
            }
            // 展開状態を復元
            SetExpandedNodes(TreeViewControl, expandedNodes);
        }

        // 基本設定
        private void UpdateTreeViewFundamental()
        {

            CTreeViewData dataFundamental1 = new()
            {
                Name = "基本設定",
                Children = []
            };

            dataFundamental1.Children.Add(new CTreeViewData
            {
                Name = $"PJ番号:{CurrentInputModel.FundamentalInput.ProjectNo}",
                TextColor = Brushes.Red
            });

            dataFundamental1.Children.Add(new CTreeViewData
            {
                Name = $"PJ名:{CurrentInputModel.FundamentalInput.ProjectName}",
                TextColor = Brushes.Red
            });

            dataFundamental1.Children.Add(new CTreeViewData
            {
                Name = $"基準標高:{CurrentInputModel.FundamentalInput.RefLevel}",
                TextColor = Brushes.Red
            });

            CTreeViewData.Add(dataFundamental1);
        }

        // 荷重条件
        private void UpdateTreeViewLoadCases()
        {
            CTreeViewData dataLoadcombination = new()
            {
                Name = "荷重組み合わせ",
                Children = []
            };

            if (CurrentInputModel.LoadCasesInput.LoadCombinations == null)
            {
                return;
            }

            int i = 0;
            foreach (var loadcombination in CurrentInputModel.LoadCasesInput.LoadCombinations)
            {
                i += 1;
                dataLoadcombination.Children.Add(new CTreeViewData
                {
                    Name = $"<{i}>",
                    Children = new ObservableCollection<CTreeViewData>
                    {
                        new() {
                            Name = $"αL: {loadcombination.Alpha1:N2}"
                        },
                        new() {
                            Name = $"βU: {loadcombination.Beta1:N2}"
                        },
                        new() {
                            Name = $"βL: {loadcombination.Beta2:N2}"
                        }
                    }
                });
            }

            CTreeViewData dataLoadCaseLevel1 = new()
            {
                Name = "レベル1荷重",
                Children = []
            };

            if (CurrentInputModel.LoadCasesInput.LoadCasesLevel1 == null)
            { return; }

            int j = 0;
            foreach (var loadcase1 in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
            {
                j += 1;
                dataLoadCaseLevel1.Children.Add(new CTreeViewData
                {
                    Name = $"<{j}> 荷重名: {loadcase1.LoadName}",
                    Children = [
                        new CTreeViewData { Name = $"適用: {loadcase1.IsApplicable:N2}"},
                        new CTreeViewData { Name = $"作用角: {loadcase1.LoadAngle:N2}"},
                        new CTreeViewData { Name = $"作用点: ({loadcase1.ForceActionPointX:N2}, {loadcase1.ForceActionPointY:N2}, {loadcase1.ForceActionPointX:N2})"},
                        new CTreeViewData { Name = $"上部構造慣性力: {loadcase1.UpperMassForce:N0}kN" },
                        new CTreeViewData { Name = $"基礎構造慣性力: {loadcase1.UpperMassForce:N0}kN" }
                        ]
                });
            }

            CTreeViewData dataLoadCaseLevel2 = new()
            {
                Name = "レベル2荷重",
                Children = []
            };

            if (CurrentInputModel.LoadCasesInput.LoadCasesLevel2 == null)
            { return; }

            j = 0;
            foreach (var loadcase2 in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
            {
                j += 1;
                dataLoadCaseLevel2.Children.Add(new CTreeViewData
                {
                    Name = $"<{j}> 荷重名: {loadcase2.LoadName}",
                    Children = [
                        new CTreeViewData { Name = $"適用: {loadcase2.IsApplicable:N2}"},
                        new CTreeViewData { Name = $"作用角: {loadcase2.LoadAngle:N2}"},
                        new CTreeViewData { Name = $"作用点: ({loadcase2.ForceActionPointX:N2}, {loadcase2.ForceActionPointY:N2}, {loadcase2.ForceActionPointX:N2})"},
                        new CTreeViewData { Name = $"上部構造慣性力: {loadcase2.UpperMassForce:N0}kN" },
                        new CTreeViewData { Name = $"基礎構造慣性力: {loadcase2.FoundationMassForce:N0}kN" }
                        ]
                });
            }
            CTreeViewData.Add(dataLoadcombination);
            CTreeViewData.Add(dataLoadCaseLevel1);
            CTreeViewData.Add(dataLoadCaseLevel2);
        }

        // 地盤
        private void UpdateTreeGroundLayers()
        {
            ObservableCollection<GroundInput> gModel = CurrentInputModel.GroundsInput;
            CTreeViewData dataGround = new()
            {
                Name = "地盤" + $"({gModel.Count})",
                Children = []
            };

            for (int i = 0; i < gModel.Count; i++)
            {
                CTreeViewData groundChildrenGround = new()
                {
                    Name = $"<{i + 1}>{gModel[i].GroundRef}",
                    Children = []
                };

                groundChildrenGround.Children.Add(
                    new CTreeViewData()
                    {
                        Name = $"地表Z: {CurrentInputModel.FundamentalInput.RefLevel}{AddSign(gModel[i].GroundTopAltitude, "N3")}m, "
                    });

                groundChildrenGround.Children.Add(
                    new CTreeViewData()
                    {
                        Name = $"土層({gModel[i].GroundLayers.Count})"
                    });

                groundChildrenGround.Children.Add(
                    new CTreeViewData()
                    {
                        Name = $"土質点({gModel[i].GroundMassesData.Count})"
                    });

                dataGround.Children.Add(groundChildrenGround);
            }
            CTreeViewData.Add(dataGround);
        }

        // 杭体
        private void UpdateTreePileBodies()
        {
            var pbViewModel = CurrentInputModel.PileBodies;

            CTreeViewData dataPileBodies = new()
            {
                Name = "杭体" + $"({pbViewModel.Count})",
                Children = []
            };

            for (int i = 0; i < pbViewModel.Count; i++)
            {
                CTreeViewData groundChildrenPileBody = new()
                {
                    Name = $"<{i + 1}> " + $"{pbViewModel[i].PileBodyRef}, ",
                    Children = []
                };

                if (pbViewModel[i].PileBodySegments.Count > 0)
                {
                    if (pbViewModel[i].PileBodyType != null)
                    {
                        if (pbViewModel[i].PileConstructionType != null)
                        {
                            groundChildrenPileBody.Children.Add(new CTreeViewData()
                            {
                                Name = $"杭工法: {pbViewModel[i].PileConstructionType}",
                            });
                        }

                        if (pbViewModel[i].PileTopType != null)
                        {
                            groundChildrenPileBody.Children.Add(new CTreeViewData()
                            {
                                Name = $"杭頭接合タイプ: {pbViewModel[i].PileTopType}",
                            });
                        }

                        CTreeViewData pileSegmentsData = new()
                        {
                            Name = $"杭体区間: " + $"({pbViewModel[i].PileBodySegments.Count})",
                            Children = []
                        };

                        for (int j = 0; j < pbViewModel[i].PileBodySegments.Count; j++)
                        {
                            CTreeViewData groundgroundChildrenPileBody = new()
                            {
                                Name = $" {j + 1}:" + $"{pbViewModel[i].PileBodySegments[j].PileSection.PileSectionType}" +
                                $"/L=" + $"{pbViewModel[i].PileBodySegments[j].SegmentLength}" + "m / " +
                                $"{pbViewModel[i].PileBodySegments[j].PileSection.PileDescription}"
                            };
                            pileSegmentsData.Children.Add(groundgroundChildrenPileBody);
                        }

                        groundChildrenPileBody.Children.Add(pileSegmentsData);

                        {
                            groundChildrenPileBody.Children.Add(new CTreeViewData()
                            {
                                Name = $"杭先端径: {pbViewModel[i].PileToeDia}mm",
                            });
                        }

                    }
                    dataPileBodies.Children.Add(groundChildrenPileBody);
                }
            }
            CTreeViewData.Add(dataPileBodies);
        }

        //杭位置・軸力
        private void UpdateTreeViewPileLocaiton()
        {
            CTreeViewData datapileLocation = new()
            {
                Name = $"杭配置（配置数：{CurrentInputModel.PileLayoutItems.Count}）",
                Children = []
            };

            for (int i = 0; i < CurrentInputModel.PileLayoutItems.Count; i++)
            {
                datapileLocation.Children.Add(new CTreeViewData()
                {
                    Name = $"<{i + 1}> ",
                });
            }
            CTreeViewData.Add(datapileLocation);
        }

        // 根入部
        private void UpdateTreeViewEmbedment()
        {
            CTreeViewData dataEmbedment = new()
            {
                Name = "根入部" + $"（区間数：{CurrentInputModel.EmbedmentInput.EmbedmentLayersCount}）",
                Children =
                [
                    new CTreeViewData
                    {
                        Name = $"地盤番号: {CurrentInputModel.EmbedmentInput.GroundNo}",
                    },
                    new CTreeViewData
                    {
                        Name = $"下端Z: {CurrentInputModel.FundamentalInput.RefLevel}{AddSign(CurrentInputModel.EmbedmentInput.BottomAltitude,"N3")}m",
                    },
                ],
            };

            CTreeViewData dataEmbementSections = new()
            {
                Name = "根入区間 " + $"({CurrentInputModel.EmbedmentInput.EmbedmentLayersCount})",
                Children = []
            };

            int i = 0;
            foreach (var section in CurrentInputModel.EmbedmentInput.EmbedmentLayers)
            {
                i += 1;
                dataEmbementSections.Children.Add(new CTreeViewData
                {
                    Name = $"<{i}> 厚さ: {AddSign(section.LayerThickness, "N3")}m, "
                    + $"高さ: {AddSign(section.BottomAltitude + section.LayerThickness, "N3")}m"
                    + $"-{AddSign(section.BottomAltitude, "N3")}m, "
                    + $"X: {AddSign(section.X1, "N3")}m-{AddSign(section.X2, "N3")}m, "
                    + $"Y: {AddSign(section.Y1, "N3")}m-{AddSign(section.Y2, "N3")}m"
                });
            }
            dataEmbedment.Children.Add(dataEmbementSections);
            CTreeViewData.Add(dataEmbedment);
        }

        // 地盤杭レベルセット
        private void UpdateTreeViewSoilPiles()
        {
            CTreeViewData dataSoilPiles = new()
            {
                Name = "地盤杭レベルセット " + $"({CurrentInputModel.ElementDivision.SoilPiles.Count})",
                Children = []
            };
            for (int i = 0; i < CurrentInputModel.ElementDivision.SoilPiles.Count; i++)
            {
                dataSoilPiles.Children.Add(new CTreeViewData()
                {
                    Name = $"<{i + 1}> (セット数:)",
                    Children = [
                        new CTreeViewData{
                            Name = $"杭頭Z: {CurrentInputModel.FundamentalInput.RefLevel}{AddSign(CurrentInputModel.ElementDivision.SoilPiles[i].Z,"N3")}m",
                        },
                        new CTreeViewData{
                            Name = $"杭体番号: {CurrentInputModel.ElementDivision.SoilPiles[i].PileBodyNo}",
                        },
                        new CTreeViewData{
                            Name = $"地盤番号: {CurrentInputModel.ElementDivision.SoilPiles[i].GroundNo}",
                        },
                        new CTreeViewData{
                            Name = $"(節点数:{CurrentInputModel.ElementDivision.SoilPiles[i].ZDataItems.Count})",
                        },
                    ]
                });
            }

            CTreeViewData.Add(dataSoilPiles);
        }

        // 杭根入部セット
        private void UpdateTreeViewSoilEmbedment()
        {
            if (CurrentInputModel.ElementDivision.SoilEmbedment != null && CurrentInputModel.ElementDivision.SoilEmbedment.ZDataItems != null)
            {
                CTreeViewData dataSoilEmbedment = new()
                {
                    Name = "杭根入部セット" + $"(節点数:{CurrentInputModel.ElementDivision.SoilEmbedment.ZDataItems.Count})",
                };

                CTreeViewData.Add(dataSoilEmbedment);
            }
        }

        // 展開状態を取得するメソッド
        private static List<string> GetExpandedNodes(ItemsControl parent)
        {
            var expandedNodes = new List<string>();
            if (parent == null)
            {
                return expandedNodes;
            }

            foreach (var item in parent.Items)
            {
                if (item is TreeViewItem treeViewItem && treeViewItem.IsExpanded)
                {
                    expandedNodes.Add(treeViewItem.Header.ToString());
                    expandedNodes.AddRange(GetExpandedNodes(treeViewItem));
                }
                else if (item is CTreeViewData data)
                {
                    var container = (TreeViewItem)parent.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null && container.IsExpanded)
                    {
                        expandedNodes.Add(data.Name);
                        expandedNodes.AddRange(GetExpandedNodes(container));
                    }
                }
            }
            return expandedNodes;
        }

        private static void SetExpandedNodes(ItemsControl parent, List<string> expandedNodes)
        {
            if (parent == null)
            { return; }

            foreach (var item in parent.Items)
            {
                if (item is TreeViewItem treeViewItem)
                {
                    if (expandedNodes.Contains(treeViewItem.Header.ToString()))
                    {
                        treeViewItem.IsExpanded = true;
                        SetExpandedNodes(treeViewItem, expandedNodes);
                    }
                }
                else if (item is CTreeViewData data)
                {
                    var container = (TreeViewItem)parent.ItemContainerGenerator.ContainerFromItem(item);
                    if (container != null && expandedNodes.Contains(data.Name))
                    {
                        container.IsExpanded = true;
                        SetExpandedNodes(container, expandedNodes);
                    }
                }
            }
        }
    }
}

