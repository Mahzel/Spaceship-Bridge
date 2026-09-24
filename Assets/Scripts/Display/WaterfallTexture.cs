using UnityEngine;

/// <summary>
/// Scrolling waterfall backed by ONE Texture2D used as a ring buffer.
///
/// Each new line overwrites the oldest row (one row upload, no per-point GameObjects), then the
/// display is "scrolled" by moving a UV offset instead of moving any pixels.
/// Show it on a RawImage: image.texture = wf.Texture; image.uvRect = wf.UvRect (after each push).
/// Newest line is at the top, oldest at the bottom.
/// </summary>
public sealed class WaterfallTexture
{
    public Texture2D Texture { get; }
    public int Width  { get; }
    public int Height { get; }

    private readonly Color32[] _row;
    private readonly Color32[] _lut = new Color32[256];
    private int _head; // row that will be overwritten next (= current oldest row)

    /// <summary>Rect to assign to RawImage.uvRect so the ring buffer displays in order.</summary>
    public Rect UvRect => new Rect(0f, _head / (float)Height, 1f, 1f);

    public WaterfallTexture(int width, int height)
    {
        Width  = Mathf.Max(1, width);
        Height = Mathf.Max(1, height);
        _row   = new Color32[Width];

        Texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,   // crisp bins
            wrapModeU  = TextureWrapMode.Repeat, // bearing wraps at +/-180: a zoomed view may straddle it
            wrapModeV  = TextureWrapMode.Repeat, // needed for the ring-buffer scroll
            name       = "WaterfallTexture"
        };

        BuildDefaultPalette();
        Clear();
    }

    /// <summary>Palette: black to light green (matches the old look). Swap for a theme colour later.</summary>
    private void BuildDefaultPalette()
    {
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            _lut[i] = new Color32((byte)(127.5f * t), (byte)(255f * t), (byte)(127.5f * t), 255);
        }
    }

    public void SetPalette(System.Func<float, Color32> ramp)
    {
        for (int i = 0; i < 256; i++) _lut[i] = ramp(i / 255f);
    }

    public void Clear()
    {
        var black = new Color32[Width * Height];
        for (int i = 0; i < black.Length; i++) black[i] = new Color32(0, 0, 0, 255);
        Texture.SetPixels32(black);
        Texture.Apply(false);
        _head = 0;
    }

    /// <summary>Adds one line of values in 0..1 (clamped). Extra/missing bins are ignored/zero.</summary>
    public void PushLine(float[] data)
    {
        for (int x = 0; x < Width; x++)
        {
            float v = (data != null && x < data.Length) ? data[x] : 0f;
            _row[x] = _lut[Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255)];
        }

        Texture.SetPixels32(0, _head, Width, 1, _row);
        Texture.Apply(false);
        _head = (_head + 1) % Height;
    }

    public void Destroy()
    {
        if (Texture != null) Object.Destroy(Texture);
    }
}
