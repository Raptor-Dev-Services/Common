using Common.Workflow.Folios;

namespace Common.Workflow.Tests.Folios;

/// <summary>C05: la expresion SQL de scope_key que usa la migracion de relleno.</summary>
public class FolioBackfillTests
{
    [Fact]
    public void Produce_la_misma_forma_que_ToScopeKey()
    {
        Assert.Equal("'global'", FolioBackfill.ScopeKeySql(FolioScope.Global));
        Assert.Equal("'t:' || tenant_id::text", FolioBackfill.ScopeKeySql(FolioScope.Tenant));
        Assert.Equal("'t:' || tenant_id::text || '/b:' || branch_id::text", FolioBackfill.ScopeKeySql(FolioScope.Branch));
    }

    [Fact]
    public void Admite_columnas_entre_comillas_calificadas_y_periodo()
    {
        Assert.Equal(
            "'t:' || t.\"TenantId\"::text || '/b:' || \"BranchId\"::text || '/p:' || period::text",
            FolioBackfill.ScopeKeySql(FolioScope.Branch, "t.\"TenantId\"", "\"BranchId\"", "period"));
        Assert.Equal("'global' || '/p:' || _anio::text",
            FolioBackfill.ScopeKeySql(FolioScope.Global, periodColumn: "_anio"));
    }

    [Theory]
    [InlineData("tenant_id; DROP TABLE x")]
    [InlineData("1tenant")]
    [InlineData("")]
    [InlineData("a.b.c")]
    [InlineData("\"a\"b\"")]
    [InlineData("\"\"")]
    [InlineData("tenant-id")]
    public void Rechaza_lo_que_no_es_un_identificador(string columna)
    {
        Assert.Throws<ArgumentException>(() => FolioBackfill.ScopeKeySql(FolioScope.Tenant, columna));
        Assert.Throws<ArgumentException>(() => FolioBackfill.ScopeKeySql(FolioScope.Branch, branchColumn: columna));
        Assert.Throws<ArgumentException>(() => FolioBackfill.ScopeKeySql(FolioScope.Global, periodColumn: columna));
    }

    [Fact]
    public void Ambito_desconocido_es_error_del_programador()
    {
        Assert.Throws<ArgumentException>(() => FolioBackfill.ScopeKeySql((FolioScope)9));
        Assert.Throws<ArgumentException>(() => FolioBackfill.ScopeKeySql(FolioScope.Tenant, null!));
    }
}
