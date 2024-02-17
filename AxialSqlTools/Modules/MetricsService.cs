﻿
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AxialSqlTools
{

    public class HealthDashboardServerMetric
    {

        public HealthDashboardServerMetric ()
        {
            Iteration = 0;
            PerfCounter_RefreshDateTime = DateTime.Now;
        }
        // Example properties of the Metric class
        // public int Id { get; set; }
        public int Iteration { get; set; }
        public string ServerName { get; set; }
        public string ServiceName { get; set; }
        public string ServerVersion { get; set; }
        public DateTime UtcStartTime { get; set; }
        public int CpuUtilization { get; set; }
        public int ConnectionCountTotal { get; set; }
        public int ConnectionCountEnc { get; set; }

        public int CountUserDatabasesTotal { get; set; }
        public int CountUserDatabasesOkay { get; set; }


        public int BlockedRequestsCount { get; set; }
        public int BlockingTotalWaitTime { get; set; }

        public DateTime PerfCounter_RefreshDateTime { get; set; }
        public long PerfCounter_BatchRequestsSec_Total { get; set; }
        public long PerfCounter_BatchRequestsSec { get; set; }
        public long PerfCounter_SQLCompilationsSec_Total { get; set; }
        public long PerfCounter_SQLCompilationsSec { get; set; }

        public long PerfCounter_PLE { get; set; }
        public long PerfCounter_DataFileSize { get; set; }
        public long PerfCounter_LogFileSize { get; set; }
        public long PerfCounter_UsedLogFileSize { get; set; }
        public long PerfCounter_TargetServerMemory { get; set; }
        public long PerfCounter_TotalServerMemory { get; set; }
        public long PerfCounter_AlwaysOn_LogSendQueue { get; set; }

        public bool AlwaysOn_Exists { get; set; }
        public int AlwaysOn_Health { get; set; }
        public int AlwaysOn_MaxLatency { get; set; }
        public long AlwaysOn_TotalRedoQueueSize { get; set; }
        public long AlwaysOn_TotalLogSentQueueSize { get; set; }

        public bool spWhoIsActiveExists { get; set; }

        public int ServerResponseTimeMs { get; set; }
        public bool Completed { get; set; }
        public bool HasException { get; set; }
        public string ExecutionException { get; set; }
        // Add other properties or methods as needed


    }
    
    public static class MetricsService
    {


        public static async Task<HealthDashboardServerMetric> FetchServerMetricsAsync(string connectionString, HealthDashboardServerMetric prev_metrics)
        {
            var metrics = new HealthDashboardServerMetric {};

            metrics.Iteration = prev_metrics.Iteration + 1;

            try
            {

                Stopwatch stopwatch = Stopwatch.StartNew();

                // Create and open a connection to SQL Server
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    // Open the connection
                    await connection.OpenAsync();

                    string queryText_1 = @"
                    SELECT @@SERVERNAME AS ServerName,
                           @@SERVICENAME AS ServiceName,
                           DATEADD(hh, DATEDIFF(hh, GETDATE(), GETUTCDATE()), sqlserver_start_time) as UtcStartTime,
                           @@VERSION,
                           SERVERPROPERTY('edition'),
                           CASE WHEN OBJECT_ID('dbo.sp_WhoIsActive') IS NULL THEN 0 ELSE 1 END AS spWhoIsActiveExists
                    FROM sys.dm_os_sys_info;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_1, connection))
                    { using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        { if (reader.HasRows)
                            { while (await reader.ReadAsync())
                                {
                                    metrics.ServerName = reader.GetString(0);
                                    metrics.ServiceName = reader.GetString(1);
                                    metrics.UtcStartTime = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);  

                                    metrics.ServerVersion = reader.GetString(3);

                                    int index = metrics.ServerVersion.IndexOf("Copyright");
                                    if (index != -1)
                                        metrics.ServerVersion = metrics.ServerVersion.Substring(0, index).Trim().Replace("\r", "").Replace("\n", "");

                                    metrics.ServerVersion += " | " + reader.GetString(4);

                                    metrics.spWhoIsActiveExists = (reader.GetInt32(5) == 1);

                                }
                            }
                        }
                    }

                    string queryText_2 = @"
                    SELECT COUNT(*) AS ConnectionCountTotal,
                           SUM(CASE WHEN encrypt_option = 'TRUE' THEN 1 ELSE 0 END) AS ConnectionCountEnc
                    FROM sys.dm_exec_connections;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_2, connection))
                    { using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        { if (reader.HasRows)
                            { while (await reader.ReadAsync())
                                {
                                    metrics.ConnectionCountTotal = reader.GetInt32(0);
                                    metrics.ConnectionCountEnc = reader.GetInt32(1);
                                }
                            }
                        }
                    }

                    string queryText_3 = @"
                    SELECT
                        AVG(SQLProcessUtilization)
                    FROM (
                        SELECT 
                            record.value('(./Record/@id)[1]', 'int') AS record_id,
                            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int') AS SystemIdle,
                            record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SQLProcessUtilization,
                            timestamp
                        FROM (
                            SELECT 
                                timestamp, 
                                CONVERT(xml, record) AS record 
                            FROM sys.dm_os_ring_buffers 
                            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR' 
                            AND record LIKE '%<SystemHealth>%'
                        ) AS x
                    ) AS y;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_3, connection))
                    { using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        { if (reader.HasRows)
                            { while (await reader.ReadAsync())
                                {
                                    metrics.CpuUtilization = reader.GetInt32(0);
                                }
                            }
                        }
                    }

                    string queryText_4 = @"                
                    SELECT ISNULL(COUNT(*), 0) AS BlockedRequestCount,
                           ISNULL(SUM(wait_time) / 1000, 0) AS TotalWaitTime
                    FROM sys.dm_exec_requests AS er
                    WHERE blocking_session_id > 0;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_4, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.HasRows)
                            {
                                while (await reader.ReadAsync())
                                {
                                    metrics.BlockedRequestsCount = reader.GetInt32(0);
                                    metrics.BlockingTotalWaitTime = reader.GetInt32(1);
                                }
                            }
                        }
                    }

                    string queryText_5a = @"                
                    SELECT [object_name], [counter_name], [cntr_value]
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:Buffer Manager'
                          AND [counter_name] = 'Page life expectancy'
                          AND [instance_name] = ''
                    UNION ALL

                    SELECT [object_name], [counter_name], SUM([cntr_value])
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:Databases'
                          AND [counter_name] = 'Log File(s) Used Size (KB)'
                          AND [instance_name] NOT IN ('_Total', 'tempdb', 'master', 'model', 'msdb', 'mssqlsystemresource')
                    GROUP BY [object_name], [counter_name]
                    UNION ALL

                    SELECT [object_name], [counter_name], SUM([cntr_value])
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:Databases'
                          AND [counter_name] IN ('Data File(s) Size (KB)', 'Log File(s) Size (KB)')
                          AND [instance_name] NOT IN ('_Total', 'tempdb', 'master', 'model', 'msdb', 'mssqlsystemresource')
                    GROUP BY [object_name], [counter_name]
                    UNION ALL
    
                    SELECT [object_name], [counter_name], [cntr_value]
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:Memory Manager'
                          AND [counter_name] IN ('Total Server Memory (KB)', 'Target Server Memory (KB)')
                    UNION ALL
    
                    SELECT [object_name], [counter_name], [cntr_value]
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:Database Replica'
                          AND [counter_name] = 'Log Send Queue'
                          AND [instance_name] = '_Total';
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_5a, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.HasRows)
                            {
                                while (await reader.ReadAsync())
                                {

                                    string CounterGroup = reader.GetString(0).Trim();
                                    string CounterName = reader.GetString(1).Trim();
                                    long CounterValue = reader.GetInt64(2);

                                    switch (CounterGroup)
                                    {
                                        case "SQLServer:Buffer Manager":
                                            if (CounterName == "Page life expectancy")
                                                metrics.PerfCounter_PLE = CounterValue;
                                            break;

                                        case "SQLServer:Memory Manager":
                                            if (CounterName == "Total Server Memory (KB)")
                                                metrics.PerfCounter_TotalServerMemory = CounterValue;
                                            else if (CounterName == "Target Server Memory (KB)")
                                                metrics.PerfCounter_TargetServerMemory = CounterValue;
                                            break;

                                        case "SQLServer:Databases":
                                            if (CounterName == "Data File(s) Size (KB)")
                                                metrics.PerfCounter_DataFileSize = CounterValue;
                                            else if (CounterName == "Log File(s) Size (KB)")
                                                metrics.PerfCounter_LogFileSize = CounterValue;
                                            else if (CounterName == "Log File(s) Used Size (KB)")
                                                metrics.PerfCounter_UsedLogFileSize = CounterValue;
                                            break;

                                        case "SQLServer:Database Replica":
                                            if (CounterName == "Log Send Queue")
                                                metrics.PerfCounter_AlwaysOn_LogSendQueue = CounterValue;
                                            break;
                                        default:
                                            break;

                                    }

                                }
                            }
                        }
                    }

                    string queryText_5b = @"                
                    SELECT 
                        [object_name], 
                        [counter_name], 
                        [cntr_value], 
                        ([cntr_value] - @Prev_BatchRequestsSec) / (DATEDIFF(second, @LastRefresh, GETDATE())), 
                        GETDATE()
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:SQL Statistics'
                          AND [counter_name] = 'Batch Requests/sec'
                          AND [instance_name] = ''
                    UNION ALL

                    SELECT 
                        [object_name], 
                        [counter_name], 
                        [cntr_value], 
                        ([cntr_value] - @Prev_SQLCompilationsSec) / (DATEDIFF(second, @LastRefresh, GETDATE())), 
                        GETDATE()
                    FROM sys.dm_os_performance_counters
                    WHERE [object_name] = 'SQLServer:SQL Statistics'
                          AND [counter_name] = 'SQL Compilations/sec'
                          AND [instance_name] = ''

                    ";

                    using (SqlCommand command = new SqlCommand(queryText_5b, connection))
                    {
                        command.Parameters.AddWithValue("@LastRefresh", prev_metrics.PerfCounter_RefreshDateTime);
                        command.Parameters.AddWithValue("@Prev_BatchRequestsSec", prev_metrics.PerfCounter_BatchRequestsSec_Total);
                        command.Parameters.AddWithValue("@Prev_SQLCompilationsSec", prev_metrics.PerfCounter_SQLCompilationsSec_Total);

                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.HasRows)
                            {
                                while (await reader.ReadAsync())
                                {

                                    string CounterGroup = reader.GetString(0).Trim();
                                    string CounterName = reader.GetString(1).Trim();
                                    long CounterValueTotal = reader.GetInt64(2);
                                    long CounterValue = reader.GetInt64(3);

                                    metrics.PerfCounter_RefreshDateTime = reader.GetDateTime(4);

                                    switch (CounterGroup)
                                    {
                                        case "SQLServer:SQL Statistics":
                                            if (CounterName == "Batch Requests/sec")
                                            {
                                                metrics.PerfCounter_BatchRequestsSec_Total = CounterValueTotal;
                                                metrics.PerfCounter_BatchRequestsSec = CounterValue;
                                            }
                                            else if (CounterName == "SQL Compilations/sec")
                                            {
                                                metrics.PerfCounter_SQLCompilationsSec_Total = CounterValueTotal;
                                                metrics.PerfCounter_SQLCompilationsSec = CounterValue;
                                            }
                                            break;

                                        default:
                                            break;

                                    }

                                }
                            }
                        }
                    }

                    string queryText_6 = @"                
                    SELECT CAST(ISNULL(MIN(synchronization_health), 0) AS INT),
                           ISNULL(MAX(DATEDIFF(millisecond, last_commit_time, getdate())), 0) AS maxLatency,
                           ISNULL(SUM(drs.redo_queue_size), 0),
                           ISNULL(SUM(drs.log_send_queue_size), 0),
                           COUNT(*)
                    FROM sys.dm_hadr_database_replica_states AS drs;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_6, connection))
                    { using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        { if (reader.HasRows)
                            { while (await reader.ReadAsync())
                                {
                                    metrics.AlwaysOn_Health = reader.GetInt32(0);
                                    metrics.AlwaysOn_MaxLatency = reader.GetInt32(1);
                                    metrics.AlwaysOn_TotalRedoQueueSize = reader.GetInt64(2);
                                    metrics.AlwaysOn_TotalLogSentQueueSize = reader.GetInt64(3);

                                    metrics.AlwaysOn_Exists = (reader.GetInt32(4) > 0);
                                }
                            }
                        }
                    }

                    string queryText_7 = @"                
                    SELECT 
                            (SELECT isnull(count(*), 0) FROM sys.databases WHERE database_id > 4) AS CountUserDatabasesTotal,
                            (SELECT isnull(count(*), 0) FROM sys.databases WHERE database_id > 4
                                                                              AND user_access_desc = 'MULTI_USER'
                                                                              AND state_desc = 'ONLINE') AS CountUserDatabasesOkay;
                    ";

                    using (SqlCommand command = new SqlCommand(queryText_7, connection))
                    {
                        using (SqlDataReader reader = await command.ExecuteReaderAsync())
                        {
                            if (reader.HasRows)
                            {
                                while (await reader.ReadAsync())
                                {
                                    metrics.CountUserDatabasesTotal = reader.GetInt32(0);
                                    metrics.CountUserDatabasesOkay = reader.GetInt32(1);
                                }
                            }
                        }
                    }

                }

                stopwatch.Stop();
                TimeSpan ts = stopwatch.Elapsed;

                metrics.ServerResponseTimeMs = (int)ts.TotalMilliseconds;
                metrics.Completed = true;

            }
            catch (Exception ex)
            {
                metrics.HasException = true;
                metrics.ExecutionException = ex.Message;
            }

            return metrics;
        }
        
    }
}
