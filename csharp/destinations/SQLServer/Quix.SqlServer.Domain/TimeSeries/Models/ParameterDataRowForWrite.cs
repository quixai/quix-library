using System.Collections.Generic;

namespace Quix.SqlServer.Domain.TimeSeries.Models
{
    public class ParameterDataRowForWrite
    {
        public long Epoch;

        public long Timestamp;

        public int NumericValueCount;
        public int StringValueCount;
        
        
        public string[] NumericParameters;
        public double[] NumericValues;

        public string[] StringParameters;
        public string[] StringValues;

        public List<KeyValuePair<string, string>> TagValues;
    }
}