using System;
using System.Collections.Generic;

namespace Combat
{
    public static class EnemyFactory
    {
        public static EnemyStats CreateWolf(int suffix)
        {
            return new EnemyStats
            {
                Name = $"Lobo Colmillo {suffix}", MaxHP = 40, HP = 40,
                Attack = 9, Defense = 3, Speed = 6,
                AttackElement = Element.Strike,
                Weakness = Element.Fire, Resistance = Element.Ice,
            };
        }

        public static EnemyStats CreateBeetle(int suffix)
        {
            return new EnemyStats
            {
                Name = $"Escarabajo Coraza {suffix}", MaxHP = 55, HP = 55,
                Attack = 7, Defense = 6, Speed = 3,
                AttackElement = Element.Strike,
                Weakness = Element.Strike, Resistance = Element.Pierce,
            };
        }

        public static EnemyStats CreateBoss()
        {
            return new EnemyStats
            {
                Name = "Guardian de Piedra", MaxHP = 220, HP = 220,
                Attack = 14, Defense = 8, Speed = 4,
                AttackElement = Element.Strike,
                Weakness = Element.Volt, Resistance = Element.Strike,
            };
        }

        // Grupo aleatorio de 1 a 3 enemigos normales (con repeticion) para un encuentro comun.
        public static List<EnemyStats> CreateRandomEncounter(Random rng)
        {
            int count = rng.Next(1, 4);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                if (rng.Next(2) == 0) list.Add(CreateWolf(i + 1));
                else list.Add(CreateBeetle(i + 1));
            }
            return list;
        }
    }
}
