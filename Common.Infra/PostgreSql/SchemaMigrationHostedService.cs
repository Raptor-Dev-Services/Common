using System.Text;
using Common.Data;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Common.PostgreSql
{
    /// <summary>
    /// Servicio de arranque que aplica los scripts SQL de esquema pendientes antes de que el host termine de arrancar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Busca los <c>.sql</c> de la carpeta <see cref="SchemaMigrationOptions.ScriptsRelativePath"/>
    /// (primero relativa al directorio de salida, despues al directorio actual), sin subcarpetas. Omite
    /// los que empiezan con <c>000_template</c> y los aplica en orden alfabetico por ruta, sin distinguir
    /// mayusculas (usar prefijos numericos con ceros: <c>010_</c> va antes que <c>2_</c>). Si la carpeta no
    /// existe, registra un Warning y no hace nada.
    /// </para>
    /// <para>
    /// Lleva el registro en <c>dbo.SchemaMigrations</c> (crea el esquema y la tabla si faltan). Cada
    /// script pendiente se ejecuta en su propia transaccion junto con su registro; un script vacio se
    /// salta sin registrarse.
    /// </para>
    /// <para>
    /// Ante una <c>NpgsqlException</c> transitoria (directa o como excepcion interna; ver
    /// <c>NpgsqlException.IsTransient</c>: red, timeouts, base arrancando, demasiadas conexiones)
    /// reintenta hasta 20 veces, esperando 1 s, 2 s... hasta 5 s entre intentos, y en cada intento
    /// vuelve a leer lo ya aplicado. Un error del propio SQL no es transitorio: falla a la primera.
    /// Cualquier otro error, o el ultimo intento, se registra y se relanza: el host no arranca.
    /// </para>
    /// <para>
    /// Usa el <see cref="IOpenDbConnectionFactory"/> registrado; si es por tenant actual, al arrancar no
    /// hay tenant y falla.
    /// </para>
    /// </remarks>
    public sealed class SchemaMigrationHostedService : IHostedService
    {
        private readonly IOpenDbConnectionFactory _connectionFactory;
        private readonly ILogger<SchemaMigrationHostedService> _logger;
        private readonly SchemaMigrationOptions _options;

        /// <summary>
        /// Crea el servicio.
        /// </summary>
        /// <param name="connectionFactory">Fabrica de la conexion contra la que se migra.</param>
        /// <param name="options">Opciones con la carpeta de scripts.</param>
        /// <param name="logger">Logger del proceso de migracion.</param>
        public SchemaMigrationHostedService(
            IOpenDbConnectionFactory connectionFactory,
            IOptions<SchemaMigrationOptions> options,
            ILogger<SchemaMigrationHostedService> logger)
        {
            _connectionFactory = connectionFactory;
            _logger = logger;
            _options = options.Value;
        }

        /// <summary>
        /// Aplica los scripts pendientes. Ver remarks de la clase.
        /// </summary>
        /// <param name="cancellationToken">Token de cancelacion del arranque.</param>
        /// <returns>Tarea que termina cuando la migracion acaba.</returns>
        /// <exception cref="Exception">Relanza el error del script o de conexion que hizo fallar la migracion.</exception>
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

        /// <summary>
        /// No hace nada.
        /// </summary>
        /// <param name="cancellationToken">No se usa.</param>
        /// <returns>Una tarea completada.</returns>
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

        // Solo lo que Npgsql marca como transitorio: red, timeouts y los estados de Postgres que
        // significan "vuelve a intentar" (57P03 la base esta arrancando, 53300 demasiadas
        // conexiones...). Antes bastaba con ser NpgsqlException, y PostgresException hereda de ella:
        // un error de sintaxis en un script se reintentaba 20 veces, unos 85 s, con avisos de "la
        // base aun no esta lista" que no eran ciertos.
        internal static bool IsTransientConnectivityError(Exception exception)
        {
            if (exception is NpgsqlException npgsqlException)
            {
                return npgsqlException.IsTransient;
            }

            return exception.InnerException is not null && IsTransientConnectivityError(exception.InnerException);
        }
    }
}
