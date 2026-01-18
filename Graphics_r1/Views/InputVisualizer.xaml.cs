using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PileDesign.Views
{
    /// <summary>
    /// マウスとキーボードの入力状態をリアルタイム表示するコントロール
    /// </summary>
    public partial class InputVisualizer : UserControl
    {
        // 押されている状態の色
        private static readonly SolidColorBrush PressedBrush = new(Color.FromRgb(100, 180, 255));
        private static readonly SolidColorBrush NormalBrush = new(Color.FromRgb(224, 224, 224));
        private static readonly SolidColorBrush WheelActiveBrush = new(Color.FromRgb(100, 180, 255));
        private static readonly SolidColorBrush WheelInactiveBrush = new(Color.FromRgb(224, 224, 224));

        // ホイール表示用タイマー
        private readonly DispatcherTimer _wheelTimer;
        private const int WheelDisplayDurationMs = 300;

        public InputVisualizer()
        {
            InitializeComponent();

            // ホイール表示リセット用タイマー
            _wheelTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(WheelDisplayDurationMs)
            };
            _wheelTimer.Tick += (s, e) =>
            {
                _wheelTimer.Stop();
                ResetWheelDisplay();
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // メインウィンドウのイベントをフック
            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
            {
                mainWindow.PreviewKeyDown += OnPreviewKeyDown;
                mainWindow.PreviewKeyUp += OnPreviewKeyUp;
                mainWindow.PreviewMouseDown += OnPreviewMouseDown;
                mainWindow.PreviewMouseUp += OnPreviewMouseUp;
                mainWindow.PreviewMouseWheel += OnPreviewMouseWheel;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _wheelTimer.Stop();

            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
            {
                mainWindow.PreviewKeyDown -= OnPreviewKeyDown;
                mainWindow.PreviewKeyUp -= OnPreviewKeyUp;
                mainWindow.PreviewMouseDown -= OnPreviewMouseDown;
                mainWindow.PreviewMouseUp -= OnPreviewMouseUp;
                mainWindow.PreviewMouseWheel -= OnPreviewMouseWheel;
            }
        }

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

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            UpdateMouseButtons();
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            UpdateMouseButtons();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // ホイール回転方向を表示
            if (e.Delta > 0)
            {
                // 上方向回転
                WheelUpArrow.Foreground = WheelActiveBrush;
                WheelDownArrow.Foreground = WheelInactiveBrush;
            }
            else if (e.Delta < 0)
            {
                // 下方向回転
                WheelUpArrow.Foreground = WheelInactiveBrush;
                WheelDownArrow.Foreground = WheelActiveBrush;
            }

            // タイマーをリセットして再スタート
            _wheelTimer.Stop();
            _wheelTimer.Start();
        }

        private void ResetWheelDisplay()
        {
            WheelUpArrow.Foreground = WheelInactiveBrush;
            WheelDownArrow.Foreground = WheelInactiveBrush;
        }

        private void UpdateModifierKeys()
        {
            // Ctrl キー
            bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            CtrlKey.Background = isCtrl ? PressedBrush : NormalBrush;

            // Shift キー
            bool isShift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            ShiftKey.Background = isShift ? PressedBrush : NormalBrush;

            // Alt キー
            bool isAlt = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
            AltKey.Background = isAlt ? PressedBrush : NormalBrush;
        }

        private void UpdateCurrentKey(Key key, bool isPressed)
        {
            // 修飾キー以外のキーを表示
            if (key != Key.LeftCtrl && key != Key.RightCtrl &&
                key != Key.LeftShift && key != Key.RightShift &&
                key != Key.LeftAlt && key != Key.RightAlt &&
                key != Key.System)
            {
                if (isPressed)
                {
                    CurrentKeyBorder.Visibility = Visibility.Visible;
                    CurrentKeyBorder.Background = PressedBrush;
                    CurrentKeyText.Text = GetKeyDisplayName(key);
                }
                else
                {
                    CurrentKeyBorder.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void UpdateMouseButtons()
        {
            // 左ボタン
            LeftMouseButton.Background = Mouse.LeftButton == MouseButtonState.Pressed ? PressedBrush : NormalBrush;

            // 右ボタン
            RightMouseButton.Background = Mouse.RightButton == MouseButtonState.Pressed ? PressedBrush : NormalBrush;

            // 中ボタン（ホイールクリック）
            MiddleMouseButton.Background = Mouse.MiddleButton == MouseButtonState.Pressed ? PressedBrush : NormalBrush;
        }

        private static string GetKeyDisplayName(Key key)
        {
            return key switch
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
                Key.Up => "\u2191",
                Key.Down => "\u2193",
                Key.Left => "\u2190",
                Key.Right => "\u2192",
                Key.Tab => "Tab",
                Key.F1 => "F1",
                Key.F2 => "F2",
                Key.F3 => "F3",
                Key.F4 => "F4",
                Key.F5 => "F5",
                Key.F6 => "F6",
                Key.F7 => "F7",
                Key.F8 => "F8",
                Key.F9 => "F9",
                Key.F10 => "F10",
                Key.F11 => "F11",
                Key.F12 => "F12",
                _ => key.ToString().Length == 1 ? key.ToString() : key.ToString()
            };
        }
    }
}
