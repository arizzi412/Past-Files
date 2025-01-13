using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Past_Files.Services
{
    /// <summary>
    /// A background service that batches log messages and writes them to the console.
    /// </summary>
    public class ConsoleLoggerService : IDisposable, IConcurrentLoggerService
    {
        private readonly BlockingCollection<string> _messageQueue = new(new ConcurrentQueue<string>());
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loggerTask;

        public ConsoleLoggerService()
        {
            // Start the background logging task
            _loggerTask = Task.Run(async () => await ProcessQueueAsync(_cts.Token));
        }

        /// <summary>
        /// Enqueue a message for asynchronous console logging.
        /// </summary>
        public void Enqueue(string message)
        {
            _messageQueue.Add(message);
        }

        /// <summary>
        /// Main loop that processes the queue and writes to the console in batches.
        /// </summary>
        private async Task ProcessQueueAsync(CancellationToken token)
        {
            var batch = new List<string>();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Block until at least one message is available or cancellation requested
                    var message = _messageQueue.Take(token);

                    // Start building a batch
                    batch.Add(message);

                    // Now grab as many more messages as are immediately available
                    while (_messageQueue.TryTake(out var nextMessage))
                    {
                        batch.Add(nextMessage);
                    }

                    // Print the collected messages
                    foreach (var m in batch)
                    {
                        Console.WriteLine(m);
                    }
                    batch.Clear();
                }
                catch (OperationCanceledException)
                {
                    // Normal during shutdown
                    break;
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _messageQueue.CompleteAdding();
            try
            {
                _loggerTask.Wait();
            }
            catch
            {
                // Ignored
            }
            _cts.Dispose();
        }
    }
}
