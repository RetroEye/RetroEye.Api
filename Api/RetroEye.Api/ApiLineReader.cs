// RetroEye app API, contract v1: reading the protocol's lines. RetroEye itself reads its clients' lines with this same file.
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RetroEye.Api
{
    /// <summary>
    /// Reads lines of UTF-8 (the protocol's messages, one JSON object a line) from a stream, refusing any longer than
    /// a limit: a longer line is skipped to its end and reported with <see cref="LastWasTooLong"/>.
    /// </summary>
    internal sealed class ApiLineReader
    {
        private readonly Stream _stream;
        private readonly int _limit;
        private readonly byte[] _buffer = new byte[8192];
        private readonly List<byte> _line = new List<byte>();
        private int _length, _at;

        /// <summary>Reads lines from the stream, each at most limit bytes.</summary>
        public ApiLineReader(Stream stream, int limit)
        {
            _stream = stream;
            _limit = limit;
        }

        /// <summary>Whether the last line read was longer than the limit (it is given as "" and skipped to its end).</summary>
        public bool LastWasTooLong { get; private set; }

        /// <summary>Returns the next line without its newline (and a "\r" before it), or null at the end of the stream.</summary>
        public async Task<string?> ReadLineAsync(CancellationToken cancel)
        {
            _line.Clear();
            LastWasTooLong = false;
            while (true)
            {
                if (_at >= _length)
                {
                    _length = await _stream.ReadAsync(_buffer, 0, _buffer.Length, cancel).ConfigureAwait(false);
                    _at = 0;
                    if (_length <= 0)
                    {
                        return _line.Count > 0 && !LastWasTooLong ? Text() : null;
                    }
                }
                byte b = _buffer[_at++];
                if (b == (byte)'\n')
                {
                    return LastWasTooLong ? "" : Text();
                }
                if (_line.Count >= _limit)
                {
                    LastWasTooLong = true;
                    continue;
                }
                _line.Add(b);
            }
        }

        /// <summary>Returns the line read so far as text.</summary>
        private string Text()
        {
            int count = _line.Count;
            if (count > 0 && _line[count - 1] == (byte)'\r')
            {
                count--;
            }
            return Encoding.UTF8.GetString(_line.ToArray(), 0, count);
        }
    }
}
