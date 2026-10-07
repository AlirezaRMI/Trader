using System.Text;

namespace Trader.Protocol;

public sealed class LineFrameReader(Stream stream, int maximumBytes = WireProtocol.MaximumFrameBytes)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _end;

    // A reader has one owner. Leftover bytes belong to the next frame, not the current request.
    public async ValueTask<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var frame = new MemoryStream();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_start == _end)
            {
                _end = await stream.ReadAsync(_buffer, cancellationToken);
                _start = 0;
                if (_end == 0)
                    throw new EndOfStreamException("Broker disconnected before a complete frame");
            }

            var newline = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            var count = (newline < 0 ? _end : newline) - _start;
            if (frame.Length + count > maximumBytes)
                throw new InvalidDataException("Broker frame exceeds the maximum size");
            frame.Write(_buffer, _start, count);
            _start += count;
            if (newline < 0)
                continue;
            _start++; // Consume only this frame's delimiter.
            return Utf8.GetString(frame.GetBuffer(), 0, checked((int)frame.Length));
        }
    }
}
