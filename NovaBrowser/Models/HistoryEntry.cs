namespace NovaBrowser.Models;

public sealed class HistoryEntry
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime VisitedAt { get; set; } = DateTime.Now;

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

    public string DisplayTime
    {
        get
        {
            var today = DateTime.Today;
            if (VisitedAt.Date == today)
            {
                return VisitedAt.ToString("HH:mm");
            }

            if (VisitedAt.Date == today.AddDays(-1))
            {
                return "вчера, " + VisitedAt.ToString("HH:mm");
            }

            return VisitedAt.ToString("dd.MM.yyyy, HH:mm");
        }
    }

    public string Initial => string.IsNullOrWhiteSpace(Title) ? "•" : Title.Trim()[..1].ToUpperInvariant();
}
