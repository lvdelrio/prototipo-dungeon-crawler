using System;
using System.Collections.Generic;
using System.Linq;
using Combat;

namespace Meta
{
    // Progreso permanente entre runs (roguelite): puntos acumulados, items comprados para la
    // proxima run, y niveles de mejora comprados para cada clase de personaje. Se guarda en disco
    // tal cual (ver Gameplay/MetaSaveService en el proyecto de Unity).
    [Serializable]
    public class MetaProgress
    {
        public int BankedPoints;
        public int MapCharges;
        public int DrillCharges;

        // Se pone en true la PRIMERA vez que el jugador abre CUALQUIER cofre (ver
        // Gameplay.DungeonManager.RollTreasureLoot): ese cofre da, ademas de lo que le toco en el
        // sorteo normal, una carga de Perforador de regalo -- para que nunca dependa de la suerte
        // si en tu primera run efectivamente vas a poder seguir explorando mas alla de un camino
        // cerrado. Se conserva entre runs de la misma partida (solo pasa una vez en total, no una
        // vez por run).
        public bool FirstChestBonusGiven;

        // Carga de Incienso: al usarse (ver DungeonManager.TryUseIncense) reduce a la mitad el
        // peligro que acumulan los pasos durante un tramo de exploracion, asi tardas mas en
        // toparte con un encuentro -- util para cruzar rapido un piso sin pelear tanto.
        public int IncenseCharges;

        // Items de combate (ver Combat.ActionType.Item / Gameplay.CombatManager.SubmitItemAction):
        // Pocion cura un flat de HP, Revivir devuelve a un caido. Usables tanto en combate como
        // desde el menu de pausa (ver PauseMenuHUD.DrawSkillActorPicker / CombatManager.
        // UsePotionOutOfCombat).
        public int PotionCharges;
        public int ReviverCharges;

        // Balas elementales extra para el Gunner (ver Combat.CharacterStats.FireBullets/etc.):
        // se SUMAN a las 5 de cada tipo con las que ya arranca (ver PartyFactory), aplicadas en
        // ApplyUpgradesToParty. No hacen nada si la party actual no tiene un Gunner.
        public int BonusFireBullets;
        public int BonusIceBullets;
        public int BonusVoltBullets;

        public List<CharacterUpgrade> Upgrades = new List<CharacterUpgrade>();

        // Las 6 clases elegidas en la pantalla de creacion de party de la partida actual (ver
        // Gameplay/PartyCreationHUD). Vacia = todavia no se eligio nunca (guardado viejo, o recien
        // "Nueva Partida" sin terminar) -- en ese caso CombatManager.InitializeParty cae de vuelta a
        // PartyFactory.DefaultClasses. Se conserva entre runs de la MISMA partida (no se pide de
        // nuevo cada vez que se pierde/gana, solo al arrancar una partida realmente nueva).
        public List<CharacterClass> PartyClasses = new List<CharacterClass>();

        // Inventario: instancias fisicas concretas de items del catalogo (Combat/EquipmentItem.cs)
        // que el jugador posee -- comprar/encontrar 2 veces el mismo item del catalogo da 2
        // EquipmentInstance distintas, cada una equipable por separado. EquippedItems dice que
        // INSTANCIA (no que catalogo-id) tiene puesta cada clase en cada uno de sus 6 slots
        // (Arma/Pecho/Grebas/Pie/Accesorio 1/Accesorio 2); una misma instancia nunca puede estar en
        // 2 slots a la vez (ver SetEquippedInstance) -- es una unica cosa fisica.
        public List<EquipmentInstance> Inventory = new List<EquipmentInstance>();
        public List<EquipmentSlot> EquippedItems = new List<EquipmentSlot>();

        // Codex: ids de Lore.LoreCatalog ya descubiertos explorando la mazmorra (ver
        // DungeonGen.CellType.Lore); tambien es lo que exige ShortcutGate.RequiredLoreId.
        public List<string> UnlockedLoreIds = new List<string>();

        public const int PointsPerFloorReached = 15;
        public const int PointsPerEnemyDefeated = 5;
        public const int PointsPerBossDefeated = 50;

        public const int UpgradeBaseCost = 20;
        public const int UpgradeMaxLevel = 5;
        public const int MapCost = 30;
        public const int DrillCost = 40;
        public const int IncenseCost = 35;
        public const int PotionCost = 20;
        public const int ReviverCost = 60;
        public const int BulletBundleCost = 15; // +3 balas de UN elemento a elegir, ver TryPurchaseBullets
        public const int BulletBundleAmount = 3;

        private const int PerLevelAttack = 1;
        private const int PerLevelDefense = 1;
        private const int PerLevelSpeed = 1;
        private const int PerLevelMaxHp = 8;
        private const int PerLevelMaxTp = 4;
        private const int PerLevelMagicAttack = 1;
        // Evasion/Luck son porcentajes (0-100): subir de a 1 por nivel para que 5 niveles (el tope,
        // UpgradeMaxLevel) sea un +5% notorio pero nunca desequilibrante por si solo.
        private const int PerLevelEvasion = 1;
        private const int PerLevelLuck = 1;

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

