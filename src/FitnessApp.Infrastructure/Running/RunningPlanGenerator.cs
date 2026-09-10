using FitnessApp.Application.Running;
using FitnessApp.Domain.Running;

namespace FitnessApp.Infrastructure.Running;

internal sealed class RunningPlanGenerator
{
    private static readonly DayOfWeek[] WeekdayOrder =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    public RunningPlan? Generate(string userId, RunningPlanInput input, DateOnly today, DateTime nowUtc)
    {
        var dates = DatesFor(input.SelectedDays!, today, input.TargetDate);
        if (dates.Count == 0)
        {
            return null;
        }

        var lastSessionDate = dates[^1];
        var completedWeeks = (lastSessionDate.DayNumber - today.DayNumber) / 7;
        if (input.TargetDistanceKm > CapacityAfterWeeks(input.ThirtyMinuteDistanceKm, completedWeeks))
        {
            return null;
        }

        var plan = new RunningPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Version = Guid.NewGuid(),
            IsActive = true,
            CreatedAtUtc = nowUtc,
            Level = input.Level,
            ThirtyMinuteDistanceKm = input.ThirtyMinuteDistanceKm,
            TargetDistanceKm = input.TargetDistanceKm,
            TargetDate = input.TargetDate,
            WeeklyFrequency = input.WeeklyFrequency,
            SelectedDays = input.SelectedDays!
                .OrderBy(day => Array.IndexOf(WeekdayOrder, day))
                .Select(day => new RunningPlanDay { Id = Guid.NewGuid(), DayOfWeek = day })
                .ToList()
        };
        foreach (var selectedDay in plan.SelectedDays)
        {
            selectedDay.PlanId = plan.Id;
        }

        var sessionsByWeek = dates.GroupBy(date => (date.DayNumber - today.DayNumber) / 7).ToArray();
        var position = 0;
        foreach (var week in sessionsByWeek)
        {
            var weekDates = week.ToArray();
            var capacity = decimal.Min(input.TargetDistanceKm,
                CapacityAfterWeeks(input.ThirtyMinuteDistanceKm, week.Key));
            for (var index = 0; index < weekDates.Length; index++)
            {
                var isLast = position == dates.Count - 1;
                var kind = KindFor(input.Level, index, weekDates.Length, capacity, isLast);
                var distance = DistanceFor(kind, capacity, input.TargetDistanceKm, isLast);
                var (paceMin, paceMax) = PaceFor(kind, input.Level, input.ThirtyMinuteDistanceKm);
                plan.Sessions.Add(new RunningSession
                {
                    Id = Guid.NewGuid(),
                    PlanId = plan.Id,
                    Date = weekDates[index],
                    Position = position++,
                    Kind = kind,
                    PlannedDistanceKm = distance,
                    PaceMinSecondsPerKm = paceMin,
                    PaceMaxSecondsPerKm = paceMax,
                    Structure = StructureFor(kind)
                });
            }
        }

        return plan;
    }

    private static List<DateOnly> DatesFor(IReadOnlyList<DayOfWeek> selectedDays, DateOnly today, DateOnly targetDate)
    {
        var dates = new List<DateOnly>();
        for (var date = today; date <= targetDate; date = date.AddDays(1))
        {
            if (selectedDays.Contains(date.DayOfWeek))
            {
                dates.Add(date);
            }
        }

        return dates;
    }

    private static decimal CapacityAfterWeeks(decimal startingDistance, int completedWeeks)
    {
        var capacity = startingDistance;
        for (var week = 0; week < completedWeeks; week++)
        {
            // Rounding toward zero keeps every stored weekly increase at or below ten percent.
            capacity = decimal.Round(capacity * 1.10m, 2, MidpointRounding.ToZero);
        }

        return capacity;
    }

    private static RunningSessionKind KindFor(RunningLevel level, int index, int sessionsThisWeek,
        decimal weeklyCapacity, bool isLast)
    {
        if (isLast || sessionsThisWeek == 1 || index == sessionsThisWeek - 1)
        {
            return RunningSessionKind.LongRun;
        }

        if (level is RunningLevel.NewToRunning)
        {
            return RunningSessionKind.Easy;
        }

        if (sessionsThisWeek >= 4 && index == sessionsThisWeek - 2 &&
            level is RunningLevel.Trained or RunningLevel.Experienced && weeklyCapacity >= 5.5m)
        {
            return RunningSessionKind.Intervals;
        }

        if (sessionsThisWeek >= 3 && index == 1)
        {
            return RunningSessionKind.Tempo;
        }

        return RunningSessionKind.Easy;
    }

    private static decimal DistanceFor(RunningSessionKind kind, decimal weeklyCapacity, decimal targetDistance,
        bool isLast)
    {
        if (isLast)
        {
            return targetDistance;
        }

        var multiplier = kind switch
        {
            RunningSessionKind.Easy => 0.60m,
            RunningSessionKind.Tempo => 0.70m,
            RunningSessionKind.Intervals => 0.65m,
            _ => 1.00m
        };
        var distance = decimal.Min(targetDistance, weeklyCapacity * multiplier);
        return decimal.Max(0.5m, decimal.Round(distance, 2, MidpointRounding.ToZero));
    }

    private static (int Minimum, int Maximum) PaceFor(RunningSessionKind kind, RunningLevel level,
        decimal thirtyMinuteDistanceKm)
    {
        var basePace = (int)decimal.Round(1_800m / thirtyMinuteDistanceKm, 0, MidpointRounding.AwayFromZero);
        var levelAdjustment = level switch
        {
            RunningLevel.NewToRunning => 30,
            RunningLevel.Beginner => 15,
            RunningLevel.Recreational => 5,
            RunningLevel.Trained => 0,
            _ => -5
        };
        var (minimumOffset, maximumOffset) = kind switch
        {
            RunningSessionKind.Easy => (20, 60),
            RunningSessionKind.Tempo => (-20, 0),
            RunningSessionKind.Intervals => (-40, -20),
            _ => (25, 70)
        };
        var minimum = ClampPace(basePace + minimumOffset + levelAdjustment);
        var maximum = ClampPace(basePace + maximumOffset + levelAdjustment);
        return minimum <= maximum ? (minimum, maximum) : (maximum, minimum);
    }

    private static int ClampPace(int value) => Math.Clamp(value, RunningRules.MinPaceSecondsPerKm,
        RunningRules.MaxPaceSecondsPerKm);

    private static string StructureFor(RunningSessionKind kind) => kind switch
    {
        RunningSessionKind.Tempo =>
            "Opvarmning: Løb roligt og byg gradvist op.|Tempoløb: Hold det planlagte tempo kontrolleret hårdt.|Nedkøling: Afslut roligt.",
        RunningSessionKind.Intervals =>
            "Opvarmning: Løb roligt og byg gradvist op.|Intervaller: Løb korte, hurtigere intervaller med rolige pauser.|Nedkøling: Afslut roligt.",
        RunningSessionKind.LongRun => "Lang tur: Løb roligt i et sammenhængende tempo.",
        _ => "Rolig tur: Løb i et sammenhængende tempo, hvor du kan føre en samtale."
    };
}
