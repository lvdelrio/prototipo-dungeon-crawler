using UnityEngine;
using Combat;

namespace Gameplay
{
    // Barra de vida (sin numero exacto) + barra de "aguante" flotando arriba de cada enemigo, en
    // la escena de batalla 3D. Proyecta la posicion de cada EnemyView a coordenadas de pantalla
    // con la camara de batalla y dibuja ahi dos barras simples (OnGUI, mismo estilo minimalista
    // que el resto de la UI del prototipo). No reemplaza el panel de texto de CombatHUD (que
    // sigue mostrando el HP exacto para decisiones tacticas): esto es solo la lectura rapida "en
    // el mundo", a proposito sin numeros.
    public class EnemyHealthBarHUD : MonoBehaviour
    {
        public CombatManager combatManager;
        public BattleStageController battleStage;

        private const float BarWidth = 110f;
        private const float HpBarHeight = 9f;
        private const float PoiseBarHeight = 6f;
        private const float BarGap = 2f;

        void OnGUI()
        {
            if (combatManager == null || battleStage == null || !combatManager.IsActive) return;
            var cam = battleStage.ActiveBattleCamera;
            if (cam == null || !cam.enabled) return;
            if (combatManager.Enemies == null) return;

            for (int i = 0; i < combatManager.Enemies.Count; i++)
            {
                var enemy = combatManager.Enemies[i];
                if (!enemy.IsAlive) continue;

                var view = battleStage.GetEnemyView(i);
                if (view == null) continue;

                Vector3 screenPos = cam.WorldToScreenPoint(view.TopAnchor);
                if (screenPos.z <= 0f) continue; // detras de la camara

                float x = screenPos.x - BarWidth / 2f;
                float y = Screen.height - screenPos.y;

                float hpFrac = enemy.MaxHP > 0 ? (float)enemy.HP / enemy.MaxHP : 0f;
                DrawBar(x, y, hpFrac, HpColor(hpFrac));

                if (enemy.MaxPoise > 0)
                {
                    float poiseFrac = enemy.IsBroken ? 0f : (float)enemy.Poise / enemy.MaxPoise;
                    float poiseY = y + HpBarHeight + BarGap;
                    DrawBar(x, poiseY, poiseFrac, PoiseColor(enemy), PoiseBarHeight);

                    if (enemy.IsBroken)
                        DrawCenteredLabel(x, poiseY - 14f, "¡ROTO!", new Color(1f, 0.85f, 0.2f));
                }
            }
        }

        private void DrawBar(float x, float y, float fill, Color fillColor, float height = HpBarHeight)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 1f, y - 1f, BarWidth + 2f, height + 2f), Texture2D.whiteTexture);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.85f);
            GUI.DrawTexture(new Rect(x, y, BarWidth, height), Texture2D.whiteTexture);
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(x, y, BarWidth * Mathf.Clamp01(fill), height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void DrawCenteredLabel(float barX, float y, string text, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            style.normal.textColor = color;
            GUI.Label(new Rect(barX - 10f, y, BarWidth + 20f, 14f), text, style);
        }

        // Verde por encima de la mitad, amarillo en la mitad baja, rojo cerca de morir -- sin
        // mostrar el numero, para que la barra "encima del enemigo" sea puramente visual.
        private static Color HpColor(float frac)
        {
            if (frac > 0.5f) return new Color(0.3f, 0.85f, 0.35f);
            if (frac > 0.25f) return new Color(0.95f, 0.85f, 0.25f);
            return new Color(0.9f, 0.25f, 0.2f);
        }

        // Cian normal; se pone blanco brillante cuando esta a punto de romperse, para avisar que
        // un golpe mas probablemente lo deja aturdido.
        private static Color PoiseColor(EnemyStats enemy)
        {
            if (enemy.MaxPoise <= 0) return Color.clear;
            float frac = (float)enemy.Poise / enemy.MaxPoise;
            return frac <= 0.2f ? new Color(0.9f, 0.95f, 1f) : new Color(0.3f, 0.75f, 0.95f);
        }
    }
}
