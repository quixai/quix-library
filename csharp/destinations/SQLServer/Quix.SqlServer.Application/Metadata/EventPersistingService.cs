using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quix.Sdk.Process.Models;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;
using Quix.SqlServer.Infrastructure.Shared.Extensions;

namespace Quix.SqlServer.Application.Metadata
{
    /*
     * NOTE:
     * This class is extremely similar to ParameterPersistingService for all code except that directly
     * deals with logic/properties unique to events. Lifting the common code is quite difficult without
     * adding excessive complexity.
     */

    public interface IEventPersistingService
    {
        Task Persist(Dictionary<string, PendingStreamUpdates> streamUpdates);
        void UnCache(string streamId);
    }

    public class EventPersistingService : IEventPersistingService
    {
        private readonly ILogger<EventPersistingService> logger;
        private readonly IEventRepository eventRepository;
        private readonly IEventGroupRepository eventGroupRepository;
        
        /// <summary>
        /// (streamId - (eventId - Event))
        /// Needs to be concurrent due to <see cref="UnCache"/>
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, TelemetryEvent>> cachedTelemetryEvents = new ConcurrentDictionary<string, Dictionary<string, TelemetryEvent>>();
        
        /// <summary>
        /// (streamId - (eventGroupPath - EventGroup))
        /// Needs to be concurrent due to <see cref="UnCache"/>
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, TelemetryEventGroup>> cachedTelemetryEventGroups = new ConcurrentDictionary<string, Dictionary<string, TelemetryEventGroup>>();

        public EventPersistingService(ILogger<EventPersistingService> logger, IEventRepository eventRepository, IEventGroupRepository eventGroupRepository)
        {
            this.logger = logger;
            this.eventRepository = eventRepository;
            this.eventGroupRepository = eventGroupRepository;
        }
        
        public async Task Persist(Dictionary<string, PendingStreamUpdates> streamUpdates)
        {
            var uncachedStreamEventIds = this.SelectUncachedStreamsIds(streamUpdates);
            
            var eventLocationLookup = await this.PersistEventGroupChanges(streamUpdates, uncachedStreamEventIds);

            await this.PersistEventChanges(streamUpdates, uncachedStreamEventIds, eventLocationLookup);
        }
        
        private List<string> SelectUncachedStreamsIds(Dictionary<string, PendingStreamUpdates> streamUpdates)
        {
            // could do except with groups too, but they have the same key set
            return streamUpdates.Where(y => y.Value.UpdatesEvents || y.Value.UpdatesEventGroups).Select(y => y.Key)
                .Except(this.cachedTelemetryEvents.Keys).ToList();
        }

