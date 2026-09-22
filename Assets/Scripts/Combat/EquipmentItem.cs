namespace Combat
{
    // Accesorio simple: un bonus fijo a una sola stat, mas (opcional) una habilidad de arma
    // estandarizada -- un dano sostenido con chance al golpear (veneno/sangrado, mismo mecanismo,
    // OnHitStatusName solo cambia el nombre mostrado) y/o espinas pasivas (refleja dano recibido).
    // Sin slots de arma/armadura separados todavia (fuera de alcance) -- sigue siendo UN
    // accesorio equipable por personaje, ahora con mas variedad de lo que puede hacer.
    public class EquipmentItem
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public int Cost;
        public int AttackBonus;
        public int DefenseBonus;
        public int SpeedBonus;
        public int MaxHpBonus;

        // Dano sostenido con chance al conectar un golpe (0 = este item no lo tiene).
        public string OnHitStatusName;
        public int OnHitStatusChancePercent;
        public int OnHitStatusDamagePercent; // % del MaxHP del enemigo, por ronda
        public int OnHitStatusRounds;

        // Espinas: pasivo, refleja este % del dano ENEMIGO recibido de vuelta al atacante.
        public int ThornsReflectPercent;
    }

    public static class EquipmentCatalog
    {
        public static readonly EquipmentItem[] All =
        {
            new EquipmentItem
            {
                Id = "brazalete_fuerza", Name = "Brazalete de Fuerza",
                Description = "Un brazalete pesado que endurece cada golpe.",
                Cost = 25, AttackBonus = 3,
            },
            new EquipmentItem
            {
                Id = "placa_reforzada", Name = "Placa Reforzada",
                Description = "Una placa de metal tosco, incomoda pero efectiva.",
                Cost = 25, DefenseBonus = 3,
            },
            new EquipmentItem
            {
                Id = "botas_veloces", Name = "Botas Veloces",
                Description = "Suelas livianas pensadas para esquivar antes que nadie.",
                Cost = 20, SpeedBonus = 2,
            },
            new EquipmentItem
            {
                Id = "amuleto_vital", Name = "Amuleto Vital",
                Description = "Un amuleto tibio que parece sumar algo de vida extra.",
                Cost = 30, MaxHpBonus = 15,
            },

            // Armas con habilidad (ver CombatEngine.ApplyDamageToEnemy) -- se encuentran sobre
            // todo en cofres (Gameplay/DungeonManager.RollTreasureLoot), tambien compra directa aca.
            new EquipmentItem
            {
                Id = "daga_venenosa", Name = "Daga Venenosa",
                Description = "El filo esta tratado con savia podrida. 30% de envenenar al golpear.",
                Cost = 45, AttackBonus = 1,
                OnHitStatusName = "Veneno", OnHitStatusChancePercent = 30, OnHitStatusDamagePercent = 6, OnHitStatusRounds = 3,
            },
            new EquipmentItem
            {
                Id = "hacha_desgarradora", Name = "Hacha Desgarradora",
                Description = "Un filo mellado a proposito. 25% de abrir una herida que sangra al golpear.",
                Cost = 45, AttackBonus = 2,
                OnHitStatusName = "Sangrado", OnHitStatusChancePercent = 25, OnHitStatusDamagePercent = 8, OnHitStatusRounds = 2,
            },
            new EquipmentItem
            {
                Id = "escudo_espinas", Name = "Escudo de Espinas",
                Description = "Puas hacia afuera: el 20% de cada golpe que recibis vuelve al que lo dio.",
                Cost = 40, DefenseBonus = 1, ThornsReflectPercent = 20,
            },

            // Herramienta (ver Gameplay/DungeonManager.RollTreasureLoot): utilitaria, sin dano
            // ofensivo -- vale por lo que facilita, no por lo que rompe.
            new EquipmentItem
            {
                Id = "guantes_del_explorador", Name = "Guantes del Explorador",
                Description = "Livianos, pensados para moverse rapido por la mazmorra.",
                Cost = 20, SpeedBonus = 1, MaxHpBonus = 8,
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
}
