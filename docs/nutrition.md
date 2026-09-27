# Nutrition

The Nutrition module keeps private daily food logs in six fixed sections: breakfast, morning snack, lunch, afternoon snack, dinner, and evening snack. Entries always use grams. A food entry stores a snapshot of the source food name, food group, catalogue version, and published nutrient values, so historical days and saved recipes do not change after a catalogue reimport.

## Frida catalogue

The catalogue is imported locally from DTU National Food Institute's **Frida 5.5** XLSX release, file ID `60901603`, DOI [`10.11583/DTU.29500682.v8`](https://doi.org/10.11583/DTU.29500682.v8). The pinned source file is `Frida_5.5_Dataset.xlsx`; its published and required MD5 is `b553eed6805e3cd8856663421de0f1fe`. The dataset is licensed [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The UI credits DTU and links to the DOI.

The importer reads the complete `Food` and `Data_Normalised` worksheets. It uses the Danish food name, food group, and source `FoodID`; it does not fetch or scrape food pages during search. The search index normalizes case and Danish `æ`, `ø`, and `å`, supports multi-word terms, and returns ranked results in pages, with the UI allowing the next page to be loaded.

The importer maps only these published Frida parameters, all per 100 g of edible food:

- `356`: Energy (kcal), the calculated total-metabolisable-energy definition.
- `218`: Protein, total.
- `172`: Available carbohydrate (not carbohydrate by difference or labelling carbohydrate).
- `141`: Fat, total.
- `245`: Sum sugars.

Missing values remain unknown (`null`), rather than becoming zero. Calculations retain decimal precision and scale the source value by `grams / 100`; the Danish UI only rounds for display.

Each approved user can add private custom foods from the food-search dialog. A custom food requires a name and all five values (energy, protein, available carbohydrate, fat, and sugar) per 100 g. It is stored separately from the shared Frida catalogue, appears only in that user's searches, and is not removed by a Frida reimport. Daily entries and saved recipes keep nutrient snapshots.

## Goals and saved meals

Nutrition goals use the account's `UserSettings` as their single source of truth and support optional Danish decimal values. Saving goals returns to the daily Nutrition overview. The consolidation migration copies legacy `NutritionTargets` values into empty settings fields, preserves pre-existing settings values, and only then removes the duplicate table. Its rollback recreates the legacy table from the account settings.

Adding food, saving a meal, and adding a saved meal use stable client-generated request IDs. Repeating the same request is safe; reusing an ID with different data conflicts. Saved meals keep snapshots of their ingredient nutrients, and scaled servings are rejected if any ingredient would be outside the valid 0.1–10,000 g range. Users can delete their saved meals; deletion is owner-scoped and does not change any food entries already recorded in daily logs. Food-quantity updates include the previously displayed amount, so a stale tab receives a conflict instead of silently replacing a newer change. Nutrition goal values retain their full decimal precision when shown for editing.

## Local import

1. Download the pinned XLSX from `https://ndownloader.figshare.com/files/60901603` without adding it to Git.
2. Apply the application migration in the normal local setup.
3. Run the existing server startup project as the explicit import command:

   ```bash
   dotnet run --project src/FitnessApp.Server -- import-frida /full/path/Frida_5.5_Dataset.xlsx
   ```

The command validates the checksum and workbook structure before it opens a database transaction. A successful re-run replaces the complete shared catalogue atomically; a failed import leaves the existing catalogue unchanged. It reports the imported food count and records the version, DOI download URL, checksum, and import timestamp in SQLite. Do not commit the downloaded workbook or a generated SQLite database.

## Figma evidence

The published Figma Make prototype was inspected on 2026-09-26. It directly shows the daily date control, calorie ring, four macro cards, six expandable meal sections, entry adjustment controls, food-search flow, and gram-based per-100 g quantity flow. The target, recipe, loading, empty, validation, retry, deletion-confirmation, and ownership/error behavior are deliberate functional extensions using the shared centered modal and mobile-only navigation patterns.
