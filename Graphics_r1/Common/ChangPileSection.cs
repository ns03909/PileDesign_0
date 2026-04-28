using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;


using Serilog;
namespace PileDesign.Common
{
    public class ChangSoilPile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private double _thickness = 120;
        public double Thickness
        {
            get => _thickness;
            set
            {
                // IsHollow が false（中実）のときは外部から変更不可にする
                if (!IsHollow) return;

                if (_thickness == value) return;
                _thickness = value;
                OnPropertyChanged(nameof(Thickness));
                // Thickness が EI に影響する（EI 計算で使用）なら EI も通知
                NotifyEIChanged();

                // Thickness 変更後に SteelThickness が上回っていたら修正
                EnsureSteelThicknessWithinBounds();
            }
        }

        private double _prevThickness;
        public double PrevThickness
        {
            get => _prevThickness;
            set
            {
                if (_prevThickness == value) return;
                _prevThickness = value;
                OnPropertyChanged(nameof(PrevThickness));
            }
        }


        private bool _isHollow = true;
        public bool IsHollow
        {
            get => _isHollow;
            set
            {
                if (_isHollow == value) return;

                _isHollow = value;

                if (!_isHollow) // 中実
                {
                    var currentThickness = Thickness;
                    PrevThickness = currentThickness;
                    // 直接フィールド更新してから通知（setter が制限する場合があるため）
                    _thickness = OuterDiameter / 2.0;
                    OnPropertyChanged(nameof(Thickness));
                    NotifyEIChanged();
                }
                else // 中空
                {
                    _thickness = PrevThickness;
                    OnPropertyChanged(nameof(Thickness));
                    NotifyEIChanged();
                }
                OnPropertyChanged(nameof(IsHollow));
            }
        }

        private double _steelThickness = 6.0;
        public double SteelThickness
        {
            get => _steelThickness;
            set
            {
                double clamped = Math.Max(0.0, Math.Min(value, Thickness));
                if (_steelThickness == clamped) return;
                _steelThickness = clamped;
                OnPropertyChanged(nameof(SteelThickness));
                NotifyEIChanged();
            }
        }

        //
        private double _fc = 85.0;
        public double Fc
        {
            get => _fc;
            set
            {
                if (_fc == value) return;
                _fc = value;
                OnPropertyChanged(nameof(Fc));
                RecalculateEc();
                NotifyEIChanged();
                RecalculateKh0();
            }

        }

        private double _gamma = 25.0;
        public double Gamma
        {
            get => _gamma;
            set
            {
                if (_gamma == value) return;
                _gamma = value;
                OnPropertyChanged(nameof(Gamma));
                RecalculateEc();
                NotifyEIChanged();
                RecalculateKh0();
            }
        }

        private double _ec;
        public double Ec
        {
            get => _ec;
            private set
            {
                if (_ec == value) return;
                _ec = value;
                OnPropertyChanged(nameof(Ec));
                // Ec が変わると EI にも影響するので EI 通知
                NotifyEIChanged();
            }
        }

        private void RecalculateEc()
        {
            if (Fc >= 85)
            {
                Ec = 40_000;
                return;
            }

            // 安全チェック
            if (!double.IsFinite(Gamma) || Gamma <= 0.0 || !double.IsFinite(Fc) || Fc <= 0.0)
            {
                Ec = 0.0;
                return;
            }

            Ec = 3.35 * Math.Pow(10, 4) * Math.Pow(Gamma / 24.0, 2) * Math.Pow(Fc / 60.0, 1.0 / 3.0);
        }

        public static double Es => 20_500;

        // 手動入力フラグと値
        private bool _useManualEI = false;
        private double _manualEI = 0.0;

        // EI: 手動入力があればその値を返し、なければ計算式で返す
        public double EI
        {
            get => _useManualEI ? _manualEI : ComputeEI();
            set
            {
                // 設定が既に手動で同じ値なら何もしない
                if (_useManualEI && Math.Abs(_manualEI - value) < 1e-12) return;

                // 負値は 0 にクランプ
                double sanitized = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
                _manualEI = sanitized;
                _useManualEI = true;
                OnPropertyChanged(nameof(EI));
            }
        }

