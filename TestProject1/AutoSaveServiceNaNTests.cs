using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// AutoSaveService が NaN/Inf を含む InputModel に遭遇したときの挙動を検証する。
    ///
    /// 設計上の合意:
    /// - FileOperationService.SaveProjectData は ValidateFinite で例外を投げる (フィールドパス付き)
    /// - AutoSaveService.PerformAutoSave は try/catch で受け、AutoSaveCompleted イベントで通知
    /// - タイマーは止まらない (次回再試行で自然回復)
    /// - 失敗時はファイルを残さない (途中書き込みでゴミファイルを作らない)
    /// </summary>
    [TestClass]
    public class AutoSaveServiceNaNTests
    {
        private static JsonSerializerOptions MakeOptions() => new()
        {
            WriteIndented = true,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve
        };

        // --- リフレクションヘルパー (production 改変を避けるため) -------------

        private static void SetPrivateField(object obj, string fieldName, object? value)
        {
            var f = obj.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"フィールド {fieldName} が見つかりません");
            f.SetValue(obj, value);
        }

        private static void InvokePrivate(object obj, string methodName, params object?[]? args)
        {
            var m = obj.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"メソッド {methodName} が見つかりません");
            m.Invoke(obj, args);
        }

        /// <summary>
        /// GroundInput.GroundTopAltitude に NaN を直書きする。
        /// 通常の setter (SetFiniteDouble) は NaN を弾いて 0.0 にフォールバックするので、
        /// 「UI 検証をすり抜けた NaN」を再現するには private フィールド _groundTopAltitude
        /// に reflection で直接書き込む必要がある。
        /// この関数は ValidateFinite (defense-in-depth) のテスト用。
        /// </summary>
        private static GroundInput CreateGroundInputWithRawNaN()
        {
            var g = new GroundInput();
            var field = typeof(GroundInput).GetField("_groundTopAltitude",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("_groundTopAltitude が見つかりません");
            field.SetValue(g, double.NaN);
            return g;
        }

        /// <summary>
        /// 各テストで AutoSaveFolder に残った前回テストのファイルを削除する。
        /// AutoSaveService は %LocalAppData%\PileDesign\AutoSave という共有フォルダを使うため、
        /// 過去の test run のゴミが「ファイルが残っているか」検証を撹乱する。
        /// </summary>
        private static void CleanupAutoSaveFolder(AutoSaveService auto, string filePrefix)
        {
            try
            {
                var pattern = $"{filePrefix}*.json";
                foreach (var f in Directory.GetFiles(auto.AutoSaveFolder, pattern))
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
        }

        // --- テスト ------------------------------------------------------------

        [TestMethod]
        public void AutoSave_WithNaNInInputModel_FailureEventContainsFieldPath()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            CleanupAutoSaveFolder(auto, "TestProject_NaN_");
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_NaN.json");

                AutoSaveEventArgs? captured = null;
                auto.AutoSaveCompleted += (s, e) => captured = e;

                InvokePrivate(auto, "PerformAutoSave");

                Assert.IsNotNull(captured, "AutoSaveCompleted が発火しなかった");
                Assert.IsFalse(captured.Success, "NaN を含むのに Success=true");
                Assert.IsNull(captured.FilePath, "失敗時に FilePath は null のはず");
                Assert.IsNotNull(captured.ErrorMessage, "ErrorMessage が空");
                StringAssert.Contains(captured.ErrorMessage, "GroundTopAltitude",
                    $"フィールド名が伝わらない: {captured.ErrorMessage}");
                StringAssert.Contains(captured.ErrorMessage, "NaN",
                    $"値の種別 (NaN) が伝わらない: {captured.ErrorMessage}");

                // LastAutoSaveTime は失敗時に更新されないはず
                Assert.IsNull(auto.LastAutoSaveTime, "失敗時に LastAutoSaveTime が更新されてはいけない");

                // ゴミファイルが残っていない (auto.AutoSaveFolder 配下に該当パターンが無い)
                var leftovers = Directory.GetFiles(auto.AutoSaveFolder, "TestProject_NaN_autosave_*.json");
                Assert.AreEqual(0, leftovers.Length,
                    $"失敗時にファイルが残っている: {string.Join(", ", leftovers)}");
            }
            finally
            {
                auto.Stop();
            }
        }

        [TestMethod]
        public void AutoSave_FailureDoesNotStopTimer()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                // Start() はタイマーを起動 + state を設定
                auto.Start("TestProject_TimerSurvival.json", inputModel, new AnaModel());
                Assert.IsTrue(auto.IsEnabled, "Start 直後にタイマーが有効でない");

                InvokePrivate(auto, "PerformAutoSave");

                // 例外で握りつぶされたが、タイマーは止まっていない (= 次回再試行可能)
                Assert.IsTrue(auto.IsEnabled,
                    "失敗後にタイマーが止まっている (= 次回 AutoSave が走らない)");
            }
            finally
            {
                auto.Stop();
            }
        }

        [TestMethod]
        public void AutoSave_ConsecutiveFailures_IncrementsOnEachFailure()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_Counter.json");

                AutoSaveEventArgs? lastEvent = null;
                auto.AutoSaveCompleted += (s, e) => lastEvent = e;

                Assert.AreEqual(0, auto.ConsecutiveFailures, "初期値は 0");

                InvokePrivate(auto, "PerformAutoSave");
                Assert.AreEqual(1, auto.ConsecutiveFailures);
                Assert.AreEqual(1, lastEvent!.ConsecutiveFailures, "イベントにも反映");

                InvokePrivate(auto, "PerformAutoSave");
                Assert.AreEqual(2, auto.ConsecutiveFailures);
                Assert.AreEqual(2, lastEvent!.ConsecutiveFailures);

                InvokePrivate(auto, "PerformAutoSave");
                Assert.AreEqual(3, auto.ConsecutiveFailures, "3 回目で閾値到達");
                Assert.AreEqual(3, lastEvent!.ConsecutiveFailures);
            }
            finally
            {
                auto.Stop();
            }
        }

        [TestMethod]
        public void AutoSave_ConsecutiveFailures_ResetsOnSuccess()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            string? createdFile = null;
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_Reset.json");

                AutoSaveEventArgs? lastEvent = null;
                auto.AutoSaveCompleted += (s, e) => lastEvent = e;

                // 失敗を 2 回重ねる
                InvokePrivate(auto, "PerformAutoSave");
                InvokePrivate(auto, "PerformAutoSave");
                Assert.AreEqual(2, auto.ConsecutiveFailures);

                // NaN を修正して成功させる
                inputModel.GroundsInput![0].GroundTopAltitude = 0.0;
                InvokePrivate(auto, "PerformAutoSave");

                Assert.AreEqual(0, auto.ConsecutiveFailures, "成功でカウンタが 0 にリセット");
                Assert.IsTrue(lastEvent!.Success);
                Assert.AreEqual(0, lastEvent!.ConsecutiveFailures);

                createdFile = lastEvent.FilePath;
            }
            finally
            {
                auto.Stop();
                if (createdFile != null && System.IO.File.Exists(createdFile))
                {
                    try { System.IO.File.Delete(createdFile); } catch { /* ignore */ }
                }
            }
        }

        [TestMethod]
        public void EmergencyAutoSave_WithValidModel_SavesToEmergencyFile()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            string? createdFile = null;
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput>
                    {
                        new() { GroundTopAltitude = 12.5 }
                    }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_Emergency.json");

                var path = auto.TryEmergencyAutoSave();
                Assert.IsNotNull(path, "緊急保存に失敗");
                Assert.IsTrue(System.IO.File.Exists(path), $"緊急保存ファイルが存在しない: {path}");
                StringAssert.Contains(path, "_emergency_",
                    $"緊急保存ファイル名に _emergency_ タグが含まれていない: {path}");
                StringAssert.Contains(path, "TestProject_Emergency",
                    $"元ファイル名のプレフィックスが含まれていない: {path}");

                createdFile = path;
            }
            finally
            {
                auto.Stop();
                if (createdFile != null && System.IO.File.Exists(createdFile))
                {
                    try { System.IO.File.Delete(createdFile); } catch { /* ignore */ }
                }
            }
        }

        [TestMethod]
        public void EmergencyAutoSave_WithoutInputModel_ReturnsNullWithoutThrowing()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            try
            {
                // 何もセットしない状態で呼び出す
                var path = auto.TryEmergencyAutoSave();
                Assert.IsNull(path, "InputModel が未設定なので null が期待される");
            }
            finally
            {
                auto.Stop();
            }
        }

        [TestMethod]
        public void EmergencyAutoSave_WithNaNModel_ReturnsNullWithoutThrowing()
        {
            // NaN を含む InputModel に対し緊急保存を試行 → ValidateFinite で例外、
            // TryEmergencyAutoSave は内部で握りつぶして null を返す。
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_EmergencyNaN.json");

                var path = auto.TryEmergencyAutoSave();
                Assert.IsNull(path, "NaN 入力では null が期待される (例外を握りつぶし)");
            }
            finally
            {
                auto.Stop();
            }
        }

        [TestMethod]
        public void AutoSave_Stop_ResetsConsecutiveFailures()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));

            var inputModel = new InputModel
            {
                GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
            };

            SetPrivateField(auto, "_currentInputModel", inputModel);
            SetPrivateField(auto, "_currentModel", new AnaModel());

            InvokePrivate(auto, "PerformAutoSave");
            InvokePrivate(auto, "PerformAutoSave");
            Assert.AreEqual(2, auto.ConsecutiveFailures);

            auto.Stop();
            Assert.AreEqual(0, auto.ConsecutiveFailures, "Stop でカウンタが 0 にリセット");
        }

        [TestMethod]
        public void AutoSave_RecoversAfterFixingNaN()
        {
            var auto = new AutoSaveService(new FileOperationService(MakeOptions()));
            string? createdFile = null;
            try
            {
                var inputModel = new InputModel
                {
                    GroundsInput = new ObservableCollection<GroundInput> { CreateGroundInputWithRawNaN() }
                };

                SetPrivateField(auto, "_currentInputModel", inputModel);
                SetPrivateField(auto, "_currentModel", new AnaModel());
                SetPrivateField(auto, "_currentFilePath", "TestProject_Recovery.json");

                AutoSaveEventArgs? lastEvent = null;
                auto.AutoSaveCompleted += (s, e) => lastEvent = e;

                // 1 回目: 失敗
                InvokePrivate(auto, "PerformAutoSave");
                Assert.IsNotNull(lastEvent);
                Assert.IsFalse(lastEvent.Success, "1 回目は失敗するはず");

                // ユーザーが NaN を修正
                inputModel.GroundsInput![0].GroundTopAltitude = 0.0;

                // 2 回目: 成功
                InvokePrivate(auto, "PerformAutoSave");
                Assert.IsNotNull(lastEvent);
                Assert.IsTrue(lastEvent.Success,
                    $"修正後も失敗: {lastEvent.ErrorMessage}");
                Assert.IsNotNull(lastEvent.FilePath, "成功時に FilePath が null");
                Assert.IsTrue(File.Exists(lastEvent.FilePath),
                    $"AutoSave ファイルが作られていない: {lastEvent.FilePath}");
                Assert.IsNotNull(auto.LastAutoSaveTime, "成功時に LastAutoSaveTime が更新されない");

                createdFile = lastEvent.FilePath;
            }
            finally
            {
                auto.Stop();
                // テスト用ファイルをクリーンアップ
                if (createdFile != null && File.Exists(createdFile))
                {
                    try { File.Delete(createdFile); } catch { /* ignore */ }
                }
            }
        }
    }
}
