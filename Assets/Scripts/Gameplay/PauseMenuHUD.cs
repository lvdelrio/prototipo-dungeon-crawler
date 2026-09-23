using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Combat;
using Meta;
using Lore;

namespace Gameplay
{
    // UI del menu de pausa (ver PauseMenuManager): Mapa, Leyenda, Habilidades, Codex, Equipamiento,
    // Formacion, Guardar.
    public class PauseMenuHUD : MonoBehaviour
    {
        public PauseMenuManager pauseMenu;
        public DungeonManager dungeonManager;
        public CombatManager combatManager;
        public GridPlayerController player;

        private float _saveMessageUntil;
        // Piso elegido en la pestana Mapa (sub-pestanas al costado); se resetea al piso actual
        // cada vez que se entra a la pestana, ver DrawMap.
        private int _selectedMapFloor = -1;
        private Vector2 _legendScroll;
        private Vector2 _codexScroll;
        // Estado del asistente de 3 pasos de la pestana Habilidades (ver DrawSkills): personaje
        // elegido, si ya confirmo su habilidad (paso 2 -> 3), y el feedback del ultimo intento de
        // curar.
        private CharacterStats _selectedActor;
        private bool _selectedActorConfirmed;
        private string _healMessage = "";
        private float _healMessageUntil;
        // Integrante elegido en la pestana Formacion, esperando un segundo click para
        // intercambiarlo de lugar (ver DrawFormation/DrawFormationRow).
        private CharacterStats _selectedFormationMember;

        // Personaje elegido en la pestana Equipamiento (ver DrawEquipment) -- null hasta el primer
        // click, en ese caso se muestra el primero de la party.
        private CharacterClass? _selectedEquipClass;

        private static readonly (EquipmentSlotType Slot, int AccessoryIndex, string Label)[] EquipSlots =
        {
            (EquipmentSlotType.Weapon, 0, "Arma"),
            (EquipmentSlotType.Chest, 0, "Pecho"),
            (EquipmentSlotType.Greaves, 0, "Grebas"),
            (EquipmentSlotType.Feet, 0, "Pie"),
            (EquipmentSlotType.Accessory, 0, "Accesorio 1"),
            (EquipmentSlotType.Accessory, 1, "Accesorio 2"),
        };

