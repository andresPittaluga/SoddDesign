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

def convert_mods(objs, owner, path, warn, where):
    pitch, vol = [], []
    for m in R(owner, 'modulators'):
        mo = objs.get(m)
        if mo is None: continue
        prop, cls = P(mo, 'nameOfPropertyBeingModulated'), mo.get('class')
        if cls == 'RandomizerModulator':
            amt = F(mo, 'amount')
            if prop == 'pitch':
                st = amt * 0.24
                pitch.append({'type': 'Random', 'min': rnd(-st / 2, 3), 'max': rnd(st / 2, 3), 'unit': 'Semitones'})
            elif prop == 'volume':
                vol.append({'type': 'Random', 'min': rnd(-amt, 2), 'max': 0, 'unit': 'dB'})
            else: warn(f'{path}: randomizer de {prop} ({where}) no convertido')
        elif cls == 'ADSRModulator' and prop == 'volume':
            vol.append({'type': 'ADSR', 'attack': rnd(F(mo, 'attackTime') / 1000), 'decay': rnd(F(mo, 'decayTime') / 1000),
                        'sustain': rnd(F(mo, 'sustainLevel', 1)), 'release': rnd(F(mo, 'releaseTime') / 1000)})
        else: warn(f'{path}: modulador {cls} sobre {prop} ({where}) no convertido')
    return pitch, vol

import math as _m
def st(r): return round(12 * _m.log2(max(r, 1e-4)), 2)

