using System;
using System.Collections.Generic;

namespace Combat
{
    public static class EnemyFactory
    {
        // La mayoria del bestiario comparte estas 2 debilidades (fuego + corte): la idea es que el
        // jugador aprenda rapido "estas 2 opciones casi siempre funcionan" -- ver CreateBeetle para
        // la excepcion deliberada que rompe el patron, para que tambien aprenda a fijarse en cada
        // enemigo en vez de piloto automatico.
        private static readonly Element[] CommonWeaknesses = { Element.Fire, Element.Slash };

        public static EnemyStats CreateWolf(int suffix)
        {
            return new EnemyStats
            {
                Name = $"Lobo Colmillo {suffix}", MaxHP = 40, HP = 40,
                Attack = 9, Defense = 3, Speed = 6,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Ice,
                MaxPoise = 30, Poise = 30,
            };
        }

        // Excepcion deliberada al patron fuego+corte (ver CommonWeaknesses): el caparazon lo
        // protege justo de esas 2 (nada de fuego ni de filo lo atraviesa), pero un golpe
        // contundente bien dado lo raja. Enseña que no toda debilidad es fuego/corte.
        public static EnemyStats CreateBeetle(int suffix)
        {
            return new EnemyStats
            {
                Name = $"Escarabajo Coraza {suffix}", MaxHP = 55, HP = 55,
                Attack = 7, Defense = 6, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Strike }, Resistance = Element.Pierce,
                MaxPoise = 45, Poise = 45,
            };
        }

        // Slime: al morir por primera vez se divide en 2 "crias" mas debiles (OnDeathSplit), que
        // ya no vuelven a dividirse. Debil a fuego+corte (se seca/se corta facil), resiste Strike
        // (los golpes contundentes solo lo aplastan sin hacerle mucho).
        public static EnemyStats CreateSlime(int suffix)
        {
            var slime = new EnemyStats
            {
                Name = $"Slime {suffix}", MaxHP = 50, HP = 50,
                Attack = 8, Defense = 2, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 35, Poise = 35,
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
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 16, Poise = 16,
            };
        }

        // El jefe de estos pisos tambien sigue el patron general (fuego+corte) a proposito: para
        // esta primera tanda de pisos, el objetivo es que el jugador confirme lo que ya aprendio
        // contra los enemigos normales, no que tenga que descubrir una debilidad nueva justo en el
        // jefe.
        public static EnemyStats CreateBoss()
        {
            return new EnemyStats
            {
                Name = "Guardian de Piedra", MaxHP = 220, HP = 220,
                Attack = 14, Defense = 8, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 90, Poise = 90,
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
