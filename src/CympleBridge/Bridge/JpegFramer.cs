namespace CympleBridge.Bridge;

/// <summary>Splits a raw byte stream into JPEG frames by SOI (FF D8 FF) and EOI (FF D9) markers.</summary>
public sealed class JpegFramer
{
    private byte[] _buf = new byte[1 << 16];
    private int _len;

    private static readonly byte[] Soi = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Eoi = { 0xFF, 0xD9 };

    public IEnumerable<byte[]> Push(byte[] data, int count)
    {
        Ensure(_len + count);
        Buffer.BlockCopy(data, 0, _buf, _len, count);
        _len += count;

        while (true)
        {
            int start = IndexOf(_buf, _len, 0, Soi);
            if (start < 0)
            {
                if (_len > 2) { _buf[0] = _buf[_len - 2]; _buf[1] = _buf[_len - 1]; _len = 2; }
                yield break;
            }

            int end = IndexOf(_buf, _len, start + 3, Eoi);
            if (end < 0)
            {
                if (start > 0) { Buffer.BlockCopy(_buf, start, _buf, 0, _len - start); _len -= start; }
                yield break;
            }

            int frameLen = end + 2 - start;
            var frame = new byte[frameLen];
            Buffer.BlockCopy(_buf, start, frame, 0, frameLen);

            int consumed = end + 2;
            Buffer.BlockCopy(_buf, consumed, _buf, 0, _len - consumed);
            _len -= consumed;

            yield return frame;
        }
    }

    private void Ensure(int needed)
    {
        if (needed <= _buf.Length) return;
        int size = _buf.Length;
        while (size < needed) size *= 2;
        Array.Resize(ref _buf, size);
    }

    private static int IndexOf(byte[] haystack, int length, int from, byte[] needle)
    {
        for (int i = from; i <= length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
