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
using Quix.SqlServer.Application.Metadata;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;
using Quix.SqlServer.Infrastructure.Shared.Extensions;
using Quix.Telemetry.Domain.Metadata.Models;
using Quix.Telemetry.Domain.Metadata.Repositories;

namespace Quix.TelemetryWriter.Application.Metadata
{
    /*
     * NOTE:
     * This class is extremely similar to EventPersistingService for all code except that directly
     * deals with logic/properties unique to parameters. Lifting the common code is quite difficult without
     * adding excessive complexity.
     */
    
    public interface IParameterPersistingService
    {
        Task Persist(Dictionary<string, PendingStreamUpdates> streamUpdates);
        void UnCache(string streamId);
    }

    public class ParameterPersistingService : IParameterPersistingService
    {
        private readonly ILogger<ParameterPersistingService> logger;
        private readonly IParameterRepository parameterRepository;
        private readonly IParameterGroupRepository parameterGroupRepository;
        
        /// <summary>
        /// (streamId - (parameterId - Parameter))
        /// Needs to be concurrent due to <see cref="UnCache"/>
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, TelemetryParameter>> cachedTelemetryParameters = new ConcurrentDictionary<string, Dictionary<string, TelemetryParameter>>();
        
        /// <summary>
        /// (streamId - (parameterGroupPath - ParameterGroup))
        /// Needs to be concurrent due to <see cref="UnCache"/>
        /// </summary>
        private readonly ConcurrentDictionary<string, Dictionary<string, TelemetryParameterGroup>> cachedTelemetryParameterGroups = new ConcurrentDictionary<string, Dictionary<string, TelemetryParameterGroup>>();

        public ParameterPersistingService(ILogger<ParameterPersistingService> logger, IParameterRepository parameterRepository, IParameterGroupRepository parameterGroupRepository)
        {
            this.logger = logger;
            this.parameterRepository = parameterRepository;
            this.parameterGroupRepository = parameterGroupRepository;
        }
        
        public async Task Persist(Dictionary<string, PendingStreamUpdates> streamUpdates)
        {
            var uncachedStreamParameterIds = this.SelectUncachedStreamsIds(streamUpdates);
            
            var parameterLocationLookup = await this.PersistParameterGroupChanges(streamUpdates, uncachedStreamParameterIds);

            await this.PersistParameterChanges(streamUpdates, uncachedStreamParameterIds, parameterLocationLookup);
        }
        
        private List<string> SelectUncachedStreamsIds(Dictionary<string, PendingStreamUpdates> streamUpdates)
        {
            // could do except with groups too, but they have the same key set
            return streamUpdates.Where(y => y.Value.UpdatesParams || y.Value.UpdatesParamGroups).Select(y => y.Key)
                .Except(this.cachedTelemetryParameters.Keys).ToList();
        }

