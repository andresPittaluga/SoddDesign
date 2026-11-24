"""Genera los archivos de datos de ejemplo del Sandbox (Assets/SoddDesign/*). Idempotente.
Uso: python Tools/gen_sandbox_data.py"""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'SoddDesign')
A = 'Assets/Sandbox/Audio/'
G = {
    'beep_220': 'bb0b95e67a6880b47b042b92c383d9c2', 'beep_440': 'ca31acd3a7f51704b91d6cf71b99f47d',
    'beep_880': '0ad695731ce6b654f820c431f0ebc0bb', 'drone_110': '1ba8103d01fc48344904d595ba2a44c8',
    'footstep_01': '1ad51caab6a21c4438da0bf6fd94fde4', 'footstep_02': '70bb397e27a3b6e4cbf1c38ebda0e32a',
    'footstep_03': 'b24b1669940b8c74a9bf98df661e999e', 'footstep_04': 'b641022d268c8734086344aa0743d9f8',
    'hihat': '095ef2908ddd70b439436ada46395730', 'kick': '0a35acb16db887144a75ac2e06ef2a3a',
    'music_loop_120bpm': '9fc0025e037e7244a9b4ed86bcf33e26', 'noise_burst': '5d9687a062873c24c9593c22a5069642',
    'snare': 'f079838fc954d8b47a90fd1f1d027e6b', 'sweep_down': '60b6997f9c95aef4bb198666a63e0d22',
    'sweep_up': 'c61fa52d03f87234f8205e7442891061', 'ui_click': 'db9094ca3fce0d848b91064251408591',
}

def clip(n): return {'guid': G[n], 'path': A + n + '.wav'}
def rnd(lo, hi, unit): return {'type': 'Random', 'min': lo, 'max': hi, 'unit': unit}

def write(rel, obj):
    p = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    if rel.endswith('.sodd.json'):
        depth = rel.count('/') + 2  # Assets/SoddDesign/<rel>
        obj = {'$schema': '../' * depth + 'Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json', **obj}
    with open(p, 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(obj, indent=2, ensure_ascii=False) + '\n')
    print('wrote', rel)

write('sodd.settings.json', {
    'maxVoices': 32, 'maxInstances': 128, 'lookaheadMs': 100,
    'buses': [{'name': 'Master'}] + [{'name': n, 'parent': 'Master'} for n in ['Music', 'SFX', 'UI', 'Voice', 'Ambience']],
    'snapshots': [
        {'name': 'Underwater', 'description': 'Bajo el agua: todo apagado (lowpass fuerte), música más baja.',
         'buses': {'SFX': {'lowpass': 600, 'volume': -3}, 'Ambience': {'lowpass': 500}, 'Music': {'lowpass': 900, 'volume': -6}}},
        {'name': 'Pause', 'description': 'Menú de pausa: baja SFX y ambiente, la música queda filtrada.',
         'buses': {'SFX': {'volume': -20}, 'Ambience': {'volume': -20}, 'Music': {'volume': -8, 'lowpass': 2500}}},
    ],
})

write('sodd.parameters.json', {'parameters': [
    {'name': 'Health', 'scope': 'global', 'min': 0, 'max': 100, 'default': 100, 'seekSpeed': 0, 'description': 'Vida del jugador (0 = muerto, 100 = vida llena).'},
    {'name': 'Intensity', 'scope': 'global', 'min': 0, 'max': 1, 'default': 0, 'seekSpeed': 2, 'description': 'Intensidad de la acción/música (0 = calma, 1 = combate).'},
    {'name': 'Speed', 'scope': 'local', 'min': 0, 'max': 1, 'default': 0, 'seekSpeed': 4, 'description': 'Velocidad normalizada (motores, viento, pasos).'},
]})

write('Events/sfx/ui/click.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c01', 'path': 'sfx/ui/click', 'description': 'Click de botón de UI, seco y corto.',
    'tags': ['ui'], 'bus': 'UI',
    'tracks': [{'name': 'Click', 'type': 'SFX', 'regions': [{'start': 0, 'length': 0.1, 'instrument': {
        'type': 'SingleClip', 'clip': clip('ui_click'), 'pitch': {'base': 0, 'modulators': [rnd(-0.5, 0.5, 'Semitones')]}}}]}],
})

