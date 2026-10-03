using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;
using FitnessApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Infrastructure.Nutrition;

/// <summary>Imports the pinned DTU Frida release without making network requests.</summary>
public sealed class FridaImportService(FitnessDbContext db, TimeProvider timeProvider)
{
    public const string Version = "5.5";
    public const string FileUrl = "https://ndownloader.figshare.com/files/60901603";
    public const string ExpectedMd5 = "b553eed6805e3cd8856663421de0f1fe";

    public async Task<FridaImportResult> ImportAsync(string workbookPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(workbookPath)) return new(false, "Filen blev ikke fundet.", 0);
        using var file = File.OpenRead(workbookPath);
        var checksum = Convert.ToHexString(MD5.HashData(file)).ToLowerInvariant();
        if (!string.Equals(checksum, ExpectedMd5, StringComparison.OrdinalIgnoreCase))
            return new(false, $"Kontrolsummen matcher ikke Frida {Version}. Forventet MD5: {ExpectedMd5}.", 0);
        file.Position = 0;
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStrings = LoadSharedStrings(archive);
        var foods = ReadFoods(archive, sharedStrings);
        ApplyNutrients(archive, sharedStrings, foods);
        if (foods.Count < 1000 || foods.Values.All(food => food.EnergyKcalPer100g is null) || foods.Values.Any(food => string.IsNullOrWhiteSpace(food.DanishName)))
            return new(false, "Frida-filen indeholder ikke en gyldig komplet fødevarekatalogstruktur.", 0);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.FridaFoods.ExecuteDeleteAsync(cancellationToken);
        db.FridaFoods.AddRange(foods.Values);
        var release = await db.FridaCatalogueReleases.SingleOrDefaultAsync(item => item.Id == FridaCatalogueRelease.Key, cancellationToken);
        if (release is null) { release = new FridaCatalogueRelease { Id = FridaCatalogueRelease.Key }; db.FridaCatalogueReleases.Add(release); }
        release.Version = Version; release.SourceUrl = FileUrl; release.Checksum = checksum; release.FoodCount = foods.Count; release.ImportedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, $"Frida {Version} blev importeret.", foods.Count);
    }

    private static Dictionary<int, FridaFood> ReadFoods(ZipArchive archive, IReadOnlyList<string> strings)
    {
        var result = new Dictionary<int, FridaFood>();
        foreach (var row in Rows(archive, "xl/worksheets/sheet4.xml", strings).Skip(1))
        {
            if (!TryInt(row, "C", out var id) || !row.TryGetValue("A", out var name) || string.IsNullOrWhiteSpace(name)) continue;
            row.TryGetValue("L", out var group);
            result[id] = new FridaFood { FoodId = id, DanishName = name.Trim(), FoodGroup = group?.Trim() ?? "Ukendt gruppe", SearchName = NutritionService.Normalize(name) };
        }
        return result;
    }

    private static void ApplyNutrients(ZipArchive archive, IReadOnlyList<string> strings, IDictionary<int, FridaFood> foods)
    {
        var allValues = new Dictionary<int, Dictionary<int, decimal>>();
        foreach (var row in Rows(archive, "xl/worksheets/sheet3.xml", strings).Skip(1))
        {
            if (!TryInt(row, "A", out var foodId) || !TryInt(row, "D", out var parameterId) || !foods.TryGetValue(foodId, out var food) || !TryDecimal(row, "H", out var value)) continue;
            if (!allValues.TryGetValue(foodId, out var nutrientValues)) allValues[foodId] = nutrientValues = [];
            nutrientValues[parameterId] = value;
            switch (parameterId)
            {
                case 356: food.EnergyKcalPer100g = value; break; // Energy (kcal), calculated from energy-producing components.
                case 218: food.ProteinPer100g = value; break; // Total protein.
                case 172: food.CarbohydratePer100g = value; break; // Available carbohydrate, not carbohydrate by difference.
                case 141: food.FatPer100g = value; break; // Total fat.
                case 245: food.SugarPer100g = value; break; // Sum sugars.
            }
        }
        foreach (var (foodId, food) in foods) food.PublishedNutrientsJson = JsonSerializer.Serialize(allValues.GetValueOrDefault(foodId) ?? new Dictionary<int, decimal>());
    }

    private static IReadOnlyList<string> LoadSharedStrings(ZipArchive archive)
    {
        using var stream = RequiredEntry(archive, "xl/sharedStrings.xml").Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root!.Elements(ns + "si").Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value))).ToArray();
    }

    private static IEnumerable<Dictionary<string, string>> Rows(ZipArchive archive, string entryName, IReadOnlyList<string> strings)
    {
        using var stream = RequiredEntry(archive, entryName).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { IgnoreWhitespace = true });
        while (reader.ReadToFollowing("row", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"))
        {
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            using var subtree = reader.ReadSubtree();
            while (subtree.Read())
            {
                if (subtree.NodeType != XmlNodeType.Element || subtree.LocalName != "c") continue;
                var address = subtree.GetAttribute("r") ?? string.Empty;
                var column = new string(address.TakeWhile(char.IsLetter).ToArray());
                var shared = subtree.GetAttribute("t") == "s";
                using var cell = subtree.ReadSubtree(); string? raw = null;
                while (cell.Read()) if (cell.NodeType == XmlNodeType.Element && cell.LocalName == "v") raw = cell.ReadElementContentAsString();
                if (raw is not null) row[column] = shared && int.TryParse(raw, out var index) && index < strings.Count ? strings[index] : raw;
            }
            yield return row;
        }
    }

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, string name) => archive.GetEntry(name) ?? throw new InvalidDataException($"Mangler regnearksarket {name}.");
    private static bool TryInt(IReadOnlyDictionary<string, string> row, string column, out int value)
    {
        value = default;
        return row.TryGetValue(column, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
    private static bool TryDecimal(IReadOnlyDictionary<string, string> row, string column, out decimal value)
    {
        value = default;
        return row.TryGetValue(column, out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}

public sealed record FridaImportResult(bool Succeeded, string Message, int FoodCount);
