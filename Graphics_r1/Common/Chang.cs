using MathNet.Numerics.Distributions;
using System;
using System.ComponentModel;

namespace PileDesign.Common
{
    public class Chang : INotifyPropertyChanged
    {

        // 派生フィールド（中間値をキャッシュ）
        //private double _denom4;            // 4 * EI * beta^2
        //private double _denom12;           // 12 * EI * beta^2
        //private double _onePlusBetaH;      // 1 + beta * h
        //private double _onePlusBetaH3;     // (1 + beta * h)^3
        //private double _expNegBetaH;       // exp(-beta * h)
        //private double _twoBetaH;          // 2 * beta * h
        //private double _A_for_max;         // (1 + 2*beta*h) - (1 + beta*h) * ar
        //private double _B_for_deflection;  // helper for deflection expressions

        // キャッシュされた計算結果
        private double _pileHeadDisplacement;
        private double _groundSurfaceDisplacement;
        private double _pileHeadMoment;
        private double _maxBendingMoment;
        private double _depthOfMaxBendingMoment;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // editable properties

        private double _axialForce; // 杭の軸力
        public double AxialForce
        {
            get => _axialForce;
            set
            {
                if (_axialForce == value) return;
                _axialForce = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(AxialForce));
                NotifyDependents();
                Update();
            }
        }