write('Events/sfx/player/footstep.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c02', 'path': 'sfx/player/footstep',
    'description': 'Paso del jugador. 4 variaciones sin repetir, con pitch y volumen aleatorios.',
    'tags': ['player', 'foley'], 'is3D': True, 'spatial': {'minDistance': 1, 'maxDistance': 25, 'rolloff': 'Logarithmic'},
    'bus': 'SFX', 'maxInstances': 4, 'stealMode': 'Oldest',
    'volume': {'base': -2, 'modulators': [rnd(-3, 0, 'dB')]},
    'tracks': [{'name': 'Steps', 'type': 'SFX', 'regions': [{'start': 0, 'length': 0.25, 'instrument': {
        'type': 'RandomClip', 'avoidRepeat': True, 'clips': [clip('footstep_0%d' % i) for i in range(1, 5)],
        'pitch': {'base': 0, 'modulators': [rnd(-1.5, 1.5, 'Semitones')]}}}]}],
})

write('Events/sfx/player/jump.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c03', 'path': 'sfx/player/jump',
    'description': 'Salto. Con menos vida el pitch se vuelve más inestable (RandomScaledByParameter sobre Health).',
    'tags': ['player'], 'is3D': True, 'spatial': {'minDistance': 1, 'maxDistance': 30},
    'cooldown': 0.05, 'networkMode': 'Broadcast',
    'pitch': {'base': 0, 'modulators': [{'type': 'RandomScaledByParameter', 'parameter': 'Health', 'range': 5, 'unit': 'Semitones',
                                         'curve': [[0, 1], [50, 0.4], [100, 0]]}]},
    'tracks': [
        {'name': 'Whoosh', 'type': 'SFX', 'regions': [{'start': 0, 'length': 0.4, 'fadeOut': 0.15, 'instrument': {'type': 'SingleClip', 'clip': clip('sweep_up')}}]},
        {'name': 'Thump', 'type': 'SFX', 'volume': -6, 'regions': [{'start': 0, 'length': 0.3, 'instrument': {'type': 'SingleClip', 'clip': clip('kick')}}]},
    ],
})

write('Events/music/loop.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c04', 'path': 'music/loop',
    'description': 'Loop musical de 2 compases a 120 BPM. Sale del loop cuando Intensity > 0.9 y toca el final. La capa de drone sube con Intensity.',
    'tags': ['music'], 'bus': 'Music', 'priority': 0, 'maxInstances': 1,
    'tracks': [
        {'name': 'Groove', 'type': 'Music', 'regions': [
            {'name': 'Loop A', 'start': 0, 'length': 4, 'instrument': {'type': 'SingleClip', 'clip': clip('music_loop_120bpm')}},
            {'name': 'Outro', 'start': 4, 'length': 0.5, 'instrument': {'type': 'SingleClip', 'clip': clip('kick')}},
        ]},
        {'name': 'Drone', 'type': 'Music',
         'volume': {'base': 0, 'modulators': [{'type': 'ParameterCurve', 'parameter': 'Intensity', 'curve': [[0, -40], [0.5, -12], [1, -4]]}]},
         'regions': [{'start': 0, 'length': 4, 'fadeIn': 0.05, 'fadeOut': 0.05, 'instrument': {'type': 'SingleClip', 'clip': clip('drone_110')}}]},
    ],
    'loopRegions': [{'name': 'Main', 'start': 0, 'end': 4, 'exitWhen': {'parameter': 'Intensity', 'op': 'Greater', 'value': 0.9}}],
    'markers': [{'name': 'Bar1', 'time': 0}, {'name': 'Bar2', 'time': 2}],
})

