using FitnessApp.Client.Nutrition;

namespace FitnessApp.IntegrationTests;

public sealed class NutritionDayResponseGuardTests
{
    [Fact]
    public void AppliesOnlyWhenDateAndLoadSequenceStillMatch()
    {
        var requestDate = new DateOnly(2026, 9, 26);

        Assert.True(NutritionDayResponseGuard.IsCurrent(requestDate, 4, requestDate, 4));
        Assert.False(NutritionDayResponseGuard.IsCurrent(requestDate, 4, new DateOnly(2026, 9, 27), 5));
        Assert.False(NutritionDayResponseGuard.IsCurrent(requestDate, 4, requestDate, 5));
    }
}
