using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PileDesign.Views
{
    /// <summary>
    /// INPUT (キーボード・マウス) をリアルタイムに可視化するフローティング小ウィンドウ。
    /// メインウィンドウのプレビューイベントを購読して状態表示する。
    /// 本体はクリックスルー (HTTRANSPARENT) で下のキャンバスにクリックが伝わる。
    /// タイトルバー領域のみドラッグ・閉じるボタンが効く。
    /// </summary>
    public partial class InputVisualizerWindow : Window
    {
        private const int WheelDisplayDurationMs = 400;
        private const int FadeDurationMs = 350;
        private const int CurrentKeyHoldDurationMs = 1000;  // 解放後にこの時間ホールドしてからフェード

        // 押下時はシアン系明色 (グラスフィルム上で輝くように)
        private static readonly Color PressedColor = Color.FromArgb(0xFF, 0x33, 0xC8, 0xFF);
        // 通常時はうっすら青みのある透明色 (XAML 既定 #80E8F4FC と整合)
        private static readonly Color NormalColor = Color.FromArgb(0x80, 0xE8, 0xF4, 0xFC);
        // ホイール非アクティブ時の矢印色 (薄青グレー)
        private static readonly Color WheelInactiveColor = Color.FromArgb(0xA8, 0x8F, 0xB7, 0xCC);
        // 左右マウスボタンは「ハイライト Path」が上 2/5 にだけ重なる構成のため、
        // 解放時はハイライトを完全に透明にして下層の常時表示パスを露出させる
        private static readonly Color MouseNormalColor = Colors.Transparent;
        // 中ボタン (ホイール) 通常時の薄青グレー
        private static readonly Color MouseMiddleNormalColor = Color.FromArgb(0xB0, 0xCF, 0xE5, 0xF2);

        private readonly Window _ownerWindow;
        private readonly DispatcherTimer _wheelTimer;

        // アプリ内すべてのウィンドウから入力をキャプチャするための購読管理
        private readonly HashSet<Window> _subscribedWindows = new();

        // マウスボタン取り逃し対策: 押下中フラグ + ポーリングタイマ
        // (PreviewMouseUp が発火しないシナリオで取り残された "押下色" を解放する)
        private bool _leftPressed;
        private bool _rightPressed;
        private bool _middlePressed;
        private readonly DispatcherTimer _mousePollTimer;
        private const int MousePollIntervalMs = 100;

        // 「現在キー」表示のホールド用タイマ (解放後に少し残してから消す)
        private readonly DispatcherTimer _currentKeyHoldTimer;

        // 現在表示中のキー (取り逃しチェック用)
        private Key? _currentKeyDisplayed;

        // Win32: GetAsyncKeyState で OS レベルのキー状態を確実に取得
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool IsKeyActuallyDown(Key key)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        public InputVisualizerWindow(Window owner)
        {
            InitializeComponent();
            _ownerWindow = owner;
            Owner = owner;

            _wheelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(WheelDisplayDurationMs) };
            _wheelTimer.Tick += (s, e) => { _wheelTimer.Stop(); ResetWheelDisplay(); };

            // マウスボタン状態ポーリング (取り逃し PreviewMouseUp の補完)
            _mousePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(MousePollIntervalMs) };
            _mousePollTimer.Tick += OnMousePollTick;

            // 現在キー表示のホールドタイマ
            _currentKeyHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CurrentKeyHoldDurationMs) };
            _currentKeyHoldTimer.Tick += OnCurrentKeyHoldTick;

            Loaded += OnLoaded;
            Closed += OnClosed;
            SourceInitialized += OnSourceInitialized;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 既定位置: オーナーの右下
            PositionAtOwnerBottomRight();

            // 既存のすべてのアプリ内ウィンドウを購読 (同一 UI スレッドのみ)
            ScanAndSubscribeMainThreadWindows();

            // マウスボタン + 新規ウィンドウのポーリング開始
            _mousePollTimer.Start();
        }

        /// <summary>
        /// Application.Current.Windows を走査し、自分と同じ UI スレッドの未購読ウィンドウを購読する。
        /// 別 UI スレッド (HelpWindow / VerificationWindow など) は除外する。
        /// </summary>
        private void ScanAndSubscribeMainThreadWindows()
        {
            // スナップショットを取って iteration 中の変更に強くする
            var snapshot = new List<Window>();
            try
            {
                foreach (Window w in Application.Current.Windows)
                    snapshot.Add(w);
            }
            catch { /* 競合があっても無視 */ }

            foreach (var w in snapshot)
            {
                AttachInputListeners(w);
            }
        }

        private void AttachInputListeners(Window w)
        {
            if (w == this) return;                  // 自分自身はスキップ
            if (_subscribedWindows.Contains(w)) return;

            // 別 UI スレッドのウィンドウ (例: HelpWindow / VerificationWindow) は購読しない。
            // 同一 Dispatcher でないと event 購読時にクロススレッド例外が発生する。
            if (w.Dispatcher != this.Dispatcher) return;

            _subscribedWindows.Add(w);

            // handledEventsToo: true で、他のハンドラが e.Handled = true にしても
            // 必ず通知を受け取るようにする (例: Ctrl+Shift+T でショートカットコマンド発火後も
            // 入力可視化が反応する)
            w.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown), true);
            w.AddHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler(OnPreviewKeyUp), true);
            w.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), true);
            w.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp), true);
            w.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), true);
            w.Closed += OnSubscribedWindowClosed;
        }

        private void DetachInputListeners(Window w)
        {
            if (!_subscribedWindows.Remove(w)) return;
            w.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
            w.RemoveHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler(OnPreviewKeyUp));
            w.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown));
            w.RemoveHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp));
            w.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
            w.Closed -= OnSubscribedWindowClosed;
        }

        private void OnSubscribedWindowClosed(object? sender, EventArgs e)
        {
            if (sender is Window w) DetachInputListeners(w);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _wheelTimer.Stop();
            _mousePollTimer.Stop();
            _currentKeyHoldTimer.Stop();
            // 購読中の全ウィンドウから解除
            foreach (var w in _subscribedWindows.ToArray())
            {
                w.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
                w.RemoveHandler(UIElement.PreviewKeyUpEvent, new KeyEventHandler(OnPreviewKeyUp));
                w.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown));
                w.RemoveHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp));
                w.RemoveHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
                w.Closed -= OnSubscribedWindowClosed;
            }
            _subscribedWindows.Clear();
        }

        private void PositionAtOwnerBottomRight()
        {
            // SizeToContent="WidthAndHeight" のため Loaded 直後では ActualWidth/Height が確定済み
            var ownerLeft = _ownerWindow.Left;
            var ownerTop = _ownerWindow.Top;
            var ownerWidth = _ownerWindow.ActualWidth;
            var ownerHeight = _ownerWindow.ActualHeight;
            const double margin = 24;
            Left = ownerLeft + ownerWidth - ActualWidth - margin;
            Top = ownerTop + ownerHeight - ActualHeight - margin;
        }

        // -- Win32 hit-test: ドラッグハンドル / 閉じるボタン以外を click-through 化 --

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // ツールウィンドウ化 (Alt+Tab に出ない) + アクティブ化抑制
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

            var src = HwndSource.FromHwnd(hwnd);
            src?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST) return IntPtr.Zero;

            // lParam: 下位16bit=X, 上位16bit=Y (スクリーン座標, 符号付)
            int lp = lParam.ToInt32();
            short screenX = (short)(lp & 0xFFFF);
            short screenY = (short)((lp >> 16) & 0xFFFF);
            Point screenPt = new(screenX, screenY);

            try
            {
                var local = PointFromScreen(screenPt);

                if (CloseButton != null && IsPointInElement(local, CloseButton))
                {
                    handled = true;
                    return new IntPtr(HTCLIENT);
                }
                if (DragHandle != null && IsPointInElement(local, DragHandle))
                {
                    handled = true;
                    return new IntPtr(HTCAPTION);
                }
            }
            catch
            {
                // 座標変換失敗時はクリックスルー
            }

            handled = true;
            return new IntPtr(HTTRANSPARENT);
        }

        private bool IsPointInElement(Point windowLocalPt, FrameworkElement el)
        {
            if (el.ActualWidth <= 0 || el.ActualHeight <= 0) return false;
            var topLeft = el.TranslatePoint(new Point(0, 0), this);
            var bounds = new Rect(topLeft, new Size(el.ActualWidth, el.ActualHeight));
            return bounds.Contains(windowLocalPt);
        }

        // -- 閉じるボタン --
        private void CloseButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // ViewModel 側のトグルプロパティを下げる: MainWindow が Close() を呼んでくれる
            if (_ownerWindow.DataContext is ViewModels.MainWindowViewModel vm)
            {
                vm.IsInputVisualizerVisible = false;
            }
            else
            {
                Close();
            }
        }
        private void CloseButton_MouseEnter(object sender, MouseEventArgs e)
        {
            if (CloseButton != null)
                CloseButton.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0x6A, 0x6A));
        }
        private void CloseButton_MouseLeave(object sender, MouseEventArgs e)
        {
            if (CloseButton != null)
                CloseButton.Background = Brushes.Transparent;
        }

        // -- キーボード --
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            UpdateModifierKeys();
            UpdateCurrentKey(e.Key, true);
        }
        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            UpdateModifierKeys();
            UpdateCurrentKey(e.Key, false);
        }

        private void UpdateModifierKeys()
        {
            SetKeyState(CtrlKeyBrush, Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl));
            SetKeyState(ShiftKeyBrush, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
            SetKeyState(AltKeyBrush, Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt));
        }

        private void SetKeyState(SolidColorBrush brush, bool pressed)
        {
            if (pressed)
                SetBrushColorImmediate(brush, PressedColor);
            else
                FadeBrushTo(brush, NormalColor);
        }

        private void UpdateCurrentKey(Key key, bool isPressed)
        {
            if (IsModifier(key) || key == Key.System) return;

            if (isPressed)
            {
                // 押下時: ホールドタイマをキャンセルして即時表示
                _currentKeyHoldTimer.Stop();
                _currentKeyDisplayed = key;
                CurrentKeyBorder.Visibility = Visibility.Visible;
                CurrentKeyText.Text = GetKeyDisplayName(key);
                SetBrushColorImmediate(CurrentKeyBrush, PressedColor);
            }
            else
            {
                // 解放時: ホールドタイマ起動 (1 秒経過後にフェード開始)
                _currentKeyDisplayed = null;
                _currentKeyHoldTimer.Stop();
                _currentKeyHoldTimer.Start();
            }
        }

        private void OnCurrentKeyHoldTick(object? sender, EventArgs e)
        {
            _currentKeyHoldTimer.Stop();
            FadeBrushTo(CurrentKeyBrush, NormalColor,
                onComplete: () => CurrentKeyBorder.Visibility = Visibility.Hidden);
        }

        // -- ブラシ色更新ヘルパー --
        // Brush.Color には「base 値」と「アニメ中の現在値」の 2 層があり、
        // BeginAnimation(null) でアニメを除去すると base 値に瞬間スナップする。
        // フェード完了時に base 値も commit しておかないと、次回 BeginAnimation(null) で
        // 古い PressedColor に一瞬戻って "他のボタン/キーが青く光る" 現象が起きる。
        //
        // ただし fade 中に再 press されると古い fade の Completed が遅延発火して、
        // 新しい cyan 設定を transparent に上書きしてしまう race が発生する。
        // → トークン辞書で「最新の fade だけ Completed を反映」するガードを設置。
        private readonly Dictionary<SolidColorBrush, object> _activeFadeTokens = new();

        private void SetBrushColorImmediate(SolidColorBrush brush, Color color)
        {
            // fade トークン無効化 (もし fade 進行中なら、その Completed は無視される)
            _activeFadeTokens.Remove(brush);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            brush.Color = color;
        }

        private void FadeBrushTo(SolidColorBrush brush, Color targetColor, Action? onComplete = null)
        {
            // 新トークンを発行 (これが最新であることを Completed 内で確認する)
            var token = new object();
            _activeFadeTokens[brush] = token;

            var anim = new ColorAnimation
            {
                To = targetColor,
                Duration = TimeSpan.FromMilliseconds(FadeDurationMs),
                EasingFunction = new QuadraticEase()
            };
            anim.Completed += (s, e) =>
            {
                // 最新トークンと一致しなければ、この fade はキャンセル/上書き済み
                if (!_activeFadeTokens.TryGetValue(brush, out var current) || current != token)
                    return;

                _activeFadeTokens.Remove(brush);
                brush.Color = targetColor;
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                onComplete?.Invoke();
            };
            // SnapshotAndReplace: 現在の表示値からシームレスに引き継ぐ
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private static bool IsModifier(Key key) =>
            key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt;

        // -- マウス --
        // 重要: 全ボタンを polling で再評価すると、押下直後の Mouse.LeftButton 評価が
        //       Released を返すレースが起きて青→灰のフリッカーになる。
        //       e.ChangedButton + e.ButtonState で「変化したボタンだけ」更新する。
        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            UpdateOneMouseButton(e.ChangedButton, true);
            SetPressedFlag(e.ChangedButton, true);
        }
        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            UpdateOneMouseButton(e.ChangedButton, false);
            SetPressedFlag(e.ChangedButton, false);
        }

        private void SetPressedFlag(MouseButton button, bool pressed)
        {
            switch (button)
            {
                case MouseButton.Left:   _leftPressed   = pressed; break;
                case MouseButton.Right:  _rightPressed  = pressed; break;
                case MouseButton.Middle: _middlePressed = pressed; break;
            }
        }

        /// <summary>
        /// 取り逃し補完ポーリング: マウス・キーボードの "解放" を OS 状態で確認し、
        /// 取り残された "押下色" を解放する。
        /// 「押下を反映する」ロジックは入れない (フリッカーや誤検知の温床になるため)。
        /// </summary>
        private void OnMousePollTick(object? sender, EventArgs e)
        {
            // ----- マウスボタン取り逃し補完 -----
            if (_leftPressed && Mouse.LeftButton == MouseButtonState.Released)
            {
                UpdateOneMouseButton(MouseButton.Left, false);
                _leftPressed = false;
            }
            if (_rightPressed && Mouse.RightButton == MouseButtonState.Released)
            {
                UpdateOneMouseButton(MouseButton.Right, false);
                _rightPressed = false;
            }
            if (_middlePressed && Mouse.MiddleButton == MouseButtonState.Released)
            {
                UpdateOneMouseButton(MouseButton.Middle, false);
                _middlePressed = false;
            }

            // ----- 修飾キー取り逃し補完 (Win32 GetAsyncKeyState で OS 状態を確認) -----
            // PreviewKeyUp が他のウィンドウ (ダイアログ等) に取られて発火しなかったケースを救済
            SetKeyState(CtrlKeyBrush, IsKeyActuallyDown(Key.LeftCtrl) || IsKeyActuallyDown(Key.RightCtrl));
            SetKeyState(ShiftKeyBrush, IsKeyActuallyDown(Key.LeftShift) || IsKeyActuallyDown(Key.RightShift));
            SetKeyState(AltKeyBrush, IsKeyActuallyDown(Key.LeftAlt) || IsKeyActuallyDown(Key.RightAlt));

            // ----- 現在キー (一般キー) 取り逃し補完 -----
            if (_currentKeyDisplayed.HasValue && !IsKeyActuallyDown(_currentKeyDisplayed.Value))
            {
                // OS は "解放" だが我々はまだ "押下中" として表示している → release を起動
                UpdateCurrentKey(_currentKeyDisplayed.Value, false);
            }

            // ----- 新規ウィンドウ自動購読 (クラスハンドラの代替) -----
            // 後から開かれた水平解析・沈下解析等のウィンドウを 100ms 以内に検出して購読
            ScanAndSubscribeMainThreadWindows();
        }
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                AnimateWheel(WheelUpBrush, WheelDownBrush);
            }
            else if (e.Delta < 0)
            {
                AnimateWheel(WheelDownBrush, WheelUpBrush);
            }
            _wheelTimer.Stop();
            _wheelTimer.Start();
        }

        private void AnimateWheel(SolidColorBrush active, SolidColorBrush inactive)
        {
            SetBrushColorImmediate(active, PressedColor);
            SetBrushColorImmediate(inactive, WheelInactiveColor);
        }

        private void UpdateOneMouseButton(MouseButton button, bool pressed)
        {
            switch (button)
            {
                case MouseButton.Left:
                    SetMouseButtonState(LeftMouseBrush, pressed, MouseNormalColor);
                    break;
                case MouseButton.Right:
                    SetMouseButtonState(RightMouseBrush, pressed, MouseNormalColor);
                    break;
                case MouseButton.Middle:
                    SetMouseButtonState(MiddleMouseBrush, pressed, MouseMiddleNormalColor);
                    break;
            }
        }

        private void SetMouseButtonState(SolidColorBrush brush, bool pressed, Color normal)
        {
            if (pressed)
                SetBrushColorImmediate(brush, PressedColor);
            else
                FadeBrushTo(brush, normal);
        }

        private void ResetWheelDisplay()
        {
            FadeBrushTo(WheelUpBrush, WheelInactiveColor);
            FadeBrushTo(WheelDownBrush, WheelInactiveColor);
        }

        // -- キー表示名 (現在押下キーの一時表示用) --
        private static string GetKeyDisplayName(Key key) => key switch
        {
            Key.Space => "Space",
            Key.Return or Key.Enter => "Enter",
            Key.Escape => "Esc",
            Key.Back => "BS",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Left => "←",
            Key.Right => "→",
            Key.Tab => "Tab",
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            Key.D0 => "0", Key.D1 => "1", Key.D2 => "2", Key.D3 => "3", Key.D4 => "4",
            Key.D5 => "5", Key.D6 => "6", Key.D7 => "7", Key.D8 => "8", Key.D9 => "9",
            Key.OemPlus => "+",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
    }
}
