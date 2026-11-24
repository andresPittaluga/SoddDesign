"""Arreglos manuales (no mecánicos) de la migración FMOD -> SoddDesign en Warpmart. Idempotente.
Uso: python -I Tools/warpmart_manual_fixes.py <ruta WarpMart>"""
import os, re, sys

root = os.path.join(sys.argv[1], 'Assets')

def edit(rel, pairs, regex=False):
    p = os.path.join(root, rel)
    raw = open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    crlf = '\r\n' in s
    s = s.replace('\r\n', '\n')
    orig = s
    for a, b in pairs:
        if regex:
            s, n = re.subn(a, b, s, flags=re.S)
        else:
            n = s.count(a); s = s.replace(a, b)
        if n == 0 and b not in s:
            print(f'  ! no encontrado en {rel}: {a[:70]!r}')
    if s != orig:
        if crlf: s = s.replace('\n', '\r\n')
        open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
        print('editado', rel)

# ---------------------------------------------------------------- MusicManager: beat por BPM (RobotScat no tiene tempo markers)
edit('Scripts/Managers/GameManagers/MusicManager.cs', [
    ('            _podiumMusic.setCallback(BeatCallback, EVENT_CALLBACK_TYPE.TIMELINE_BEAT);\n', ''),
    ('        private static readonly EVENT_CALLBACK BeatCallback = OnFmodBeat;\n', ''),
    ('''        [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
        private static FMOD.RESULT OnFmodBeat(EVENT_CALLBACK_TYPE type, System.IntPtr instance, System.IntPtr parameters)
        {
            System.Threading.Interlocked.Increment(ref _fmodBeats);
            return FMOD.RESULT.OK;
        }
''', '''        // SoddDesign: sin callbacks de tempo; el beat sale de _podiumBpm y la posición del timeline.
'''),
    ('''            podiumMusic.getDescription(out var description);
            description.getLength(out int lengthMs);''', '''            float lengthMs = podiumMusic.Length * 1000f;'''),
    ('[Tooltip("Tempo for OnPodiumBeat when the podium song has no tempo markers in FMOD.")]', '[Tooltip("Tempo for OnPodiumBeat (SoddDesign has no tempo markers).")]'),
])

# ---------------------------------------------------------------- Buses (volumen de opciones)
edit('Scripts/Menu/Options/Sound/SliderVolume.cs', [
    ('    private Bus _fmodBus;', '    private SoddBus _fmodBus;'),
    ('        _fmodBus.Volume = vlm;', '        if (_fmodBus != null) _fmodBus.VolumeLinear = vlm;'),
    ('        if (!_fmodBus.IsValid) return;', '        if (_fmodBus == null) return;'),
    ('        _fmodBus.Volume = value;', '        _fmodBus.VolumeLinear = value;'),
    ('    private static readonly Dictionary<string, Bus> _buses = new();\n', ''),
    ('''            Bus fmodBus = GetBus(bus);
            if (fmodBus.IsValid)
                fmodBus.Volume = Mathf.Clamp01(PlayerPrefs.GetFloat(key, fallback));''', '''            SoddBus fmodBus = GetBus(bus);
            if (fmodBus != null)
                fmodBus.VolumeLinear = Mathf.Clamp01(PlayerPrefs.GetFloat(key, fallback));'''),
    ('''    public static Bus GetBus(string path)
    {
        if (_buses.TryGetValue(path, out Bus cached) && cached.IsValid)
            return cached;

        Bus bus = RuntimeManager.GetBus(path);
        if (bus.IsValid)
            _buses[path] = bus;

        return bus;
    }

    public static void Clear() => _buses.Clear();''', '''    /// <summary>Bus de SoddDesign por path estilo FMOD ("bus:/", "bus:/Music"). Null si el motor no arrancó.</summary>
    public static SoddBus GetBus(string path) => SoddAudio.GetBus(path);

    public static void Clear() { }'''),
])
edit('Scripts/Menu/Settings/AudioSettingsApplier.cs', [
    ('            if (bus.IsValid) bus.Volume = SliderToGain(slider);', '            if (bus != null) bus.VolumeLinear = SliderToGain(slider);'),
    ('            if (bus.IsValid) bus.setMute(_muteWhenUnfocused && !focused);', '            if (bus != null) bus.Mute = _muteWhenUnfocused && !focused;'),
    ('        internal static bool BanksReady => RuntimeManager.IsInitialized && RuntimeManager.HaveMasterBanksLoaded;',
     '        internal static bool BanksReady => Sodd.Design.SoddAudio.Engine != null;'),
])

