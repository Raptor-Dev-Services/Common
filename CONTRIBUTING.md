# Como contribuir

## Preparar el entorno

- .NET SDK 10.x
- Python 3 (para la puerta de dependencias)

```bash
git clone https://github.com/Raptor-Dev-Services/Common
cd Common
dotnet build Common.slnx
dotnet test Common.slnx
```

## Antes de dar un cambio por terminado

Las tres puertas, en verde:

```bash
dotnet build Common.slnx -c Release     # 0 errores y 0 warnings
dotnet test Common.slnx -c Release      # todas las pruebas
python scripts/check-prerelease-deps.py # solo dependencias estables
```

- **0 warnings.** Incluye CS1591: todo tipo y miembro publico nuevo lleva su comentario XML, porque
  es la documentacion que ven los consumidores en el IDE.
- **Un arreglo trae su prueba**, y la prueba tiene que fallar sin el arreglo: quitalo, comprueba que
  cae, y vuelve a ponerlo.
- **Solo dependencias estables**, con la version fijada
  ([regla](.claude/rules/stable-dependencies.md), [ADR-0007](docs/adr/0007-solo-dependencias-estables.md)).
- **No agregues un `Directory.Build.props` ni un `Directory.Packages.props` en la raiz.** Los
  consumidores por submodulo tienen los suyos una carpeta arriba; uno aqui los taparia y les rompe la
  compilacion. La configuracion va en cada `.csproj`.
- **No rompas un contrato publico en una version menor.** Renombrar, quitar o cambiar la firma de algo
  publico es version mayor, con su seccion de migracion en el `CHANGELOG`.

## Documentacion que acompana al cambio

| Si el cambio... | Actualiza |
|---|---|
| Agrega o cambia algo que el consumidor usa | La guia correspondiente en `docs/guia/` |
| Cambia un comportamiento que el SRS describe | `docs/srs.md`: el requisito y su fila en la matriz de verificacion |
| Es una decision de arquitectura (dependencia central, frontera entre ensamblados, politica) | Un ADR nuevo en `docs/adr/` |
| Cualquier cosa que el consumidor note | `CHANGELOG.md`, en la seccion "sin publicar" |

Un ADR aceptado no se edita: si la decision cambia, se escribe otro que lo reemplaza.

## Mensajes de commit

[Conventional Commits](https://www.conventionalcommits.org/es/): `feat:`, `fix:`, `docs:`,
`refactor:`, `test:`, `chore:`, `ci:`. El cuerpo explica el porque; el que ya esta en el diff.

## Publicar una version

1. En el `CHANGELOG`, cambia "sin publicar" por la version y la fecha.
2. Commit, y tag anotado:
   ```bash
   git tag -a v2.1.3 -m "v2.1.3: <resumen>"
   git push origin main v2.1.3
   ```
3. **Espejo NuGet.** El paquete `Raptor.Common.*` se publica desde el repositorio espejo
   [`Raptor057/ApiCommon`](https://github.com/Raptor057/ApiCommon), que es copia 1:1 de este:
   ```bash
   cd ../ApiCommon
   # 1) archivos nuevos en Common: ¿alguno se llama igual que uno propio del espejo?
   git -C ../Common diff --name-only --diff-filter=A v2.1.2 v2.1.3
   # 2) archivos borrados en Common: se borran tambien aqui
   git -C ../Common diff --name-only --diff-filter=D v2.1.2 v2.1.3
   git -C ../Common archive v2.1.3 | tar -x -C .   # copia encima
   echo 2.1.3 > version
   # agrega la entrada a CHANGELOG-NUGET.md (el CHANGELOG.md llega de aqui)
   git add -A && git commit -m "feat: sincroniza con Common v2.1.3"
   git tag -a v2.1.3 -m "v2.1.3 - espejo de Common v2.1.3"
   git push origin main v2.1.3                     # el tag dispara la publicacion
   ```
   Antes de empujar el tag, comprueba que cada archivo de Common coincide con el del espejo (el
   procedimiento completo y por que es asi estan en `docs/adr-nuget/` del espejo). La publicacion usa
   Trusted Publishing: no hay llave que configurar.
4. Los consumidores por submodulo suben cuando quieran, moviendo su puntero al tag nuevo.
