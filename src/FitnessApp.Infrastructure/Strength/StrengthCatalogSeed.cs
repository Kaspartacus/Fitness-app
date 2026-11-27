using FitnessApp.Domain.Strength;

namespace FitnessApp.Infrastructure.Strength;

internal static class StrengthCatalogSeed
{
    public static CatalogExercise[] Create() =>
    [
        new("squat", "Squat", "legs", "knæbøjning|knæbøjninger|squats",
            "En person sænker sig i en squat med bøjede knæ og ret ryg", false),
        new("deadlift", "Deadlift", "full-body", "dødløft|doedloeft|dødløft med stang|dødløft fra gulv",
            "En stående person holder en vægtstang med begge hænder foran lårene", false),
        new("bench-press", "Bench Press", "chest", "bænkpres|bænkpress|brystpres",
            "En person ligger på en bænk med fødderne i gulvet og holder en vægtstang over brystet", false),
        new("push-up", "Push-Up", "chest", "armbøjning|armbøjninger|push up|pushups",
            "En person holder kroppen løftet med strakte arme og støtte på tæerne", false),
        new("dumbbell-row", "Dumbbell Row", "back", "håndvægtsroning|enarmsroning|roning med håndvægt",
            "En person støtter på en bænk og trækker en håndvægt op ved siden af kroppen", false),
        new("pull-up", "Pull-Up", "back", "kropshævning|kropshævninger|pull up|pullups",
            "En person trækker sig op i en stang med begge hænder", false),
        new("biceps-curl", "Biceps Curl", "arms", "bicepsbøjning|bicep curl|armcurl",
            "En person bøjer albuerne og løfter håndvægtene mod skuldrene", false),
        new("triceps-extension", "Triceps Extension", "arms", "tricepsstræk|tricepspres|triceps extension med håndvægt",
            "En person strækker albuerne og løfter en håndvægt over hovedet", false),
        new("lunge", "Lunge", "legs", "udfald|udfaldsskridt|lunges",
            "En person har én fod foran den anden og bøjer begge knæ", false),
        new("calf-raise", "Calf Raise", "legs", "lægpres|tåhævning|taahaevning|lægøvelse",
            "En stående person løfter hælene og støtter på forfødderne", false),
        new("plank", "Plank", "core", "planke|underarmsplanke|front plank",
            "En person holder kroppen lige med støtte på underarme og tæer", true),
        new("side-plank", "Side Plank", "core", "sideplanke|side planke|sidebro",
            "En person holder kroppen lige på siden med støtte på underarm og fødder", true),
    ];
}
