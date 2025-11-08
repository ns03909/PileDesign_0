using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Windows.Media.Media3D;

namespace PileDesign.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
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