        private async Task PersistParameterChanges(Dictionary<string, PendingStreamUpdates> streamUpdates, List<string> uncachedStreamParameterIds, Dictionary<string, Dictionary<string, string>> parameterLocationLookup)
        {
            
            var paramRequests = new List<TelemetryParameter>();
            
            await this.CacheParametersForStreams(uncachedStreamParameterIds, paramRequests);

            
            var paramUpdateCounter = 0;
            var paramCreateCounter = 0;
            foreach (var streamUpdate in streamUpdates)
            {
                if (!streamUpdate.Value.UpdatesParams) continue; // Doing this after cache check to avoid unnecessarily looking in the db again for the same stream

                if (!this.cachedTelemetryParameters.TryGetValue(streamUpdate.Key, out var telemetryParameters))
                {
                    telemetryParameters = new Dictionary<string, TelemetryParameter>();
                    this.cachedTelemetryParameters[streamUpdate.Key] = telemetryParameters;
                }

                var hasStreamParamLocations = parameterLocationLookup.TryGetValue(streamUpdate.Key, out var streamParamLocations);

                bool TryLookupLocation(string paramId, out string location)
                {
                    location = null;
                    if (!hasStreamParamLocations) return false;
                    if (!streamParamLocations.TryGetValue(paramId, out var path)) return false;
                    location = path;
                    return true;
                }

                // Add/Update parameters that arrived via definitions
                if (streamUpdate.Value.ParameterDefinitions?.Parameters != null)
                {
                    foreach (var parameterDefinition in EnumerateForDefinitions(streamUpdate.Value))
                    {
                        if (!telemetryParameters.TryGetValue(parameterDefinition.Id, out var telemetryParameter))
                        {
                            telemetryParameter = new TelemetryParameter(streamUpdate.Key)
                            {
                                ParameterId = parameterDefinition.Id,
                                Name = string.IsNullOrWhiteSpace(parameterDefinition.Name) ? parameterDefinition.Id : parameterDefinition.Name,
                                Description = parameterDefinition.Description,
                                Location = "/",
                                Type = ParameterType.Unknown,
                                Format = parameterDefinition.Format,
                                Unit = parameterDefinition.Unit,
                                CustomProperties = parameterDefinition.CustomProperties,
                                MaximumValue = parameterDefinition.MaximumValue,
                                MinimumValue = parameterDefinition.MinimumValue
                            };


                            if (TryLookupLocation(telemetryParameter.ParameterId, out var location))
                            {
                                telemetryParameter.Location = location;
                            }

                            if (telemetryParameter.Type == ParameterType.Unknown && streamUpdate.Value.ParameterIds != null)
                            {
                                if (streamUpdate.Value.ParameterIds.TryGetValue(parameterDefinition.Id, out var type))
                                {
                                    telemetryParameter.Type = type;
                                }
                            }

                            paramRequests.Add(telemetryParameter);
                            telemetryParameters[parameterDefinition.Id] = telemetryParameter;
                            paramCreateCounter++;
                            continue;
                        }

                        var updateDefinitions = new List<TelemetryParameter>();

                        void UpdateProperty<T>(Expression<Func<TelemetryParameter, T>> selector, T newVal, TelemetryParameter cachedParam)
                        {
                            var func = selector.Compile();
                            var oldVal = func(cachedParam);
                            if (newVal == null || (oldVal != null && oldVal.Equals(newVal))) return;
                            //TODO this is a mongo thing
                            //updateDefinitions.Add(Builders<TelemetryParameter>.Update.Set(selector, newVal));
                            var body = selector.Body;
                            if (body is UnaryExpression unaryExpression)
                            {
                                body = unaryExpression.Operand;
                            }

                            var prop = (PropertyInfo) ((MemberExpression) body).Member;
                            prop.SetValue(cachedParam, newVal, null);
                        }

                        if (telemetryParameter.Name == null || !string.IsNullOrEmpty(parameterDefinition.Name))
                        {
                            var nameToUse = string.IsNullOrWhiteSpace(parameterDefinition.Name) ? parameterDefinition.Id : parameterDefinition.Name;
                            UpdateProperty(y => y.Name, nameToUse, telemetryParameter);
                        }

                        if (!string.IsNullOrEmpty(parameterDefinition.Description)) UpdateProperty(y => y.Description, parameterDefinition.Description, telemetryParameter);
                        if (!string.IsNullOrEmpty(parameterDefinition.Format)) UpdateProperty(y => y.Format, parameterDefinition.Format, telemetryParameter);
                        if (!string.IsNullOrEmpty(parameterDefinition.Unit)) UpdateProperty(y => y.Unit, parameterDefinition.Unit, telemetryParameter);
                        if (!string.IsNullOrEmpty(parameterDefinition.CustomProperties)) UpdateProperty(y => y.CustomProperties, parameterDefinition.CustomProperties, telemetryParameter);
                        UpdateProperty(y => y.MaximumValue, parameterDefinition.MaximumValue, telemetryParameter);
                        UpdateProperty(y => y.MinimumValue, parameterDefinition.MinimumValue, telemetryParameter);
                        if (telemetryParameter.Type == ParameterType.Unknown)
                        {
                            if (streamUpdate.Value.ParameterIds != null)
                            {
                                if (streamUpdate.Value.ParameterIds.TryGetValue(parameterDefinition.Id, out var type) && type != ParameterType.Unknown)
                                {
                                    UpdateProperty(y => y.Type, type, telemetryParameter);
                                }
                            }
                        }

                        if (TryLookupLocation(telemetryParameter.ParameterId, out var locationToUpdateTo))
                        {
                            UpdateProperty(y => y.Location, locationToUpdateTo, telemetryParameter);
                        }

                        if (updateDefinitions.Count == 0) continue;
                        
                        // TODO mongo stuff
                        // var eqStream = Builders<TelemetryParameter>.Filter.Eq(y => y.StreamId, streamUpdate.Key);
                        // var eqParamId = Builders<TelemetryParameter>.Filter.Eq(y => y.ParameterId, telemetryParameter.ParameterId);
                        // var filter = Builders<TelemetryParameter>.Filter.And(eqStream, eqParamId);
                        // paramRequests.Add(new UpdateOneModel<TelemetryParameter>(filter, Builders<TelemetryParameter>.Update.Combine(updateDefinitions)));
                        paramUpdateCounter++;
                    }
                }

                // Add parameters for which no definition came through yet, but value did.
                if (streamUpdate.Value.ParameterIds != null)
                {
                    foreach (var paramIdToTypePair in streamUpdate.Value.ParameterIds)
                    {
                        if (!telemetryParameters.TryGetValue(paramIdToTypePair.Key, out var telemetryParameter))
                        {
                            telemetryParameter = new TelemetryParameter(streamUpdate.Key)
                            {
                                ParameterId = paramIdToTypePair.Key,
                                Name = paramIdToTypePair.Key,
                                Location = "/",
                                Type = paramIdToTypePair.Value
                            };

                            paramRequests.Add(telemetryParameter);
                            telemetryParameters[paramIdToTypePair.Key] = telemetryParameter;
                            paramCreateCounter++;
                            continue;
                        }

                        if (telemetryParameter.Type != ParameterType.Unknown) continue; // already not unknown
                        if (paramIdToTypePair.Value == ParameterType.Unknown) continue; // value is unknown ?? likely not possible, but better safe
                        
                        //TODO mongo stuff
                        // var eqStream = Builders<TelemetryParameter>.Filter.Eq(y => y.StreamId, streamUpdate.Key);
                        // var eqParamId = Builders<TelemetryParameter>.Filter.Eq(y => y.ParameterId, telemetryParameter.ParameterId);
                        // var filter = Builders<TelemetryParameter>.Filter.And(eqStream, eqParamId);
                        // paramRequests.Add(new UpdateOneModel<TelemetryParameter>(filter, Builders<TelemetryParameter>.Update.Set(y => y.Type, paramIdToTypePair.Value)));
                        paramUpdateCounter++;
                    }

                    // TODO check for parameters to delete ? These could be parameters that were previously added but never had any value come through and no longer show up in the definition... super edge case, so not bothering for now
                }
            }

            if (paramRequests.Count == 0)
            {
                this.logger.LogTrace("None of the changes require stream parameter update. Not updating.");
                return;
            }

            this.logger.LogTrace("Creating {0} stream parameters and updating {1}.", paramCreateCounter, paramUpdateCounter);
            var sw = Stopwatch.StartNew();
            await this.parameterRepository.BulkWrite(paramRequests);
            sw.Stop();
            this.logger.LogDebug("Created {0} stream parameters and updated {1} in {2:g}.", paramCreateCounter, paramUpdateCounter, sw.Elapsed);
        }

