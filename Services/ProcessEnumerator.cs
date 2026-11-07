using System.Diagnostics;
using WTOPMBot.Models;

namespace WTOPMBot.Services;

public static class ProcessEnumerator
{
    public static IReadOnlyList<ProcessEntry> GetWindowedProcesses()
    {
        var result = new List<ProcessEntry>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == nint.Zero)
                    continue;

                var title = process.MainWindowTitle?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                result.Add(new ProcessEntry
                {
                    Id = process.Id,
                    Name = process.ProcessName,
                    Title = title,
                    MainWindowHandle = process.MainWindowHandle
                });
            }
            catch
            {
                // Access denied / exited mid-enumeration — skip.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id)
            .ToList();
    }
}
