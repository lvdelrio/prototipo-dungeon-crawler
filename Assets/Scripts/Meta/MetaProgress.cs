using System;
using System.Collections.Generic;
using Combat;

namespace Meta
{
    // Progreso permanente entre runs (roguelite): puntos acumulados, items comprados para la
    // proxima run, y niveles de mejora comprados para cada clase de personaje. Se guarda en disco
    // tal cual (ver Gameplay/MetaSaveService).
    [Serializable]
    public class MetaProgress
    {
        public int BankedPoints;
        public int MapCharges;
        public int DrillCharges;
        public List<CharacterUpgrade> Upgrades = new List<CharacterUpgrade>();

        public const int PointsPerFloorReached = 15;
        public const int PointsPerEnemyDefeated = 5;
        public const int PointsPerBossDefeated = 50;

        public const int UpgradeBaseCost = 20;
        public const int UpgradeMaxLevel = 5;
        public const int MapCost = 30;
        public const int DrillCost = 40;

        private const int PerLevelAttack = 1;
        private const int PerLevelDefense = 1;
        private const int PerLevelSpeed = 1;
        private const int PerLevelMaxHp = 8;
        private const int PerLevelMaxTp = 4;

        public static int ComputeRunPoints(int deepestFloorIndexReached, int enemiesDefeated, int bossesDefeated)
        {
            int floorsReached = Math.Max(0, deepestFloorIndexReached) + 1;
            return floorsReached * PointsPerFloorReached
                 + enemiesDefeated * PointsPerEnemyDefeated
                 + bossesDefeated * PointsPerBossDefeated;
        }

        public int AddRunRewards(int deepestFloorIndexReached, int enemiesDefeated, int bossesDefeated)
        {
            int earned = ComputeRunPoints(deepestFloorIndexReached, enemiesDefeated, bossesDefeated);
            BankedPoints += earned;
            return earned;
        }

        private CharacterUpgrade FindUpgrade(CharacterClass cls) => Upgrades.Find(u => u.Class == cls);

        private CharacterUpgrade GetOrCreateUpgrade(CharacterClass cls)
        {
            var existing = FindUpgrade(cls);
            if (existing != null) return existing;
            var created = new CharacterUpgrade { Class = cls };
            Upgrades.Add(created);
            return created;
        }

        public int GetUpgradeLevel(CharacterClass cls, UpgradeStat stat) => FindUpgrade(cls)?.GetLevel(stat) ?? 0;

        // Costo del PROXIMO nivel (el (nivel actual + 1)-esimo). -1 si ya esta al maximo.
        public int GetUpgradeCost(CharacterClass cls, UpgradeStat stat)
        {
            int level = GetUpgradeLevel(cls, stat);
            if (level >= UpgradeMaxLevel) return -1;
            return UpgradeBaseCost * (level + 1);
        }

        public bool TryPurchaseUpgrade(CharacterClass cls, UpgradeStat stat)
        {
            int cost = GetUpgradeCost(cls, stat);
            if (cost < 0 || BankedPoints < cost) return false;
            BankedPoints -= cost;
            GetOrCreateUpgrade(cls).AddLevel(stat);
            return true;
        }

        public bool TryPurchaseMap()
        {
            if (BankedPoints < MapCost) return false;
            BankedPoints -= MapCost;
            MapCharges++;
            return true;
        }

        public bool TryPurchaseDrill()
        {
            if (BankedPoints < DrillCost) return false;
            BankedPoints -= DrillCost;
            DrillCharges++;
            return true;
        }

        // Aplica los niveles comprados como bonus fijos sobre las stats BASE de una party recien
        // creada (PartyFactory.CreateDefaultParty), y cura HP/TP al maximo resultante.
        public void ApplyUpgradesToParty(List<CharacterStats> party)
        {
            foreach (var character in party)
            {
                var upgrade = FindUpgrade(character.Class);
                if (upgrade == null) continue;

                character.Attack += upgrade.AttackLevel * PerLevelAttack;
                character.Defense += upgrade.DefenseLevel * PerLevelDefense;
                character.Speed += upgrade.SpeedLevel * PerLevelSpeed;
                character.MaxHP += upgrade.MaxHpLevel * PerLevelMaxHp;
                character.MaxTP += upgrade.MaxTpLevel * PerLevelMaxTp;
                character.HP = character.MaxHP;
                character.TP = character.MaxTP;
            }
        }
    }
}
