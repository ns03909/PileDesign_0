using Microsoft.Win32;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PileDesign.Services
{
    /// <summary>
    /// .pdj ファイル拡張子を現在のユーザーの「プログラムから開く」一覧に登録するサービス。
    /// Portable 配布 (zip 展開) でも管理者権限なしで関連付け可能。
    ///
    /// 登録するレジストリエントリ (HKCU = HKEY_CURRENT_USER、admin 不要):
    ///   HKCU\Software\Classes\.pdj                                 → ProgID "PileDesign.Project"
    ///   HKCU\Software\Classes\PileDesign.Project                   → 表示名
    ///   HKCU\Software\Classes\PileDesign.Project\DefaultIcon       → exe のアイコン
    ///   HKCU\Software\Classes\PileDesign.Project\shell\open\command → "exe" --open "%1"
    ///
    /// Windows 10/11 の制約により「既定アプリ化」だけは API でできないため、
    /// レジストリ登録後に ms-settings:defaultapps を開いてユーザーに 1 クリック選択を促す。
    /// </summary>
    public static class FileAssociationService
    {
        private const string Extension = ".pdj";
        private const string ProgId = "PileDesign.Project";
        private const string FriendlyName = "PileDesign プロジェクト";

        // SHChangeNotify (Explorer に関連付け変更を通知してアイコン/表示を即時反映)
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        /// <summary>
        /// 現在のユーザー用に .pdj 関連付けを登録する。登録済みの場合も冪等に上書きする
        /// (exe のパスが変わった場合にも追従するため)。
        /// </summary>
        /// <returns>登録に成功した場合 true。失敗時はログに記録し false を返す。</returns>
        public static bool Register()
        {
            try
            {
                var exePath = GetExePath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Log.Warning("[FileAssociation] exe path not found: {Path}", exePath);
                    return false;
                }

                using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", writable: true))
                {
                    if (classes == null) return false;

                    // 1. .pdj → ProgID 紐付け
                    using (var extKey = classes.CreateSubKey(Extension, writable: true))
                    {
                        extKey?.SetValue(string.Empty, ProgId);
                        // OpenWithProgids を併設 (Open With メニューに必ず出る)
                        using var openWith = extKey?.CreateSubKey("OpenWithProgids", writable: true);
                        openWith?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
                    }

                    // 2. ProgID 本体
                    using (var progIdKey = classes.CreateSubKey(ProgId, writable: true))
                    {
                        progIdKey?.SetValue(string.Empty, FriendlyName);
                        progIdKey?.SetValue("FriendlyTypeName", FriendlyName);

                        using var iconKey = progIdKey?.CreateSubKey("DefaultIcon", writable: true);
                        iconKey?.SetValue(string.Empty, $"\"{exePath}\",0");

                        using var commandKey = progIdKey?.CreateSubKey(@"shell\open\command", writable: true);
                        commandKey?.SetValue(string.Empty, $"\"{exePath}\" --open \"%1\"");
                    }
                }

                // 3. Explorer に通知 (アイコン即時更新)
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

                Log.Information("[FileAssociation] Registered .pdj to {ExePath}", exePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileAssociation] Register failed");
                return false;
            }
        }

        /// <summary>
        /// .pdj が PileDesign.Project ProgID として HKCU に登録されているかを確認する。
        /// 登録パスが現在の exe を指しているかは判定しない (それは <see cref="IsRegisteredPathCurrent"/>)。
        /// </summary>
        public static bool IsRegistered()
        {
            try
            {
                using var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Extension}");
                if (extKey?.GetValue(string.Empty) as string != ProgId) return false;

                using var commandKey = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ProgId}\shell\open\command");
                var command = commandKey?.GetValue(string.Empty) as string;
                return !string.IsNullOrEmpty(command);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 登録済みの shell\open\command の exe パスが、現在実行中の PileDesign.exe と一致するかを確認する。
        /// false (= 異なる) ならば <see cref="Register"/> を再実行することでパスを追従更新できる。
        /// </summary>
        public static bool IsRegisteredPathCurrent()
        {
            try
            {
                using var commandKey = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ProgId}\shell\open\command");
                var command = commandKey?.GetValue(string.Empty) as string;
                if (string.IsNullOrEmpty(command)) return false;

                var exePath = GetExePath();
                return !string.IsNullOrEmpty(exePath) && command!.Contains(exePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// .pdj 関連付けを HKCU から削除する (アンインストール想定)。
        /// </summary>
        public static bool Unregister()
        {
            try
            {
                using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
                {
                    classes?.DeleteSubKeyTree(Extension, throwOnMissingSubKey: false);
                    classes?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
                }
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                Log.Information("[FileAssociation] Unregistered .pdj");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[FileAssociation] Unregister failed");
                return false;
            }
        }

        /// <summary>
        /// Windows の「既定のアプリ」設定ページを開く。Windows 10/11 では
        /// プログラム的に既定アプリを変更できないため、ユーザーに 1 クリックで選択してもらう。
        /// </summary>
        public static void OpenDefaultAppsSettings()
        {
            try
            {
                // ms-settings:defaultapps はトップページ。拡張子指定 (ms-settings:defaultapps?registeredAppMachine 等)
                // はバージョン差が大きいため安全なトップを開く
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:defaultapps",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FileAssociation] OpenDefaultAppsSettings failed");
            }
        }

        /// <summary>
        /// 現在実行中の PileDesign.exe のフルパスを取得する。
        /// </summary>
        private static string GetExePath()
        {
            // .NET 6+ の AppContext.BaseDirectory + Process.MainModule.FileName
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch { /* fallthrough */ }

            return Path.Combine(AppContext.BaseDirectory, "PileDesign.exe");
        }
    }
}
