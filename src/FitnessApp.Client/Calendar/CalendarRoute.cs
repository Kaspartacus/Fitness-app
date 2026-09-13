using System.Globalization;

namespace FitnessApp.Client.Calendar;

internal static class CalendarRoute
{
    public static string Path(DateOnly month, DateOnly selectedDate) =>
        $"/kalender?maaned={month:yyyy-MM}&dato={selectedDate:yyyy-MM-dd}";

    public static string AppendState(string path, DateOnly month, DateOnly selectedDate) =>
        $"{path}{(path.Contains('?') ? "&" : "?")}maaned={month:yyyy-MM}&dato={selectedDate:yyyy-MM-dd}";

    public static string ManualRunningPath(DateOnly month, DateOnly selectedDate) =>
        $"/loeb/registrer?maaned={month:yyyy-MM}&dato={selectedDate:yyyy-MM-dd}";

    public static string BackPath(string? monthValue, string? dateValue, string fallback) =>
        TryParseState(monthValue, dateValue, out var month, out var selectedDate)
            ? Path(month, selectedDate)
            : fallback;

    public static bool TryParseState(string? monthValue, string? dateValue, out DateOnly month, out DateOnly selectedDate)
    {
        month = default;
        selectedDate = default;
        if (!DateTime.TryParseExact(monthValue, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsedMonth) ||
            !DateOnly.TryParseExact(dateValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out selectedDate))
        {
            return false;
        }

        month = new DateOnly(parsedMonth.Year, parsedMonth.Month, 1);
        return selectedDate.Year == month.Year && selectedDate.Month == month.Month;
    }
}
