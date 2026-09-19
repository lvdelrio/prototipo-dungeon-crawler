using UnityEngine;
using Combat;

namespace Gameplay
{
    // Mapeo compartido de elemento -> color "canonico" para todo el feedback visual de combate
    // (anillo de shader, sprite de impacto teñido, brillo de borde de camara, luz de impacto).
    // Antes vivia duplicado como metodo privado en BattleStageController.
    public static class ElementVisuals
    {
        public static Color ColorFor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return new Color(1f, 0.35f, 0.12f);
                case Element.Ice: return new Color(0.4f, 0.85f, 1f);
                case Element.Volt: return new Color(1f, 0.92f, 0.2f);
                case Element.Slash: return new Color(0.85f, 0.9f, 1f);
                case Element.Strike: return new Color(1f, 0.75f, 0.4f);
                case Element.Pierce: return new Color(0.8f, 1f, 0.7f);
                default: return Color.white;
            }
        }
    }
}
