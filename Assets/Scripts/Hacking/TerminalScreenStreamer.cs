using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Operator-only: captures the local screen a few times per second while a terminal session is
/// open, compresses it to a small JPEG and sends it through the player's terminal relay so the
/// teammate sees the same UI on the physical monitor. Low resolution on purpose: the big picture
/// is readable, fine print still needs the radio.
/// </summary>
public sealed class TerminalScreenStreamer : MonoBehaviour
{
    private const int Width = 320;
    private const int Height = 180;
    private const int JpegQuality = 45;
    private const float Interval = 0.25f;

    private string terminalId;
    private CooperativeTerminalHintRelay relay;
    private RenderTexture fullFrame;
    private RenderTexture smallFrame;
    private ushort frameId;
    private bool readbackPending;
    private bool loopback;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>Test path: shows the captured frames on this client's own monitor (no network).</summary>
    public static TerminalScreenStreamer BeginLoopback(GameObject host, string terminalId)
    {
        var streamer = host.AddComponent<TerminalScreenStreamer>();
        streamer.terminalId = terminalId;
        streamer.loopback = true;
        return streamer;
    }
#endif

    public static TerminalScreenStreamer Begin(GameObject host, string terminalId)
    {
        Unity.Netcode.NetworkManager manager = Unity.Netcode.NetworkManager.Singleton;
        CooperativeTerminalHintRelay relay = manager != null && manager.IsListening && manager.IsClient
            ? manager.LocalClient?.PlayerObject?.GetComponentInChildren<CooperativeTerminalHintRelay>(true)
            : null;
        if (relay == null || !relay.IsSpawned || !relay.IsOwner ||
            (manager.IsServer && manager.ConnectedClientsIds.Count < 2))
            return null; // solo host: nobody to show it to

        var streamer = host.AddComponent<TerminalScreenStreamer>();
        streamer.terminalId = terminalId;
        streamer.relay = relay;
        return streamer;
    }

    private void OnEnable() => StartCoroutine(CaptureLoop());

    private void OnDestroy()
    {
        if (fullFrame != null) { fullFrame.Release(); Destroy(fullFrame); }
        if (smallFrame != null) { smallFrame.Release(); Destroy(smallFrame); }
    }

    private IEnumerator CaptureLoop()
    {
        var endOfFrame = new WaitForEndOfFrame();
        var interval = new WaitForSecondsRealtime(Interval);
        while (enabled)
        {
            yield return interval;
            if (readbackPending || (relay == null && !loopback)) continue;
            yield return endOfFrame;
            Capture();
        }
    }

    private void Capture()
    {
        smallFrame ??= new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
#if UNITY_EDITOR
        // In the editor the back buffer holds the whole editor window, so read the Game view
        // the same way screenshots do (slower CPU path; players run the GPU path below).
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        Graphics.Blit(shot, smallFrame);
        Destroy(shot);
#else
        if (fullFrame == null || fullFrame.width != Screen.width || fullFrame.height != Screen.height)
        {
            if (fullFrame != null) { fullFrame.Release(); Destroy(fullFrame); }
            fullFrame = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 0, RenderTextureFormat.ARGB32);
        }

        ScreenCapture.CaptureScreenshotIntoRenderTexture(fullFrame);
        // The back buffer comes back upside down on top-left-origin APIs (D3D, Vulkan, Metal).
        if (SystemInfo.graphicsUVStartsAtTop)
            Graphics.Blit(fullFrame, smallFrame, new Vector2(1f, -1f), new Vector2(0f, 1f));
        else
            Graphics.Blit(fullFrame, smallFrame);
#endif

        readbackPending = true;
        AsyncGPUReadback.Request(smallFrame, 0, TextureFormat.RGBA32, OnReadback);
    }

    private void OnReadback(AsyncGPUReadbackRequest request)
    {
        readbackPending = false;
        if (this == null || request.hasError || (relay == null && !loopback)) return;
        NativeArray<byte> pixels = request.GetData<byte>();
        using NativeArray<byte> jpeg = ImageConversion.EncodeNativeArrayToJPG(
            pixels, GraphicsFormat.R8G8B8A8_UNorm, Width, Height, 0, JpegQuality);
        byte[] bytes = jpeg.ToArray();
        if (loopback) ConnectionDevice.ApplyMirrorFrame(terminalId, bytes);
        else relay.SendTerminalMirrorFrame(terminalId, ++frameId, bytes);
    }
}

/// <summary>Reassembles mirror chunks on receiving clients and hands complete frames to the monitor.</summary>
public static class TerminalMirrorAssembler
{
    private sealed class Pending
    {
        public ushort Frame;
        public byte[][] Parts;
        public int Received;
    }

    private static readonly Dictionary<string, Pending> Frames = new();

    public static void Receive(string terminalId, ushort frame, byte index, byte count, byte[] data)
    {
        if (!Frames.TryGetValue(terminalId, out Pending pending) || pending.Frame != frame || pending.Parts.Length != count)
        {
            pending = new Pending { Frame = frame, Parts = new byte[count][] };
            Frames[terminalId] = pending;
        }
        if (index >= pending.Parts.Length || pending.Parts[index] != null) return;
        pending.Parts[index] = data;
        if (++pending.Received < pending.Parts.Length) return;

        int length = 0;
        foreach (byte[] part in pending.Parts) length += part.Length;
        byte[] jpeg = new byte[length];
        int offset = 0;
        foreach (byte[] part in pending.Parts)
        {
            System.Buffer.BlockCopy(part, 0, jpeg, offset, part.Length);
            offset += part.Length;
        }
        Frames.Remove(terminalId);
        ConnectionDevice.ApplyMirrorFrame(terminalId, jpeg);
    }
}
