#!/usr/bin/env python3
"""Puerta de dependencias inestables.

Truena si el repositorio consume un paquete en version **prerelease**
(`-alpha`, `-beta`, `-rc`, `-preview`, `-dev`, `-next`, `-canary`...) o en
version **flotante** (`1.2.*`, `*`, `latest`), salvo que este declarado en el
archivo de excepciones.

Regla que hace cumplir: `stable-dependencies.md`.

Cubre los dos ecosistemas:
  - NuGet: *.csproj, *.props, *.targets, packages.config
  - npm:   package.json (dependencies, devDependencies, peer, optional)

Por que XML/JSON de verdad y no `grep`: el orden de los atributos de MSBuild no
esta fijado, `Version` puede ser atributo o elemento hijo, y una `"version"` en
package.json puede ser la del propio paquete y no la de una dependencia. Un
`grep` falla en los tres casos, y una guarda con falsos negativos es peor que no
tenerla: entrena a confiar en un verde que no midio nada.

Uso:
    check-prerelease-deps.py [--raiz DIR] [--permitidos ARCHIVO] [--formato texto|json]
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

# ---------------------------------------------------------------------------
# Configuracion

DIRS_IGNORADOS = {
    "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".venv",
    "dist", "build", "out", ".next", ".expo", ".nuxt", "vendor",
    "worktrees", "TestResults", "publish", "__pycache__",
}

# Etiqueta de prerelease segun SemVer: un guion seguido de algo que empieza por
# letra o digito, despues de la terna numerica. `1.15.0-beta.1`, `2.0.0-rc.3`,
# `3.0.0-alpha`, `1.0.0-0`.
RE_PRERELEASE = re.compile(r"^\s*[\^~>=<v\s]*\d+(?:\.\d+)*\s*-\s*[0-9A-Za-z.-]+")

# Especificadores npm que no son versiones semver y no se pueden juzgar aqui.
PREFIJOS_NO_SEMVER = (
    "file:", "link:", "workspace:", "git+", "git:", "github:", "npm:",
    "http://", "https://", "portal:", "patch:", "./", "../", "/",
)

ARCHIVO_PERMITIDOS = "PRERELEASE-PERMITIDOS.txt"


# ---------------------------------------------------------------------------
# Modelo

class Hallazgo:
    def __init__(self, ecosistema, paquete, version, archivo, linea, clase):
        self.ecosistema = ecosistema
        self.paquete = paquete
        self.version = version
        self.archivo = archivo
        self.linea = linea
        self.clase = clase  # "prerelease" | "flotante"

    @property
    def llave(self):
        return (self.ecosistema, self.paquete.lower(), self.version.strip())

    def __repr__(self):
        return f"{self.ecosistema}:{self.paquete}@{self.version}"


class Excepcion:
    def __init__(self, ecosistema, paquete, version, motivo, revisar_en, linea):
        self.ecosistema = ecosistema
        self.paquete = paquete
        self.version = version
        self.motivo = motivo
        self.revisar_en = revisar_en  # date | None
        self.linea = linea
        self.usada = False

    @property
    def llave(self):
        return (self.ecosistema, self.paquete.lower(), self.version.strip())


# ---------------------------------------------------------------------------
# Clasificacion de una version

def clasificar(spec: str):
    """Devuelve 'prerelease', 'flotante' o None."""
    if spec is None:
        return None
    s = spec.strip()
    if not s:
        return None

    # Referencias a propiedades de MSBuild: no se resuelven estaticamente.
    if "$(" in s:
        return None

    if s.startswith(PREFIJOS_NO_SEMVER):
        return None

    # Etiquetas de dist de npm: apuntan a lo que el registro diga HOY.
    if s.lower() in {"latest", "next", "canary", "beta", "alpha", "rc", "*", "x"}:
        return "flotante"

    # Comodines: `1.2.*`, `1.*`, `*`. Un build reproducible no los admite.
    if "*" in s or re.search(r"\bx\b", s):
        return "flotante"

    if RE_PRERELEASE.match(s):
        return "prerelease"

    return None


# ---------------------------------------------------------------------------
# Localizar la linea de un texto dentro de un archivo (para reportar file:line)

def linea_de(ruta, aguja):
    try:
        with open(ruta, "r", encoding="utf-8", errors="replace") as fh:
            for i, linea in enumerate(fh, 1):
                if aguja in linea:
                    return i
    except OSError:
        pass
    return 0


# ---------------------------------------------------------------------------
# NuGet

def _sin_ns(tag: str) -> str:
    return tag.split("}", 1)[-1]


def revisar_msbuild(ruta, hallazgos):
    try:
        arbol = ET.parse(ruta)
    except (ET.ParseError, OSError):
        return  # Un XML roto lo denuncia el build, no esta guarda.

    for nodo in arbol.iter():
        if _sin_ns(nodo.tag) not in ("PackageReference", "PackageVersion"):
            continue

        paquete = nodo.get("Include") or nodo.get("Update")
        if not paquete:
            continue

        version = nodo.get("Version") or nodo.get("VersionOverride")
        if version is None:
            for hijo in nodo:
                if _sin_ns(hijo.tag) in ("Version", "VersionOverride"):
                    version = (hijo.text or "").strip()
                    break

        clase = clasificar(version)
        if clase:
            hallazgos.append(Hallazgo(
                "nuget", paquete, version.strip(), ruta,
                linea_de(ruta, paquete), clase))


def revisar_packages_config(ruta, hallazgos):
    try:
        arbol = ET.parse(ruta)
    except (ET.ParseError, OSError):
        return
    for nodo in arbol.iter():
        if _sin_ns(nodo.tag) != "package":
            continue
        paquete = nodo.get("id")
        version = nodo.get("version")
        if not paquete:
            continue
        clase = clasificar(version)
        if clase:
            hallazgos.append(Hallazgo(
                "nuget", paquete, version.strip(), ruta,
                linea_de(ruta, paquete), clase))


# ---------------------------------------------------------------------------
# npm

BLOQUES_NPM = ("dependencies", "devDependencies",
               "peerDependencies", "optionalDependencies")


def revisar_package_json(ruta, hallazgos):
    try:
        with open(ruta, "r", encoding="utf-8", errors="replace") as fh:
            datos = json.load(fh)
    except (json.JSONDecodeError, OSError):
        return
    if not isinstance(datos, dict):
        return

    # Solo los bloques de DEPENDENCIAS. La clave `version` de primer nivel es la
    # del propio paquete: que una app sin publicar se llame 0.1.0-dev no es lo
    # que esta regla persigue.
    for bloque in BLOQUES_NPM:
        deps = datos.get(bloque)
        if not isinstance(deps, dict):
            continue
        for paquete, spec in deps.items():
            if not isinstance(spec, str):
                continue
            clase = clasificar(spec)
            if clase:
                hallazgos.append(Hallazgo(
                    "npm", paquete, spec.strip(), ruta,
                    linea_de(ruta, f'"{paquete}"'), clase))


# ---------------------------------------------------------------------------
# Recorrido

def recorrer(raiz):
    hallazgos = []
    for base, dirs, archivos in os.walk(raiz):
        dirs[:] = [d for d in dirs if d not in DIRS_IGNORADOS]
        for nombre in archivos:
            ruta = os.path.join(base, nombre)
            rel = os.path.relpath(ruta, raiz)
            if nombre.endswith((".csproj", ".props", ".targets", ".fsproj", ".vbproj")):
                revisar_msbuild(rel, hallazgos)
            elif nombre == "packages.config":
                revisar_packages_config(rel, hallazgos)
            elif nombre == "package.json":
                revisar_package_json(rel, hallazgos)
    return hallazgos


# ---------------------------------------------------------------------------
# Excepciones

def leer_permitidos(ruta):
    """Formato por linea:  ecosistema | paquete | version | motivo | YYYY-MM-DD"""
    permitidos = []
    errores = []
    if not os.path.exists(ruta):
        return permitidos, errores

    with open(ruta, "r", encoding="utf-8", errors="replace") as fh:
        for n, linea in enumerate(fh, 1):
            cruda = linea.strip()
            if not cruda or cruda.startswith("#"):
                continue
            campos = [c.strip() for c in cruda.split("|")]
            if len(campos) != 5:
                errores.append(f"{ruta}:{n}: se esperaban 5 campos separados por '|', hay {len(campos)}")
                continue
            eco, paquete, version, motivo, fecha = campos
            if eco not in ("nuget", "npm"):
                errores.append(f"{ruta}:{n}: ecosistema '{eco}' desconocido (usa nuget o npm)")
                continue
            if not motivo:
                errores.append(f"{ruta}:{n}: el motivo no puede ir vacio")
                continue
            try:
                revisar = _dt.date.fromisoformat(fecha)
            except ValueError:
                errores.append(f"{ruta}:{n}: fecha de revision invalida '{fecha}' (usa YYYY-MM-DD)")
                continue
            permitidos.append(Excepcion(eco, paquete, version, motivo, revisar, n))
    return permitidos, errores


# ---------------------------------------------------------------------------
# Informe

def main():
    ap = argparse.ArgumentParser(description="Puerta de dependencias inestables.")
    ap.add_argument("--raiz", default=".", help="raiz del repositorio (por defecto: .)")
    ap.add_argument("--permitidos", default=None,
                    help=f"archivo de excepciones (por defecto: <raiz>/{ARCHIVO_PERMITIDOS})")
    ap.add_argument("--formato", choices=("texto", "json"), default="texto")
    args = ap.parse_args()

    raiz = os.path.abspath(args.raiz)
    if not os.path.isdir(raiz):
        print(f"error: la raiz '{raiz}' no existe", file=sys.stderr)
        return 2
    os.chdir(raiz)

    ruta_permitidos = args.permitidos or ARCHIVO_PERMITIDOS
    permitidos, errores_permitidos = leer_permitidos(ruta_permitidos)
    por_llave = {e.llave: e for e in permitidos}

    hallazgos = recorrer(".")

    hoy = _dt.date.today()
    sin_declarar, caducadas, declaradas = [], [], []

    for h in hallazgos:
        exc = por_llave.get(h.llave)
        if exc is None:
            sin_declarar.append(h)
            continue
        exc.usada = True
        if exc.revisar_en < hoy:
            caducadas.append((h, exc))
        else:
            declaradas.append((h, exc))

    huerfanas = [e for e in permitidos if not e.usada]

    if args.formato == "json":
        print(json.dumps({
            "sin_declarar": [vars(h) for h in sin_declarar],
            "caducadas": [{"hallazgo": vars(h), "revisar_en": e.revisar_en.isoformat()}
                          for h, e in caducadas],
            "declaradas": [{"hallazgo": vars(h), "motivo": e.motivo,
                            "revisar_en": e.revisar_en.isoformat()} for h, e in declaradas],
            "huerfanas": [{"paquete": e.paquete, "version": e.version, "linea": e.linea}
                          for e in huerfanas],
            "errores_permitidos": errores_permitidos,
        }, indent=2, ensure_ascii=False))
        return 1 if (sin_declarar or caducadas or errores_permitidos) else 0

    # --- texto ---
    print("=" * 74)
    print("  PUERTA DE DEPENDENCIAS INESTABLES  (regla: stable-dependencies)")
    print("=" * 74)
    print(f"  raiz         : {raiz}")
    print(f"  excepciones  : {ruta_permitidos}"
          f"{'' if os.path.exists(ruta_permitidos) else '  (no existe: cero excepciones)'}")
    print(f"  revisadas    : {len(hallazgos)} referencias inestables encontradas en total")
    print()

    if errores_permitidos:
        print("!! El archivo de excepciones tiene errores de formato:")
        for e in errores_permitidos:
            print(f"   - {e}")
        print()

    if declaradas:
        print(f"-- {len(declaradas)} excepcion(es) vigente(s), permitidas:")
        for h, e in declaradas:
            print(f"   ok  {h.ecosistema}: {h.paquete} {h.version}")
            print(f"       motivo     : {e.motivo}")
            print(f"       revisar en : {e.revisar_en.isoformat()}")
        print()

    if huerfanas:
        print(f"-- {len(huerfanas)} excepcion(es) que ya no corresponden a ningun paquete del repo.")
        print("   Ya se resolvieron: borralas del archivo para que la lista no mienta.")
        for e in huerfanas:
            print(f"   ??  {ruta_permitidos}:{e.linea}  {e.ecosistema}: {e.paquete} {e.version}")
        print()

    if caducadas:
        print(f"** {len(caducadas)} excepcion(es) CADUCADA(S):")
        for h, e in caducadas:
            print(f"   XX  {h.ecosistema}: {h.paquete} {h.version}")
            print(f"       vencio el {e.revisar_en.isoformat()} (hoy es {hoy.isoformat()})")
            print(f"       {h.archivo}:{h.linea}")
        print()
        print("   Tocaba revisar si ya hay version estable. Si la hay, sube el paquete y borra")
        print("   la excepcion. Si sigue sin haberla, mueve la fecha y di por que.")
        print()

    if sin_declarar:
        pre = [h for h in sin_declarar if h.clase == "prerelease"]
        flo = [h for h in sin_declarar if h.clase == "flotante"]
        if pre:
            print(f"** {len(pre)} dependencia(s) PRERELEASE sin declarar:")
            for h in sorted(pre, key=lambda x: (x.ecosistema, x.paquete.lower(), x.archivo)):
                print(f"   XX  {h.ecosistema}: {h.paquete} {h.version}")
                print(f"       {h.archivo}:{h.linea}")
            print()
        if flo:
            print(f"** {len(flo)} dependencia(s) con version FLOTANTE sin declarar:")
            for h in sorted(flo, key=lambda x: (x.ecosistema, x.paquete.lower(), x.archivo)):
                print(f"   XX  {h.ecosistema}: {h.paquete} {h.version}")
                print(f"       {h.archivo}:{h.linea}")
            print()

    fallo = bool(sin_declarar or caducadas or errores_permitidos)

    print("-" * 74)
    if fallo:
        print("RESULTADO: ROJO")
        print()
        print("  Que hacer, en este orden:")
        print("   1. Sube el paquete a su ultima version ESTABLE. Es la salida correcta")
        print("      en la gran mayoria de los casos.")
        print("   2. Si de verdad no existe ninguna version estable publicada -compruebalo,")
        print("      no lo supongas- declara la excepcion en:")
        print(f"         {ruta_permitidos}")
        print("      formato:  ecosistema | paquete | version | motivo | YYYY-MM-DD")
        print()
        print("  Lo que NO es motivo valido: 'la estable tiene un bug', 'la beta trae la")
        print("  funcion que quiero', 'es solo para desarrollo'. Ver stable-dependencies.md.")
    else:
        print("RESULTADO: VERDE  -  no hay dependencias inestables sin declarar.")
    print("-" * 74)

    return 1 if fallo else 0


if __name__ == "__main__":
    sys.exit(main())
