﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.TimeSeries.Models;
using Quix.Snowflake.Domain.TimeSeries.Repositories;
using Quix.Snowflake.Infrastructure.Shared;
using Snowflake.Data.Client;

namespace Quix.Snowflake.Infrastructure.TimeSeries.Repositories
{
    /// <summary>
    /// Implementation of <see cref="ITimeSeriesWriteRepository"/> for Snowflake
    /// </summary>
    public class TimeSeriesWriteRepository : ITimeSeriesWriteRepository, IDisposable
    {
        private readonly ILogger<TimeSeriesWriteRepository> logger;
        private readonly IDbConnection dbConnection;
        
        private const string ParameterValuesTableName = "PARAMETERVALUES";
        private const string EventValuesTableName = "EVENTVALUES";
        private const string InformationSchema = "dbo";
        private const string NumericParameterColumnFormat = "N_{0}";
        private const string StringParameterColumnFormat = "S_{0}";
        private const string StringEventColumnFormat = "{0}";
        private const string TagFormat = "TAG_{0}";
        private const string TimeStampColumn = "TIMESTAMP";
        private static readonly string StreamIdColumn = string.Format(TagFormat, "STREAMID");
        private const int MaxQueryByteSize = 512*1024 * 8 - 1024*64; // 1/2 MB, -64 KB for safety margin

        private readonly HashSet<string> parameterColumns = new HashSet<string>();
        private readonly HashSet<string> eventColumns = new HashSet<string>();

        public TimeSeriesWriteRepository(
            ILoggerFactory loggerFactory,
            IDbConnection dbConnection)
        {
            this.logger = loggerFactory.CreateLogger<TimeSeriesWriteRepository>();
            this.dbConnection = dbConnection;
            if (dbConnection == null) throw new ArgumentNullException(nameof(dbConnection));
            Initialize();
        }

        public void Dispose()
        {
            dbConnection.Close();
        }

        private bool TableExists(string table)
        {
            var checkForTableSql = $"SELECT coalesce((SELECT '1' FROM information_schema.tables WHERE table_schema = '{InformationSchema}' AND table_name = '{table}'), '0')";
            var exists = false;
            dbConnection.QuerySnowflake(checkForTableSql, existingTablesReader =>
            {
                while (existingTablesReader.Read())
                {
                    if (existingTablesReader.GetString(0) != "1") continue;
                    exists = true;
                    return;
                }
            });

            return exists;
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
                    $"CREATE TABLE {InformationSchema}{(InformationSchema != "" ? "." : "")}{requiredTable} ({TimeStampColumn} BIGINT, {StreamIdColumn} VARCHAR(256))",
                    //$"ALTER TABLE {InformationSchema}.{requiredTable} CLUSTER BY (timestamp)" // not clustering for now, as timestamp at nanosec precision introduces bad clustering
                };
                
                //var recordsAffected = ExecuteSnowFlakeNonQuery(sql);
                ExecuteStatements(sqlInsertStatements);
                
                this.logger.LogInformation($"Table {requiredTable} created");
                