        // 手動入力を解除して計算値に戻す
        public void ClearManualEI()
        {
            if (!_useManualEI) return;
            _useManualEI = false;
            OnPropertyChanged(nameof(EI));
        }

        // EI の計算本体（従来の式を移動）
        private double ComputeEI()
        {
            try
            {
                return Math.PI / 64.0 * (
                    (Math.Pow(OuterDiameter, 4) - Math.Pow(OuterDiameter - 2 * SteelThickness, 4)) * Es +
                    (Math.Pow(OuterDiameter - 2 * SteelThickness, 4) - Math.Pow(OuterDiameter - 2 * Thickness, 4)) * Ec
                    ) / Math.Pow(10, 9); // kNm2
            }
            catch
            {
                return 0.0;
            }
        }

        // OuterDiameter 等の変更時に EI を通知するユーティリティ（手動入力中は通知を抑止）
        private void NotifyEIChanged()
        {
            if (!_useManualEI)
                OnPropertyChanged(nameof(EI));
        }

        private double _outerDiameter = 1000.0;
        public double OuterDiameter
        {
            get => _outerDiameter;
            set
            {
                if (_outerDiameter == value) return;
                _outerDiameter = value;
                OnPropertyChanged(nameof(OuterDiameter));

                // 中実 (IsHollow == false) の場合、厚さは外径に依存するため自動更新する
                if (!IsHollow)
                {
                    _thickness = _outerDiameter / 2.0;
                    OnPropertyChanged(nameof(Thickness));
                    OnPropertyChanged(nameof(EI));
                    EnsureSteelThicknessWithinBounds();
                }

                // OuterDiameter の変更は Kh0 に影響するので再計算
                RecalculateKh0();
            }
        }

        //　地盤定数
        private double _alpha = 80;
        public double Alpha
        {
            get => _alpha;
            set
            {
                if (_alpha == value) return;
                _alpha = value;
                OnPropertyChanged(nameof(Alpha));
                RecalculateKh0();
            }
        }

        // 変形係数
        private double _e0 = 10_000;
        public double E0
        {
            get => _e0;
            set
            {
                if (_e0 == value) return;
                _e0 = value;
                OnPropertyChanged(nameof(E0));
                RecalculateKh0();
            }
        }

        private double _xi = 1.0;
        public double Xi
        {
            get => _xi;
            set
            {
                if (_xi == value) return;
                _xi = value;
                OnPropertyChanged(nameof(Xi));
                RecalculateKh0();
            }
        }

        // キャッシュされた Kh0（再計算して通知する設計）
        private double _kh0;
        public double Kh0
        {
            get => _kh0;
            private set
            {
                if (_kh0 == value) return;
                _kh0 = value;
                OnPropertyChanged(nameof(Kh0));
            }
        }

        private void RecalculateKh0()
        {
            try
            {
                // OuterDiameter / 10.0 の負や零を避ける
                double baseVal = OuterDiameter / 10.0;
                if (!(double.IsFinite(baseVal) && baseVal > 0.0))
                {
                    Kh0 = 0.0;
                    return;
                }

                double newKh0 = Alpha * Xi * E0 * Math.Pow(baseVal, -3.0 / 4.0);
                Kh0 = newKh0;
            }
            catch
            {
                // 安全にフォールバック
                Kh0 = 0.0;
            }
        }

        // 保助メソッド：SteelThickness が Thickness を超えないように調整
        private void EnsureSteelThicknessWithinBounds()
        {
            if (_steelThickness > _thickness)
            {
                _steelThickness = _thickness;
                OnPropertyChanged(nameof(SteelThickness));
            }
            if (_steelThickness < 0.0)
            {
                _steelThickness = 0.0;
                OnPropertyChanged(nameof(SteelThickness));
            }
            NotifyEIChanged();
        }


        // 追加: PileTopType プロパティ（DataGrid のコンボボックスにバインド）
        private string _pileTopType = "";
        public string PileTopType
        {
            get => _pileTopType;
            set
            {
                if (_pileTopType == value) return;
                _pileTopType = value;
                OnPropertyChanged(nameof(PileTopType));
            }
        }

