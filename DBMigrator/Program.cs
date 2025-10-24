using Past_Files.Migration;

string oldDatabasePathForMigration = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "filetracker.db");
string newDatabasePathForMigration = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "newfiletracker.db");

Console.WriteLine("Starting database migration...");
if (!File.Exists(oldDatabasePathForMigration))
{
    Console.WriteLine($"ERROR: Old database for migration not found: {oldDatabasePathForMigration}");
    Console.ReadLine();
    return;
}
DatabaseMigrator.Migrate(oldDatabasePathForMigration, newDatabasePathForMigration);
Console.WriteLine("Database migration completed.");
Console.ReadLine();