        static IEnumerable<ParameterDefinition> EnumerateForDefinitions(PendingStreamUpdates streamUpdate)
        {
            foreach (var parameterDefinition in streamUpdate.ParameterDefinitions.Parameters)
            {
                yield return parameterDefinition;
            }

            IEnumerable<ParameterDefinition> EnumerateGroupForDefinitions(ParameterGroupDefinition groupDefinition)
            {
                if (groupDefinition.Parameters != null)
                {
                    foreach (var definitionParameter in groupDefinition.Parameters)
                    {
                        yield return definitionParameter;
                    }
                }

                if (groupDefinition.ChildGroups != null)
                {
                    foreach (var groupDefinitionChildGroup in groupDefinition.ChildGroups)
                    {
                        foreach (var parameterDefinition in EnumerateGroupForDefinitions(groupDefinitionChildGroup))
                        {
                            yield return parameterDefinition;
                        }
                    }
                }
            }

            if (streamUpdate.ParameterDefinitions.ParameterGroups == null) yield break;
            foreach (var groupDefinitionChildGroup in streamUpdate.ParameterDefinitions.ParameterGroups)
            {
                foreach (var parameterDefinition in EnumerateGroupForDefinitions(groupDefinitionChildGroup))
                {
                    yield return parameterDefinition;
                }
            }
        }

