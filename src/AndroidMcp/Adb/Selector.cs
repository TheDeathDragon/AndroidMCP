namespace AndroidMcp.Adb;

internal sealed class Selector
{
    public string? Text { get; set; }
    public string? ResourceId { get; set; }
    public string? ContentDesc { get; set; }
    public string? ClassName { get; set; }
    public string? Package { get; set; }
    public string? Xpath { get; set; }
    public bool Partial { get; set; }
    public bool? Checkable { get; set; }
    public bool? Checked { get; set; }
    public bool? Clickable { get; set; }
    public bool? Enabled { get; set; }
    public bool? Focusable { get; set; }
    public bool? Focused { get; set; }
    public bool? LongClickable { get; set; }
    public bool? Password { get; set; }
    public bool? Scrollable { get; set; }
    public bool? Selected { get; set; }

    public bool IsEmpty =>
        string.IsNullOrEmpty(Text) &&
        string.IsNullOrEmpty(ResourceId) &&
        string.IsNullOrEmpty(ContentDesc) &&
        string.IsNullOrEmpty(ClassName) &&
        string.IsNullOrEmpty(Package) &&
        string.IsNullOrEmpty(Xpath) &&
        Checkable is null && Checked is null && Clickable is null &&
        Enabled is null && Focusable is null && Focused is null &&
        LongClickable is null && Password is null &&
        Scrollable is null && Selected is null;

    public string DescribeShort()
    {
        if (!string.IsNullOrEmpty(Xpath))
        {
            return $"xpath={Xpath}";
        }
        if (!string.IsNullOrEmpty(Text))
        {
            return Partial ? $"text~{Text}" : $"text={Text}";
        }
        if (!string.IsNullOrEmpty(ResourceId))
        {
            return $"id={ResourceId}";
        }
        if (!string.IsNullOrEmpty(ContentDesc))
        {
            return Partial ? $"desc~{ContentDesc}" : $"desc={ContentDesc}";
        }
        if (!string.IsNullOrEmpty(ClassName))
        {
            return $"cls={ClassName}";
        }
        if (!string.IsNullOrEmpty(Package))
        {
            return $"pkg={Package}";
        }
        return "<attrs>";
    }
}
