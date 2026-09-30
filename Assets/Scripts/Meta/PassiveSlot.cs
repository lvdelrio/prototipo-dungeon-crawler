using System;
using Combat;

namespace Meta
{
    // Que pasiva (si alguna) tiene puesta cada clase en cada uno de sus PassiveCatalog.
    // SlotsPerCharacter slots, persistido entre runs -- mismo patron que Meta.EquipmentSlot, pero
    // sin InstanceId: una pasiva no es un objeto fisico, PassiveId apunta directo al catalogo
    // (Combat.PassiveCatalog.Find).
    [Serializable]
    public class PassiveSlot
    {
        public CharacterClass Class;
        public int SlotIndex;
        public string PassiveId = "";
    }
}
