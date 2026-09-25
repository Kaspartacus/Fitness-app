using System.Globalization;

namespace FitnessApp.Client.Settings;

public static class SettingsDecimalInput
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");
    private const NumberStyles DecimalInputStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    public static bool TryParseOptional(string? value, out decimal? result)
    {
        result = null;
        var input = value?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
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
        if (!decimal.TryParse(input, DecimalInputStyle, culture, out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }
}
