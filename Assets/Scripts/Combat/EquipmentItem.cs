namespace Combat
{
    // Accesorio simple: un bonus fijo a una sola stat. Sin slots de arma/armadura todavia (fuera
    // de alcance por ahora) -- cada personaje tiene UN accesorio equipable como mucho.
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
