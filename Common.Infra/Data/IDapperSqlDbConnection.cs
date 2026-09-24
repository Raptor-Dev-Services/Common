using Microsoft.Extensions.Logging;

namespace Common.Data
{
    /// <summary>
    /// Acceso a datos con Dapper que registra cada ejecucion. Ver <see cref="DapperSqlDbConnectionBase"/>
    /// para el detalle de conexion y log.
    /// </summary>
    public interface IDapperSqlDbConnection
    {
        /// <summary>
        /// Ejecuta un comando (INSERT, UPDATE, DELETE, DDL).
        /// </summary>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta.</param>
        /// <param name="queryName">Nombre con el que se registra la ejecucion en el log.</param>
        /// <param name="level">Nivel de log de una ejecucion normal. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Numero de filas afectadas.</returns>
        Task<int> ExecuteAsync(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Devuelve la primera columna de la primera fila.
        /// </summary>
        /// <typeparam name="T">Tipo del valor.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta.</param>
        /// <param name="queryName">Nombre con el que se registra la ejecucion en el log.</param>
        /// <param name="level">Nivel de log de una ejecucion normal. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>El valor, o <c>default</c> si no hay filas.</returns>
        Task<T> ExecuteScalarAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Devuelve todas las filas mapeadas.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea cada fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta.</param>
        /// <param name="queryName">Nombre con el que se registra la ejecucion en el log.</param>
        /// <param name="level">Nivel de log de una ejecucion normal. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Las filas; vacio si no hay.</returns>
        Task<IEnumerable<T>> QueryAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Devuelve la unica fila, o <c>default</c> si no hay. Lanza si hay mas de una.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea la fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta.</param>
        /// <param name="queryName">Nombre con el que se registra la ejecucion en el log.</param>
        /// <param name="level">Nivel de log de una ejecucion normal. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La fila, o <c>default</c>.</returns>
        Task<T?> QuerySingleAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Devuelve la primera fila, o <c>default</c> si no hay.
        /// </summary>
        /// <typeparam name="T">Tipo al que se mapea la fila.</typeparam>
        /// <param name="sql">Texto SQL. Los valores van en <paramref name="param"/>, nunca concatenados.</param>
        /// <param name="param">Parametros de la consulta.</param>
        /// <param name="queryName">Nombre con el que se registra la ejecucion en el log.</param>
        /// <param name="level">Nivel de log de una ejecucion normal. Por defecto Debug.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La fila, o <c>default</c>.</returns>
        Task<T?> QueryFirstAsync<T>(
            string sql,
            object? param = null,
            string? queryName = null,
            LogLevel level = LogLevel.Debug,
            CancellationToken cancellationToken = default);
    }
}
