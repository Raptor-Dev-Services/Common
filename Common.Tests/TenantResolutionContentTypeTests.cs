using Xunit;
using Common.MultiTenancy;
using Common.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Common.Tests;

/// <summary>
/// Los rechazos de <c>UseTenantResolution</c> son ProblemDetails y tienen que declararse como tales.
/// Salian con <c>application/json</c> por el mismo motivo que <c>UseCoreProblemDetails</c>.
/// </summary>
public sealed class TenantResolutionContentTypeTests
{
    [Theory]
    [InlineData(null, StatusCodes.Status400BadRequest)]         // obligatorio y no vino
    [InlineData("apagado", StatusCodes.Status403Forbidden)]     // deshabilitado
    [InlineData("desconocido", StatusCodes.Status403Forbidden)] // no esta en el catalogo
    public async Task Los_rechazos_salen_como_problem_json(string? tenant, int statusEsperado)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MultiTenancy:RequireTenant"] = "true",
            ["MultiTenancy:ResolveFromSubdomain"] = "false",
            ["MultiTenancy:Tenants:acme:IsEnabled"] = "true",
            ["MultiTenancy:Tenants:apagado:IsEnabled"] = "false",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMultiTenancy(configuration);
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        if (tenant is not null)
            context.Request.Headers["X-Tenant-Id"] = tenant;

        var llegoAlEndpoint = false;
        var middleware = ActivatorUtilities.CreateInstance<TenantResolutionMiddleware>(
            provider, (RequestDelegate)(_ => { llegoAlEndpoint = true; return Task.CompletedTask; }));

        await middleware.Invoke(context);

        Assert.False(llegoAlEndpoint);
        Assert.Equal(statusEsperado, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
    }
}
