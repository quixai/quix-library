using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quix.SqlServer.Domain.Common;
using Quix.SqlServer.Domain.TimeSeries.Models;
using Quix.SqlServer.Domain.TimeSeries.Repositories;
using Quix.SqlServer.Infrastructure.TimeSeries.Models;

namespace Quix.SqlServer.Infrastructure.TimeSeries.Repositories
{
    /// <summary>
    /// Implementation of <see cref="ITimeSeriesWriteRepository"/> for SqlServer
    /// </summary>
    public class SqlServerWriteRepository : ITimeSeriesWriteRepository, IRequiresSetup, IDisposable
    {
        private readonly ILogger<SqlServerWriteRepository> logger;
        private readonly SqlServerConnectionConfiguration SqlServerConfiguration;

        private SqlServerDbConnection SqlServerDbConnection;
        
        private const string ParameterValuesTableName = "PARAMETERVALUES";
        private const string EventValuesTableName = "EVENTVALUES";
        private const string InformationSchema = "PUBLIC";
        private const string NumericParameterColumnFormat = "N_{0}";
        private const string StringParameterColumnFormat = "S_{0}";
        private const string StringEventColumnFormat = "{0}";
        private const string TagFormat = "TAG_{0}";
        private const string TimeStampColumn = "TIMESTAMP";
        private static readonly string StreamIdColumn = string.Format(TagFormat, "STREAMID");
        private const int MaxQueryLength = 950000; // 1mb in theory, but better be safe

        private readonly HashSet<string> parameterColumns = new HashSet<string>();
        private readonly HashSet<string> eventColumns = new HashSet<string>();

        public SqlServerWriteRepository(
            ILogger<SqlServerWriteRepository> logger,
            SqlServerConnectionConfiguration SqlServerConfiguration)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.SqlServerConfiguration = SqlServerConfiguration;
            if (SqlServerConfiguration == null) throw new ArgumentNullException(nameof(SqlServerConfiguration));
        }

        private IDataReader ExecuteSqlServerRead(string sql)
        {
            IDbCommand cmd = SqlServerDbConnection.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteReader();
        }