write('Events/sfx/impacts/hit.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c05', 'path': 'sfx/impacts/hit',
    'description': 'Golpe en capas: kick (grave) + snare (cuerpo) + ruido (transiente).',
    'tags': ['impact', 'combat'], 'is3D': True,
    'spatial': {'minDistance': 2, 'maxDistance': 40, 'rolloff': 'Custom', 'customRolloff': [[0, 1], [10, 0.5], [40, 0]]},
    'maxInstances': 6, 'stealMode': 'Quietest',
    'tracks': [
        {'name': 'Low', 'type': 'SFX', 'regions': [{'start': 0, 'length': 0.5, 'instrument': {'type': 'SingleClip', 'clip': clip('kick')}}]},
        {'name': 'Body', 'type': 'SFX', 'volume': -3, 'regions': [{'start': 0, 'length': 0.3, 'instrument': {'type': 'SingleClip', 'clip': clip('snare'),
                                                                     'pitch': {'base': 0, 'modulators': [rnd(-2, 2, 'Semitones')]}}}]},
        {'name': 'Transient', 'type': 'SFX', 'volume': -10, 'regions': [{'start': 0, 'length': 0.08, 'fadeOut': 0.06, 'instrument': {'type': 'SingleClip', 'clip': clip('noise_burst')}}]},
    ],
})

write('Events/ambience/drone.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c06', 'path': 'ambience/drone',
    'description': "Drone ambiental en loop que 'respira' (LFO lento en volumen y pitch).",
    'tags': ['ambience', 'loop'], 'is3D': True, 'spatial': {'minDistance': 3, 'maxDistance': 30}, 'bus': 'Ambience',
    'volume': {'base': -6, 'modulators': [{'type': 'LFO', 'shape': 'Sine', 'rate': 0.25, 'depth': 4},
                                          {'type': 'ADSR', 'attack': 1.5, 'sustain': 1, 'release': 2}]},
    'pitch': {'base': 0, 'modulators': [{'type': 'LFO', 'shape': 'Triangle', 'rate': 0.1, 'depth': 0.3}]},
    'tracks': [{'name': 'Drone', 'type': 'Ambience',
                'effects': [{'type': 'LowPass', 'params': {'cutoff': {'base': 1800, 'modulators': [{'type': 'LFO', 'shape': 'Sine', 'rate': 0.15, 'depth': 1200}]}, 'resonance': 2}}],
                'regions': [{'start': 0, 'length': 4, 'instrument': {'type': 'SingleClip', 'loop': True, 'clip': clip('drone_110')}}]}],
    'loopRegions': [{'name': 'Forever', 'start': 0, 'end': 4}],
})

write('Events/sfx/fx/lofi_beeps.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c07', 'path': 'sfx/fx/lofi_beeps',
    'description': 'Arpegio de beeps en secuencia, lo-fi (Bitcrusher) con delay. Demo de efectos DSP propios.',
    'tags': ['fx', 'demo'], 'bus': 'SFX',
    'tracks': [{'name': 'Beeps', 'type': 'SFX',
                'effects': [{'type': 'Bitcrusher', 'params': {'bits': 5, 'downsample': 6, 'mix': 1}},
                            {'type': 'SimpleDelay', 'params': {'time': 0.18, 'feedback': 0.45, 'mix': 0.4}}],
                'regions': [{'start': t * 0.2, 'length': 0.2, 'instrument': {'type': 'Sequential', 'clips': [clip('beep_220'), clip('beep_440'), clip('beep_880')]}} for t in range(6)]}],
})

write('Events/sfx/fx/robot.sodd.json', {
    'id': '5b0e7d9a-3c1f-4c55-9a51-0f1e2a3b4c08', 'path': 'sfx/fx/robot',
    'description': 'Barrido metálico/robótico: RingMod + Tremolo + Saturator, comprimido.',
    'tags': ['fx', 'demo'], 'bus': 'SFX',
    'tracks': [{'name': 'Sweep', 'type': 'SFX',
                'effects': [{'type': 'RingMod', 'params': {'frequency': {'base': 300, 'modulators': [{'type': 'LFO', 'shape': 'Triangle', 'rate': 2, 'depth': 200}]}, 'mix': 0.8}},
                            {'type': 'Tremolo', 'params': {'rate': 12, 'depth': 0.6}},
                            {'type': 'Saturator', 'params': {'drive': 4, 'mix': 0.7, 'output': -3}},
                            {'type': 'SimpleCompressor', 'params': {'threshold': -20, 'ratio': 6, 'makeup': 6}}],
                'regions': [{'start': 0, 'length': 1, 'fadeOut': 0.2, 'instrument': {'type': 'SingleClip', 'clip': clip('sweep_down')}}]}],
})
