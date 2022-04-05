using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quix.Snowflake.Application.Helpers;
using Quix.Snowflake.Domain.Models;
using Quix.Snowflake.Domain.TimeSeries.Models;
using Quix.Snowflake.Domain.TimeSeries.Repositories;
using Quix.Sdk.Process.Models;

namespace Quix.Snowflake.Application.TimeSeries
{
    public interface ITimeSeriesBufferedPersistingService
    {
        Task Buffer(string sourceStreamId, EventDataRaw[] eventDataRaws);
        Task Buffer(string sourceStreamId, ParameterDataRaw parameterDataRaw);
        
        void ClearBuffer(string[] sourceStreamIds);
        Task Save();
    }
    
    public class TimeSeriesBufferedPersistingService : ITimeSeriesBufferedPersistingService
    {
        private readonly ILogger<TimeSeriesBufferedPersistingService> logger;
        private readonly ITimeSeriesWriteRepository timeSeriesWriteRepository;
        private readonly string topicId;
        private ConcurrentQueue<StreamEventDataForWrite> eventQueue;
        private ConcurrentQueue<StreamParameterDataForWrite> paramQueue; 
        private readonly ConcurrentQueue<EventCommitItem> eventCommitQueue = new ConcurrentQueue<EventCommitItem>();
        private readonly ConcurrentQueue<ParameterCommitItem> parameterCommitQueue = new ConcurrentQueue<ParameterCommitItem>();
        private readonly object paramQueueLock = new object();
        private readonly object eventQueueLock = new object();
        private Action forceWakeParameterWorkerMethod = () => { };
        private Action forceWakeEventWorkerMethod = () => { };
        private readonly Task paramWorkerTask;
        private readonly Task eventWorkerTask;
        private readonly int maxBatchSize; // maximum
        private int queueCount = 0;

        public TimeSeriesBufferedPersistingService(ILogger<TimeSeriesBufferedPersistingService> logger,
            ITimeSeriesWriteRepository timeSeriesWriteRepository,
            TopicId topicId,
            int maxBatchSize)
        {
            this.logger = logger;
            this.timeSeriesWriteRepository = timeSeriesWriteRepository;
            this.topicId = topicId.Value;
            this.eventQueue = new ConcurrentQueue<StreamEventDataForWrite>();
            this.paramQueue = new ConcurrentQueue<StreamParameterDataForWrite>();
            this.maxBatchSize = maxBatchSize;
            if (this.maxBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxBatchSize));

            this.paramWorkerTask = Task.Run(this.ParamWorkerMethod);
            this.eventWorkerTask = Task.Run(this.EventWorkerMethod);
        }

        private async Task ParamWorkerMethod()
        {
            const int sleepDurationWhenNoWork = 2000;
            var mre = new SemaphoreSlim(0);
            var timer = new System.Timers.Timer();
            timer.AutoReset = false;
            timer.Elapsed += (s, a) =>
            {
                mre.Release();
            };

            timer.Disposed += (sender, args) =>
            {
                Console.WriteLine("Timer disposed!");
            };
            
            this.forceWakeParameterWorkerMethod = () =>
            {
                if (timer.Enabled)
                {
                    this.logger.LogTrace("Waking up parameter timer due to force awake.");
                    timer.Stop();
                    mre.Release();
                }
            };
            
            var batch = new StreamParameterDataForWrite[this.maxBatchSize]; // reuse array and avoid always resizing a list to fit new elements
            while(true)
            {
                var isCommitQueue = false;
                try
                {
                    var queueToConsume = this.paramQueue;
                    if (this.parameterCommitQueue.Count > 0)
                    {
                        this.parameterCommitQueue.TryPeek(out var result);
                        
                        if (result != null)
                        {
                            if (result.ParameterQueue.IsEmpty)
                            {
                                result.MarkDone();
                                this.parameterCommitQueue.TryDequeue(out var _);
                            }
                            else
                            {
                                queueToConsume = result.ParameterQueue;
                                isCommitQueue = true;
                            }
                        }
                    }
                    
                    var batchSize = 0;
                    var batchIndex = 0;
                    var earlyBreak = false;
                    while (queueToConsume.TryDequeue(out var streamBatch))
                    {
                        foreach (var row in streamBatch.Rows)
                        {
                            batchSize += row.NumericValueCount + row.StringValueCount;   
                        }

                        Interlocked.Add(ref this.queueCount, -streamBatch.Rows.Length);
                        batch[batchIndex] = streamBatch;
                        batchIndex++;

                        if (batchSize >= this.maxBatchSize || batchIndex >= this.maxBatchSize)
                        {
                            earlyBreak = true;
                            break;
                        }
                    }

                    if (batchSize > 0)
                    {
                        var sw = Stopwatch.StartNew();
                        await this.SendBatch(batch, batchIndex, batchSize);
                        sw.Stop();

                        if (isCommitQueue)  this.logger.LogDebug("Saved {0} parameter values for topic {1} as part of a commit in {2:g}", batchSize, this.topicId, sw.Elapsed);
                        else this.logger.LogDebug("Saved {0} parameter values for topic {1} in {2:g}.", batchSize, this.topicId, sw.Elapsed);
                    }
                    
                    if (earlyBreak) continue; // There is work to do, re-do loop
                    
                    if (this.parameterCommitQueue.Count > 0)
                    {
                        if (!isCommitQueue) this.logger.LogTrace("Avoiding sleep due to having to deal with parameter commit."); // already dealing with it
                        continue;
                    }
                    
                    var sleepSw = Stopwatch.StartNew();
                    this.logger.LogTrace("Parameter value persistence to sleep for {0}ms", sleepDurationWhenNoWork);
                    
                    timer.Interval = sleepDurationWhenNoWork;
                    timer.Start();
                    await mre.WaitAsync();
                    sleepSw.Stop();
                    this.logger.LogTrace("Parameter value persistence slept for {0}ms", sleepSw.ElapsedMilliseconds);
                }
                catch (System.Exception ex)
                {
                    this.logger.LogError(ex, "Data worker thread failed.");
                }
            }
        }
        
