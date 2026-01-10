using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using PileDesign.Views;
using PileDesign.Common;

namespace PileDesign.ViewModels
{
    public partial class GroundLayerViewModel : ObservableObject, ICloseable

    {
        public ChangWindow ChangWindowInstance { get; internal set; }

        private void InitializeExampleItems()
        {
            ExampleItems.Clear();

            // ä˘ë∂ÇÃÉRÉ}ÉìÉhñºÇ…çáÇÌÇπÇƒìoò^ÇµÇ‹Ç∑ÅiGroundLayerViewModelExamples.cs Ç…íËã`çœÇ›ÇÃëOíÒÅj
            ExampleItems.Add(new ExampleItem("äÓëbéwêj'19åvéZó·1", Example1Command));
            ExampleItems.Add(new ExampleItem("äÓëbéwêj'19åvéZó·2", Example2Command));
            ExampleItems.Add(new ExampleItem("äÓëbéwéﬂ'19åvéZó·9", Example9Command)); // ï\é¶ñºÇÃåÎéöÇ™Ç†ÇÍÇŒçáÇÌÇπÇƒÇ≠ÇæÇ≥Ç¢
            ExampleItems.Add(new ExampleItem("ê›åvó·èW3.1", Example3_1Command));
            ExampleItems.Add(new ExampleItem("ê›åvó·èW3.2", Example3_2Command));
            ExampleItems.Add(new ExampleItem("ê›åvó·èW3.3", Example3_3Command));
            ExampleItems.Add(new ExampleItem("ê›åvó·èW3.4", Example3_4Command));
            ExampleItems.Add(new ExampleItem("ä÷ìåéxïî8èÕ", ExampleK8Command));
            ExampleItems.Add(new ExampleItem("î™èdèFìÒíöñ⁄No.1", ExampleYeasu2Command));
        }

        // äÓëbéwêj'19 åvéZó·1
        [RelayCommand]
        private void Example1()
        {
            GroundInput.GroundRef = "'19Ex1";
            GroundInput.GroundWaterGLDepth = -2.0;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;
            GroundInput.GroundLayers = [];
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 0,
                BottomGLDepth = -2.0,
                LayerThickness = 2.0,
                BottomAltitude = -2.0,
                Name = "As1",
                GranularityClass = "çªéøìy",
                Density = 17.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 8.0,
                Cohesive = 0.0,
                Vs = 170.0,
                Es = 5600.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -11.0,
                LayerThickness = 9.0,
                BottomAltitude = -11.0,
                Name = "As2",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 11.0,
                Cohesive = 0.0,
                Vs = 180.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -16.0,
                LayerThickness = 5.0,
                BottomAltitude = -16.0,
                Name = "Ac1",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.5,
                Cohesive = 100.0,
                Vs = 160.0,
                Es = 5000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -25.0,
                LayerThickness = 9.0,
                BottomAltitude = -25.0,
                Name = "Ds1",
                GranularityClass = "çªéøìy",
                Density = 20.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 44.0,
                Cohesive = 0.0,
                Vs = 340.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.0,
                NValue = 8.0,
                Fc = 25.0,
                VS0 = 250.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -2.0,
                NValue = 3.0,
                Fc = 31.0,
                VS0 = 250.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -3.0,
                NValue = 2.0,
                Fc = 8.0,
                VS0 = 250.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -4.0,
                NValue = 2.0,
                Fc = 5.0,
                VS0 = 250.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -5.0,
                NValue = 12.0,
                Fc = 4.0,
                VS0 = 250.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -6.0,
                NValue = 10.0,
                Fc = 3.0,
                VS0 = 250.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -7.0,
                //Spacing = 1.0,
                NValue = 15.0,
                Fc = 5.0,
                VS0 = 250.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -8.0,
                //Spacing = 1.0,
                NValue = 10.0,
                Fc = 22.0,
                VS0 = 250.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -9.0,
                //Spacing = 1.0,
                NValue = 15.0,
                Fc = 15.0,
                VS0 = 250.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -10.0,
                //Spacing = 1.0,
                NValue = 22.0,
                Fc = 7.0,
                VS0 = 250.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -11.0,
                //Spacing = 1.0,
                NValue = 23.0,
                Fc = 5.0,
                VS0 = 250.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -12.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 95.0,
                VS0 = 250.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -13.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 93.0,
                VS0 = 250.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -14.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 92.0,
                VS0 = 250.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -15.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -16.0,
                //Spacing = 1.0,
                NValue = 16.0,
                Fc = 63.0,
                VS0 = 250.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -17.0,
                //Spacing = 1.0,
                NValue = 42.0,
                Fc = 3.0,
                VS0 = 250.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -18.0,
                //Spacing = 1.0,
                NValue = 45.0,
                Fc = 2.0,
                VS0 = 250.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -19.0,
                //Spacing = 1.0,
                NValue = 38.0,
                Fc = 12.0,
                VS0 = 250.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -20.0,
                //Spacing = 1.0,
                NValue = 50.0,
                Fc = 3.0,
                VS0 = 250.0,
            });

