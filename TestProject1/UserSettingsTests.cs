using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// ユーザー設定 (保存オプション) の既定値と往復を固定する。
    ///
    /// 「解析結果もファイルに保存する」は既定 ON で、OFF にした選択は次回起動時にも
    /// 引き継がれる必要がある。既定値を戻すと、ファイルを開き直しても前回結果が
    /// 見られない状態に静かに戻ってしまう。
    /// </summary>
    [TestClass]
    public class UserSettingsTests
    {
        [TestMethod]
        public void ManualSave_DefaultsToIncludingAnalysisResults()
        {
            var settings = new UserSettings();

            Assert.IsTrue(settings.IsSaveAnalysisResultsManual,
                "手動保存は既定で解析結果を含める (開き直したときに再計算不要にするため)");
        }

        [TestMethod]
        public void AutoSave_DefaultsToInputOnly()
        {
            var settings = new UserSettings();

            Assert.IsFalse(settings.IsSaveAnalysisResultsAutoSave,
                "自動保存は定期実行なので既定 OFF (数十 MB の書込が繰り返される)");
        }

        [TestMethod]
        public void Settings_SurviveJsonRoundTrip()
        {
            var original = new UserSettings
            {
                IsSaveAnalysisResultsManual = false,   // 既定から明示的に外した状態
                IsSaveAnalysisResultsAutoSave = true,
            };

            var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(original));

            Assert.IsNotNull(restored);
            Assert.IsFalse(restored.IsSaveAnalysisResultsManual, "OFF にした選択が保存されていない");
            Assert.IsTrue(restored.IsSaveAnalysisResultsAutoSave);
        }

        [TestMethod]
        public void MissingKeys_FallBackToDefaults()
        {
            // 設定項目を増やす前に書かれた古い設定ファイルを読んでも、
            // 未知のキーは既定値で補われること
            var restored = JsonSerializer.Deserialize<UserSettings>("{}");

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.IsSaveAnalysisResultsManual);
            Assert.IsFalse(restored.IsSaveAnalysisResultsAutoSave);
        }
    }
}
