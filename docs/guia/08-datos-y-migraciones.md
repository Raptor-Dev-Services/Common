# 08 - Datos y migraciones

`Common` no es un ORM. Trae tres cosas para trabajar con SQL directo, todas en `Common.Infra`:

| Pieza | Namespace | Para que |
|---|---|---|
| Fabricas de conexion | `Common.Data`, `Common.PostgreSql` | Abrir una conexion: fija, por nombre, por tenant |
| `DapperSqlDbConnectionBase` | `Common.Data` | Ejecutar SQL con Dapper, medido y registrado |
| `AddSchemaMigrations` | `Common.PostgreSql` | Aplicar scripts `.sql` al arrancar |

Las abstracciones sirven para cualquier motor; las piezas concretas (`Npgsql...`, migraciones) son
para PostgreSQL. Si usas EF Core, nada de esta guia te hace falta.

## Fabricas de conexion

Todas implementan `IOpenDbConnectionFactory`: `GetOpenConnectionAsync()` devuelve una conexion **ya
abierta**, y quien la pide la cierra (`using`).

### Una base, cadena en configuracion

Un tipo marcador por base, y la fabrica busca `ConnectionStrings:{NombreDelTipo}`:

```csharp
using Common.PostgreSql;

public sealed class VentasDb;   // marcador: busca ConnectionStrings:VentasDb

public sealed class VentasDbConnectionFactory(IConfiguration configuration)
    : ConfigurationNpgsqlConnectionFactory<VentasDb>(configuration);

builder.Services.AddSingleton<IOpenDbConnectionFactory, VentasDbConnectionFactory>();
```

```json
{ "ConnectionStrings": { "VentasDb": "Host=localhost;Database=ventas;Username=ventas_app;Password=..." } }
```

Si la cadena no existe, falla al construir la fabrica, con el nombre que busco.

Con varias bases, un marcador por base y una fabrica por marcador. Para otro motor, hereda de
`ConfigurationDbConnectionFactory<T>` y sobreescribe `CreateConnection`.

### Una base por tenant

```csharp
public sealed class TenantDbFactory(ITenantConnectionStringResolver resolver)
    : TenantNpgsqlConnectionFactory(resolver);                       // usa la cadena "Default" del tenant

public sealed class CurrentTenantDbFactory(ITenantContextAccessor accessor, ITenantOpenDbConnectionFactory tenants)
    : CurrentTenantNpgsqlConnectionFactory(accessor, tenants);       // la del tenant de la ejecucion actual

builder.Services.AddSingleton<ITenantOpenDbConnectionFactory, TenantDbFactory>();
builder.Services.AddSingleton<IOpenDbConnectionFactory, CurrentTenantDbFactory>();
```

Las cadenas salen del catalogo `MultiTenancy:Tenants:{id}:ConnectionStrings` ([guia 06](06-multi-tenancy.md)).
`CurrentTenant...` lanza si la ejecucion no tiene tenant: es lo correcto, porque sin tenant no hay a
que base ir.

> **Cuidado:** si el tenant no tiene la cadena pedida, el resolvedor devuelve la global
> `ConnectionStrings:Default`. Con una base por tenant, **no definas esa cadena global**: sin ella, un
> tenant mal configurado falla con un error claro en lugar de trabajar sobre la base de otro.

## `DapperSqlDbConnectionBase`

Ejecuta SQL con Dapper abriendo y cerrando su conexion en cada llamada, y **registra cada ejecucion**:

```csharp
builder.Services.AddScoped<IDapperSqlDbConnection, DapperSqlDbConnectionBase>();
```

```csharp
public sealed class ProductosRepository(IDapperSqlDbConnection db)
{
    public Task<IEnumerable<ProductoDto>> ListarAsync(int categoria, CancellationToken ct) =>
        db.QueryAsync<ProductoDto>(
            "SELECT id, nombre, precio FROM productos WHERE categoria_id = @categoria",
            new { categoria },
            queryName: "Productos.Listar",
            cancellationToken: ct);

    public Task<int> RenombrarAsync(int id, string nombre, CancellationToken ct) =>
        db.ExecuteAsync(
            "UPDATE productos SET nombre = @nombre WHERE id = @id",
            new { id, nombre },
            queryName: "Productos.Renombrar",
            cancellationToken: ct);
}
```

