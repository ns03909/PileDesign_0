using PileDesign.FEM;
using PileDesign.Models;
using PileDesign.Models.InputData;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PileDesign.Services
{
    /// <summary>
    /// ファイル操作に関するサービスクラス
    /// JSON シリアライズ、デシリアライズ、コレクション型変換を担当
    /// </summary>
    public class FileOperationService
    {
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// 手動保存 (<see cref="SaveProjectDataAsync"/>) でも ValidateFinite (NaN/∞ 検出) を実行して、<b>保存を止める</b>かどうか。
        /// 既定 false: 手動保存を NaN で失敗させると、利用者が作業を保存できなくなる。
        /// 止めないときも検査はして、見つかった箇所を戻り値で返す (画面が警告として知らせる)。
        /// AllowNamedFloatingPointLiterals により NaN/∞ もそのまま保存できる。
        /// 自動保存・緊急保存 (<see cref="SaveProjectData"/>) はこの設定に関わらず必ず検査する。
        /// (かつては反射の全走査に 6 秒以上かかったが、重い算出プロパティを [JsonIgnore] にしてからは
        ///  計算例 9・10 の入力で 15 ms 以下。2026-09-25 実測)
        /// </summary>
        public bool ValidateFiniteBeforeSave { get; set; } = false;

        public FileOperationService(JsonSerializerOptions jsonOptions)
        {
            _jsonOptions = jsonOptions;
        }

        /// <summary>
        /// ProjectData を JSON ファイルに保存 (自動保存・緊急保存・テスト・CLI が使う)。
        /// 中身の確定 (<see cref="PrepareSave"/>) と書き出し (<see cref="WritePrepared"/>) を続けて行う。
        /// 画面のスレッドで確定させてから書き出しだけをバックグラウンドへ回したい呼び出し
        /// (自動保存) は、2 つを別々に呼ぶこと。
        /// </summary>
        public void SaveProjectData(string filePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null,
            InputModel? resultInputSnapshot = null, DateTime? resultCapturedAt = null,
            PileFemLinkTable? pileFemLinks = null, bool? isElementSplit = null,
            bool inputChangedSinceAnalysis = false, string? sourceFilePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            // NaN 検査はここでは ValidateFiniteBeforeSave に関わらず必ず走らせる。
            //
            // この同期版を呼ぶのは自動保存・緊急保存で、その出力は<b>あとで復元する元</b>に
            // なる。NaN を含んだまま書くと、壊れたファイルしか残っていない状態で
            // 復元することになる。非同期版 (手動保存) が既定で走らせないのは、
            // 手動保存を NaN で失敗させると作業を保存できなくなるため。
            // (AutoSaveServiceNaNTests が、NaN のとき保存を失敗させ、
            //  どのフィールドかを伝えることを見ている)
            var prepared = PrepareSave(inputModel, anaModel, verticalBeamCaseResults, resultInputSnapshot,
                resultCapturedAt, pileFemLinks, isElementSplit, inputChangedSinceAnalysis, sourceFilePath,
                validateFinite: true);
            WritePrepared(filePath, prepared);
        }

        /// <summary>
        /// 保存 1 回ぶんの中身。<see cref="PrepareSave"/> が画面のスレッドで作り、
        /// <see cref="WritePrepared"/> (または非同期保存) がバックグラウンドで書く。
        /// </summary>
        internal sealed class PreparedSave
        {
            /// <summary>書き出す入力 (編集できるコレクションだけ写した器)。</summary>
            public InputModel? InputToSave { get; init; }

            /// <summary>直列化した中身。検査か直列化に失敗したときは null で、理由は <see cref="Error"/>。</summary>
            public byte[]? Payload { get; init; }

            /// <summary>
            /// 保存を止めない検査で見つかった NaN・無限大の箇所 (最初の 1 か所)。無ければ null。
            /// 手動保存は NaN で止めないので、保存したあとに利用者へ知らせるのに使う。
            /// </summary>
            public string? NonFiniteLocation { get; init; }

            /// <summary>
            /// NaN 検査か直列化で出た例外。書き出しの側で投げ直す。
            /// 中身を確定させる処理は画面のスレッドで走るので (自動保存の Tick など)、そこでは投げずに持ち越し、
            /// 書き出しの失敗として同じ経路で知らせる。
            /// </summary>
            public Exception? Error { get; init; }
        }

        /// <summary>
        /// 保存する中身を<b>いまの値で確定させる</b> (直列化まで済ませる)。<b>画面のスレッドから呼ぶこと。</b>
        ///
        /// 以前は写した器をバックグラウンドへ渡し、そこで直列化していた。写しが守るのは
        /// コレクションの入れ物だけで、要素の実体は画面と共有している (要素まで複製すると
        /// $ref の畳まれ方が変わる。<see cref="SnapshotForSaving"/> 参照)。そのため保存の途中で
        /// セルの値を書き換えると、先に書いた要素は編集前・後に書いた要素は編集後という、
        /// どの時点にも無かった入力が 1 つのファイルに混ざり得た。画面のスレッドで直列化まで
        /// 済ませれば、そのあいだ編集は割り込めない。かかる時間は計算例 10 で入力のみ 2 ms 以下、
        /// 解析結果込み (14.6 MB) で約 0.1 秒。
        ///
        /// <paramref name="validateFinite"/> のときは、<b>直列化の直前に同じスレッドで</b> NaN / ±∞ を検査する。
        /// 以前は直列化だけをここで済ませ、検査はあとからバックグラウンドで、画面と要素を共有する
        /// モデルにかけていた。そのあいだに値が変わると、検査した値と書いた JSON が食い違った
        /// (NaN を書いたのに検査を通る、またはその逆)。検査と直列化のあいだに編集は割り込めないので、
        /// いまは検査した値がそのまま書かれる。
        /// </summary>
        internal PreparedSave PrepareSave(InputModel? inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null,
            InputModel? resultInputSnapshot = null, DateTime? resultCapturedAt = null,
            PileFemLinkTable? pileFemLinks = null, bool? isElementSplit = null,
            bool inputChangedSinceAnalysis = false, string? sourceFilePath = null, string? autoSaveSessionId = null,
            bool validateFinite = false)
        {
            // 編集できるコレクションだけ写した器を先に作る (理由は SnapshotForSaving)。
            // 自動保存では呼び出し側がすでに写していることがあり、二重になるが、
            // 要素は同じ実体を指したままなので保存ファイルの中身は変わらない。
            var inputToSave = SnapshotForSaving(inputModel, anaModel, resultInputSnapshot);

            // 検査は<b>写した器</b>に、直列化と同じ時点でかける (書き出すものをそのまま検査する)
            string? nonFiniteLocation = null;
            if (validateFinite)
            {
                try
                {
                    ValidateFinite(inputToSave);
                }
                catch (Exception ex)
                {
                    return new PreparedSave { InputToSave = inputToSave, Error = ex };
                }
            }
            else
            {
                // 保存は止めないが、どこに NaN・無限大があるかは調べて返す (画面が警告として知らせる)。
                // 以前は手動保存では調べもしなかったので、値の異常に気付く手掛かりが無かった。
                // 調べること自体が失敗しても保存は続ける (警告のための検査で作業を失わない)
                try { nonFiniteLocation = inputToSave == null ? null : FindNonFiniteDouble(inputToSave, "InputModel"); }
                catch (Exception ex) { Log.Warning(ex, "[Save] NaN・無限大の箇所を調べられませんでした"); }
            }

            var projectData = new ProjectData
            {
                FormatVersion = 2,  // v2: PileLayoutItems[*].Z = 接合節点 Z (旧 v1 = 杭頭 Z)
                InputModel = inputToSave!,
                AnaModel = anaModel!,
                VerticalBeamCaseResults = verticalBeamCaseResults != null
                    ? new List<FEM.VerticalBeamCaseResult>(verticalBeamCaseResults)
                    : null!,
                // 単杭沈下の荷重-沈下曲線も入力の中ではなくこの節に 1 回だけ書く。
                // SoilPile 側は [JsonIgnore] なので、ここで書かないと保存されない。
                SinglePileSettlementResult = Models.Results.SinglePileSettlementResult.Capture(inputToSave),
                // 群杭沈下の結果は入力の中ではなく、この節に 1 回だけ書く。
                // 入力モデルは結果への参照を持つだけ ([JsonIgnore]) なので、ここで書かないと保存されない。
                // 水平解析の結果を保存しない設定でも沈下の結果は保存する (従来と同じ)。
                GroupSettlementResult = inputToSave?.PileGroupSettlement?.Result is { HasResults: true } gsr
                    ? gsr : null,
                // 解析結果を保存しないときはスナップショットも不要
                ResultInputSnapshot = anaModel != null ? resultInputSnapshot : null,
                ResultCapturedAt = anaModel != null ? resultCapturedAt : null,
                InputChangedSinceAnalysis = anaModel != null ? inputChangedSinceAnalysis : null,
                PileFemLinks = anaModel != null ? pileFemLinks : null,
                IsElementSplit = isElementSplit,
                // 自動保存・緊急保存だけが渡す。復元したあとの保存先を確定するために使う。
                SourceFilePath = sourceFilePath,
                AutoSaveSessionId = autoSaveSessionId,
            };

            try
            {
                // string 中間生成を避けて UTF-8 バイトへ直に書く。_jsonOptions の全設定 (WriteIndented /
                // Encoder 等) を尊重するので、同期・非同期の保存で出力が一致する。
                return new PreparedSave
                {
                    InputToSave = inputToSave,
                    Payload = JsonSerializer.SerializeToUtf8Bytes(projectData, _jsonOptions),
                    NonFiniteLocation = nonFiniteLocation,
                };
            }
            catch (Exception ex)
            {
                return new PreparedSave { InputToSave = inputToSave, Error = ex };
            }
        }

        /// <summary>
        /// <see cref="PrepareSave"/> で確定させた中身を書き出す。バックグラウンドから呼んでよい
        /// (生きたモデルを直列化も検査もしないので、保存中の編集は中身にも検査にも入らない)。
        /// </summary>
        internal void WritePrepared(string filePath, PreparedSave prepared)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            ThrowIfPrepareFailed(prepared);
            WriteAtomically(filePath, stream => stream.Write(prepared.Payload!));
        }

        /// <summary>検査・直列化で出た例外を、型とスタックを保ったまま投げ直す。</summary>
        private static void ThrowIfPrepareFailed(PreparedSave prepared)
        {
            if (prepared.Error != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prepared.Error).Throw();
        }


        /// <summary>
        /// 直列化にかける入力モデルを決める。編集できるコレクションだけを写した器を返す。
        ///
        /// 保存は別スレッドで直列化する一方、利用者は表を編集し続けられる。生きたモデルを
        /// そのまま辿ると列挙が壊れて保存が失敗する。入れ物だけを新しくし、要素は同じ実体を
        /// 指したまま渡す (詳細は <see cref="InputModel.SnapshotForSaving"/>)。
        /// 要素が同じなので保存ファイルの中身は変わらない。
        ///
        /// 解析結果と同じインスタンスを指しているときは<b>写さない</b>。保存ファイルは
        /// ProjectData.InputModel → AnaModel.InputModel → ResultInputSnapshot が同じ実体を
        /// 指すことで $ref 1 個に畳まれており、ここだけ差し替えると実体が 2 つになる。
        /// その状態は「結果を読み込んだ直後」で、利用者が編集していない場面なので写す必要もない。
        /// </summary>
        /// <remarks>
        /// <b>画面のスレッドから呼ぶこと。</b>写す処理そのものが元のコレクションを列挙するので、
        /// バックグラウンドで呼ぶと、写している最中の編集で列挙が壊れる。
        /// 自動保存は <see cref="AutoSaveService"/> が Tick (画面のスレッド) で写してから
        /// バックグラウンドへ渡す。
        /// </remarks>
        internal static InputModel? SnapshotForSaving(
            InputModel? inputModel, AnaModel? anaModel, InputModel? resultInputSnapshot)
        {
            if (inputModel == null) return null;

            if (ReferenceEquals(inputModel, resultInputSnapshot) ||
                ReferenceEquals(inputModel, anaModel?.InputModel))
            {
                return inputModel;
            }

            try
            {
                return inputModel.SnapshotForSaving();
            }
            catch (Exception ex)
            {
                // 写せなければ生きたモデルを書く (保存できないよりはよい)。
                //
                // 手動保存・自動保存では、これで編集中の変更が混ざることは<b>無い</b>。写した器を直列化するのは
                // PrepareSave で、写すのと同じ画面のスレッドで続けて行う (そのあいだ編集は割り込めない)。
                // 生きたモデルを直列化しても書かれる中身は写した場合と同じなので、利用者へは知らせない
                // (知らせても、保存し直す以外にすることが無く、保存し直しても同じ中身になる)。
                // 画面のスレッドを選べないのは緊急保存 (落ちる直前) だけで、そこは知らせる相手がいない。
                Serilog.Log.Warning(ex, "[Save] 入力モデルを写せなかったので、生きたモデルをそのまま直列化します (同じスレッドで続けて直列化するので中身は同じ)");
                return inputModel;
            }
        }

        /// <summary>
        /// 一時ファイルへ書いてから差し替える。
        ///
        /// 保存先へ直接書くと、途中で落ちた場合に<b>切り株のファイルが残る</b>。
        /// 自動保存では、その切り株が復元候補として拾われてしまう。
        /// 同じフォルダの一時ファイルに書き切ってから <see cref="File.Move(string, string, bool)"/> で
        /// 置き換えれば、保存先は「前の内容」か「新しい内容」のどちらかになる。
        /// 保存ファイルのほか、MGT・CSV の書き出しも使う。
        /// </summary>
        internal static void WriteAtomically(string filePath, Action<Stream> write)
            => ReplaceAtomically(filePath, tempPath =>
            {
                using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                write(stream);
            });

        /// <summary>
        /// <see cref="WriteAtomically"/> の、書き込み先を<b>パス</b>で受け取る版。
        /// ファイルのパスしか受け付けない部品 (計算書の docx・DXF・3dm の書き出し) に一時ファイルのパスを渡して
        /// 書かせ、書き切ってから保存先と差し替える。<paramref name="writeToPath"/> が例外を出せば差し替えず、
        /// 一時ファイルも消す (保存先は前の内容のまま)。
        /// </summary>
        internal static void ReplaceAtomically(string filePath, Action<string> writeToPath)
        {
            var gate = GateFor(filePath);
            gate.Wait();
            try
            {
                string tempPath = TempPathFor(filePath);
                try
                {
                    writeToPath(tempPath);
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    TryDelete(tempPath);
                    throw;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 保存先ごとの排他。同じ保存先への書き込みと差し替えを 1 本ずつ通す。
        ///
        /// 一時ファイルの名前を保存ごとに分けても (<see cref="TempPathFor"/>)、最後の差し替え
        /// (<see cref="File.Move(string, string, bool)"/> の上書き) 同士が同時にぶつかると、OS が一方を
        /// 「アクセスが拒否されました」で拒む。上書き保存・自動保存・緊急保存が同じファイルに重なる場合がこれにあたる。
        /// 同期 (<c>Wait</c>) と非同期 (<c>WaitAsync</c>) の保存が同じ排他を使う。
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _saveGates
            = new(StringComparer.OrdinalIgnoreCase);

        internal static System.Threading.SemaphoreSlim GateFor(string filePath)
            => _saveGates.GetOrAdd(Path.GetFullPath(filePath), _ => new System.Threading.SemaphoreSlim(1, 1));

        /// <summary>
        /// 保存 1 回ぶんの一時ファイルの名前 (保存先と同じフォルダ、<c>保存先.xxxxxxxx.saving</c>)。
        ///
        /// 以前は <c>保存先.saving</c> 固定で、同じ保存先への保存が重なると (上書き保存・自動保存・緊急保存、
        /// 同期と非同期)、一方がもう一方の一時ファイルを作り直したり消したりして、保存が失敗したり
        /// 片方の内容がもう片方の名前で差し替わったりした。保存ごとに名前を分ければ、それぞれが自分の
        /// 一時ファイルに書き切ってから差し替えるので、保存先は常にどれか 1 回ぶんの完全な内容になる。
        /// 拡張子は .saving のままにして、自動保存の復元候補 (*.pdj) に拾われないようにする。
        /// </summary>
        internal static string TempPathFor(string filePath)
            => $"{filePath}.{Guid.NewGuid().ToString("N")[..8]}.saving";

        /// <summary>一時ファイルの後始末。消せなくても保存の失敗として扱わない。</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// ProjectData を JSON ファイルに非同期保存する (手動保存が使う)。
        /// 中身の確定 (直列化) は呼び出したスレッド (画面のスレッド) で済ませ、ファイルへの書き込みだけを
        /// バックグラウンドで行う (理由は <see cref="PrepareSave"/>)。
        /// </summary>
        /// <returns>
        /// 保存した入力に NaN・無限大があれば、その箇所 (最初の 1 か所)。無ければ null。
        /// 保存は止めない (<see cref="ValidateFiniteBeforeSave"/> が false のとき)。呼び出し側が警告として知らせる。
        /// </returns>
        public async Task<string?> SaveProjectDataAsync(string filePath, InputModel inputModel, AnaModel? anaModel,
            IList<FEM.VerticalBeamCaseResult>? verticalBeamCaseResults = null,
            InputModel? resultInputSnapshot = null, DateTime? resultCapturedAt = null,
            PileFemLinkTable? pileFemLinks = null, bool? isElementSplit = null,
            bool inputChangedSinceAnalysis = false, string? sourceFilePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            var swTotal = Stopwatch.StartNew();

            // 保存する値は<b>ここ (画面のスレッド) で確定させる</b>。最初の await より前に済ませること。
            // NaN 検査は既定では行わない (ValidateFiniteBeforeSave 参照)。行うときも確定と同じ時点で行う。
            var swSer = Stopwatch.StartNew();
            var prepared = PrepareSave(inputModel, anaModel, verticalBeamCaseResults, resultInputSnapshot,
                resultCapturedAt, pileFemLinks, isElementSplit, inputChangedSinceAnalysis, sourceFilePath,
                validateFinite: ValidateFiniteBeforeSave);
            swSer.Stop();
            long tSerialize = swSer.ElapsedMilliseconds;

            long tWrite = 0;
            long fileSize = 0;

            await Task.Run(async () =>
            {
                ThrowIfPrepareFailed(prepared);

                var swWrite = Stopwatch.StartNew();
                const int bufferSize = 1024 * 1024;  // 1 MB バッファ (旧 80KB → I/O 回数削減)

                // 一時ファイルへ書き切ってから差し替える (WriteAtomically と同じ理由)。
                // 同じ保存先への保存は 1 本ずつ通す (GateFor 参照)
                var gate = GateFor(filePath);
                await gate.WaitAsync();
                string tempPath = TempPathFor(filePath);
                try
                {
                    await using (var stream = new FileStream(
                        tempPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await stream.WriteAsync(prepared.Payload!);
                        await stream.FlushAsync();
                        fileSize = stream.Length;
                    }
                    File.Move(tempPath, filePath, overwrite: true);
                }
                catch
                {
                    TryDelete(tempPath);
                    throw;
                }
                finally
                {
                    gate.Release();
                }

                swWrite.Stop();
                tWrite = swWrite.ElapsedMilliseconds;
            });

            swTotal.Stop();
            Log.Information(
                "[Save] total={Total}ms prepare(validate+serialize)={Serialize}ms write={Write}ms size={SizeKB:N0}KB path={Path}",
                swTotal.ElapsedMilliseconds, tSerialize, tWrite, fileSize / 1024, System.IO.Path.GetFileName(filePath));
            if (prepared.NonFiniteLocation != null)
                Log.Warning("[Save] NaN・無限大を含んだまま保存しました: {Location}", prepared.NonFiniteLocation);
            return prepared.NonFiniteLocation;
        }

        /// <summary>手動保存のあとに出す警告の文。<paramref name="location"/> は <see cref="SaveProjectDataAsync"/> の戻り値。</summary>
        internal static string DescribeSavedNonFinite(string location)
            => "保存しましたが、数値として扱えない値 (NaN・無限大) が含まれています。\n"
               + $"該当箇所: {location}\n"
               + "値を確認して入力し直してください。このまま解析すると、結果が正しく求まりません。\n"
               + "(作業を失わないよう、保存そのものは止めていません)";

        /// <summary>
        /// InputModel の中に NaN や ±∞ が含まれていれば、そのフィールドパスを示す
        /// InvalidOperationException を投げる。
        /// JSON シリアライズ前にプリチェックすることで、System.Text.Json のデフォルト
        /// 例外メッセージ (どの値が問題かを示さない) を、利用者が値を特定できる形に置換する。
        /// </summary>
        internal static void ValidateFinite(InputModel? inputModel)
        {
            if (inputModel == null) return;
            var path = FindNonFiniteDouble(inputModel, "InputModel");
            if (path != null)
            {
                throw new InvalidOperationException(
                    $"NaN または無限大の値が含まれているため保存できません。\n該当箇所: {path}\n値を確認してください。");
            }
        }

        // Type → 走査対象 PropertyInfo 配列のキャッシュ。
        // 旧版は毎オブジェクトで GetProperties + GetCustomAttribute を呼び、
        // 数万オブジェクト × 数十プロパティで数百万回のリフレクションが発生していた。
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _validateCache = new();

        private static PropertyInfo[] GetValidateProperties(Type t)
        {
            return _validateCache.GetOrAdd(t, type =>
            {
                var list = new List<PropertyInfo>();
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (p.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    var pt = p.PropertyType;
                    // double / double? / 参照型 (PileDesign 名前空間に再帰可能) のみ対象
                    bool keep =
                        pt == typeof(double) ||
                        pt == typeof(double?) ||
                        (!pt.IsValueType && p.CanRead);
                    if (keep) list.Add(p);
                }
                return list.ToArray();
            });
        }

        private static string? FindNonFiniteDouble(object root, string rootName)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return Recurse(root, rootName);

            string? Recurse(object? obj, string path)
            {
                if (obj == null) return null;
                if (obj is string) return null;
                if (!visited.Add(obj)) return null;

                var t = obj.GetType();
                if (t.IsPrimitive || t.IsEnum || t == typeof(decimal) ||
                    t == typeof(DateTime) || t == typeof(Guid)) return null;

                if (obj is System.Collections.IEnumerable e && obj is not System.Collections.IDictionary)
                {
                    int i = 0;
                    foreach (var item in e)
                    {
                        var r = Recurse(item, $"{path}[{i}]");
                        if (r != null) return r;
                        i++;
                    }
                    return null;
                }

                // 自社型 (PileDesign.* 名前空間) のみ深く再帰する。
                // System.* やフレームワーク型に踏み込んでも誤検知や循環を招くだけ。
                var ns = t.Namespace ?? "";
                if (!ns.StartsWith("PileDesign", StringComparison.Ordinal)) return null;

                foreach (var p in GetValidateProperties(t))
                {
                    var pt = p.PropertyType;

                    if (pt == typeof(double))
                    {
                        double v;
                        try
                        {
                            var raw = p.GetValue(obj);
                            if (raw == null) continue;
                            v = (double)raw;
                        }
                        catch { continue; }
                        if (!double.IsFinite(v))
                            return $"{path}.{p.Name} = {FormatNonFinite(v)}";
                    }
                    else if (pt == typeof(double?))
                    {
                        double? v;
                        try { v = (double?)p.GetValue(obj); } catch { continue; }
                        if (v.HasValue && !double.IsFinite(v.Value))
                            return $"{path}.{p.Name} = {FormatNonFinite(v.Value)}";
                    }
                    else
                    {
                        // 参照型 → 再帰
                        object? child;
                        try { child = p.GetValue(obj); } catch { continue; }
                        var r = Recurse(child, $"{path}.{p.Name}");
                        if (r != null) return r;
                    }
                }
                return null;
            }
        }

        private static string FormatNonFinite(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "+∞";
            if (double.IsNegativeInfinity(v)) return "-∞";
            return v.ToString();
        }

        /// <summary>
        /// JSON ファイルから ProjectData を読み込み
        /// </summary>
        public ProjectData LoadProjectData(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません。", filePath);

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (IOException ex)
            {
                throw new IOException($"ファイルの読込に失敗しました。\n別のプロセスで使用中の可能性があります。\n{filePath}", ex);
            }

            ProjectData? projectData;
            try
            {
                projectData = JsonSerializer.Deserialize<ProjectData>(json, _jsonOptions);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException(
                    $"ファイルのJSON形式が不正です。ファイルが破損しているか、対応していない形式です。\n{filePath}", ex);
            }

            if (projectData == null)
                throw new InvalidOperationException("ファイル形式が不正です。");

            // バージョン0はFormatVersionプロパティ追加前の旧ファイル → 互換あり
            // v1: 初期形式
            // v2: PileLayoutItems[*].Z のセマンティクスを「杭頭節点」→「接合節点」に変更 (2026-05 改修)
            //     v1 ファイルはロード時に MigratePileZSemantics_v1_to_v2 で内部的に v2 化される
            const int currentVersion = 2;
            if (projectData.FormatVersion > currentVersion)
            {
                throw new InvalidOperationException(
                    $"このファイルは新しいバージョン（v{projectData.FormatVersion}）で保存されています。\n" +
                    $"現在のプログラム（v{currentVersion}）では読み込めません。プログラムを更新してください。");
            }

            return projectData;
        }

        /// <summary>
        /// JSON ファイルから ProjectData を非同期読み込み（UIスレッドをブロックしない）
        /// </summary>
        public async Task<ProjectData> LoadProjectDataAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが指定されていません。", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません。", filePath);

            // I/O + デシリアライズをバックグラウンドスレッドで実行
            return await Task.Run(() => LoadProjectData(filePath));
        }

        /// <summary>
        /// InputModel の全コレクションを ObservableCollection に変換
        /// </summary>
        public void ConvertToObservableCollections(InputModel inputModel)
        {
            if (inputModel == null)
                throw new ArgumentNullException(nameof(inputModel));

            // PileGroupSettlement のコレクション変換
            if (inputModel.PileGroupSettlement != null)
            {
                inputModel.PileGroupSettlement.SettlementSoilLayers =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementSoilLayers);
                inputModel.PileGroupSettlement.RectLoads =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.RectLoads);
                inputModel.PileGroupSettlement.SettlementGridX =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridX);
                inputModel.PileGroupSettlement.SettlementGridY =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.SettlementGridY);
                // 読み込み専用の複製。旧ファイルを開くためだけに残してある
                inputModel.PileGroupSettlement.SettlementGridData =
                    EnsureObservableCollection(inputModel.PileGroupSettlement.LegacySettlementGridData);
            }

            // トップレベルのコレクション変換
            inputModel.PileLayoutItems = EnsureObservableCollection(inputModel.PileLayoutItems);
            inputModel.InputNodes = EnsureObservableCollection(inputModel.InputNodes);

            inputModel.GridXItems = EnsureObservableCollection(inputModel.GridXItems);
            inputModel.GridYItems = EnsureObservableCollection(inputModel.GridYItems);
            inputModel.PileBodies = EnsureObservableCollection(inputModel.PileBodies);
            inputModel.GroundsInput = EnsureObservableCollection(inputModel.GroundsInput);

            // EmbedmentInput のコレクション変換
            if (inputModel.EmbedmentInput != null)
            {
                inputModel.EmbedmentInput.EmbedmentLayers =
                    EnsureObservableCollection(inputModel.EmbedmentInput.EmbedmentLayers);
            }

            // LoadCasesInput のコレクション変換
            if (inputModel.LoadCasesInput != null)
            {
                inputModel.LoadCasesInput.LoadCasesLevel1 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel1);
                inputModel.LoadCasesInput.LoadCasesLevel2 =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCasesLevel2);
                inputModel.LoadCasesInput.LoadCombinations =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinations);
                inputModel.LoadCasesInput.LoadCombinationsPlus =
                    EnsureObservableCollection(inputModel.LoadCasesInput.LoadCombinationsPlus);
            }

            // ネストされたコレクションの変換
            if (inputModel.GroundsInput != null)
            {
                foreach (var ground in inputModel.GroundsInput)
                {
                    ground.GroundLayers = EnsureObservableCollection(ground.GroundLayers);
                    ground.GroundMassesData = EnsureObservableCollection(ground.GroundMassesData);
                }
            }

            if (inputModel.PileBodies != null)
            {
                foreach (var pileBody in inputModel.PileBodies)
                {
                    pileBody.PileBodySegments = EnsureObservableCollection(pileBody.PileBodySegments);
                }
            }

            // FoundationBeamInput のコレクション変換
            if (inputModel.FoundationBeamInput != null)
            {
                inputModel.FoundationBeamInput.Materials =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Materials);
                inputModel.FoundationBeamInput.Sections =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Sections);
                inputModel.FoundationBeamInput.Nodes =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Nodes);
                inputModel.FoundationBeamInput.Beams =
                    EnsureObservableCollection(inputModel.FoundationBeamInput.Beams);

                // 梁要素があるが材料・断面が空のとき、参照先を保証するためデフォルトエントリを追加
                if (inputModel.FoundationBeamInput.Beams.Count > 0)
                {
                    inputModel.FoundationBeamInput.EnsureDefaultMaterialAndSection();
                }
            }

            // Null チェック・空コレクション初期化
            inputModel.GridXItems ??= new ObservableCollection<GridDataItem>();
            inputModel.GridYItems ??= new ObservableCollection<GridDataItem>();
        }

        /// <summary>
        /// IEnumerable を ObservableCollection に変換（既に ObservableCollection の場合はそのまま返す）
        /// </summary>
        private static ObservableCollection<T> EnsureObservableCollection<T>(IEnumerable<T>? source)
        {
            if (source is ObservableCollection<T> observableCollection)
                return observableCollection;

            return source != null ? new ObservableCollection<T>(source) : new ObservableCollection<T>();
        }
    }
}
