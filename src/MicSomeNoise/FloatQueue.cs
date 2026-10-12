using System;

namespace MicSomeNoise;

// Drop old samples when device clocks drift. The queue is bounded and always frame-aligned.
internal sealed class FloatQueue(int capacity)
{
    private readonly float[] data = new float[capacity];
    private int start, length;
    public void Clear() { start = 0; length = 0; }
    public void Write(float[] source, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (length == data.Length) { start = (start + 2) % data.Length; length -= 2; }
            data[(start + length++) % data.Length] = source[offset + i];
        }
    }
    public void Read(float[] target, int offset, int count)
    {
        int take = Math.Min(count, length);
        for (int i = 0; i < take; i++) target[offset + i] = data[(start + i) % data.Length];
        start = (start + take) % data.Length; length -= take;
    }
}
