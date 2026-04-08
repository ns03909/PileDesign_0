using System;

namespace PileDesign.Models.InputData
{
    public class ZDataItem : BaseModel
    {
        private double _z;
        public double Z
        {
            get => _z;
            set
            {
                //if (SetProperty(ref _z, value) && GroundLayerNo != null)
                if (SetProperty(ref _z, value) && GroundInput != null)
                {
                    SetSoilDisplacement(/*InputModel.Instance*/); // Zが設定されたときにSetSoilDisplacementを呼び出す
                }
            }
        }

        public bool IsChangeable { get; set; }
        public int? SegmentNo { get; set; } // 杭体区間番号
                                            //public int? GroundLayerNo { get; set; } 

        // 土層番号
        //private int? _groundLayerNo;
        //public int? GroundLayerNo
        //{
        //    get => _groundLayerNo;
        //    set
        //    {
        //        if (SetProperty(ref _groundLayerNo, value))
        //        {

        //                    Console.WriteLine($"GroundLayerNo set to {value} in ZDataItem");


        //            SetSoilDisplacement(/*InputModel.Instance*/); // Zが設定されたときにSetSoilDisplacementを呼び出す
        //        }
        //    }
        //}

        // 土層番号
        private GroundInput _groundInput;
        public GroundInput GroundInput
        {
            get => _groundInput;
            set
            {
                if (SetProperty(ref _groundInput, value))
                {
                    SetSoilDisplacement(); // Zが設定されたときにSetSoilDisplacementを呼び出す
                }
            }
        }

        public double GroundDisp1 { get; set; } // レベル1地盤変位
        public double GroundDisp2 { get; set; } // レベル2地盤変位
        public double GroundDisp1L { get; set; } // レベル1液状化地盤変位
        public double GroundDisp2L { get; set; } // レベル2液状化地盤変位

        // 地盤変位のセットメソッド
        public void SetSoilDisplacement(/*GroundInput groundInput*/)
        {
            if (GroundInput == null) return;
            GroundInput groundInput = GroundInput;

            // 任意地盤変位プロファイルが有効な場合はそちらを使用
            var custom = groundInput.CustomDisplacementProfile;
            if (custom != null && custom.IsEnabled)
            {
                GroundDisp1 = custom.Interpolate(custom.Level1NonLiq, Z);
                GroundDisp2 = custom.Interpolate(custom.Level2NonLiq, Z);
                GroundDisp1L = custom.Interpolate(custom.Level1Liq, Z);
                GroundDisp2L = custom.Interpolate(custom.Level2Liq, Z);
                return;
            }
            for (int i = 0; i < groundInput.GroundMassesData.Count; i++)
            {
                if (groundInput.GroundMassesData[i].AltitudeDepth < Z)
                {
                    if (i == 0)
                    {
                        GroundDisp1 = groundInput.GroundMassesData[0].DmaxUStar[0];
                        GroundDisp2 = groundInput.GroundMassesData[0].DmaxUStar[1];
                        GroundDisp1L = groundInput.GroundMassesData[0].DmaxUStarSigmaGammaCyH[0];
                        GroundDisp2L = groundInput.GroundMassesData[0].DmaxUStarSigmaGammaCyH[1];
                        break;
                    }
                    else
                    {
                        var data1 = groundInput.GroundMassesData[i - 1];
                        var data2 = groundInput.GroundMassesData[i];

                        GroundDisp1 = GetInterpolatedValue(Z, data1.AltitudeDepth, data2.AltitudeDepth, data1.DmaxUStar[0], data2.DmaxUStar[0]);
                        GroundDisp2 = GetInterpolatedValue(Z, data1.AltitudeDepth, data2.AltitudeDepth, data1.DmaxUStar[1], data2.DmaxUStar[1]);
                        GroundDisp1L = GetInterpolatedValue(Z, data1.AltitudeDepth, data2.AltitudeDepth, data1.DmaxUStarSigmaGammaCyH[0], data2.DmaxUStarSigmaGammaCyH[0]);
                        GroundDisp2L = GetInterpolatedValue(Z, data1.AltitudeDepth, data2.AltitudeDepth, data1.DmaxUStarSigmaGammaCyH[1], data2.DmaxUStarSigmaGammaCyH[1]);
                        break;
                    }
                }
                else if (i == groundInput.GroundMassesData.Count - 1)
                {
                    GroundDisp1 = 0;
                    GroundDisp2 = 0;
                    GroundDisp1L = 0;
                    GroundDisp2L = 0;
                    break;
                }
            }
        }

        // 内挿の値を返すメソッド
        public static double GetInterpolatedValue(double x, double x1, double x2, double value1, double value2)

        {
            if (x1 == x2)
            {
                throw new ArgumentException("x1 and x2 must not be equal to avoid division by zero.");
            }

            if (x < Math.Min(x1, x2) || x > Math.Max(x1, x2))
            {
                throw new ArgumentOutOfRangeException(nameof(x), "x is out of the interpolation range.");
            }

            return ((x2 - x) * value1 + (x - x1) * value2) / (x2 - x1);
        }
    }
}