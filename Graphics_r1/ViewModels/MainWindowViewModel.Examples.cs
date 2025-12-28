using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Threading.Tasks; // 先頭の using に追加すること

namespace PileDesign.ViewModels
{

    public partial class MainWindowViewModel : ObservableObject
    {

        // 再入防止フラグ（partial クラス内に置く）
        private bool _isExampleRunning = false;

        // 共通の開始チェック（呼び出し側は true の場合のみ処理を続行）
        private bool TryStartExample(string exampleName)
        {
            if (_isExampleRunning)
            {
                Debug.WriteLine($"{exampleName}: reentrancy prevented.");
                return false;
            }
            _isExampleRunning = true;
            Debug.WriteLine($"{exampleName}: start");
            return true;
        }

        // 共通の終了処理（例外でも finally で必ず呼ぶ）
        private void EndExample(string exampleName)
        {
            _isExampleRunning = false;
            Debug.WriteLine($"{exampleName}: end");
        }


        // 設計例集3.1
        [RelayCommand]
        private async Task Example3_1()
        {
            if (!TryStartExample(nameof(Example3_1))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

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
                var section00 = CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection;
                section00.PileSectionType = "SC杭";
                section00.SetSelectedPrecastPileByName("SC-700-標準-105-12");

                CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 10.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 10.0;
                var section01 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section01.PileSectionType = "PRC杭";
                section01.SetSelectedPrecastPileByName("SCPRC-700-105-Ⅲ");

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
                var section10 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section10.PileSectionType = "SC杭";
                section10.SetSelectedPrecastPileByName("SC-800-標準-105-12");

                CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[1].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentLength = 10.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentDepth = 10.0;
                var section11 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section11.PileSectionType = "PRC杭";
                section11.SetSelectedPrecastPileByName("CPRC-800-105-Ⅲ");

                CurrentInputModel.PileLayoutItems =
                [
                    new()
                {
                    PileNo = 1,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
                    PileSpacingFactor = 10,
                    X = 0.8,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
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
                    GroupPileFactor = 0.981,
                    PileSpacingFactor = 10,
                    X = 39.2,
                    Y = 11.0,
                    Z = -2.0,
                    AxialForceVL0 = 2839,
                    AxialForceLevel1s=[3387, 3519, 2291, 2159],
                    AxialForceLevel2s=[3773, 4016, 1920, 1565],
                },
            ];

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 11.0, Spacing = 11.0, Name = "Y2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 16.3, Spacing = 5.3, Name = "Y3" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0.8, Spacing = 0.8, Name = "X1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 10.4, Spacing = 9.6, Name = "X2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 16.8, Spacing = 6.4, Name = "X3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 23.2, Spacing = 6.4, Name = "X4" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 29.6, Spacing = 6.4, Name = "X5" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 39.2, Spacing = 9.6, Name = "X6" });

                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }


                // Elements を「なし」に、PileGroupSettlement を「なし」にする処理
                // Elements を安全に空にする
                if (CurrentInputModel.Elements != null)
                {
                    CurrentInputModel.Elements.Clear();
                }
                else
                {
                    CurrentInputModel.Elements = new System.Collections.ObjectModel.ObservableCollection<Element>();
                }

