"""Convierte eventos de un proyecto de FMOD Studio (Metadata/*.xml) a archivos .sodd.json de SoddDesign.

Uso:
  python -I Tools/fmod_to_sodd.py <proyecto_fmod> <proyecto_unity> <evento> [<evento> ...] [--list]
  <evento> = path FMOD sin "event:/" (ej. Weapons/Blitz/BlitzShoot)

Qué convierte: tracks (nombre + volumen), SingleSound / MultiSound (→ RandomClip o Sequential), start/length,
timelockedOffset (→ startOffset), loop del instrumento, fades (curvas de fade de FMOD), volumen maestro,
randomizadores de pitch/volumen del master, LoopRegion y markers, 3D (spatializer + maximumDistance).
Copia los audios usados a <proyecto_unity>/Assets/SoddDesign/Audio/FMOD/<misma ruta que en FMOD>.
Lo que NO convierte (lo avisa): automatizaciones, efectos de FMOD, sends, parámetros con lógica.
El id del evento SoddDesign = GUID del evento de FMOD (trazabilidad); el path = path de FMOD (espacios → _).
"""
import json, math, os, shutil, sys, glob
import xml.etree.ElementTree as ET

def load(root):
    objs = {}
    for f in glob.glob(os.path.join(root, 'Metadata', '**', '*.xml'), recursive=True):
        try:
            for o in ET.parse(f).getroot().iter('object'):
                o.set('_file', f)
                objs[o.get('id')] = o
        except Exception:
            pass
    return objs

def P(o, n, d=None):
    v = o.find(f"property[@name='{n}']/value") if o is not None else None
    return v.text if v is not None else d

def R(o, n):
    return [d.text for d in o.findall(f"relationship[@name='{n}']/destination")] if o is not None else []

def F(o, n, d=0.0):
    v = P(o, n)
    return float(v) if v is not None else d

def rnd(x, n=4): return round(float(x), n)

def event_path(objs, ev):
    parts = [P(ev, 'name')]
    f = R(ev, 'folder')
    while f and f[0] in objs and objs[f[0]].get('class') == 'EventFolder':
        o = objs[f[0]]
        if P(o, 'name') is None: break
        parts.insert(0, P(o, 'name'))
        f = R(o, 'folder')
    return '/'.join(parts)

