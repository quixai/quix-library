using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Quix.Sdk.Process;
using Quix.Sdk.Process.Models;

namespace Quix.Snowflake.Application.Streaming
{
    /// <summary>
    /// This component works by tracking the memory used by the application.
    /// If the memory used reaches the maximum prohibited amount then it blocks further processing of messages.
    /// Once the memory pressure eases, the processing resumes.
    /// </summary>
    public class MemoryLimiterComponent : StreamComponent, IDisposable
    {
        private readonly ILogger<MemoryLimiterComponent> logger;
        private readonly int absoluteMaxMegabyteLimit;
        private readonly SemaphoreSlim semaphoreSlim =new SemaphoreSlim(1);
        private readonly object entryLock = new object();
        private readonly Timer timer;
        private readonly int memoryThreshold;
        private bool disposed = false;
        private const int TimerInterval = 50; // 50 ms

        public MemoryLimiterComponent(ILogger<MemoryLimiterComponent> logger, int absoluteMaxMegabyteLimit, int memoryLimitPercentage)
        {
            this.logger = logger;
            this.absoluteMaxMegabyteLimit = absoluteMaxMegabyteLimit;
            // allow a maximum of x% of it to be taken, in case a new message bumps it up by big %. Also because only checking managed memory
            this.memoryThreshold = this.absoluteMaxMegabyteLimit * memoryLimitPercentage / 100;
            this.Input.Subscribe(this.OnStreamMessageArrived);
            this.timer = new Timer(this.TimerCallback, null, TimerInterval, Timeout.Infinite);
        }

        private void TimerCallback(object state)
        {
            if (this.IsWithinMemoryLimit())
            {
                this.semaphoreSlim.Release();
            }
            else
            {
                if (!this.disposed) this.timer.Change(TimerInterval, Timeout.Infinite);
            }
        }

        private void OnStreamMessageArrived(StreamPackage obj)
        {
            lock (this.entryLock)
            {
                this.SetMbBarrier();
                try
                {
                    this.Output.Send(obj);
                }
                catch(Exception ex)
                {
                    this.logger.LogError(ex, "BAD");
                }
            }
        }

        private bool IsWithinMemoryLimit()
        {
            if (this.disposed) return true; //disposing, let it flow
            var mbTaken = GC.GetTotalMemory(false) / 1024 / 1024;
            this.logger.LogTrace("Memory {0}/{1} MB", mbTaken, this.memoryThreshold);
            return mbTaken < this.memoryThreshold;
        }
        private void SetMbBarrier()
        {
            if (this.IsWithinMemoryLimit())
            {
                this.semaphoreSlim.Release();
            }
            else
            {
                this.logger.LogDebug("Memory limit reached, blocking packages");
                this.timer.Change(TimerInterval, Timeout.Infinite);
                this.semaphoreSlim.Wait();
            }
        }

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;
            this.timer.Dispose();
            this.semaphoreSlim.Release(); // let it continue ?
        }
    }
}