# ---------------------------------------------------------------- Precarga de samples (SoddDesign carga los clips con el evento)
for rel, field in [('Scripts/Art/Feedback/InstantiableParticle/Effects/BlackHoleParticle.cs', '_implosionSfx'),
                   ('Scripts/Art/Feedback/InstantiableParticle/Effects/WhiteHoleParticle.cs', '_explosionSfx')]:
    edit(rel, [
        ('        private FMOD.Studio.EventDescription _sfxDescription;\n', ''),
        (f'            if (!{field}.IsNull) _sfxDescription = FMODUnity.RuntimeManager.GetEventDescription({field});\n', ''),
        ('            if (_sfxDescription.IsValid) _sfxDescription.loadSampleData();\n', ''),
        ('            if (_sfxDescription.IsValid) _sfxDescription.unloadSampleData();\n', ''),
        ('            _sfxDescription = default;\n', ''),
    ])
edit('Scripts/Art/Feedback/InstantiableParticle/Effects/BlackHoleParticle.cs', [
    ('''            instance.setTimelinePosition(Mathf.RoundToInt(_implosionSfxSkip * 1000f));
            instance.Start();''', '''            instance.StartFrom(_implosionSfxSkip);'''),
])
edit('Scripts/Weapons/Guns/VAC.cs', [
    ('        private readonly List<FMOD.Studio.EventDescription> _preloaded = new();\n', ''),
    ('''            Preload(string.IsNullOrEmpty(ShotSfxPath) ? default : RuntimeManager.GetEventDescription(ShotSfxPath));
            if (IsOwner && !_chargeLoopSfx.IsNull) Preload(RuntimeManager.GetEventDescription(_chargeLoopSfx));
''', ''),
    ('''            foreach (var description in _preloaded)
                if (description.IsValid) description.unloadSampleData();

            _preloaded.Clear();
''', ''),
    ('''        private void Preload(FMOD.Studio.EventDescription description)
        {
            if (!description.IsValid || description.loadSampleData() != FMOD.RESULT.OK) return;

            _preloaded.Add(description);
        }
''', ''),
    ('''                if (_chargeLoop.getTimelinePosition(out int position) == FMOD.RESULT.OK)
                {''', '''                int position = Mathf.RoundToInt(_chargeLoop.TimelinePosition * 1000f);
                {'''),
])

# ---------------------------------------------------------------- Existencia de eventos
edit('Scripts/Player/Composition/PlayerPassives.FinalNotice.cs', [
    ('            if (FMODUnity.RuntimeManager.StudioSystem.getEventByID(notice.ArmedSfx.Guid, out _) != FMOD.RESULT.OK) return;',
     '            if (notice.ArmedSfx.Resolve() == null) return;'),
])
edit('Scripts/Sounds/FMODAudioManager.cs', [
    ('        sfxInstance.getVolume(out float startVolume);', '        float startVolume = sfxInstance.Volume;'),
    ('        music.getVolume(out float startVolume);', '        float startVolume = music.Volume;'),
    ('            return RuntimeManager.StudioSystem.getEvent(eventPath, out _) == FMOD.RESULT.OK;',
     '            return Sodd.Design.SoddFmodCompat.EventExists(eventPath);'),
])