def convert(objs, ev, fmod_root, unity_root, warn):
    path = event_path(objs, ev)
    sodd_path = path.replace(' ', '_')
    eid = ev.get('id').strip('{}')
    master = next((o for o in objs.values() if o.get('class') == 'EventMixerMaster' and R(o, 'mixer') and R(ev, 'mixer') and R(o, 'mixer')[0] == R(ev, 'mixer')[0]), None)
    if master is None:  # eventos sin mixer explícito: buscar dentro del mismo archivo
        master = next((o for o in objs.values() if o.get('class') == 'EventMixerMaster' and o.get('_file') == ev.get('_file')), None)
    out = {'id': eid, 'path': sodd_path, 'description': f'Convertido desde FMOD event:/{path}.', 'tags': ['fmod'] + [path.split('/')[0].lower()]}
    # 3D
    props = objs.get((R(ev, 'automatableProperties') or [None])[0])
    has_spatial = any(objs.get(e) is not None and objs[e].get('class') == 'SpatialiserEffect'
                      for ch in R(master, 'effectChain') for e in R(objs.get(ch), 'effects')) if master is not None else False
    if has_spatial:
        out['is3D'] = True
        out['spatial'] = {'minDistance': rnd(F(props, 'minimumDistance', 1)), 'maxDistance': rnd(F(props, 'maximumDistance', 20))}
    top = path.split('/')[0].lower()
    out['bus'] = 'Music' if top == 'music' else 'UI' if top in ('ui', 'menu', 'vending', 'planetselect', 'firmselect') else 'SFX'
    if master is not None:
        if P(master, 'volume') is not None: out['volume'] = rnd(F(master, 'volume'), 2)
        mods_p, mods_v = [], []
        for m in R(master, 'modulators'):
            mo = objs.get(m)
            if mo is None: continue
            if mo.get('class') == 'RandomizerModulator':
                prop, amt = P(mo, 'nameOfPropertyBeingModulated'), F(mo, 'amount')
                if prop == 'pitch':
                    st = amt * 0.24  # FMOD guarda % de 24 semitonos (aprox.); rango total → ±mitad
                    mods_p.append({'type': 'Random', 'min': rnd(-st / 2, 3), 'max': rnd(st / 2, 3), 'unit': 'Semitones'})
                elif prop == 'volume':
                    mods_v.append({'type': 'Random', 'min': rnd(-amt, 2), 'max': 0, 'unit': 'dB'})
                else: warn(f'{path}: randomizer de {prop} no convertido')
            else: warn(f'{path}: modulador {mo.get("class")} del master no convertido')
        if mods_p: out['pitch'] = {'base': 0, 'modulators': mods_p}
        if mods_v: out['volume'] = {'base': out.get('volume', 0), 'modulators': mods_v}
    # tracks
    copied = {}
    def clip_of(af_id):
        af = objs.get(af_id)
        rel = P(af, 'assetPath')
        if rel is None: return None, 0
        src = os.path.join(fmod_root, 'Assets', rel)
        dst_rel = 'Assets/SoddDesign/Audio/FMOD/' + rel.replace('\\', '/')
        if os.path.exists(src):
            os.makedirs(os.path.dirname(os.path.join(unity_root, dst_rel)), exist_ok=True)
            if not os.path.exists(os.path.join(unity_root, dst_rel)): shutil.copy2(src, os.path.join(unity_root, dst_rel))
            copied[dst_rel] = True
        else: warn(f'{path}: falta el audio {src}')
        return dst_rel, F(af, 'length')
    def fade(inst, rel_name):
        c = objs.get((R(inst, rel_name) or [None])[0])
        if c is None: return 0.0
        a, b = objs.get((R(c, 'startPoint') or [None])[0]), objs.get((R(c, 'endPoint') or [None])[0])
        return max(0.0, F(b, 'position') - F(a, 'position')) if a is not None and b is not None else 0.0
    tracks = []
    for ti, t in enumerate(R(ev, 'groupTracks')):
        to = objs.get(t)
        mg = objs.get((R(to, 'mixerGroup') or [None])[0])
        tr = {'name': P(mg, 'name', f'Audio {ti + 1}'), 'type': 'Music' if out['bus'] == 'Music' else 'SFX'}
        if mg is not None and P(mg, 'volume') is not None: tr['volume'] = rnd(F(mg, 'volume'), 2)
        if R(to, 'automationTracks'): warn(f'{path} / {tr["name"]}: tiene automatizaciones (revisar a mano)')
        regions = []
        for m in R(to, 'modules'):
            mo = objs.get(m)
            if mo is None: continue
            cls = mo.get('class')
            start = F(mo, 'start')
            ins = {}
            if cls == 'SingleSound':
                clip, flen = clip_of((R(mo, 'audioFile') or [None])[0])
                if clip is None: continue
                ins = {'type': 'SingleClip', 'clip': clip}
            elif cls == 'MultiSound':
                clips, flen = [], 0
                for s in R(mo, 'sounds'):
                    c, l = clip_of((R(objs.get(s), 'audioFile') or [None])[0])
                    if c: clips.append(c); flen = max(flen, l)
                ins = {'type': 'Sequential' if P(mo, 'playlistMode') == '1' else 'RandomClip', 'clips': clips}
                if ins['type'] == 'RandomClip': ins['avoidRepeat'] = True
            else:
                warn(f'{path}: instrumento {cls} no soportado'); continue
            length = F(mo, 'length', flen or 1.0)
            if P(mo, 'looping') == 'true': ins['loop'] = True
            off = F(mo, 'timelockedOffset')
            if off > 0: ins['startOffset'] = rnd(off)
            if P(mo, 'volume') is not None: ins['volume'] = rnd(F(mo, 'volume'), 2)
            if P(mo, 'pitch') is not None: ins['pitch'] = rnd(F(mo, 'pitch'), 2)
            if R(mo, 'modulators'): warn(f'{path}: moduladores del instrumento no convertidos')
            reg = {'start': rnd(start), 'length': rnd(length), 'instrument': ins}
            fi, fo = fade(mo, 'fadeInCurve'), fade(mo, 'fadeOutCurve')
            if fi > 0: reg['fadeIn'] = rnd(fi)
            if fo > 0: reg['fadeOut'] = rnd(fo)
            regions.append(reg)
        tr['regions'] = regions
        tracks.append(tr)
    out['tracks'] = tracks
    tl = objs.get((R(ev, 'timeline') or [None])[0])
    loops, markers = [], []
    for mk in R(tl, 'markers'):
        mo = objs.get(mk)
        if mo is None: continue
        if mo.get('class') == 'LoopRegion':
            s = F(mo, 'position'); loops.append({'name': 'Loop', 'start': rnd(s), 'end': rnd(s + F(mo, 'length'))})
        elif mo.get('class') == 'NamedMarker':
            markers.append({'name': P(mo, 'name', 'Marker'), 'time': rnd(F(mo, 'position'))})
        else: warn(f'{path}: marker {mo.get("class")} no convertido')
    if loops: out['loopRegions'] = loops
    if markers: out['markers'] = markers
    if R(ev, 'parameters'): warn(f'{path}: tiene parámetros FMOD (revisar a mano si se usan)')
    return sodd_path, out, list(copied)

def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    fmod_root, unity_root, wanted = args[0], args[1], args[2:]
    objs = load(fmod_root)
    events = {event_path(objs, o): o for o in objs.values() if o.get('class') == 'Event'}
    if '--list' in sys.argv:
        print('\n'.join(sorted(events))); return
    warns = []
    for w in wanted:
        ev = events.get(w)
        if ev is None:
            print('NO EXISTE:', w); continue
        sodd_path, data, clips = convert(objs, ev, fmod_root, unity_root, warns.append)
        dst = os.path.join(unity_root, 'Assets', 'SoddDesign', 'Events', *sodd_path.split('/')) + '.sodd.json'
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, 'w', encoding='utf-8', newline='\n') as f:
            f.write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
        print('OK', w, '->', os.path.relpath(dst, unity_root), f'({len(clips)} audios)')
    for w in warns: print('AVISO', w)

if __name__ == '__main__':
    main()
