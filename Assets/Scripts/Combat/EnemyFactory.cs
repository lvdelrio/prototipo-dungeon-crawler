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

        // Slime: al morir por primera vez se divide en 2 "crias" mas debiles (OnDeathSplit), que
        // ya no vuelven a dividirse. Debil a Pierce (se pincha facil), resiste Strike (los golpes
        // contundentes solo lo aplastan sin hacerle mucho).
        public static EnemyStats CreateSlime(int suffix)
        {
            var slime = new EnemyStats
            {
                Name = $"Slime {suffix}", MaxHP = 50, HP = 50,
                Attack = 8, Defense = 2, Speed = 4,
                AttackElement = Element.Strike,
                Weakness = Element.Pierce, Resistance = Element.Strike,
            };
            slime.OnDeathSplit = () => new List<EnemyStats>
            {
                CreateSlimeling($"{suffix}a"),
                CreateSlimeling($"{suffix}b"),
            };
            return slime;
        }

        public static EnemyStats CreateSlimeling(string suffix)
        {
            return new EnemyStats
            {
                Name = $"Cria de Slime {suffix}", MaxHP = 18, HP = 18,
                Attack = 5, Defense = 1, Speed = 5,
                AttackElement = Element.Strike,
                Weakness = Element.Pierce, Resistance = Element.Strike,
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
                int roll = rng.Next(3);
                if (roll == 0) list.Add(CreateWolf(i + 1));
                else if (roll == 1) list.Add(CreateBeetle(i + 1));
                else list.Add(CreateSlime(i + 1));
            }
            return list;
        }
    }
}
