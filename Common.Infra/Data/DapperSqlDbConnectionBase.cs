using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Common.Data
{
    public class DapperSqlDbConnectionBase : IDapperSqlDbConnection
    {
        private readonly IOpenDbConnectionFactory _connections;
        private readonly ILogger _logger;
        private readonly bool _includeSqlText;

        public DapperSqlDbConnectionBase(
            IOpenDbConnectionFactory connections,
            ILogger<DapperSqlDbConnectionBase> logger,
            IConfiguration configuration)
            : this(
                connections,
                logger,
                configuration.GetValue<bool>("CustomLogging:IncludeSqlText"))
        {
        }

        public DapperSqlDbConnectionBase(
            IOpenDbConnectionFactory connections,
            ILogger logger,
            bool includeSqlText = false)
        {
            _connections = connections;
            _logger = logger;
            _includeSqlText = includeSqlText;
        }

        public Task<int> ExecuteAsync(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            var resolvedQueryName = ResolveQueryName(queryName, nameof(ExecuteAsync));
            return ExecuteTimedAsync(
                resolvedQueryName,
                sql,
                async () =>
                {
                    using var con = await _connections.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
                    return await con.ExecuteAsync(command).ConfigureAwait(false);
                },
                param,
                level);
        }

        public Task<T> ExecuteScalarAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            var resolvedQueryName = ResolveQueryName(queryName, nameof(ExecuteScalarAsync));
            return ExecuteTimedAsync(
                resolvedQueryName,
                sql,
                async () =>
                {
                    using var con = await _connections.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
                    var scalar = await con.ExecuteScalarAsync<T>(command).ConfigureAwait(false);
                    return scalar!;
                },
                param,
                level);
        }

        public Task<IEnumerable<T>> QueryAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            var resolvedQueryName = ResolveQueryName(queryName, nameof(QueryAsync));
            return ExecuteTimedAsync(
                resolvedQueryName,
                sql,
                async () =>
                {
                    using var con = await _connections.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
                    return await con.QueryAsync<T>(command).ConfigureAwait(false);
                },
                param,
                level);
        }

        public Task<T?> QuerySingleAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            var resolvedQueryName = ResolveQueryName(queryName, nameof(QuerySingleAsync));
            return ExecuteTimedAsync(
                resolvedQueryName,
                sql,
                async () =>
                {
                    using var con = await _connections.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
                    return await con.QuerySingleOrDefaultAsync<T>(command).ConfigureAwait(false);
                },
                param,
                level);
        }

        public Task<T?> QueryFirstAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default)
        {
            var resolvedQueryName = ResolveQueryName(queryName, nameof(QueryFirstAsync));
            return ExecuteTimedAsync(
                resolvedQueryName,
                sql,
                async () =>
                {
                    using var con = await _connections.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var command = new CommandDefinition(sql, param, cancellationToken: cancellationToken);
                    return await con.QueryFirstOrDefaultAsync<T>(command).ConfigureAwait(false);
                },
                param,
                level);
        }

        private async Task<T> ExecuteTimedAsync<T>(string queryName, string sql, Func<Task<T>> action, object? param, LogLevel level)
        {
            var sw = Stopwatch.StartNew();
            var sqlHash = ComputeSqlHash(sql);

            try
            {
                var result = await action().ConfigureAwait(false);
                sw.Stop();

                var elapsedMs = sw.ElapsedMilliseconds;
                if (elapsedMs >= 2000)
                {
                    LogSql(LogLevel.Critical, queryName, elapsedMs, sqlHash, sql, param, "SLOW");
                }
                else if (elapsedMs >= 1000)
                {
                    LogSql(LogLevel.Error, queryName, elapsedMs, sqlHash, sql, param, "SLOW");
                }
                else if (elapsedMs >= 300)
                {
                    LogSql(LogLevel.Warning, queryName, elapsedMs, sqlHash, sql, param, "SLOW");
                }
                else
                {
                    LogSql(level, queryName, elapsedMs, sqlHash, sql, param, "OK");
                }

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogSql(LogLevel.Error, queryName, sw.ElapsedMilliseconds, sqlHash, sql, param, "FAIL", ex);
                throw;
            }
        }

        private void LogSql(
            LogLevel level,
            string queryName,
            long elapsedMs,
            string sqlHash,
            string sqlText,
            object? param,
            string outcome,
            Exception? ex = null)
        {
            const string messageWithSql = "SQL {QueryName} {Outcome} in {ElapsedMs} ms | hash: {SqlHash} | sql: {SqlText} | params: {@Params}";
            const string messageWithoutSql = "SQL {QueryName} {Outcome} in {ElapsedMs} ms | hash: {SqlHash} | params: {@Params}";

            if (_includeSqlText)
            {
                if (ex is null)
                {
                    _logger.Log(level, messageWithSql, queryName, outcome, elapsedMs, sqlHash, sqlText, param);
                }
                else
                {
                    _logger.Log(level, ex, messageWithSql, queryName, outcome, elapsedMs, sqlHash, sqlText, param);
                }

                return;
            }

            if (ex is null)
            {
                _logger.Log(level, messageWithoutSql, queryName, outcome, elapsedMs, sqlHash, param);
            }
            else
            {
                _logger.Log(level, ex, messageWithoutSql, queryName, outcome, elapsedMs, sqlHash, param);
            }
        }

        private static string ResolveQueryName(string? queryName, string fallbackName)
        {
            if (!string.IsNullOrWhiteSpace(queryName))
            {
                return queryName;
            }

            return fallbackName;
        }

        private static string ComputeSqlHash(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return "NA";
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
            return Convert.ToHexString(bytes);
        }
    }
}
