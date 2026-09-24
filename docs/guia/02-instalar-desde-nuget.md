# 02 - Instalar desde NuGet

`Common` se publica en nuget.org como seis paquetes, uno por ensamblado, todos con la misma version:

| Paquete | Ensamblado | Trae consigo |
|---|---|---|
| `Raptor.Common` | facade (sin codigo) | los cinco de abajo |
| `Raptor.Common.Contracts` | `Common.Contracts` | nada |
| `Raptor.Common.Messaging` | `Common.Messaging` | Contracts |
| `Raptor.Common.MultiTenancy` | `Common.MultiTenancy` | ASP.NET Core, Serilog |
| `Raptor.Common.Infra` | `Common.Infra` | Contracts, Messaging, MultiTenancy, Serilog, OpenTelemetry, Dapper, Npgsql |
| `Raptor.Common.Web` | `Common.Web` | Contracts, MultiTenancy, ASP.NET Core |

La version de los paquetes es la del tag del repositorio: `Raptor.Common 2.1.3` es el codigo del
tag `v2.1.3`.

## Instalar todo

Para una API sin capas separadas:

```bash
dotnet add package Raptor.Common --version 2.1.3
```

```xml
<ItemGroup>
  <PackageReference Include="Raptor.Common" Version="2.1.3" />
</ItemGroup>
```

## Instalar por capa

Si tu solucion esta dividida en capas, cada proyecto trae solo lo suyo:

| Capa de tu proyecto | Paquetes |
|---|---|
| Domain | `Raptor.Common.Contracts` |
| Application | `Raptor.Common.Contracts` + `Raptor.Common.Messaging` (+ `Raptor.Common.MultiTenancy` si lee el tenant) |
| Infrastructure | + `Raptor.Common.MultiTenancy` + `Raptor.Common.Infra` |
| Presentation | `Raptor.Common.Contracts` + `Raptor.Common.Messaging` + `Raptor.Common.Web` |
| Host | `Raptor.Common.Infra` + `Raptor.Common.Web` |

Asi el dominio no arrastra Npgsql, OpenTelemetry ni ASP.NET.

## Con gestion central de paquetes

Si tu repo usa `Directory.Packages.props`, la version va ahi y los `.csproj` la citan sin `Version`.
Fija todos los `Raptor.Common.*` a la **misma** version:

```xml
<!-- Directory.Packages.props -->
<ItemGroup>
  <PackageVersion Include="Raptor.Common.Contracts" Version="2.1.3" />
  <PackageVersion Include="Raptor.Common.Messaging" Version="2.1.3" />
  <PackageVersion Include="Raptor.Common.Web" Version="2.1.3" />
</ItemGroup>
```

```xml
<!-- Un .csproj -->
<ItemGroup>
  <PackageReference Include="Raptor.Common.Contracts" />
</ItemGroup>
```

A diferencia del submodulo, aqui no hay nada que excluir: los paquetes llegan compilados y tu
`Directory.Build.props` no los toca.

## Fijar la version

Fija la version exacta. No uses rangos ni comodines (`2.*`, `[2.0,3.0)`): un build tiene que dar el
mismo resultado hoy y dentro de seis meses, y una version nueva de `Common` se adopta leyendo su
[`CHANGELOG`](../../CHANGELOG.md), no por sorpresa.

## Depurar dentro de la libreria

Los paquetes publican simbolos (`.snupkg`) y llevan SourceLink. En Visual Studio o Rider, desactiva
"Just My Code" y habilita la descarga de simbolos de nuget.org: el depurador entra al codigo de
`Common` en la version exacta que tienes instalada.

## Dependencias de Common

Los paquetes declaran sus dependencias (Serilog, OpenTelemetry, Npgsql...) como **version minima**.
Si otro paquete de tu proyecto pide una version mayor de alguna, NuGet sube a esa. Es lo normal en
NuGet, y es la unica diferencia de comportamiento real frente al submodulo, que compila con las
versiones exactas de sus `.csproj`.

## Cambiar de submodulo a NuGet (o al reves)

El codigo no cambia. Solo cambian las referencias:

1. Quita las `ProjectReference` a `$(CommonRoot)...` y agrega los `PackageReference` equivalentes de
   la tabla de arriba, con la version que corresponde al tag que tenias fijado.
2. Quita el submodulo: `git rm Common` y borra su entrada de `.gitmodules`.
3. Si ya no hay submodulo, la exclusion de la guia 01 (paso 3) sobra; puedes dejarla o quitarla.

Al reves es el mismo camino en sentido contrario. En ningun momento tengas los dos a la vez.