        public bool TryPurchaseIncense()
        {
            if (BankedPoints < IncenseCost) return false;
            BankedPoints -= IncenseCost;
            IncenseCharges++;
            return true;
        }

        public bool TryPurchasePotion()
        {
            if (BankedPoints < PotionCost) return false;
            BankedPoints -= PotionCost;
            PotionCharges++;
            return true;
        }

        public bool TryPurchaseReviver()
        {
            if (BankedPoints < ReviverCost) return false;
            BankedPoints -= ReviverCost;
            ReviverCharges++;
            return true;
        }

        // element: cual de las 3 bolsas de balas comprar (Fire/Ice/Volt) -- cualquier otro valor no hace nada.
        public bool TryPurchaseBullets(Element element)
        {
            if (BankedPoints < BulletBundleCost) return false;
            if (element != Element.Fire && element != Element.Ice && element != Element.Volt) return false;

            BankedPoints -= BulletBundleCost;
            if (element == Element.Fire) BonusFireBullets += BulletBundleAmount;
            else if (element == Element.Ice) BonusIceBullets += BulletBundleAmount;
            else BonusVoltBullets += BulletBundleAmount;
            return true;
        }

        // Cuantas instancias de este item del catalogo se poseen en total (equipadas o no).
        public int CountOwned(string itemId) => string.IsNullOrEmpty(itemId) ? 0 : Inventory.Count(i => i.ItemId == itemId);
        public bool OwnsItem(string itemId) => CountOwned(itemId) > 0;

        // true si ESTA instancia fisica en particular esta puesta en algun slot de algun personaje
        // ahora mismo -- una instancia equipada no puede volver a elegirse hasta desequiparla.
        public bool IsInstanceEquipped(string instanceId) =>
            !string.IsNullOrEmpty(instanceId) && EquippedItems.Any(e => e.InstanceId == instanceId);

        // Crea una instancia NUEVA de este item del catalogo (id invalido -> no hace nada, devuelve
        // null) y la agrega al inventario. Comprar/encontrar el mismo item 2 veces da 2 instancias
        // independientes -- cada una se puede equipar en un personaje distinto.
        public EquipmentInstance AddToInventory(string itemId)
        {
            if (EquipmentCatalog.Find(itemId) == null) return null;
            var instance = new EquipmentInstance { InstanceId = Guid.NewGuid().ToString("N"), ItemId = itemId };
            Inventory.Add(instance);
            return instance;
        }

        public bool TryPurchaseItem(string itemId)
        {
            var item = EquipmentCatalog.Find(itemId);
            if (item == null || BankedPoints < item.Cost) return false;
            BankedPoints -= item.Cost;
            AddToInventory(itemId);
            return true;
        }

        public string GetEquippedInstanceId(CharacterClass cls, EquipmentSlotType slot, int accessoryIndex = 0) =>
            EquippedItems.Find(e => e.Class == cls && e.Slot == slot && (slot != EquipmentSlotType.Accessory || e.AccessoryIndex == accessoryIndex))?.InstanceId;

        // El EquipmentItem (catalogo) resuelto a partir de la instancia puesta en este slot, o null
        // si esta vacio -- para mostrar nombre/stats en la UI sin que le importe el instanceId.
        public EquipmentItem GetEquippedItem(CharacterClass cls, EquipmentSlotType slot, int accessoryIndex = 0)
        {
            string instanceId = GetEquippedInstanceId(cls, slot, accessoryIndex);
            var instance = string.IsNullOrEmpty(instanceId) ? null : Inventory.Find(i => i.InstanceId == instanceId);
            return instance != null ? EquipmentCatalog.Find(instance.ItemId) : null;
        }

        // instanceId vacio/null desequipa. No hace nada (falla en silencio: la UI no deberia dejar
        // llegar a estos casos, pero por las dudas no corrompe nada) si: la instancia no se posee,
        // ya esta puesta en OTRO slot (una misma instancia fisica no puede estar en 2 lados a la
        // vez -- hay que desequiparla de ahi primero), el item no es del tipo que corresponde a
        // este slot (un Chest no entra en Weapon), o es un arma con clases permitidas y cls no esta
        // entre ellas.
        public bool SetEquippedInstance(CharacterClass cls, EquipmentSlotType slot, string instanceId, int accessoryIndex = 0)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                var instance = Inventory.Find(i => i.InstanceId == instanceId);
                if (instance == null) return false;
                var item = EquipmentCatalog.Find(instance.ItemId);
                if (item == null || item.Slot != slot) return false;
                if (item.AllowedClasses.Length > 0 && Array.IndexOf(item.AllowedClasses, cls) < 0) return false;

