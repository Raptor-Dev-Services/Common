using Microsoft.Extensions.Logging;

namespace Common.Data
{
    public interface IDapperSqlDbConnection
    {
        Task<int> ExecuteAsync(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        Task<T> ExecuteScalarAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<T>> QueryAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        Task<T?> QuerySingleAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        Task<T?> QueryFirstAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);
    }
}
