using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PileDesignCore
{
    // アプリケーションの状態を保存するクラス
    //public class StateSaver
    //{
    //    public static void SaveState<T>(T viewModel, string filePath) where T : class
    //    {
    //        if (viewModel == null)
    //        {
    //            throw new ArgumentNullException(nameof(viewModel));
    //        }

    //        Type viewModelType = viewModel.GetType();

    //        using (FileStream stream = new FileStream(filePath, FileMode.Create))
    //        {
    //            IFormatter formatter = new BinaryFormatter();

    //            // オブジェクトの型に基づいて新しいオブジェクトを作成
    //            object state = Activator.CreateInstance(viewModelType);

    //            // オブジェクトのプロパティをコピー
    //            foreach (var property in viewModelType.GetProperties())
    //            {
    //                //var stateProperty = state.GetType().GetProperty(property.Name);
    //                //if (stateProperty != null && stateProperty.CanWrite)
    //                //{
    //                //    stateProperty.SetValue(state, property.GetValue(viewModel));
    //                //}

    //                // シリアライズ可能なプロパティのみをコピー
    //                if (property.PropertyType.IsSerializable)
    //                {
    //                    var stateProperty = state.GetType().GetProperty(property.Name);
    //                    if (stateProperty != null && stateProperty.CanWrite)
    //                    {
    //                        stateProperty.SetValue(state, property.GetValue(viewModel));
    //                    }
    //                }
    //            }


    //            formatter.Serialize(stream, state);
    //        }
    //    }
    //}

    // アプリケーションの状態を保存するクラス
    public class StateSaver
    {
        public static void SaveState<T>(T viewModel, string filePath) where T : class
        {
            if (viewModel == null)
            {
                throw new ArgumentNullException(nameof(viewModel));
            }

            JsonSerializerOptions options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals // Add this line
            };

            string jsonString = JsonSerializer.Serialize(viewModel, options);
            File.WriteAllText(filePath, jsonString);
        }

        public static T LoadState<T>(string filePath) where T : class
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            string jsonString = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(jsonString);
        }
    }
}
