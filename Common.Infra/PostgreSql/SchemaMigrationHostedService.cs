using System.Text;
using Common.Data;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Common.PostgreSql
{
    public sealed class SchemaMigrationHostedService : IHostedService
    {
        private readonly IOpenDbConnectionFactory _connectionFactory;
        private readonly ILogger<SchemaMigrationHostedService> _logger;
        private readonly SchemaMigrationOptions _options;

        public SchemaMigrationHostedService(
            IOpenDbConnectionFactory connectionFactory,
            IOptions<SchemaMigrationOptions> options,
            ILogger<SchemaMigrationHostedService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
            _options = options.Value;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var scriptsDirectory = ResolveScriptsDirectory(_options.ScriptsRelativePath);
            if (!Directory.Exists(scriptsDirectory))
            {
                _logger.LogWarning("Schema migration scripts directory was not found: {ScriptsDirectory}", scriptsDirectory);
                return;
            }

            var sqlFiles = Directory
                .GetFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return !fileName.StartsWith("000_template", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sqlFiles.Count == 0)
            {
                _logger.LogInformation("No schema migration scripts found in {ScriptsDirectory}.", scriptsDirectory);
                return;
            }

            const int maxAttempts = 20;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var connection = await _connectionFactory.GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                    await EnsureSchemaMigrationsTableAsync(connection, cancellationToken).ConfigureAwait(false);
                    var appliedScripts = (await connection.QueryAsync<string>(
                            new CommandDefinition(
                                "SELECT ScriptName FROM dbo.SchemaMigrations;",
                                cancellationToken: cancellationToken))
                        .ConfigureAwait(false))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var sqlFile in sqlFiles)
                    {
                        var scriptName = Path.GetFileName(sqlFile);
                        if (appliedScripts.Contains(scriptName))
                        {
                            continue;
                        }

                        var sql = await File.ReadAllTextAsync(sqlFile, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(sql))
                        {
                            continue;
                        }

                        using var transaction = connection.BeginTransaction();
                        await connection.ExecuteAsync(
                            new CommandDefinition(sql, transaction: transaction, cancellationToken: cancellationToken))
                            .ConfigureAwait(false);
                        await connection.ExecuteAsync(
                            new CommandDefinition(
                                "INSERT INTO dbo.SchemaMigrations (ScriptName) VALUES (@ScriptName) ON CONFLICT (ScriptName) DO NOTHING;",
                                new { ScriptName = scriptName },
                                transaction: transaction,
                                cancellationToken: cancellationToken))
                            .ConfigureAwait(false);
                        transaction.Commit();
                    }

                    _logger.LogInformation("Schema migration completed.");
                    return;
                }
                catch (Exception ex) when (IsTransientConnectivityError(ex) && attempt < maxAttempts)
                {
                    var delayMs = Math.Min(1000 * attempt, 5000);
                    _logger.LogWarning(
                        ex,
                        "Database is not ready yet (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs} ms.",
                        attempt,
                        maxAttempts,
                        delayMs);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Schema migration failed.");
                    throw;
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static Task EnsureSchemaMigrationsTableAsync(
            System.Data.IDbConnection connection,
            CancellationToken cancellationToken)
        {
            const string sql = """
                CREATE SCHEMA IF NOT EXISTS dbo;

                CREATE TABLE IF NOT EXISTS dbo.SchemaMigrations (
                    ScriptName      VARCHAR(260) PRIMARY KEY,
                    AppliedAtUtc    TIMESTAMP(0) NOT NULL DEFAULT (timezone('utc', now()))
                );
                """;

            return connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }

        private static string ResolveScriptsDirectory(string scriptsRelativePath)
        {
            var candidateInOutput = Path.Combine(AppContext.BaseDirectory, scriptsRelativePath);
            if (Directory.Exists(candidateInOutput))
            {
                return candidateInOutput;
            }

            var candidateInProject = Path.Combine(Directory.GetCurrentDirectory(), scriptsRelativePath);
            if (Directory.Exists(candidateInProject))
            {
                return candidateInProject;
            }

            return candidateInOutput;
        }

        private static bool IsTransientConnectivityError(Exception exception)
        {
            if (exception is NpgsqlException)
            {
                return true;
            }

            return exception.InnerException is not null && IsTransientConnectivityError(exception.InnerException);
        }
    }
}