        private int ExecuteSqlServerNonQuery(string sql)
        {
            try
            {
                IDbCommand cmd = SqlServerDbConnection.CreateCommand();
                cmd.CommandText = sql;
                return cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private bool TableExists(string table)
        {
            var checkForTableSql = $"SELECT EXISTS (SELECT * FROM information_schema.tables WHERE table_schema = '{InformationSchema}' AND table_name = '{table}')";
            var existingTablesReader = ExecuteSqlServerRead(checkForTableSql);
            
            while (existingTablesReader.Read())
            {
                if (existingTablesReader.GetString(0) == "1")
                    return true;
            }

            return false;
        }
        
        private void VerifyTable(string requiredTable, HashSet<string> columns)
        {
            // check if the table exists
            if (!TableExists(requiredTable))
            {
                // if not
                // create the table
                var sqlInsertStatements = new List<string>
                {
                    $"CREATE TABLE {InformationSchema}.{requiredTable} ({TimeStampColumn} BIGINT, {StreamIdColumn} VARCHAR(256))",
                    $"ALTER TABLE {InformationSchema}.{requiredTable} CLUSTER BY (timestamp)"
                };
                
                //var recordsAffected = ExecuteSqlServerNonQuery(sql);
                ExecuteSqlList(sqlInsertStatements);
                
                this.logger.LogInformation($"Table {requiredTable} created");
                
                columns.Add(TimeStampColumn);
                columns.Add(StreamIdColumn);    
            }
            else
            {
                // otherwise
                // get the tables existing column names and add them to the list
                var sql = $"SELECT COLUMN_NAME FROM information_schema.columns WHERE table_name = '{requiredTable}'";
                var existingColumnNameReader = ExecuteSqlServerRead(sql);
                
                while (existingColumnNameReader.Read())
                {
                    parameterColumns.Add(existingColumnNameReader.GetString(0));    
                }
                
                this.logger.LogInformation($"Table {requiredTable} verified");
            }
        }
        
        public Task Setup()
        {
            this.logger.LogDebug("Checking tables...");

            SqlServerDbConnection = new SqlServerDbConnection();
            SqlServerDbConnection.ConnectionString = SqlServerConfiguration.ConnectionString;
            
            SqlServerDbConnection.Open();
            this.logger.LogInformation($"Connected to SqlServer database {SqlServerDbConnection.Database}");

            CheckDbConnection();

            // verify tables exist, if not create them
            VerifyTable(ParameterValuesTableName, parameterColumns);
            VerifyTable(EventValuesTableName, eventColumns);

            this.logger.LogInformation("Tables verified");
            
            return Task.CompletedTask;
        }

        public Task WriteTelemetryData(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData)
        {
            CheckDbConnection();
            
            var sqlInserts = new Dictionary<string, List<string>>();
            
            var uniqueColumns = new Dictionary<string, string>();

            var totalValues = PrepareParameterSqlInserts(streamParameterData, uniqueColumns, sqlInserts);
            this.logger.LogTrace($"Saving {totalValues} parameter values to SqlServer db");

            VerifyColumns(uniqueColumns, parameterColumns, ParameterValuesTableName);

            var sqlInsertStatements = new List<string>();
            foreach (var statementPair in sqlInserts)
            {
                var sb = new StringBuilder();
                sb.Append(statementPair.Key.ToUpper());
                sb.Append(" ");
                foreach (var line in statementPair.Value)
                {
                    if (sb.Length > MaxQueryLength)
                    {
                        sb.Remove(sb.Length - 1, 1);
                        sqlInsertStatements.Add(sb.ToString());
                        
                        sb = new StringBuilder();
                        sb.Append(statementPair.Key.ToUpper());
                        sb.Append(" ");                        
                    }
                    sb.Append(line);
                    sb.Append(',');
                }

                sb.Remove(sb.Length - 1, 1);
                sqlInsertStatements.Add(sb.ToString());
            }

            ExecuteSqlList(sqlInsertStatements);

            this.logger.LogTrace($"Saved {totalValues} parameter values to SqlServer db");
            
            return Task.CompletedTask;
        }

        private void CheckDbConnection()
        {
            if (SqlServerDbConnection.State != ConnectionState.Open)
                throw new Exception("Database connection is not in the 'Open' state");
        }

        private int ExecuteSqlList(List<string> sqlInsertStatements)
        {
            var totalInserted = 0;
            foreach (var sqlInsertStatement in sqlInsertStatements)
            {
                var recordsAffected = ExecuteSqlServerNonQuery(sqlInsertStatement);
                totalInserted += recordsAffected;
                // todo log
            }

            return totalInserted;
        }

        private static int PrepareParameterSqlInserts(IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData, Dictionary<string, string> uniqueColumns, Dictionary<string, List<string>> sqlInserts)
        {
            var totalValues = 0;
            foreach (var streamRows in streamParameterData)
            {
                foreach (var row in streamRows.Value)
                {
                    var numericValueCount = 0;
                    var stringValueCount = 0;

                    var headerSb = new StringBuilder();
                    headerSb.Append($"insert into {ParameterValuesTableName} ({TimeStampColumn},{StreamIdColumn}");

                    var valueSb = new StringBuilder();
                    valueSb.Append($"({row.Epoch + row.Timestamp},'{streamRows.Key.ToUpper()}'");

                    if (row.TagValues != null && row.TagValues.Count > 0)
                    {
                        foreach (var kPair in row.TagValues)
                        {
                            if (string.IsNullOrEmpty(kPair.Value)) continue;
                            var name = string.Format(TagFormat, kPair.Key.ToUpper());
                            if (name.Equals(StreamIdColumn, StringComparison.InvariantCultureIgnoreCase)) continue;

                            valueSb.Append(",");
                            valueSb.Append($"'{kPair.Value}'");
                            
                            headerSb.Append(",");
                            headerSb.Append(name);
                            uniqueColumns[name] = "tag";
                        }
                    }

                    if (row.NumericValueCount > 0)
                    {
                        for (var i = 0; i < row.NumericValueCount; i++)
                        {
                            var name = string.Format(NumericParameterColumnFormat, row.NumericParameters[i]);
                            var value = row.NumericValues[i];
                            if (
                                double.IsNaN(value) ||
                                double.IsInfinity(value) ||
                                double.IsNegativeInfinity(value))
                            {
                                // NaNs or Infinity are ignored for this 
                                continue;
                            }

                            valueSb.Append(",");
                            valueSb.Append(value);

                            headerSb.Append(",");
                            headerSb.Append(name);
                            uniqueColumns[name] = "number";

                            numericValueCount++;
                        }
                    }

                    if (row.StringValueCount > 0)
                    {
                        for (var i = 0; i < row.StringValueCount; i++)
                        {
                            var name = string.Format(StringParameterColumnFormat, row.StringParameters[i]);
                            var value = row.StringValues[i];

                            valueSb.Append(",");
                            valueSb.Append($"'{value}'");

                            headerSb.Append(",");
                            headerSb.Append(name);
                            uniqueColumns[name] = "string";

                            stringValueCount++;
                        }
                    }

                    if (numericValueCount == 0 && stringValueCount == 0) continue; // non persistable values only

                    headerSb.Append(") values");
                    valueSb.Append(")");

                    var header = headerSb.ToString();
                    if (!sqlInserts.TryGetValue(header, out var lines))
                    {
                        lines = new List<string>();
                        sqlInserts[header] = lines;
                    }
                    lines.Add(valueSb.ToString());
                    
                    totalValues += numericValueCount;
                    totalValues += stringValueCount;
                }
            }

            return totalValues;
        }

        private void VerifyColumns(Dictionary<string, string> columnsToHave, HashSet<string> existingColumns, string tableToVerify)
        {
            var columnsToAdd = columnsToHave.Keys.Except(existingColumns, StringComparer.InvariantCultureIgnoreCase).ToList();
            if (columnsToAdd.Count == 0) return;
            List<string> sqlStatements = new List<string>();
            foreach (var col in columnsToAdd)
            {
                switch (columnsToHave[col])
                {
                    case "string":
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}.{tableToVerify} ADD {col} VARCHAR(16777216)");
                        break;
                    case "tag":
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}.{tableToVerify} ADD {col} VARCHAR(512)");
                        break;
                    case "number":
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}.{tableToVerify} ADD {col} FLOAT8");
                        break;
                }
            }

