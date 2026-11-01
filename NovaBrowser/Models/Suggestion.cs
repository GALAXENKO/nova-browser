namespace NovaBrowser.Models;

public sealed class Suggestion
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Url { get; set; } = "";
    public string Glyph { get; set; } = "\uE774";
    public bool IsSearch { get; set; }
}
