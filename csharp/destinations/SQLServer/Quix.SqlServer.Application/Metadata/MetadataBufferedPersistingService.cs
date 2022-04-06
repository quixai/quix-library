using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quix.Sdk.Process.Models;
using Quix.SqlServer.Application.Helpers;
using Quix.SqlServer.Application.Models;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;
using Quix.SqlServer.Infrastructure.Shared.Extensions;
using Quix.SqlServer.Infrastructure.TimeSeries.Repositories;
using Quix.Telemetry.Domain.Metadata.Models;
using Quix.TelemetryWriter.Application.Metadata;

namespace Quix.SqlServer.Application.Metadata
{
    public interface IMetadataBufferedPersistingService
    {
        Task Buffer(string sourceStreamId, EventDataRaw[] eventDataRaws, DiscardRange discardRange = null);
        Task Buffer(string sourceStreamId, StreamEnd streamEnd);
        Task Buffer(string sourceStreamId, ParameterDataRaw parameterDataRaw, DiscardRange discardRange = null);
        Task Buffer(string sourceStreamId, ParameterDefinitions parameterDefinitions);
        Task Buffer(string sourceStreamId, EventDefinitions eventDefinitions);
        Task Buffer(string sourceStreamId, StreamProperties streamProperties);
        
        //Task<DiscardRange> GetDiscardRange(string streamId, long timestamp);
        
        void ClearBuffer(string[] sourceStreamIds);
        Task Save();
    }
    
    public class MetadataBufferedPersistingService : IMetadataBufferedPersistingService
    {
        private readonly ILogger<MetadataBufferedPersistingService> logger;
        //private readonly IShutdownService shutdownService;
        private readonly IParameterPersistingService parameterPersistingService;
        private readonly IStreamRepository streamRepository;
        private readonly IEventPersistingService eventPersistingService;
        private readonly StreamIdleTime streamIdleTime;
        private readonly string topicDisplayName;

        /// <summary>
        /// Every time a stream message is buffered, it gets added to this queue.
        /// This queue is processed in a single threaded way, drained in order of buffer. (NOTE: this could be improved by parallel processing grouped by streamId)
        /// Each message shapes what the expected state of the stream should be at that time.
        /// The queue is drained, then the new expected state of each stream (and its parameters etc) is persisted (NOTE: this could be improved by limiting the maximum time of drain to avoid forever drain edge case 
        /// </summary>
        private ConcurrentQueue<StreamMetaDataItem> queue;
        private readonly object queueLock = new object();
        private readonly Task workerTask;
        
        /// <summary>
        /// (streamId - Stream) 
        /// </summary>
        private readonly IDictionary<string, TelemetryStream> cachedTelemetryStreams = new ConcurrentDictionary<string, TelemetryStream>();

        /// <summary>
        /// (streamId - discardRange)
        /// </summary>
        private readonly ConcurrentDictionary<string, DiscardRange> discardRange = new ConcurrentDictionary<string, DiscardRange>();
        
        /// <summary>
        /// (streamId - LastTimeItWasUpdated) 
        /// </summary>
        private readonly Dictionary<string, DateTime> lastStreamUpdate = new Dictionary<string, DateTime>();
        
        /// <summary>
        /// When a save is triggered, current queues are snapshot and added to this queue.
        /// The worked method prioritizes persistence of this queue over others. Thanks to this
        /// queue multiple saves can be triggered without having to wait for last to complete 
        /// </summary>
        private readonly ConcurrentQueue<CommitCollection> commitQueue = new ConcurrentQueue<CommitCollection>();
        private Action ForceWakeWorkerMethod = () => { };
        