        private async Task PersistEventChanges(Dictionary<string, PendingStreamUpdates> streamUpdates, List<string> uncachedStreamEventIds, Dictionary<string, Dictionary<string, string>> eventLocationLookup)
        {
            
            var eventRequests = new List<TelemetryEvent>();
            
            await this.CacheEventsForStreams(uncachedStreamEventIds, eventRequests);

            
            var eventUpdateCounter = 0;
            var eventCreateCounter = 0;
            foreach (var streamUpdate in streamUpdates)
            {
                if (!streamUpdate.Value.UpdatesEvents) continue; // Doing this after cache check to avoid unnecessarily looking in the db again for the same stream
                
                if (!this.cachedTelemetryEvents.TryGetValue(streamUpdate.Key, out var telemetryEvents))
                {
                    telemetryEvents = new Dictionary<string, TelemetryEvent>();
                    this.cachedTelemetryEvents[streamUpdate.Key] = telemetryEvents;
                }

                var hasStreamEventLocations = eventLocationLookup.TryGetValue(streamUpdate.Key, out var streamEventLocations);

                bool TryLookupLocation(string eventId, out string location)
                {
                    location = null;
                    if (!hasStreamEventLocations) return false;
                    if (!streamEventLocations.TryGetValue(eventId, out var path)) return false;
                    location = path;
                    return true;
                }

                // Add/Update events that arrived via definitions
                if (streamUpdate.Value.EventDefinitions?.Events != null)
                {
                    foreach (var eventDefinition in EnumerateForDefinitions(streamUpdate.Value))
                    {
                        if (!telemetryEvents.TryGetValue(eventDefinition.Id, out var telemetryEvent))
                        {
                            telemetryEvent = new TelemetryEvent(streamUpdate.Key)
                            {
                                EventId = eventDefinition.Id,
                                Name = string.IsNullOrWhiteSpace(eventDefinition.Name) ? eventDefinition.Id : eventDefinition.Name,
                                Description = eventDefinition.Description,
                                Location = "/",
                                CustomProperties = eventDefinition.CustomProperties,
                                Level = (TelemetryEventLevel)Enum.Parse(typeof(TelemetryEventLevel), eventDefinition.Level.ToString(), true)
                            };


                            if (TryLookupLocation(telemetryEvent.EventId, out var location))
                            {
                                telemetryEvent.Location = location;
                            }

                            eventRequests.Add(telemetryEvent);
                            telemetryEvents[eventDefinition.Id] = telemetryEvent;
                            eventCreateCounter++;
                            continue;
                        }

                        var updateDefinitions = new List<TelemetryEvent>();

                        void UpdateProperty<T>(Expression<Func<TelemetryEvent, T>> selector, T newVal, TelemetryEvent cachedEvent)
                        {
                            var func = selector.Compile();
                            var oldVal = func(cachedEvent);
                            if (newVal == null || (oldVal != null && oldVal.Equals(newVal))) return;
                            //todo mongo stuff
                            //updateDefinitions.Add(Builders<TelemetryEvent>.Update.Set(selector, newVal));
                            var body = selector.Body;
                            if (body is UnaryExpression unaryExpression)
                            {
                                body = unaryExpression.Operand;
                            }

                            var prop = (PropertyInfo) ((MemberExpression) body).Member;
                            prop.SetValue(cachedEvent, newVal, null);
                        }

                        if (telemetryEvent.Name == null || !string.IsNullOrEmpty(eventDefinition.Name))
                        {
                            var nameToUse = string.IsNullOrWhiteSpace(eventDefinition.Name) ? eventDefinition.Id : eventDefinition.Name;
                            UpdateProperty(y => y.Name, nameToUse, telemetryEvent);
                        }
                        if (!string.IsNullOrEmpty(eventDefinition.Description)) UpdateProperty(y => y.Description, eventDefinition.Description, telemetryEvent);
                        if (!string.IsNullOrEmpty(eventDefinition.CustomProperties)) UpdateProperty(y => y.CustomProperties, eventDefinition.CustomProperties, telemetryEvent);
                        UpdateProperty(y => y.Level, (TelemetryEventLevel)Enum.Parse(typeof(TelemetryEventLevel), eventDefinition.Level.ToString(), true), telemetryEvent);

                        if (TryLookupLocation(telemetryEvent.EventId, out var locationToUpdateTo))
                        {
                            UpdateProperty(y => y.Location, locationToUpdateTo, telemetryEvent);
                        }

                        if (updateDefinitions.Count == 0) continue;
                        
                        //todo mongo stuff
                        //var eqStream = Builders<TelemetryEvent>.Filter.Eq(y => y.StreamId, streamUpdate.Key);
                        //var eqEventId = Builders<TelemetryEvent>.Filter.Eq(y => y.EventId, telemetryEvent.EventId);
                        //var filter = Builders<TelemetryEvent>.Filter.And(eqStream, eqEventId);
                        //eventRequests.Add(new UpdateOneModel<TelemetryEvent>(filter, Builders<TelemetryEvent>.Update.Combine(updateDefinitions)));
                        eventUpdateCounter++;
                    }
                }

                // Add events for which no definition came through yet, but value did.
                if (streamUpdate.Value.ActiveEventIds != null)
                {
                    foreach (var eventId in streamUpdate.Value.ActiveEventIds)
                    {
                        if (!telemetryEvents.TryGetValue(eventId, out var telemetryEvent))
                        {
                            telemetryEvent = new TelemetryEvent(streamUpdate.Key)
                            {
                                EventId = eventId,
                                Name = eventId,
                                Location = "/",
                                Level = TelemetryEventLevel.Information
                            };

                            eventRequests.Add(telemetryEvent);
                            telemetryEvents[eventId] = telemetryEvent;
                            eventCreateCounter++;
                        }
                    }

                    // TODO check for events to delete ? These could be events that were previously added but never had any value come through and no longer show up in the definition... super edge case, so not bothering for now
                }
            }

            if (eventRequests.Count == 0)
            {
                this.logger.LogTrace("None of the changes require stream event update. Not updating.");
                return;
            }

            this.logger.LogTrace("Creating {0} stream events and updating {1}.", eventCreateCounter, eventUpdateCounter);
            var sw = Stopwatch.StartNew();
            await this.eventRepository.BulkWrite(eventRequests);
            sw.Stop();
            this.logger.LogDebug("Created {0} stream events and updated {1} in {2:g}.", eventCreateCounter, eventUpdateCounter, sw.Elapsed);
        }

