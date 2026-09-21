using UnityEngine;
using Combat;

namespace Gameplay
{
    // Mapeo compartido de elemento -> color "canonico" para todo el feedback visual de combate
    // (anillo de shader, particulas, sprite de impacto teñido, brillo de borde de camara/enemigo,
    // luz de impacto). Antes vivia duplicado como metodo privado en BattleStageController, y
    // ademas cada efecto (EnemyView, ElementalParticleEffect) definia sus propios colores sueltos
    // que se iban desalineando entre si. Ahora TODO color de combate sale de aca: la clasificacion
    // es por tipo de golpe/habilidad (elemento), nunca por personaje.
    //
    // Paleta fija para que un mismo color siempre signifique lo mismo en toda la UI de combate:
    //  - Naranjo  -> Golpe fisico contundente (Strike, y el pulso generico de "recibiste un golpe"
    //                de EnemyView/CombatFeedback, que no depende del elemento).
    //  - Verde    -> Curacion (CombatFeedback.healFlashColor). Por eso Pierce (antes verde palido)
    //                se movio a violeta: dos cosas tan distintas como "te perforaron" y "te curaron"
    //                no pueden compartir el mismo color o se confunden a simple vista.
    //  - Rojo     -> Fuego.
    //  - Celeste  -> Hielo.
    //  - Amarillo -> Rayo.
    //  - Plateado -> Corte (Slash).
    //  - Violeta  -> Perforacion (Pierce).
    public static class ElementVisuals
    {
        // Naranjo canonico de "golpe": lo comparten Strike y el pulso generico de EnemyView, asi
        // el borde de disolucion y el anillo de shader coinciden siempre que el golpe sea fisico.
        public static readonly Color GolpeColor = new Color(1f, 0.55f, 0.1f);

        // Verde canonico de curacion, usado por CombatFeedback.healFlashColor.
        public static readonly Color HealColor = new Color(0.3f, 1f, 0.45f);

        public static Color ColorFor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(0.95f, 0.2f, 0.05f);
                case Element.Ice: return new Color(0.4f, 0.85f, 1f);
                case Element.Volt: return new Color(1f, 0.92f, 0.2f);
                case Element.Slash: return new Color(0.85f, 0.9f, 1f);
                case Element.Strike: return GolpeColor;
                case Element.Pierce: return new Color(0.75f, 0.45f, 1f);
                default: return Color.white;
            }
        }
    }
}
