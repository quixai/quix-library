using System;
using System.Collections.Generic;
using System.Linq;
using Quix.Sdk.Process.Models;
using Quix.SqlServer.Domain.Models;
using Quix.Telemetry.Domain.Metadata.Models;

namespace Quix.SqlServer.Application.Metadata
{
    public class PendingStreamUpdates
    {
        public bool UpdatesStream => this.Status != null || this.Location != null || this.Name != null ||
                                     this.Metadata != null || this.Parents != null || this.TimeOfRecording != null ||
                                     this.Start != null || this.End != null;

        public bool UpdatesParams => this.ParameterDefinitions?.Parameters != null && this.ParameterDefinitions.Parameters.Any() || this.ParameterIds != null && this.ParameterIds.Any();
        public bool UpdatesParamGroups => this.ParameterDefinitions?.ParameterGroups != null && this.ParameterDefinitions.ParameterGroups.Any();
            
            
        public bool UpdatesEvents => this.EventDefinitions?.Events != null && this.EventDefinitions.Events.Any() || this.ActiveEventIds != null && this.ActiveEventIds.Any();
        public bool UpdatesEventGroups => this.EventDefinitions?.EventGroups != null && this.EventDefinitions.EventGroups.Any();

        public StreamStatus? Status;
        public string Location;
        public string Name;
        public Dictionary<string, string> Metadata;
        public List<string> Parents;
        public DateTime? TimeOfRecording;
        public ParameterDefinitions ParameterDefinitions;
        public EventDefinitions EventDefinitions;
        public long? Start;
        public long? End;
        public Dictionary<string, ParameterType> ParameterIds;
        public HashSet<string> ActiveEventIds;
    }
}