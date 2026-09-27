using FitnessApp.Client.Settings;

namespace FitnessApp.IntegrationTests;

public sealed class SettingsDecimalInputTests
{
    [Theory]
    [InlineData("20.5")]
    [InlineData("20,5")]
    public void ParsesDotAndDanishCommaDecimals(string input)
    {
        var parsed = SettingsDecimalInput.TryParseOptional(input, out var value);

        Assert.True(parsed);
        Assert.Equal(20.5m, value!.Value);
    }

    [Theory]
    [InlineData("1.000,5")]
    [InlineData("1,000.5")]
    [InlineData("1 000")]
    public void RejectsGroupingAndMixedSeparatorInput(string input)
    {
        var parsed = SettingsDecimalInput.TryParseOptional(input, out var value);

        Assert.False(parsed);
        Assert.Null(value);
    }
}
