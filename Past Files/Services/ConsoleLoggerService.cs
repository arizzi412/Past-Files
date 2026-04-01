using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Past_Files.Services
{
    /// <summary>
    /// A background service that asynchronously writes log messages to the console.
    /// </summary>
    public class ConsoleLoggerService : IDisposable
    {
        private readonly Channel<string> _messageChannel;
        private readonly Task _loggerTask;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConsoleLoggerService"/> class.
        /// </summary>
        public ConsoleLoggerService()
        {
            var options = new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            };
            _messageChannel = Channel.CreateUnbounded<string>(options);

            // Start the background logging task (No CancellationToken needed)
            _loggerTask = Task.Run(ProcessQueueAsync);
        }

        /// <summary>
        /// Enqueue a message for asynchronous console logging.
        /// </summary>
        /// <param name="message">The log message.</param>
        public void Log(string message)
        {
            if (!_messageChannel.Writer.TryWrite(message))
            {
                // This will now only trigger if Log() is called AFTER Dispose()
                throw new InvalidOperationException("Unable to enqueue log message. Logger is shutting down.");
            }
        }

        /// <summary>
        /// Main loop that processes the queue and writes to the console one by one.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            var reader = _messageChannel.Reader;

            // WaitToReadAsync automatically returns false when the channel is marked as 
            // Complete() AND all remaining messages have been drained.
            while (await reader.WaitToReadAsync())
            {
                while (reader.TryRead(out var message))
                {
                    Console.WriteLine(message);
                }
            }
        }

        /// <summary>
        /// Disposes the logger service, ensuring all pending messages are written to the console.
        /// </summary>
        public void Dispose()
        {
            // 1. Tell the channel to close the gates. No more messages are allowed in.
            _messageChannel.Writer.TryComplete();

            // 2. Block until ProcessQueueAsync finishes draining the remaining messages
            _loggerTask.Wait();
        }
    }
}