                columns.Add(TimeStampColumn);
                columns.Add(StreamIdColumn);    
            }
            else
            {
                // otherwise
                // get the tables existing column names and add them to the list
                var sql = $"SELECT COLUMN_NAME FROM information_schema.columns WHERE table_name = '{requiredTable}'";
                dbConnection.QuerySnowflake(sql, existingColumnNameReader =>
                {
                    while (existingColumnNameReader.Read())
                    {
                        columns.Add(existingColumnNameReader.GetString(0));
                    }
                });
                
                this.logger.LogInformation($"Table {requiredTable} verified");
            }
        }
        
        private void Initialize()
        {
            this.logger.LogDebug("Checking tables...");
            
            CheckDbConnection();

            // verify tables exist, if not create them
            VerifyTable(ParameterValuesTableName, parameterColumns);
            VerifyTable(EventValuesTableName, eventColumns);

            this.logger.LogInformation("Tables verified");
        }

        public Task WriteTelemetryData(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData)
        {
            CheckDbConnection();
            
            var sqlInserts = new Dictionary<string, List<string>>();
            
            var uniqueColumns = new Dictionary<string, string>();

            var totalValues = PrepareParameterSqlInserts(streamParameterData, uniqueColumns, sqlInserts);
            this.logger.LogTrace($"Saving {totalValues} parameter values to Snowflake db");

            VerifyColumns(uniqueColumns, parameterColumns, ParameterValuesTableName);

            var i = sqlInserts.Take(1000);

            while (i.Any())
            {
                
                
                ExecuteStatements(i);
                i = sqlInserts.Take(1000);
            }
            
            this.logger.LogTrace($"Saved {totalValues} parameter values to Snowflake db");
            
            return Task.CompletedTask;
        }
        
        private void CheckDbConnection()
        {
            if (dbConnection.State != ConnectionState.Open)
                throw new Exception("Database connection is not in the 'Open' state");
        }

        private static int PrepareParameterSqlInserts(
            IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData, 
            Dictionary<string, string> uniqueColumns, 
            Dictionary<string, List<string>> sqlInserts)
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
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}{(InformationSchema != "" ? "." : "")}{tableToVerify} ADD {col} VARCHAR(MAX)");
                        break;
                    case "tag":
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}{(InformationSchema != "" ? "." : "")}{tableToVerify} ADD {col} VARCHAR(512)");
                        break;
                    case "number":
                        if (existingColumns.Add(col)) sqlStatements.Add($"ALTER TABLE {InformationSchema}{(InformationSchema != "" ? "." : "")}{tableToVerify} ADD {col} decimal (38, 10)"); //38 max, 10dp's
                        break;
                }
                
                if (sqlStatements.Count == 1000)
                {
                    ExecuteStatements(sqlStatements);
                    sqlStatements = new List<string>();
                }
            }

            if (sqlStatements.Count == 0) return;
            // this is really just safe coding, not likely to ever happen

            ExecuteStatements(sqlStatements);
            // todo log
            
        }

        public Task WriteTelemetryEvent(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<EventDataRow>>> streamEventData)
        {
            var sqlInserts = new Dictionary<string, List<string>>();
            
            var uniqueColumns = new Dictionary<string, string>();

            var totalValues = PrepareEventSqlInserts(streamEventData, uniqueColumns, sqlInserts);
            this.logger.LogTrace($"Saving {totalValues} event values to Snowflake db");

            VerifyColumns(uniqueColumns, eventColumns, EventValuesTableName);

            ExecuteStatements(sqlInserts);
            
            this.logger.LogTrace($"Saved {totalValues} event values to Snowflake db");
            
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
                    headerSb.Append($"insert into {InformationSchema}{(InformationSchema != "" ? "." : "")}{EventValuesTableName} ({TimeStampColumn},{StreamIdColumn}");

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
        
                /// <summary>
        /// When the statement is made up of multiple statement header - statement lines (like a batch insert)
        /// </summary>
        /// <param name="statementPairs"></param>
        private void ExecuteStatements(IEnumerable<KeyValuePair<string, List<string>>> statementPairs)
        {
            var totalStatementSize = 0;
            var sb = new StringBuilder();
            var begin = "BEGIN\n";
            var end = ";\nEND";
            var beginEndLength = Encoding.UTF8.GetByteCount(begin) + Encoding.UTF8.GetByteCount(end);
            var pairSeparator = ";\n\n"; // \n so it is somewhat human readable in console
            var pairSeparatorSize = Encoding.UTF8.GetByteCount(pairSeparator);
            var headSeparator = "\n";
            var headSeparatorSize = Encoding.UTF8.GetByteCount(headSeparator);
            var lineSeparator = ",\n";
            var lineSeparatorSize = Encoding.UTF8.GetByteCount(lineSeparator);
            var segmentCount = 0;
            var firstPair = true;
            foreach (var statementPair in statementPairs)
            {
                var statementSize = 0;
                var firstLine = true;
                if (!firstPair)
                {
                    sb.Append(pairSeparator);
                    statementSize += pairSeparatorSize;
                } else firstPair = false;
                
                statementSize += Encoding.UTF8.GetByteCount(statementPair.Key);
                sb.Append(statementPair.Key);
                sb.Append(headSeparator);
                statementSize += headSeparatorSize;
                segmentCount++;

                string GetCurrentHeader()
                {
                    return statementPair.Key + headSeparator;
                }

                foreach (var statement in statementPair.Value)
                {
                    var sql = GetCurrentHeader() + statement;
                    Console.WriteLine($"Executing SQL statement [{sql}]");
                    ExecuteStatement(sql);
                }
                    
                // foreach (var statement in statementPair.Value)
                // {
                //     if (!firstLine)
                //     {
                //         sb.Append(lineSeparator);
                //         statementSize += lineSeparatorSize;
                //
                //         // check if we would be over the limit with the new statement
                //         if (statementSize + totalStatementSize + beginEndLength > MaxQueryByteSize)
                //         {
                //             // if so, send it already
                //             sb.Insert(0, begin);
                //             sb.Append(end);
                //             ExecuteStatement(sb.ToString());
                //             sb.Clear();
                //             
                //             totalStatementSize = 0;
                //             segmentCount = 0;
                //             
                //             statementSize = Encoding.UTF8.GetByteCount(statementPair.Key);
                //             sb.Append(pairSeparator);
                //             statementSize += pairSeparatorSize;
                //             sb.Append(statementPair.Key);
                //             sb.Append(headSeparator);
                //             statementSize += headSeparatorSize;                            
                //             segmentCount++;
                //             
                //             firstLine = true;
                //         }
                //     }
                //     else firstLine = false;
                //
                //     sb.Append(statement);
                //     totalStatementSize += statementSize;
                // }          
            }

            // if (segmentCount > 1)
            // {
            //     sb.Insert(0, begin);
            //     sb.Append(end);
            // }
            // ExecuteStatement(sb.ToString());
        }
        
        /// <summary>
        /// One liner statements
        /// </summary>
        /// <param name="statements"></param>
        private void ExecuteStatements(IEnumerable<string> statements)
        {
            var totalStatementSize = 0;
            var sb = new StringBuilder();
            var first = true;
            var begin = "BEGIN\n";
            var end = ";\nEND";
            var beginEndLength = Encoding.UTF8.GetByteCount(begin) + Encoding.UTF8.GetByteCount(end);
            var separator = ";\n"; // \n so it is somewhat human readable in console
            var separatorSize = Encoding.UTF8.GetByteCount(separator);
            var segmentCount = 0;
            foreach (var statement in statements)
            {
                var statementSize = Encoding.UTF8.GetByteCount(statement);
                if (!first)
                {
                    sb.Append(separator);
                    statementSize += separatorSize;

                    // check if we would be over the limit with the new statement
                    if (statementSize + totalStatementSize + beginEndLength > MaxQueryByteSize)
                    {
                        // if so, send it already
                        sb.Insert(0, begin);
                        sb.Append(end);
                        ExecuteStatement(sb.ToString());
                        sb.Clear();
                        totalStatementSize = 0;
                        segmentCount = 0;
                        first = true;
                    }
                }
                else first = false;

                sb.Append(statement);
                totalStatementSize += statementSize;
                segmentCount++;
            }

            if (segmentCount > 1)
            {
                sb.Insert(0, begin);
                sb.Append(end);
            }
            ExecuteStatement(sb.ToString());
        }
        
        private void ExecuteStatement(string statement)
        {
            if (string.IsNullOrWhiteSpace(statement)) return;
            //this.logger.LogTrace("Executing Snowflake statement:{0}{1}", Environment.NewLine, statement);
            var sw = Stopwatch.StartNew();
            IDisposable timer = null;

            void setTimer()
            {
                timer = InaccurateSharedTimer.Instance.Subscribe(10, () =>
                {
                    this.logger.LogInformation("Executing data write Snowflake statement is taking longer ({0:g}) than expected...", sw.Elapsed);
                    timer.Dispose();
                    setTimer();
                });
            }
            setTimer();

            try
            {
                dbConnection.ExecuteSnowflakeStatement(statement);
            }
            catch (Exception ex)
            {
                this.logger.LogError("Failed to execute Snowflake statement:{0}{1}", Environment.NewLine, statement);
                throw;
            }
            finally
            {
                timer.Dispose();
            }

            sw.Stop();
            //this.logger.LogDebug("Executed Snowflake statement in {0:g}:{1}{2}", sw.Elapsed, Environment.NewLine, statement);
        }
    }
}