        // 追加: 簡易要約表示（必要なら拡張）
        private string _fTPileSummary = "";
        public string FTPileSummary
        {
            get => _fTPileSummary;
            private set
            {
                if (_fTPileSummary == value) return;
                _fTPileSummary = value;
                OnPropertyChanged(nameof(FTPileSummary));
            }
        }

        private string _captainPileSummary = "";
        public string CaptainPileSummary
        {
            get => _captainPileSummary;
            private set
            {
                if (_captainPileSummary == value) return;
                _captainPileSummary = value;
                OnPropertyChanged(nameof(CaptainPileSummary));
            }
        }

        /// <summary>
        /// PileTop のデータがあれば ChangSoilPile の性質に反映します。
        /// - ConcreteOutDia -> OuterDiameter
        /// - ConcreteThickness -> Thickness (中実/中空フラグにより適宜調整)
        /// - PileCapFc / PileCapGamma -> Fc / Gamma
        /// - PileTopType -> PileTopType
        /// - FTPile/CaptainPile の簡易要約を生成
        /// </summary>
        public void ApplyPileTop(PileTop? pileTop)
        {
            if (pileTop == null)
            {
                PileTopType = "";
                FTPileSummary = "";
                CaptainPileSummary = "";
                return;
            }

            try
            {
                // PileTopType を設定
                PileTopType = pileTop.PileTopType ?? "";

                // 外径
                if (pileTop.ConcreteOutDia > 0.0)
                {
                    OuterDiameter = pileTop.ConcreteOutDia;
                }

                // コンクリート肉厚（中空扱いで有効な場合のみセット）
                if (pileTop.ConcreteThickness > 0.0)
                {
                    if (IsHollow)
                    {
                        Thickness = pileTop.ConcreteThickness;
                    }
                    else
                    {
                        PrevThickness = pileTop.ConcreteThickness;
                        // 中実では Thickness は OuterDiameter 依存の実装のままにする
                    }
                }

                // コンクリート特性
                if (pileTop.PileCapFc > 0.0) Fc = pileTop.PileCapFc;
                if (pileTop.PileCapGamma > 0.0) Gamma = pileTop.PileCapGamma;

                // FTPile の要約（存在する場合）
                if (pileTop.FTPile != null)
                {
                    var ft = pileTop.FTPile;
                    double d1 = ft.FTPilePile?.D1 ?? 0.0;
                    double d2 = ft.FTPilePile?.D2 ?? 0.0;
                    int tensionNum = ft.FTPileTensionBars?.TensionAnchorNum ?? 0;
                    FTPileSummary = $"D1={d1:N0}, D2={d2:N0}, Tension={tensionNum}";
                }
                else
                {
                    FTPileSummary = "";
                }

                // CaptainPile の要約（存在する場合） — 公開プロパティをいくつか拾う
                if (pileTop.CaptainPile != null)
                {
                    var cp = pileTop.CaptainPile;
                    var props = cp.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    var parts = new List<string>();
                    foreach (var p in props)
                    {
                        if (parts.Count >= 3) break;
                        try
                        {
                            var val = p.GetValue(cp);
                            if (val == null) continue;
                            if (val is IConvertible)
                            {
                                string s = val is double d ? $"{d:N0}" : val.ToString() ?? "";
                                if (!string.IsNullOrEmpty(s)) parts.Add($"{p.Name}={s}");
                            }
                        }
                        catch (Exception ex) { Log.Warning(ex, "[ChangPileSection] プロパティ読取失敗"); }
                    }
                    CaptainPileSummary = parts.Count > 0 ? string.Join(", ", parts) : cp.GetType().Name;
                }
                else
                {
                    CaptainPileSummary = "";
                }

                RecalculateEc();
                RecalculateKh0();
                NotifyEIChanged();
            }
            catch
            {
                // 応急: 失敗しても無視
            }
        }

        // 追加: OnPropertyChanged 実装（CallerMemberName を使用）
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ChangSoilPile()
        {
            // 初期キャッシュを計算しておく
            RecalculateEc();
            RecalculateKh0();
            EnsureSteelThicknessWithinBounds();

            // 初期の杭頭タイプを "杭頭固定" に設定（新規行追加時の既定値）
            PileTopType = "杭頭固定";
            FTPileSummary = "";
            CaptainPileSummary = "";
        }

    }
}