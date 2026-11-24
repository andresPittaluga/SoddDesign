"""Lee un proyecto de FMOD Studio (Metadata/*.xml) y resume eventos: path, instrumentos, clips, parámetros.
Uso: python -I Tools/fmod_inspect.py <ruta proyecto FMOD> [filtro]"""
import sys, os, glob, xml.etree.ElementTree as ET, json
root = sys.argv[1]; flt = sys.argv[2].lower() if len(sys.argv) > 2 else ''
objs = {}
for f in glob.glob(os.path.join(root, 'Metadata', '**', '*.xml'), recursive=True):
    try: t = ET.parse(f)
    except Exception: continue
    for o in t.getroot().iter('object'):
        objs[o.get('id')] = o
def prop(o, n):
    p = o.find(f"property[@name='{n}']/value"); return p.text if p is not None else None
def rel(o, n): return [d.text for d in o.findall(f"relationship[@name='{n}']/destination")]
def name_of(i): o = objs.get(i); return prop(o, 'name') if o is not None else None
def folder_path(i):
    parts = []
    while i in objs and objs[i].get('class') == 'EventFolder':
        n = prop(objs[i], 'name')
        if n is None: break
        parts.insert(0, n); r = rel(objs[i], 'folder'); i = r[0] if r else None
    return '/'.join(parts)
def audio(i):
    o = objs.get(i); return prop(o, 'assetPath') if o is not None else None
def instrument(i, depth=0):
    o = objs.get(i)
    if o is None: return None
    c = o.get('class'); d = {'class': c}
    for k in ('start', 'length', 'fadeInLength', 'fadeOutLength', 'volume', 'pitch', 'looping', 'playlistMode', 'isAsync', 'startOffset'):
        v = prop(o, k)
        if v is not None: d[k] = v
    af = rel(o, 'audioFile')
    if af: d['file'] = audio(af[0])
    pl = rel(o, 'playlist')
    if pl:
        items = []
        for p in pl:
            po = objs.get(p)
            if po is None: continue
            for e in rel(po, 'entries'): items.append(instrument(e, depth+1))
        d['playlist'] = items
    ev = rel(o, 'event')
    if ev: d['event'] = ev[0]
    mods = []
    for m in rel(o, 'modulators'):
        mo = objs.get(m)
        if mo is not None: mods.append({'class': mo.get('class'), **{k: prop(mo, k) for k in ('nameOfPropertyBeingModulated','minimum','maximum','amount','attackTime','releaseTime','shape','rate','depth') if prop(mo, k) is not None}})
    if mods: d['modulators'] = mods
    return d
out = []
for i, o in objs.items():
    if o.get('class') != 'Event': continue
    f = rel(o, 'folder'); path = (folder_path(f[0]) + '/' if f else '') + (prop(o, 'name') or '?')
    if flt and flt not in path.lower(): continue
    ev = {'path': path, 'id': i}
    tracks = []
    for t in rel(o, 'groupTracks'):
        to = objs.get(t)
        if to is None: continue
        tr = {'name': None, 'instruments': []}
        for mt in rel(to, 'mixerGroup'):
            tr['name'] = prop(objs.get(mt), 'name') if objs.get(mt) is not None else None
        for m in rel(to, 'modules'): tr['instruments'].append(instrument(m))
        tracks.append(tr)
    ev['tracks'] = tracks
    params = []
    for p in rel(o, 'parameters'):
        po = objs.get(p)
        if po is None: continue
        pp = rel(po, 'preset')
        params.append(name_of(pp[0]) if pp else None)
    if params: ev['parameters'] = params
    mixer = rel(o, 'mixer')
    for m in mixer:
        mo = objs.get(m)
        if mo is None: continue
        for mi in rel(mo, 'masterBus'):
            mb = objs.get(mi)
            if mb is not None:
                v = prop(mb, 'volume'); p = prop(mb, 'pitch')
                if v: ev['volume'] = v
                if p: ev['pitch'] = p
    out.append(ev)
out.sort(key=lambda e: e['path'])
print(json.dumps(out, indent=1, ensure_ascii=False) if flt else '\n'.join(f"{e['path']}  tracks={len(e['tracks'])} inst={sum(len(t['instruments']) for t in e['tracks'])} params={e.get('parameters','')}" for e in out))
