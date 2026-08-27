using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace PileDesign.Common
{
    /// <summary>
    /// JSON の <c>$type</c> から復元してよい型を、このアプリ自身の型に限る。
    ///
    /// ファイル読込のフォールバックは <c>TypeNameHandling.Auto</c> で Newtonsoft を使う。
    /// この設定は <c>$type</c> に書かれた任意の型を復元しようとするため、
    /// <b>細工した入力ファイルで意図しない型を作らせる</b>ことができてしまう
    /// (デシリアライズのガジェット攻撃として知られる)。
    ///
    /// 保存は System.Text.Json が行い、<c>$type</c> を書かない。つまり正規のファイルに
    /// <c>$type</c> は現れない。ここで許すのは、万一の互換のために
    /// <b>このアセンブリの型だけ</b>にしておけば十分。
    /// </summary>
    public sealed class TrustedTypeBinder : ISerializationBinder
    {
        public static readonly TrustedTypeBinder Instance = new();

        private static readonly Assembly OwnAssembly = typeof(TrustedTypeBinder).Assembly;
        private readonly Dictionary<string, Type> _cache = [];

        public Type BindToType(string? assemblyName, string typeName)
        {
            string key = $"{assemblyName}|{typeName}";
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var type = OwnAssembly.GetType(typeName, throwOnError: false);
                if (type == null)
                {
                    throw new System.Security.SecurityException(
                        $"ファイルに、このアプリの型ではないものが指定されています: {typeName}");
                }

                _cache[key] = type;
                return type;
            }
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            // 読み込み専用。書き出しは System.Text.Json が行う。
            assemblyName = null;
            typeName = serializedType.FullName;
        }
    }
}