        private async Task CacheParametersForStreams(List<string> uncachedStreamParameterIds, List<TelemetryParameter> paramRequests)
        {
            var parametersLoadSw = Stopwatch.StartNew();
            var parameters = await this.parameterRepository.GetAll().Where(y => uncachedStreamParameterIds.Contains(y.StreamId)).ToListAsync();
            parametersLoadSw.Stop();
            var loadedStreamsFromDbForParams = 0;

            foreach (var telemetryStreamParams in parameters.GroupBy(y => y.StreamId))
            {
                loadedStreamsFromDbForParams++;
                try
                {
                    this.cachedTelemetryParameters[telemetryStreamParams.Key] = telemetryStreamParams.ToDictionary(y => y.ParameterId, y => y);
                }
                catch (ArgumentException ex)
                {
                    if (ex.Message.Contains("An item with the same key has already been added"))
                    {
                        var delete = new List<TelemetryParameter>();
                        // this is a special problem which can only ever happen if there are multiple instances of writer processing the same stream for some reason.
                        var groupedDupes = telemetryStreamParams.GroupBy(y => y.ParameterId, y => y).Where(y => y.Count() > 1);
                        foreach (var dupe in groupedDupes)
                        {
                            int CalcScore(TelemetryParameter parameter)
                            {
                                int score = 0;
                                if (parameter.Name != parameter.ParameterId) score++;
                                if (parameter.Description != null) score++;
                                if (parameter.Location != "/") score++;
                                if (parameter.Unit != null) score++;
                                if (parameter.CustomProperties != null) score++;
                                if (parameter.Format != null) score++;
                                if (parameter.Type != ParameterType.Unknown) score++;
                                return score;
                            }

                            // take the max score, then last in there. Why last? Well, the hope the order is kept the same as it was read from DB, and in that case a later occurrence
                            // would mean it was added later to the db, preferably being more up to date.
                            var keep = dupe.GroupBy(CalcScore).OrderByDescending(y => y.Key).First().Last();
                            delete.AddRange(dupe.Except( new [] {keep}));
                        }

                        foreach (var telemetryParameter in delete)
                        {
                            //todo mongo stuff
                            //paramRequests.Add(new DeleteOneModel<TelemetryParameter>(Builders<TelemetryParameter>.Filter.Eq(y => y.BsonObjectId, telemetryParameter.BsonObjectId)));
                        }

                        this.cachedTelemetryParameters[telemetryStreamParams.Key] = telemetryStreamParams.Select(y => y).Except(delete).ToDictionary(y => y.ParameterId, y => y);
                        this.logger.LogWarning("Found {0} duplicate parameters, deleting them. If this is a frequent occurence, then it indicates a problem.", delete.Count);
                    }
                }
            }

            if (loadedStreamsFromDbForParams != uncachedStreamParameterIds.Count)
            {
                // new stream, never had data or parameter definition maybe
                this.logger.LogTrace("Only found parameters for {0} out of {1} streams in the db. Loaded in {2:g}", loadedStreamsFromDbForParams, uncachedStreamParameterIds.Count,
                    parametersLoadSw.Elapsed);
            }
            else
            {
                this.logger.LogTrace("Found parameters for all {0} streams in the db. Loaded in {1:g}", uncachedStreamParameterIds.Count, parametersLoadSw.Elapsed);
            }
        }

