namespace Past_Files.Services
{
    public interface IConcurrentLoggerService
    {
        void Dispose();
        void Enqueue(string message);
    }
}