        void OnGUI()
        {
            if (pauseMenu == null || !pauseMenu.IsOpen || dungeonManager == null || combatManager == null) return;
            var meta = dungeonManager.Meta;
            if (meta == null) return;

            const int panelX = 60, panelY = 40, panelW = 900, panelH = 600;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");
            GUI.Label(new Rect(panelX + 20, panelY + 4, 300, 20), "MENÚ");

            // Pestanas en 2 filas (antes 1 sola, cada vez mas angosta a medida que se agregaban
            // pestanas nuevas -- con 6 de contenido mas Guardar/X ya no entraban legibles en una
            // fila). Fila de arriba: las pestanas de "consulta" (mapa/info). Fila de abajo: las que
            // cambian algo del estado de la party, mas Guardar/X al final.
            const float tabW = 130f, tabGap = 6f, tabH = 28f;
            float row1Y = panelY + 24f;
            float row2Y = row1Y + tabH + 4f;
            float tabX = panelX + 20f;

            if (UIButton.Draw(new Rect(tabX, row1Y, tabW, tabH), "Mapa"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Map);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row1Y, tabW, tabH), "Leyenda"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Legend);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row1Y, tabW, tabH), "Habilidades"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Skills);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row1Y, tabW, tabH), "Códex"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Codex);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row1Y, tabW, tabH), "Stats"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Stats);

            tabX = panelX + 20f;
            if (UIButton.Draw(new Rect(tabX, row2Y, tabW, tabH), "Equipamiento"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Equipment);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row2Y, tabW, tabH), "Formación"))
                pauseMenu.ShowTab(PauseMenuManager.Tab.Formation);
            tabX += tabW + tabGap;
            if (UIButton.Draw(new Rect(tabX, row2Y, 100f, tabH), "Guardar"))
            {
                MetaSaveService.Save(meta);
                _saveMessageUntil = Time.time + 2f;
            }
            tabX += 100f + tabGap;
            if (UIButton.Draw(new Rect(tabX, row2Y, 60f, tabH), "Cerrar (X)"))
                pauseMenu.Close();

            float y = row2Y + tabH + 12f;
            GUI.Box(new Rect(panelX + 10, y, panelW - 20, 4), "");
            y += 14;

            switch (pauseMenu.CurrentTab)
            {
                case PauseMenuManager.Tab.Map: DrawMap(panelX, y, panelW, panelH - (y - panelY)); break;
                case PauseMenuManager.Tab.Legend: DrawLegend(panelX, y, panelW, panelH - (y - panelY)); break;
                case PauseMenuManager.Tab.Skills: DrawSkills(panelX, y, panelW); break;
                case PauseMenuManager.Tab.Stats: DrawStats(panelX, y, panelW); break;
                case PauseMenuManager.Tab.Codex: DrawCodex(panelX, y, panelW, panelH - (y - panelY), meta); break;
                case PauseMenuManager.Tab.Equipment: DrawEquipment(panelX, y, panelW, meta); break;
                case PauseMenuManager.Tab.Formation: DrawFormation(panelX, y, panelW); break;
                default:
                    GUI.Label(new Rect(panelX + 20, y, panelW - 40, 24), "Elegí una opción arriba: Mapa, Leyenda, Habilidades, Códex, Stats, Equipamiento, Formación o Guardar.");
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

        // Sub-pestanas a la izquierda (una por piso ya explorado, del 0 al actual) y el mapa del
        // piso elegido a la derecha, reutilizando el mismo dibujo que MinimapUI (fondo de niebla
        // de guerra real de cada piso, no un mapa completo espoileado) -- asi el jugador puede
        // repasar el layout de pisos anteriores sin perder el progreso de descubrimiento de cada
        // uno. El marcador de posicion del jugador solo se dibuja en el piso ACTUAL.
        private void DrawMap(float panelX, float y, float panelW, float availableHeight)
        {
            var floors = dungeonManager.Floors;
            if (floors == null || floors.Count == 0) return;

            int currentIndex = dungeonManager.CurrentFloorIndex;
            if (_selectedMapFloor < 0 || _selectedMapFloor > currentIndex)
                _selectedMapFloor = currentIndex;

            const float tabsW = 130f;
            GUI.Label(new Rect(panelX + 20, y, tabsW, 20), "Pisos explorados");
            float tabY = y + 24;
            for (int i = 0; i <= currentIndex && i < floors.Count; i++)
            {
                string label = i == currentIndex ? $"{dungeonManager.FloorLabel(i)} (actual)" : dungeonManager.FloorLabel(i);
                var old = GUI.color;
                if (i == _selectedMapFloor) GUI.color = new Color(1f, 0.85f, 0.3f);
                if (UIButton.Draw(new Rect(panelX + 20, tabY, tabsW, 28), label))
                    _selectedMapFloor = i;
                GUI.color = old;
                tabY += 32;
            }

            var floor = floors[_selectedMapFloor];
            if (floor == null) return;

            float mapAreaX = panelX + 20 + tabsW + 20;
            float mapAreaW = panelW - (20 + tabsW + 20) - 20;
            int cellPixelSize = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Min(mapAreaW / floor.Width, (availableHeight - 24) / floor.Height)), 6, 16);

            bool isCurrent = _selectedMapFloor == currentIndex;
            string selectedLabel = dungeonManager.FloorLabel(_selectedMapFloor);
            GUI.Label(new Rect(mapAreaX, y, mapAreaW, 20),
                isCurrent ? $"{selectedLabel} (posición actual marcada en magenta)" : $"{selectedLabel} (tal como quedó al dejarlo)");
            DungeonMapRenderer.Draw(new Vector2(mapAreaX, y + 24), floor, playerMode: true, cellPixelSize, wallPixelThickness: 2,
                player, showPlayerMarker: isCurrent,
                foe: isCurrent ? dungeonManager.ActiveFoe : null, foeAlwaysVisible: dungeonManager.debugFoeAlwaysVisibleOnMap);
        }

        // Leyenda del mapa: un cuadradito del color exacto que usa DungeonMapRenderer (misma fuente
        // de verdad que dibuja el mapa de verdad, nunca se puede desincronizar) al lado del nombre
        // y una linea explicando que es. Dos columnas: marcadores (lo que se ve ENCIMA de una
        // celda) y colores de piso (el color de la celda en si).
        private void DrawLegend(float panelX, float y, float panelW, float availableHeight)
        {
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Qué es cada cosa en el mapa (pestaña Mapa / minimapa):");
            const float headerH = 28f;
            y += headerH;

            float colW = (panelW - 60) / 2f;
            // Filas de 48px (antes 36): a 36 las descripciones mas largas ("Activalo para abrir un
            // teletransporte...") envolvian a 2-3 lineas y se pisaban con la fila de abajo -- se
            // veian "cortadas". Ademas TODO el bloque ahora vive en un scroll view propio, asi que
            // ninguna fila puede quedar recortada por el borde del panel sin importar cuantas
            // entradas tenga la leyenda en el futuro.
            const float rowH = 48f;
            float contentH = 24f + Mathf.Max(DungeonMapRenderer.MarkerLegend.Length, DungeonMapRenderer.FloorLegend.Length) * rowH;

            float viewH = Mathf.Max(60f, availableHeight - headerH - 10f);
            var viewRect = new Rect(panelX + 10, y, panelW - 30, viewH);
            var contentRect = new Rect(0, 0, panelW - 50, contentH);

            _legendScroll = GUI.BeginScrollView(viewRect, _legendScroll, contentRect);

            GUI.Label(new Rect(10, 0, colW, 20), "Marcadores");
            GUI.Label(new Rect(30 + colW, 0, colW, 20), "Color de piso");
            float leftY = 24f, rightY = 24f;

            foreach (var entry in DungeonMapRenderer.MarkerLegend)
            {
                DrawLegendRow(10f, leftY, colW, entry);
                leftY += rowH;
            }
            foreach (var entry in DungeonMapRenderer.FloorLegend)
            {
                DrawLegendRow(30f + colW, rightY, colW, entry);
                rightY += rowH;
            }

            GUI.EndScrollView();
        }

        private void DrawLegendRow(float x, float y, float w, DungeonMapRenderer.LegendEntry entry)
        {
            const float swatch = 16f;
            var old = GUI.color;
            GUI.color = entry.Color;
            GUI.DrawTexture(new Rect(x, y + 1, swatch, swatch), Texture2D.whiteTexture);
            GUI.color = old;

            GUI.Label(new Rect(x + swatch + 8, y - 2, w - swatch - 8, 16), entry.Label);
            var descStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
            descStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(x + swatch + 8, y + 14, w - swatch - 8, 32), entry.Description, descStyle);
        }

        // Flujo en 3 pasos (como pidio el usuario, no "elegi curador" directo): 1) elegis CUALQUIER
        // personaje de la party (no solo los que curan), 2) se muestra SU habilidad -- si es de
        // curacion y la puede usar (vivo, con TP), un boton para confirmarla; si no (es de ataque,
        // o no tiene TP, o esta caido/a), se explica por que no se puede usar aca, sin trabarte:
        // siempre hay un "Volver" para probar otro personaje, 3) recien ahi elegis sobre quien.
        private void DrawSkills(float panelX, float y, float panelW)
        {
            if (_selectedActor == null || !combatManager.Party.Contains(_selectedActor))
            {
                DrawSkillActorPicker(panelX, y, panelW);
                return;
            }

            if (!_selectedActorConfirmed)
            {
                DrawSkillDetail(panelX, y, panelW);
                return;
            }

            DrawSkillTargets(panelX, y, panelW);
        }

        // Paso 1: cualquier personaje de la party, con su habilidad de un vistazo (nombre + TP) --
        // no se filtra a "solo curadores" aca, eso se explica recien en el paso 2 si corresponde.
        private void DrawSkillActorPicker(float panelX, float y, float panelW)
        {
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Paso 1/3 -- Elegí quién va a actuar:");
            y += 30;

            float bx = panelX + 20, by = y;
            int col = 0;
            foreach (var character in combatManager.Party)
            {
                if (UIButton.Draw(new Rect(bx, by, 270, 34), $"{character.Name} ({character.Class})\n{character.SkillName} -- TP {character.TP}/{character.MaxTP}", fontSize: 12))
                {
                    _selectedActor = character;
                    _selectedActorConfirmed = false;
                }
                col++;
                if (col >= 3) { col = 0; bx = panelX + 20; by += 42; }
                else bx += 278;
            }
            if (col != 0) by += 42;

            // Pociones (ver CombatManager.UsePotionOutOfCombat): a diferencia de las habilidades
            // de arriba, no dependen de a quien elijas como "actor" -- cualquiera puede usar una,
            // asi que van aparte, no como paso 2/3 del wizard.
            var meta = dungeonManager.Meta;
            by += 10;
            GUI.Label(new Rect(panelX + 20, by, panelW - 40, 20), $"Pociones ({meta.PotionCharges}): cura hasta {CombatEngine.PotionHealAmount} HP, sin gastar TP.");
            by += 24;
            bx = panelX + 20;
            foreach (var character in combatManager.Party)
            {
                bool full = character.HP >= character.MaxHP;
                bool enabled = meta.PotionCharges > 0 && character.IsAlive && !full;
                string label = !character.IsAlive ? $"{character.Name}: caído/a" : full ? $"{character.Name}: HP lleno" : $"Curar a {character.Name}";
                if (UIButton.Draw(new Rect(bx, by, 200, 26), label, enabled: enabled))
                {
                    int healed = combatManager.UsePotionOutOfCombat(meta, character);
                    _healMessage = healed > 0 ? $"Poción: {character.Name} recuperó {healed} HP." : "No se pudo usar la poción.";
                    _healMessageUntil = Time.time + 2.5f;
                }
                bx += 208;
            }
        }

        // Paso 2: la habilidad del personaje elegido, con el motivo claro de por que si o no se
        // puede usar fuera de combate (solo las de curacion tienen sentido aca -- una de ataque no
        // tiene a quien pegarle sin un enemigo).
        private void DrawSkillDetail(float panelX, float y, float panelW)
        {
            if (UIButton.Draw(new Rect(panelX + 20, y, 100, 26), "< Volver"))
            {
                _selectedActor = null;
                return;
            }
            y += 38;

            var actor = _selectedActor;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 22), $"Paso 2/3 -- {actor.Name} ({actor.Class})");
            y += 26;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 22), $"Habilidad: {actor.SkillName}  (costo {actor.SkillTpCost} TP, tiene {actor.TP}/{actor.MaxTP})");
            y += 30;

            bool alive = actor.IsAlive;
            bool isHeal = actor.IsHealSkill;
            bool canAfford = alive && actor.TP >= actor.SkillTpCost;
            bool usable = alive && isHeal && canAfford;

            string reason = !alive
                ? $"{actor.Name} está caído/a: no puede usar ninguna habilidad ahora."
                : !isHeal
                    ? $"\"{actor.SkillName}\" es una habilidad de ataque: solo se puede usar en combate, contra un enemigo."
                    : !canAfford
                        ? $"No tiene TP suficiente ({actor.TP}/{actor.SkillTpCost})."
                        : $"Cura hasta {actor.HealAmount + actor.MagicAttack / 2} HP. Elegí sobre quién usarla.";

            var reasonStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            reasonStyle.normal.textColor = usable ? new Color(0.6f, 1f, 0.65f) : new Color(0.85f, 0.75f, 0.4f);
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 40), reason, reasonStyle);
            y += 46;

            if (UIButton.Draw(new Rect(panelX + 20, y, 220, 34), "Usar habilidad", enabled: usable))
                _selectedActorConfirmed = true;
        }

        // Paso 3: sobre quien usarla. Curar aplica la MISMA cuenta que en combate (ver
        // CombatManager.UseHealSkillOutOfCombat), sin QTE ni turno, directo sobre los CharacterStats
        // de siempre (se nota apenas entras a la proxima pelea).
        private void DrawSkillTargets(float panelX, float y, float panelW)
        {
            if (UIButton.Draw(new Rect(panelX + 20, y, 100, 26), "< Volver"))
            {
                _selectedActorConfirmed = false;
                return;
            }
            y += 38;

            var actor = _selectedActor;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 22),
                $"Paso 3/3 -- {actor.Name} usa {actor.SkillName} (cura hasta {actor.HealAmount + actor.MagicAttack / 2} HP). Elegí a quién:");
            y += 30;

            bool canAfford = actor.IsAlive && actor.TP >= actor.SkillTpCost;

            foreach (var target in combatManager.Party)
            {
                GUI.Label(new Rect(panelX + 20, y, 220, 24), $"{target.Name} ({target.Class})");
                var hpStyle = new GUIStyle(GUI.skin.label);
                hpStyle.normal.textColor = !target.IsAlive ? new Color(1f, 0.4f, 0.4f)
                    : target.HP >= target.MaxHP ? new Color(0.5f, 1f, 0.6f) : Color.white;
                GUI.Label(new Rect(panelX + 250, y, 150, 24), $"HP {target.HP}/{target.MaxHP}", hpStyle);

                bool full = target.HP >= target.MaxHP;
                bool enabled = canAfford && target.IsAlive && !full;
                string label = !target.IsAlive ? "Caído/a" : full ? "HP lleno" : "Curar";
                if (UIButton.Draw(new Rect(panelX + 420, y, 150, 24), label, enabled: enabled))
                {
                    int healed = combatManager.UseHealSkillOutOfCombat(actor, target);
                    _healMessage = healed > 0
                        ? $"{actor.Name} curó {healed} HP a {target.Name}."
                        : "No se pudo curar.";
                    _healMessageUntil = Time.time + 2.5f;
                    // A proposito NO se cambia de paso/pestana: te quedas en la lista de objetivos
                    // (el numero de HP de arriba se actualiza solo, porque se lee en vivo de
                    // target.HP cada frame) para poder curar a otro integrante de una, sin tener
                    // que volver a elegir personaje ni habilidad de nuevo.
                }
                y += 30;
            }

            if (Time.time < _healMessageUntil)
            {
                var old = GUI.color;
                GUI.color = new Color(0.5f, 1f, 0.6f);
                GUI.Label(new Rect(panelX + 20, y + 6, panelW - 40, 24), _healMessage);
                GUI.color = old;
            }
        }

        // Tabla de solo lectura con los stats de combate de cada integrante (ATQ, ATQ Magico, DEF,
        // Evasion, Velocidad, Suerte, mas HP/TP) -- para consultar sin ir hasta la tienda post-run.
        // Cada clase tiene ciertos puntos mas desarrollados que otros (ver PartyFactory): esto deja
        // verlo de un vistazo en vez de tener que memorizarlo.
        private void DrawStats(float panelX, float y, float panelW)
        {
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Stats de la party (solo lectura -- se suben en la tienda al final de una run):");
            y += 28;

            (string label, System.Func<CharacterStats, string> get)[] columns =
            {
                ("Personaje", c => $"{c.Name} ({c.Class})"),
                ("ATQ", c => c.Attack.ToString()),
                ("ATQ MAG", c => c.MagicAttack.ToString()),
                ("DEF", c => c.Defense.ToString()),
                ("EVA", c => $"{c.Evasion}%"),
                ("VEL", c => c.Speed.ToString()),
                ("SUERTE", c => $"{c.Luck}%"),
                ("HP", c => $"{c.HP}/{c.MaxHP}"),
                ("TP", c => $"{c.TP}/{c.MaxTP}"),
            };
            float[] colW = { 190f, 55f, 80f, 55f, 55f, 55f, 70f, 90f, 90f };

            var colX = new float[columns.Length];
            float cx = panelX + 20;
            for (int i = 0; i < columns.Length; i++) { colX[i] = cx; cx += colW[i]; }

            var headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            for (int i = 0; i < columns.Length; i++)
                GUI.Label(new Rect(colX[i], y, colW[i], 20), columns[i].label, headerStyle);
            y += 24;

            foreach (var character in combatManager.Party)
            {
                for (int i = 0; i < columns.Length; i++)
                    GUI.Label(new Rect(colX[i], y, colW[i], 22), columns[i].get(character));
                y += 26;
            }
        }

        // Entradas descubiertas muestran titulo+texto; las no descubiertas quedan como "???" para
        // no espoilear el contenido, pero avisan que existen (motiva a seguir explorando).
        private void DrawCodex(float panelX, float y, float panelW, float availableHeight, MetaProgress meta)
        {
            // Tip de mecanica (siempre visible, no es un fragmento de lore descubrible): el FOE es
            // nuevo y no tiene ninguna otra explicacion en pantalla salvo chocarse con el.
            var tipStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, wordWrap = true };
            tipStyle.normal.textColor = new Color(0.85f, 0.75f, 0.4f);
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 32),
                "FOE: enemigo fuerte que patrulla a la vista, blanco y tranquilo -- si te acercás se pone rojo y te persigue. Colisionar arranca un combate donde escapar cuesta más (30%).",
                tipStyle);
            y += 40;

            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), $"Fragmentos de lore descubiertos: {meta.UnlockedLoreIds.Count}/{LoreCatalog.All.Length}");
            const float headerH = 26f;
            y += headerH;

            // Todo el bloque de entradas vive en su propio scroll view (mismo patron que DrawLegend):
            // la altura de cada fila varia (64 desbloqueada, 28 sin descubrir todavia) asi que se sum
            // caso por caso en vez de asumir una altura fija -- ninguna entrada queda recortada por
            // el borde del panel sin importar cuantos fragmentos de lore tenga el juego.
            float contentH = 4f;
            foreach (var entry in LoreCatalog.All)
                contentH += meta.IsLoreUnlocked(entry.Id) ? 70f : 34f;

            float viewH = Mathf.Max(60f, availableHeight - headerH - 40f - 10f);
            var viewRect = new Rect(panelX + 10, y, panelW - 30, viewH);
            var contentRect = new Rect(0, 0, panelW - 50, contentH);

            _codexScroll = GUI.BeginScrollView(viewRect, _codexScroll, contentRect);

            float rowY = 0f;
            foreach (var entry in LoreCatalog.All)
            {
                // Las 3 pistas de la Puerta Fria (ver DungeonGen.DungeonGenerator.BiomeGateLoreIds)
                // son mecanicamente importantes (apuntan a un secreto real, no solo sabor) -- se
                // marcan distinto para que no se pierdan entre el resto del lore.
                bool isMysteryClue = System.Array.IndexOf(DungeonGen.DungeonGenerator.BiomeGateLoreIds, entry.Id) >= 0;
                bool unlocked = meta.IsLoreUnlocked(entry.Id);
                var boxColor = GUI.color;
                if (isMysteryClue) GUI.color = new Color(1f, 0.9f, 0.5f, 0.5f);
                GUI.Box(new Rect(10, rowY, panelW - 60, unlocked ? 64 : 28), "");
                GUI.color = boxColor;

                if (isMysteryClue)
                {
                    var tagStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 10 };
                    tagStyle.normal.textColor = new Color(1f, 0.82f, 0.2f);
                    GUI.Label(new Rect(panelW - 210, rowY + 4, 140, 16), "◆ PISTA PRINCIPAL", tagStyle);
                }

                if (unlocked)
                {
                    GUI.Label(new Rect(20, rowY + 4, panelW - 90, 20), entry.Title);
                    GUI.Label(new Rect(20, rowY + 24, panelW - 90, 36), entry.Text);
                    rowY += 70;
                }
                else
                {
                    GUI.Label(new Rect(20, rowY + 4, panelW - 90, 20), "??? (todavía sin descubrir)");
                    rowY += 34;
                }
            }

            GUI.EndScrollView();
        }

        // 6 slots por personaje (Arma/Pecho/Grebas/Pie/2 Accesorios, ver EquipSlots): se elige un
        // integrante arriba y se arma su equipo slot por slot debajo, con efecto inmediato sobre la
        // party actual (CombatManager.SetEquippedItemLive). Los items se compran en la tienda
        // post-run (MetaShopHUD) o se encuentran en cofres (DungeonManager.RollTreasureLoot); cada
        // compra/hallazgo es una INSTANCIA fisica propia (Meta.EquipmentInstance) -- si comprás 2
        // Placas Reforzadas podes ponerle una a cada uno de 2 personajes distintos, pero una
        // instancia puesta en un slot no aparece como opcion en ningun otro hasta desequiparla.
        private void DrawEquipment(float panelX, float y, float panelW, MetaProgress meta)
        {
            if (combatManager.Party.Count == 0) return;
            var selected = combatManager.Party.FirstOrDefault(p => p.Class == _selectedEquipClass) ?? combatManager.Party[0];
            _selectedEquipClass = selected.Class;

            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 18), "Elegí un integrante y armá su equipo. Las armas pueden estar restringidas por clase.");
            y += 24;

            float bx = panelX + 20;
            foreach (var character in combatManager.Party)
            {
                bool isSelected = character.Class == selected.Class;
                if (UIButton.Draw(new Rect(bx, y, 130, 26), character.Class.ToString(), accentColor: isSelected ? new Color(1f, 0.85f, 0.3f) : (Color?)null))
                    _selectedEquipClass = character.Class;
                bx += 136;
            }
            y += 34;

            var totals = EquipmentTotals.From(meta.GetEquippedItems(selected.Class));
            string resistSummary = totals.Resistances.Count > 0
                ? string.Join(", ", totals.Resistances.Select(r => $"{ElementLabel(r.Element)} {r.Percent}%"))
                : "ninguna";
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 40),
                $"{selected.Name} -- ATQ +{totals.Attack}  ATQ.MAG +{totals.MagicAttack}  DEF +{totals.Defense}  VEL +{totals.Speed}  EVA +{totals.Evasion}  PV +{totals.MaxHp}  Espinas {totals.Thorns}%  Resistencias: {resistSummary}",
                new GUIStyle(GUI.skin.label) { wordWrap = true });
            y += 40;

            foreach (var slotDef in EquipSlots)
            {
                var equippedItem = meta.GetEquippedItem(selected.Class, slotDef.Slot, slotDef.AccessoryIndex);
                GUI.Label(new Rect(panelX + 20, y, 140, 22), slotDef.Label + ":");
                GUI.Label(new Rect(panelX + 160, y, 280, 22), equippedItem != null ? equippedItem.Name : "(vacío)");

                float rx = panelX + 440;
                if (UIButton.Draw(new Rect(rx, y, 80, 22), "Quitar", enabled: equippedItem != null))
                    combatManager.SetEquippedItemLive(meta, selected.Class, slotDef.Slot, "", slotDef.AccessoryIndex);
                y += 26;

                // Instancias LIBRES (ni puestas en este ni en ningun otro slot -- una instancia
                // equipada en otro personaje no aparece aca, hay que desequiparla primero) que
                // calzan en este slot y, si es Arma, en la clase elegida. Agrupadas por item del
                // catalogo (si tenes 3 Placas Reforzadas libres, un solo boton con "x3") para no
                // mostrar 3 botones identicos -- clickearlo equipa UNA de esas instancias libres.
                // Envuelve a la linea siguiente si no entran todos en el ancho del panel.
                float ix = panelX + 20;
                var freeGroups = meta.Inventory
                    .Where(inst => !meta.IsInstanceEquipped(inst.InstanceId))
                    .GroupBy(inst => inst.ItemId);
                foreach (var group in freeGroups)
                {
                    var item = EquipmentCatalog.Find(group.Key);
                    if (item == null || item.Slot != slotDef.Slot) continue;
                    if (item.AllowedClasses.Length > 0 && Array.IndexOf(item.AllowedClasses, selected.Class) < 0) continue;

                    int count = group.Count();
                    string label = count > 1 ? $"{item.Name} x{count}" : item.Name;
                    string instanceToEquip = group.First().InstanceId;

                    const float btnW = 150f;
                    if (ix + btnW > panelX + panelW - 20)
                    {
                        ix = panelX + 20;
                        y += 26;
                    }
                    if (UIButton.Draw(new Rect(ix, y, btnW, 22), label, fontSize: 12))
                        combatManager.SetEquippedItemLive(meta, selected.Class, slotDef.Slot, instanceToEquip, slotDef.AccessoryIndex);
                    ix += btnW + 6;
                }
                y += 32;
            }

            if (meta.Inventory.Count == 0)
            {
                y += 6;
                GUI.Label(new Rect(panelX + 20, y, panelW - 40, 24), "Todavía no compraste ningún equipo. Se compran con puntos en la tienda al terminar una run, o se encuentran en cofres.");
            }
        }

        private static string ElementLabel(Element element)
        {
            switch (element)
            {
                case Element.Fire: return "Fuego";
                case Element.Ice: return "Hielo";
                case Element.Volt: return "Rayo";
                case Element.Slash: return "Corte";
                case Element.Strike: return "Golpe";
                case Element.Pierce: return "Perforación";
                default: return "Ninguno";
            }
        }

        // FRENTE en una fila horizontal arriba, FONDO en una fila horizontal debajo (antes eran 2
        // columnas lado a lado). Para cambiar: click en un integrante lo selecciona (se resalta),
        // click en OTRO los intercambia entre si (CombatManager.SwapFormation) y deselecciona;
        // click en el mismo que ya estaba seleccionado cancela la seleccion.
        private void DrawFormation(float panelX, float y, float panelW)
        {
            int frontCount = combatManager.Party.Count(p => p.IsFrontRow);
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), $"Formación (siempre 3 y 3) -- Frente: {frontCount}/3. Los personajes del frente reciben más ataques enemigos.");
            y += 28;

            var front = combatManager.Party.Where(p => p.IsFrontRow).ToList();
            var back = combatManager.Party.Where(p => !p.IsFrontRow).ToList();

            GUI.Label(new Rect(panelX + 20, y, 300, 20), "FRENTE");
            y += 22;
            DrawFormationRow(panelX + 20, y, front);
            y += 56;

            GUI.Label(new Rect(panelX + 20, y, 300, 20), "FONDO");
            y += 22;
            DrawFormationRow(panelX + 20, y, back);
            y += 56;

            var hintStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            hintStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            string hint = _selectedFormationMember != null
                ? $"Elegiste a {_selectedFormationMember.Name}. Hacé click en otro integrante para cambiarlo de lugar, o click en {_selectedFormationMember.Name} de nuevo para cancelar."
                : "Hacé click en un integrante y después en otro para intercambiarlos de lugar.";
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 24), hint, hintStyle);
        }

        private void DrawFormationRow(float x, float y, List<CharacterStats> members)
        {
            float bx = x;
            foreach (var member in members)
            {
                bool selected = member == _selectedFormationMember;
                var old = GUI.color;
                if (selected) GUI.color = new Color(1f, 0.85f, 0.3f);
                if (UIButton.Draw(new Rect(bx, y, 260, 42), $"{member.Name}\n({member.Class})", fontSize: 13))
                {
                    if (_selectedFormationMember == member)
                        _selectedFormationMember = null; // click de nuevo sobre el mismo: cancela
                    else if (_selectedFormationMember == null)
                        _selectedFormationMember = member; // primer click: selecciona
                    else
                    {
                        combatManager.SwapFormation(_selectedFormationMember, member); // segundo click sobre otro: intercambia
                        _selectedFormationMember = null;
                    }
                }
                GUI.color = old;
                bx += 268;
            }
        }
    }
}