                bool equippedInThisSameSlot = EquippedItems.Any(e => e.InstanceId == instanceId
                    && e.Class == cls && e.Slot == slot && (slot != EquipmentSlotType.Accessory || e.AccessoryIndex == accessoryIndex));
                if (IsInstanceEquipped(instanceId) && !equippedInThisSameSlot) return false;
            }

            var existing = EquippedItems.Find(e => e.Class == cls && e.Slot == slot && (slot != EquipmentSlotType.Accessory || e.AccessoryIndex == accessoryIndex));
            if (existing == null)
            {
                existing = new EquipmentSlot { Class = cls, Slot = slot, AccessoryIndex = accessoryIndex };
                EquippedItems.Add(existing);
            }
            existing.InstanceId = instanceId ?? "";
            return true;
        }

        // Los hasta 6 items equipados por esta clase (Arma/Pecho/Grebas/Pie/Accesorio 1/Accesorio 2),
        // resueltos a su EquipmentItem y sin nulls -- para sumar sus bonuses (ver ApplyUpgradesToParty
        // y Gameplay.CombatManager.SetEquippedItemLive).
        public List<EquipmentItem> GetEquippedItems(CharacterClass cls)
        {
            var result = new List<EquipmentItem>();
            void AddIfAny(EquipmentSlotType slot, int accessoryIndex = 0)
            {
                var item = GetEquippedItem(cls, slot, accessoryIndex);
                if (item != null) result.Add(item);
            }
            AddIfAny(EquipmentSlotType.Weapon);
            AddIfAny(EquipmentSlotType.Chest);
            AddIfAny(EquipmentSlotType.Greaves);
            AddIfAny(EquipmentSlotType.Feet);
            AddIfAny(EquipmentSlotType.Accessory, 0);
            AddIfAny(EquipmentSlotType.Accessory, 1);
            return result;
        }

        public bool IsLoreUnlocked(string loreId) => !string.IsNullOrEmpty(loreId) && UnlockedLoreIds.Contains(loreId);

        // Devuelve true solo la PRIMERA vez que se desbloquea este id (para poder mostrar un
        // aviso de "nuevo" solo una vez); false si ya estaba desbloqueado antes.
        public bool UnlockLore(string loreId)
        {
            if (string.IsNullOrEmpty(loreId) || UnlockedLoreIds.Contains(loreId)) return false;
            UnlockedLoreIds.Add(loreId);
            return true;
        }

        // Aplica los niveles comprados Y los items equipados en los 6 slots (ver GetEquippedItems)
        // como bonus fijos sobre las stats BASE de una party recien creada
        // (PartyFactory.CreateDefaultParty), y cura HP/TP al maximo resultante.
        public void ApplyUpgradesToParty(List<CharacterStats> party)
        {
            foreach (var character in party)
            {
                var upgrade = FindUpgrade(character.Class);
                if (upgrade != null)
                {
                    character.Attack += upgrade.AttackLevel * PerLevelAttack;
                    character.Defense += upgrade.DefenseLevel * PerLevelDefense;
                    character.Speed += upgrade.SpeedLevel * PerLevelSpeed;
                    character.MaxHP += upgrade.MaxHpLevel * PerLevelMaxHp;
                    character.MaxTP += upgrade.MaxTpLevel * PerLevelMaxTp;
                    character.MagicAttack += upgrade.MagicAttackLevel * PerLevelMagicAttack;
                    character.Evasion += upgrade.EvasionLevel * PerLevelEvasion;
                    character.Luck += upgrade.LuckLevel * PerLevelLuck;
                }

                var totals = EquipmentTotals.From(GetEquippedItems(character.Class));
                character.Attack += totals.Attack;
                character.MagicAttack += totals.MagicAttack;
                character.Defense += totals.Defense;
                character.Speed += totals.Speed;
                character.MaxHP += totals.MaxHp;
                character.Evasion += totals.Evasion;
                character.ThornsReflectPercent += totals.Thorns;
                character.Resistances = totals.Resistances;
                character.OnHitStatusName = totals.OnHitWeapon?.OnHitStatusName;
                character.OnHitStatusChancePercent = totals.OnHitWeapon?.OnHitStatusChancePercent ?? 0;
                character.OnHitStatusDamagePercent = totals.OnHitWeapon?.OnHitStatusDamagePercent ?? 0;
                character.OnHitStatusRounds = totals.OnHitWeapon?.OnHitStatusRounds ?? 0;

                // Balas extra compradas (ver TryPurchaseBullets): no hacen nada si este personaje
                // no es Gunner (los demas ni miran estos campos), asi que sumarlas siempre es seguro.
                character.FireBullets += BonusFireBullets;
                character.IceBullets += BonusIceBullets;
                character.VoltBullets += BonusVoltBullets;

                character.HP = character.MaxHP;
                character.TP = character.MaxTP;
            }
        }
    }
}
