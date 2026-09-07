using Microsoft.Data.Sqlite;

namespace FitnessApp.Infrastructure.Persistence;

public static class SqliteConnectionString
{
    public static string Resolve(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource is ":memory:" || Path.IsPathRooted(builder.DataSource))
        {
            return builder.ToString();
        }

        var databasePath = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (databaseDirectory is not null)
        {
            Directory.CreateDirectory(databaseDirectory);
        }

        builder.DataSource = databasePath;
        return builder.ToString();
    }
}
