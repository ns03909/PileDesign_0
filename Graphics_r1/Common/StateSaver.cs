using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PileDesign.Common
{

    // アプリケーションの状態を保存するクラス
    public class StateSaver
    {
        public static void SaveState<T>(T viewModel, string filePath) where T : class
        {
            ArgumentNullException.ThrowIfNull(viewModel);

            JsonSerializerOptions jsonSerializerOptions = new()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals // Add this line
            };
            JsonSerializerOptions options = jsonSerializerOptions;

            string jsonString = JsonSerializer.Serialize(viewModel, options);
            File.WriteAllText(filePath, jsonString);
        }

        public static T? LoadState<T>(string filePath) where T : class
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            try
            {
                string jsonString = File.ReadAllText(filePath);
                JsonSerializerOptions options = new()
                {
                    ReferenceHandler = ReferenceHandler.Preserve,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
                };
                return JsonSerializer.Deserialize<T>(jsonString, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StateSaver] LoadState失敗 ({filePath}): {ex.Message}");
                return null;
            }
        }
    }
}
