using Past_Files.Data;
using System.Xml.Linq;

namespace Past_Files.Services
{
    public class ImportDbInfo
    {
        public FileDbContext ImportedDbContext { get; set; }
        public DbCache dataStore;

        public static ImportDbInfo CreateDBImportInfo(string dbName, ConsoleLoggerService consoleLoggerService) => new(dbName, consoleLoggerService);
        private ImportDbInfo(string dbName, ConsoleLoggerService consoleLoggerService)
        {
            consoleLoggerService.Enqueue("Reading data from import database");
            ImportedDbContext = new FileDbContext(dbName);
            dataStore =  DbCache.CreateCache(ImportedDbContext, consoleLoggerService);
        }
    }
}