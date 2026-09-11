using System.Globalization;

namespace FitnessApp.Client.Running;

internal static class RunningPresentation
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");
    private static readonly TimeZoneInfo CopenhagenTimeZone = FindCopenhagenTimeZone();

    public static DateOnly CopenhagenToday
    {
        get
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, CopenhagenTimeZone).DateTime;
            return DateOnly.FromDateTime(now);
        }
    }

    public static string Distance(decimal? distance) => distance is { } value
        ? $"{value.ToString("0.#", DanishCulture)} km"
        : "Ikke registreret";

    public static string Duration(int? seconds) => seconds is { } value && value > 0
        ? TimeSpan.FromSeconds(value).ToString(value >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
        : "Ikke registreret";

    public static string Pace(int? secondsPerKilometre) => secondsPerKilometre is { } value && value > 0
        ? $"{PaceTime(value)} min/km"
        : "Ikke registreret";

    public static string PaceRange(int minimumSecondsPerKilometre, int maximumSecondsPerKilometre) =>
        $"{PaceTime(minimumSecondsPerKilometre)}–{PaceTime(maximumSecondsPerKilometre)} min/km";

    public static string Date(DateOnly value) => value.ToString("dddd d. MMMM", DanishCulture);

    public static string ShortDate(DateOnly value) => value.ToString("ddd d. MMM", DanishCulture);

    public static string Month(DateOnly value) => value.ToString("MMMM yyyy", DanishCulture);

    public static string Weekday(DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => "Mandag",
        DayOfWeek.Tuesday => "Tirsdag",
        DayOfWeek.Wednesday => "Onsdag",
        DayOfWeek.Thursday => "Torsdag",
        DayOfWeek.Friday => "Fredag",
        DayOfWeek.Saturday => "Lørdag",
        _ => "Søndag"
    };

    public static DateOnly Monday(DateOnly value)
    {
        var offset = ((int)value.DayOfWeek + 6) % 7;
        return value.AddDays(-offset);
    }

    public static bool TryParseDistance(string? value, out decimal distance)
    {
        distance = 0;
        var input = value?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var commaIndex = input.IndexOf(',');
        var dotIndex = input.IndexOf('.');
        if ((commaIndex >= 0 && dotIndex >= 0) ||
            (commaIndex >= 0 && commaIndex != input.LastIndexOf(',')) ||
            (dotIndex >= 0 && dotIndex != input.LastIndexOf('.')))
        {
            return false;
        }

        var separatorIndex = Math.Max(commaIndex, dotIndex);
        if (separatorIndex == 0 || separatorIndex == input.Length - 1 ||
            (separatorIndex > 0 && input[separatorIndex - 1] is '+' or '-'))
        {
            return false;
        }

        var culture = commaIndex >= 0 ? DanishCulture : CultureInfo.InvariantCulture;
        const NumberStyles decimalInput = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(input, decimalInput, culture, out distance);
    }

    private static string PaceTime(int seconds) => TimeSpan.FromSeconds(seconds).ToString(
        seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    private static TimeZoneInfo FindCopenhagenTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }
}
