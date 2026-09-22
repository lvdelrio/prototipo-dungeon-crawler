using UnityEngine;
using Combat;
using Meta;

namespace Gameplay
{
    // Pantalla que aparece cuando la party muere o vence al jefe y la run termina: deja gastar los
    // puntos ganados (piso alcanzado + enemigos/jefes derrotados) en mejoras permanentes de
    // personajes y en items (Mapa, Perforador, accesorios) para la proxima run. No se puede cerrar
    // sin elegir "Comenzar nueva run": mientras esta abierta, el jugador no puede moverse (ver
    // GridPlayerController).
    //
    // Pantalla completa y fondo negro (a proposito -- se siente a "partida terminada", no un panel
    // flotando sobre la mazmorra) con dos pestanas, cada una con su propia "escena": Subir de Nivel
    // (silueta de guerrero con la espada en alto) y Comprar (silueta de mercader ofreciendo un
    // item, con una frase de venta al azar). Las siluetas son arte procedural, ver SilhouetteArt --
    // el proyecto todavia no tiene assets de arte importados.
    public class MetaShopHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public CombatManager combatManager;

        private static readonly UpgradeStat[] Stats =
            { UpgradeStat.Attack, UpgradeStat.Defense, UpgradeStat.Speed, UpgradeStat.MaxHp, UpgradeStat.MaxTp, UpgradeStat.MagicAttack, UpgradeStat.Evasion, UpgradeStat.Luck };
        private static readonly string[] StatLabels = { "ATQ", "DEF", "VEL", "HP", "TP", "ATQM", "EVA", "SUERTE" };

        private static readonly string[] MerchantLines =
        {
            "¿Interesado en algo, aventurero?",
            "Todo tiene un precio justo. El mío, un poco menos.",
            "Fresco del piso de abajo. No preguntes cómo.",
            "Con esto vas a durar un ratito más ahí abajo.",
            "Comprá ahora, arrepentite después.",
        };

        private static readonly string[] WarriorLines =
        {
            "El poder que ganaste no se olvida la próxima vez.",
            "Cada nivel es una cicatriz menos.",
            "Más fuerte. Más rápido. Más vivo.",
        };

        private enum ShopTab { Levels, Shop }

        private ShopTab _tab = ShopTab.Levels;
        private bool _wasActive;
        private string _merchantLine = "";
        private string _warriorLine = "";

        void OnGUI()
        {
            bool active = dungeonManager != null && combatManager != null && dungeonManager.IsGameOverShopActive;
            if (!active) { _wasActive = false; return; }
            var meta = dungeonManager.Meta;
            if (meta == null) return;

            // Cada vez que la pantalla PASA de cerrada a abierta, se elige una frase nueva al azar
            // -- asi no repite siempre la misma pero tampoco cambia sola cuadro a cuadro.
            if (!_wasActive)
            {
                _wasActive = true;
                _merchantLine = MerchantLines[Random.Range(0, MerchantLines.Length)];
                _warriorLine = WarriorLines[Random.Range(0, WarriorLines.Length)];
            }

            DrawBlackBackdrop();

            float contentW = Mathf.Min(Screen.width - 80f, 1200f);
            float contentX = (Screen.width - contentW) / 2f;
            float y = DrawHeader(contentX, contentW, meta);

            y = DrawTabs(contentX, contentW, y);

            float bottomReserve = 70f;
            float contentBottom = Screen.height - bottomReserve;

            if (_tab == ShopTab.Levels)
                DrawLevelsTab(contentX, y, contentW, contentBottom, meta);
            else
                DrawShopTab(contentX, y, contentW, contentBottom, meta);

            DrawStartRunButton(contentX, contentW);
        }