        private async Task<Dictionary<string, Dictionary<string, string>>> PersistParameterGroupChanges(Dictionary<string, PendingStreamUpdates> streamUpdates, List<string> uncachedStreamParameterIds)
        {
            var paramGroupRequests = new List<TelemetryParameterGroup>();

            await this.CacheGroupsForStreams(uncachedStreamParameterIds, paramGroupRequests);

            var paramGroupUpdateCounter = 0;
            var paramGroupCreateCounter = 0;

            var locationLookup =
                new Dictionary<string, Dictionary<string, string>>(); //(streamid, (paramid, path)) This is not cached because if it isn't available means it hasn't changed, so I don't need to update anything

            foreach (var streamUpdate in streamUpdates)
            {
                if (!streamUpdate.Value.UpdatesParamGroups) continue; // Doing this after cache check to avoid unnecessarily looking in the db again for the same stream

                if (!this.cachedTelemetryParameterGroups.TryGetValue(streamUpdate.Key, out var telemetryParameterGroups))
                {
                    telemetryParameterGroups = new Dictionary<string, TelemetryParameterGroup>();
                    this.cachedTelemetryParameterGroups[streamUpdate.Key] = telemetryParameterGroups;
                }

                this.ConvertFromSdkGroup(streamUpdate.Key, streamUpdate.Value.ParameterDefinitions, out var parameterGroups, out var paramIdToPath);
                locationLookup[streamUpdate.Key] = paramIdToPath;

                foreach (var incomingGroup in parameterGroups)
                {
                    if (!telemetryParameterGroups.TryGetValue(incomingGroup.Path, out var telemetryParameterGroup))
                    {
                        telemetryParameterGroup = incomingGroup;

                        paramGroupRequests.Add(telemetryParameterGroup);
                        telemetryParameterGroups[incomingGroup.Path] = telemetryParameterGroup;
                        paramGroupCreateCounter++;
                        continue;
                    }

                    var updateDefinitions = new List<TelemetryParameterGroup>();

                    void UpdateProperty<T>(Expression<Func<TelemetryParameterGroup, T>> selector, T newVal, TelemetryParameterGroup cachedParam)
                    {
                        var func = selector.Compile();
                        var oldVal = func(cachedParam);
                        if (newVal != null && (oldVal == null || !oldVal.Equals(newVal)))
                        {
                            //todo mongo stuff
                            //updateDefinitions.Add(Builders<TelemetryParameterGroup>.Update.Set(selector, newVal));
                            var body = selector.Body;
                            if (body is UnaryExpression unaryExpression)
                            {
                                body = unaryExpression.Operand;
                            }

                            var prop = (PropertyInfo) ((MemberExpression) body).Member;
                            prop.SetValue(cachedParam, newVal, null);
                        }
                    }

                    if (!string.IsNullOrEmpty(incomingGroup.Name)) UpdateProperty(y => y.Name, incomingGroup.Name, telemetryParameterGroup);
                    if (!string.IsNullOrEmpty(incomingGroup.Description)) UpdateProperty(y => y.Description, incomingGroup.Description, telemetryParameterGroup);
                    if (!string.IsNullOrEmpty(incomingGroup.CustomProperties)) UpdateProperty(y => y.CustomProperties, incomingGroup.CustomProperties, telemetryParameterGroup);
                    UpdateProperty(y => y.ChildrenCount, incomingGroup.ChildrenCount, telemetryParameterGroup);

                    if (updateDefinitions.Count != 0)
                    {
                        //todo mongo stuff
                        // var eqStream = Builders<TelemetryParameterGroup>.Filter.Eq(y => y.StreamId, streamUpdate.Key);
                        // var eqParamId = Builders<TelemetryParameterGroup>.Filter.Eq(y => y.Path, telemetryParameterGroup.Path);
                        // var filter = Builders<TelemetryParameterGroup>.Filter.And(eqStream, eqParamId);
                        // paramGroupRequests.Add(new UpdateOneModel<TelemetryParameterGroup>(filter, Builders<TelemetryParameterGroup>.Update.Combine(updateDefinitions)));
                        paramGroupUpdateCounter++;
                    }
                }
            }

            if (paramGroupRequests.Count == 0)
            {
                this.logger.LogTrace("None of the changes require stream parameter group update. Not updating.");
            }
            else
            {
                this.logger.LogTrace("Creating {0} stream parameter groups and updating {1}.", paramGroupCreateCounter, paramGroupUpdateCounter);
                var groupWriteSw = Stopwatch.StartNew();
                await this.parameterGroupRepository.BulkWrite(paramGroupRequests);
                groupWriteSw.Stop();
                this.logger.LogDebug("Created {0} stream parameter groups and updated {1} in {2:g}.", paramGroupCreateCounter, paramGroupUpdateCounter, groupWriteSw.Elapsed);
            }

            return locationLookup;
        }

