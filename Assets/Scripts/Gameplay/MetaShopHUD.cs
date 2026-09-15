using UnityEngine;
using Meta;

namespace Gameplay
{
    // Pantalla que aparece cuando la party muere y la run termina: deja gastar los puntos
    // ganados (piso alcanzado + enemigos/jefes derrotados) en mejoras permanentes de personajes
    // y en items (Mapa, Perforador) para la proxima run. No se puede cerrar sin elegir "Comenzar
    // nueva run": mientras esta abierta, el jugador no puede moverse (ver GridPlayerController).
    public class MetaShopHUD : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public CombatManager combatManager;

        private static readonly UpgradeStat[] Stats =
            { UpgradeStat.Attack, UpgradeStat.Defense, UpgradeStat.Speed, UpgradeStat.MaxHp, UpgradeStat.MaxTp };
        private static readonly string[] StatLabels = { "ATQ", "DEF", "VEL", "HP", "TP" };

        void OnGUI()
        {
            if (dungeonManager == null || combatManager == null || !dungeonManager.IsGameOverShopActive) return;
            var meta = dungeonManager.Meta;
            if (meta == null) return;

            const int panelX = 40, panelY = 30, panelW = 960, panelH = 640;
            GUI.Box(new Rect(panelX, panelY, panelW, panelH), "");

            float y = panelY + 10;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 26), "RUN TERMINADA");
            y += 30;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 22),
                $"Puntos ganados esta run: {dungeonManager.LastRunPointsEarned}  |  Puntos disponibles: {meta.BankedPoints}");
            y += 34;

            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Mejoras permanentes de personajes:");
            y += 24;

            float col0 = panelX + 20, colW = 165;
            for (int s = 0; s < Stats.Length; s++)
                GUI.Label(new Rect(col0 + 120 + s * colW, y, colW, 18), StatLabels[s]);
            y += 20;

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
                        GUI.enabled = meta.BankedPoints >= cost;
                        if (GUI.Button(new Rect(bx, y, colW - 10, 24), $"Lv {level}->{level + 1} ({cost}p)"))
                        {
                            meta.TryPurchaseUpgrade(character.Class, stat);
                            MetaSaveService.Save(meta);
                        }
                        GUI.enabled = true;
                    }
                }
                y += 28;
            }

            y += 16;
            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 20), "Tienda de items para la proxima run:");
            y += 26;

            GUI.enabled = meta.BankedPoints >= MetaProgress.MapCost;
            if (GUI.Button(new Rect(panelX + 20, y, 280, 30), $"Comprar Mapa ({meta.MapCharges} en inventario) - {MetaProgress.MapCost}p"))
            {
                meta.TryPurchaseMap();
                MetaSaveService.Save(meta);
            }
            GUI.enabled = true;

            GUI.enabled = meta.BankedPoints >= MetaProgress.DrillCost;
            if (GUI.Button(new Rect(panelX + 320, y, 320, 30), $"Comprar Perforador ({meta.DrillCharges} en inventario) - {MetaProgress.DrillCost}p"))
            {
                meta.TryPurchaseDrill();
                MetaSaveService.Save(meta);
            }
            GUI.enabled = true;
            y += 42;

            GUI.Label(new Rect(panelX + 20, y, panelW - 40, 40),
                "El Mapa revela de golpe todo el piso actual (tecla M). El Perforador abre un paso permanente en la pared que tengas enfrente, si hay algo real detras (tecla P).");
            y += 56;

            if (GUI.Button(new Rect(panelX + 20, y, 240, 38), "Comenzar nueva run"))
            {
                dungeonManager.StartNewRun();
            }
        }
    }
}
