using System.Data;
using Common.Workflow.Folios;

namespace Common.Workflow.Tests.Folios;

/// <summary>
/// C05: el contrato de <see cref="IFolioGenerator"/> y <see cref="IGaplessFolioGenerator"/> sobre la
/// implementacion en memoria (V5-V8). Cada producto corre estos mismos vectores contra la suya.
/// </summary>
public class InMemoryFolioGeneratorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task El_primer_numero_de_un_ambito_nuevo_es_1()
    {
        var folios = new InMemoryFolioGenerator();

        Assert.Equal(0, folios.LastValue(FolioKey.Global("support_ticket")));
        Assert.Equal(1, await folios.NextAsync(FolioKey.Global("support_ticket"), Ct));
        Assert.Equal(2, await folios.NextAsync(FolioKey.Global("support_ticket"), Ct));
        Assert.Equal(2, folios.LastValue(FolioKey.Global("support_ticket")));
    }

    // V5
    [Fact(Timeout = 10000)]
    public async Task V5_cien_llamadas_paralelas_dan_1_a_100_sin_repetidos()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.Global("support_ticket");

        var numeros = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => Task.Run(async () => await folios.NextAsync(key, Ct))));

        Assert.Equal(Enumerable.Range(1, 100).Select(n => (long)n), numeros.Order());
    }

    // V6
    [Fact]
    public async Task V6_con_huecos_un_rollback_de_negocio_no_devuelve_el_numero()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.Global("support_ticket");
        folios.Seed(key, 6);

        using (var negocio = folios.BeginTransaction())
        {
            Assert.Equal(7, await folios.NextAsync(key, Ct));
            negocio.Rollback();
        }

        Assert.Equal(8, await folios.NextAsync(key, Ct));
    }

    // V7
    [Fact]
    public async Task V7_sin_huecos_un_rollback_devuelve_el_numero()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.ForTenant("invoice_series", "A");
        folios.Seed(key, 6);

        var tx = folios.BeginTransaction();
        Assert.Equal(7, await folios.NextInTransactionAsync(key, tx, Ct));
        tx.Rollback();

        var tx2 = folios.BeginTransaction();
        Assert.Equal(7, await folios.NextInTransactionAsync(key, tx2, Ct));
        Assert.Equal(8, await folios.NextInTransactionAsync(key, tx2, Ct));
        tx2.Commit();

        Assert.Equal(8, folios.LastValue(key));
        Assert.Equal(9, await folios.NextAsync(key, Ct));
    }

    [Fact]
    public async Task Sin_huecos_dispose_sin_commit_revierte()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.Global("invoice_series");

        using (var tx = folios.BeginTransaction())
            Assert.Equal(1, await folios.NextInTransactionAsync(key, tx, Ct));

        Assert.Equal(0, folios.LastValue(key));
        using var tx2 = folios.BeginTransaction();
        Assert.Equal(1, await folios.NextInTransactionAsync(key, tx2, Ct));
        tx2.Commit();
    }

    [Fact(Timeout = 10000)]
    public async Task Sin_huecos_el_ambito_queda_bloqueado_hasta_el_commit()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.Global("invoice_series");
        var primera = folios.BeginTransaction();
        Assert.Equal(1, await folios.NextInTransactionAsync(key, primera, Ct));

        var segunda = folios.BeginTransaction();
        var espera = folios.NextInTransactionAsync(key, segunda, Ct).AsTask();
        var conHuecos = folios.NextAsync(key, Ct).AsTask();
        await Task.Delay(50);
        Assert.False(espera.IsCompleted);
        Assert.False(conHuecos.IsCompleted);

        primera.Commit();

        // Si la segunda transaccion gana el bloqueo, la llamada con huecos espera a su COMMIT.
        var deLaSegunda = await espera;
        segunda.Commit();
        var numeros = new[] { deLaSegunda, await conHuecos };
        Assert.Equal([2L, 3L], numeros.Order());
        Assert.Equal(3, folios.LastValue(key));
    }

    [Fact(Timeout = 10000)]
    public async Task La_espera_del_bloqueo_respeta_la_cancelacion()
    {
        var folios = new InMemoryFolioGenerator();
        var key = FolioKey.Global("invoice_series");
        var primera = folios.BeginTransaction();
        await folios.NextInTransactionAsync(key, primera, Ct);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await folios.NextInTransactionAsync(key, folios.BeginTransaction(), cancel.Token));
        primera.Rollback();
    }

    [Fact]
    public async Task Sin_huecos_solo_acepta_transacciones_propias_y_activas()
    {
        var folios = new InMemoryFolioGenerator();
        var otro = new InMemoryFolioGenerator();
        var key = FolioKey.Global("invoice_series");

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await folios.NextInTransactionAsync(key, null!, Ct));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await folios.NextInTransactionAsync(key, otro.BeginTransaction(), Ct));

        var tx = folios.BeginTransaction();
        Assert.Equal(IsolationLevel.ReadCommitted, tx.IsolationLevel);
        Assert.Null(tx.Connection);
        tx.Commit();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await folios.NextInTransactionAsync(key, tx, Ct));
        Assert.Throws<InvalidOperationException>(tx.Rollback);
        tx.Dispose();   // ya completada: no hace nada
    }

    // V8
    [Fact]
    public async Task V8_ambitos_distintos_son_independientes()
    {
        var folios = new InMemoryFolioGenerator();

        var a1 = await folios.NextAsync(FolioKey.ForTenant("contract", "A"), Ct);
        var a2 = await folios.NextAsync(FolioKey.ForTenant("contract", "A"), Ct);
        var b1 = await folios.NextAsync(FolioKey.ForTenant("contract", "B"), Ct);

        Assert.Equal((1L, 2L, 1L), (a1, a2, b1));
    }

    [Fact]
    public async Task La_secuencia_y_el_periodo_tambien_separan_contadores()
    {
        var folios = new InMemoryFolioGenerator();

        Assert.Equal(1, await folios.NextAsync(FolioKey.ForTenant("contract", "A"), Ct));
        Assert.Equal(1, await folios.NextAsync(FolioKey.ForTenant("asset_tag", "A"), Ct));
        Assert.Equal(1, await folios.NextAsync(FolioKey.ForTenant("contract", "A").WithPeriod(2026), Ct));
        Assert.Equal(1, await folios.NextAsync(FolioKey.ForBranch("contract", "A", "1"), Ct));
    }

    [Fact]
    public async Task Errores_del_programador()
    {
        var folios = new InMemoryFolioGenerator();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await folios.NextAsync(null!, Ct));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await folios.NextAsync(new FolioKey("s", FolioScope.Tenant, null, null, null), Ct));
        Assert.Throws<ArgumentOutOfRangeException>(() => folios.Seed(FolioKey.Global("s"), -1));
    }
}
