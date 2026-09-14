using System;
using System.Collections.Generic;

namespace DungeonGen
{
    [Serializable]
    public class EventEntry
    {
        public string Name = "";
        public bool IsLucky;
        public int Weight = 1;
        public string Description = "";
    }

    public static class EventTable
    {
        public static readonly List<EventEntry> Entries = new List<EventEntry>
        {
            new EventEntry { Name = "Fuente curativa", IsLucky = true, Weight = 10, Description = "Recuperas HP/TP." },
            new EventEntry { Name = "Cofre de monedas", IsLucky = true, Weight = 10, Description = "Ganas oro." },
            new EventEntry { Name = "Objeto raro", IsLucky = true, Weight = 5, Description = "Encuentras un objeto valioso." },
            new EventEntry { Name = "Bendicion temporal", IsLucky = true, Weight = 6, Description = "Buff temporal de stats." },
            new EventEntry { Name = "Trampa de pinchos", IsLucky = false, Weight = 10, Description = "Pierdes HP." },
            new EventEntry { Name = "Emboscada", IsLucky = false, Weight = 8, Description = "Combate forzoso contra un enemigo." },
            new EventEntry { Name = "Robo", IsLucky = false, Weight = 6, Description = "Pierdes oro." },
            new EventEntry { Name = "Maldicion temporal", IsLucky = false, Weight = 5, Description = "Debuff temporal de stats." },
        };

        public static EventEntry Roll(Random rng)
        {
            int total = 0;
            foreach (var e in Entries) total += e.Weight;
            int roll = rng.Next(total);
            int acc = 0;
            foreach (var e in Entries)
            {
                acc += e.Weight;
                if (roll < acc) return e;
            }
            return Entries[Entries.Count - 1];
        }
    }
}
