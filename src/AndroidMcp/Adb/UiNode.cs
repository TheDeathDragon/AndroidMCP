namespace AndroidMcp.Adb;

internal sealed class UiNode
{
    public string Text { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string ContentDesc { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
