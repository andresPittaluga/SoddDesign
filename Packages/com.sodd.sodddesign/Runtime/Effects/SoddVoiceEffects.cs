using UnityEngine;

namespace Sodd.Design
{
    /// <summary>Cadena de efectos de una voz (componentes pre-creados y desactivados; se habilitan por track).</summary>
    internal sealed class SoddVoiceEffects
    {
        public SoddVoiceEffects(GameObject go) { }
        public void Configure(SoddEffect[] effects) { }
        public void Update(SoddEffect[] effects, ref SoddModContext ctx, SoddBus bus) { }
        public void Disable() { }
    }
}