            if (sqlStatements.Count == 0) return;
            // this is really just safe coding, not likely to ever happen

            var recordsAffected = ExecuteSqlList(sqlStatements);
            // todo log
            
        }

        public Task WriteTelemetryEvent(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<EventDataRow>>> streamEventData)
        {
            var sqlInserts = new Dictionary<string, List<string>>();
            
            var uniqueColumns = new Dictionary<string, string>();

            var totalValues = PrepareEventSqlInserts(streamEventData, uniqueColumns, sqlInserts);
            this.logger.LogTrace($"Saving {totalValues} event values to SqlServer db");

            VerifyColumns(uniqueColumns, eventColumns, EventValuesTableName);

            var sqlInsertStatements = new List<string>();
            foreach (var statementPair in sqlInserts)
            {
                var sb = new StringBuilder();
                sb.Append(statementPair.Key.ToUpper());
                sb.Append(" ");
                foreach (var line in statementPair.Value)
                {
                    if (sb.Length > MaxQueryLength)
                    {
                        sb.Remove(sb.Length - 1, 1);
                        sqlInsertStatements.Add(sb.ToString());
                        
                        sb = new StringBuilder();
                        sb.Append(statementPair.Key.ToUpper());
                        sb.Append(" ");                        
                    }
                    sb.Append(line);
                    sb.Append(',');
                }

                sb.Remove(sb.Length - 1, 1);
                sqlInsertStatements.Add(sb.ToString());
            }
            
            ExecuteSqlList(sqlInsertStatements);
            
            this.logger.LogTrace($"Saved {totalValues} event values to SqlServer db");
            
            return Task.CompletedTask;
        }
        
        private static int PrepareEventSqlInserts(IEnumerable<KeyValuePair<string, IEnumerable<EventDataRow>>> streamEventData, Dictionary<string, string> uniqueColumns, Dictionary<string, List<string>> sqlInserts)
        {
            var totalValues = 0;
            foreach (var streamRows in streamEventData)
            {
                foreach (var row in streamRows.Value)
                {
                    var headerSb = new StringBuilder();
                    headerSb.Append($"insert into {InformationSchema}.{EventValuesTableName} ({TimeStampColumn},{StreamIdColumn}");

                    var valueSb = new StringBuilder();
                    valueSb.Append($"({row.Timestamp},'{streamRows.Key.ToUpper()}'");

                    if (row.TagValues != null && row.TagValues.Count > 0)
                    {
                        foreach(var kPair in row.TagValues)
                        {
                            if (string.IsNullOrEmpty(kPair.Value)) continue;
                            var name = string.Format(TagFormat, kPair.Key.ToUpper());
                            if (name.Equals(StreamIdColumn, StringComparison.InvariantCultureIgnoreCase)) continue;

                            valueSb.Append(",");
                            valueSb.Append($"'{kPair.Value}'");
                            
                            headerSb.Append(",");
                            headerSb.Append(name);
                            uniqueColumns[name] = "tag";
                        }
                    }

                    var eventColumnName = string.Format(StringEventColumnFormat, row.EventId);
                    var value = row.Value;

                    valueSb.Append(",");
                    valueSb.Append($"'{value}'");

                    headerSb.Append(",");
                    headerSb.Append(eventColumnName);
                    uniqueColumns[eventColumnName] = "string"; // will matter because of column verification

                    headerSb.Append(") values");
                    valueSb.Append(")");

                    var header = headerSb.ToString();
                    if (!sqlInserts.TryGetValue(header, out var lines))
                    {
                        lines = new List<string>();
                        sqlInserts[header] = lines;
                    }
                    lines.Add(valueSb.ToString());

                    totalValues++;
                }
            }

            return totalValues;
        }

        public void Dispose()
        {
            SqlServerDbConnection.Close();
        }
    }
}
