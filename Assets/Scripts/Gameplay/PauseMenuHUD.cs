using System.Linq;
using UnityEngine;
using Combat;
using Meta;
using Lore;

namespace Gameplay
{
    // UI del menu de pausa (ver PauseMenuManager): Codex, Equipamiento, Formacion, Guardar.
    public class PauseMenuHUD : MonoBehaviour
    {
        public PauseMenuManager pauseMenu;
        public DungeonManager dungeonManager;
        public CombatManager combatManager;

        private float _saveMessageUntil;

        void OnGUI()
        {
            if (pauseMenu == null || !pauseMenu.IsOpen || dungeonManager == null || combatManager == null) return;
            var meta = dungeonManager.Meta;
            if (meta == null) return;

            const int panelX = 60, panelY = 40, panelW = 900, panelH = 600;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 20, panelY + 8, 300, 26), "MENÚ");

            float tabY = panelY + 8;
            if (UIButton.Draw(new Rect(panelX + panelW - 620, tabY, 140, 28), "Códex"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Codex);
            if (UIButton.Draw(new Rect(panelX + panelW - 470, tabY, 140, 28), "Equipamiento"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Equipment);
            if (UIButton.Draw(new Rect(panelX + panelW - 320, tabY, 140, 28), "Formación"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Formation);
            if (UIButton.Draw(new Rect(panelX + panelW - 170, tabY, 90, 28), "Guardar"))
            {
                MetaSaveService.Save(meta);
                _saveMessageUntil = Time.time + 2f;
            }
            if (UIButton.Draw(new Rect(panelX + panelW - 70, tabY, 50, 28), "X"))
                pauseMenu.Close();

            float y = panelY + 46;
            GUI.Box(new Rect(panelX + 10, y, panelW - 20, 4), "");
            y += 14;

            switch (pauseMenu.CurrentTab)
            {
                case PauseMenuManager.Tab.Codex: DrawCodex(panelX, y, panelW, meta); break;
                case PauseMenuManager.Tab.Equipment: DrawEquipment(panelX, y, panelW, meta); break;
                case PauseMenuManager.Tab.Formation: DrawFormation(panelX, y, panelW); break;
                default:
                    GUI.Label(new Rect(panelX + 20, y, panelW - 40, 24), "Elegí una opción arriba: Códex, Equipamiento, Formación o Guardar.");
                    break;
            }

            if (Time.time < _saveMessageUntil)
            {
                var old = GUI.color;
                GUI.color = new Color(0.4f, 1f, 0.5f);
                GUI.Label(new Rect(panelX + 20, panelY + panelH - 30, 300, 24), "¡Progreso guardado!");
                GUI.color = old;
            }
        }

        // Entradas descubiertas muestran titulo+texto; las no descubiertas quedan como "???" para
        // no espoilear el contenido, pero avisan que existen (motiva a seguir explorando).
        private void DrawCodex(float panelX, float y, float panelW, MetaProgress meta)
        {
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), $"Fragmentos de lore descubiertos: {meta.UnlockedLoreIds.Count}/{LoreCatalog.All.Length}");
            y += 26;
            foreach (var entry in LoreCatalog.All)
            {
                bool unlocked = meta.IsLoreUnlocked(entry.Id);
                GUI.Box(new Rect(panelX + 20, y, panelW - 40, unlocked ? 64 : 28), "");
                if (unlocked)
                {
                    GUI.Label(new Rect(panelX + 30, y + 4, panelW - 60, 20), entry.Title);
                    GUI.Label(new Rect(panelX + 30, y + 24, panelW - 60, 36), entry.Text);
                    y += 70;
                }
                else
                {
                    GUI.Label(new Rect(panelX + 30, y + 4, panelW - 60, 20), "??? (todavía sin descubrir)");
                    y += 34;
                }
            }
        }

        // Un accesorio por personaje; los items se compran con puntos en la tienda post-run
        // (MetaShopHUD) y se equipan/desequipan aca, con efecto inmediato sobre la party actual.
        private void DrawEquipment(float panelX, float y, float panelW, MetaProgress meta)
        {
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Un accesorio por personaje (se compran nuevos en la tienda al final de la run).");
            y += 26;

            foreach (var character in combatManager.Party)
            {
                string equippedId = meta.GetEquippedItemId(character.Class);
                var equippedItem = EquipmentCatalog.Find(equippedId);
                GUI.Label(new Rect(panelX + 20, y, 220, 24), $"{character.Name} ({character.Class})");
                GUI.Label(new Rect(panelX + 250, y, 260, 24), equippedItem != null ? equippedItem.Name : "(sin equipar)");

                float bx = panelX + 520;
                if (UIButton.Draw(new Rect(bx, y, 90, 24), "Ninguno"))
                    combatManager.SetEquippedItemLive(meta, character.Class, "");
                bx += 96;

                foreach (var itemId in meta.OwnedItemIds)
                {
                    if (itemId == equippedId) continue;
                    var item = EquipmentCatalog.Find(itemId);
                    if (item == null) continue;
                    if (UIButton.Draw(new Rect(bx, y, 130, 24), item.Name))
                        combatManager.SetEquippedItemLive(meta, character.Class, itemId);
                    bx += 136;
                }
                y += 30;
            }

            if (meta.OwnedItemIds.Count == 0)
            {
                y += 10;
                GUI.Label(new Rect(panelX + 20, y, panelW - 40, 24), "Todavía no compraste ningún accesorio. Se compran con puntos en la tienda al terminar una run.");
            }
        }

        // Clic en cualquier boton pide ese lado (frente/fondo) para ese personaje; CombatManager
        // se encarga de mantener el balance 3/3 intercambiando con otro si hace falta.
        private void DrawFormation(float panelX, float y, float panelW)
        {
            int frontCount = combatManager.Party.Count(p => p.IsFrontRow);
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), $"Formación (siempre 3 y 3) -- Frente: {frontCount}/3. Los personajes del frente reciben más ataques enemigos.");
            y += 30;

            GUI.Label(new Rect(panelX + 20, y, 200, 20), "FRENTE");
            GUI.Label(new Rect(panelX + 470, y, 200, 20), "FONDO");
            y += 22;

            var front = combatManager.Party.Where(p => p.IsFrontRow).ToList();
            var back = combatManager.Party.Where(p => !p.IsFrontRow).ToList();

            for (int i = 0; i < System.Math.Max(front.Count, back.Count); i++)
            {
                if (i < front.Count)
                {
                    var p = front[i];
                    if (UIButton.Draw(new Rect(panelX + 20, y, 400, 30), $"{p.Name} ({p.Class}) -> mover al fondo"))
                        combatManager.SetFrontRow(p, false);
                }
                if (i < back.Count)
                {
                    var p = back[i];
                    if (UIButton.Draw(new Rect(panelX + 470, y, 400, 30), $"{p.Name} ({p.Class}) -> mover al frente"))
                        combatManager.SetFrontRow(p, true);
                }
                y += 36;
            }
        }
    }
}
