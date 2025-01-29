using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Past_Files.Services
{
    /// <summary>
    /// A background service that batches log messages and writes them to the console.
    /// </summary>
    public class ConsoleLoggerService : IConcurrentLoggerService
    {
        private readonly Channel<string> _messageChannel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loggerTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsoleLoggerService"/> class.
        /// </summary>
        public ConsoleLoggerService()
        {
            // Create an unbounded channel with single reader for better performance
            var options = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            };
            _messageChannel = Channel.CreateUnbounded<string>(options);

            // Start the background logging task
            _loggerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        /// <summary>
        /// Enqueue a message for asynchronous console logging.
        /// </summary>
        /// <param name="message">The log message.</param>
        public void Enqueue(string message)
        {
            if (!_messageChannel.Writer.TryWrite(message))
            {
                // Handle cases where the channel is full or completed
                throw new InvalidOperationException("Unable to enqueue log message.");
            }
        }

        /// <summary>
        /// Main loop that processes the queue and writes to the console in batches.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        private async Task ProcessQueueAsync(CancellationToken token)
        {
            var reader = _messageChannel.Reader;

            try
            {
                while (await reader.WaitToReadAsync(token))
                {
                    while (reader.TryRead(out var message))
                    {
                        Console.WriteLine(message);
                    }

                    // Optional: Implement time-based batching
                    // You can add a delay or implement a timer to flush the batch periodically
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            finally
            {
                // Process any remaining messages
                while (reader.TryRead(out var message))
                {
                    Console.WriteLine(message);
                }
            }
        }

        /// <summary>
        /// Disposes the logger service, ensuring all messages are processed.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _messageChannel.Writer.Complete();

            try
            {
                _loggerTask.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
            {
                // Ignore cancellation exceptions
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
