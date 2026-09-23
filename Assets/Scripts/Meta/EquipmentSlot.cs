using System;
using Combat;

namespace Meta
{
    // Una copia FISICA concreta de un item del catalogo (Combat/EquipmentItem.cs), con su propia
    // identidad (InstanceId) -- tener 2 de "Placa Reforzada" son 2 EquipmentInstance con el mismo
    // ItemId pero InstanceId distinto, cada una equipable por separado. MetaProgress.Inventory es
    // la lista de TODAS las que el jugador posee; ver MetaProgress.SetEquippedInstance para la
    // regla de que una misma instancia no puede estar puesta en 2 slots a la vez.
    [Serializable]
    public class EquipmentInstance
    {
        public string InstanceId = "";
        public string ItemId = "";
    }

    // Que INSTANCIA (si alguna) tiene puesto cada clase en cada slot, persistido entre runs. Slot
    // es el tipo de hueco (Weapon/Chest/Greaves/Feet/Accessory); AccessoryIndex (0 o 1) solo
    // distingue ENTRE los 2 huecos de accesorio cuando Slot == Accessory, se ignora en el resto.
    [Serializable]
    public class EquipmentSlot
    {
        public CharacterClass Class;
        public EquipmentSlotType Slot;
        public int AccessoryIndex;
        public string InstanceId = "";
    }
}