        private async Task SendBatch(StreamParameterDataForWrite[] batch, int batchIndex, int batchSize)
        {
            var batchSegment = new ArraySegment<StreamParameterDataForWrite>(batch, 0, batchIndex);
            var streamGrouped = batchSegment.GroupBy(y => y.StreamId)
                .Select(y => new KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>(y.Key, y.SelectMany(x => x.Rows)));
            await this.timeSeriesWriteRepository.WriteTelemetryData(this.topicId, streamGrouped);

            for (int i = 0; i < batchIndex; i++)
            {
                batch[i] = null; // free it up, in case near future batches don't take as much
            }
        }
        
        private async Task EventWorkerMethod()
        {
            const int sleepDurationWhenNoWork = 2000;
            
            var mre = new SemaphoreSlim(0);
            var timer = new System.Timers.Timer();
            timer.AutoReset = false;
            timer.Elapsed += (s, a) =>
            {
                mre.Release();
            };
            
            this.forceWakeEventWorkerMethod = () =>
            {
                if (timer.Enabled)
                {
                    this.logger.LogTrace("Waking up event timer due to force awake.");
                    timer.Stop();
                    mre.Release();
                }
            };
            
            var batch = new StreamEventDataForWrite[this.maxBatchSize]; // reuse array and avoid always resizing a list to fit new elements
            while (true)
            {
                var isCommitQueue = false;
                try
                {
                    var queueToConsume = this.eventQueue;
                    if (this.eventCommitQueue.Count > 0)
                    {
                        this.eventCommitQueue.TryPeek(out var result);
                        
                        if (result != null)
                        {
                            if (result.EventQueue.IsEmpty)
                            {
                                result.MarkDone();
                                if (result.IsDone) this.eventCommitQueue.TryDequeue(out var _);
                            }
                            else
                            {
                                queueToConsume = result.EventQueue;
                                isCommitQueue = true;
                            }
                        }
                    }
                    var batchSize = 0;
                    var batchIndex = 0;
                    var earlyBreak = false;

                    while (queueToConsume.TryDequeue(out var streamEvents))
                    {
                        batch[batchIndex] = streamEvents;
                        batchSize += streamEvents.Rows.Length;
                        batchIndex++;
                        if (batchSize >= this.maxBatchSize || batchIndex >= this.maxBatchSize)
                        {
                            earlyBreak = true;
                            break;
                        }
                    }
                    
                    if (batchIndex > 0)
                    {
                        IEnumerable<EventDataRow> Convert(IGrouping<long, EventDataRaw> events)
                        {
                            var tagGrouped = events.GroupBy(x => x.Tags.DictionaryContentHash());
                            foreach (var grouping in tagGrouped)
                            {
                                var idGroupedEvents = grouping.GroupBy(g => g.Id).ToDictionary(x => x.Key, x => x.Last().Value); // if same event is present with same tags more than once, take last value
                                foreach (var idGroupedEvent in idGroupedEvents)
                                {
                                    yield return new EventDataRow
                                    {
                                        Timestamp = events.Key,
                                        TagValues = grouping.First().Tags, // they're identical
                                        Value = idGroupedEvent.Value,
                                        EventId = idGroupedEvent.Key
                                    };   
                                }
                            }
                        }
                        
                        var sw = Stopwatch.StartNew();
                        var batchSegment = new ArraySegment<StreamEventDataForWrite>(batch, 0, batchIndex);
                        var rows = batchSegment.GroupBy(x => x.StreamId)
                            .SelectMany(streamIdToEvents =>
                                streamIdToEvents.Select(streamEvents =>
                                    streamEvents.Rows.GroupBy(@event => @event.Timestamp).SelectMany(Convert)).Select(converted =>
                                    new KeyValuePair<string, IEnumerable<EventDataRow>>(streamIdToEvents.Key, converted)));

                        await this.timeSeriesWriteRepository.WriteTelemetryEvent(this.topicId, rows);
                        for (int i = 0; i < batchSize; i++)
                        {
                            batch[i] = null; // free it up, in case near future batches don't take as much
                        }
                        sw.Stop();

                        if (isCommitQueue)  this.logger.LogDebug("Saved {0} event values for topic {1} as part of a commit in {2:g}", batchSize, this.topicId, sw.Elapsed);
                        else this.logger.LogDebug("Saved {0} event values for topic {1} in {2:g}.", batchSize, this.topicId, sw.Elapsed);
                    }

                    if (earlyBreak) continue; // There is work to do, re-do loop
                    
                    if (this.eventCommitQueue.Count > 0)
                    {
                        if (!isCommitQueue) this.logger.LogTrace("Avoiding sleep due to having to deal with event commit."); // already dealing with it
                        continue;
                    }
                    
                    var sleepSw = Stopwatch.StartNew();
                    this.logger.LogTrace("Event value persistence to sleep for {0}ms", sleepDurationWhenNoWork);
                    timer.Interval = sleepDurationWhenNoWork;
                    timer.Start();
                    await mre.WaitAsync();
                    sleepSw.Stop();
                    this.logger.LogTrace("Event value persistence slept for {0}ms", sleepSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Event worker thread failed.");
                }
            }
        }

        public Task Buffer(string sourceStreamId, EventDataRaw[] eventDataRaws)
        {
            this.logger.LogTrace("Received {0} event data for {1}", eventDataRaws.Length, sourceStreamId);
            IEnumerable<EventDataRaw> PrepareEvents()
            {
                foreach (var eventDataRaw in eventDataRaws)
                {
                    if (eventDataRaw.Tags?.ContainsKey("eventId") == true) // nullable, don't look at me like that
                    {
                        // remove conflicting key that we use internally
                        eventDataRaw.Tags["eventId1"] = eventDataRaw.Tags["eventId"];
                        eventDataRaw.Tags.Remove("eventId");
                    }

                    eventDataRaw.Tags = eventDataRaw.Tags?
                        .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
                        .ToDictionary(x => x.Key, x => x.Value);

                    yield return eventDataRaw;
                }
            }

            var eventRows = PrepareEvents().ToArray();

            Interlocked.Add(ref this.queueCount, eventRows.Length);
            lock (this.eventQueueLock)
            {
                // Doing a lock here, so we get a hold of the queue only if nothing else is messing with it in terms of reference change. (Reading is irrelevant)
                this.eventQueue.Enqueue(new StreamEventDataForWrite()
                {
                    StreamId = sourceStreamId,
                    Rows = eventRows
                });
            }

            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, ParameterDataRaw parameterDataRaw)
        {
            this.logger.LogTrace("Received {0} parameter data for {1}", parameterDataRaw.Timestamps.Length, sourceStreamId);
            var totalQueued = 0;
            var batch = new ParameterDataRowForWrite[parameterDataRaw.Timestamps.Length];
            // Create a batch for values that are readily available
            if (parameterDataRaw.NumericValues != null && parameterDataRaw.NumericValues.Count > 0 ||
                parameterDataRaw.StringValues != null && parameterDataRaw.StringValues.Count > 0)
            {
                Parallel.For(0, parameterDataRaw.Timestamps.Length, new ParallelOptions {MaxDegreeOfParallelism = 4},
                    index => { batch[index] = this.CreateRowFromParameterData(parameterDataRaw, index); });

                IEnumerable<ParameterDataRowForWrite> FilterRows()
                {
                    for (int index = 0; index < batch.Length; index++)
                    {
                        var model = batch[index];
                        if (model.NumericValueCount == 0 && model.StringValueCount == 0)
                        {
                            this.logger.LogTrace("Empty non-binary batch skipped in OnParameterDataReceived at ts {0}", model.Epoch + model.Timestamp);
                            continue;
                        }

                        yield return model;
                    }
                }

                var rows = FilterRows().ToArray();
                if (rows.Length > 0)
                {
                    Interlocked.Add(ref this.queueCount, rows.Length);
                    totalQueued+= rows.Length;
                    lock (this.paramQueueLock)
                    {
                        // Doing a lock here, so we get a hold of the queue only if nothing else is messing with it in terms of reference change. (Reading is irrelevant)
                        this.paramQueue.Enqueue(new StreamParameterDataForWrite()
                        {
                            StreamId = sourceStreamId,
                            Rows = rows
                        });
                    }
                }
            }

            // Create a batch for values that might take some time to  be available
            if (parameterDataRaw.BinaryValues != null && parameterDataRaw.BinaryValues.Count > 0)
            {
                if (this.logger.IsEnabled(LogLevel.Warning))
                {
                    var asArray = parameterDataRaw.BinaryValues.Keys.ToArray();
                    this.logger.LogWarning("Binary values are not supported by this persisting service. Discarding parameters: {@asArray}", asArray);
                }
            }

            this.logger.LogTrace("Received {0} timestamps for {1}, queued {2} rows", parameterDataRaw.Timestamps.Length, sourceStreamId, totalQueued);

            return Task.CompletedTask;
        }
        
        private ParameterDataRowForWrite CreateRowFromParameterData(ParameterDataRaw source, int index)
        {
            try
            {
                var row = new ParameterDataRowForWrite();

                row.Epoch = source.Epoch;
                row.Timestamp = source.Timestamps[index];

                if (source.NumericValues != null)
                {
                    row.NumericParameters =
                        new string[source.NumericValues.Count]; // worst scenario is partially wasted array
                    row.NumericValues =
                        new double[source.NumericValues.Count]; // worst scenario is partially wasted array
                    var keys = source.NumericValues.Keys.ToArray();
                    var values = source.NumericValues.Values.ToArray();
                    for (var j = 0; j < keys.Length; j++)
                    {
                        var val = values[j][index];
                        if (ReferenceEquals(val, null)) continue;
                        row.NumericParameters[row.NumericValueCount] = keys[j];
                        row.NumericValues[row.NumericValueCount] = val.Value;
                        row.NumericValueCount++;
                    }
                }

                if (source.StringValues != null)
                {
                    row.StringParameters =
                        new string[source.StringValues.Count]; // worst scenario is partially wasted array
                    row.StringValues =
                        new string[source.StringValues.Count]; // worst scenario is partially wasted array
                    var keys = source.StringValues.Keys.ToArray();
                    var values = source.StringValues.Values.ToArray();
                    for (var j = 0; j < keys.Length; j++)
                    {
                        var val = values[j][index];
                        if (ReferenceEquals(val, null)) continue;
                        row.StringParameters[row.StringValueCount] = keys[j];
                        row.StringValues[row.StringValueCount] = val;
                        row.StringValueCount++;
                    }
                }

                if (source.TagValues != null)
                {
                    row.TagValues = new List<KeyValuePair<string, string>>();
                    foreach (var tagValues in source.TagValues)
                    {
                        var val = tagValues.Value[index];
                        if (val == null) continue;
                        row.TagValues.Add(new KeyValuePair<string, string>(tagValues.Key, val));
                    }
                }

                return row;
            }
            catch (System.Exception ex)
            {
                this.logger.LogError(ex, "Exception while converting with CreateRowFromParameterData");
                return new ParameterDataRowForWrite();
            }
        }

        public void ClearBuffer(string[] sourceStreamIds)
        {
            if (sourceStreamIds == null || sourceStreamIds.Length == 0) return;
            // Doing locks here, so we can make sure the collection is not getting expanded or added to out of order while we're shifting through it
            lock (this.eventQueueLock)
            lock (this.paramQueueLock)
            {
                if (!this.eventQueue.IsEmpty)
                {
                    var eventsBefore = this.eventQueue;
                    this.eventQueue = new ConcurrentQueue<StreamEventDataForWrite>();
                    while (eventsBefore.TryDequeue(out var eventDequeued))
                    {
                        if (sourceStreamIds.Contains(eventDequeued.StreamId)) continue;
                        this.eventQueue.Enqueue(eventDequeued);
                    }
                }

                if (!this.paramQueue.IsEmpty)
                {
                    var paramsBefore = this.paramQueue;
                    this.paramQueue = new ConcurrentQueue<StreamParameterDataForWrite>();
                    while (paramsBefore.TryDequeue(out var paramDequeued))
                    {
                        if (sourceStreamIds.Contains(paramDequeued.StreamId)) continue;
                        this.paramQueue.Enqueue(paramDequeued);
                    }
                }
            }
        }

        public async Task Save()
        {
            var eventCommitDoneTaskSource = new TaskCompletionSource<EventCommitItem>();
            var parameterCommitDoneTaskSource = new TaskCompletionSource<ParameterCommitItem>();
            var sw = new Stopwatch();
            // Doing locks here, so we can make sure the collection is not getting expanded after doing certain checks
            lock (this.paramQueueLock)
            {
                lock (this.eventQueueLock)
                {
                    if (this.eventQueue.IsEmpty && this.paramQueue.IsEmpty)
                    {
                        this.logger.LogDebug("There is nothing to save to time-series db for this commit");
                        return;
                    }
                    var eventCommitItem = new EventCommitItem(eventCommitDoneTaskSource, this.eventQueue);
                    this.eventCommitQueue.Enqueue(eventCommitItem);
                    sw.Start();

                    this.eventQueue = new ConcurrentQueue<StreamEventDataForWrite>();
                    this.forceWakeEventWorkerMethod();
                }
                var parameterCommitItem = new ParameterCommitItem(parameterCommitDoneTaskSource, this.paramQueue);
                this.parameterCommitQueue.Enqueue(parameterCommitItem);
                this.forceWakeParameterWorkerMethod();

                this.paramQueue = new ConcurrentQueue<StreamParameterDataForWrite>();
            }

            var compositeTask = Task.WhenAll(eventCommitDoneTaskSource.Task, parameterCommitDoneTaskSource.Task);
            var tsSave = compositeTask.ContinueWith(t=>
            {
                sw.Stop();
                this.logger.LogDebug("Save completed in {0:g} for the time-series db", sw.Elapsed);
            });
            
            // TODO handle exception. At the moment it gets propagates up, but it pretty much un-does the whole commit queue
            await compositeTask;
        }

        private class ParameterCommitItem
        {
            private readonly TaskCompletionSource<ParameterCommitItem> taskCompletionSource;
            private bool isDone = false;
            
            public readonly ConcurrentQueue<StreamParameterDataForWrite> ParameterQueue;

            public ParameterCommitItem(TaskCompletionSource<ParameterCommitItem> taskCompletionSource, ConcurrentQueue<StreamParameterDataForWrite> parameterQueue)
            {
                this.taskCompletionSource = taskCompletionSource;
                this.ParameterQueue = parameterQueue;
                if (this.ParameterQueue.IsEmpty) this.MarkDone();
            }

            public void MarkDone()
            {
                if (this.isDone) return;
                this.isDone = true;
                this.taskCompletionSource.SetResult(this);
                
            }

            public bool IsDone => this.isDone;
        }
        
        private class EventCommitItem
        {
            private readonly TaskCompletionSource<EventCommitItem> taskCompletionSource;
            private bool isDone = false;
            
            public readonly ConcurrentQueue<StreamEventDataForWrite> EventQueue;

            public EventCommitItem(TaskCompletionSource<EventCommitItem> taskCompletionSource, ConcurrentQueue<StreamEventDataForWrite> eventQueue)
            {
                this.taskCompletionSource = taskCompletionSource;
                this.EventQueue = eventQueue;
                if (this.EventQueue.IsEmpty) this.MarkDone();
            }

            public void MarkDone()
            {
                if (this.isDone) return;
                this.isDone = true;
                this.taskCompletionSource.SetResult(this);
                
            }

            public bool IsDone => this.isDone;
        }
        
        private class StreamParameterDataForWrite
        {
            public string StreamId;
            public ParameterDataRowForWrite[] Rows;
        }
        
        private class StreamEventDataForWrite
        {
            public string StreamId;
            public EventDataRaw[] Rows;
        }
    }
}