using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PileDesign.Services
{
    /// <summary>
    /// 直列化した保存の中身 (JSON の UTF-8)。書き込み専用の流れとして直列化を受け、決まった大きさの塊の列で持つ。
    ///
    /// <para>以前は 1 つの配列 (<c>JsonSerializer.SerializeToUtf8Bytes</c>) で持っていた。.NET の配列は 2 GB までで、
    /// 解析結果の多いモデル (報告は節点・梁要素・地盤ばねの結果で 3.27 GB) は保存できなかった。
    /// 中身を画面のスレッドで確定させ、書き出しだけをバックグラウンドで行う作り (<see cref="FileOperationService.PrepareSave"/>)
    /// はそのまま。</para>
    /// </summary>
    internal sealed class SavePayload : Stream
    {
        /// <summary>塊 1 つの大きさ。</summary>
        internal const int ChunkSize = 4 * 1024 * 1024;

        private readonly List<byte[]> _chunks = [];
        private int _lastUsed = ChunkSize;
        private long _length;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                if (_lastUsed == ChunkSize)
                {
                    _chunks.Add(new byte[ChunkSize]);
                    _lastUsed = 0;
                }
                int n = Math.Min(buffer.Length, ChunkSize - _lastUsed);
                buffer[..n].CopyTo(_chunks[^1].AsSpan(_lastUsed));
                _lastUsed += n;
                _length += n;
                buffer = buffer[n..];
            }
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void WriteByte(byte value) => Write([value]);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>塊を、書いた順に並べる (最後の塊は使った分だけ)。</summary>
        private IEnumerable<ReadOnlyMemory<byte>> Segments()
        {
            for (int i = 0; i < _chunks.Count; i++)
                yield return _chunks[i].AsMemory(0, i == _chunks.Count - 1 ? _lastUsed : ChunkSize);
        }

        /// <summary>中身を <paramref name="destination"/> へ書く。</summary>
        public void WriteContentTo(Stream destination)
        {
            foreach (var segment in Segments())
                destination.Write(segment.Span);
        }

        /// <inheritdoc cref="WriteContentTo"/>
        public async Task WriteContentToAsync(Stream destination)
        {
            foreach (var segment in Segments())
                await destination.WriteAsync(segment);
        }

        /// <summary>中身を 1 つの配列で返す (2 GB 未満のときだけ。テスト・小さな中身用)。</summary>
        public byte[] ToArray()
        {
            var all = new byte[checked((int)_length)];
            int at = 0;
            foreach (var segment in Segments())
            {
                segment.Span.CopyTo(all.AsSpan(at));
                at += segment.Length;
            }
            return all;
        }
    }
}
