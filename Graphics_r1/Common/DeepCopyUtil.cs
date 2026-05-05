using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Serilog;
using NewtonJson = Newtonsoft.Json;

namespace PileDesign.Common
{
    /// <summary>
    /// JSON による deep copy 共通ユーティリティ。
    ///
    /// 2 段フォールバック:
    /// 1. System.Text.Json（高速）で試行
    /// 2. NotSupportedException / JsonException が出たら Newtonsoft.Json にフォールバック
    ///    （パラメータなしコンストラクタを持たない型・internal 専用コンストラクタ等に対応）
    ///
    /// 使い所:
    /// - [JsonIgnore] を尊重した純データ型の複製（InputData 配下の value-shape クラス）
    /// - 循環参照あり: ReferenceHandler.Preserve / PreserveReferencesHandling.All
    /// - NaN / Infinity を含むリテラルを許容
    ///
    /// 使ってはいけない対象:
    /// - MathNet 行列・ベクトル（Matrix&lt;double&gt; / Vector&lt;double&gt;）を保持する FEM クラス
    ///   （[JsonIgnore] されていれば JSON 往復で null 化するため、手書き DeepCopy を維持すること）
    /// - イベントハンドラの再構築が必要な型
    /// - パフォーマンスクリティカルな Undo スナップショット（手書き DeepCopy の 5〜10 倍遅い）
    /// </summary>
    public static class DeepCopyUtil
    {
        private static readonly JsonSerializerOptions _stjOptions = new()
        {
            WriteIndented = false,
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            // public フィールドも deep copy 対象にする（プロパティのみの既定動作だと
            // 値型フィールドが取りこぼされる。DoatsuGoryokuBane.DeltaP 等が該当）
            IncludeFields = true,
        };

        // Phase C 計測: ReferenceHandler.Preserve を外した高速版オプション。
        // 循環参照が無いことを前提とする。serialize 時間が 5〜10× 高速化される代わり、
        // 循環があると StackOverflow / JsonException で失敗する。失敗時は従来オプションへフォールバック。
        private static readonly JsonSerializerOptions _stjOptionsNoPreserve = new()
        {
            WriteIndented = false,
            // ReferenceHandler 未設定 (循環参照なし前提)
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            IncludeFields = true,
            // 循環参照に到達した場合に StackOverflow ではなく JsonException でエラーを返すよう、最大深度を制限
            MaxDepth = 256,
        };

        /// <summary>
        /// ReferenceHandler.Preserve を外した高速版 deep copy。循環参照が含まれる場合は失敗するので、
        /// 呼び出し元は失敗時 (戻り値 null + isOk=false) に CloneJson にフォールバックする。
        /// </summary>
        public static T? CloneJsonFast<T>(T source, out bool isOk)
        {
            isOk = false;
            if (source is null) return default;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string json = System.Text.Json.JsonSerializer.Serialize(source, _stjOptionsNoPreserve);
                long tSer = sw.ElapsedMilliseconds;
                int size = json.Length;
                sw.Restart();
                var result = System.Text.Json.JsonSerializer.Deserialize<T>(json, _stjOptionsNoPreserve);
                long tDes = sw.ElapsedMilliseconds;
                Log.Debug("CloneJsonFast<{T}> path=STJ-NoPreserve size={SizeBytes}B serialize={Ser}ms deserialize={Des}ms",
                    typeof(T).Name, size, tSer, tDes);
                isOk = true;
                return result;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CloneJsonFast<{TypeName}> failed (likely cycle), caller should fallback to CloneJson", typeof(T).Name);
                return default;
            }
        }

        private static readonly NewtonJson.JsonSerializerSettings _newtonsoftSettings = new()
        {
            PreserveReferencesHandling = NewtonJson.PreserveReferencesHandling.All,
            TypeNameHandling = NewtonJson.TypeNameHandling.Auto,
            Formatting = NewtonJson.Formatting.None,
        };

        /// <summary>
        /// JSON 往復により <typeparamref name="T"/> の deep copy を作成する。
        /// System.Text.Json で失敗した場合は Newtonsoft.Json にフォールバック。
        /// 両方失敗した場合は例外をスローせず既定値を返し、Debug.WriteLine にログ出力する。
        /// </summary>
        public static T? CloneJson<T>(T source)
        {
            if (source is null) return default;

            // 第 1 候補: System.Text.Json
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(source, _stjOptions);
                return System.Text.Json.JsonSerializer.Deserialize<T>(json, _stjOptions);
            }
            catch (Exception stjEx) when (stjEx is NotSupportedException || stjEx is System.Text.Json.JsonException)
            {
                // InputModel.LoadFromFile と同じパターンで Newtonsoft にフォールバック
                // 典型: パラメータなしコンストラクタを持たない型（CaptainPile 等）
                Log.Debug(stjEx, "DeepCopyUtil.CloneJson<{TypeName}>: STJ failed, falling back to Newtonsoft", typeof(T).Name);
                try
                {
                    string json = NewtonJson.JsonConvert.SerializeObject(source, _newtonsoftSettings);
                    return NewtonJson.JsonConvert.DeserializeObject<T>(json, _newtonsoftSettings);
                }
                catch (Exception nswEx)
                {
                    Log.Warning(nswEx, "DeepCopyUtil.CloneJson<{TypeName}>: Newtonsoft fallback also failed", typeof(T).Name);
                    return default;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DeepCopyUtil.CloneJson<{TypeName}> failed", typeof(T).Name);
                return default;
            }
        }

        /// <summary>
        /// 失敗時に例外をスローする版。呼び出し側が確実にコピー成功を期待する場面で使用する。
        /// </summary>
        public static T CloneJsonOrThrow<T>(T source) where T : class
        {
            ArgumentNullException.ThrowIfNull(source);

            var clone = CloneJson(source)
                ?? throw new InvalidOperationException($"DeepCopy (JSON) of {typeof(T).Name} returned null");
            return clone;
        }
    }
}