        private double _ei; // 杭の曲げ剛性
        public double EI
        {
            get => _ei;
            set
            {
                if (_ei == value) return;
                _ei = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(EI));
                NotifyDependents();
                Update();
            }
        }

        private double _beta0;
        public double Beta0
        {
            get => _beta0;
            set
            {
                if (_beta0 == value) return;
                _beta0 = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(Beta0));
                NotifyDependents();
                Update();
            }
        }

        private double _kh0;
        public double Kh0
        {
            get => _kh0;
            set
            {
                if (_kh0 == value) return;
                _kh0 = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(Kh0));
                NotifyDependents();
                Update();
            }
        }


        private double _kh;
        public double Kh
        {
            get => _kh;
            set
            {
                if (_kh == value) return;
                _kh = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(Kh));
            }
        }

        private double _beta;
        public double Beta
        {
            get => _beta;
            set
            {
                if (_beta == value) return;
                _beta = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(Beta));
            }
        }


        // 杭本数
        private int _number = 1;
        public int Number
        {
            get => _number;
            set
            {
                if (_number == value) return;
                _number = value;
                OnPropertyChanged(nameof(Number));
            }
        }

        private double _h = 0.0; // 地上高
        public double H
        {
            get => _h;
            set
            {
                // 非数を無視し、負値は 0 に丸めて受け付ける（必須要件）
                double sanitized = double.IsFinite(value) ? Math.Max(0.0, value) : _h;
                if (_h == sanitized) return;
                _h = sanitized;

                // 通知と再計算（既存設計に合わせる）
                OnPropertyChanged(nameof(H));
                NotifyDependents();
                Update();
            }
        }

        private double _horizontalLoad; // 水平荷重
        public double HorizontalLoad
        {
            get => _horizontalLoad;
            set
            {
                if (_horizontalLoad == value) return;
                _horizontalLoad = value;
                OnPropertyChanged(nameof(HorizontalLoad));
                NotifyDependents();
                Update();
            }
        }

        private double _ar = 1.0; // 杭頭の固定度
        public double Ar
        {
            get => _ar;
            set
            {
                if (_ar == value) return;
                _ar = value;
                //UpdateDerivedFields();
                OnPropertyChanged(nameof(Ar));
                NotifyDependents();
                Update();
            }
        }

        // 追加: 選択される地盤杭セットの「インデックス（1-based）」と実体参照
        private int _selectedSoilPileIndex = 0; // 0 = 未選択
        public int SelectedSoilPileIndex
        {
            get => _selectedSoilPileIndex;
            set
            {
                if (_selectedSoilPileIndex == value) return;
                _selectedSoilPileIndex = value;
                OnPropertyChanged(nameof(SelectedSoilPileIndex));
                // AssignedSoilPile は ViewModel が解決してセットする想定のため、
                // モデル内ではここで変換は行いません（ViewModel が PropertyChanged を監視して割当します）。
            }
        }

        private ChangSoilPile? _assignedSoilPile;
        public ChangSoilPile? AssignedSoilPile
        {
            get => _assignedSoilPile;
            set
            {
                if (ReferenceEquals(_assignedSoilPile, value)) return;
                _assignedSoilPile = value;
                OnPropertyChanged(nameof(AssignedSoilPile));

                // Nullチェックを追加して NRE を防止
                if (_assignedSoilPile == null)
                {
                    // 割当解除時は現在の EI/Kh0/Beta0 をそのまま保持する設計。
                    // 必要ならここで初期化する（例: EI = 0; Kh0 = 0; Beta0 = 0;）
                    return;
                }

                // 安全に AssignedSoilPile の値を読み取って反映
                // EI: 有効な数値なら設定
                try
                {
                    double assignedEI = _assignedSoilPile.EI;
                    if (double.IsFinite(assignedEI) && assignedEI > 0.0)
                        EI = assignedEI;
                }
                catch
                {
                    // 読み取り失敗は無視
                }

                // Kh0: 有効なら設定
                try
                {
                    double assignedKh0 = _assignedSoilPile.Kh0;
                    if (double.IsFinite(assignedKh0))
                        Kh0 = assignedKh0;
                }
                catch
                {
                    // 無視
                }

                // Beta0: OuterDiameter と EI/Kh0 が有効な場合に計算
                try
                {
                    double od = _assignedSoilPile.OuterDiameter;
                    if (od > 0.0 && double.IsFinite(EI) && EI > 0.0 && double.IsFinite(Kh0) && Kh0 > 0.0)
                    {
                        double d = od / 1000.0; // m
                        double val = Math.Pow(Kh0 * d / (4 * EI), 1.0 / 4.0);
                        if (double.IsFinite(val) && val > 0.0)
                            Beta0 = val;
                    }
                }
                catch
                {
                    // 無視
                }
            }
        }

        // 既存のコンストラクタ（内部フィールドに代入）
        public Chang(double _EI, double _beta, double _h, double _horizontalLoad, double _ar)
        {
            _ei = _EI;
            _beta = _beta;
            _h = _h;
            _horizontalLoad = _horizontalLoad;
            _ar = _ar;
            //UpdateDerivedFields();
            Update();
        }

        // 通知ヘルパ：計算結果プロパティがある場合はここで OnPropertyChanged を追加
        private void NotifyDependents()
        {
            OnPropertyChanged(nameof(PileHeadDisplacement));
            OnPropertyChanged(nameof(GroundSurfaceDisplacement));
            OnPropertyChanged(nameof(PileHeadMoment));
            OnPropertyChanged(nameof(MaxBendingMoment));
            OnPropertyChanged(nameof(DepthOfMaxBendingMoment));
        }

        // 杭頭変位
        public double PileHeadDisplacement
        {
            get => _pileHeadDisplacement;
            set
            {
                if (_pileHeadDisplacement == value) return;
                _pileHeadDisplacement = value;
                OnPropertyChanged(nameof(PileHeadDisplacement));
            }
        }

        // 杭頭拘束モーメント
        public double PileHeadMoment
        {
            get => _pileHeadMoment;
            set
            {
                if (_pileHeadMoment == value) return;
                _pileHeadMoment = value;
                OnPropertyChanged(nameof(PileHeadMoment));
            }
        }

        // 地表面杭変位
        public double GroundSurfaceDisplacement
        {
            get => _groundSurfaceDisplacement;
            set
            {
                if (_groundSurfaceDisplacement == value) return;
                _groundSurfaceDisplacement = value;
                OnPropertyChanged(nameof(GroundSurfaceDisplacement));
            }
        }

        // 地中部最大曲げモーメント発生深さ
        public double DepthOfMaxBendingMoment
        {
            get => _depthOfMaxBendingMoment;
            set
            {
                if (_depthOfMaxBendingMoment == value) return;
                _depthOfMaxBendingMoment = value;
                OnPropertyChanged(nameof(DepthOfMaxBendingMoment));
            }
        }

        // 地中部最大曲げモーメント
        public double MaxBendingMoment
        {
            get => _maxBendingMoment;
            set
            {
                if (_maxBendingMoment == value) return;
                _maxBendingMoment = value;
                OnPropertyChanged(nameof(MaxBendingMoment));
            }
        }


        public void Update()
        {
            // 反復して Kh と Beta を求める（GroundSurfaceDisplacement を使う関係のため）
            const int maxIter = 500;
            const double tol = 1e-6;

            double beta = Math.Max(1e-12, Beta0); // 初期値
            double kh = Kh0; // 初期値

            for (int iter = 0; iter < maxIter; iter++)
            {
                // 地表変位を beta を使って評価
                double groundDisp = ComputeGroundSurfaceDisplacement(beta);

                if (!(double.IsFinite(groundDisp) && groundDisp > 0.0))
                {
                    // 安全装置: 収束不能なら既定値で終了
                    kh = Kh0;
                    beta = Beta0;
                    break;
                }

                double newKh = groundDisp < 0.1 ? Kh0 : Kh0 / Math.Sqrt(groundDisp / 0.01);
                if (!(double.IsFinite(newKh) && newKh > 0.0))
                {
                    newKh = kh;
                }

                double newBeta = Beta0 * Math.Pow(newKh / Kh0, 1.0 / 4.0);

                // 収束判定（相対変化）
                double relChange = Math.Abs(newBeta - beta) / (Math.Abs(beta) + 1e-12);
                beta = newBeta;
                kh = newKh;
                if (relChange <= tol) break;
            }
            Kh = kh;
            Beta = beta;
            // 収束した beta を用いてキャッシュを計算して格納
            PileHeadDisplacement = ComputePileHeadDisplacement(beta);
            GroundSurfaceDisplacement = ComputeGroundSurfaceDisplacement(beta);
            PileHeadMoment = ComputePileHeadMoment(beta);
            MaxBendingMoment = ComputeMaxBendingMoment(beta);
            DepthOfMaxBendingMoment = ComputeDepthOfMaxBendingMoment(beta);

            // Notify UI
            NotifyDependents();


        }


        // 以下、beta を引数に取る計算メソッド群（元の式を beta 依存にしたもの）
        private double ComputePileHeadDisplacement(double beta)
        {
            if (beta == 0.0) return double.NaN;
            if (H == 0)
            {
                return HorizontalLoad / (4 * EI * beta * beta * beta) * (2 - Ar);
            }
            else
            {
                return HorizontalLoad / (12 * EI * beta * beta * beta) * (Math.Pow(1 + beta * H, 3) * (4 - 3 * Ar) + 2);
            }
        }

        private double ComputeGroundSurfaceDisplacement(double beta)
        {
            if (beta == 0.0) return double.NaN;
            if (H == 0) return ComputePileHeadDisplacement(beta);
            return HorizontalLoad / (4 * EI * beta * beta * beta) * (1 + beta * H) * (2 - Ar);
        }

        private double ComputePileHeadMoment(double beta)
        {
            if (beta == 0.0) return double.NaN;
            if (H == 0) return HorizontalLoad / (2 * beta) * Ar;
            return HorizontalLoad / (2 * beta) * (1 + beta * H) * Ar;
        }

        private double ComputeMaxBendingMoment(double beta)
        {
            if (beta == 0.0) return double.NaN;
            if (H == 0)
            {
                return -HorizontalLoad / (2 * Beta) * Math.Sqrt((1 - Ar) * (1 - Ar) + 1) * Math.Exp(-Math.Atan(1 / (1 - Ar)));
            }
            else
            {
                return -HorizontalLoad / (2 * Beta) * Math.Sqrt(Math.Pow(((1 + 2 * Beta * H) - (1 + Beta * H) * Ar), 2) + 1) * Math.Exp(-Math.Atan(1 / (1 + 2 * Beta * H - (1 + Beta * H) * Ar)));
            }
        }

        private double ComputeDepthOfMaxBendingMoment(double beta)
        {
            if (beta == 0.0) return double.NaN;
            if (H == 0) return 1 / beta * Math.Atan(1 / (1 - Ar));
            return 1 / beta * Math.Atan(1 / (1 + 2 * Beta * H - (1 + Beta * H) * Ar));
        }

        // たわみ曲線
        public double GetDeflection(double x)
        {
            if (H == 0)
            {
                return HorizontalLoad / (4 * EI * Beta * Beta * Beta) * Math.Exp(-Beta * x) * ((2 - Ar) * Math.Cos(Beta * x) + Ar * Math.Sin(Beta * x));
            }
            else if (x < 0)
            {
                return HorizontalLoad / (12 * EI * Beta * Beta * Beta) * (Math.Pow((1 + Beta * H), 3) * (4 - 3 * Ar) + 2
                    - 6 * Beta * (1 + Beta * H) * (1 + Beta * H) * (1 - Ar) * (H + x)
                    - 3 * Beta * Beta * (1 + Beta * H) * Ar * (H + x) * (H + x) + 2 * Math.Pow(Beta, 3) * Math.Pow(H + x, 3));
            }
            else
            {
                return HorizontalLoad / (4 * EI * Beta * Beta * Beta) * Math.Exp(-Beta * H) * ((1 + Beta * H) * (2 - Ar) * Math.Cos(Beta * x)
                    - (2 * Beta * H - (1 + Beta * H) * Ar) * Math.Sin(Beta * x));
                // たわみ曲線の計算
                // ここでは簡単な例として、たわみ曲線を直線で近似
            }
        }

        // 杭頭変位
        public double GetPileHeadDisplacement()
        {
            if (H == 0)
            {
                return HorizontalLoad / (4 * EI * Beta * Beta * Beta) * (2 - Ar);
            }

            else
            {
                return HorizontalLoad / (12 * EI * Beta * Beta * Beta) * (Math.Pow(1 + Beta * H, 3) * (4 - 3 * Ar) + 2);
            }
        }

        // 地表面変位
        public double GetGroundSurfaceDisplacement()
        {
            if (H == 0)
            {
                return GetPileHeadDisplacement();
            }
            else
            {
                return HorizontalLoad / (4 * EI * Beta * Beta * Beta) * (1 + Beta * H) * (2 - Ar);
            }
        }

        // 杭頭拘束モーメント
        public double GetPileHeadMoment()
        {
            if (H == 0)
            {
                return HorizontalLoad / (2 * Beta) * Ar;
            }
            else
            {
                return HorizontalLoad / (2 * Beta) * (1 + Beta * H) * Ar;
            }
        }

        // 杭各部の曲げモーメント
        public double GetBendingMoment(double x)
        {
            if (H == 0)
            {
                return -HorizontalLoad / (2 * Beta) * Math.Exp(-Beta * x) * ((2 - Ar) * Math.Sin(Beta * x) - Ar * Math.Cos(Beta * x));
            }
            else if (x < 0)
            {
                return HorizontalLoad / (2 * Beta) * ((1 + Beta * H) * Ar - 2 * Beta * (H + x));

            }
            else
            {
                return -HorizontalLoad / (2 * Beta) * Math.Exp(-Beta * x) * ((1 + Beta * H) * (2 - Ar) * Math.Sin(Beta * x)
                    + (2 * Beta * H - (1 + Beta * H) * Ar) * Math.Cos(Beta * x));
            }
        }
        // 地中部最大曲げモーメント
        public double GetMaxBendingMoment()
        {
            if (H == 0)
            {
                return -HorizontalLoad / (2 * Beta) * Math.Sqrt((1 - Ar) * (1 - Ar) + 1) * Math.Exp(-Math.Atan(1 / (1 - Ar)));
            }
            else
            {
                return -HorizontalLoad / (2 * Beta) * Math.Sqrt(Math.Pow(((1 + 2 * Beta * H) - (1 + Beta * H) * Ar), 2) + 1) * Math.Exp(-Math.Atan(1 / (1 + 2 * Beta * H - (1 + Beta * H) * Ar)));
            }
        }
        // Mmを生じる深さ
        public double GetDepthOfMaxBendingMoment()
        {
            if (H == 0)
            {
                return 1 / Beta * Math.Atan(1 / (1 - Ar));
            }
            else
            {
                return 1 / Beta * Math.Atan(1 / (1 + 2 * Beta * H - (1 + Beta * H) * Ar));
            }
        }

        // 杭各部のせん断力
        public double GetShearForce(double x)
        {
            if (H == 0)
            {
                return -HorizontalLoad * Math.Exp(-Beta * x) * (Math.Cos(Beta * x) - (1 - Ar) * Math.Sin(Beta * x));
            }
            else if (x < 0)
            {
                return -HorizontalLoad;
            }
            else
            {
                return -HorizontalLoad / 2 * Math.Exp(-Beta * H) * ((1 + Beta * H) * (2 - Ar) * (Math.Cos(Beta * x) - Math.Sin(Beta * x))
                    - (2 * Beta * H - (1 + Beta * H) * Ar) * (Math.Cos(Beta * x) - Math.Sin(Beta * x)));
            }
        }

        public double GetHorizontalForce(double displacement)
        {
            double kh = displacement < 0.01 ? Kh0 : Kh0 / Math.Sqrt(displacement / 0.01);
            double beta = Beta0 * Math.Pow(kh / Kh0, 1.0 / 4.0);
            return 12 * EI * Math.Pow(beta, 3) / (Math.Pow(1 + beta * H, 3) * (4 - 3 * Ar) + 2) * displacement;
        }


    }
}