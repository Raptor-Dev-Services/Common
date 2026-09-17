<!-- Plantilla generica del catalogo Raptor. Rellena los {{PLACEHOLDERS}} - ver templates/project-variables.md -->

# Dependencias estables

> **Regla de oro.** Un proyecto solo consume dependencias en **version estable publicada**.
> Nada de `-alpha`, `-beta`, `-rc`, `-preview`, `-dev`, `-next`, `-canary`, `-pre`, ni versiones
> flotantes. Aplica a NuGet, npm y a cualquier gestor de paquetes del proyecto.

## La ley

1. **Toda dependencia va en su ultima version estable.** Si la version instalada tiene una
   etiqueta de prerelease, el cambio no se mergea.
2. **Toda version va fijada.** Nada de comodines (`1.2.*`, `*`) ni de etiquetas del registro
   (`latest`, `next`). Un build tiene que dar el mismo resultado hoy y dentro de seis meses.
3. **El SDK y el framework tambien cuentan.** `global.json` no fija un SDK preview, y ningun
   proyecto apunta a un `TargetFramework` que aun no se libero.
4. **Esto no distingue entre desarrollo y produccion.** Una dependencia de desarrollo entra al
   build, al pipeline y a la superficie de ataque igual que las demas.

## Por que

Un prerelease no es "una version un poco mas nueva". Es una version con **garantias distintas**:

- **Puede cambiar su API sin aviso** entre `beta.1` y `beta.2`. El versionado semantico solo
  promete compatibilidad a partir de la estable; antes de eso, todo puede moverse.
- **Puede desaparecer del feed.** Un prerelease se despublica sin ceremonia, y el dia que pasa,
  el build deja de restaurar. No en tu maquina -donde ya esta en cache- sino en CI y en el
  servidor de despliegue, que es donde duele.
- **No recibe parches de seguridad.** Cuando sale un CVE, se corrige en la linea estable. La
  beta de hace ocho meses no se parchea: se abandona.
- **No tiene soporte ni ciclo de vida.** No hay fecha de fin de soporte porque nunca hubo
  comienzo, y ninguna politica de la organizacion lo cubre.
- **Contamina hacia abajo.** NuGet exige que, para referenciar un prerelease, el paquete que lo
  consume tambien pueda serlo. Un beta en una libreria compartida obliga a beta a todo lo que
  la use.

## La unica excepcion valida

**Que el paquete no tenga ninguna version estable publicada, nunca.** No "la estable esta
vieja": que no exista. Es un caso raro pero real: hay paquetes de ecosistemas grandes que
llevan anios en beta permanente y no tienen alternativa.

Se comprueba, no se supone:

```bash
# NuGet: si la lista de versiones sin guion sale vacia, no hay estable.
curl -s "https://api.nuget.org/v3-flatcontainer/<paquete-en-minusculas>/index.json" \
  | tr ',' '\n' | tr -d '"[]{} ' | grep -E '^[0-9]' | grep -v -- -

# npm
npm view <paquete> versions --json
```

Si de verdad no hay estable, **se declara**. La excepcion vive en `PRERELEASE-PERMITIDOS.txt`
en la raiz del repositorio, una linea por paquete:

```
# ecosistema | paquete | version | motivo | revisar-en (YYYY-MM-DD)
nuget | Ejemplo.Paquete.Sin.Estable | 1.15.0-beta.1 | 34 versiones publicadas, 0 estables. Sin alternativa. | 2026-12-31
```

Los cinco campos son obligatorios y la fecha tiene dientes: **cuando vence, la puerta se pone
roja.** Eso es lo que convierte la excepcion en temporal en vez de permanente. Al vencer se
comprueba otra vez si ya hay estable; si la hay, se sube y se borra la linea; si no, se mueve
la fecha y se dice por que.

### Lo que NO es motivo valido

| Excusa | Que hacer en su lugar |
|---|---|
| "La estable tiene un bug que la beta arregla" | Reportarlo, y mientras tanto rodearlo en tu codigo o fijar la estable anterior. |
| "La beta trae la funcion que quiero" | Esperar, o resolverlo sin esa funcion. Una funcion no vale un build que no restaura. |
| "Es solo para desarrollo / solo para pruebas" | Entra al pipeline igual. No hay dependencias de segunda. |
| "Ya estaba asi cuando llegue" | Es exactamente el caso que esta regla persigue. |
| "La estable no soporta el framework nuevo" | Entonces el framework nuevo todavia no esta listo para este proyecto. |

## Como se hace cumplir

Con `scripts/check-prerelease-deps.py`, que parsea el XML de MSBuild y el JSON de npm -no es un
`grep`- y termina en codigo 1 si encuentra algo inestable sin declarar:

```bash
python3 scripts/check-prerelease-deps.py
```

Corre en tres sitios, y los tres importan:

- **En local**, enganchado a `/check` y `/verify`.
- **En CI**, como paso obligatorio del gate de PR (regla `ci-quality-gates`). Falla el job a
  proposito: un aviso que no rompe nada se lee una vez y se ignora despues.
- **Al revisar**, como parte de `/code-review` sobre un diff que toca archivos de proyecto.

La puerta tambien se pone roja si el archivo de excepciones esta mal formado o si una excepcion
vencio, y avisa -sin tronar- de las excepciones que ya no corresponden a ningun paquete, para
que la lista no acabe mintiendo.

## Antipatrones

- Instalar con `--prerelease` o `npm install pkg@next` "para probar" y dejarlo commiteado.
- Marcar el paso de CI como opcional o `continue-on-error` para desbloquear un merge.
- Eximir una ruta entera del detector en vez de declarar el paquete concreto.
- Declarar una excepcion con motivo vacio o generico ("hace falta", "temporal").
- Renovar la fecha de una excepcion sin volver a comprobar si ya salio la estable.
- Poner un prerelease en una libreria compartida: contamina a todos sus consumidores.
- Fijar dependencias con comodin para "recibir arreglos solos". Lo que se recibe solo tambien
  son los cambios que rompen, y sin que nadie lo decida.