        static IEnumerable<EventDefinition> EnumerateForDefinitions(PendingStreamUpdates streamUpdate)
        {
            foreach (var eventDefinition in streamUpdate.EventDefinitions.Events)
            {
                yield return eventDefinition;
            }

            IEnumerable<EventDefinition> EnumerateGroupForDefinitions(EventGroupDefinition groupDefinition)
            {
                if (groupDefinition.Events != null)
                {
                    foreach (var definitionEvent in groupDefinition.Events)
                    {
                        yield return definitionEvent;
                    }
                }

                if (groupDefinition.ChildGroups != null)
                {
                    foreach (var groupDefinitionChildGroup in groupDefinition.ChildGroups)
                    {
                        foreach (var eventDefinition in EnumerateGroupForDefinitions(groupDefinitionChildGroup))
                        {
                            yield return eventDefinition;
                        }
                    }
                }
            }

            if (streamUpdate.EventDefinitions.EventGroups == null) yield break;
            foreach (var groupDefinitionChildGroup in streamUpdate.EventDefinitions.EventGroups)
            {
                foreach (var eventDefinition in EnumerateGroupForDefinitions(groupDefinitionChildGroup))
                {
                    yield return eventDefinition;
                }
            }
        }

        private async Task CacheEventsForStreams(List<string> uncachedStreamEventIds, List<TelemetryEvent> eventRequests)
        {
            var eventsLoadSw = Stopwatch.StartNew();
            var events = await this.eventRepository.GetAll().Where(y => uncachedStreamEventIds.Contains(y.StreamId)).ToListAsync();
            eventsLoadSw.Stop();
            var loadedStreamsFromDbForEvents = 0;

            foreach (var telemetryStreamEvents in events.GroupBy(y => y.StreamId))
            {
                loadedStreamsFromDbForEvents++;
                try
                {
                    this.cachedTelemetryEvents[telemetryStreamEvents.Key] = telemetryStreamEvents.ToDictionary(y => y.EventId, y => y);
                }
                catch (ArgumentException ex)
                {
                    if (ex.Message.Contains("An item with the same key has already been added"))
                    {
                        var delete = new List<TelemetryEvent>();
                        // this is a special problem which can only ever happen if there are multiple instances of writer processing the same stream for some reason.
                        var groupedDupes = telemetryStreamEvents.GroupBy(y => y.EventId, y => y).Where(y => y.Count() > 1);
                        foreach (var dupe in groupedDupes)
                        {
                            int CalcScore(TelemetryEvent @event)
                            {
                                int score = 0;
                                if (@event.Name != @event.EventId) score++;
                                if (@event.Description != null) score++;
                                if (@event.Location != "/") score++;
                                if (@event.Level != TelemetryEventLevel.Information) score++; // info is the default, so I consider anything not extra value
                                if (@event.CustomProperties != null) score++;
                                return score;
                            }

                            // take the max score, then last in there. Why last? Well, the hope the order is kept the same as it was read from DB, and in that case a later occurrence
                            // would mean it was added later to the db, preferably being more up to date.
                            var keep = dupe.GroupBy(CalcScore).OrderByDescending(y => y.Key).First().Last();
                            delete.AddRange(dupe.Except(new [] {keep}));
                        }

                        foreach (var telemetryEvent in delete)
                        {
                            //todo mongo stuff
                            //eventRequests.Add(new DeleteOneModel<TelemetryEvent>(Builders<TelemetryEvent>.Filter.Eq(y => y.BsonObjectId, telemetryEvent.BsonObjectId)));
                        }

                        this.cachedTelemetryEvents[telemetryStreamEvents.Key] = telemetryStreamEvents.Select(y => y).Except(delete).ToDictionary(y => y.EventId, y => y);
                        this.logger.LogWarning("Found {0} duplicate events, deleting them. If this is a frequent occurence, then it indicates a problem.", delete.Count);
                    }
                }
            }

            if (loadedStreamsFromDbForEvents != uncachedStreamEventIds.Count)
            {
                // new stream, never had data or event definition maybe
                this.logger.LogTrace("Only found events for {0} out of {1} streams in the db. Loaded in {2:g}", loadedStreamsFromDbForEvents, uncachedStreamEventIds.Count,
                    eventsLoadSw.Elapsed);
            }
            else
            {
                this.logger.LogTrace("Found events for all {0} streams in the db. Loaded in {1:g}", uncachedStreamEventIds.Count, eventsLoadSw.Elapsed);
            }
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> PersistEventGroupChanges(Dictionary<string, PendingStreamUpdates> streamUpdates, List<string> uncachedStreamEventIds)
        {
            var eventGroupRequests = new List<TelemetryEventGroup>();

            await this.CacheGroupsForStreams(uncachedStreamEventIds, eventGroupRequests);

            var eventGroupUpdateCounter = 0;
            var eventGroupCreateCounter = 0;

            var locationLookup =
                new Dictionary<string, Dictionary<string, string>>(); //(streamid, (eventid, path)) This is not cached because if it isn't available means it hasn't changed, so I don't need to update anything

            foreach (var streamUpdate in streamUpdates)
            {
                if (!streamUpdate.Value.UpdatesEventGroups) continue; // Doing this after cache check to avoid unnecessarily looking in the db again for the same stream
                
                if (!this.cachedTelemetryEventGroups.TryGetValue(streamUpdate.Key, out var telemetryEventGroups))
                {
                    telemetryEventGroups = new Dictionary<string, TelemetryEventGroup>();
                    this.cachedTelemetryEventGroups[streamUpdate.Key] = telemetryEventGroups;
                }

                this.ConvertFromSdkGroup(streamUpdate.Key, streamUpdate.Value.EventDefinitions, out var eventGroups, out var eventIdToPath);
                locationLookup[streamUpdate.Key] = eventIdToPath;

                foreach (var incomingGroup in eventGroups)
                {
                    if (!telemetryEventGroups.TryGetValue(incomingGroup.Path, out var telemetryEventGroup))
                    {
                        telemetryEventGroup = incomingGroup;

                        eventGroupRequests.Add(telemetryEventGroup);
                        telemetryEventGroups[incomingGroup.Path] = telemetryEventGroup;
                        eventGroupCreateCounter++;
                        continue;
                    }

                    var updateDefinitions = new List<TelemetryEventGroup>();

                    void UpdateProperty<T>(Expression<Func<TelemetryEventGroup, T>> selector, T newVal, TelemetryEventGroup cachedEvent)
                    {
                        var func = selector.Compile();
                        var oldVal = func(cachedEvent);
                        if (newVal != null && (oldVal == null || !oldVal.Equals(newVal)))
                        {
                            //todo mongo stuff
                            //updateDefinitions.Add(Builders<TelemetryEventGroup>.Update.Set(selector, newVal));
                            var body = selector.Body;
                            if (body is UnaryExpression unaryExpression)
                            {
                                body = unaryExpression.Operand;
                            }

                            var prop = (PropertyInfo) ((MemberExpression) body).Member;
                            prop.SetValue(cachedEvent, newVal, null);
                        }
                    }

                    if (!string.IsNullOrEmpty(incomingGroup.Name)) UpdateProperty(y => y.Name, incomingGroup.Name, telemetryEventGroup);
                    if (!string.IsNullOrEmpty(incomingGroup.Description)) UpdateProperty(y => y.Description, incomingGroup.Description, telemetryEventGroup);
                    if (!string.IsNullOrEmpty(incomingGroup.CustomProperties)) UpdateProperty(y => y.CustomProperties, incomingGroup.CustomProperties, telemetryEventGroup);
                    UpdateProperty(y => y.ChildrenCount, incomingGroup.ChildrenCount, telemetryEventGroup);

                    if (updateDefinitions.Count != 0)
                    {
                        //todo mongo stuff

                        // var eqStream = Builders<TelemetryEventGroup>.Filter.Eq(y => y.StreamId, streamUpdate.Key);
                        // var eqEventId = Builders<TelemetryEventGroup>.Filter.Eq(y => y.Path, telemetryEventGroup.Path);
                        // var filter = Builders<TelemetryEventGroup>.Filter.And(eqStream, eqEventId);
                        // eventGroupRequests.Add(new UpdateOneModel<TelemetryEventGroup>(filter, Builders<TelemetryEventGroup>.Update.Combine(updateDefinitions)));
                        eventGroupUpdateCounter++;
                    }
                }
            }

            if (eventGroupRequests.Count == 0)
            {
                this.logger.LogTrace("None of the changes require stream event group update. Not updating.");
            }
            else
            {
                this.logger.LogTrace("Creating {0} stream event groups and updating {1}.", eventGroupCreateCounter, eventGroupUpdateCounter);
                var groupWriteSw = Stopwatch.StartNew();
                await this.eventGroupRepository.BulkWrite(eventGroupRequests);
                groupWriteSw.Stop();
                this.logger.LogDebug("Created {0} stream event groups and updated {1} in {2:g}.", eventGroupCreateCounter, eventGroupUpdateCounter, groupWriteSw.Elapsed);
            }

            return locationLookup;
        }

        private async Task CacheGroupsForStreams(List<string> uncachedStreamEventIds, List<TelemetryEventGroup> eventGroupRequests)
        {
            var eventGroupsLoadSw = Stopwatch.StartNew();
            var eventGroups = await this.eventGroupRepository.GetAll().Where(y => uncachedStreamEventIds.Contains(y.StreamId)).ToListAsync();
            eventGroupsLoadSw.Stop();
            var loadedStreamsFromDbForGroups = 0;

            foreach (var telemetryStreamEventGroups in eventGroups.GroupBy(y => y.StreamId))
            {
                loadedStreamsFromDbForGroups++;
                try
                {
                    this.cachedTelemetryEventGroups[telemetryStreamEventGroups.Key] = telemetryStreamEventGroups.ToDictionary(y => y.Path, y => y);
                }
                catch (ArgumentException ex)
                {
                    if (ex.Message.Contains("An item with the same key has already been added"))
                    {
                        var delete = new List<TelemetryEventGroup>();
                        // this is a special problem which can only ever happen if there are multiple instances of writer processing the same stream for some reason.
                        var groupedDupes = telemetryStreamEventGroups.GroupBy(y => y.Path, y => y).Where(y => y.Count() > 1);
                        foreach (var dupe in groupedDupes)
                        {
                            int CalcScore(TelemetryEventGroup eventGroup)
                            {
                                int score = 0;
                                if (eventGroup.Description != null) score++;
                                if (eventGroup.Location != "/") score++;
                                if (eventGroup.CustomProperties != null) score++;
                                return score;
                            }

                            // take the max score, then last in there. Why last? Well, the hope the order is kept the same as it was read from DB, and in that case a later occurrence
                            // would mean it was added later to the db, preferably being more up to date.
                            var keep = dupe.GroupBy(CalcScore).OrderByDescending(y => y.Key).First().Last();
                            delete.AddRange(dupe.Except(new []{keep}));
                        }

                        foreach (var telemetryEvent in delete)
                        {
                            //todo mongo stuff
                            
                            // eventGroupRequests.Add(
                            //     new DeleteOneModel<TelemetryEventGroup>(Builders<TelemetryEventGroup>.Filter.Eq(y => y.BsonObjectId, telemetryEvent.BsonObjectId)));
                        }

                        this.cachedTelemetryEventGroups[telemetryStreamEventGroups.Key] = telemetryStreamEventGroups.Select(y => y).Except(delete).ToDictionary(y => y.Path, y => y);
                        this.logger.LogWarning("Found {0} duplicate event groups, deleting them. If this is a frequent occurence, then it indicates a problem.", delete.Count);
                    }
                }
            }

            if (loadedStreamsFromDbForGroups != uncachedStreamEventIds.Count)
            {
                // new stream, never had data or event definition maybe
                this.logger.LogTrace("Only found events groups for {0} out of {1} streams in the db. Loaded in {2:g}", loadedStreamsFromDbForGroups, uncachedStreamEventIds.Count,
                    eventGroupsLoadSw.Elapsed);
            }
            else
            {
                this.logger.LogTrace("Found events groups for all {0} streams in the db. Loaded in {1:g}", uncachedStreamEventIds.Count, eventGroupsLoadSw.Elapsed);
            }
        }

        private void ConvertFromSdkGroup(string streamId, EventDefinitions eventDefinitions, out List<TelemetryEventGroup> groups, out Dictionary<string, string> eventIdToPath)
        {
            var eventIdsToPath = new Dictionary<string, string>();
            var groupsList = new List<TelemetryEventGroup>();

            void ConvertFromSdkGroupHelper(List<EventGroupDefinition> groupDefinitions, TelemetryEventGroup parentGroup, string location)
            {
                foreach (var groupDefinition in groupDefinitions)
                {
                    var path = location + groupDefinition.Name;
                    var group = new TelemetryEventGroup(streamId)
                    {
                        Name = groupDefinition.Name,
                        Description = groupDefinition.Description,
                        CustomProperties = groupDefinition.CustomProperties,
                        Location = location,
                        Path = path
                    };

                    groupsList.Add(@group);

                    if (parentGroup != null)
                    {
                        parentGroup.ChildrenCount += 1;
                    }

                    foreach (var streamGroupEvent in groupDefinition.Events)
                    {
                        if (@group.Path == "/")
                        {
                            eventIdsToPath[streamGroupEvent.Id] = "/";
                        }
                        else
                        {
                            eventIdsToPath[streamGroupEvent.Id] = @group.Path + "/";
                        }
                    }

                    if (groupDefinition.ChildGroups == null || groupDefinition.ChildGroups.Count == 0) continue;
                    var childLocation = path + "/";
                    ConvertFromSdkGroupHelper(groupDefinition.ChildGroups, @group, childLocation);
                }
            }

            foreach (var streamGroupEvent in eventDefinitions.Events)
            {
                eventIdsToPath[streamGroupEvent.Id] = "/";
            }

            if (eventDefinitions.EventGroups != null) ConvertFromSdkGroupHelper(eventDefinitions.EventGroups, null, "/");
            groups = groupsList; // c# limitation, workaround to have this duplicated variable
            eventIdToPath = eventIdsToPath; // c# limitation, workaround to have this duplicated variable
        }

        public void UnCache(string streamId)
        {
            this.cachedTelemetryEvents.TryRemove(streamId, out var _);
            this.cachedTelemetryEventGroups.TryRemove(streamId, out var _);
        }
    }
}