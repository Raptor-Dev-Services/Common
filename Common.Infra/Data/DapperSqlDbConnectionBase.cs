using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Common.Messaging;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Common.Data
{
    /// <summary>
    /// Implementacion de <see cref="IDapperSqlDbConnection"/> sobre Dapper que mide y registra cada ejecucion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cada llamada pide una conexion abierta a <see cref="IOpenDbConnectionFactory"/> y la cierra al
    /// terminar: no hay conexion compartida ni transaccion entre llamadas.
    /// </para>
    /// <para>
    /// Cada ejecucion se registra con nombre de la consulta, resultado (OK, SLOW o FAIL), milisegundos y
    /// hash SHA-256 del texto SQL (o <c>NA</c> si esta vacio). Nivel: 300 ms o mas, Warning; 1000 ms o
    /// mas, Error; 2000 ms o mas, Critical; por debajo de 300 ms, el nivel que pide la llamada. Un fallo
    /// se registra como Error y la excepcion se relanza.
    /// </para>
    /// <para>
    /// El texto SQL solo se incluye si <c>CustomLogging:IncludeSqlText</c> es <c>true</c>. Los
    /// parametros siempre se registran, enmascarados con <see cref="SensitiveDataMasker"/>
    /// (<c>DynamicParameters</c> incluido).
    /// </para>
    /// </remarks>
    public class DapperSqlDbConnectionBase : IDapperSqlDbConnection
    {
        private readonly IOpenDbConnectionFactory _connections;
        private readonly ILogger _logger;
        private readonly bool _includeSqlText;

        /// <summary>
        /// Crea la conexion leyendo <c>CustomLogging:IncludeSqlText</c> de la configuracion (por defecto <c>false</c>).
        /// </summary>
        /// <param name="connections">Fabrica de conexiones abiertas.</param>
        /// <param name="logger">Logger donde se registra cada ejecucion.</param>
        /// <param name="configuration">Configuracion de la que se lee <c>CustomLogging:IncludeSqlText</c>.</param>
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

        /// <summary>
        /// Crea la conexion indicando explicitamente si el texto SQL va al log.
        /// </summary>
        /// <param name="connections">Fabrica de conexiones abiertas.</param>
        /// <param name="logger">Logger donde se registra cada ejecucion.</param>
        /// <param name="includeSqlText">Si es <c>true</c>, el texto SQL se incluye en el log.</param>
        public DapperSqlDbConnectionBase(
            IOpenDbConnectionFactory connections,
            ILogger logger,
            bool includeSqlText = false)
        {
            _connections = connections;
            _logger = logger;
            _includeSqlText = includeSqlText;
        }

        /// <summary>
        /// Ejecuta un comando (INSERT, UPDATE, DELETE, DDL) en una conexion propia.
        /// </summary>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta (objeto anonimo, diccionario o <c>DynamicParameters</c>).</param>
        /// <param name="queryName">Nombre con el que se registra; si falta, el nombre del metodo.</param>
        /// <param name="level">Nivel de log cuando la ejecucion tarda menos de 300 ms. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Numero de filas afectadas que reporta el proveedor.</returns>
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

        /// <summary>
        /// Ejecuta la consulta y devuelve la primera columna de la primera fila, en una conexion propia.
        /// </summary>
        /// <typeparam name="T">Tipo del valor.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta (objeto anonimo, diccionario o <c>DynamicParameters</c>).</param>
        /// <param name="queryName">Nombre con el que se registra; si falta, el nombre del metodo.</param>
        /// <param name="level">Nivel de log cuando la ejecucion tarda menos de 300 ms. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>El valor; <c>default</c> de <typeparamref name="T"/> si no hay filas o es NULL, aunque la firma no lo marque como anulable.</returns>
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

        /// <summary>
        /// Ejecuta la consulta y mapea todas las filas, en una conexion propia. El resultado ya esta cargado en memoria.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea cada fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta (objeto anonimo, diccionario o <c>DynamicParameters</c>).</param>
        /// <param name="queryName">Nombre con el que se registra; si falta, el nombre del metodo.</param>
        /// <param name="level">Nivel de log cuando la ejecucion tarda menos de 300 ms. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Las filas mapeadas; vacio si no hay.</returns>
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

        /// <summary>
        /// Ejecuta la consulta y mapea la unica fila, en una conexion propia. Usa <c>QuerySingleOrDefaultAsync</c> de Dapper.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea la fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta (objeto anonimo, diccionario o <c>DynamicParameters</c>).</param>
        /// <param name="queryName">Nombre con el que se registra; si falta, el nombre del metodo.</param>
        /// <param name="level">Nivel de log cuando la ejecucion tarda menos de 300 ms. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La fila mapeada, o <c>default</c> si no hay filas.</returns>
        /// <exception cref="InvalidOperationException">Si la consulta devuelve mas de una fila.</exception>
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

        /// <summary>
        /// Ejecuta la consulta y mapea la primera fila, en una conexion propia. Usa <c>QueryFirstOrDefaultAsync</c> de Dapper.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea la fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta (objeto anonimo, diccionario o <c>DynamicParameters</c>).</param>
        /// <param name="queryName">Nombre con el que se registra; si falta, el nombre del metodo.</param>
        /// <param name="level">Nivel de log cuando la ejecucion tarda menos de 300 ms. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La primera fila mapeada, o <c>default</c> si no hay filas.</returns>
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

            // Los parametros pasan por el mismo enmascarado que el pipeline. Antes se
            // registraban tal cual, y el INSERT de un usuario dejaba su hash de contrasena en
            // el log: la misma fuga que se cerro en InteractorPipeline, por otra puerta.
            param = ParametrosParaLog(param);

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

        private static object? ParametrosParaLog(object? param)
        {
            // DynamicParameters no expone sus valores como propiedades: sin esto el log solo
            // veria ParameterNames. Se pasa a diccionario, y un nombre sensible se tapa sin
            // leer su valor, igual que hace el enmascarado con una propiedad.
            if (param is DynamicParameters dynamicParameters)
            {
                var valores = new Dictionary<string, object?>();
                foreach (var nombre in dynamicParameters.ParameterNames)
                {
                    if (SensitiveDataMasker.EsSensible(nombre))
                    {
                        valores[nombre] = SensitiveDataMasker.Tapado;
                        continue;
                    }

                    try   { valores[nombre] = dynamicParameters.Get<object?>(nombre); }
                    catch { valores[nombre] = "<no legible>"; }
                }

                return SensitiveDataMasker.Enmascarar(valores);
            }

            return SensitiveDataMasker.Enmascarar(param);
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
