using System.Diagnostics;

namespace FileTransmitter;

internal sealed class ProgressBar
{
    private const int BarWidth = 30;
    private static readonly char[] Spinner = ['|', '/', '-', '\\'];
    private static readonly long RedrawIntervalTicks = Stopwatch.Frequency / 12;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private long _lastRedrawTicks = -RedrawIntervalTicks;
    private int _spinnerIndex;
    private bool _completed;

    public void Report(long current, long total)
    {
        if (_completed)
            return;

        bool isFinal = current >= total;
        long nowTicks = _stopwatch.ElapsedTicks;

        if (!isFinal && nowTicks - _lastRedrawTicks < RedrawIntervalTicks)
            return;

        _lastRedrawTicks = nowTicks;

        double fraction = total <= 0 ? 1 : Math.Clamp((double)current / total, 0, 1);
        int filled = (int)(fraction * BarWidth);

        string bar = new string('█', filled) + new string('░', BarWidth - filled);
        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        double speed = elapsedSeconds > 0 ? current / elapsedSeconds : 0;

        char spinnerChar = isFinal ? '*' : Spinner[_spinnerIndex++ % Spinner.Length];

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"\r    {spinnerChar} [{bar}] {fraction * 100,5:0.0}%  {FormatBytes(current)}/{FormatBytes(total)}  {FormatBytes((long)speed)}/s   ");
        Console.ResetColor();

        if (isFinal)
            _completed = true;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{value:0} {units[unitIndex]}" : $"{value:0.00} {units[unitIndex]}";
    }
}
