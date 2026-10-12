namespace CympleBridge.Bridge;

/// <summary>
/// Cymple image transport. A tracker sends JPEG frames to the desktop app as MSG_IMAGE_V2
/// TLV packets over UDP (port 23303), split into chunks of at most MAX_CHUNK bytes:
///   u16 type=5, u16 len=dataLen+8, u16 offset, u16 total, u16 dataLen, u8 deviceFlag, u8 frameIndex, data
/// deviceFlag: 0 left eye, 1 right eye, 2 mouth.
/// </summary>
public static class CymplePackets
{
    public const ushort MsgImageV2 = 5;
    public const int MaxChunk = 1400;
    public const byte FlagLeftEye = 0;
    public const byte FlagRightEye = 1;
    public const byte FlagMouth = 2;

    public static IEnumerable<byte[]> BuildSegments(byte[] data, byte deviceFlag, byte frameIndex)
    {
        int total = data.Length;
        for (int offset = 0; offset < total; offset += MaxChunk)
        {
            int len = Math.Min(MaxChunk, total - offset);
            var packet = new byte[12 + len];
            BitConverter.GetBytes(MsgImageV2).CopyTo(packet, 0);
            BitConverter.GetBytes((ushort)(len + 8)).CopyTo(packet, 2);
            BitConverter.GetBytes((ushort)offset).CopyTo(packet, 4);
            BitConverter.GetBytes((ushort)total).CopyTo(packet, 6);
            BitConverter.GetBytes((ushort)len).CopyTo(packet, 8);
            packet[10] = deviceFlag;
            packet[11] = frameIndex;
            Buffer.BlockCopy(data, offset, packet, 12, len);
            yield return packet;
        }
    }
}
