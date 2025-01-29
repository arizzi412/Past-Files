namespace Past_Files.Services
{
    public interface IConcurrentLoggerService : IDisposable
    {
        void Enqueue(string message);
    }
}