Metodos: `ExecuteAsync`, `ExecuteScalarAsync<T>`, `QueryAsync<T>`, `QuerySingleAsync<T>` (null si no
hay fila) y `QueryFirstAsync<T>` (null si no hay fila).

**Siempre parametros**, nunca texto concatenado: los parametros van como objeto anonimo o
`DynamicParameters`, igual que en Dapper.

Cada ejecucion deja un evento:

```
SQL Productos.Listar OK in 12 ms | hash: 5F3A... | params: {"categoria": 3}
```

| Duracion | Nivel |
|---|---|
| < 300 ms | el `level` que pases (por defecto `Debug`) |
| >= 300 ms | `Warning` (`SLOW`) |
| >= 1000 ms | `Error` (`SLOW`) |
| >= 2000 ms | `Critical` (`SLOW`) |
| Lanza | `Error` (`FAIL`) con la excepcion, y la relanza |

- `queryName` es lo que buscas en los logs; sin el, sale el nombre del metodo (`QueryAsync`).
- El **hash** (SHA-256 del SQL) agrupa las ejecuciones de la misma consulta sin guardar su texto.
- El texto del SQL solo se registra con `CustomLogging:IncludeSqlText: true`.
- Los **parametros se enmascaran** como los logs del pipeline ([guia 07](07-logging-y-observabilidad.md#enmascarado-de-secretos)):
  `PasswordHash`, `Token` y compania salen como `***`.

Para una transaccion que abarque varias sentencias, pide la conexion a la fabrica y usa Dapper
directo: el envoltorio abre una conexion por llamada, asi que dos llamadas no comparten transaccion.

## Migraciones al arranque

Aplica scripts `.sql` al arrancar la aplicacion, una sola vez cada uno:

```csharp
builder.Services.AddSingleton<IOpenDbConnectionFactory, VentasDbConnectionFactory>();
builder.Services.AddSchemaMigrations(o => o.ScriptsRelativePath = Path.Combine("Database", "Scripts"));
```

```
Database/Scripts/
  000_template.sql                 <- se ignora: plantilla para copiar
  001_crear_productos.sql
  002_indice_productos_nombre.sql
  003_agregar_columna_sku.sql
```

Y que los scripts se copien a la salida:

```xml
<ItemGroup>
  <None Update="Database/Scripts/*.sql" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Como funciona:

1. Crea, si no existe, la tabla `dbo.SchemaMigrations (ScriptName, AppliedAtUtc)`.
2. Toma los `.sql` de la carpeta (sin subcarpetas) en **orden alfabetico**, omitiendo los que empiezan
   con `000_template`.
3. Cada script que no este registrado se ejecuta **en su propia transaccion**, junto con su registro.
   Si falla, se revierte y **la aplicacion no arranca**.
4. Si la base aun no responde (arranque en contenedores), reintenta hasta 20 veces con espera creciente.
   **Problema conocido:** tambien reintenta los errores del propio SQL (un error de sintaxis es una
   `PostgresException`, que hereda de `NpgsqlException`). Un script roto tarda unos 85 segundos en
   tumbar el arranque, con 19 avisos de "la base aun no esta lista" que no son ciertos: busca el
   error real en la excepcion de cada aviso.
5. La migracion usa el `IOpenDbConnectionFactory` registrado. Si ese es uno "por tenant actual",
   al arrancar no hay tenant y falla: registra una fabrica fija para la base que se migra.

La carpeta se busca primero junto al ejecutable y luego en el directorio actual; por defecto es
`Services/Schema Migration/Tables`. Si no existe, avisa y sigue.

Reglas para los scripts:

- **Numera con ceros a la izquierda** (`001_`, `002_`): el orden es alfabetico, y `10_` iria antes que `9_`.
- **Un script aplicado no se edita.** Se registra por nombre: editarlo no lo vuelve a correr. Un
  cambio es un script nuevo.
- **Escribelos idempotentes** cuando se pueda (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`).
- Nada que no pueda ir en una transaccion (`CREATE INDEX CONCURRENTLY`): fallaria.

**Con que usuario corren:** con el de la conexion de la fabrica, que normalmente es el de la
aplicacion. Si en produccion la aplicacion usa un rol sin permisos de DDL (lo recomendable), las
migraciones fallan, y es lo correcto: en ese caso correlas como un paso aparte del despliegue, con el
rol dueno del esquema, y no registres `AddSchemaMigrations` en produccion.