        public MetadataBufferedPersistingService(
            ILoggerFactory loggerFactory,
            //IShutdownService shutdownService,
            IParameterPersistingService parameterPersistingService,
            IStreamRepository streamRepository,
            IEventPersistingService eventPersistingService,         
            TopicName topicDisplayName,
            StreamIdleTime streamIdleTime)
        {
            this.logger = loggerFactory.CreateLogger<MetadataBufferedPersistingService>();
            //this.shutdownService = shutdownService;
            this.parameterPersistingService = parameterPersistingService;
            this.streamRepository = streamRepository;
            this.eventPersistingService = eventPersistingService;
            this.streamIdleTime = streamIdleTime;
            this.topicDisplayName = topicDisplayName.Value;
            this.queue = new ConcurrentQueue<StreamMetaDataItem>();
            this.workerTask = Task.Run(this.WorkerMethod);
        }

        private async Task WorkerMethod()
        {
            int cooldownPeriodUpdatedStream = 20; // if we have a lot of streams (100+ to update per loop, then this should start to increase the delay based on load)
            int minCooldownPeriod = 2000; // however we still don't want to update too frequently
            int maxCooldownPeriod = 10000; // but lets try to max it..
            int minimumSleep = 100; // This in case the process of the queue or db takes most of the cooldown period then don't sleep excessively short amounts
            var mre = new SemaphoreSlim(0);
            var timer = new System.Timers.Timer();
            timer.AutoReset = false;
            timer.Elapsed += (s, a) =>
            {
                mre.Release();
            };

            ForceWakeWorkerMethod = () =>
            {
                if (timer.Enabled)
                {
                    this.logger.LogTrace("Waking up timer due to force awake.");
                    timer.Stop();
                    mre.Release();
                }
            };

            await CacheOpenStreamsFromDatabase();

            while (true)
            {
                var isCommitQueue = false;
                try
                {
                    ConcurrentQueue<StreamMetaDataItem> queueToConsume = null;
                    if (this.commitQueue.Count > 0)
                    {
                        this.commitQueue.TryPeek(out var result);

                        if (result != null)
                        {
                            if (!result.IsDone)
                            {
                                if (result.Queue.IsEmpty)
                                {
                                    result.MarkDone();
                                    this.commitQueue.TryDequeue(out var _);
                                }
                                else
                                {
                                    queueToConsume = result.Queue;
                                    isCommitQueue = true;
                                }
                            }
                        }
                    }

                    if (!isCommitQueue)
                    {
                        lock (this.queueLock)
                        {
                            queueToConsume = this.queue;
                            this.queue = new ConcurrentQueue<StreamMetaDataItem>();
                        }
                    }
                    
                    // drain the queue, so according to the data we have in there we can
                    // set the final version of streams we need to update them to
                    var started = DateTime.UtcNow;
                    var streamUpdates = GetStreamUpdates(queueToConsume);
                    if (streamUpdates.Count > 0)
                    {
                        // Now that we have the final state of things, lets load streams and details that need loading
                        
                        var streamChangesTask = TryUntilSuccess(() => this.PersistStreamChanges(streamUpdates), "Stream changes failed to save");
                        var parameterChangesTask = TryUntilSuccess(() => this.parameterPersistingService.Persist(streamUpdates), "Parameter changes failed to save");
                        var eventChangesTask = TryUntilSuccess(() => this.eventPersistingService.Persist(streamUpdates), "Event changes failed to save");
                        await Task.WhenAll(streamChangesTask, parameterChangesTask, eventChangesTask);
                    }

                    if (this.commitQueue.Count > 0)
                    {
                        if (!isCommitQueue) this.logger.LogTrace("Avoiding sleep due to having to deal with commit."); // already dealing with it
                        continue;
                    }

                    // finish up
                    var ended = DateTime.UtcNow;
                    var duration = (ended - started).TotalMilliseconds;
                    var leftOver = Math.Min(Math.Max(cooldownPeriodUpdatedStream * streamUpdates.Count, minCooldownPeriod), maxCooldownPeriod) - duration;
                    if (leftOver > minimumSleep)
                    {
                        var sw = Stopwatch.StartNew();
                        this.logger.LogTrace("Metadata persistence to sleep for {0}ms", Math.Round(leftOver));
                        timer.Interval = leftOver;
                        timer.Start();
                        await mre.WaitAsync();
                        sw.Stop();
                        this.logger.LogTrace("Metadata persistence slept for {0}ms", sw.ElapsedMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Exception in the Metadata WorkerMethod.");
                }
            }
        }

        private async Task TryUntilSuccess(Func<Task> funcToWrap, string errorMessage)
        {
            var delay = 0;
            const int incr = 5000;
            const int max = 30000;
            var counter = 0;
            do
            {
                try
                {
                    await funcToWrap();
                    return;
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, errorMessage);
                    if (counter > 10)
                    {
                        // Had enough exceptions. Lets try restarting the writer. (This would simply kill it in a non orchestrator environment where it is running as a service)
                        this.logger.LogCritical("Exceeded maximum number of retries. Restarting application in order to try automatic self-heal.");
                        
                        // TODO do we need this?
                        //this.shutdownService.StopApplication();
                    }
                }

                counter++;
                delay = Math.Min(delay + incr, max);
                this.logger.LogDebug("Waiting for {0}ms, then reattempting again. Total tries so far: {1}.", delay, counter);
                await Task.Delay(delay);
            } while (true);// will break out with return;
        }

        private async Task CacheOpenStreamsFromDatabase()
        {
            var streamsToNotLoad = this.cachedTelemetryStreams.Keys.ToList();
            
            var streamLoadSw = Stopwatch.StartNew();
            var streams = await this.streamRepository.GetAll().Where(y => !streamsToNotLoad.Contains(y.StreamId) && y.Status == StreamStatus.Open && y.Topic == this.topicDisplayName).ToListAsync();
            streamLoadSw.Stop();
            foreach (var telemetryStream in streams)
            {
                this.cachedTelemetryStreams[telemetryStream.StreamId] = telemetryStream;
                this.lastStreamUpdate[telemetryStream.StreamId] = telemetryStream.LastUpdate;
            }

            if (this.logger.IsEnabled(LogLevel.Debug))
            {
                this.logger.LogDebug("Loaded {0} open streams from repository in {2:g}", streams.Count(y=> y.Status == StreamStatus.Open), streamLoadSw.Elapsed);
            }
        } 

        private Dictionary<string, PendingStreamUpdates> GetStreamUpdates(ConcurrentQueue<StreamMetaDataItem> queueToConsume)
        {
            var now = DateTime.UtcNow;
            var streamUpdates = new Dictionary<string, PendingStreamUpdates>();
            while (queueToConsume.TryDequeue(out var dequeued))
            {
                if (!streamUpdates.TryGetValue(dequeued.StreamId, out var pendingStreamUpdates))
                {
                    pendingStreamUpdates = new PendingStreamUpdates();
                    streamUpdates[dequeued.StreamId] = pendingStreamUpdates;
                }

                switch (dequeued.MetaDataItem)
                {
                    case StreamEnd streamEnd:
                        ApplyMessageChanges(streamEnd, pendingStreamUpdates);
                        break;
                    case StreamProperties streamProperties:
                        ApplyMessageChanges(streamProperties, pendingStreamUpdates);
                        break;
                    case ParameterDefinitions parameterDefinitions:
                        ApplyMessageChanges(parameterDefinitions, pendingStreamUpdates);
                        break;
                    case EventDefinitions eventDefinitions:
                        ApplyMessageChanges(eventDefinitions, pendingStreamUpdates);
                        break;
                    case ParameterDataRaw parameterDataRaw:
                        ApplyMessageChanges(parameterDataRaw, pendingStreamUpdates, dequeued.DiscardRange);
                        break;
                    case EventDataRaw[] eventDataRaw:
                        ApplyMessageChanges(eventDataRaw, pendingStreamUpdates, dequeued.DiscardRange);
                        break;
                }
            }

            foreach (var streamUpdate in streamUpdates)
            {
                this.logger.LogTrace("Updated {0} last activity to {1}", streamUpdate.Key, now);
                this.lastStreamUpdate[streamUpdate.Key] = now;
            }
            
            var cutOff = now.AddMilliseconds(-this.streamIdleTime.Value);
            var idleStreams = this.lastStreamUpdate.Where(y => y.Value <= cutOff);
            var idleCount = 0;
            foreach (var keyValuePair in idleStreams)
            {
                this.logger.LogTrace("Updated {0} as inactive as last activity is at {1}", keyValuePair.Key, keyValuePair.Value);
                idleCount++;
                streamUpdates[keyValuePair.Key] = new PendingStreamUpdates()
                {
                    Status = StreamStatus.Idle
                };
            }

            if (idleCount > 0)
            {
                this.logger.LogDebug("Marking {0} streams as {1} as they had no data for over {2}ms.", idleCount, StreamStatus.Idle, this.streamIdleTime.Value);
            }

            return streamUpdates;
        }

        /// <returns>The newly cached streams</returns>
        private async Task<List<TelemetryStream>> CacheStreams(List<string> streamsToCache)
        {
            var streamsToLoad = streamsToCache.Except(this.cachedTelemetryStreams.Keys).ToList();
            var streams = await this.streamRepository.GetAll().Where(y => streamsToLoad.Contains(y.StreamId)).ToListAsync();
            foreach (var telemetryStream in streams)
            {
                this.cachedTelemetryStreams[telemetryStream.StreamId] = telemetryStream;
            }

            return streams;
        }

        private async Task PersistStreamChanges(Dictionary<string, PendingStreamUpdates> streamUpdates)
        {
            var streamsToLoad = streamUpdates.Where(y => y.Value.UpdatesStream)
                .Select(y => y.Key).Except(this.cachedTelemetryStreams.Keys).ToList();

            var streamLoadSw = Stopwatch.StartNew();
            var streamsCached = await this.CacheStreams(streamsToLoad);
            streamLoadSw.Stop();
            if (streamsCached.Count != streamsToLoad.Count)
            {
                this.logger.LogTrace("Only found {0} out of {1} streams in the db. Possibly new streams. Loaded in {2:g}", streamsCached.Count, streamsToLoad.Count, streamLoadSw.Elapsed);
            }
            else
            {
                this.logger.LogTrace("Found all {0} streams in the db. Loaded in {1:g}", streamsCached.Count, streamLoadSw.Elapsed);
            }

            var requests = new List<TelemetryStream>();
            var updateCounter = 0;
            var createCounter = 0;

            var nolongerReceiving = 0;
            var reopenedStreams = 0;

            foreach (var streamUpdate in streamUpdates)
            {
                if (!this.cachedTelemetryStreams.TryGetValue(streamUpdate.Key, out var telemetryStream))
                {
                    telemetryStream = new TelemetryStream(streamUpdate.Key)
                    {
                        Status = streamUpdate.Value.Status ?? StreamStatus.Open,
                        Location = streamUpdate.Value.Location ?? "/",
                        Topic = this.topicDisplayName,
                        Name = streamUpdate.Value.Name ?? streamUpdate.Key,
                        Parents = streamUpdate.Value.Parents ?? new List<string>(),
                        Start = streamUpdate.Value.Start,
                        End = streamUpdate.Value.End,
                        TimeOfRecording = streamUpdate.Value.TimeOfRecording,
                        Metadata = streamUpdate.Value.Metadata
                    };
                    
                    requests.Add(telemetryStream);
                    this.cachedTelemetryStreams[streamUpdate.Key] = telemetryStream;
                    createCounter++;
                    continue;
                }
                
                var inactiveStatuses = new StreamStatus[]
                {
                    StreamStatus.Idle, StreamStatus.Aborted, StreamStatus.Closed, StreamStatus.Deleting, StreamStatus.Terminated, StreamStatus.SoftDeleted, StreamStatus.Interrupted
                };

                if (!streamUpdate.Value.UpdatesStream)
                {
                    // Even if there is no stream details specific change but the stream was previously inactive, but is no longer then I want to mark it as reopened
                    if (telemetryStream.Status != StreamStatus.Open && inactiveStatuses.Contains(telemetryStream.Status))
                    {
                        reopenedStreams++;
                        // todo mongo stuff
                        // requests.Add(new UpdateOneModel<TelemetryStream>(Builders<TelemetryStream>.Filter.Eq(y=> y.StreamId, streamUpdate.Key), Builders<TelemetryStream>.Update.Set(y=> y.Status, StreamStatus.Open)));
                        updateCounter++;
                    }
                    continue;
                }
                
                // if the stream was previously inactive, but is no longer then I want to mark it as reopened
                if (streamUpdate.Value.Status != null && inactiveStatuses.Contains(telemetryStream.Status) && !inactiveStatuses.Contains(streamUpdate.Value.Status.Value))
                {
                    reopenedStreams++;
                }
                
                var updateDefinitions = new List<TelemetryStream>();

                void UpdateProperty<T>(Expression<Func<TelemetryStream, T>> selector, T newVal, TelemetryStream cachedStream)
                {
                    var func = selector.Compile();
                    var oldVal = func(cachedStream);
                    if (newVal != null && (oldVal == null || !oldVal.Equals(newVal)))
                    {
                        // todo mongo stuff
                        //updateDefinitions.Add(Builders<TelemetryStream>.Update.Set(selector, newVal));
                        var body = selector.Body;
                        if (body is UnaryExpression unaryExpression)
                        {
                            body = unaryExpression.Operand;
                        }
                        var prop = (PropertyInfo)((MemberExpression)body).Member;
                        prop.SetValue(cachedStream, newVal, null);
                    }
                }
                
                var durBefore = telemetryStream.Duration;
                streamUpdate.Value.End = streamUpdate.Value.End == null ? (long?) null : Math.Max(streamUpdate.Value.End.Value, telemetryStream.End ?? long.MinValue);
                UpdateProperty(s => s.End, streamUpdate.Value.End, telemetryStream);
                streamUpdate.Value.Start = streamUpdate.Value.Start == null ? (long?) null : Math.Min(streamUpdate.Value.Start.Value, telemetryStream.Start ?? long.MaxValue);
                UpdateProperty(s => s.Start, streamUpdate.Value.Start, telemetryStream);
                
                // todo mongo stuff
                //if (durBefore != telemetryStream.Duration) updateDefinitions.Add(Builders<TelemetryStream>.Update.Set(s=> s.Duration, telemetryStream.Duration)); // computed field, need to force like this
                
                UpdateProperty(s => s.Name, streamUpdate.Value.Name, telemetryStream);
                UpdateProperty(s => s.Location, streamUpdate.Value.Location, telemetryStream);
                UpdateProperty(s => s.Status, streamUpdate.Value.Status, telemetryStream);
                UpdateProperty(s => s.TimeOfRecording, streamUpdate.Value.TimeOfRecording, telemetryStream);
                
                if (inactiveStatuses.Contains(telemetryStream.Status))
                {
                    this.parameterPersistingService.UnCache(telemetryStream.StreamId);
                    this.eventPersistingService.UnCache(telemetryStream.StreamId);
                    this.cachedTelemetryStreams.Remove(telemetryStream.StreamId);
                    this.lastStreamUpdate.Remove(telemetryStream.StreamId);
                    this.discardRange.TryRemove(telemetryStream.StreamId, out var _);
                    nolongerReceiving++;
                }

                // collections need a bit special treatment
                if (streamUpdate.Value.Metadata != null && (telemetryStream.Metadata == null || !streamUpdate.Value.Metadata.SequenceEqual(telemetryStream.Metadata)))
                {
                    telemetryStream.Metadata = streamUpdate.Value.Metadata;
                    // todo mongo stuff
                    //updateDefinitions.Add(Builders<TelemetryStream>.Update.Set(y => y.Metadata, telemetryStream.Metadata));
                }
                if (streamUpdate.Value.Parents != null && (telemetryStream.Parents == null || !streamUpdate.Value.Parents.SequenceEqual(telemetryStream.Parents)))
                {
                    telemetryStream.Parents = streamUpdate.Value.Parents;
                    // todo mongo stuff
                    //updateDefinitions.Add(Builders<TelemetryStream>.Update.Set(y => y.Parents, telemetryStream.Parents));
                }
                
                if (updateDefinitions.Count != 0)
                {
                    // todo mongo stuff
                    //requests.Add(new UpdateOneModel<TelemetryStream>(Builders<TelemetryStream>.Filter.Eq(y=> y.StreamId, streamUpdate.Key), Builders<TelemetryStream>.Update.Combine(updateDefinitions)));
                    updateCounter++;
                }
            }
            
            if (requests.Count == 0)
            {
                this.logger.LogTrace("None of the changes require stream update. Not updating.");
                return;
            }
            this.logger.LogTrace("Creating {0} streams and updating {1}.", createCounter, updateCounter);
            var sw = Stopwatch.StartNew();
            await this.streamRepository.BulkWrite(requests);
            sw.Stop();
            this.logger.LogDebug("Created {0} streams and updated {1} in {2:g}.", createCounter, updateCounter, sw.Elapsed);
            if (nolongerReceiving > 0)
            {
                this.logger.LogDebug("Marking {0} streams as 'no longer receiving' due to closure, idle or similar.", nolongerReceiving);
            }

            if (reopenedStreams > 0)
            {
                this.logger.LogDebug("Marking {0} streams as {1} due to receiving data again.", reopenedStreams, StreamStatus.Open);
            }
        }

        #region ApplyMessageChanges
        private static void ApplyMessageChanges(EventDataRaw[] eventDataRaws, PendingStreamUpdates pendingStreamUpdates, DiscardRange discardRange)
        {
            if (eventDataRaws == null || eventDataRaws.Length == 0) return;
            var min = long.MaxValue;
            var max = long.MinValue;
            var empty = true;
            foreach (var eventDataRaw in eventDataRaws)
            {
                var ts = eventDataRaw.Timestamp;
                if (ts < discardRange.DiscardBefore) continue;
                if (ts > discardRange.DiscardAfter) continue;
                empty = false;
                if (min > ts) min = ts;
                if (max < ts) max = ts;
            }

            if (!empty)
            {
                pendingStreamUpdates.Start = Math.Min(pendingStreamUpdates.Start ?? long.MaxValue, min);
                pendingStreamUpdates.End = Math.Max(pendingStreamUpdates.End ?? long.MinValue, max);
            }

            pendingStreamUpdates.ActiveEventIds = pendingStreamUpdates.ActiveEventIds ?? new HashSet<string>();
            foreach (var eventDataRaw in eventDataRaws)
            {
                // it is possible that the type gets overwritten by another if same id used for multiple, but that is just bad practice from stream sender
                pendingStreamUpdates.ActiveEventIds.Add(eventDataRaw.Id);
            }
            pendingStreamUpdates.Status = StreamStatus.Open;
        }

        private static void ApplyMessageChanges(ParameterDataRaw parameterDataRaw, PendingStreamUpdates pendingStreamUpdates, DiscardRange discardRange)
        {
            if (!parameterDataRaw.HasValues()) return; // how is that possible
            if (parameterDataRaw.TryGetStartEndNanoseconds(out var start, out var end, discardRange))
            {
                // we have unfiltered data, so can update start/end 
                pendingStreamUpdates.Start = Math.Min(pendingStreamUpdates.Start ?? long.MaxValue, start);
                pendingStreamUpdates.End = Math.Max(pendingStreamUpdates.End ?? long.MinValue, end);
            }
            
            pendingStreamUpdates.ParameterIds = pendingStreamUpdates.ParameterIds ?? new Dictionary<string, ParameterType>();

                if (parameterDataRaw.NumericValues != null)
            {
                foreach (var paramId in parameterDataRaw.NumericValues)
                {
                    pendingStreamUpdates.ParameterIds[paramId.Key] = ParameterType.Numeric;
                }
            }

            // it is possible that the type gets overwritten by another if same id used for multiple, but that is just bad practice from stream sender
            if (parameterDataRaw.StringValues != null)
            {
                foreach (var paramId in parameterDataRaw.StringValues)
                {
                    pendingStreamUpdates.ParameterIds[paramId.Key] = ParameterType.String;
                }
            }

            // it is possible that the type gets overwritten by another if same id used for multiple, but that is just bad practice from stream sender
            if (parameterDataRaw.BinaryValues != null)
            {
                foreach (var paramId in parameterDataRaw.BinaryValues)
                {
                    pendingStreamUpdates.ParameterIds[paramId.Key] = ParameterType.Binary;
                }
            }
            pendingStreamUpdates.Status = StreamStatus.Open;
        }

        private static void ApplyMessageChanges(EventDefinitions eventDefinitions, PendingStreamUpdates pendingStreamUpdates)
        {
            pendingStreamUpdates.EventDefinitions = eventDefinitions;
            pendingStreamUpdates.Status = StreamStatus.Open;
        }

        private static void ApplyMessageChanges(ParameterDefinitions parameterDefinitions, PendingStreamUpdates pendingStreamUpdates)
        {
            pendingStreamUpdates.ParameterDefinitions = parameterDefinitions;
            pendingStreamUpdates.Status = StreamStatus.Open;
        }

        private static void ApplyMessageChanges(StreamEnd streamEnd, PendingStreamUpdates streamUpdates)
        {
            switch (streamEnd.StreamEndType)
            {
                case StreamEndType.Closed:
                    streamUpdates.Status = StreamStatus.Closed;
                    break;
                case StreamEndType.Aborted:
                    streamUpdates.Status = StreamStatus.Aborted;
                    break;
                case StreamEndType.Terminated:
                    streamUpdates.Status = StreamStatus.Terminated;
                    break;
            }
        }
        
        private static void ApplyMessageChanges(StreamProperties streamProperties, PendingStreamUpdates streamUpdates)
        {
            if (string.IsNullOrEmpty(streamProperties.Location))
            {
                streamUpdates.Location = "/";
            }
            else
            {
                streamUpdates.Location = streamProperties.Location;
                if (streamUpdates.Location[0] != '/') streamUpdates.Location = "/" + streamUpdates.Location;
                if (streamUpdates.Location[streamUpdates.Location.Length - 1] != '/') streamUpdates.Location += "/";     
            }


            streamUpdates.Name = streamProperties.Name;
            streamUpdates.Metadata = streamProperties.Metadata;
            streamUpdates.Parents = streamProperties.Parents;
            streamUpdates.TimeOfRecording = streamProperties.TimeOfRecording;
            streamUpdates.Status = StreamStatus.Open;
        }
#endregion

        public Task Buffer(string sourceStreamId, EventDataRaw[] eventDataRaws, DiscardRange discardRange = null)
        {
            discardRange = discardRange ?? DiscardRange.NoDiscard;
            this.queue.Enqueue(new StreamMetaDataItem() { StreamId = sourceStreamId, MetaDataItem = eventDataRaws, DiscardRange = discardRange});
            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, StreamEnd streamEnd)
        {
            this.queue.Enqueue(new StreamMetaDataItem() { StreamId = sourceStreamId, MetaDataItem = streamEnd});
            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, ParameterDataRaw parameterDataRaw, DiscardRange discardRange = null)
        {
            discardRange = discardRange ?? DiscardRange.NoDiscard;
            this.queue.Enqueue(new StreamMetaDataItem() { StreamId = sourceStreamId, MetaDataItem = parameterDataRaw, DiscardRange = discardRange});
            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, ParameterDefinitions parameterDefinitions)
        {
            this.queue.Enqueue(new StreamMetaDataItem() { StreamId = sourceStreamId, MetaDataItem = parameterDefinitions});
            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, EventDefinitions eventDefinitions)
        {
            this.queue.Enqueue(new StreamMetaDataItem() {StreamId = sourceStreamId, MetaDataItem = eventDefinitions});
            return Task.CompletedTask;
        }

        public Task Buffer(string sourceStreamId, StreamProperties streamProperties)
        {
            this.queue.Enqueue(new StreamMetaDataItem() {StreamId = sourceStreamId, MetaDataItem = streamProperties});
            return Task.CompletedTask;
        }

        //TODO I think were unlikely to need the discard range.
        // public async Task<DiscardRange> GetDiscardRange(string streamId, long timestamp)
        // {
        //     if (this.discardRange.TryGetValue(streamId, out var discardDataDetails)) return discardDataDetails;
        //
        //     await this.CacheStreams(new List<string> {streamId});
        //
        //     if (this.cachedTelemetryStreams.TryGetValue(streamId, out var stream))
        //     {
        //         if (stream.Start != null) timestamp = stream.Start.Value;
        //     }
        //
        //     var epoch = new DateTime(1970, 1, 1);
        //     var timestampAsDate = epoch + new TimeSpan(timestamp / 100);
        //     var discardRange = new DiscardRange()
        //     {
        //         DiscardAfter = (timestampAsDate.AddYears(2) - epoch).Ticks * 100, // time + 2 years as nano
        //         DiscardBefore = (timestampAsDate.AddDays(-1) - epoch).Ticks * 100 // time -1 day as nano (to allow for minor back-filling)
        //     };
        //     this.discardRange[streamId] = discardRange;
        //     return discardRange;
        // }

        public Task Save()
        {
            var commitDoneTaskSource = new TaskCompletionSource<CommitCollection>();
            var sw = new Stopwatch();
            // Doing locks here, so we can make sure the collection is not getting expanded after doing certain checks
            lock (this.queueLock)
            {
                if (this.queue.IsEmpty)
                {
                    this.logger.LogDebug("There is nothing to save to metadata db for this commit");
                    return Task.CompletedTask;
                }

                var commitCollection = new CommitCollection(commitDoneTaskSource, this.queue);
                this.commitQueue.Enqueue(commitCollection);
                sw.Start();

                this.queue = new ConcurrentQueue<StreamMetaDataItem>();
            }

            this.ForceWakeWorkerMethod();

            // TODO handle exception. At the moment it gets propagates up, but it pretty much un-does the whole commit queue
            return commitDoneTaskSource.Task.ContinueWith(t=>
            {
                sw.Stop();
                this.logger.LogDebug("Save completed in {0:g} for the metadata db", sw.Elapsed);
            });
        }
        
        public void ClearBuffer(string[] sourceStreamIds)
        {
            if (sourceStreamIds == null || sourceStreamIds.Length == 0) return;
            lock (this.queueLock)
            {
                if (this.queue.IsEmpty) return;
                var before = this.queue;
                this.queue = new ConcurrentQueue<StreamMetaDataItem>();
                while (before.TryDequeue(out var dequeued))
                {
                    if (sourceStreamIds.Contains(dequeued.StreamId)) continue;
                    this.queue.Enqueue(dequeued);
                }
            }
        }

        private class StreamMetaDataItem
        {
            public string StreamId;
            public object MetaDataItem;
            public DiscardRange DiscardRange; // Optional
        }

        private class CommitCollection
        {
            private readonly TaskCompletionSource<CommitCollection> taskCompletionSource;
            private bool isDone = false;
            
            public readonly ConcurrentQueue<StreamMetaDataItem> Queue;

            public CommitCollection(TaskCompletionSource<CommitCollection> taskCompletionSource, ConcurrentQueue<StreamMetaDataItem> queue)
            {
                this.taskCompletionSource = taskCompletionSource;
                this.Queue = queue;
                if (this.Queue.IsEmpty) this.MarkDone();
            }
            
            public bool IsDone => this.isDone;

            public void MarkDone()
            {
                if (this.isDone) return;
                this.isDone = true;
                if (this.isDone)
                {
                    this.taskCompletionSource.SetResult(this);
                }
            }
        }
    }
}