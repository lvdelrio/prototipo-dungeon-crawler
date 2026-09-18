using System;
using Combat;

namespace Meta
{
    // Que accesorio (si alguno) tiene equipado cada clase, persistido entre runs.
    [Serializable]
    public class EquipmentSlot
    {
        public CharacterClass Class;
        public string ItemId = "";
    }
}
