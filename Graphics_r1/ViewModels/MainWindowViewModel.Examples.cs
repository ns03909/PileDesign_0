using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        // 設計例集3.1
        [RelayCommand]
        private void Example3_1Command()
        {
            // 新規作成
            CurrentFilePath = null;
            UpdateWindowAction?.Invoke();
            UpdateTreeView();

            var groundLayerViewModel = new GroundLayerViewModel(this);
            groundLayerViewModel.Example3_1Command.Execute(null);

            // ここでCurrentInputModelに反映
            CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(20.0, 8.15, -2.0);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = true;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 3092;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 979;
            }
            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(20.0, 8.15, -2.0);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                if (i == 0 || i == 2)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 5287;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 3914;

                }
                else
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 5658;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 3914;
                }
            }

            CurrentInputModel.PileBodies.Clear();

            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[0].PileBodyRef = "(P1)";
            CurrentInputModel.PileBodies[0].PileBodyType = "既製コンクリート杭";
            CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[0].PileConstructionType = "埋込み杭（プレボーリング杭）";
            CurrentInputModel.PileBodies[0].PileToeDia = 700;
            CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[0].SettlePileToeDia = 700;
            CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[0].SettleN = 2;

            CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileDiameter = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "SC杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteOutDia = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteThickness = 100;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarDr = 600;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeTs = 12.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeDia = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarNum = 0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteFc = 85.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileDiameter = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "PRC杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteOutDia = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteThickness = 100;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarDr = 600;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeDia = 700.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarNum = 16;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarSize = "D19";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteFc = 85.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[1].PileBodyRef = "(P1)";
            CurrentInputModel.PileBodies[1].PileBodyType = "既製コンクリート杭";
            CurrentInputModel.PileBodies[1].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[1].PileConstructionType = "埋込み杭（プレボーリング杭）";
            CurrentInputModel.PileBodies[1].PileToeDia = 700;
            CurrentInputModel.PileBodies[1].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[1].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[1].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[1].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[1].SettlePileToeDia = 700;
            CurrentInputModel.PileBodies[1].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[1].SettleN = 2;

            CurrentInputModel.PileBodies[1].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileDiameter = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileSectionType = "SC杭";
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteOutDia = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteThickness = 110;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarDr = 700;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeTs = 12.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeDia = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarNum = 0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteFc = 85.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[1].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileDiameter = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileSectionType = "PRC杭";
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteOutDia = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteThickness = 110;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarDr = 700;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeDia = 800.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarNum = 20;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarSize = "D19";
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteFc = 85.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;
            //CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem());

            //CurrentInputModel.PileLayoutItems[0].PileNo = 1;
            //CurrentInputModel.PileLayoutItems[0].PileBodyNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroundNo = 1;
            //CurrentInputModel.PileLayoutItems[0].SoilPileAltNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroupPileFactor = 1.0;
            //CurrentInputModel.PileLayoutItems[0].PileSpacingFactor = 10;
            //CurrentInputModel.PileLayoutItems[0].X = 18.43;
            //CurrentInputModel.PileLayoutItems[0].Y = 6.90;
            //CurrentInputModel.PileLayoutItems[0].Z = -5.5;
            //CurrentInputModel.PileLayoutItems[0].AxialForceVL0 = 2452.0;

            CurrentInputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
            {
                new()
                {
                    PileNo = 1,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.8,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1445,
                    AxialForceLevel1s=[893.0, 765.0, 1997.0, 2125.0],
                    AxialForceLevel2s=[405.0, 273.0, 2528.0, 2723.0],

                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 10.4,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 2009,
                    AxialForceLevel1s=[1878.0, 1289.0, 2140.0, 2729.0],
                    AxialForceLevel2s=[2016.0, 893.0, 1929.0, 3379.0],
                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.8,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1503,
                    AxialForceLevel1s=[1465.0, 784.0, 1541.0, 2222.0],
                    AxialForceLevel2s=[1538.0, 374.0, 1583.0, 2841.0],
                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 23.2,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1510,
                    AxialForceLevel1s=[1548.0, 791.0, 1472.0, 2229.0],
                    AxialForceLevel2s=[1588.0, 380.0, 1542.0, 2846.0],
                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 21,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 29.6,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 2038,
                    AxialForceLevel1s=[2169, 1318, 1907, 2758],
                    AxialForceLevel2s=[1933, 899, 2024, 3385],
                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 39.2,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1445,
                    AxialForceLevel1s=[1997, 764, 893, 2126],
                    AxialForceLevel2s=[2524, 271, 400, 2723],
                },
                new()
                {
                    PileNo = 7,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 2797,
                    AxialForceLevel1s=[2249, 3477, 3345, 2117],
                    AxialForceLevel2s=[1909, 4004, 3762, 1554],
                },

                new()
                {
                    PileNo = 8,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 10.4,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 3650,
                    AxialForceLevel1s=[3575, 4371, 3726, 2929],
                    AxialForceLevel2s=[3842, 4819, 3458, 2330],
                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.8,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 2874,
                    AxialForceLevel1s=[2845, 3592, 2903, 2156],
                    AxialForceLevel2s=[2958, 4060, 2920, 1595],
                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 23.2,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 2874,
                    AxialForceLevel1s=[2903, 3592, 2845, 2156],
                    AxialForceLevel2s=[2921, 4059, 2958, 1595],
                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 29.6,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 3706,
                    AxialForceLevel1s=[3782, 4427, 3631, 2985],
                    AxialForceLevel2s=[3473, 4833, 3858, 2345],
                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 39.2,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 2839,
                    AxialForceLevel1s=[3387, 3519, 2291, 2159],
                    AxialForceLevel2s=[3773, 4016, 1920, 1565],
                },
            };

            foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.SetMainWindowViewModel(this);
            }

            CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
            //CurrentInputModel.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem
            //{
            //    No = 1,
            //    X1 = 0,
            //    X2 = 36.8,
            //    Y1 = -2.85,
            //    Y2 = 13.75,
            //    LayerThickness = 6.5,
            //    TopAltitude = 0.0,
            //    BottomAltitude = -6.5
            //});
            //CurrentInputModel.EmbedmentInput.BottomAltitude = -6.5;

            //UpdateEmbedment();
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }

        // 関東支部 計算例8
        [RelayCommand]
        private void ExampleK8()
        {
            // 新規作成
            CurrentFilePath = null;
            UpdateWindowAction?.Invoke();
            UpdateTreeView();

            var groundLayerViewModel = new GroundLayerViewModel(this);
            groundLayerViewModel.ExampleK8Command.Execute(null);

            // ここでCurrentInputModelに反映
            CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(10.80, 8.250, -2.0);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = false;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 3282;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 828;
            }
            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(10.80, 8.250, -2.0);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                if(i==0 || i==2)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 6679;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 3310;
                    
                }
                else
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 6794;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 3310;
                }
            }

            CurrentInputModel.PileBodies.Clear();

            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[0].PileBodyRef = "(P1)";
            CurrentInputModel.PileBodies[0].PileBodyType = "場所打ち鉄筋コンクリート杭";
            CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[0].PileConstructionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileToeDia = 1300.0;
            CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[0].SettlePileToeDia = 1300.0;
            CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[0].SettleN = 2;

            CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileDiameter = 1300.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteOutDia = 1300.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteThickness = 650;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarDr = 1000;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarNum = 20;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 18.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 28.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileDiameter = 1300.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteOutDia = 1300.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteThickness = 650;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarDr = 1000;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarNum = 10;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[1].PileBodyRef = "(P2)";
            CurrentInputModel.PileBodies[1].PileBodyType = "場所打ち鉄筋コンクリート杭";
            CurrentInputModel.PileBodies[1].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[1].PileConstructionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[1].PileToeDia = 1300.0;
            CurrentInputModel.PileBodies[1].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[1].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[1].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[1].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[1].SettlePileToeDia = 1300.0;
            CurrentInputModel.PileBodies[1].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[1].SettleN = 2;

            CurrentInputModel.PileBodies[1].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileDiameter = 1300.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteOutDia = 1300.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteThickness = 650;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarDr = 1000;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarNum = 24;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[1].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentLength = 18.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentDepth = 28.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileDiameter = 1300.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteOutDia = 1300.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteThickness = 650;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarDr = 1000;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarNum = 12;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[2].PileBodyRef = "(P3)";
            CurrentInputModel.PileBodies[2].PileBodyType = "場所打ち鉄筋コンクリート杭";
            CurrentInputModel.PileBodies[2].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[2].PileConstructionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[2].PileToeDia = 1300.0;
            CurrentInputModel.PileBodies[2].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[2].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[2].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[2].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[2].SettlePileToeDia = 1300.0;
            CurrentInputModel.PileBodies[2].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[2].SettleN = 2;

            CurrentInputModel.PileBodies[2].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PileDiameter = 1200.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteOutDia = 1200.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteThickness = 600;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarDr = 900;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarNum = 24;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[2].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[2].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentLength = 18.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentDepth = 28.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PileDiameter = 1200.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteOutDia = 1200.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteThickness = 600;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarDr = 900;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarNum = 12;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[3].PileBodyRef = "(P4)";
            CurrentInputModel.PileBodies[3].PileBodyType = "場所打ち鉄筋コンクリート杭";
            CurrentInputModel.PileBodies[3].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[3].PileConstructionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[3].PileToeDia = 1300.0;
            CurrentInputModel.PileBodies[3].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[3].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[3].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[3].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[3].SettlePileToeDia = 1300.0;
            CurrentInputModel.PileBodies[3].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[3].SettleN = 2;

            CurrentInputModel.PileBodies[3].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].SegmentLength = 10.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].SegmentDepth = 10.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PileDiameter = 1100.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteOutDia = 1100.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteThickness = 550;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarDr = 800;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarNum = 24;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteFc = 36.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[3].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[3].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].SegmentLength = 18.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].SegmentDepth = 28.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PileDiameter = 1100.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteOutDia = 1100.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteThickness = 550;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarDr = 800;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarNum = 12;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarSize = "D29";
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteFc = 36.0; 
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;
            //CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem());

            //CurrentInputModel.PileLayoutItems[0].PileNo = 1;
            //CurrentInputModel.PileLayoutItems[0].PileBodyNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroundNo = 1;
            //CurrentInputModel.PileLayoutItems[0].SoilPileAltNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroupPileFactor = 1.0;
            //CurrentInputModel.PileLayoutItems[0].PileSpacingFactor = 10;
            //CurrentInputModel.PileLayoutItems[0].X = 18.43;
            //CurrentInputModel.PileLayoutItems[0].Y = 6.90;
            //CurrentInputModel.PileLayoutItems[0].Z = -5.5;
            //CurrentInputModel.PileLayoutItems[0].AxialForceVL0 = 2452.0;

            CurrentInputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
            {
                new()
                {
                    PileNo = 1,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 4,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1070.0,
                    AxialForceLevel1s=[-142.0, -296.0, 2281.0, 2436.0],
                    AxialForceLevel2s=[-639.0, -775.0, 2773.0, 2931.0],

                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 3,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1930.0,
                    AxialForceLevel1s=[2275.0, 520.0, 1585.0, 3341.0],
                    AxialForceLevel2s=[2313.0, 42.0, 1553.0, 3935.0],
                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 3,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.40,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 2073,
                    AxialForceLevel1s=[1725.0, 424.0, 2420.0, 3722.0],
                    AxialForceLevel2s=[1690.0, -186.0, 2459.0, 4384.0],
                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 4,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 0.0,
                    Z = -2.0,
                    AxialForceVL0 = 1242,
                    AxialForceLevel1s=[2454.0, -125.0, 31.0, 2609.0],
                    AxialForceLevel2s=[2947.0, -608.0, -466.0, 3101.0],
                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 5.7,
                    Z = -2.0,
                    AxialForceVL0 = 2527,
                    AxialForceLevel1s=[1129.0, 2998.0, 3924.0, 2055.0],
                    AxialForceLevel2s=[519.0, 2897.0, 4536.0, 2090.0],
                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 5.7,
                    Z = -2.0,
                    AxialForceVL0 = 3546.0,
                    AxialForceLevel1s=[3813.0, 3890.0, 3280.0, 3203.0],
                    AxialForceLevel2s=[3671.0, 3679.0, 3419.0, 3226.0],
                },

                new()
                {
                    PileNo = 7,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 5.7,
                    Z = -2.0,
                    AxialForceVL0 = 3613.0,
                    AxialForceLevel1s=[3350.0, 4208.0, 3877.0, 3019.0],
                    AxialForceLevel2s=[3492.0, 4128.0, 3732.0, 2970.0],
                },
                new()
                {
                    PileNo = 8,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 5.7,
                    Z = -2.0,
                    AxialForceVL0 = 2599.0,
                    AxialForceLevel1s=[3996.0, 3075.0, 1201.0, 2123.0],
                    AxialForceLevel2s=[4609.0, 2974.0, 592.0, 2160.0],
                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 3,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 16.5,
                    Z = -2.0,
                    AxialForceVL0 = 1789.0,
                    AxialForceLevel1s=[636.0, 2682.0, 2941.0, 895.0],
                    AxialForceLevel2s=[69.0, 3261.0, 3510.0, 365.0],
                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 16.5,
                    Z = -2.0,
                    AxialForceVL0 = 2602.0,
                    AxialForceLevel1s=[2829.0, 3671.0, 2374.0, 1532.0],
                    AxialForceLevel2s=[2827.0, 4361.0, 2374.0, 913.0],
                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 16.5,
                    Z = -2.0,
                    AxialForceVL0 = 2599.0,
                    AxialForceLevel1s=[2371.0, 3651.0, 2827.0, 1548.0],
                    AxialForceLevel2s=[2370.0, 4339.0, 2826.0, 936.0],
                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 3,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 16.5,
                    Z = -2.0,
                    AxialForceVL0 = 1787.0,
                    AxialForceLevel1s=[2940.0, 2679.0, 635.0, 895.0],
                    AxialForceLevel2s=[3508.0, 3264.0, 68.0, 367.0],
                },
            };

            foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.SetMainWindowViewModel(this);
            }

            CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
            //CurrentInputModel.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem
            //{
            //    No = 1,
            //    X1 = 0,
            //    X2 = 36.8,
            //    Y1 = -2.85,
            //    Y2 = 13.75,
            //    LayerThickness = 6.5,
            //    TopAltitude = 0.0,
            //    BottomAltitude = -6.5
            //});
            //CurrentInputModel.EmbedmentInput.BottomAltitude = -6.5;

            //UpdateEmbedment();
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }

            // 基礎指針'19 計算例9
            [RelayCommand]
        private void Example9()
        {
            // 新規作成
            CurrentFilePath = null;
            UpdateWindowAction?.Invoke();
            UpdateTreeView();

            var groundLayerViewModel = new GroundLayerViewModel(this);
            groundLayerViewModel.Example9Command.Execute(null);

            // ここでCurrentInputModelに反映
            CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(18.43, 6.90, -5.5);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = false;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 4010;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 5200;
            }
            for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(18.43, 6.90, -5.5);
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 8025;
                CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 10453;
            }

            CurrentInputModel.PileBodies.Clear();
            CurrentInputModel.PileBodies.Add(new());

            CurrentInputModel.PileBodies[0].PileBodyRef = "(PB1)";
            CurrentInputModel.PileBodies[0].PileBodyType = "場所打ち鉄筋コンクリート杭";
            CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
            CurrentInputModel.PileBodies[0].PileConstructionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileToeDia = 1000.0;
            CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
            CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
            CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
            CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
            CurrentInputModel.PileBodies[0].SettlePileToeDia = 1000.0;
            CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
            CurrentInputModel.PileBodies[0].SettleN = 2;

            CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 11.5;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 11.5;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileDiameter = 1000.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteOutDia = 1000.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteThickness = 500;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarDr = 700;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarNum = 20;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarSize = "D25";
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteFc = 27.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

            CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

            CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 14.7;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 26.2;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileDiameter = 1000.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "場所打ちコンクリート杭";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteOutDia = 1000.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteThickness = 500;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarDr = 700;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeTs = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeDia = 0.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarNum = 12;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarSize = "D25";
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteFc = 27.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
            CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;

            //CurrentInputModel.PileLayoutItems.Add(new PileLayoutDataItem());

            //CurrentInputModel.PileLayoutItems[0].PileNo = 1;
            //CurrentInputModel.PileLayoutItems[0].PileBodyNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroundNo = 1;
            //CurrentInputModel.PileLayoutItems[0].SoilPileAltNo = 1;
            //CurrentInputModel.PileLayoutItems[0].GroupPileFactor = 1.0;
            //CurrentInputModel.PileLayoutItems[0].PileSpacingFactor = 10;
            //CurrentInputModel.PileLayoutItems[0].X = 18.43;
            //CurrentInputModel.PileLayoutItems[0].Y = 6.90;
            //CurrentInputModel.PileLayoutItems[0].Z = -5.5;
            //CurrentInputModel.PileLayoutItems[0].AxialForceVL0 = 2452.0;

            CurrentInputModel.PileLayoutItems = new ObservableCollection<PileLayoutDataItem>
            {
                new()
                {
                    PileNo = 1,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.6,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.375,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.75,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 22.125,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 29.475,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.225,
                    Y = -0.4,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },

                new()
                {
                    PileNo = 7,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.6,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 8,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.375,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.75,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 22.125,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 29.475,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.225,
                    Y = 7.95,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },

                new()
                {
                    PileNo = 13,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.6,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 14,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.375,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 15,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.75,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 16,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 22.125,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 17,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 29.475,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
                new()
                {
                    PileNo = 18,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.225,
                    Y = 13.15,
                    Z = -6.5,
                    AxialForceVL0 = 2452.0,
                },
            };
            foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
            {
                pileLayoutItem.SetMainWindowViewModel(this);
            }

            CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
            CurrentInputModel.EmbedmentInput.EmbedmentLayers.Add(new EmbedmentDataItem
            {
                No = 1,
                X1 = 0,
                X2 = 36.8,
                Y1 = -2.85,
                Y2 = 13.75,
                LayerThickness = 6.5,
                TopAltitude = 0.0,
                BottomAltitude = -6.5
            });
            CurrentInputModel.EmbedmentInput.BottomAltitude = -6.5;

            UpdateEmbedment();
            UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
            UpdateTreeView();
        }
    }
}