            Update();
        }

        // äÓëbéwêj'19 åvéZó·2
        [RelayCommand]
        private void Example2()
        {
            GroundInput.GroundRef = "'19Ex2";
            GroundInput.GroundWaterGLDepth = -1.5;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.BedrockDensity = 20;
            GroundInput.BedrockShearWaveVelocity = 400;
            GroundInput.ShallowSoilType = "îSê´ìy";

            GroundInput.GroundLayers = [];
            //1
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 0,
                //LayerThickness = 0.8,
                BottomGLDepth = -0.8,
                Name = "ñÑìy",
                GranularityClass = "çªéøìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                NValue = 2.0,
                Cohesive = 0.0,
                Vs = 90.0,
                Es = 1400

            });
            //2
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 0.7,
                BottomGLDepth = -1.5,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                NValue = 2.0,
                Cohesive = 30.0,
                Vs = 90.0,
                Es = 1400

            });
            //3
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.0,
                BottomGLDepth = -2.5,
                Name = "ÉVÉãÉgç¨Ç∂ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                NValue = 6.0,
                Cohesive = 0.0,
                Vs = 120.0,
                Es = 4200

            });
            //4
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 6.0,
                BottomGLDepth = -8.5,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                NValue = 22.8,
                Cohesive = 0.0,
                Vs = 170.0,
                Es = 15960

            });
            //5
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 9.0,
                BottomGLDepth = -17.5,
                Name = "ÉVÉãÉg1",
                GranularityClass = "îSê´ìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                NValue = 2.7,
                Cohesive = 50.0,
                Vs = 120.0,
                Es = 5630
            });
            //6
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 4.15,
                BottomGLDepth = -21.65,
                Name = "ÉVÉãÉg2",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                NValue = 2.0,
                Cohesive = 50.0,
                Vs = 120.0,
                Es = 5280
            });

            //6
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 3.95,
                BottomGLDepth = -25.6,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                NValue = 3.8,
                Cohesive = 40.0,
                Vs = 160.0,
                Es = 9390
            });
            //7
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.8,
                BottomGLDepth = -27.4,
                Name = "ÉVÉãÉgéøîSìy",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                NValue = 6.0,
                Cohesive = 40.0,
                Vs = 200.0,
                Es = 14700
            });
            //8
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.90,
                BottomGLDepth = -29.3,
                Name = "çdéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "ç^êœëw",
                NValue = 9.5,
                Cohesive = 150.0,
                Vs = 200.0,
                Es = 14700
            });
            //9
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.10,
                BottomGLDepth = -30.4,
                Name = "ÉVÉãÉgéøîSìy",
                GranularityClass = "îSê´ìy",
                Density = 15.0,
                AgeCategory = "ç^êœëw",
                NValue = 22.0,
                Cohesive = 200.0,
                Vs = 400.0,
                Es = 58700,
                IsEngineeringBedrock = true
            });
            //10
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 2.30,
                BottomGLDepth = -32.7,
                Name = "îSìyç¨Ç∂ÇËçª‚I",
                GranularityClass = "‚Iéøìy",
                Density = 20.0,
                AgeCategory = "ç^êœëw",
                NValue = 60.0,
                Cohesive = 0.0,
                Vs = 400.0,
                Es = 42000,
                IsEngineeringBedrock = true
            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 0.8,
                GLDepth = -0.8,
                NValue = 2.0,
                //Fc = 25.0,
                VS0 = 90.0,
                Density = 15.0,
                Fc = 30.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {


                GLDepth = -1.5,
                NValue = 2.0,
                //Fc = 31.0,
                VS0 = 90.0,
                Density = 15.0,
                Fc = 80.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -2.5,
                //Spacing = 1.0,
                NValue = 6.0,
                //Fc = 31.0,
                VS0 = 120.0,
                Density = 16.0,
                Fc = 30.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -3.5,
                //Spacing = 1.0,
                NValue = 21.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 18.0,
                Fc = 10.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -4.5,
                //Spacing = 1.0,
                NValue = 25.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 18.0,
                Fc = 10.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -5.5,
                //Spacing = 1.0,
                NValue = 29.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 18.0,
                Fc = 10.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -6.5,
                //Spacing = 1.0,
                NValue = 25.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 18.0,
                Fc = 10.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -7.5,
                //Spacing = 1.0,
                NValue = 19.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 18.0,
                Fc = 10.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -8.5,
                //Spacing = 1.0,
                NValue = 18.0,
                //Fc = 5.0,
                Density = 18.0,
                VS0 = 170.0,
                Fc = 10.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -9.6,
                //Spacing = 1.1,
                NValue = 16.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -10.5,
                //Spacing = 0.9,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -11.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -12.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -13.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -14.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -15.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -16.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -17.5,
                //Spacing = 1.0,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 16.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -18.5,
                //Spacing = 1.0,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -19.55,
                //Spacing = 1.05,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -20.6,
                //Spacing = 1.05,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -21.65,
                //Spacing = 1.05,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -22.7,
                //Spacing = 1.05,
                NValue = 4.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -23.6,
                //Spacing = 0.90,
                NValue = 4.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -24.6,
                //Spacing = 1.00,
                NValue = 5.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 160.0,
            });
            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -25.6,
                //Spacing = 1.00,
                NValue = 5.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -26.6,
                //Spacing = 1.00,
                NValue = 7.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -27.4,
                //Spacing = 0.80,
                NValue = 9.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -28.2,
                //Spacing = 0.80,
                NValue = 10.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -29.3,
                //Spacing = 1.10,
                NValue = 22.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -30.4,
                //Spacing = 1.10,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 15.0,
                VS0 = 400.0,
                Fc = 80.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -31.2,
                //Spacing = 0.80,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 20.0,
                VS0 = 400.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -32.0,
                //Spacing = 0.80,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 20.0,
                VS0 = 400.0,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -32.7,
                Spacing = 0.70,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 20.0,
                VS0 = 400.0,
            });

            Update();
        }

        // äÓëbéwêj'19 åvéZó·9
        [RelayCommand]
        private void Example9()
        {
            GroundInput.GroundRef = "'19Ex2";
            GroundInput.GroundWaterGLDepth = -1.5;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.BedrockDensity = 19.6;
            GroundInput.BedrockShearWaveVelocity = 400;
            GroundInput.ShallowSoilType = "îSê´ìy";

            GroundInput.GroundLayers = [];
            //1
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 0,
                //LayerThickness = 0.8,
                BottomGLDepth = -0.8,
                Name = "ñÑìy",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "â´êœëw",
                NValue = 2.0,
                Cohesive = 28.4,
                Vs = 90.0,
                Es = 1400

            });
            //2
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 0.7,
                BottomGLDepth = -1.5,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "â´êœëw",
                NValue = 6.0,
                Cohesive = 28.4,
                Vs = 90.0,
                Es = 1400

            });
            //3
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.0,
                BottomGLDepth = -2.5,
                Name = "ÉVÉãÉgç¨Ç∂ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 15.7,
                AgeCategory = "â´êœëw",
                NValue = 21.0,
                Cohesive = 0.0,
                Vs = 120.0,
                Es = 4200

            });
            //4
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 6.0,
                BottomGLDepth = -8.5,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 17.7,
                AgeCategory = "â´êœëw",
                NValue = 19.4,
                Cohesive = 0.0,
                Vs = 170.0,
                Es = 15600

            });
            //5
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 9.0,
                BottomGLDepth = -17.5,
                Name = "ÉVÉãÉg1",
                GranularityClass = "îSê´ìy",
                Density = 15.7,
                AgeCategory = "â´êœëw",
                NValue = 1.1,
                Cohesive = 28.4,
                Vs = 120.0,
                Es = 1140
            });
            //6
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 4.15,
                BottomGLDepth = -21.65,
                Name = "ÉVÉãÉg2",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "â´êœëw",
                NValue = 2.5,
                Cohesive = 28.4,
                Vs = 120.0,
                Es = 5630
            });

            //6
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 3.95,
                BottomGLDepth = -25.6,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "â´êœëw",
                NValue = 5.2,
                Cohesive = 52.0,
                Vs = 160.0,
                Es = 5280
            });
            //7
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.8,
                BottomGLDepth = -27.4,
                Name = "ÉVÉãÉgéøîSìy",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "â´êœëw",
                NValue = 9.0,
                Cohesive = 137.5,
                Vs = 200.0,
                Es = 14700
            });
            //8
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.90,
                BottomGLDepth = -29.3,
                Name = "çdéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "ç^êœëw",
                NValue = 9.5,
                Cohesive = 137.5,
                Vs = 200.0,
                Es = 58700
            });
            //9
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 1.10,
                BottomGLDepth = -30.4,
                Name = "ÉVÉãÉgéøîSìy",
                GranularityClass = "îSê´ìy",
                Density = 14.7,
                AgeCategory = "ç^êœëw",
                NValue = 60.0,
                Cohesive = 137.5,
                Vs = 400.0,
                Es = 70200,
                IsEngineeringBedrock = true
            });
            //10
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //LayerThickness = 2.30,
                BottomGLDepth = -35.0,
                Name = "îSìyç¨Ç∂ÇËçª‚I",
                GranularityClass = "‚Iéøìy",
                Density = 19.6,
                AgeCategory = "ç^êœëw",
                NValue = 60.0,
                Cohesive = 0.0,
                Vs = 400.0,
                Es = 70200,
                IsEngineeringBedrock=true
            });

            GroundInput.GroundMassesData = [];
            //1
            //GroundInput.GroundMassesData.Add(new GroundMassDataInput
            //{
            //    //Spacing = 0.8,
            //    GLDepth = -0.8,
            //    NValue = 2.0,
            //    //Fc = 25.0,
            //    VS0 = 90.0,
            //    Density = 14.7,
            //    Fc = 80.0,
            //});
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 0.70,
                GLDepth = -0.8,
                NValue = 2.0,
                //Fc = 31.0,
                VS0 = 90.0,
                Density = 14.7,
                Fc = 80.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.5,
                NValue = 2.0,
                //Fc = 31.0,
                VS0 = 90.0,
                Density = 14.7,
                Fc = 80.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -2.5,
                NValue = 6.0,
                //Fc = 31.0,
                VS0 = 120.0,
                Density = 15.7,
                Fc = 80.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -3.5,
                NValue = 25.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 17.7,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -4.5,
                NValue = 29.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 17.7,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -5.5,
                NValue = 25.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 17.7,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -6.5,
                NValue = 19.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 17.7,
            });
            //8a
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 0.5,
                GLDepth = -7.0,
                NValue = 19.0,
                //Fc = 5.0,
                VS0 = 170.0,
                Density = 17.7,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 0.5,
                GLDepth = -7.5,
                NValue = 18.0,
                //Fc = 5.0,
                Density = 17.7,
                VS0 = 170.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -8.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 17.7,
                VS0 = 120.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -9.6,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 17.7,
                VS0 = 120.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -10.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -11.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -12.5,
                NValue = 1.0,
                //Fc = 5.0,
                VS0 = 120.0,
                Density = 15.7,
                Fc = 80.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -13.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -14.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -15.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -16.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -17.5,
                NValue = 1.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -18.5,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -19.6,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -20.6, ///////////// ämîF 
                NValue = 2.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -21.65,
                NValue = 2.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -22.7,
                NValue = 4.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -23.6,
                NValue = 4.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -24.6,
                NValue = 5.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -25.6,
                NValue = 5.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 160.0,
                Fc = 80.0,
            });
            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -27.4,
                NValue = 9.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -28.2,
                NValue = 10.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -29.3,
                NValue = 22.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -30.4,
                NValue = 22.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 200.0,
                Fc = 80.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -31.2,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -32,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -32.7,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });

            Update();
            //35
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -34.0,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });

            Update();
        }

        // ê›åvó·èW3.1
        [RelayCommand]
        private void Example3_1()
        {
            GroundInput.GroundRef = "ê›åvó·èW3.1";
            GroundInput.GroundTopAltitude = 2.40;
            GroundInput.GroundWaterGLDepth = -2.0;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;
            GroundInput.GroundLayers = [];
            // 1 ñÑìy
            double layerThickness;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = -2.0,
                LayerThickness = 2.0,
                BottomAltitude = 0.4,
                Name = "ñÑìy",
                GranularityClass = "çªéøìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.0,
                Cohesive = 0.0,
                Vs = 90.0,
                Es = 5600.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            // 2 ÉVÉãÉgç¨Ç∂ÇËç◊çª
            layerThickness = 0.6;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ÉVÉãÉgç¨Ç∂ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 7.0,
                Cohesive = 0.0,
                Vs = 120.0,
                Es = 4000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 3 çªéøÉVÉãÉg
            layerThickness = 1.0;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 1,
                Cohesive = 30.0,
                Vs = 90.0,
                Es = 5000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 ÉVÉãÉgç¨Ç∂ÇËç◊çª
            layerThickness = 2.4;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ÉVÉãÉgç¨Ç∂ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 15.0,
                Cohesive = 0.0,
                Vs = 180.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 5 çªç¨Ç∂ÇËÉVÉãÉg
            layerThickness = 1.80;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "çªç¨Ç∂ÇËÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2,
                Cohesive = 40.0,
                Vs = 160.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 6 çªéøÉVÉãÉg
            layerThickness = 1.70;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 3.0,
                Cohesive = 40.0,
                Vs = 160,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 7 îSìyç¨Ç∂ÇËç◊çª
            layerThickness = 7.80;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "îSìyç¨Ç∂ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true,

            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.3,
                NValue = 2.0,
                Fc = 25.0,
                VS0 = 90.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 7.0,
                Fc = 31.0,
                VS0 = 120.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 70.0,
                VS0 = 90.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 13.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 17.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 160.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 160.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 160.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 8.0,
                Fc = 70.0,
                VS0 = 160.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 36.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 52.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 67.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 100.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 120.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 129.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 180.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 200.0,
                Fc = 10.0,
                VS0 = 380.0,
            });
        }





        // ê›åvó·èW3.3
        [RelayCommand]
        private void Example3_2()
        {
            GroundInput.GroundRef = "ê›åvó·èW3.2";
            GroundInput.GroundTopAltitude = 0;
            GroundInput.GroundWaterGLDepth = -1.4;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;
            GroundInput.GroundLayers = [];

            // 1 ê∑ìy
            double layerThickness;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = -2.2,
                LayerThickness = 2.2,
                BottomAltitude = -2.2,
                Name = "ê∑ìy",
                GranularityClass = "çªéøìy",
                Density = 15.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 12.0,
                Cohesive = 0.0,
                Vs = 130.0,
                Es = 5600.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            // 2 ç◊çª
            layerThickness = 11.75;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 16.0,
                Cohesive = 20.0,
                Vs = 230.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 3 ÉVÉãÉg
            layerThickness = 13.15;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.0,
                Cohesive = 20.0,
                Vs = 180.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 ÉVÉãÉgç¨Ç∂ÇËçª
            layerThickness = 2.60;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ÉVÉãÉgç¨Ç∂ÇËçª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 5.0,
                Cohesive = 20.0,
                Vs = 210.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 ç◊çª
            layerThickness = 4.65;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 20.0,
                Cohesive = 20.0,
                Vs = 320.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 5 çª‚I
            layerThickness = 4.03;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "çª‚I",
                GranularityClass = "‚Iéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 20.0,
                Vs = 460.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });


            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.1,
                NValue = 12.0,
                Fc = 5.0,
                VS0 = 130.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 15.0,
                Fc = 5.0,
                VS0 = 130.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 12.0,
                Fc = 21.0,
                VS0 = 230.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 16.0,
                Fc = 20.0,
                VS0 = 230.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 11.0,
                Fc = 35.0,
                VS0 = 230.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 13.0,
                Fc = 31.0,
                VS0 = 230.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 21.0,
                VS0 = 230.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 19.0,
                VS0 = 230.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 20.0,
                Fc = 20.0,
                VS0 = 230.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 21.0,
                VS0 = 180.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 35.0,
                VS0 = 180.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 16.0,
                Fc = 15.0,
                VS0 = 180.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 17.0,
                Fc = 26.0,
                VS0 = 180.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 4.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 10.0,
                VS0 = 180.0,
            });


            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 180.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 180.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 10.0,
                VS0 = 180.0,
            });
            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 10.0,
                VS0 = 180.0,
            });

            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 210.0,
            });
            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 210.0,
            });
            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 8.0,
                Fc = 10.0,
                VS0 = 210.0,
            });
            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 17.0,
                Fc = 10.0,
                VS0 = 320.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 12.0,
                Fc = 10.0,
                VS0 = 320.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 14.0,
                Fc = 10.0,
                VS0 = 320.0,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 40.0,
                Fc = 10.0,
                VS0 = 320.0,
            });
            //35
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 30.0,
                Fc = 10.0,
                VS0 = 320.0,
            });
            //36
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 10.0,
                VS0 = 460.0,
            });
            //37
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 10.0,
                VS0 = 460.0,
            });
            //38
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 10.0,
                VS0 = 460.0,
            });
            //39
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 10.0,
                VS0 = 460.0,
            });
        }



        // ê›åvó·èW3.3
        [RelayCommand]
        private void Example3_3()
        {
            GroundInput.GroundRef = "ê›åvó·èW3.3";
            GroundInput.GroundTopAltitude = 0;
            GroundInput.GroundWaterGLDepth = -1.7;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;
            GroundInput.GroundLayers = [];
            // 1 ‚Iç¨Ç∂ÇËçª
            double layerThickness;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = -2.0,
                LayerThickness = 0.6,
                BottomAltitude = 0.6,
                Name = "As1",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.0,
                Cohesive = 0.0,
                Vs = 200.0,
                Es = 5600.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            // 2 çªéøîSìy
            layerThickness = 1.75;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac1",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 7.0,
                Cohesive = 20.0,
                Vs = 200.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 3 ÉVÉãÉgç¨Ç∂ÇËçª
            layerThickness = 1.55;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "As2",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 1,
                Cohesive = 0.0,
                Vs = 200.0,
                Es = 5000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 çªéøîSìy
            layerThickness = 1.6;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac2",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 15.0,
                Cohesive = 50.0,
                Vs = 200.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 5 ÉVÉãÉgéøîSìy
            layerThickness = 3.30;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac3",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2,
                Cohesive = 50.0,
                Vs = 240.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 6 çª‚I
            layerThickness = 5.00;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ag1",
                GranularityClass = "‚Iéøìy",
                Density = 20.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 3.0,
                Cohesive = 0.0,
                Vs = 320,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 7 ÉVÉãÉgéøîSìy
            layerThickness = 8.00;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc1",
                GranularityClass = "îSê´ìy",
                Density = 16.7,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 50.0,
                Vs = 320.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 8 çªç¨Ç∂ÇËîSìyéøÉVÉãÉg
            layerThickness = 0.90;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc2",
                GranularityClass = "îSê´ìy",
                Density = 20.6,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 200.0,
                Vs = 320.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 8 çªéøÉVÉãÉg
            layerThickness = 1.00;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc3",
                GranularityClass = "îSê´ìy",
                Density = 16.7,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 200.0,
                Vs = 280.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });


            // 8 ‚Iç¨Ç∂ÇËçª
            layerThickness = 0.90;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds1",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 9 çª‚I
            layerThickness = 1.65;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dg1",
                GranularityClass = "‚Iéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 10 çª
            layerThickness = 0.15;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds2",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });


            // 11 çª‚I
            layerThickness = 3.90;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dg2",
                GranularityClass = "‚Iéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 12 ÉVÉãÉgéøîSìy
            layerThickness = 6.40;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc4",
                GranularityClass = "îSê´ìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 500.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });


            // 13 çªéøÉVÉãÉg
            layerThickness = 0.55;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc5",
                GranularityClass = "îSê´ìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 500.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 14 ÉVÉãÉgç¨Ç∂ÇËçª
            layerThickness = 1.30;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds3",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true

            });

            // 14 çªç¨Ç∂ÇËîSìy
            layerThickness = 0.35;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc6",
                GranularityClass = "îSê´ìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 500.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true

            });


            // 15 çª‚I
            layerThickness = 1.10;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dg3",
                GranularityClass = "‚Iéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 14 çªç¨Ç∂ÇËîSìy
            layerThickness = 0.80;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc2",
                GranularityClass = "îSê´ìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 500.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true

            });

            // 15 íÜçª
            layerThickness = 1.10;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds4",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 380.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.6,
                NValue = 11.0,
                Fc = 28.0,
                VS0 = 250.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 12.0,
                Fc = 12.4,
                VS0 = 250.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 12.0,
                Fc = 12.4,
                VS0 = 250.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 7.0,
                Fc = 43.9,
                VS0 = 250.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 10.0,
                Fc = 43.9,
                VS0 = 250.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 7.0,
                Fc = 43.9,
                VS0 = 250.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 30.0,
                Fc = 12.0,
                VS0 = 250.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 26.0,
                Fc = 9.7,
                VS0 = 250.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 25.0,
                Fc = 9.7,
                VS0 = 250.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 31.0,
                Fc = 9.7,
                VS0 = 250.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 27.0,
                Fc = 9.7,
                VS0 = 250.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 10.0,
                Fc = 98.9,
                VS0 = 250.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 12.0,
                Fc = 98.9,
                VS0 = 250.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 12.0,
                Fc = 98.9,
                VS0 = 250.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 11.0,
                Fc = 98.9,
                VS0 = 250.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 12.0,
                Fc = 99.5,
                VS0 = 250.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 11.0,
                Fc = 99.5,
                VS0 = 250.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 10.0,
                Fc = 99.5,
                VS0 = 250.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 20.0,
                Fc = 99.5,
                VS0 = 250.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 58.0,
                Fc = 99.5,
                VS0 = 250.0,
            });

            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 99.5,
                VS0 = 250.0,
            });

            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 12.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 88.0,
                VS0 = 250.0,
            });


            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 15.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 18.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 35.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
        }


        // ê›åvó·èW3.4
        [RelayCommand]
        private void Example3_4()
        {
            GroundInput.GroundRef = "ê›åvó·èW3.4";
            GroundInput.GroundTopAltitude = 0;
            GroundInput.GroundWaterGLDepth = -2.4;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;
            GroundInput.GroundLayers = [];
            // 1 ñÑìy
            double layerThickness;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = -2.15,
                LayerThickness = 2.15,
                BottomAltitude = -2.15,
                Name = "As1",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.0,
                Cohesive = 0.0,
                Vs = 200.0,
                Es = 5600.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            // 2 îSìyéøÉVÉãÉg
            layerThickness = 0.65;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac1",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 7.0,
                Cohesive = 25,
                Vs = 200.0,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 3 çª
            layerThickness = 6.10;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "As2",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 1,
                Cohesive = 0,
                Vs = 200.0,
                Es = 5000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 ÉVÉãÉg
            layerThickness = 29.60;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac2",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 15.0,
                Cohesive = 80,
                Vs = 200.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 5 çª
            layerThickness = 1.20;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "As2",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2,
                Cohesive = 0.0,
                Vs = 240.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 6 îSìy
            layerThickness = 1.65;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ac1",
                GranularityClass = "îSê´ìy",
                Density = 20.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 3.0,
                Cohesive = 100,
                Vs = 320,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 7 ÉVÉãÉgç¨Ç∂ÇËçª
            layerThickness = 1.25;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds1",
                GranularityClass = "çªéøìy",
                Density = 16.7,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 320.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 8 îSìy
            layerThickness = 0.80;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Dc1",
                GranularityClass = "îSê´ìy",
                Density = 20.6,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 1200,
                Vs = 320.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 9 çª
            layerThickness = 6.60;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "Ds1",
                GranularityClass = "çªéøìy",
                Density = 16.7,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = false,
                NValue = 100.0,
                Cohesive = 0.0,
                Vs = 280.0,
                Es = 30800.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -1.0,
                NValue = 10.0,
                Fc = 30.0,
                VS0 = 250.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 30.0,
                VS0 = 250.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 3.0,
                Fc = 30.0,
                VS0 = 250.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 8.0,
                Fc = 24.0,
                VS0 = 250.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 13.0,
                Fc = 12.0,
                VS0 = 250.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 11.0,
                Fc = 18.0,
                VS0 = 250.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 10.0,
                Fc = 23.0,
                VS0 = 250.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 7.0,
                Fc = 31.0,
                VS0 = 250.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });


            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 250.0,
            });

            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 88.0,
                VS0 = 250.0,
            });

            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 3.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //35
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 30.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //36
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 21.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //37
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 31.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //38
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 17.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //39
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 53.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //40
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 49.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //41
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //42
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //43
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
            //44
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 60.0,
                Fc = 88.0,
                VS0 = 250.0,
            });
        }


        // ä÷ìåéxïî8
        [RelayCommand]
        private void ExampleK8()
        {
            GroundInput.GroundRef = "ä÷ìåéxïî8";
            GroundInput.GroundTopAltitude = 0;
            GroundInput.GroundWaterGLDepth = -2.4;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.GroundAcceleration1 = 2.0;

            GroundInput.ShallowSoilType = "îSê´ìy";
            GroundInput.GroundLayers = [];

            // 0 ñÑìy
            double layerThickness;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = -1.7,
                LayerThickness = 1.7,
                BottomAltitude = -1.7,
                Name = "ï\ìy(îSê´ìy)",
                GranularityClass = "îSê´ìy",
                Density = 17.3,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 4.5,
                Cohesive = 0.0,
                Vs = 90.0,
                Es = 1000.0,
                IsPositiveCircumResistance = false,
                IsNegativeCircumResistance = false
            });

            // 1 îSìyéø
            layerThickness = 2.9;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "è„ïîóLäyí¨ëwçªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 17.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 1.0,
                Cohesive = 5.0,
                Vs = 90,
                Es = 1000.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 2 îSìyéø
            layerThickness = 5.25;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "â∫ïîóLäyí¨ëwÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.3,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 1.0,
                Cohesive = 5.0,
                Vs = 90,
                Es = 1500.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 3 îSìyéø
            layerThickness = 15.95;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "â∫ïîóLäyí¨ëwçªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.6,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 2.0,
                Cohesive = 5.0,
                Vs = 130,
                Es = 4200.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 4 îSìyéø
            layerThickness = 2.1;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ñÑê›ÉçÅ[ÉÄëwÉVÉãÉgéøîSìy",
                GranularityClass = "îSê´ìy",
                Density = 16.5,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 6.0,
                Cohesive = 10.0,
                Vs = 150,
                Es = 16100.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 5 çªéøéø
            layerThickness = 0.9;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ñÑê›íiãu‚Iëwç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                IsEngineeringBedrock = false,
                NValue = 23.0,
                Cohesive = 0.0,
                Vs = 320,
                Es = 54600.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 6 çª‚I
            layerThickness = 5.3;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ñÑê›íiãu‚Iëw",
                GranularityClass = "‚Iéøìy",
                Density = 21.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 78,
                Cohesive = 0.0,
                Vs = 430,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 7 ç◊çª
            layerThickness = 2.6;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ìåãûëwç◊çª",
                GranularityClass = "çªéøìy",
                Density = 19.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 39,
                Cohesive = 0.0,
                Vs = 370,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            // 8 ÉVÉãÉgéøç◊çª
            layerThickness = 3.6;
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = GroundInput.GroundLayers.Count + 1,
                BottomGLDepth = GroundInput.GroundLayers[^1].BottomGLDepth - layerThickness,
                LayerThickness = layerThickness,
                BottomAltitude = GroundInput.GroundLayers[^1].BottomAltitude - layerThickness,
                Name = "ìåãûëäÉVÉãÉgéøç◊çª",
                GranularityClass = "çªéøìy",
                Density = 19.0,
                AgeCategory = "ç^êœëw",
                IsEngineeringBedrock = true,
                NValue = 150.0,
                Cohesive = 0.0,
                Vs = 470,
                Es = 7700.0,
                IsPositiveCircumResistance = true,
                IsNegativeCircumResistance = true
            });

            GroundInput.GroundMassesData = [];
            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = -2.0,
                NValue = 5.0,
                Fc = 30.0,
                VS0 = 90.0,
            });

            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 0.7,
                NValue = 5.0,
                Fc = 30.0,
                VS0 = 90.0,
            });

            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 30.0,
                VS0 = 90.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 24.0,
                VS0 = 90.0,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 12.0,
                VS0 = 90.0,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                //Spacing = 1.0,
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                NValue = 1.0,
                Fc = 18.0,
                VS0 = 90.0,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 23.0,
                VS0 = 90.0,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.25,
                //Spacing = 1.0,
                NValue = 1.0,
                Fc = 31.0,
                VS0 = 90.0,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 90.0,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 2.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 0.95,
                //Spacing = 1.0,
                NValue = 2.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 6.0,
                Fc = 70.0,
                VS0 = 130.0,
            });

            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.1,
                //Spacing = 1.0,
                NValue = 6.0,
                Fc = 70.0,
                VS0 = 150.0,
            });

            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 0.90,
                //Spacing = 1.0,
                NValue = 23,
                Fc = 70.0,
                VS0 = 150.0,
            });


            //28 (29.8)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.2,
                //Spacing = 1.0,
                NValue = 23.0,
                Fc = 70.0,
                VS0 = 320.0,
            });

            //29 (31.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.2,
                //Spacing = 1.0,
                NValue = 78.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //30 (32.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 78.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //31 (33.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 78.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //32 (34.1)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.1,
                //Spacing = 1.0,
                NValue = 78.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //33 (35.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 0.9,
                //Spacing = 1.0,
                NValue = 39.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //34 (36.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 39.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //35 (36.7)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 0.7,
                //Spacing = 1.0,
                NValue = 39.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //36 (38.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.3,
                //Spacing = 1.0,
                NValue = 150.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //37 (39.0)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.0,
                //Spacing = 1.0,
                NValue = 150.0,
                Fc = 70.0,
                VS0 = 430.0,
            });

            //38 (40.3)
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = GroundInput.GroundMassesData[^1].GLDepth - 1.3,
                //Spacing = 1.0,
                NValue = 150.0,
                Fc = 70.0,
                VS0 = 430.0,
            });
        }


        // î™èdèFìÒíöñ⁄
        [RelayCommand]
        private void ExampleYeasu2()
        {
            GroundInput.GroundRef = "'Yaesu2";
            GroundInput.GroundTopAltitude = 3.836;

            GroundInput.GroundWaterGLDepth = -15.0;
            TextBoxGroundWaterGLDepth_LostFocus();

            GroundInput.StressGLDepth = 0.0;
            TextBoxStressGLDepth_LostFocus();

            GroundInput.BedrockDensity = 20.0;
            GroundInput.BedrockShearWaveVelocity = 450;
            GroundInput.ShallowSoilType = "îSê´ìy";

            GroundInput.GroundLayers = [];


            //1
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 0,
                BottomGLDepth = -7.05,
                Name = "ñÑìy",
                GranularityClass = "çªéøìy",
                Density = 15.6,
                AgeCategory = "â´êœëw",
                NValue = 6.0,
                Cohesive = 0.0,
                Vs = 170.0

            });
            //2
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -8.75,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 19.2,
                AgeCategory = "â´êœëw",
                NValue = 15.0,
                Cohesive = 0.0,
                Vs = 290.0

            });
            //3
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -9.95,
                Name = "íÜçª",
                GranularityClass = "çªéøìy",
                Density = 16.7,
                AgeCategory = "â´êœëw",
                NValue = 21.0,
                Cohesive = 0.0,
                Vs = 240.0

            });
            //3a
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -14.5,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 17.9,
                AgeCategory = "â´êœëw",
                NValue = 24.0,
                Cohesive = 0.0,
                Vs = 290.0

            });
            //4
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -16.65,
                Name = "ÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 16.3,
                AgeCategory = "â´êœëw",
                NValue = 16.0,
                Cohesive = 80.0,
                Vs = 230.0

            });
            //5
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -17.7,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 18.0,
                AgeCategory = "â´êœëw",
                NValue = 10.0,
                Cohesive = 80.0,
                Vs = 330.0
            });
            //6
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -20.05,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.8,
                AgeCategory = "ç^êœëw",
                NValue = 21.0,
                Cohesive = 0.0,
                Vs = 380.0
            });

            //7
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -20.8,
                Name = "çª‚I",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "ç^êœëw",
                NValue = 30.0,
                Cohesive = 0.0,
                Vs = 390.0
            });
            //8
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -23.9,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.8,
                AgeCategory = "ç^êœëw",
                NValue = 84.0,
                Cohesive = 0.0,
                Vs = 390.0
            });
            //9
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -29.95,
                Name = "ÉVÉãÉgç¨ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.6,
                AgeCategory = "ç^êœëw",
                NValue = 68.0,
                Cohesive = 0.0,
                Vs = 410.0,
                IsEngineeringBedrock=true
            });
            //10
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -30.5,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 18.7,
                AgeCategory = "ç^êœëw",
                NValue = 62.0,
                Cohesive = 200.0,
                Vs = 420.0,
                IsEngineeringBedrock = true
            });
            //11
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -32.35,
                Name = "ÉVÉãÉgç¨ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.4,
                AgeCategory = "ç^êœëw",
                NValue = 51.0,
                Cohesive = 0.0,
                Vs = 390.0,
                IsEngineeringBedrock = true
            });
            //12
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -35.0,
                Name = "çªéøÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 18.9,
                AgeCategory = "ç^êœëw",
                NValue = 59.0,
                Cohesive = 200.0,
                Vs = 460.0,
                IsEngineeringBedrock = true,

            });
            //13
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -36.4,
                Name = "ÉVÉãÉgç¨ÇËç◊çª",
                GranularityClass = "çªéøìy",
                Density = 19.1,
                AgeCategory = "ç^êœëw",
                NValue = 56.0,
                Cohesive = 0.0,
                Vs = 520.0,
                IsEngineeringBedrock = true,
            });
            //14
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -37.9,
                Name = "çªç¨ÇËÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 18.6,
                AgeCategory = "ç^êœëw",
                NValue = 90.0,
                Cohesive = 200.0,
                Vs = 460.0,
                IsEngineeringBedrock = true,
            });
            //15
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -38.6,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 18.0,
                AgeCategory = "ç^êœëw",
                NValue = 95.0,
                Cohesive = 0.0,
                Vs = 470.0,
                IsEngineeringBedrock = true,
            });
            //16
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -41.4,
                Name = "çªç¨ÇËÉVÉãÉg",
                GranularityClass = "îSê´ìy",
                Density = 18.9,
                AgeCategory = "ç^êœëw",
                NValue = 56.0,
                Cohesive = 200.0,
                Vs = 440.0,
                IsEngineeringBedrock = true,
            });

            //17
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -53.0,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                Density = 19.8,
                AgeCategory = "ç^êœëw",
                NValue = 120.0,
                Cohesive = 0.0,
                Vs = 410.0,
                IsEngineeringBedrock = true,
            });
            //18
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -53.95,
                Name = "‚Iç¨Ç∂ÇËíÜçª",
                GranularityClass = "çªéøìy",
                Density = 20.2,
                AgeCategory = "ç^êœëw",
                Vs = 410.0,
                IsEngineeringBedrock = true,
            });
            //19
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                BottomGLDepth = -58.2,
                Name = "ç◊çª",
                GranularityClass = "çªéøìy",
                NValue = 145.0,
                AgeCategory = "ç^êœëw",
                Cohesive = 0.0,
                Density = 19.8,
                Vs = 410.0,
                IsEngineeringBedrock = true,
            });
            //20
            GroundInput.GroundLayers.Add(new GroundLayerInput
            {
                No = 1,
                //BottomGLDepth = -58.83,
                BottomGLDepth = -61.00,
                Name = "‚Iç¨Ç∂ÇËíÜçª",
                GranularityClass = "çªéøìy",
                AgeCategory = "ç^êœëw",
                Cohesive = 0.0,
                Density = 20.2,
                Vs = 400.0,
                IsEngineeringBedrock = true,
            });

            GroundInput.GroundMassesData = [];

            //1
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -1.10,
                NValue = 4.0,
                //Fc = 25.0,
                VS0 = 170.0,
                Density = 14.7,
                Fc = 80.0,
            });
            //2
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -1.10,
                NValue = 3.0,
                //Fc = 31.0,
                VS0 = 170.0,
                Density = 14.7,
                Fc = 80.0,
            });
            //3
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -3.335,
                NValue = 2.0,
                //Fc = 31.0,
                VS0 = 170.0,
                Density = 15.7,
                Fc = 80.0,
            });
            //4
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -4.32,
                NValue = 5.0,
                //Fc = 5.0,
                VS0 = 140.0,
                Density = 17.7,
            });
            //5
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -5.3,
                NValue = 8.0,
                //Fc = 5.0,
                VS0 = 140.0,
                Density = 17.7,
            });
            //6
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -6.65,
                NValue = 15.0,
                //Fc = 5.0,
                VS0 = 530.0,
                Density = 17.7,
            });
            //7
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -7.8,
                NValue = 15.0,
                //Fc = 5.0,
                VS0 = 290.0,
                Density = 17.7,
            });
            //8
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -8.8,
                NValue = 21.0,
                //Fc = 5.0,
                VS0 = 240.0,
                Density = 17.7,
            });
            //9
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -9.8,
                NValue = 21.0,
                //Fc = 5.0,
                VS0 = 290.0,
                Density = 17.7,
            });
            //10
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -10.8,
                NValue = 17.0,
                //Fc = 5.0,
                Density = 17.7,
                VS0 = 230.0,
            });
            //11
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -11.85,
                NValue = 19.0,
                //Fc = 5.0,
                Density = 17.7,
                VS0 = 240,
            });
            //12
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -12.8,
                NValue = 31.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 240,
                Fc = 80.0,
            });
            //13
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -13.8,
                NValue = 29.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 240,
                Fc = 80.0,
            });
            //14
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -14.8,
                NValue = 23.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 240,
                Fc = 80.0,
            });
            //15
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -15.8,
                NValue = 9.0,
                //Fc = 5.0,
                VS0 = 240,
                Density = 15.7,
                Fc = 80.0,
            });
            //16
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -16.8,
                NValue = 10.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 120.0,
                Fc = 80.0,
            });
            //17
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -17.8,
                NValue = 12.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 340,
                Fc = 80.0,
            });
            //18
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -18.8,
                NValue = 29.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 340,
                Fc = 80.0,
            });
            //19
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -20.15,
                NValue = 30.0,
                //Fc = 5.0,
                Density = 15.7,
                VS0 = 450,
                Fc = 80.0,
            });
            //20
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -21.215,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 420,
                Fc = 80.0,
            });
            //21
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -22.3,
                NValue = 53.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 370,
                Fc = 80.0,
            });
            //22
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -23.3,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 390,
                Fc = 80.0,
            });
            //23
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -24.255,
                NValue = 90.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 430,
                Fc = 80.0,
            });
            //24
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -25.35,
                NValue = 37.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 400,
                Fc = 80.0,
            });
            //25
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -26.245,
                NValue = 95.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 380,
                Fc = 80.0,
            });
            //26
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -27.295,
                NValue = 62.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 410,
            });
            //27
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -28.29,
                NValue = 64.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 420,
                Fc = 80.0,
            });
            //28
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -29.3,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 400,
                Fc = 80.0,
            });
            //29
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -30.295,
                NValue = 62.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 420,
                Fc = 80.0,
            });
            //30
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -31.3,
                NValue = 51.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 390,
                Fc = 80.0,
            });
            //31
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -32.3,
                NValue = 51.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 430,
                Fc = 80.0,
            });
            //32
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -33.3,
                NValue = 48.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 450,
                Fc = 80.0,
            });
            //33
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -34.28,
                NValue = 69.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 470,
            });
            //34
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -35.3,
                NValue = 55.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 480,
            });
            //35
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -36.3,
                NValue = 56.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 420,
            });
            //36
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -37.25,
                NValue = 90.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 520,
            });
            //37
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -38.245,
                NValue = 95.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 480,
                Fc = 80.0,
            });
            //38
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -39.785,
                NValue = 45.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 420,
                Fc = 80.0,
            });
            //39
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -40.8,
                NValue = 67,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 520,
                Fc = 80.0,
            });
            //40
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -41.8,
                NValue = 50.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 480,
                Fc = 80.0,
            });
            //41
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -42.8,
                NValue = 57.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 450,
                Fc = 80.0,
            });
            //42
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -43.8,
                NValue = 60.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 470,
                Fc = 80.0,
            });
            //43
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -44.75,
                NValue = 55.00,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 440,
            });
            //44
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -45.73,
                NValue = 90.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 440,
            });
            //45
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -46.72,
                NValue = 113.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 350,
            });
            //46
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -47.715,
                NValue = 129.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 370,
            });
            //47
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -48.7,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 390,
                Fc = 80.0,
            });
            //48
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -49.69,
                NValue = 180.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 410,
                Fc = 80.0,
            });
            //49
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -50.695,
                NValue = 225.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 430,
                Fc = 80.0,
            });
            //50
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -51.72,
                NValue = 200.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 450,
                Fc = 80.0,
            });
            //51
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -52.705,
                NValue = 129.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 440,
                Fc = 80.0,
            });
            //52
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -53.705,
                NValue = 164.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 450,
                Fc = 80.0,
            });
            //53
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -54.715,
                NValue = 164.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 430,
            });
            //54
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -55.72,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });
            //55
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -56.715,
                NValue = 129.0,
                //Fc = 5.0,
                Density = 19.6,
                VS0 = 400.0,
            });
            //56
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -57.71,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 370,
            });
            //57
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -58.715,
                NValue = 150.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 400,
                Fc = 80,
            });
            //58
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -59.715,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 400,
                Fc = 80.0,
            });
            //59
            GroundInput.GroundMassesData.Add(new GroundMassDataInput
            {
                GLDepth = -60.69,
                NValue = 138.0,
                //Fc = 5.0,
                Density = 14.7,
                VS0 = 400,
                Fc = 80.0,
            });
            Update();
        }
    }
}
