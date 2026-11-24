#!/usr/bin/env bash
# CI local de SoddDesign: compila y corre tests en batchmode sobre una COPIA espejo del proyecto
# (_ci/project), para poder testear aunque el Editor de Unity esté abierto con el proyecto original.
# Uso: bash Tools/ci.sh [EditMode|PlayMode|both|compile]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe}"
MIRROR="$ROOT/_ci/project"
MODE="${1:-both}"
mkdir -p "$MIRROR"
WROOT=$(cygpath -w "$ROOT"); WMIR=$(cygpath -w "$MIRROR")
for d in Assets Packages ProjectSettings; do
  MSYS_NO_PATHCONV=1 robocopy "$WROOT/$d" "$WMIR/$d" /MIR /NFL /NDL /NJH /NJS /NP > /dev/null
done
run() {
  local plat="$1"; local log="$ROOT/_ci/log-$plat.txt"; local res="$ROOT/_ci/results-$plat.xml"
  rm -f "$res"
  if [ "$plat" = exec ]; then
    "$UNITY" -batchmode -nographics -projectPath "$WMIR" -quit -executeMethod "$EXEC_METHOD" -logFile "$(cygpath -w "$log")" > /dev/null 2>&1
  elif [ "$plat" = compile ]; then
    "$UNITY" -batchmode -nographics -projectPath "$WMIR" -quit -logFile "$(cygpath -w "$log")" > /dev/null 2>&1
  else
    "$UNITY" -batchmode -nographics -projectPath "$WMIR" -runTests -testPlatform "$plat" \
      -testResults "$(cygpath -w "$res")" -logFile "$(cygpath -w "$log")" > /dev/null 2>&1
  fi
  local code=$?
  echo "== $plat exit=$code"
  grep -E "(error|warning) CS[0-9]+" "$log" | grep -i "sodd\|Sandbox" | sort -u | head -40
  grep -E "^(\[SoddDesign\]|SoddDesign)" "$log" | sort -u | head -20
  if [ -f "$res" ]; then
    grep -o '<test-run[^>]*>' "$res" | grep -oE '(total|passed|failed|skipped)="[0-9]+"' | tr '\n' ' '; echo
    PYTHONIOENCODING=utf-8 python - "$res" <<'PY'
import sys, xml.etree.ElementTree as ET
t = ET.parse(sys.argv[1])
for c in t.iter('test-case'):
    if c.get('result') == 'Failed':
        m = c.find('.//message'); s = c.find('.//stack-trace')
        print('FAIL', c.get('fullname'), '\n   ', (m.text or '').strip()[:600], '\n   ', (s.text or '').strip()[:400] if s is not None else '')
PY
  elif [ "$plat" != compile ]; then
    echo "(sin results: ver $log)"; grep -iE "error|exception" "$log" | grep -v "^UnityEngine\|Licens" | head -20
  fi
}
EXEC_METHOD="${2:-}"
case "$MODE" in
  both) run EditMode; run PlayMode ;;
  *) run "$MODE" ;;
esac
# Traer de vuelta los archivos nuevos que generó Unity en el espejo (.meta, registro, escenas generadas
# por -executeMethod). Solo los que faltan en el original; un .meta solo si su archivo existe en el original.
cd "$MIRROR"
for pass in 1 2; do find Assets Packages -type f | sort | while read -r m; do
  [ -f "$ROOT/$m" ] && continue
  case "$m" in
    *.meta) src="${m%.meta}"; [ -e "$ROOT/$src" ] || continue ;;
    *) [ "$MODE" = exec ] || [ "$m" = "Assets/SoddDesign/Resources/SoddRegistry.asset" ] || continue ;;
  esac
  mkdir -p "$(dirname "$ROOT/$m")"; cp "$m" "$ROOT/$m"
done; done
