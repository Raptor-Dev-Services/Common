using System.Data;
using Xunit;
using Common.Data;
using Common.Messaging;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Common.Tests;

/// <summary>
/// Los parametros SQL que registra `DapperSqlDbConnectionBase`.
///
/// El enmascarado del pipeline (ver `SensitiveDataMaskerTests`) no llegaba aqui: cada
/// ejecucion registraba `{@Params}` tal cual, y el INSERT de un usuario dejaba su hash de
/// contrasena en el log. Estas pruebas pasan por la ruta de fallo --la fabrica de conexiones
/// lanza-- porque es la que se puede ejercitar sin base de datos, y comparte con la ruta de
/// exito el unico punto donde se registra.
/// </summary>
public sealed class DapperSqlDbConnectionLogTests
{
    [Fact]
    public async Task El_hash_de_contrasena_de_un_INSERT_no_llega_al_log()
    {
        var logger = new LoggerQueCaptura();
        var db = new DapperSqlDbConnectionBase(new FabricaQueFalla(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteAsync(
            "INSERT INTO usuarios (email, password_hash) VALUES (@Email, @PasswordHash)",
            new { Email = "ana@ejemplo.test", PasswordHash = "AQAAAAIAAYagAAAAE-hash" }));

        var parametros = Assert.IsType<Dictionary<string, object?>>(logger.Parametros());
        Assert.Equal(SensitiveDataMasker.Tapado, parametros["PasswordHash"]);
        Assert.Equal("ana@ejemplo.test", parametros["Email"]);
    }

    [Fact]
    public async Task Tambien_con_DynamicParameters()
    {
        // DynamicParameters no expone sus valores como propiedades: sin tratarlo aparte, el
        // log veria ParameterNames y nada mas. Tiene que verse el valor NO sensible y taparse
        // el sensible.
        var dp = new DynamicParameters();
        dp.Add("Email", "ana@ejemplo.test");
        dp.Add("ResetToken", "tok_123");

        var logger = new LoggerQueCaptura();
        var db = new DapperSqlDbConnectionBase(new FabricaQueFalla(), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteAsync("UPDATE ...", dp));

        var parametros = Assert.IsType<Dictionary<string, object?>>(logger.Parametros());
        Assert.Equal(SensitiveDataMasker.Tapado, parametros["ResetToken"]);
        Assert.Equal("ana@ejemplo.test", parametros["Email"]);
    }

    private sealed class FabricaQueFalla : IOpenDbConnectionFactory
    {
        public Task<IDbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sin base de datos en la prueba");
    }

    private sealed class LoggerQueCaptura : ILogger
    {
        private readonly List<IReadOnlyList<KeyValuePair<string, object?>>> _estados = new();

        public object? Parametros() =>
            Assert.Single(_estados).Single(kv => kv.Key == "@Params").Value;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> estado)
            {
                _estados.Add(estado);
            }
        }
    }
}
