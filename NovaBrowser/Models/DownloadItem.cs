using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace NovaBrowser.Models;

public sealed class DownloadItem : INotifyPropertyChanged
{
    private double _progress;
    private string _statusText = "Загрузка…";
    private bool _isComplete;
    private bool _isInterrupted;
    private string _sizeText = "";

    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime StartedAt { get; } = DateTime.Now;
    public CoreWebView2DownloadOperation? Operation { get; set; }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsComplete
    {
        get => _isComplete;
        set
        {
            if (SetField(ref _isComplete, value))
            {
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public bool IsInterrupted
    {
        get => _isInterrupted;
        set => SetField(ref _isInterrupted, value);
    }

    public string SizeText
    {
        get => _sizeText;
        set => SetField(ref _sizeText, value);
    }

    public bool CanOpen => IsComplete && File.Exists(FullPath);

    public void Refresh()
    {
        if (Operation is null)
        {
            return;
        }

        var received = Operation.BytesReceived;
        var total = Operation.TotalBytesToReceive ?? 0;
        if (total > 0)
        {
            Progress = Math.Clamp(received * 100.0 / total, 0, 100);
            SizeText = $"{FormatSize(received)} из {FormatSize((long)total)}";
        }
        else
        {
            SizeText = FormatSize(received);
        }

        switch (Operation.State)
        {
            case CoreWebView2DownloadState.InProgress:
                StatusText = total > 0 ? $"Загрузка · {Progress:0}%" : "Загрузка…";
                break;
            case CoreWebView2DownloadState.Completed:
                IsComplete = true;
                IsInterrupted = false;
                Progress = 100;
                StatusText = "Готово";
                FullPath = Operation.ResultFilePath;
                break;
            case CoreWebView2DownloadState.Interrupted:
                IsInterrupted = true;
                StatusText = "Загрузка прервана";
                FullPath = Operation.ResultFilePath;
                break;
        }

        OnPropertyChanged(nameof(CanOpen));
    }

    public void OpenFile()
    {
        if (!CanOpen)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(FullPath) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell errors.
        }
    }

    public void ShowInFolder()
    {
        try
        {
            if (File.Exists(FullPath))
            {
                Process.Start("explorer.exe", $"/select,\"{FullPath}\"");
            }
            else
            {
                var folder = Path.GetDirectoryName(FullPath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
        }
        catch
        {
            // Ignore shell errors.
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
