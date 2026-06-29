using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MainbotCSharp.Services
{
    public static class MessageProtectionService
    {
        private static readonly SemaphoreSlim MessageProcessingSlots = new(120, 120);
        private static readonly ConcurrentDictionary<ulong, Queue<DateTime>> CommandWindows = new();
        private static readonly TimeSpan CommandWindowDuration = TimeSpan.FromSeconds(10);
        private const int MaxCommandsPerWindow = 6;

        public static Task<bool> TryEnterMessagePipelineAsync()
        {
            return MessageProcessingSlots.WaitAsync(0);
        }

        public static void ExitMessagePipeline()
        {
            MessageProcessingSlots.Release();
        }

        public static bool IsCommandRateLimited(ulong userId)
        {
            var now = DateTime.UtcNow;
            var queue = CommandWindows.GetOrAdd(userId, _ => new Queue<DateTime>());

            lock (queue)
            {
                while (queue.Count > 0 && (now - queue.Peek()) > CommandWindowDuration)
                {
                    queue.Dequeue();
                }

                if (queue.Count >= MaxCommandsPerWindow)
                {
                    return true;
                }

                queue.Enqueue(now);
                return false;
            }
        }
    }
}
