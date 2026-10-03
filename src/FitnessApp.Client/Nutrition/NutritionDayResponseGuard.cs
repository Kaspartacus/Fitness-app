namespace FitnessApp.Client.Nutrition;

public static class NutritionDayResponseGuard
{
    public static bool IsCurrent(DateOnly requestedDate, int requestSequence, DateOnly selectedDate, int currentSequence) =>
        requestedDate == selectedDate && requestSequence == currentSequence;
}
