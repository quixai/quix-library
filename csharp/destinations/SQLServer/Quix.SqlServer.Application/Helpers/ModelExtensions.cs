using System;
using System.Collections.Generic;
using Quix.Sdk.Process.Models;
using Quix.SqlServer.Application.Models;
using Quix.SqlServer.Domain.Models;
using Quix.Telemetry.Domain.Metadata.Models;

namespace Quix.SqlServer.Application.Helpers
{
    public static class ModelExtensions
    {
        public static bool HasValues(this ParameterDataRaw data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.NumericValues != null)
            {
                foreach (var nvalues in data.NumericValues)
                {
                    for (var index = 0; index < nvalues.Value.Length; index++)
                    {
                        if (nvalues.Value[index].HasValue) return true;
                    }
                }
            }
            
            if (data.StringValues != null)
            {
                foreach (var nvalues in data.StringValues)
                {
                    for (var index = 0; index < nvalues.Value.Length; index++)
                    {
                        if (nvalues.Value[index] != null) return true;
                    }
                }
            }
            
            if (data.BinaryValues != null)
            {
                foreach (var nvalues in data.BinaryValues)
                {
                    for (var index = 0; index < nvalues.Value.Length; index++)
                    {
                        if (nvalues.Value[index]?.Length > 0) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves all unique parameter identifiers from tData with their type
        /// </summary>
        /// <param name="tData">The telemetry data to retrieve the identifiers from</param>
        /// <returns>The unique parameter identifiers with their type</returns>
        public static IEnumerable<(string Id, ParameterType Type)> GetParameters(this ParameterDataRaw tData)
        {
            if (tData.NumericValues != null)
            {
                foreach (var paramId in tData.NumericValues)
                {
                    yield return (paramId.Key, ParameterType.Numeric);
                }
            }

            if (tData.StringValues != null)
            {
                foreach (var paramId in tData.StringValues)
                {
                    yield return (paramId.Key, ParameterType.String);
                }
            }

            if (tData.BinaryValues != null)
            {
                foreach (var paramId in tData.BinaryValues)
                {
                    yield return (paramId.Key, ParameterType.Binary);
                }
            }
        }

        /// <summary>
        /// Returns the earliest and latest times in the data
        /// </summary>
        /// <returns>The first sample time in unix nanoseconds</returns>
        public static bool TryGetStartEndNanoseconds(this ParameterDataRaw tData, out long start, out long end, DiscardRange discardRange)
        {
            if (tData == null) throw new ArgumentNullException(nameof(tData));
            var min = long.MaxValue;
            var max = long.MinValue;
            var empty = true;
            var discardBefore = Math.Min(discardRange.DiscardBefore - tData.Epoch, discardRange.DiscardBefore); // in case it is long.min, we don't want overflow
            var discardAfter = discardRange.DiscardAfter - tData.Epoch;
            foreach (var ts in tData.Timestamps)
            {
                if (ts < discardBefore) continue;
                if (ts > discardAfter) continue;
                empty = false;
                if (min > ts) min = ts;
                if (max < ts) max = ts;
            }

            start = min + tData.Epoch;
            end = max + tData.Epoch;
            return !empty;
        }

        /// <summary>
        /// Returns whether the stream is in a final status
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>Whether the stream is in a final status</returns>
        public static bool IsFinished(this TelemetryStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return stream.Status == StreamStatus.Closed || stream.Status == StreamStatus.Terminated || stream.Status == StreamStatus.Aborted;
        }
    }
}
