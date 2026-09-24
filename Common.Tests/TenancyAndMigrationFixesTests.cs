using System.Net.Sockets;
using Xunit;
using Common.MultiTenancy;
using Common.PostgreSql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Common.Tests;

/// <summary>
/// Un tenant nunca recibe la cadena de conexion de otro.
///
/// Hasta la v2.1.2, si el tenant no tenia la cadena pedida, o no estaba en el catalogo, el
/// resolvedor devolvia la global <c>ConnectionStrings:{nombre}</c>: con una base por tenant, un tenant
/// mal configurado trabajaba sobre la base compartida o la de otro, sin ningun error.
/// </summary>
public sealed class TenantConnectionStringTests
{
    private static ITenantConnectionStringResolver Resolvedor()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=compartida",
            ["MultiTenancy:Tenants:acme:ConnectionStrings:Default"] = "Host=acme",
            ["MultiTenancy:Tenants:sin-cadena:IsEnabled"] = "true",
        }).Build();

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddMultiTenancy(configuration)
            .BuildServiceProvider()
            .GetRequiredService<ITenantConnectionStringResolver>();
    }

    [Fact]
    public void Un_tenant_con_cadena_propia_la_recibe()
    {
        Assert.Equal("Host=acme", Resolvedor().GetRequiredConnectionString("acme"));
    }

    [Theory]
    [InlineData("sin-cadena")]   // esta en el catalogo pero no tiene cadena
    [InlineData("desconocido")]  // ni siquiera esta en el catalogo
    public void Sin_cadena_propia_falla_en_vez_de_recibir_la_compartida(string tenant)
    {
        var resolvedor = Resolvedor();

        Assert.False(resolvedor.TryGetConnectionString(tenant, "Default", out var cadena));
        Assert.Equal(string.Empty, cadena);
        Assert.Throws<InvalidOperationException>(() => resolvedor.GetRequiredConnectionString(tenant));
    }
}

/// <summary>
/// Una IP no tiene subdominio. Con la resolucion por subdominio activa (lo esta por defecto),
/// <c>192.168.1.10</c> tiene cuatro segmentos y resolvia el tenant <c>"192"</c>.
/// </summary>
public sealed class TenantSubdomainTests
{
    private static async Task<string?> Resolver(string host)
    {
        var resolver = new DefaultTenantResolver(
            Microsoft.Extensions.Options.Options.Create(new MultiTenantOptions { DefaultTenantId = "por-defecto" }));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        return await resolver.ResolveTenantIdAsync(context);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5:8080")]
    [InlineData("[::1]:5000")]
    public async Task Una_IP_no_resuelve_tenant(string host)
    {
        Assert.Equal("por-defecto", await Resolver(host));
    }

    [Fact]
    public async Task Un_subdominio_de_verdad_sigue_resolviendo()
    {
        Assert.Equal("acme", await Resolver("acme.midominio.com"));
    }
}

/// <summary>
/// <c>RunAsync</c> dentro de un flujo que ya tiene tenant no se lo quita.
///
/// El setter de <c>TenantContextAccessor.Current</c> vaciaba el contenedor anterior antes de poner
/// el nuevo, y ese contenedor se comparte con el flujo que llamo: al volver de <c>RunAsync</c>, la
/// peticion se quedaba sin tenant.
/// </summary>
public sealed class TenantExecutionContextTests
{
    [Fact]
    public async Task El_flujo_que_llama_conserva_su_tenant()
    {
        var accessor = new TenantContextAccessor();
        var runner = new TenantExecutionContextRunner(accessor);
        accessor.Current = new TenantContext("padre");

        string? dentro = null;
        await runner.RunAsync("hijo", _ => { dentro = accessor.Current?.TenantId; return Task.CompletedTask; });

        Assert.Equal("hijo", dentro);
        Assert.Equal("padre", accessor.Current?.TenantId);
    }

    [Fact]
    public async Task Asignar_null_sigue_cortando_a_los_flujos_derivados()
    {
        // Lo que el vaciado protegia y tiene que seguir protegiendo: trabajo lanzado durante una
        // peticion no debe ver su tenant cuando la peticion ya termino.
        var accessor = new TenantContextAccessor();
        accessor.Current = new TenantContext("peticion");

        var puedeLeer = new TaskCompletionSource();
        var derivado = Task.Run(async () =>
        {
            await puedeLeer.Task;
            return accessor.Current?.TenantId;
        });

        accessor.Current = null;   // fin de la peticion
        puedeLeer.SetResult();

        Assert.Null(await derivado);
    }
}

/// <summary>
/// Las migraciones reintentan solo lo transitorio.
///
/// <c>PostgresException</c> hereda de <c>NpgsqlException</c>, y bastaba con ser <c>NpgsqlException</c>:
/// un error de sintaxis se reintentaba 20 veces, unos 85 s, antes de tumbar el arranque.
/// </summary>
public sealed class SchemaMigrationRetryTests
{
    [Theory]
    [InlineData("42601")]   // syntax_error
    [InlineData("42P01")]   // undefined_table
    [InlineData("23505")]   // unique_violation
    public void Un_error_del_SQL_no_se_reintenta(string sqlState)
    {
        var ex = new PostgresException("fallo", "ERROR", "ERROR", sqlState);

        Assert.False(SchemaMigrationHostedService.IsTransientConnectivityError(ex));
    }

    [Theory]
    [InlineData("57P03")]   // cannot_connect_now: la base esta arrancando
    [InlineData("53300")]   // too_many_connections
    public void Una_base_que_aun_no_esta_lista_se_reintenta(string sqlState)
    {
        var ex = new PostgresException("fallo", "FATAL", "FATAL", sqlState);

        Assert.True(SchemaMigrationHostedService.IsTransientConnectivityError(ex));
    }

    [Fact]
    public void Un_error_de_red_se_reintenta_aunque_venga_envuelto()
    {
        var red = new NpgsqlException("sin conexion", new SocketException((int)SocketError.ConnectionRefused));

        Assert.True(SchemaMigrationHostedService.IsTransientConnectivityError(red));
        Assert.True(SchemaMigrationHostedService.IsTransientConnectivityError(new InvalidOperationException("envuelto", red)));
    }
}
