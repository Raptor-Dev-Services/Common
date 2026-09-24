# 01 - Instalar como submodulo de git

El codigo de `Common` vive dentro de tu repositorio, fijado a una version exacta. Tus proyectos lo
referencian con `ProjectReference`.

## 1. Agregar el submodulo y fijarlo a un tag

Desde la raiz de tu repo:

```bash
git submodule add https://github.com/Raptor-Dev-Services/Common Common
cd Common
git checkout v2.1.3          # un tag, nunca una rama
cd ..
git add .gitmodules Common
git commit -m "chore: agrega Common v2.1.3 como submodulo"
```

Lo que se commitea no es el codigo de `Common`: es un **puntero a un commit**. Quien clone tu repo
recibe exactamente esa version, hoy y dentro de un ano.

> Fija siempre a un tag. Un submodulo que sigue a `main` hace que dos clones del mismo commit de tu
> repo compilen codigo distinto.

## 2. Referenciar los proyectos

Define una propiedad con la ruta al submodulo, en el `Directory.Build.props` de la raiz de tu repo:

```xml
<Project>
  <PropertyGroup>
    <CommonRoot>$(MSBuildThisFileDirectory)Common/</CommonRoot>
  </PropertyGroup>
</Project>
```

Y cada proyecto referencia solo lo que su capa necesita:

```xml
<ItemGroup>
  <ProjectReference Include="$(CommonRoot)Common.Contracts/Common.Contracts.csproj" />
  <ProjectReference Include="$(CommonRoot)Common.Messaging/Common.Messaging.csproj" />
</ItemGroup>
```

| Capa de tu proyecto | Proyectos de Common |
|---|---|
| Domain | `Common.Contracts` |
| Application | `Common.Contracts` + `Common.Messaging` (+ `Common.MultiTenancy` si lee el tenant) |
| Infrastructure | `Common.Contracts` + `Common.Messaging` + `Common.MultiTenancy` + `Common.Infra` |
| Presentation | `Common.Contracts` + `Common.Messaging` + `Common.Web` |
| Host | `Common.Infra` + `Common.Web` |

Si tu proyecto no esta dividido en capas, referencia la facade `$(CommonRoot)Common/Common.csproj`, que
trae las cinco.

No hace falta agregar los proyectos de `Common` a tu `.sln`/`.slnx`: `dotnet build` los compila al
seguir las referencias. Agregalos solo si quieres verlos en el explorador de soluciones.

## 3. Excluir a Common de tu configuracion de compilacion

**Este paso es obligatorio si tu repo tiene `Directory.Build.props` o `Directory.Packages.props`.**

MSBuild aplica esos archivos a **todo** proyecto que este debajo de ellos en el arbol de carpetas, y
el submodulo esta debajo. Sin excluirlo:

- Con gestion central de paquetes (`ManagePackageVersionsCentrally`), `Common` no compila: sus
  `.csproj` fijan su propia `Version` y eso es el error **NU1008**.
- Con `TreatWarningsAsErrors`, cualquier warning de `Common` rompe tu build, y no lo puedes arreglar
  desde tu repo.
- Tu `TargetFramework`, tus analizadores o tu `LangVersion` se le aplican a un codigo que no los espera.

La forma de excluirlo es detectar si el proyecto que se esta compilando vive dentro del submodulo.
En tu `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <CommonRoot>$(MSBuildThisFileDirectory)Common/</CommonRoot>
    <IsCommonSubmodule Condition="$([MSBuild]::NormalizeDirectory('$(MSBuildProjectDirectory)').StartsWith('$([MSBuild]::NormalizeDirectory('$(CommonRoot)'))'))">true</IsCommonSubmodule>
  </PropertyGroup>

  <!-- Todo lo tuyo, solo para tus proyectos -->
  <PropertyGroup Condition="'$(IsCommonSubmodule)' != 'true'">
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Y en tu `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup Condition="'$(IsCommonSubmodule)' != 'true'">
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <!-- tus PackageVersion ... -->
</Project>
```

`Common` no tiene `Directory.Build.props` propio a proposito: uno ahi taparia el tuyo (MSBuild usa el
mas cercano) y esta condicion dejaria de evaluarse.

## 4. Clonar un repo que ya tiene el submodulo

```bash
git clone --recursive https://github.com/tu-org/tu-repo
```

Si ya lo clonaste sin `--recursive`, la carpeta `Common/` esta vacia y el build falla con "project
file not found". Se arregla con:

```bash
git submodule update --init --recursive
```

En CI, el checkout tiene que traer submodulos. En GitHub Actions:

```yaml
- uses: actions/checkout@v7
  with:
    submodules: recursive
```

## 5. Subir de version

```bash
cd Common
git fetch --tags
git checkout v2.2.0          # la version nueva
cd ..
git add Common
git commit -m "chore: sube Common a v2.2.0"
```

Antes de subir, lee en el [`CHANGELOG`](../../CHANGELOG.md) si la version nueva trae pasos de
migracion. El commit que mueve el puntero es el lugar para hacer esos ajustes.

## 6. Reglas del submodulo

- **Es de solo lectura desde tu repo.** No hagas cambios dentro de `Common/`: se pierden al mover el
  puntero y nadie mas los recibe. Un cambio en `Common` se hace en su repositorio y se publica como
  version nueva.
- **Un solo `Common` por solucion.** No mezcles el submodulo con los paquetes `Raptor.Common.*`.

## Problemas frecuentes

| Sintoma | Causa | Solucion |
|---|---|---|
| `Common/` esta vacia, "project file not found" | Se clono sin submodulos | `git submodule update --init --recursive` |
| `NU1008` en proyectos de `Common` | Tu gestion central de paquetes se le aplica | Paso 3 |
| Warnings de `Common` rompen tu build | Tu `TreatWarningsAsErrors` se le aplica | Paso 3 |
| `git status` muestra `modified: Common (new commits)` | Alguien movio el puntero, o entraste y cambiaste de commit | Si fue a proposito, commitea el puntero; si no, `git submodule update` |
| En CI falta `Common` | El checkout no trae submodulos | `submodules: recursive` en el checkout |