        private void DrawBlackBackdrop()
        {
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Titulo dentro de una banda roja apenas inclinada (el "corte diagonal" tipico de los menus
        // de Persona): la banda se dibuja rotada, pero el texto se dibuja DESPUES de restaurar la
        // matriz, derecho, para que siga siendo legible.
        private float DrawHeader(float x, float w, MetaProgress meta)
        {
            float bannerY = 34f, bannerH = 64f;
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-2.5f, new Vector2(Screen.width / 2f, bannerY + bannerH / 2f));
            var oldColor = GUI.color;
            GUI.color = new Color(0.82f, 0.08f, 0.1f);
            GUI.DrawTexture(new Rect(-40f, bannerY, Screen.width + 80f, bannerH), Texture2D.whiteTexture);
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 30,
            };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, bannerY, w, bannerH),
                dungeonManager.LastRunWasVictory ? "¡RUN COMPLETADA! DERROTASTE AL JEFE" : "RUN TERMINADA",
                titleStyle);

            float y = bannerY + bannerH + 14f;
            var subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 };
            subStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(x, y, w, 22),
                $"Puntos ganados esta run: {dungeonManager.LastRunPointsEarned}   |   Puntos disponibles: {meta.BankedPoints}",
                subStyle);
            return y + 32f;
        }

        private float DrawTabs(float x, float w, float y)
        {
            float tabW = 260f, tabGap = 16f;
            float startX = x + (w - (tabW * 2 + tabGap)) / 2f;

            Color goldAccent = new Color(1f, 0.78f, 0.2f);
            if (UIButton.Draw(new Rect(startX, y, tabW, 40),
                    "SUBIR DE NIVEL", accentColor: _tab == ShopTab.Levels ? goldAccent : (Color?)null))
                _tab = ShopTab.Levels;
            if (UIButton.Draw(new Rect(startX + tabW + tabGap, y, tabW, 40),
                    "COMPRAR", accentColor: _tab == ShopTab.Shop ? goldAccent : (Color?)null))
                _tab = ShopTab.Shop;

            return y + 56f;
        }

        private void DrawLevelsTab(float x, float y, float w, float bottom, MetaProgress meta)
        {
            float sceneW = w * 0.24f;
            float listW = w - sceneW - 24f;

            DrawSidePanel(new Rect(x + listW + 24f, y, sceneW, bottom - y),
                drawArt: area => SilhouetteArt.DrawWarrior(area, new Color(0f, 0f, 0f, 0.9f)),
                accentColor: new Color(0.5f, 0.1f, 0.1f), quote: _warriorLine);

            GUI.Label(new Rect(x, y, listW, 20), "Mejoras permanentes de personajes:");
            y += 26;

            float col0 = x, colW = (listW - 120f) / Stats.Length;
            for (int s = 0; s < Stats.Length; s++)
                GUI.Label(new Rect(col0 + 120 + s * colW, y, colW, 18), StatLabels[s]);
            y += 22;

            foreach (var character in combatManager.Party)
            {
                GUI.Label(new Rect(col0, y, 120, 24), character.Name);
                for (int s = 0; s < Stats.Length; s++)
                {
                    var stat = Stats[s];
                    int level = meta.GetUpgradeLevel(character.Class, stat);
                    int cost = meta.GetUpgradeCost(character.Class, stat);
                    float bx = col0 + 120 + s * colW;

                    if (cost < 0)
                    {
                        GUI.Label(new Rect(bx, y, colW - 10, 24), $"Lv {level}/{MetaProgress.UpgradeMaxLevel} (MAX)");
                    }
                    else
                    {
                        if (UIButton.Draw(new Rect(bx, y, colW - 10, 24), $"Lv {level}->{level + 1} ({cost}p)", enabled: meta.BankedPoints >= cost))
                        {
                            meta.TryPurchaseUpgrade(character.Class, stat);
                            MetaSaveService.Save(meta);
                        }
                    }
                }
                y += 30;
            }
        }

        private void DrawShopTab(float x, float y, float w, float bottom, MetaProgress meta)
        {
            float sceneW = w * 0.28f;
            float listW = w - sceneW - 24f;

            DrawSidePanel(new Rect(x + listW + 24f, y, sceneW, bottom - y),
                drawArt: area => SilhouetteArt.DrawMerchant(area, new Color(0f, 0f, 0f, 0.9f)),
                accentColor: new Color(0.15f, 0.35f, 0.15f), quote: _merchantLine);

            GUI.Label(new Rect(x, y, listW, 20), "Ítems de exploración para la próxima run:");
            y += 26;

            float itemW = (listW - 16f) / 3f;
            if (UIButton.Draw(new Rect(x, y, itemW, 32), $"Comprar Mapa ({meta.MapCharges}) - {MetaProgress.MapCost}p", enabled: meta.BankedPoints >= MetaProgress.MapCost))
            {
                meta.TryPurchaseMap();
                MetaSaveService.Save(meta);
            }
            if (UIButton.Draw(new Rect(x + itemW + 8f, y, itemW, 32), $"Comprar Perforador ({meta.DrillCharges}) - {MetaProgress.DrillCost}p", enabled: meta.BankedPoints >= MetaProgress.DrillCost))
            {
                meta.TryPurchaseDrill();
                MetaSaveService.Save(meta);
            }
            if (UIButton.Draw(new Rect(x + (itemW + 8f) * 2f, y, itemW, 32), $"Comprar Incienso ({meta.IncenseCharges}) - {MetaProgress.IncenseCost}p", enabled: meta.BankedPoints >= MetaProgress.IncenseCost))
            {
                meta.TryPurchaseIncense();
                MetaSaveService.Save(meta);
            }
            y += 42;

            var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            hintStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
            GUI.Label(new Rect(x, y, listW, 34),
                "El Mapa revela de golpe todo el piso actual (tecla M). El Perforador abre un paso permanente en la pared que tengas enfrente, si hay algo real detrás (tecla P). El Incienso reduce a la mitad el peligro de cada paso por un buen tramo (tecla N).",
                hintStyle);
            y += 44;

            GUI.Label(new Rect(x, y, listW, 20), "Ítems de combate (se usan en pelea o desde el menú de pausa):");
            y += 26;
            if (UIButton.Draw(new Rect(x, y, itemW, 32), $"Comprar Poción ({meta.PotionCharges}) - {MetaProgress.PotionCost}p", enabled: meta.BankedPoints >= MetaProgress.PotionCost))
            {
                meta.TryPurchasePotion();
                MetaSaveService.Save(meta);
            }
            if (UIButton.Draw(new Rect(x + itemW + 8f, y, itemW, 32), $"Comprar Revivir ({meta.ReviverCharges}) - {MetaProgress.ReviverCost}p", enabled: meta.BankedPoints >= MetaProgress.ReviverCost))
            {
                meta.TryPurchaseReviver();
                MetaSaveService.Save(meta);
            }
            y += 42;

            GUI.Label(new Rect(x, y, listW, 20), $"Balas para el Gunner ({MetaProgress.BulletBundleAmount} por compra, solo si tenés uno en la party):");
            y += 26;
            if (UIButton.Draw(new Rect(x, y, itemW, 32), $"Fuego (+{meta.BonusFireBullets}) - {MetaProgress.BulletBundleCost}p", enabled: meta.BankedPoints >= MetaProgress.BulletBundleCost))
            {
                meta.TryPurchaseBullets(Element.Fire);
                MetaSaveService.Save(meta);
            }
            if (UIButton.Draw(new Rect(x + itemW + 8f, y, itemW, 32), $"Hielo (+{meta.BonusIceBullets}) - {MetaProgress.BulletBundleCost}p", enabled: meta.BankedPoints >= MetaProgress.BulletBundleCost))
            {
                meta.TryPurchaseBullets(Element.Ice);
                MetaSaveService.Save(meta);
            }
            if (UIButton.Draw(new Rect(x + (itemW + 8f) * 2f, y, itemW, 32), $"Rayo (+{meta.BonusVoltBullets}) - {MetaProgress.BulletBundleCost}p", enabled: meta.BankedPoints >= MetaProgress.BulletBundleCost))
            {
                meta.TryPurchaseBullets(Element.Volt);
                MetaSaveService.Save(meta);
            }
            y += 42;

            GUI.Label(new Rect(x, y, listW, 20), "Accesorios (se equipan luego desde el menú de pausa, tecla I):");
            y += 26;
            foreach (var item in EquipmentCatalog.All)
            {
                bool owned = meta.OwnsItem(item.Id);
                GUI.Label(new Rect(x, y, listW - 190, 22), $"{item.Name} - {item.Description}");
                if (owned)
                {
                    var ownedStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                    ownedStyle.normal.textColor = new Color(0.4f, 1f, 0.5f);
                    GUI.Label(new Rect(x + listW - 180, y, 180, 22), "Ya comprado", ownedStyle);
                }
                else
                {
                    if (UIButton.Draw(new Rect(x + listW - 180, y, 180, 22), $"Comprar ({item.Cost}p)", enabled: meta.BankedPoints >= item.Cost))
                    {
                        meta.TryPurchaseItem(item.Id);
                        MetaSaveService.Save(meta);
                    }
                }
                y += 27;
            }
        }

        // Panel lateral compartido por las 2 pestanas: caja oscura con acento de color propio,
        // la silueta de la escena arriba y una frase corta abajo, como un cartel de vidriera.
        private void DrawSidePanel(Rect area, System.Action<Rect> drawArt, Color accentColor, string quote)
        {
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.06f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = accentColor;
            GUI.DrawTexture(new Rect(area.x, area.y, area.width, 4f), Texture2D.whiteTexture);
            GUI.color = old;

            float artSize = Mathf.Min(area.width * 0.8f, area.height * 0.6f);
            var artRect = new Rect(area.x + (area.width - artSize) / 2f, area.y + 20f, artSize, artSize);
            drawArt(artRect);

            var quoteStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Italic,
                wordWrap = true,
            };
            quoteStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(area.x + 12f, artRect.y + artRect.height + 16f, area.width - 24f, area.height - artRect.height - 40f),
                $"“{quote}”", quoteStyle);
        }

        private void DrawStartRunButton(float x, float w)
        {
            float btnW = 320f, btnH = 48f;
            var rect = new Rect(x + (w - btnW) / 2f, Screen.height - btnH - 16f, btnW, btnH);
            if (UIButton.Draw(rect, "COMENZAR NUEVA RUN", accentColor: new Color(1f, 0.78f, 0.2f), fontSize: 18))
                dungeonManager.StartNewRun();
        }
    }
}
