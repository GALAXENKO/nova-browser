namespace NovaBrowser.Models;

public sealed class Bookmark
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;

    public string Host
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            }

            return Url;
        }
    }

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "•" : Title.Trim()[..1].ToUpperInvariant();
}
