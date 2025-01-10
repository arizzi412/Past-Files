using Past_Files.Data;
using System.Xml.Linq;

namespace Past_Files.Services
{
    public class DBImportInfo
    {
        public FileTrackerContext ImportedDbContext { get; set; }
        public DataStore dataStore;

        public static DBImportInfo CreateDBImportInfo(string dbName, ConsoleLoggerService consoleLoggerService) => new(dbName, consoleLoggerService);
        private DBImportInfo(string dbName, ConsoleLoggerService consoleLoggerService)
        {
            consoleLoggerService.Enqueue("Reading data from import database");
            ImportedDbContext = new FileTrackerContext(dbName);
            dataStore = new DataStore(consoleLoggerService);
            dataStore.LoadRecords(ImportedDbContext);
        }
    }
}