def apply_automations(objs, ev, out, tracks, master, path, warn):
    owners = {}
    for o in objs.values():
        if o.get('_file') == ev.get('_file'):
            for a in R(o, 'automators'): owners[a] = o
    # efecto → track (por la cadena de efectos del EventMixerGroup del track)
    fx_track = {}
    for ti, t in enumerate(tracks):
        for ch in R(objs.get(t.get('_mg')), 'effectChain'):
            for e in R(objs.get(ch), 'effects'): fx_track[e] = ti
    master_fx = set(e for ch in R(master, 'effectChain') for e in R(objs.get(ch), 'effects')) if master is not None else set()
    params = {}
    def regions_of(mod):
        return [r for t in tracks for r in t['regions'] if r.get('_mod') == mod]
    for a, tgt in owners.items():
        ao = objs.get(a); prop = P(ao, 'nameOfPropertyBeingAutomated'); cls = tgt.get('class')
        for c in R(ao, 'automationCurves'):
            co = objs.get(c); par = objs.get((R(co, 'parameter') or [None])[0])
            pts = sorted((F(objs[q], 'position'), F(objs[q], 'value')) for q in R(co, 'automationPoints') if q in objs)
            if not pts: continue
            timeline = par is not None and par.get('class') == 'Timeline'
            pname = None
            if not timeline and par is not None:
                gp = par.get('id') if par.get('class') == 'GameParameter' else (R(par, 'preset') or [None])[0]
                pp = next((x for x in objs.values() if x.get('class') == 'ParameterPreset' and (R(x, 'parameter') or [None])[0] == gp), None)
                pname = P(pp, 'name')
                if pname: params[pname] = True
            if timeline and len(pts) < 2: continue  # punto único = constante neutra
            done = False
            if cls == 'PitchShifterEffect' and prop == 'pitch':
                tracks_hit = [tracks[fx_track[tgt.get('id')]]] if tgt.get('id') in fx_track else (tracks if tgt.get('id') in master_fx else [])
                for t in tracks_hit:
                    for r in t['regions']:
                        if timeline:
                            (t0, v0), (t1, v1) = pts[0], pts[-1]
                            r['instrument']['pitch'] = {'base': st(v0), 'modulators': [{'type': 'Envelope', 'attack': rnd(max(0.001, t1 - t0)), 'hold': 600, 'release': 0, 'depth': rnd(st(v1) - st(v0), 2)}]}
                        else:
                            r['instrument']['pitch'] = {'base': 0, 'modulators': [{'type': 'ParameterCurve', 'parameter': pname, 'unit': 'Linear', 'curve': [[rnd(x), rnd(y)] for x, y in pts]}]}
                done = bool(tracks_hit)
            elif cls == 'EventMixerMaster' and prop in ('pitch', 'volume') and not timeline and pname:
                cur = out.get(prop, 0)
                if not isinstance(cur, dict): cur = {'base': cur, 'modulators': []}
                cur['modulators'].append({'type': 'ParameterCurve', 'parameter': pname, 'curve': [[rnd(x), rnd(y)] for x, y in pts]})
                out[prop] = cur; done = True
            elif cls in ('SingleSound', 'MultiSound') and prop == 'volume' and timeline and pts[-1][1] <= -79 and len(pts) == 2:
                for r in regions_of(tgt.get('id')):
                    v = r['instrument'].get('volume', 0)
                    if not isinstance(v, dict): v = {'base': v, 'modulators': []}
                    rel0 = pts[0][0] - r['start']
                    v['modulators'].append({'type': 'Envelope', 'attack': 0, 'hold': rnd(max(0, rel0)), 'release': rnd(pts[1][0] - pts[0][0])})
                    r['instrument']['volume'] = v
                done = True
            elif cls == 'TremoloEffect' and not timeline and pname:
                idx = {'frequency': 'rate', 'depth': 'depth'}.get(prop)
                hit = [tracks[fx_track[tgt.get('id')]]] if tgt.get('id') in fx_track else tracks
                for t in hit:
                    fx = next((f for f in t.setdefault('effects', []) if f['type'] == 'Tremolo'), None)
                    if fx is None: fx = {'type': 'Tremolo', 'params': {}}; t['effects'].append(fx)
                    if idx: fx['params'][idx] = {'base': 0, 'modulators': [{'type': 'ParameterCurve', 'parameter': pname, 'curve': [[rnd(x), rnd(y)] for x, y in pts]}]}
                done = idx is not None
            if not done: warn(f'{path}: automatización de {cls}.{prop} sobre {"timeline" if timeline else pname} no convertida')
    if params:
        out['parameters'] = [{'name': n, 'min': 0, 'max': 1, 'default': 0, 'description': 'Parámetro de FMOD (lo setea el código).'} for n in params]

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
    if P(props, 'maxVoices') is not None:
        out['maxInstances'] = int(F(props, 'maxVoices'))
        steal = P(props, 'voiceStealing')
        if steal is not None:
            mode = {'0': 'Oldest', '1': 'Quietest', '3': 'None'}.get(steal)
            if mode: out['stealMode'] = mode
            else: warn(f'{path}: voiceStealing={steal} sin equivalente, queda Oldest')
    mi = objs.get((R(ev, 'mixerInput') or [None])[0])
    grp = objs.get((R(mi, 'output') or [None])[0])
    out['bus'] = P(grp, 'name', 'Master') if grp is not None and grp.get('class') == 'MixerGroup' else 'Master'
    if master is not None:
        if P(master, 'volume') is not None: out['volume'] = rnd(F(master, 'volume'), 2)
        mods_p, mods_v = convert_mods(objs, master, path, warn, 'master')
        base_p = rnd(F(master, 'pitch'), 2) if P(master, 'pitch') is not None else 0
        if mods_p: out['pitch'] = {'base': base_p, 'modulators': mods_p}
        elif base_p: out['pitch'] = base_p
        if mods_v: out['volume'] = {'base': out.get('volume', 0), 'modulators': mods_v}
    # tracks
    copied = {}
    def clip_of(af_id):
        af = objs.get(af_id)
        rel = P(af, 'assetPath')
        if rel is None: return None, 0
        src = os.path.join(fmod_root, 'Assets', rel)
        dst_rel = 'Assets/SoddDesign/Audio/FMOD/' + rel.replace('\\', '/')
        if unity_root is None: return dst_rel, F(af, 'length')
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
    def inst_region(m, start_override=None):
        mo = objs.get(m)
        if mo is None: return None
        cls = mo.get('class')
        start = F(mo, 'start') if start_override is None else start_override
        ins = {}
        if cls == 'SingleSound':
            clip, flen = clip_of((R(mo, 'audioFile') or [None])[0])
            if clip is None: return None
            ins = {'type': 'SingleClip', 'clip': clip}
        elif cls == 'MultiSound':
            clips, flen = [], 0
            for s in R(mo, 'sounds'):
                c, l = clip_of((R(objs.get(s), 'audioFile') or [None])[0])
                if c: clips.append(c); flen = max(flen, l)
            if not clips: return None  # MultiSound vacío en FMOD: no suena
            ins = {'type': 'Sequential' if P(mo, 'playlistMode') == '1' else 'RandomClip', 'clips': clips}
            if ins['type'] == 'RandomClip': ins['avoidRepeat'] = True
        else:
            warn(f'{path}: instrumento {cls} no soportado'); return None
        length = F(mo, 'length', flen or 1.0)
        if P(mo, 'looping') == 'true': ins['loop'] = True
        off = F(mo, 'timelockedOffset')
        if off > 0: ins['startOffset'] = rnd(off)
        if P(mo, 'volume') is not None: ins['volume'] = rnd(F(mo, 'volume'), 2)
        if P(mo, 'pitch') is not None: ins['pitch'] = rnd(F(mo, 'pitch'), 2)
        ip, iv = convert_mods(objs, mo, path, warn, 'instrumento')
        if ip: ins['pitch'] = {'base': ins.get('pitch', 0), 'modulators': ip}
        if iv: ins['volume'] = {'base': ins.get('volume', 0), 'modulators': iv}
        reg = {'start': rnd(start), 'length': rnd(length), 'instrument': ins, '_mod': m}
        fi, fo = fade(mo, 'fadeInCurve'), fade(mo, 'fadeOutCurve')
        if fi > 0: reg['fadeIn'] = rnd(fi)
        if fo > 0: reg['fadeOut'] = rnd(fo)
        return reg

    tracks = []
    for ti, t in enumerate(R(ev, 'groupTracks')):
        to = objs.get(t)
        mg = objs.get((R(to, 'mixerGroup') or [None])[0])
        tr = {'name': P(mg, 'name', f'Audio {ti + 1}'), 'type': 'Music' if out['bus'] == 'Music' else 'SFX', '_mg': (R(to, 'mixerGroup') or [None])[0]}
        if mg is not None and P(mg, 'volume') is not None: tr['volume'] = rnd(F(mg, 'volume'), 2)
        regions = [r for r in (inst_region(m) for m in R(to, 'modules')) if r]
        tr['regions'] = regions
        tracks.append(tr)
    for sh in (o for o in objs.values() if o.get('class') == 'ActionSheet' and o.get('_file') == ev.get('_file')):
        consecutive = P(sh, 'playbackMode') == '1'
        t0 = 0.0
        for k, m in enumerate(R(sh, 'modules')):
            reg = inst_region(m, t0 if consecutive else 0.0)
            if reg is None: continue
            if consecutive: t0 += reg['length']
            tracks.append({'name': f'Action {k + 1}', 'type': 'Music' if out['bus'] == 'Music' else 'SFX', 'regions': [reg]})
    apply_automations(objs, ev, out, tracks, master, path, warn)
    for t in tracks:
        t.pop('_mg', None)
        for r in t['regions']: r.pop('_mod', None)
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
    return sodd_path, out, list(copied)

def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    fmod_root, unity_root, wanted = args[0], args[1], args[2:]
    objs = load(fmod_root)
    events = {event_path(objs, o): o for o in objs.values() if o.get('class') == 'Event'}
    if '--list' in sys.argv:
        print('\n'.join(sorted(events))); return
    warns = []
    if '--all' in sys.argv: wanted = sorted(events)
    dry = '--dry' in sys.argv
    for w in wanted:
        ev = events.get(w)
        if ev is None:
            print('NO EXISTE:', w); continue
        sodd_path, data, clips = convert(objs, ev, fmod_root, None if dry else unity_root, warns.append)
        if dry: continue
        dst = os.path.join(unity_root, 'Assets', 'SoddDesign', 'Events', *sodd_path.split('/')) + '.sodd.json'
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, 'w', encoding='utf-8', newline='\n') as f:
            f.write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
        print('OK', w, '->', os.path.relpath(dst, unity_root), f'({len(clips)} audios)')
    for w in warns: print('AVISO', w)

if __name__ == '__main__':
    main()
