"""Migra código C# de la API de FMOD for Unity a SoddDesign (reemplazos mecánicos y seguros).
Uso: python -I Tools/fmod_code_migrate.py <proyecto_unity> [--dry]
Lo que no puede migrar solo (EventDescription, callbacks, getPlaybackState, Bus...) queda marcado
por errores de compilación y se arregla a mano. Ver PROGRESS.md / docs de migración.
"""
import os, re, sys

root = sys.argv[1]
dry = '--dry' in sys.argv

RULES = [
    # usings
    (r'^[ \t]*using FMODUnity;[ \t]*\n', 'using Sodd.Design;\n'),
    (r'^[ \t]*using FMOD\.Studio;[ \t]*\n', ''),
    (r'^[ \t]*using FMOD;[ \t]*\n', ''),
    # tipos
    (r'\bFMODUnity\.EventReference\b', 'Sodd.Design.SoddEventReference'),
    (r'(?<![\w.])EventReference\b', 'SoddEventReference'),
    (r'\bFMOD\.Studio\.EventInstance\b', 'Sodd.Design.SoddEventInstance'),
    (r'(?<![\w.])EventInstance\b', 'SoddEventInstance'),
    # one-shots / instancias
    (r'\b(?:FMODUnity\.)?RuntimeManager\.PlayOneShotAttached\(\s*([^,]+?),\s*([^)]+?)\s*\)', r'Sodd.Design.SoddAudio.PlayOneShot(\1, (\2).transform)'),
    (r'\b(?:FMODUnity\.)?RuntimeManager\.PlayOneShot\(', 'Sodd.Design.SoddAudio.PlayOneShot('),
    (r'\b(?:FMODUnity\.)?RuntimeManager\.CreateInstance\(', 'Sodd.Design.SoddAudio.CreateInstance('),
    (r'\b(?:FMODUnity\.)?RuntimeManager\.AttachInstanceToGameObject\(\s*([^,]+?),\s*([^)]+?)\s*\)', r'\1.AttachTo((\2).transform)'),
    # métodos de instancia
    (r'\.start\(\)', '.Start()'),
    (r'\.release\(\)', '.Release()'),
    (r'\.isValid\(\)', '.IsValid'),
    (r'\.stop\(\s*(?:FMOD\.Studio\.)?STOP_MODE\.ALLOWFADEOUT\s*\)', '.Stop(Sodd.Design.SoddStopMode.AllowFadeOut)'),
    (r'\.stop\(\s*(?:FMOD\.Studio\.)?STOP_MODE\.IMMEDIATE\s*\)', '.Stop(Sodd.Design.SoddStopMode.Immediate)'),
    (r'\.setParameterByName\(', '.SetParameter('),
    (r'\.setPaused\(', '.SetPaused('),
    (r'\.set3DAttributes\(\s*(?:FMODUnity\.)?RuntimeUtils\.To3DAttributes\(\s*(.+?)\s*\)\s*\)', r'.Set3DPosition(Sodd.Design.SoddFmodCompat.Pos(\1))'),
    (r'\.set3DAttributes\(\s*([^;]+?)\.To3DAttributes\(\)\s*\)', r'.Set3DPosition(Sodd.Design.SoddFmodCompat.Pos(\1))'),
    (r'\b(?:FMODUnity\.)?EventNotFoundException\b', 'System.Exception'),
    (r'([\w\.\[\]]+)\.setVolume\((.+?)\);', r'\1.Volume = \2;'),
    (r'([\w\.\[\]]+)\.setPitch\((.+?)\);', r'\1.Pitch = \2;'),
    (r'([\w\.\[\]]+)\.clearHandle\(\);', r'\1 = default;'),
    (r'(\w+)\.handle\s*(==|!=)\s*(\w+)\.handle', r'\1 \2 \3'),
    (r'(\w+)\.getPlaybackState\(\s*out\s+(?:var|PLAYBACK_STATE|FMOD\.Studio\.PLAYBACK_STATE)\s+(\w+)\s*\);', r'var \2 = Sodd.Design.SoddFmodCompat.State(\1);'),
    (r'\b(?:FMOD\.Studio\.)?PLAYBACK_STATE\.STOPPED\b', 'Sodd.Design.SoddPlaybackState.Stopped'),
    (r'\b(?:FMOD\.Studio\.)?PLAYBACK_STATE\.PLAYING\b', 'Sodd.Design.SoddPlaybackState.Playing'),
    (r'\b(?:FMOD\.Studio\.)?PLAYBACK_STATE\.STOPPING\b', 'Sodd.Design.SoddPlaybackState.Stopping'),
    (r'\b(?:FMOD\.Studio\.)?PLAYBACK_STATE\.STARTING\b', 'Sodd.Design.SoddPlaybackState.Starting'),
    (r'(\w+)\.getTimelinePosition\(\s*out\s+int\s+(\w+)\s*\);', r'int \2 = UnityEngine.Mathf.RoundToInt(\1.TimelinePosition * 1000f);'),
    (r'\bFMOD\.Studio\.STOP_MODE\.IMMEDIATE\b', 'Sodd.Design.SoddStopMode.Immediate'),
    (r'\bFMOD\.Studio\.STOP_MODE\.ALLOWFADEOUT\b', 'Sodd.Design.SoddStopMode.AllowFadeOut'),
    (r'(?<![\w.])STOP_MODE\.IMMEDIATE\b', 'Sodd.Design.SoddStopMode.Immediate'),
    (r'(?<![\w.])STOP_MODE\.ALLOWFADEOUT\b', 'Sodd.Design.SoddStopMode.AllowFadeOut'),
    (r'\bFMOD\.Studio\.STOP_MODE\b', 'Sodd.Design.SoddStopMode'),
    (r'\.stop\(', '.Stop('),
]

changed = 0
for dp, dn, fn in os.walk(os.path.join(root, 'Assets')):
    if os.sep + 'Plugins' in dp or '/Plugins' in dp.replace('\\', '/'): continue
    for f in fn:
        if not f.endswith('.cs'): continue
        p = os.path.join(dp, f)
        raw = open(p, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        try:
            src = raw.decode('utf-8-sig'); enc = 'utf-8'
        except UnicodeDecodeError:
            src = raw.decode('cp1252'); enc = 'cp1252'
        crlf = '\r\n' in src
        src = src.replace('\r\n', '\n')
        if 'FMOD' not in src and 'RuntimeManager' not in src and 'EventReference' not in src: continue
        out = src
        for a, b in RULES:
            out = re.sub(a, b, out, flags=re.M)
        # Si quedaron tipos de SoddDesign sin calificar y falta el using (ej. se borró "using FMOD.Studio;"), agregarlo.
        if re.search(r'(?<![\w.])Sodd(EventInstance|EventReference|StopMode|Audio|Bus)\b', out) and 'using Sodd.Design;' not in out:
            out = 'using Sodd.Design;\n' + out
        if out != src:
            changed += 1
            print('migrado:', os.path.relpath(p, root))
            if not dry:
                if crlf: out = out.replace(chr(10), chr(13) + chr(10))
                with open(p, 'wb') as h: h.write((bytes([0xEF, 0xBB, 0xBF]) if bom else b'') + out.encode(enc))
print(changed, 'archivos')
