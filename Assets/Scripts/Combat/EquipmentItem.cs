using System;
using System.Collections.Generic;

namespace Combat
{
    // Que tipo de item es (determina en que slot del personaje puede ir, ver Meta.EquipmentSlot).
    // Accessory es el unico tipo que entra en CUALQUIERA de los 2 slots de accesorio.
    public enum EquipmentSlotType
    {
        Weapon,
        Chest,
        Greaves,
        Feet,
        Accessory,
    }

    // Item equipable: stats planas mas (opcional) una habilidad de arma estandarizada -- un dano
    // sostenido con chance al golpear (veneno/sangrado, mismo mecanismo, OnHitStatusName solo
    // cambia el nombre mostrado) y/o espinas pasivas (refleja dano recibido). OnHitStatus solo
    // tiene efecto si Slot == Weapon (ver Meta.MetaProgress.GetEquippedItems); Thorns y
    // Resistencia elemental se suman/combinan sin importar en que slot esten.
    public class EquipmentItem
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public int Cost;
        public EquipmentSlotType Slot = EquipmentSlotType.Accessory;

        public int AttackBonus;
        public int MagicAttackBonus;
        public int DefenseBonus;
        public int SpeedBonus;
        public int MaxHpBonus;

        // Tradeoff de peso: armadura pesada (Chest/Greaves) suele restar aca al sumar Defense/HP,
        // liviana suele sumar aca a costa de Defense -- ver catalogo mas abajo.
        public int EvasionBonus;

        // Solo tiene sentido en Slot == Weapon: si no esta vacio, restringe que clases pueden
        // equipar esta arma (ver Meta.MetaProgress.SetEquippedInstance). Vacio = cualquier clase.
        public CharacterClass[] AllowedClasses = Array.Empty<CharacterClass>();

        // Resistencia elemental (tipicamente en Chest/Greaves): reduce en este % el dano de los
        // ataques enemigos de ESTE elemento (ver CombatEngine.ExecuteEnemyAction). Se suma entre
        // todas las piezas equipadas que resistan el MISMO elemento (ver EquipmentTotals.AddResistance).
        public Element ResistElement = Element.None;
        public int ResistPercent;

        // Dano sostenido con chance al conectar un golpe (0 = este item no lo tiene). Solo se
        // aplica si esta en el slot de Arma.
        public string OnHitStatusName;
        public int OnHitStatusChancePercent;
        public int OnHitStatusDamagePercent; // % del MaxHP del enemigo, por ronda
        public int OnHitStatusRounds;

        // Espinas: pasivo, refleja este % del dano ENEMIGO recibido de vuelta al atacante. Se suma
        // entre todas las piezas equipadas que lo tengan.
        public int ThornsReflectPercent;

        // Resumen legible de los bonus de stats de este item (solo los que no son 0), para mostrar
        // en el dialogo de "encontraste un item" (ver Gameplay.DungeonManager.ShowItemFoundDialogue).
        public string DescribeStats()
        {
            var parts = new List<string>();
            if (AttackBonus != 0) parts.Add($"ATQ {(AttackBonus > 0 ? "+" : "")}{AttackBonus}");
            if (MagicAttackBonus != 0) parts.Add($"ATQ.MAG {(MagicAttackBonus > 0 ? "+" : "")}{MagicAttackBonus}");
            if (DefenseBonus != 0) parts.Add($"DEF {(DefenseBonus > 0 ? "+" : "")}{DefenseBonus}");
            if (SpeedBonus != 0) parts.Add($"VEL {(SpeedBonus > 0 ? "+" : "")}{SpeedBonus}");
            if (EvasionBonus != 0) parts.Add($"EVA {(EvasionBonus > 0 ? "+" : "")}{EvasionBonus}");
            if (MaxHpBonus != 0) parts.Add($"PV +{MaxHpBonus}");
            if (ResistElement != Element.None && ResistPercent > 0) parts.Add($"Resiste {ResistElement} {ResistPercent}%");
            if (ThornsReflectPercent > 0) parts.Add($"Espinas {ThornsReflectPercent}%");
            if (OnHitStatusChancePercent > 0) parts.Add($"{OnHitStatusChancePercent}% {OnHitStatusName} al golpear");
            return parts.Count > 0 ? string.Join(", ", parts) : "Sin bonus de stats";
        }

