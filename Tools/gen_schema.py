"""Genera Packages/com.sodd.sodddesign/Schema/sodd.event.schema.json (JSON Schema 2020-12).
Cada campo lleva 'description' para humanos e IA. Uso: python Tools/gen_schema.py"""
import json, os

OUT = os.path.join(os.path.dirname(__file__), '..', 'Packages', 'com.sodd.sodddesign', 'Schema', 'sodd.event.schema.json')

def num(desc, **kw): return {'type': 'number', 'description': desc, **kw}
def s(desc, **kw): return {'type': 'string', 'description': desc, **kw}
def b(desc): return {'type': 'boolean', 'description': desc}
def ref(name, desc): return {'$ref': '#/$defs/' + name, 'description': desc}
def arr(items, desc): return {'type': 'array', 'items': items, 'description': desc}

EFFECTS = ['LowPass', 'HighPass', 'ThreeEQ', 'Echo', 'Reverb', 'Distortion', 'Chorus', 'Bitcrusher', 'Tremolo', 'AutoPan',
           'RingMod', 'Saturator', 'SimpleDelay', 'SimpleCompressor']

schema = {
    '$schema': 'https://json-schema.org/draft/2020-12/schema',
    '$id': 'https://sodd.studio/schemas/sodd.event.schema.json',
    'title': 'SoddDesign Event',
    'description': 'Un evento de audio de SoddDesign (archivo *.sodd.json). Contiene tracks con regiones en un timeline, '
                   'parámetros locales, loops y markers. Toda propiedad numérica modulable acepta un número o {base, modulators}.',
    'type': 'object',
    'required': ['id', 'path', 'tracks'],
    'additionalProperties': False,
    'properties': {
        '$schema': s('Ruta a este schema (para autocompletado en editores).'),
        'id': s('GUID único y estable del evento. No cambiar después de crearlo: las referencias del juego lo usan.', format='uuid'),
        'path': s('Ruta lógica legible, estilo carpetas: "sfx/player/jump". Se usa para buscar el evento desde código.', pattern='^[A-Za-z0-9_\\-]+(/[A-Za-z0-9_\\-]+)*$'),
        'description': s('Descripción en lenguaje natural de cómo suena y cuándo se usa.'),
        'tags': arr(s('Etiqueta.'), 'Etiquetas libres para buscar/filtrar (ej. "ui", "player", "loop").'),
        'is3D': b('Si es true el sonido se posiciona en el mundo (usa "spatial"). Si es false es 2D (UI, música).'),
        'spatial': ref('spatial', 'Ajustes de audio 3D (solo si is3D).'),
        'bus': s('Bus lógico de salida: Master, Music, SFX, UI, Voice, Ambience o uno definido en sodd.settings.json.', default='SFX'),
        'priority': {'type': 'integer', 'minimum': 0, 'maximum': 256, 'default': 128, 'description': 'Prioridad de voz: 0 = más importante (nunca se roba), 256 = menos importante.'},
        'maxInstances': {'type': 'integer', 'minimum': 0, 'default': 0, 'description': 'Máximo de instancias sonando a la vez de este evento (0 = sin límite).'},
        'stealMode': s('Qué hacer al superar maxInstances: Oldest (corta la más vieja), Quietest (corta la más baja), None (no arranca la nueva).', enum=['Oldest', 'Quietest', 'None'], default='Oldest'),
        'cooldown': num('Segundos mínimos entre dos disparos del evento (anti-spam).', minimum=0, default=0),
        'networkMode': s('Red (NGO): Local = no se envía; Broadcast = suena en todos los clientes; OwnerOnly = solo el dueño.', enum=['Local', 'Broadcast', 'OwnerOnly'], default='Local'),
        'volume': ref('modProp', 'Volumen maestro del evento en dB (0 = original, -6 = mitad de amplitud).'),
        'pitch': ref('modProp', 'Pitch maestro del evento en semitonos (12 = una octava arriba, -12 = abajo).'),
        'parameters': arr(ref('parameter', 'Parámetro local.'), 'Parámetros locales de cada instancia (ej. "Speed" de un motor). Los globales van en sodd.parameters.json.'),
        'tracks': arr(ref('track', 'Track.'), 'Pistas del timeline. Todas suenan en paralelo.'),
        'loopRegions': arr(ref('loopRegion', 'Región de loop.'), 'Regiones de loop del timeline. Cuando el cursor llega al final vuelve al inicio, salvo que se cumpla exitWhen.'),
        'markers': arr(ref('marker', 'Marker.'), 'Marcas con nombre en el timeline; disparan el callback OnMarker en el juego.'),
    },
    '$defs': {
        'curve': {
            'type': 'array', 'description': 'Curva lineal por tramos: lista de puntos [x, y] ordenados por x. Fuera del rango se mantiene el extremo.',
            'items': {'type': 'array', 'prefixItems': [num('x (ej. valor del parámetro)'), num('y (valor resultante)')], 'minItems': 2, 'maxItems': 2, 'description': 'Punto [x, y].'},
        },
        'modulator': {
            'type': 'object', 'required': ['type'], 'additionalProperties': False,
            'description': 'Modulador que se suma al valor base de una propiedad.',
            'properties': {
                'type': s('Random: valor aleatorio entre min y max. ParameterCurve: valor según curva(parámetro). '
                          'RandomScaledByParameter: aleatorio ±range escalado por curva(parámetro). LFO: oscilador. '
                          'Envelope: attack/hold/release desde el inicio. ADSR: envolvente con release al detener.',
                          enum=['Random', 'ParameterCurve', 'RandomScaledByParameter', 'LFO', 'Envelope', 'ADSR']),
                'min': num('Mínimo (Random / RandomScaledByParameter).'),
                'max': num('Máximo (Random / RandomScaledByParameter).'),
                'range': num('Atajo: equivale a min = -range, max = +range.'),
                'unit': s('Unidad de min/max: Auto (la de la propiedad), dB, Semitones o Linear (ratio; 0.5 = mitad).', enum=['Auto', 'Linear', 'dB', 'Semitones'], default='Auto'),
                'perTrigger': b('true: nuevo valor aleatorio en cada disparo de región; false: uno solo por instancia.'),
                'parameter': s('Nombre del parámetro (local del evento o global de sodd.parameters.json). En LFO multiplica el rate (sync).'),
                'curve': ref('curve', 'Curva parámetro → valor (ParameterCurve) o parámetro → escala 0..1 (RandomScaledByParameter).'),
                'shape': s('Forma del LFO.', enum=['Sine', 'Triangle', 'Square', 'SawUp', 'SawDown', 'Random'], default='Sine'),
                'rate': num('Frecuencia del LFO en Hz.', minimum=0, default=1),
                'depth': num('Profundidad del LFO/Envelope en unidades de la propiedad (dB, semitonos...).', default=1),
                'phase': num('Fase inicial del LFO (0..1).', default=0),
                'attack': num('Ataque en segundos.', minimum=0),
                'hold': num('Hold en segundos (Envelope).', minimum=0),
                'decay': num('Decay en segundos (ADSR).', minimum=0),
                'sustain': num('Nivel de sustain 0..1 (ADSR).', minimum=0, maximum=1),
                'release': num('Release en segundos. En ADSR sobre volume define el fade-out al detener.', minimum=0),
            },
        },
        'modProp': {
            'description': 'Propiedad modulable: un número, o { "base": x, "modulators": [...] }.',
            'oneOf': [
                {'type': 'number', 'description': 'Valor fijo.'},
                {'type': 'object', 'additionalProperties': False, 'description': 'Valor base + moduladores.', 'properties': {
                    'base': num('Valor base.'),
                    'modulators': arr(ref('modulator', 'Modulador.'), 'Moduladores que se suman al base.'),
                }},
            ],
        },
        'spatial': {
            'type': 'object', 'additionalProperties': False, 'description': 'Audio 3D.',
            'properties': {
                'spatialBlend': num('0 = 2D, 1 = 3D completo.', minimum=0, maximum=1, default=1),
                'minDistance': num('Distancia (m) hasta la que suena a volumen completo.', minimum=0, default=1),
                'maxDistance': num('Distancia (m) a partir de la cual deja de atenuar/no se oye.', minimum=0, default=50),
                'rolloff': s('Curva de atenuación por distancia.', enum=['Logarithmic', 'Linear', 'Custom'], default='Logarithmic'),
                'customRolloff': ref('curve', 'Curva distancia(m) → volumen 0..1 para rolloff Custom.'),
                'doppler': num('Efecto Doppler (0 = ninguno).', minimum=0, maximum=5, default=1),
                'spread': num('Apertura estéreo en grados (0 = puntual, 360 = envolvente).', minimum=0, maximum=360, default=0),
            },
        },
        'parameter': {
            'type': 'object', 'required': ['name'], 'additionalProperties': False, 'description': 'Definición de parámetro.',
            'properties': {
                'name': s('Nombre del parámetro (ej. "Speed").'),
                'description': s('Qué representa, en lenguaje natural.'),
                'min': num('Valor mínimo.', default=0), 'max': num('Valor máximo.', default=1),
                'default': num('Valor inicial.'),
                'seekSpeed': num('Unidades por segundo con que el valor persigue al objetivo (0 = instantáneo). Suaviza saltos.', minimum=0, default=0),
                'scope': s('local (por instancia) o global (uno para todo el juego).', enum=['local', 'global']),
            },
        },
        'clip': {
            'description': 'Referencia a un AudioClip: GUID del asset (estable) + path legible (para humanos/IA). Puede ser solo un path string.',
            'oneOf': [
                {'type': 'string', 'description': 'Path del AudioClip (ej. "Assets/Audio/jump.wav").'},
                {'type': 'object', 'additionalProperties': False, 'description': 'Clip con GUID.', 'properties': {
                    'guid': s('GUID del asset de Unity (del .meta).'), 'path': s('Path del asset (legible).'),
                    'weight': num('Peso relativo para RandomClip.', minimum=0, default=1),
                    'volume': num('Volumen de este clip en dB, se suma al del instrumento.', default=0),
                    'pitch': num('Pitch de este clip en semitonos, se suma al del instrumento.', default=0),
                    'startOffset': num('Segundos a saltear de este clip, se suman al startOffset del instrumento.', minimum=0, default=0),
                }},
            ],
        },
        'instrument': {
            'type': 'object', 'required': ['type'], 'additionalProperties': False, 'description': 'Qué suena dentro de la región.',
            'properties': {
                'type': s('SingleClip: un clip. RandomClip: uno al azar (con pesos). Sequential: en orden. Silence: nada. NestedEvent: otro evento.',
                          enum=['SingleClip', 'RandomClip', 'Sequential', 'Silence', 'NestedEvent']),
                'clip': ref('clip', 'Clip único (atajo para SingleClip).'),
                'clips': arr(ref('clip', 'Clip.'), 'Lista de clips.'),
                'avoidRepeat': b('RandomClip: no repetir el último clip elegido ("que no se repita").'),
                'sequenceScope': s('Sequential: Global = el orden sigue entre todas las instancias del evento (one-shots tic-tac). Instance = cada instancia lleva su propio orden.', enum=['Global', 'Instance']),
                'loop': b('El clip se repite dentro de la región hasta que termina la región.'),
                'nestedEvent': s('NestedEvent: id o path del evento a disparar.'),
                'volume': ref('modProp', 'Volumen del instrumento en dB.'),
                'pitch': ref('modProp', 'Pitch del instrumento en semitonos.'),
                'startOffset': ref('modProp', 'Segundos a saltear del inicio del clip (útil con Random para variar).'),
            },
        },
        'region': {
            'type': 'object', 'required': ['start', 'length', 'instrument'], 'additionalProperties': False,
            'description': 'Bloque en el timeline de un track.',
            'properties': {
                'name': s('Nombre opcional.'),
                'start': num('Inicio en segundos desde el comienzo del evento.', minimum=0),
                'length': num('Duración en segundos.', exclusiveMinimum=0),
                'fadeIn': num('Fade-in en segundos.', minimum=0), 'fadeOut': num('Fade-out en segundos.', minimum=0),
                'fadeInCurve': s('Forma del fade-in.', enum=['Linear', 'EqualPower', 'Exponential', 'SCurve']),
                'fadeOutCurve': s('Forma del fade-out.', enum=['Linear', 'EqualPower', 'Exponential', 'SCurve']),
                'instrument': ref('instrument', 'Instrumento de la región.'),
            },
        },
        'effect': {
            'type': 'object', 'required': ['type'], 'additionalProperties': False, 'description': 'Efecto del track (ver catálogo en AI_GUIDE.md).',
            'properties': {
                'type': s('Tipo de efecto.', enum=EFFECTS),
                'bypass': b('Desactiva el efecto sin borrarlo.'),
                'params': {'type': 'object', 'description': 'Parámetros del efecto (cada uno modulable). Ej. LowPass: {"cutoff": 800}.',
                           'additionalProperties': ref('modProp', 'Valor del parámetro.')},
            },
        },
        'track': {
            'type': 'object', 'additionalProperties': False, 'description': 'Pista del timeline.',
            'properties': {
                'name': s('Nombre de la pista.'),
                'type': s('Tipo (define el color en el Studio).', enum=['SFX', 'Music', 'Voice', 'Ambience']),
                'volume': ref('modProp', 'Volumen de la pista en dB.'),
                'pan': ref('modProp', 'Paneo -1 (izquierda) a 1 (derecha). Solo 2D.'),
                'reverbSend': ref('modProp', 'Envío a reverb (-1..0; 0 = máximo, -1 = nada).'),
                'mute': b('Silenciar la pista.'), 'solo': b('Solo esta pista (y otras en solo).'),
                'bus': s('Bus propio de la pista (por defecto el del evento).'),
                'effects': arr(ref('effect', 'Efecto.'), 'Cadena de efectos en orden.'),
                'regions': arr(ref('region', 'Región.'), 'Regiones (instrumentos) en el timeline.'),
            },
        },
        'condition': {
            'type': 'object', 'required': ['parameter', 'op', 'value'], 'additionalProperties': False, 'description': 'Condición sobre un parámetro.',
            'properties': {
                'parameter': s('Nombre del parámetro.'),
                'op': s('Comparación.', enum=['Greater', 'GreaterOrEqual', 'Less', 'LessOrEqual', 'Equal', 'NotEqual']),
                'value': num('Valor a comparar.'),
            },
        },
        'loopRegion': {
            'type': 'object', 'required': ['start', 'end'], 'additionalProperties': False, 'description': 'Loop del timeline.',
            'properties': {
                'name': s('Nombre del loop.'),
                'start': num('Inicio del loop (s).', minimum=0), 'end': num('Fin del loop (s).', minimum=0),
                'exitWhen': ref('condition', 'Si se cumple al llegar al final, se sale del loop (ej. Intensity > 0.9).'),
            },
        },
        'marker': {
            'type': 'object', 'required': ['name', 'time'], 'additionalProperties': False, 'description': 'Marker con nombre.',
            'properties': {'name': s('Nombre (llega al callback OnMarker).'), 'time': num('Tiempo en segundos.', minimum=0)},
        },
    },
}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    f.write(json.dumps(schema, indent=2, ensure_ascii=False) + '\n')
print('wrote', OUT)
