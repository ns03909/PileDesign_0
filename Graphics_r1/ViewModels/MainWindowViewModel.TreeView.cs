using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
                UpdateTreeViewGridLines();
                UpdateTreeViewLoadCases();
                UpdateTreeGroundLayers();
                UpdateTreePileBodies();
                UpdateTreeViewPileLocaiton();
                UpdateTreeViewInputNodes();
                UpdateTreeViewFoundationBeams();
                UpdateTreeViewEmbedment();
                UpdateTreeViewSoilPiles();
                UpdateTreeViewSoilEmbedment();
                UpdateTreeViewPileGroupSettlement();
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
                Name = $"標高記号:{CurrentInputModel.FundamentalInput.RefLevel}",
                TextColor = Brushes.Red
            });

            dataFundamental1.Children.Add(new CTreeViewData
            {
                Name = $"Z=0 絶対標高:{CurrentInputModel.FundamentalInput.ReferenceAltitude:N3}m",
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
                        Name = $"地表Z: {AddSign(gModel[i].GroundTopAltitude, "N3")}m, "
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
                        Name = $"下端Z: {AddSign(CurrentInputModel.EmbedmentInput.BottomAltitude,"N3")}m",
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
                            Name = $"杭頭Z: {AddSign(CurrentInputModel.ElementDivision.SoilPiles[i].Z,"N3")}m",
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

        // 通り心
        private void UpdateTreeViewGridLines()
        {
            var gridX = CurrentInputModel.GridXItems;
            var gridY = CurrentInputModel.GridYItems;

            int xCount = gridX?.Count ?? 0;
            int yCount = gridY?.Count ?? 0;

            if (xCount == 0 && yCount == 0)
                return;

            CTreeViewData dataGridLines = new()
            {
                Name = $"通り心（X:{xCount}, Y:{yCount}）",
                Children = []
            };

            CTreeViewData dataGridX = new()
            {
                Name = $"X方向（{xCount}）",
                Children = []
            };
            if (gridX != null)
            {
                foreach (var item in gridX)
                {
                    dataGridX.Children.Add(new CTreeViewData
                    {
                        Name = $"<{item.No}> {item.Name}: {item.Coord:N3}m"
                    });
                }
            }
            dataGridLines.Children.Add(dataGridX);

            CTreeViewData dataGridY = new()
            {
                Name = $"Y方向（{yCount}）",
                Children = []
            };
            if (gridY != null)
            {
                foreach (var item in gridY)
                {
                    dataGridY.Children.Add(new CTreeViewData
                    {
                        Name = $"<{item.No}> {item.Name}: {item.Coord:N3}m"
                    });
                }
            }
            dataGridLines.Children.Add(dataGridY);

            CTreeViewData.Add(dataGridLines);
        }

        // 一般節点
        private void UpdateTreeViewInputNodes()
        {
            var nodes = CurrentInputModel.InputNodes;
            if (nodes == null || nodes.Count == 0)
                return;

            CTreeViewData dataInputNodes = new()
            {
                Name = $"一般節点（{nodes.Count}）",
                Children = []
            };

            int i = 0;
            foreach (var node in nodes)
            {
                i++;
                string typeStr = node.Type == NodeType.Pile ? "杭" : "一般";
                dataInputNodes.Children.Add(new CTreeViewData
                {
                    Name = $"<{i}> No:{node.No} ({typeStr}) X:{node.X:N3}, Y:{node.Y:N3}, Z:{node.Z:N3}"
                });
            }

            CTreeViewData.Add(dataInputNodes);
        }

        // 一般梁要素
        private void UpdateTreeViewFoundationBeams()
        {
            var fbInput = CurrentInputModel.FoundationBeamInput;
            if (fbInput == null)
                return;

            int matCount = fbInput.Materials?.Count ?? 0;
            int secCount = fbInput.Sections?.Count ?? 0;
            int beamCount = fbInput.Beams?.Count ?? 0;

            if (matCount == 0 && secCount == 0 && beamCount == 0)
                return;

            string modeStr = fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody
                ? "剛体" : "剛床";

            CTreeViewData dataFB = new()
            {
                Name = $"一般梁要素（接続:{modeStr}, 材料:{matCount}, 断面:{secCount}, 梁:{beamCount}）",
                Children = []
            };

            string modeDetail = fbInput.ConnectionMode == FoundationBeamConnectionMode.RigidBody
                ? "剛体連結" : "剛床連結";
            dataFB.Children.Add(new CTreeViewData
            {
                Name = $"接続モード: {modeDetail}"
            });

            if (matCount > 0)
            {
                CTreeViewData dataMaterials = new()
                {
                    Name = $"材料（{matCount}）",
                    Children = []
                };
                foreach (var mat in fbInput.Materials)
                {
                    dataMaterials.Children.Add(new CTreeViewData
                    {
                        Name = $"<{mat.No}> {mat.Name}: E={mat.YoungModulus:E2} kN/m²"
                    });
                }
                dataFB.Children.Add(dataMaterials);
            }

            if (secCount > 0)
            {
                CTreeViewData dataSections = new()
                {
                    Name = $"断面（{secCount}）",
                    Children = []
                };
                foreach (var sec in fbInput.Sections)
                {
                    dataSections.Children.Add(new CTreeViewData
                    {
                        Name = $"<{sec.No}> {sec.Name}: {sec.Width:N3}m×{sec.Height:N3}m"
                    });
                }
                dataFB.Children.Add(dataSections);
            }

            if (beamCount > 0)
            {
                CTreeViewData dataBeams = new()
                {
                    Name = $"梁要素（{beamCount}）",
                    Children = []
                };
                foreach (var beam in fbInput.Beams)
                {
                    string nodeIStr = CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeI_Type, beam.NodeI_Id);
                    string nodeJStr = CurrentInputModel.GetNodeReferenceDisplayString(beam.NodeJ_Type, beam.NodeJ_Id);
                    dataBeams.Children.Add(new CTreeViewData
                    {
                        Name = $"<{beam.No}> 材料:{beam.MaterialNo}, 断面:{beam.SectionNo}, {nodeIStr}→{nodeJStr}"
                    });
                }
                dataFB.Children.Add(dataBeams);
            }

            CTreeViewData.Add(dataFB);
        }

        // 群杭沈下
        private void UpdateTreeViewPileGroupSettlement()
        {
            var pgs = CurrentInputModel.PileGroupSettlement;
            if (pgs == null)
                return;

            int rectCount = pgs.RectLoads?.Count ?? 0;
            int soilLayerCount = pgs.SettlementSoilLayers?.Count ?? 0;

            if (pgs.LoadingType == "なし" && rectCount == 0 && soilLayerCount == 0)
                return;

            CTreeViewData dataPGS = new()
            {
                Name = "群杭沈下",
                Children = []
            };

            dataPGS.Children.Add(new CTreeViewData
            {
                Name = $"荷重タイプ: {pgs.LoadingType ?? "未設定"}"
            });

            dataPGS.Children.Add(new CTreeViewData
            {
                Name = $"載荷面標高: {pgs.LoadingPlaneAltitude:N3}m"
            });

            dataPGS.Children.Add(new CTreeViewData
            {
                Name = $"矩形荷重（{rectCount}）"
            });

            dataPGS.Children.Add(new CTreeViewData
            {
                Name = $"沈下地盤層（{soilLayerCount}）"
            });

            CTreeViewData.Add(dataPGS);
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