                // PileGroupSettlement の中身を安全にクリア（null にすると他コードで NRE になる恐れがあるため中身を削る）
                if (CurrentInputModel.PileGroupSettlement != null)
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads?.Clear();
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                    CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = 0.0;
                }

                // 根入れ
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例3.1を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {   
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_1));
            }
        }

        // 設計例集3.2
        [RelayCommand]
        private async Task Example3_2()
        {
            if (!TryStartExample(nameof(Example3_2))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // 新規作成
                CurrentFilePath = null;
                UpdateWindowAction?.Invoke();
                UpdateTreeView();

                var groundLayerViewModel = new GroundLayerViewModel(this);
                groundLayerViewModel.Example3_2Command.Execute(null);

                // ここでCurrentInputModelに反映
                CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(16.4, 12.6, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = true;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 4734;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 1188;
                }
                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(16.4, 12.6, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 8285;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 4751;

                }

                CurrentInputModel.PileBodies.Clear();

                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[0].PileBodyRef = "(P1)";
                CurrentInputModel.PileBodies[0].PileBodyType = "既製コンクリート杭";
                CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[0].PileConstructionType = "埋込み杭（プレボーリング杭）";
                CurrentInputModel.PileBodies[0].PileToeDia = 500;
                CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[0].SettlePileToeDia = 700;
                CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[0].SettleN = 2;

                CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 7.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 7.0;

                var section00 = CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection;
                section00.PileSectionType = "PHC杭";
                section00.SetSelectedPrecastPileByName("PHC-500-標準-85-C");

                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileDiameter = 500.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "PHC杭";
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteOutDia = 500.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteThickness = 100;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarDr = 400;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeTs = 0.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeDia = 500.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarNum = 0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarSize = "D29";
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteFc = 85.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileLayoutItems =
                [
                    new()
                {
                    PileNo = 1,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 791,
                    AxialForceLevel1s = [-224, -214, 224, 214],
                    AxialForceLevel2s = [-336, -321, 336, 321],


                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 814,
                    AxialForceLevel1s = [-33, -66, 33, 66],
                    AxialForceLevel2s = [-50, -99, 50, 99],

                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 800,
                    AxialForceLevel1s = [-3, -69, 3, 69],
                    AxialForceLevel2s = [-5, -104, 5, 104],

                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 820,
                    AxialForceLevel1s = [3, -75, -3, 75],
                    AxialForceLevel2s = [5, -113, -5, 113],

                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 21,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 28.8,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 851,
                    AxialForceLevel1s = [33, -82, -33, 82],
                    AxialForceLevel2s = [50, -123, -50, 123],

                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 793,
                    AxialForceLevel1s = [224, -126, -224, 126],
                    AxialForceLevel2s = [336, -189, -336, 189],

                },
                new()
                {
                    PileNo = 7,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1554,
                    AxialForceLevel1s = [-406, -316, 406, 316],
                    AxialForceLevel2s = [-609, -474, 609, 474],

                },

                new()
                {
                    PileNo = 8,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1767,
                    AxialForceLevel1s = [-73, -164, 73, 164],
                    AxialForceLevel2s = [-110, -246, 110, 246],

                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1839,
                    AxialForceLevel1s = [-30, -305, 30, 305],
                    AxialForceLevel2s = [-45, -458, 45, 458],

                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1817,
                    AxialForceLevel1s = [30, -312, -30, 312],
                    AxialForceLevel2s = [45, -468, -45, 468],

                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 28.8,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1781,
                    AxialForceLevel1s = [56, -153, -56, 153],
                    AxialForceLevel2s = [84, -230, -84, 230],

                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.0,
                    Y = 7.2,
                    Z = -1.4,
                    AxialForceVL0 = 1721,
                    AxialForceLevel1s = [397, -365, -397, 365],
                    AxialForceLevel2s = [596, -548, -596, 548],

                },
                                new()
                {
                    PileNo = 13,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1512,
                    AxialForceLevel1s = [-346, -521, 346, 521],
                    AxialForceLevel2s = [-519, -782, 519, 782],

                },
                new()
                {
                    PileNo = 14,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1651,
                    AxialForceLevel1s = [-211, -491, 211, 491],
                    AxialForceLevel2s = [-317, -737, 317, 737],

                },
                new()
                {
                    PileNo = 15,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1818,
                    AxialForceLevel1s = [-65, -519, 65, 519],
                    AxialForceLevel2s = [-98, -779, 98, 779],

                },
                new()
                {
                    PileNo = 16,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1741,
                    AxialForceLevel1s = [60, -495, -60, 495],
                    AxialForceLevel2s = [90, -743, -90, 743],

                },
                new()
                {
                    PileNo = 17,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 28.8,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1607,
                    AxialForceLevel1s = [210, -491, -210, 491],
                    AxialForceLevel2s = [315, -737, -315, 737],

                },
                new()
                {
                    PileNo = 18,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.0,
                    Y = 14.4,
                    Z = -1.4,
                    AxialForceVL0 = 1734,
                    AxialForceLevel1s = [347, -489, -347, 489],
                    AxialForceLevel2s = [521, -734, -521, 734],

                },
                new()
                {
                    PileNo = 19,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1508,
                    AxialForceLevel1s = [-407, 255, 407, -255],
                    AxialForceLevel2s = [-611, 383, 611, -383],

                },
                new()
                {
                    PileNo = 20,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1692,
                    AxialForceLevel1s = [-215, 158, 215, -158],
                    AxialForceLevel2s = [-323, 257, 323, -257],

                },
                new()
                {
                    PileNo = 21,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1793,
                    AxialForceLevel1s = [-4, 836, 4, -836],
                    AxialForceLevel2s = [-6, 1254, 6, -1254],

                },
                new()
                {
                    PileNo = 22,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1618,
                    AxialForceLevel1s = [54, 914, -54, -914],
                    AxialForceLevel2s = [81, 1371, -81, -1371],

                },
                new()
                {
                    PileNo = 23,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 28.8,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1490,
                    AxialForceLevel1s = [229, 506, -229, -506],
                    AxialForceLevel2s = [344, 759, -344, -759],

                },
                new()
                {
                    PileNo = 24,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 36.0,
                    Y = 21.6,
                    Z = -1.4,
                    AxialForceVL0 = 1629,
                    AxialForceLevel1s = [425, 1046, -425, -1046],
                    AxialForceLevel2s = [638, 1569, -638, -1569],

                },
                new()
                {
                    PileNo = 25,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 28.8,
                    Z = -1.4,
                    AxialForceVL0 = 793,
                    AxialForceLevel1s = [-206, 522, 206, -522],
                    AxialForceLevel2s = [-309, 783, 309, -783],

                },
                new()
                {
                    PileNo = 26,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 28.8,
                    Z = -1.4,
                    AxialForceVL0 = 1559,
                    AxialForceLevel1s = [-13, 515, 13, -515],
                    AxialForceLevel2s = [-20, 773, 20, -773],

                },
                new()
                {
                    PileNo = 27,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 28.8,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
            ];

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 7.2, Spacing = 7.2, Name = "Y2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 14.4, Spacing = 7.2, Name = "Y3" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 21.6, Spacing = 7.2, Name = "Y4" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 28.8, Spacing = 7.2, Name = "Y5" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0.0, Spacing = 0.0, Name = "X1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7.2, Spacing = 7.2, Name = "X2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14.4, Spacing = 7.2, Name = "X3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 21.6, Spacing = 7.2, Name = "X4" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 28.8, Spacing = 7.2, Name = "X5" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 36.0, Spacing = 7.2, Name = "X6" });


                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }


                // Elements を「なし」に、PileGroupSettlement を「なし」にする処理
                // Elements を安全に空にする
                if (CurrentInputModel.Elements != null)
                {
                    CurrentInputModel.Elements.Clear();
                }
                else
                {
                    CurrentInputModel.Elements = new System.Collections.ObjectModel.ObservableCollection<Element>();
                }

                // PileGroupSettlement の中身を安全にクリア（null にすると他コードで NRE になる恐れがあるため中身を削る）
                if (CurrentInputModel.PileGroupSettlement != null)
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads?.Clear();
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                    CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = 0.0;
                }

                // 根入れ
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例3.2を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_2));
            }
        }


        // 設計例集3.3
        [RelayCommand]
        private async Task Example3_3()
        {
            if (!TryStartExample(nameof(Example3_3))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // 新規作成
                CurrentFilePath = null;
                UpdateWindowAction?.Invoke();
                UpdateTreeView();

                var groundLayerViewModel = new GroundLayerViewModel(this);
                groundLayerViewModel.Example3_3Command.Execute(null);

                // ここでCurrentInputModelに反映
                CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                {
                    //CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(16.4, 12.6, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = false;
                    //CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 4734;
                    //CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 1188;
                }
                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(26.6, 15.3, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 16495;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 19592;

                }

                CurrentInputModel.PileBodies.Clear();

                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[0].PileBodyRef = "(P1)";
                CurrentInputModel.PileBodies[0].PileBodyType = "場所打ち鉄筋コンクリート杭";
                CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[0].PileConstructionType = "場所打ちコンクリート杭";
                CurrentInputModel.PileBodies[0].PileToeDia = 2500;
                CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[0].SettlePileToeDia = 2500;
                CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[0].SettleN = 2;

                CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 9.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 9.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarNum = 28;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.MainBarSize = "D32";
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

                CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 12.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 21.0;
                //CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarNum = 24;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.MainBarSize = "D29";
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[1].PileBodyRef = "(P2)";
                CurrentInputModel.PileBodies[1].PileBodyType = "場所打ち鉄筋コンクリート杭";
                CurrentInputModel.PileBodies[1].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[1].PileConstructionType = "場所打ちコンクリート杭";
                CurrentInputModel.PileBodies[1].PileToeDia = 2300;
                CurrentInputModel.PileBodies[1].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[1].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[1].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[1].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[1].SettlePileToeDia = 2300;
                CurrentInputModel.PileBodies[1].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[1].SettleN = 2;

                CurrentInputModel.PileBodies[1].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentLength = 9.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentDepth = 9.0;
                //CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarNum = 26;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.MainBarSize = "D32";
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

                CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[1].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentLength = 12.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentDepth = 21.0;
                //CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarNum = 24;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.MainBarSize = "D29";
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[2].PileBodyRef = "(P3)";
                CurrentInputModel.PileBodies[2].PileBodyType = "場所打ち鉄筋コンクリート杭";
                CurrentInputModel.PileBodies[2].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[2].PileConstructionType = "場所打ちコンクリート杭";
                CurrentInputModel.PileBodies[2].PileToeDia = 1900;
                CurrentInputModel.PileBodies[2].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[2].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[2].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[2].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[2].SettlePileToeDia = 1900;
                CurrentInputModel.PileBodies[2].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[2].SettleN = 2;

                CurrentInputModel.PileBodies[2].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentLength = 9.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentDepth = 9.0;
                //CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarNum = 28;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.MainBarSize = "D32";
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

                CurrentInputModel.PileBodies[2].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[2].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentLength = 12.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentDepth = 21.0;
                //CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarNum = 24;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.MainBarSize = "D29";
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[3].PileBodyRef = "(P4)";
                CurrentInputModel.PileBodies[3].PileBodyType = "場所打ち鉄筋コンクリート杭";
                CurrentInputModel.PileBodies[3].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[3].PileConstructionType = "場所打ちコンクリート杭";
                CurrentInputModel.PileBodies[3].PileToeDia = 1900;
                CurrentInputModel.PileBodies[3].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[3].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[3].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[3].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[3].SettlePileToeDia = 1900;
                CurrentInputModel.PileBodies[3].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[3].SettleN = 2;

                CurrentInputModel.PileBodies[3].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].SegmentLength = 9.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].SegmentDepth = 9.0;
                //CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarNum = 26;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.MainBarSize = "D32";
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

                CurrentInputModel.PileBodies[3].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[3].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].SegmentLength = 12.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].SegmentDepth = 21.0;
                //CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PileDiameter = 1900;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteOutDia = 1900;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteThickness = 950;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarDr = 1600;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarNum = 24;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.MainBarSize = "D29";
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[4].PileBodyRef = "(P5)";
                CurrentInputModel.PileBodies[4].PileBodyType = "場所打ち鉄筋コンクリート杭";
                CurrentInputModel.PileBodies[4].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[4].PileConstructionType = "場所打ちコンクリート杭";
                CurrentInputModel.PileBodies[4].PileToeDia = 1800;
                CurrentInputModel.PileBodies[4].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[4].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[4].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[4].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[4].SettlePileToeDia = 1800;
                CurrentInputModel.PileBodies[4].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[4].SettleN = 2;

                CurrentInputModel.PileBodies[4].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].SegmentLength = 9.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].SegmentDepth = 9.0;
                //CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.PileDiameter = 1800;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.ConcreteOutDia = 1800;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.ConcreteThickness = 900;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.MainBarDr = 1500;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.MainBarNum = 28;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.MainBarSize = "D32";
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[0].PileSection.ConcreteGamma = 23.0;

                CurrentInputModel.PileBodies[4].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[4].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].SegmentLength = 12.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].SegmentDepth = 21.0;
                //CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.PileDiameter = 1800;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.ConcreteOutDia = 1800;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.ConcreteThickness = 900;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.MainBarDr = 1500;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.PipeTs = 0.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.PipeDia = 0.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.MainBarNum = 24;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.MainBarSize = "D29";
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.ConcreteFc = 33.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.ConcreteGsi = 1.0;
                CurrentInputModel.PileBodies[4].PileBodySegments[1].PileSection.ConcreteGamma = 23.0;


                CurrentInputModel.PileLayoutItems =
                [
                    new()
                {
                    PileNo = 1,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 791,
                    AxialForceLevel1s = [-224, -214, 224, 214],
                    AxialForceLevel2s = [-336, -321, 336, 321],


                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 814,
                    AxialForceLevel1s = [-33, -66, 33, 66],
                    AxialForceLevel2s = [-50, -99, 50, 99],

                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 800,
                    AxialForceLevel1s = [-3, -69, 3, 69],
                    AxialForceLevel2s = [-5, -104, 5, 104],

                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 820,
                    AxialForceLevel1s = [3, -75, -3, 75],
                    AxialForceLevel2s = [5, -113, -5, 113],

                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 21,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 27.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 851,
                    AxialForceLevel1s = [33, -82, -33, 82],
                    AxialForceLevel2s = [50, -123, -50, 123],

                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 45.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 793,
                    AxialForceLevel1s = [224, -126, -224, 126],
                    AxialForceLevel2s = [336, -189, -336, 189],

                },
                new()
                {
                    PileNo = 7,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 51.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 1554,
                    AxialForceLevel1s = [-406, -316, 406, 316],
                    AxialForceLevel2s = [-609, -474, 609, 474],

                },

                new()
                {
                    PileNo = 8,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1767,
                    AxialForceLevel1s = [-73, -164, 73, 164],
                    AxialForceLevel2s = [-110, -246, 110, 246],

                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1839,
                    AxialForceLevel1s = [-30, -305, 30, 305],
                    AxialForceLevel2s = [-45, -458, 45, 458],

                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1817,
                    AxialForceLevel1s = [30, -312, -30, 312],
                    AxialForceLevel2s = [45, -468, -45, 468],

                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1781,
                    AxialForceLevel1s = [56, -153, -56, 153],
                    AxialForceLevel2s = [84, -230, -84, 230],

                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 27.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1721,
                    AxialForceLevel1s = [397, -365, -397, 365],
                    AxialForceLevel2s = [596, -548, -596, 548],

                },
                                new()
                {
                    PileNo = 13,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 33.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1512,
                    AxialForceLevel1s = [-346, -521, 346, 521],
                    AxialForceLevel2s = [-519, -782, 519, 782],

                },
                new()
                {
                    PileNo = 14,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 39.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1651,
                    AxialForceLevel1s = [-211, -491, 211, 491],
                    AxialForceLevel2s = [-317, -737, 317, 737],

                },
                new()
                {
                    PileNo = 15,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 45.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1818,
                    AxialForceLevel1s = [-65, -519, 65, 519],
                    AxialForceLevel2s = [-98, -779, 98, 779],

                },
                new()
                {
                    PileNo = 16,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 51.6,
                    Y = 5.5,
                    Z = -1.4,
                    AxialForceVL0 = 1741,
                    AxialForceLevel1s = [60, -495, -60, 495],
                    AxialForceLevel2s = [90, -743, -90, 743],

                },
                new()
                {
                    PileNo = 17,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1607,
                    AxialForceLevel1s = [210, -491, -210, 491],
                    AxialForceLevel2s = [315, -737, -315, 737],

                },
                new()
                {
                    PileNo = 18,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1734,
                    AxialForceLevel1s = [347, -489, -347, 489],
                    AxialForceLevel2s = [521, -734, -521, 734],

                },
                new()
                {
                    PileNo = 19,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1508,
                    AxialForceLevel1s = [-407, 255, 407, -255],
                    AxialForceLevel2s = [-611, 383, 611, -383],

                },
                new()
                {
                    PileNo = 20,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1692,
                    AxialForceLevel1s = [-215, 158, 215, -158],
                    AxialForceLevel2s = [-323, 257, 323, -257],

                },
                new()
                {
                    PileNo = 21,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 27.6,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1793,
                    AxialForceLevel1s = [-4, 836, 4, -836],
                    AxialForceLevel2s = [-6, 1254, 6, -1254],

                },
                new()
                {
                    PileNo = 22,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 39.6,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1618,
                    AxialForceLevel1s = [54, 914, -54, -914],
                    AxialForceLevel2s = [81, 1371, -81, -1371],

                },
                new()
                {
                    PileNo = 23,
                    PileBodyNo = 4,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 51.6,
                    Y = 14.5,
                    Z = -1.4,
                    AxialForceVL0 = 1490,
                    AxialForceLevel1s = [229, 506, -229, -506],
                    AxialForceLevel2s = [344, 759, -344, -759],

                },
                new()
                {
                    PileNo = 24,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 1629,
                    AxialForceLevel1s = [425, 1046, -425, -1046],
                    AxialForceLevel2s = [638, 1569, -638, -1569],

                },
                new()
                {
                    PileNo = 25,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 793,
                    AxialForceLevel1s = [-206, 522, 206, -522],
                    AxialForceLevel2s = [-309, 783, 309, -783],

                },
                new()
                {
                    PileNo = 26,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 1559,
                    AxialForceLevel1s = [-13, 515, 13, -515],
                    AxialForceLevel2s = [-20, 773, 20, -773],

                },
                new()
                {
                    PileNo = 27,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 28,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 27.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 29,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 33.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 30,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 39.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 31,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 45.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 32,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 51.6,
                    Y = 23.5,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 33,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 34,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 7.2,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 35,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 14.4,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 36,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 21.6,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 37,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 27.6,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 38,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 45.6,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
                new()
                {
                    PileNo = 39,
                    PileBodyNo = 5,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 51.6,
                    Y = 29.0,
                    Z = -1.4,
                    AxialForceVL0 = 763,
                    AxialForceLevel1s = [168, 501, -168, -501],
                    AxialForceLevel2s = [252, 752, -252, -752],
                },
            ];

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "A" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 5.5, Spacing = 5.5, Name = "B" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 14.5, Spacing = 9.0, Name = "C" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 23.5, Spacing = 9.0, Name = "D" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 29.0, Spacing = 5.5, Name = "E" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0.0, Spacing = 0.0, Name = "1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7.2, Spacing = 7.2, Name = "2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14.4, Spacing = 7.2, Name = "3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 21.6, Spacing = 7.2, Name = "4" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 27.6, Spacing = 6.0, Name = "5" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 33.6, Spacing = 6.0, Name = "6" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 39.6, Spacing = 6.0, Name = "7" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 45.6, Spacing = 6.0, Name = "8" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 51.6, Spacing = 6.0, Name = "9" });

                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }


                // Elements を「なし」に、PileGroupSettlement を「なし」にする処理
                // Elements を安全に空にする
                if (CurrentInputModel.Elements != null)
                {
                    CurrentInputModel.Elements.Clear();
                }
                else
                {
                    CurrentInputModel.Elements = new System.Collections.ObjectModel.ObservableCollection<Element>();
                }

                // PileGroupSettlement の中身を安全にクリア（null にすると他コードで NRE になる恐れがあるため中身を削る）
                if (CurrentInputModel.PileGroupSettlement != null)
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads?.Clear();
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                    CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = 0.0;
                }

                // 根入れ
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();

                // 各 PileSection を再計算してプロパティ反映（Example3_3 の該当位置に挿入）
                foreach (var pb in CurrentInputModel.PileBodies)
                {
                    foreach (var seg in pb.PileBodySegments)
                    {
                        var sec = seg.PileSection;
                        // 既製杭を名前で指定しているケースがあれば確実に反映させる
                        if (!string.IsNullOrWhiteSpace(sec.SelectedPrecastPile?.Name))
                        {
                            sec.RecalculateSelectedPrecastPile();
                        }

                        // 断面直設定の場合は直後に再計算して直す
                        sec.RecalculatePileDia();
                        sec.RecalculateConcreteE();
                        sec.SetSpecs();

                        Debug.WriteLine($"Example3_3: PileBodyRef={pb.PileBodyRef}, PileSectionType={sec.PileSectionType}, PileDiameter={sec.PileDiameter}, ConcreteOutDia={sec.ConcreteOutDia}");
                    }
                }

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例3.3を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example3_3));
            }
        }


        // 設計例集3.4
        [RelayCommand]
        private async Task Example3_4()
        {
            if (!TryStartExample(nameof(Example3_4))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // 新規作成
                CurrentFilePath = null;
                UpdateWindowAction?.Invoke();
                UpdateTreeView();

                var groundLayerViewModel = new GroundLayerViewModel(this);
                groundLayerViewModel.Example3_4Command.Execute(null);

                // ここでCurrentInputModelに反映
                CurrentInputModel.GroundsInput[0] = groundLayerViewModel.GroundInput.DeepCopy();

                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(8, 12.8, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = true;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 7559;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 1005;
                }
                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(8, 12.8, -2.0);
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                    if(i==0 || i == 2)
                    {
                        CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 13706;
                        CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 4018;
                    }
                    else
                    {
                        CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].UpperMassForce = 17322;
                        CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].FoundationMassForce = 4018;
                    }

                }


                CurrentInputModel.PileBodies.Clear();

                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[0].PileBodyRef = "(P1)";
                CurrentInputModel.PileBodies[0].PileBodyType = "既製コンクリート杭";
                CurrentInputModel.PileBodies[0].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[0].PileConstructionType = "埋込み杭（プレボーリング杭）";
                CurrentInputModel.PileBodies[0].PileToeDia = 900;
                CurrentInputModel.PileBodies[0].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[0].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[0].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[0].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[0].SettlePileToeDia = 900;
                CurrentInputModel.PileBodies[0].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[0].SettleN = 2;


                CurrentInputModel.PileBodies[0].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentLength = 7;
                CurrentInputModel.PileBodies[0].PileBodySegments[0].SegmentDepth = 7;
                var section00 = CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection;
                section00.PileSectionType = "SC杭";
                section00.SetSelectedPrecastPileByName("SC-900-標準-105-19");

                CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[0].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentLength = 13;
                CurrentInputModel.PileBodies[0].PileBodySegments[1].SegmentDepth = 20;
                var section01 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section01.PileSectionType = "SC杭";
                section01.SetSelectedPrecastPileByName("標準,,SC-900-標準-105-9");

                CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[0].PileBodySegments[2].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[2].SegmentLength = 12;
                CurrentInputModel.PileBodies[0].PileBodySegments[2].SegmentDepth = 32;
                var section02 = CurrentInputModel.PileBodies[0].PileBodySegments[2].PileSection;
                section02.PileSectionType = "SC杭";
                section02.SetSelectedPrecastPileByName("標準,,SC-900-標準-105-9");

                CurrentInputModel.PileBodies[0].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[0].PileBodySegments[3].No = 1;
                CurrentInputModel.PileBodies[0].PileBodySegments[3].SegmentLength = 12;
                CurrentInputModel.PileBodies[0].PileBodySegments[3].SegmentDepth = 44;
                var section03 = CurrentInputModel.PileBodies[0].PileBodySegments[3].PileSection;
                section03.PileSectionType = "SC杭";
                section03.SetSelectedPrecastPileByName("標準,,SC-900-標準-105-9");


                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[1].PileBodyRef = "(P2)";
                CurrentInputModel.PileBodies[1].PileBodyType = "既製コンクリート杭";
                CurrentInputModel.PileBodies[1].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[1].PileConstructionType = "埋込み杭（プレボーリング杭）";
                CurrentInputModel.PileBodies[1].PileToeDia = 1000;
                CurrentInputModel.PileBodies[1].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[1].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[1].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[1].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[1].SettlePileToeDia = 1000;
                CurrentInputModel.PileBodies[1].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[1].SettleN = 2;

                CurrentInputModel.PileBodies[1].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentLength = 7;
                CurrentInputModel.PileBodies[1].PileBodySegments[0].SegmentDepth = 7;
                var section10 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section10.PileSectionType = "SC杭";
                section10.SetSelectedPrecastPileByName("SC-1000-標準-105-19");

                CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[1].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentLength = 13;
                CurrentInputModel.PileBodies[1].PileBodySegments[1].SegmentDepth = 20;
                var section11 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section11.PileSectionType = "PHC杭";
                section11.SetSelectedPrecastPileByName("標準,B,PHC-1000-標準-105-B");

                CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[1].PileBodySegments[2].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[2].SegmentLength = 12;
                CurrentInputModel.PileBodies[1].PileBodySegments[2].SegmentDepth = 32;
                var section12 = CurrentInputModel.PileBodies[0].PileBodySegments[2].PileSection;
                section12.PileSectionType = "PHC杭";
                section12.SetSelectedPrecastPileByName("標準,B,PHC-1000-標準-105-B");

                CurrentInputModel.PileBodies[1].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[1].PileBodySegments[3].No = 1;
                CurrentInputModel.PileBodies[1].PileBodySegments[3].SegmentLength = 12;
                CurrentInputModel.PileBodies[1].PileBodySegments[3].SegmentDepth = 44;
                var section13 = CurrentInputModel.PileBodies[0].PileBodySegments[3].PileSection;
                section13.PileSectionType = "SC杭";
                section13.SetSelectedPrecastPileByName("SC-1000-標準-105-9");

                CurrentInputModel.PileBodies.Add(new());

                CurrentInputModel.PileBodies[2].PileBodyRef = "(P2A)";
                CurrentInputModel.PileBodies[2].PileBodyType = "既製コンクリート杭";
                CurrentInputModel.PileBodies[2].PileTopType = "鉄筋定着工法";
                CurrentInputModel.PileBodies[2].PileConstructionType = "埋込み杭（プレボーリング杭）";
                CurrentInputModel.PileBodies[2].PileToeDia = 700;
                CurrentInputModel.PileBodies[2].TipNonPermability = 0.0;
                CurrentInputModel.PileBodies[2].EmbedmentIntoBearingSoil = 1.0;
                CurrentInputModel.PileBodies[2].PileInnerDia = 0.0;
                CurrentInputModel.PileBodies[2].PileTipStyle = "閉端杭";
                CurrentInputModel.PileBodies[2].SettlePileToeDia = 700;
                CurrentInputModel.PileBodies[2].SettleAlpha = 0.3;
                CurrentInputModel.PileBodies[2].SettleN = 2;

                CurrentInputModel.PileBodies[2].PileBodySegments[0].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentLength = 7;
                CurrentInputModel.PileBodies[2].PileBodySegments[0].SegmentDepth = 7;
                var section20 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section20.PileSectionType = "SC杭";
                section20.SetSelectedPrecastPileByName("SC-1000-標準-105-19");

                CurrentInputModel.PileBodies[2].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[2].PileBodySegments[1].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentLength = 13;
                CurrentInputModel.PileBodies[2].PileBodySegments[1].SegmentDepth = 20;
                var section21 = CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection;
                section21.PileSectionType = "SC杭";
                section21.SetSelectedPrecastPileByName("SC-1000-標準-105-9");

                CurrentInputModel.PileBodies[2].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[2].PileBodySegments[2].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[2].SegmentLength = 12;
                CurrentInputModel.PileBodies[2].PileBodySegments[2].SegmentDepth = 32;
                var section22 = CurrentInputModel.PileBodies[0].PileBodySegments[2].PileSection;
                section22.PileSectionType = "SC杭";
                section22.SetSelectedPrecastPileByName("SC-1000-標準-105-9");

                CurrentInputModel.PileBodies[2].PileBodySegments.Add(new PileBodySegment());

                CurrentInputModel.PileBodies[2].PileBodySegments[3].No = 1;
                CurrentInputModel.PileBodies[2].PileBodySegments[3].SegmentLength = 12;
                CurrentInputModel.PileBodies[2].PileBodySegments[3].SegmentDepth = 44;
                var section23 = CurrentInputModel.PileBodies[0].PileBodySegments[3].PileSection;
                section23.PileSectionType = "SC杭";
                section23.SetSelectedPrecastPileByName("SC-1000-標準-105-9");

                CurrentInputModel.PileLayoutItems =
                [
                    new()
                {
                    PileNo = 1,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 2794,
                    AxialForceLevel1s = [1604, 1103, 3924, 4485],
                    AxialForceLevel2s = [-722, -722, 4922, 6410],


                },
                new()
                {
                    PileNo = 2,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 9.6,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 3555,
                    AxialForceLevel1s = [2425, 2064, 4685, 5047],
                    AxialForceLevel2s = [1908, 100, 5018, 6998],

                },
                new()
                {
                    PileNo = 3,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.0,
                    Y = 0.0,
                    Z = -1.4,
                    AxialForceVL0 = 2269,
                    AxialForceLevel1s = [4627, 633, 1, 3905],
                    AxialForceLevel2s = [5980, -1704, -1339, 6245],

                },
                new()
                {
                    PileNo = 4,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 6.4,
                    Z = -1.4,
                    AxialForceVL0 = 3767,
                    AxialForceLevel1s = [2576, 3996, 4959, 3539],
                    AxialForceLevel2s = [1567, 5959, 4156, 3274],

                },
                new()
                {
                    PileNo = 5,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 21,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 9.6,
                    Y = 6.4,
                    Z = -1.4,
                    AxialForceVL0 = 4399,
                    AxialForceLevel1s = [3718, 4605, 5081, 4194],
                    AxialForceLevel2s = [3073, 4906, 5693, 3887],

                },
                new()
                {
                    PileNo = 6,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 1,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.0,
                    Y = 6.4,
                    Z = -1.4,
                    AxialForceVL0 = 3009,
                    AxialForceLevel1s = [4884, 3162, 1134, 2856],
                    AxialForceLevel2s = [6543, 3239, -478, 2666],

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
                    Y = 12.8,
                    Z = -1.4,
                    AxialForceVL0 = 3680,
                    AxialForceLevel1s = [2487, 3630, 4873, 3730],
                    AxialForceLevel2s = [1473, 3700, 5869, 3843],

                },

                new()
                {
                    PileNo = 8,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 9.6,
                    Y = 12.80,
                    Z = -1.4,
                    AxialForceVL0 = 4258,
                    AxialForceLevel1s = [3612, 4212, 4905, 4304],
                    AxialForceLevel2s = [2966, 4275, 5526, 4443],

                },
                new()
                {
                    PileNo = 9,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.0,
                    Y = 12.8,
                    Z = -1.4,
                    AxialForceVL0 = 2756,
                    AxialForceLevel1s = [4595, 2722, 916, 2789],
                    AxialForceLevel2s = [6263, 2773, -704, 2900],

                },
                new()
                {
                    PileNo = 10,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0.0,
                    Y = 19.2,
                    Z = -1.4,
                    AxialForceVL0 = 4074,
                    AxialForceLevel1s = [2887, 3928, 5260, 4219],
                    AxialForceLevel2s = [1876, 3644, 6254, 4327],

                },
                new()
                {
                    PileNo = 11,
                    PileBodyNo = 2,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 9.6,
                    Y = 19.2,
                    Z = -1.4,
                    AxialForceVL0 = 4431,
                    AxialForceLevel1s = [3752, 4280, 5110, 4582],
                    AxialForceLevel2s = [3108, 3977, 5727, 4759],

                },
                new()
                {
                    PileNo = 12,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.0,
                    Y = 19.2,
                    Z = -1.4,
                    AxialForceVL0 = 2976,
                    AxialForceLevel1s = [4842, 2867, 1109, 3085],
                    AxialForceLevel2s = [6502, 2680, -498, 3066],

                },
                new()
                {
                    PileNo = 13,
                    PileBodyNo = 1,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 0,
                    Y = 25.6,
                    Z = -1.4,
                    AxialForceVL0 = 2931,
                    AxialForceLevel1s = [1800, 4590, 4062, 1272],
                    AxialForceLevel2s = [844, 6502, 5074, -618],

                },
                new()
                {
                    PileNo = 14,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 9.60,
                    Y = 25.6,
                    Z = -1.4,
                    AxialForceVL0 = 3318,
                    AxialForceLevel1s = [2198, 4801, 4438, 1835],
                    AxialForceLevel2s = [1605, 6751, 4806, -95],

                },
                new()
                {
                    PileNo = 15,
                    PileBodyNo = 3,
                    GroundNo = 1,
                    SoilPileAltNo = 2,
                    GroupPileFactor = 1.0,
                    PileSpacingFactor = 10,
                    X = 16.0,
                    Y = 25.6,
                    Z = -1.4,
                    AxialForceVL0 = 2039,
                    AxialForceLevel1s = [4290, 3665, -212, 414],
                    AxialForceLevel2s = [5742, 6034, -1592, -1844],

                },
            ];

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 6.4, Spacing = 6.4, Name = "Y2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 12.8, Spacing = 6.4, Name = "Y3" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 19.2, Spacing = 6.4, Name = "Y4" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 25.6, Spacing = 6.4, Name = "Y5" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0.0, Spacing = 0.0, Name = "X1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 9.6, Spacing = 9.6, Name = "X2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 16.0, Spacing = 6.4, Name = "X3" });


                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }

                // 根入れ
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();

                // 各 PileSection を再計算してプロパティ反映（Example3_3 の該当位置に挿入）
                foreach (var pb in CurrentInputModel.PileBodies)
                {
                    foreach (var seg in pb.PileBodySegments)
                    {
                        var sec = seg.PileSection;
                        // 既製杭を名前で指定しているケースがあれば確実に反映させる
                        if (!string.IsNullOrWhiteSpace(sec.SelectedPrecastPile?.Name))
                        {
                            sec.RecalculateSelectedPrecastPile();
                        }

                        // 断面直設定の場合は直後に再計算して直す
                        sec.RecalculatePileDia();
                        sec.RecalculateConcreteE();
                        sec.SetSpecs();

                        Debug.WriteLine($"Example3_4: PileBodyRef={pb.PileBodyRef}, PileSectionType={sec.PileSectionType}, PileDiameter={sec.PileDiameter}, ConcreteOutDia={sec.ConcreteOutDia}");
                    }
                }

                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例3.4を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null; 
                EndExample(nameof(Example3_4));
            }
        }

        // 関東支部 計算例8
        [RelayCommand]
        private async Task ExampleK8()
        {
            if (!TryStartExample(nameof(ExampleK8))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

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
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].ForceActionPoint = new Point3D(10.80, 8.250, -2.0); // 慣性力中心点
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].IsApplicable = true;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].UpperMassForce = 3282;
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel1[i].FoundationMassForce = 828;
                }
                for (int i = 0; i < CurrentInputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
                {
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].ForceActionPoint = new Point3D(10.80, 8.250, -2.0); // 慣性力中心点
                    CurrentInputModel.LoadCasesInput.LoadCasesLevel2[i].IsApplicable = true;
                    if (i == 0 || i == 2)
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
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[1].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[1].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[2].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[2].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[3].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[3].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
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

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 5.7, Spacing = 5.7, Name = "Y2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 16.5, Spacing = 10.8, Name = "Y3" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0, Spacing = 0, Name = "X1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7.2, Spacing = 7.2, Name = "X2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14.4, Spacing = 7.2, Name = "X3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 21.6, Spacing = 7.2, Name = "X4" });


                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }

                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();

                // 根入れ部を「なし」にする（Embedment レイヤをクリアし、計算用保持を null に）
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
                CurrentInputModel.ElementDivision.SoilEmbedment = null;


                // Elements を「なし」に、PileGroupSettlement を「なし」にする処理
                // Elements を安全に空にする
                if (CurrentInputModel.Elements != null)
                {
                    CurrentInputModel.Elements.Clear();
                }
                else
                {
                    CurrentInputModel.Elements = new System.Collections.ObjectModel.ObservableCollection<Element>();
                }

                // PileGroupSettlement の中身を安全にクリア（null にすると他コードで NRE になる恐れがあるため中身を削る）
                if (CurrentInputModel.PileGroupSettlement != null)
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads?.Clear();
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                    CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = 0.0;
                }

                // -- 真に「null にしたい」場合は次の行を有効化してください（副作用に注意） --
                // CurrentInputModel.PileGroupSettlement = null;


                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例8を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(ExampleK8));
            }
        }




        // 基礎指針'19 計算例9
        [RelayCommand]
        private async Task Example9()
        {
            if (!TryStartExample(nameof(Example9))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

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
                CurrentInputModel.PileBodies[0].PileBodySegments[0].PileSection.PileSectionType = "鉄筋コンクリート部";
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
                CurrentInputModel.PileBodies[0].PileBodySegments[1].PileSection.PileSectionType = "鉄筋コンクリート部";
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

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "C" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 7.75, Spacing = 7.75, Name = "B" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 13.75, Spacing = 6.0, Name = "A" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0, Spacing = 0, Name = "1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7.375, Spacing = 7.375, Name = "2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14.725, Spacing = 7.35, Name = "3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 22.075, Spacing = 7.35, Name = "4" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 29.425, Spacing = 7.35, Name = "5" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 36.8, Spacing = 7.375, Name = "6" });


                UpdateEmbedment();

                // Elements を「なし」に、PileGroupSettlement を「なし」にする処理
                // Elements を安全に空にする
                if (CurrentInputModel.Elements != null)
                {
                    CurrentInputModel.Elements.Clear();
                }
                else
                {
                    CurrentInputModel.Elements = [];
                }

                // PileGroupSettlement の中身を安全にクリア（null にすると他コードで NRE になる恐れがあるため中身を削る）
                if (CurrentInputModel.PileGroupSettlement != null)
                {
                    CurrentInputModel.PileGroupSettlement.RectLoads?.Clear();
                    CurrentInputModel.PileGroupSettlement.SettlementSoilLayers?.Clear();
                    CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = 0.0;
                }

                // -- 真に「null にしたい」場合は次の行を有効化してください（副作用に注意） --
                // CurrentInputModel.PileGroupSettlement = null;


                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();


                Debug.WriteLine("Example9: end");

                // 読み込み完了メッセージ
                MessageBox.Show("設計例9を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(Example9));
            }
        }


        // 関東支部　設計例7
        [RelayCommand]
        private async Task ExampleK7()
        {
            if (!TryStartExample(nameof(ExampleK7))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // 慣性力中心点
                Point3D inertiaPoint = new(13.0, 9.5, -0.90);
                foreach (var loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
                foreach (var loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }



                CurrentInputModel.PileGroupSettlement.RectLoads = [
                    new RectLoad(){
                    X1 = -2.10,
                    X2 = 2.10,
                    Y1 = -2.10,
                    Y2 = 2.10,
                    QA = 115.1927,
                },
                new RectLoad(){
                    X1 = 5.70,
                    X2 = 9.50,
                    Y1 = -1.90,
                    Y2 = 1.90,
                    QA = 145.2909,
                },
                new RectLoad(){
                    X1 = 12.10,
                    X2 = 15.90,
                    Y1 = -1.90,
                    Y2 = 1.90,
                    QA = 127.8393,
                },
                new RectLoad(){
                    X1 = 18.30,
                    X2 = 22.50,
                    Y1 = -2.10,
                    Y2 = 2.10,
                    QA = 145.6916,
                },
                new RectLoad(){
                    X1 = 24.10,
                    X2 = 27.90,
                    Y1 = -1.90,
                    Y2 = 1.90,
                    QA = 115.0277,
                },
                new RectLoad(){
                    X1 = -2.5,
                    X2 = 2.50,
                    Y1 = 6.0,
                    Y2 = 11.0,
                    QA = 119.08,
                },
                new RectLoad(){
                    X1 = 5.1,
                    X2 = 10.1,
                    Y1 = 6.0,
                    Y2 = 11.0,
                    QA = 154.76,
                },
                new RectLoad(){
                    X1 = 11.5,
                    X2 = 16.5,
                    Y1 = 6.0,
                    Y2 = 11.0,
                    QA = 139.36,
                },
                new RectLoad(){
                    X1 = 17.9,
                    X2 = 22.9,
                    Y1 = 6.0,
                    Y2 = 11.0,
                    QA = 153.04,
                },
                new RectLoad(){
                    X1 = 23.9,
                    X2 = 28.1,
                    Y1 = 6.4,
                    Y2 = 10.6,
                    QA = 154.8753,
                },
                new RectLoad(){
                    X1 = -2.1,
                    X2 = 2.1,
                    Y1 = 16.9,
                    Y2 = 21.1,
                    QA = 124.1497,
                },
                new RectLoad(){
                    X1 = 5.5,
                    X2 =9.7,
                    Y1 = 16.9,
                    Y2 = 21.1,
                    QA = 140.0227,
                },
                new RectLoad(){
                    X1 = 11.9,
                    X2 =16.1,
                    Y1 = 16.9,
                    Y2 = 21.1,
                    QA = 129.3084,
                },
                new RectLoad(){
                    X1 = 18.3,
                    X2 =22.5,
                    Y1 = 16.9,
                    Y2 = 21.1,
                    QA = 141.8934,
                },
                new RectLoad(){
                    X1 = 24.4,
                    X2 =27.6,
                    Y1 = 17.4,
                    Y2 = 20.6,
                    QA = 139.7461,
                }];

                CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = -2.0;

                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers = [
                    new(){
                    BottomAltitude = -9.5,
                    Ek = 28_000,
                    PoissonsRatio = 0.33,
                    Thickness = 9.5,
                    },
                ];
                CurrentInputModel.PileLayoutItems = [
                    new(){ X = 0.0, Y = 0.0 },
                new(){ X = 7.6, Y = 0.0 },
                new(){ X = 14.0, Y = 0.0 },
                new(){ X = 20.4, Y = 0.0 },
                new(){ X = 26.0, Y = 0.0 },
                new(){ X = 0.0, Y = 8.5 },
                new(){ X = 7.6, Y = 8.5 },
                new(){ X = 14.0, Y = 8.5 },
                new(){ X = 20.4, Y = 8.5 },
                new(){ X = 26.0, Y = 8.5 },
                new(){ X = 0.0, Y = 19.0 },
                new(){ X = 7.6, Y = 19.0 },
                new(){ X = 14.0, Y = 19.0 },
                new(){ X = 20.4, Y = 19.0 },
                new(){ X = 26.0, Y = 19.0 },
    ];

                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }

                ObservableCollection<PileLayoutDataItem> pileLayouts = CurrentInputModel.PileLayoutItems;

                CurrentInputModel.Elements = [
                    new("ダミー", pileLayouts[0], pileLayouts[1]),
                new("ダミー", pileLayouts[1], pileLayouts[2]),
                new("ダミー", pileLayouts[2], pileLayouts[3]),
                new("ダミー", pileLayouts[3], pileLayouts[4]),

                new("ダミー", pileLayouts[5], pileLayouts[6]),
                new("ダミー", pileLayouts[6], pileLayouts[7]),
                new("ダミー", pileLayouts[7], pileLayouts[8]),
                new("ダミー", pileLayouts[8], pileLayouts[9]),

                new("ダミー", pileLayouts[10], pileLayouts[11]),
                new("ダミー", pileLayouts[11], pileLayouts[12]),
                new("ダミー", pileLayouts[12], pileLayouts[13]),
                new("ダミー", pileLayouts[13], pileLayouts[14]),

                new("ダミー", pileLayouts[0], pileLayouts[5]),
                new("ダミー", pileLayouts[1], pileLayouts[6]),
                new("ダミー", pileLayouts[2], pileLayouts[7]),
                new("ダミー", pileLayouts[3], pileLayouts[8]),
                new("ダミー", pileLayouts[4], pileLayouts[9]),

                new("ダミー", pileLayouts[5], pileLayouts[10]),
                new("ダミー", pileLayouts[6], pileLayouts[11]),
                new("ダミー", pileLayouts[7], pileLayouts[12]),
                new("ダミー", pileLayouts[8], pileLayouts[13]),
                new("ダミー", pileLayouts[9], pileLayouts[14]),

                ];

                // グリッド
                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 8.5, Spacing = 8.5, Name = "Y2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 19, Spacing = 10.5, Name = "Y3" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0, Spacing = 0, Name = "X1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7.6, Spacing = 7.6, Name = "X2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14, Spacing = 6.4, Name = "X3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 20.4, Spacing = 6.4, Name = "X4" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 26.0, Spacing = 5.6, Name = "X5" });

                GroupPileSettlementXOffset = 7.0;
                GroupPileSettlementYOffset = 9.0;

                GroupPileSettlementXSpacing = 1.4;
                GroupPileSettlementYSpacing = 1.8;

                // 根入れ部を無効化
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
                CurrentInputModel.ElementDivision.SoilEmbedment = null;

                UpdatePileLayoutNo();

                IsGroupPileSettlementAnalysisDone = false;
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例7を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(ExampleK7));
            }
        }

        // 設計例5
        [RelayCommand]
        private async Task OnExample5()
        {
            if (!TryStartExample(nameof(OnExample5))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());

                // 慣性力中心点
                Point3D inertiaPoint = new(10.5, 9.0, -1.00);
                foreach (var loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel1)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }
                foreach (var loadCase in CurrentInputModel.LoadCasesInput.LoadCasesLevel2)
                {
                    loadCase.ForceActionPoint = inertiaPoint;
                }

                CurrentInputModel.PileGroupSettlement.RectLoads = [
                    new RectLoad(){
                    X1 = -1.25,
                    X2 = 1.25,
                    Y1 = -1.25,
                    Y2 = 1.25,
                    QA = 500.0,
                },
                new RectLoad(){
                    X1 = 7.0 - 1.70,
                    X2 = 7.0 + 1.70,
                    Y1 = -1.50,
                    Y2 = 1.50,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = 14.0 - 1.70,
                    X2 = 14.0 + 1.70,
                    Y1 = -1.50,
                    Y2 = 1.50,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = 21.0 - 1.25,
                    X2 = 21.0 + 1.25,
                    Y1 = -1.25,
                    Y2 = 1.25,
                    QA = 500.0,
                },
                new RectLoad(){
                    X1 = -1.5,
                    X2 = 1.5,
                    Y1 = 9.0 - 1.7,
                    Y2 = 9.0 + 1.7,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = 7.0 - 2.0,
                    X2 = 7.0 + 2.0,
                    Y1 = 9.0 - 2.0,
                    Y2 = 9.0 + 2.0,
                    QA = 1280.0,
                },
                new RectLoad(){
                    X1 = 14.0 - 2.0,
                    X2 = 14.0 + 2.0,
                    Y1 = 9.0 - 2.0,
                    Y2 = 9.0 + 2.0,
                    QA = 1280.0,
                },
                new RectLoad(){
                    X1 = 21.0 - 1.5,
                    X2 = 21.0 + 1.5,
                    Y1 = 9.0 - 1.7,
                    Y2 = 9.0 + 1.7,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = -1.25,
                    X2 = 1.25,
                    Y1 = 18.0 - 1.25,
                    Y2 = 18.0 + 1.25,
                    QA = 500.0,
                },
                new RectLoad(){
                    X1 = 7.0 - 1.70,
                    X2 = 7.0 + 1.70,
                    Y1 = 18.0 - 1.50,
                    Y2 = 18.0 + 1.50,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = 14.0 - 1.70,
                    X2 = 14.0 + 1.70,
                    Y1 = 18.0 - 1.50,
                    Y2 = 18.0 + 1.50,
                    QA = 816.0,
                },
                new RectLoad(){
                    X1 = 21.0 - 1.25,
                    X2 = 21.0 + 1.25,
                    Y1 = 18.0 - 1.25,
                    Y2 = 18.0 + 1.25,
                    QA = 500.0,
                }];

                CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = -3.0;

                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers = [
                    new(){
                    BottomAltitude = -12.0,
                    Ek = 28_000,
                    PoissonsRatio = 0.33,
                    Thickness = 9.0,
                    },
                new(){
                    BottomAltitude = -14.0,
                    Ek = 8_400,
                    PoissonsRatio = 0.45,
                    Thickness = 2.0,
                    },
                new(){
                    BottomAltitude = -17.0,
                    Ek = 28_000,
                    PoissonsRatio = 0.33,
                    Thickness = 3.0,
                    },
                ];

                CurrentInputModel.PileLayoutItems = [
                    new(){ X = 0.0, Y = 0.0 },
                    new(){ X = 7.0, Y = 0.0 },
                    new(){ X = 14.0, Y = 0.0 },
                    new(){ X = 21.0, Y = 0.0 },
                    new(){ X = 0.0, Y = 9.0 },
                    new(){ X = 7.0, Y = 9.0 },
                    new(){ X = 14.0, Y = 9.0 },
                    new(){ X = 21.0, Y = 9.0 },
                    new(){ X = 0.0, Y = 18.0 },
                    new(){ X = 7.0, Y = 18.0 },
                    new(){ X = 14.0, Y = 18.0 },
                    new(){ X = 21.0, Y = 18.0 }
                    ];

                foreach (var pileLayoutItem in CurrentInputModel.PileLayoutItems)
                {
                    pileLayoutItem.SetMainWindowViewModel(this);
                }

                ObservableCollection<PileLayoutDataItem> pileLayouts = CurrentInputModel.PileLayoutItems;

                CurrentInputModel.Elements = [
                    new("ダミー", pileLayouts[0], pileLayouts[1]),
                    new("ダミー", pileLayouts[1], pileLayouts[2]),
                    new("ダミー", pileLayouts[2], pileLayouts[3]),
                    new("ダミー", pileLayouts[4], pileLayouts[5]),
                    new("ダミー", pileLayouts[5], pileLayouts[6]),
                    new("ダミー", pileLayouts[6], pileLayouts[7]),
                    new("ダミー", pileLayouts[8], pileLayouts[9]),
                    new("ダミー", pileLayouts[9], pileLayouts[10]),
                    new("ダミー", pileLayouts[10], pileLayouts[11]),
                    new("ダミー", pileLayouts[0], pileLayouts[4]),
                    new("ダミー", pileLayouts[4], pileLayouts[8]),
                    new("ダミー", pileLayouts[1], pileLayouts[5]),
                    new("ダミー", pileLayouts[5], pileLayouts[9]),
                    new("ダミー", pileLayouts[2], pileLayouts[6]),
                    new("ダミー", pileLayouts[6], pileLayouts[10]),
                    new("ダミー", pileLayouts[3], pileLayouts[7]),
                    new("ダミー", pileLayouts[7], pileLayouts[11]),
                ];

                CurrentInputModel.GridYItems.Clear();
                CurrentInputModel.GridYItems.Add(new() { Coord = 0, Spacing = 0, Name = "X1" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 9, Spacing = 9, Name = "X2" });
                CurrentInputModel.GridYItems.Add(new() { Coord = 18, Spacing = 9, Name = "X3" });

                CurrentInputModel.GridXItems.Clear();
                CurrentInputModel.GridXItems.Add(new() { Coord = 0, Spacing = 0, Name = "Y1" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 7, Spacing = 7, Name = "Y2" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 14, Spacing = 7, Name = "Y3" });
                CurrentInputModel.GridXItems.Add(new() { Coord = 21, Spacing = 7, Name = "Y4" });

                GroupPileSettlementXOffset = 7.0;
                GroupPileSettlementYOffset = 9.0;

                GroupPileSettlementXSpacing = 1.4;
                GroupPileSettlementYSpacing = 1.8;


                // 根入れ部を「なし」にする（Embedment レイヤをクリアし、計算用保持を null に）
                CurrentInputModel.EmbedmentInput.EmbedmentLayers.Clear();
                CurrentInputModel.ElementDivision.SoilEmbedment = null;

                UpdatePileLayoutNo();

                IsGroupPileSettlementAnalysisDone = false;
                UpdateWindowAction?.Invoke(); // デリゲートを通じてコードビハインドのメソッドを呼び出す
                UpdateTreeView();

                // 読み込み完了メッセージ
                MessageBox.Show("設計例5を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(OnExample5));
            }
        }

        // 設計例集2.1
        [RelayCommand]
        private async Task OnExample2_1()
        {
            if (!TryStartExample(nameof(OnExample2_1))) return;
            try
            {
                // 砂時計にする（UI スレッドで設定）
                Mouse.OverrideCursor = Cursors.Wait;
                // UI を一度描画させる（カーソル表示のために短時間yield）
                await Task.Delay(50);

                // Undoポイントを追加
                _undoManager.SaveState(CurrentInputModel.DeepCopy());


                CurrentInputModel.PileGroupSettlement.RectLoads = [
                    new RectLoad(){
                    X1 = -1.1,
                    X2 = 1.1,
                    Y1 = -1.1,
                    Y2 = 1.1,
                    QA = 947.0 / (2.2 * 2.2),
                },
                new RectLoad(){
                    X1 = 7.8 - 1.3,
                    X2 = 7.8 + 1.3,
                    Y1 = -1.3,
                    Y2 = 1.3,
                    QA = 1453.0 / (2.2 * 2.2),
                },
                ];

                CurrentInputModel.PileGroupSettlement.LoadingPlaneAltutude = -2.4;

                CurrentInputModel.PileGroupSettlement.SettlementSoilLayers = [
                    new(){
                    BottomAltitude = -5.9,
                    Ek = 72_800,
                    PoissonsRatio = 0.3,
                    Thickness = 3.5,
                    },
                new(){
                    BottomAltitude = -6.9,
                    Ek = 8_400,
                    PoissonsRatio = 0.45,
                    Thickness = 1.0,
                    },
                new(){
                    BottomAltitude = -12.0,
                    Ek = 84_000,
                    PoissonsRatio = 0.30,
                    Thickness = 5.1,
                    },
                    ];

                // 読み込み完了メッセージ
                MessageBox.Show("設計例2.1を読み込みました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                // 砂時計を戻す
                Mouse.OverrideCursor = null;
                EndExample(nameof(OnExample2_1));
            }
        }
    }
}