        public string DescribeAllowedClasses() =>
            AllowedClasses.Length > 0 ? string.Join(", ", AllowedClasses) : "Cualquier clase";
    }

    public static class EquipmentCatalog
    {
        public static readonly EquipmentItem[] All =
        {
            // --- Accesorios: el bonus mas libre, sin restriccion de clase ni de peso. ---
            new EquipmentItem
            {
                Id = "brazalete_fuerza", Name = "Brazalete de Fuerza", Slot = EquipmentSlotType.Accessory,
                Description = "Un brazalete pesado que endurece cada golpe.",
                Cost = 25, AttackBonus = 3,
            },
            new EquipmentItem
            {
                Id = "amuleto_vital", Name = "Amuleto Vital", Slot = EquipmentSlotType.Accessory,
                Description = "Un amuleto tibio que parece sumar algo de vida extra.",
                Cost = 30, MaxHpBonus = 15,
            },
            new EquipmentItem
            {
                Id = "foco_arcano", Name = "Foco Arcano", Slot = EquipmentSlotType.Accessory,
                Description = "Un cristal que amplifica el dano de las habilidades magicas.",
                Cost = 30, MagicAttackBonus = 3,
            },
            new EquipmentItem
            {
                Id = "guantes_del_explorador", Name = "Guantes del Explorador", Slot = EquipmentSlotType.Accessory,
                Description = "Livianos, pensados para moverse rapido por la mazmorra.",
                Cost = 20, SpeedBonus = 1, MaxHpBonus = 8,
            },
            new EquipmentItem
            {
                Id = "anillo_de_espinas", Name = "Anillo de Espinas", Slot = EquipmentSlotType.Accessory,
                Description = "Puas hacia afuera: el 20% de cada golpe que recibis vuelve al que lo dio.",
                Cost = 40, ThornsReflectPercent = 20,
            },

            // --- Pecho: DEF/HP a costa de Evasion (mas pesado = mas costo). ---
            new EquipmentItem
            {
                Id = "placa_reforzada", Name = "Placa Reforzada", Slot = EquipmentSlotType.Chest,
                Description = "Una placa de metal tosco, incomoda pero efectiva.",
                Cost = 25, DefenseBonus = 3, EvasionBonus = -2,
            },
            // Nota de diseno: los enemigos actuales solo atacan con Golpe o Perforacion (ver
            // EnemyFactory.AttackElement) -- ningun enemigo pega con Fuego/Hielo/Rayo todavia, asi
            // que la resistencia elemental de armadura apunta a esos 2 elementos por ahora.
            new EquipmentItem
            {
                Id = "tunica_ignifuga", Name = "Coraza Acolchada", Slot = EquipmentSlotType.Chest,
                Description = "Relleno grueso que amortigua los golpes contundentes antes de que lleguen al hueso.",
                Cost = 35, DefenseBonus = 1, ResistElement = Element.Strike, ResistPercent = 40,
            },
            new EquipmentItem
            {
                Id = "manto_glacial", Name = "Cota de Malla", Slot = EquipmentSlotType.Chest,
                Description = "Anillos de metal trenzados: una punta que entra pierde casi toda su fuerza.",
                Cost = 35, DefenseBonus = 1, ResistElement = Element.Pierce, ResistPercent = 40,
            },

            // --- Grebas: mismo tradeoff que el pecho, pesadas o livianas. ---
            new EquipmentItem
            {
                Id = "grebas_de_hierro", Name = "Grebas de Hierro", Slot = EquipmentSlotType.Greaves,
                Description = "Pesadas, pero paran un golpe que de otra forma pasaria de largo.",
                Cost = 25, DefenseBonus = 2, EvasionBonus = -2,
            },
            new EquipmentItem
            {
                Id = "grebas_del_viento", Name = "Grebas del Viento", Slot = EquipmentSlotType.Greaves,
                Description = "Casi no pesan: pensadas para esquivar antes que para tapear.",
                Cost = 25, SpeedBonus = 1, EvasionBonus = 3, DefenseBonus = -1,
            },
            new EquipmentItem
            {
                Id = "grebas_aislantes", Name = "Grebas Acolchadas", Slot = EquipmentSlotType.Greaves,
                Description = "Relleno grueso en las canilleras: absorbe golpes contundentes en las piernas.",
                Cost = 30, ResistElement = Element.Strike, ResistPercent = 30,
            },

            // --- Pie: livianas por naturaleza, Speed/Evasion. ---
            new EquipmentItem
            {
                Id = "botas_veloces", Name = "Botas Veloces", Slot = EquipmentSlotType.Feet,
                Description = "Suelas livianas pensadas para esquivar antes que nadie.",
                Cost = 20, SpeedBonus = 2,
            },
            new EquipmentItem
            {
                Id = "botas_de_plomo", Name = "Botas de Plomo", Slot = EquipmentSlotType.Feet,
                Description = "Increiblemente pesadas. Cuestan velocidad, pero anclan cada golpe.",
                Cost = 20, AttackBonus = 2, SpeedBonus = -1,
            },

            // --- Armas: con habilidad (ver CombatEngine.ApplyDamageToEnemy) y, algunas,
            // restringidas por clase (ver Meta.MetaProgress.SetEquippedItem) -- se encuentran sobre
            // todo en cofres (Gameplay/DungeonManager.RollTreasureLoot), tambien compra directa aca.
            new EquipmentItem
            {
                Id = "daga_venenosa", Name = "Daga Venenosa", Slot = EquipmentSlotType.Weapon,
                Description = "El filo esta tratado con savia podrida. 30% de envenenar al golpear. Solo Ranger o Warrior.",
                Cost = 45, AttackBonus = 1,
                AllowedClasses = new[] { CharacterClass.Ranger, CharacterClass.Warrior },
                OnHitStatusName = "Veneno", OnHitStatusChancePercent = 30, OnHitStatusDamagePercent = 6, OnHitStatusRounds = 3,
            },
            new EquipmentItem
            {
                Id = "hacha_desgarradora", Name = "Hacha Desgarradora", Slot = EquipmentSlotType.Weapon,
                Description = "Un filo mellado a proposito. 25% de abrir una herida que sangra al golpear. Solo Warrior o Berserker.",
                Cost = 45, AttackBonus = 2,
                AllowedClasses = new[] { CharacterClass.Warrior, CharacterClass.Berserker },
                OnHitStatusName = "Sangrado", OnHitStatusChancePercent = 25, OnHitStatusDamagePercent = 8, OnHitStatusRounds = 2,
            },
            new EquipmentItem
            {
                Id = "baston_de_chispas", Name = "Baston de Chispas", Slot = EquipmentSlotType.Weapon,
                Description = "Conduce mana en vez de fuerza bruta. Solo Mage o Alchemist.",
                Cost = 40, MagicAttackBonus = 3,
                AllowedClasses = new[] { CharacterClass.Mage, CharacterClass.Alchemist },
            },
        };

        public static EquipmentItem Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var item in All)
                if (item.Id == id) return item;
            return null;
        }
    }

    // Suma de todos los items equipados de un personaje (ver Meta.MetaProgress.GetEquippedItems),
    // ya resuelta a numeros planos -- un unico lugar donde vive esta cuenta, usado tanto al armar
    // la party (Meta.MetaProgress.ApplyUpgradesToParty) como al re-equipar en vivo en el menu de
    // pausa (Gameplay.CombatManager.SetEquippedItemLive) y al mostrar el resumen en la UI.
    public struct EquipmentTotals
    {
        public int Attack, MagicAttack, Defense, Speed, Evasion, MaxHp, Thorns;
        public List<(Element Element, int Percent)> Resistances;
        // El arma equipada con proc on-hit, si alguna (ver el comentario en OnHitStatus arriba:
        // solo el arma lo define, nunca se combina con otro slot).
        public EquipmentItem OnHitWeapon;

        private const int ResistPercentCap = 75;

        public static EquipmentTotals From(IEnumerable<EquipmentItem> items)
        {
            var totals = new EquipmentTotals { Resistances = new List<(Element, int)>() };
            foreach (var item in items)
            {
                totals.Attack += item.AttackBonus;
                totals.MagicAttack += item.MagicAttackBonus;
                totals.Defense += item.DefenseBonus;
                totals.Speed += item.SpeedBonus;
                totals.Evasion += item.EvasionBonus;
                totals.MaxHp += item.MaxHpBonus;
                totals.Thorns += item.ThornsReflectPercent;
                if (item.Slot == EquipmentSlotType.Weapon && item.OnHitStatusChancePercent > 0)
                    totals.OnHitWeapon = item;
                totals.AddResistance(item.ResistElement, item.ResistPercent);
            }
            return totals;
        }

        private void AddResistance(Element element, int percent)
        {
            if (element == Element.None || percent <= 0) return;
            for (int i = 0; i < Resistances.Count; i++)
            {
                if (Resistances[i].Element != element) continue;
                Resistances[i] = (element, Math.Min(ResistPercentCap, Resistances[i].Percent + percent));
                return;
            }
            Resistances.Add((element, Math.Min(ResistPercentCap, percent)));
        }
    }
}
