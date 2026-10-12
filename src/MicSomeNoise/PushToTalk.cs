using System;
using System.Threading;

namespace MicSomeNoise;

public sealed class PushToTalk(Action<ushort, bool> sendKey) : IDisposable
{
    internal const int ReleaseTailMilliseconds = 120;
    private readonly object gate = new();
    private bool enabled;
    private ushort key, heldKey;
    private Timer? releaseTimer;
    private int generation;
    public event Action<string>? Error;
    public void Configure(bool enabled, ushort key)
    {
        lock (gate)
        {
            Release();
            if (heldKey != 0) throw new InvalidOperationException("The previous push-to-talk key could not be released. Press and release it physically, then try again.");
            this.enabled = enabled; this.key = key;
        }
    }
    public void Hold()
    {
        lock (gate)
        {
            generation++; releaseTimer?.Dispose(); releaseTimer = null;
            if (!enabled || heldKey != 0) return;
            sendKey(key, true);
            heldKey = key;
        }
    }
    public void ReleaseAfterPlayback()
    {
        lock (gate)
        {
            if (heldKey == 0) return;
            int request = ++generation;
            releaseTimer?.Dispose();
            // Allow the final WASAPI output buffer to drain before closing voice transmission.
            releaseTimer = new Timer(_ => { lock (gate) { if (request == generation) Release(); } }, null, ReleaseTailMilliseconds, Timeout.Infinite);
        }
    }
    public void Release()
    {
        lock (gate)
        {
            generation++; releaseTimer?.Dispose(); releaseTimer = null;
            if (heldKey == 0) return;
            try { sendKey(heldKey, false); heldKey = 0; }
            catch (Exception e) { Error?.Invoke("Could not release push-to-talk. Press and release the key physically, then press Stop. " + e.Message); }
        }
    }
    public void Dispose() => Release();
}