        private async Task CacheGroupsForStreams(List<string> uncachedStreamParameterIds, List<TelemetryParameterGroup> paramGroupRequests)
        {
            var parameterGroupsLoadSw = Stopwatch.StartNew();
            var parameterGroups = await this.parameterGroupRepository.GetAll().Where(y => uncachedStreamParameterIds.Contains(y.StreamId)).ToListAsync();
            parameterGroupsLoadSw.Stop();
            var loadedStreamsFromDbForGroups = 0;

            foreach (var telemetryStreamParamGroups in parameterGroups.GroupBy(y => y.StreamId))
            {
                loadedStreamsFromDbForGroups++;
                try
                {
                    this.cachedTelemetryParameterGroups[telemetryStreamParamGroups.Key] = telemetryStreamParamGroups.ToDictionary(y => y.Path, y => y);
                }
                catch (ArgumentException ex)
                {
                    if (ex.Message.Contains("An item with the same key has already been added"))
                    {
                        var delete = new List<TelemetryParameterGroup>();
                        // this is a special problem which can only ever happen if there are multiple instances of writer processing the same stream for some reason.
                        var groupedDupes = telemetryStreamParamGroups.GroupBy(y => y.Path, y => y).Where(y => y.Count() > 1);
                        foreach (var dupe in groupedDupes)
                        {
                            int CalcScore(TelemetryParameterGroup parameterGroup)
                            {
                                int score = 0;
                                if (parameterGroup.Description != null) score++;
                                if (parameterGroup.Location != "/") score++;
                                if (parameterGroup.CustomProperties != null) score++;
                                return score;
                            }

                            // take the max score, then last in there. Why last? Well, the hope the order is kept the same as it was read from DB, and in that case a later occurrence
                            // would mean it was added later to the db, preferably being more up to date.
                            var keep = dupe.GroupBy(CalcScore).OrderByDescending(y => y.Key).First().Last();
                            delete.AddRange(dupe.Except(new []{keep}));
                        }

                        foreach (var telemetryParameter in delete)
                        {
                            //todo mongo stuff
                            // paramGroupRequests.Add(
                            //     new DeleteOneModel<TelemetryParameterGroup>(Builders<TelemetryParameterGroup>.Filter.Eq(y => y.BsonObjectId, telemetryParameter.BsonObjectId)));
                        }

                        this.cachedTelemetryParameterGroups[telemetryStreamParamGroups.Key] = telemetryStreamParamGroups.Select(y => y).Except(delete).ToDictionary(y => y.Path, y => y);
                        this.logger.LogWarning("Found {0} duplicate parameter groups, deleting them. If this is a frequent occurence, then it indicates a problem.", delete.Count);
                    }
                }
            }

            if (loadedStreamsFromDbForGroups != uncachedStreamParameterIds.Count)
            {
                // new stream, never had data or parameter definition maybe
                this.logger.LogTrace("Only found parameters groups for {0} out of {1} streams in the db. Loaded in {2:g}", loadedStreamsFromDbForGroups, uncachedStreamParameterIds.Count,
                    parameterGroupsLoadSw.Elapsed);
            }
            else
            {
                this.logger.LogTrace("Found parameters groups for all {0} streams in the db. Loaded in {1:g}", uncachedStreamParameterIds.Count, parameterGroupsLoadSw.Elapsed);
            }
        }

        private void ConvertFromSdkGroup(string streamId, ParameterDefinitions parameterDefinitions, out List<TelemetryParameterGroup> groups, out Dictionary<string, string> paramIdToPath)
        {
            var paramIdsToPath = new Dictionary<string, string>();
            var groupsList = new List<TelemetryParameterGroup>();

            void ConvertFromSdkGroupHelper(List<ParameterGroupDefinition> groupDefinitions, TelemetryParameterGroup parentGroup, string location)
            {
                foreach (var groupDefinition in groupDefinitions)
                {
                    var path = location + groupDefinition.Name;
                    var group = new TelemetryParameterGroup(streamId)
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

                    foreach (var streamGroupParameter in groupDefinition.Parameters)
                    {
                        if (@group.Path == "/")
                        {
                            paramIdsToPath[streamGroupParameter.Id] = "/";
                        }
                        else
                        {
                            paramIdsToPath[streamGroupParameter.Id] = @group.Path + "/";
                        }
                    }

                    if (groupDefinition.ChildGroups == null || groupDefinition.ChildGroups.Count == 0) continue;
                    var childLocation = path + "/";
                    ConvertFromSdkGroupHelper(groupDefinition.ChildGroups, @group, childLocation);
                }
            }

            foreach (var streamGroupParameter in parameterDefinitions.Parameters)
            {
                paramIdsToPath[streamGroupParameter.Id] = "/";
            }

            if (parameterDefinitions.ParameterGroups != null) ConvertFromSdkGroupHelper(parameterDefinitions.ParameterGroups, null, "/");
            groups = groupsList; // c# limitation, workaround to have this duplicated variable
            paramIdToPath = paramIdsToPath; // c# limitation, workaround to have this duplicated variable
        }

        public void UnCache(string streamId)
        {
            this.cachedTelemetryParameters.TryRemove(streamId, out var _);
            this.cachedTelemetryParameterGroups.TryRemove(streamId, out var _);
        }
    }
}