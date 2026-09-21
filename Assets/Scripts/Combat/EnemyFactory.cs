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

        // Cuanto mas fuerte pega cada enemigo por piso de profundidad (floorIndex=0 en el primer
        // piso): 12% mas de ATK por piso, asi la dificultad sube de verdad a medida que se baja,
        // no solo por HP/cantidad.
        private const float AttackScalePerFloor = 0.12f;

        private static int ScaledAttack(int baseAttack, int floorIndex) =>
            (int)Math.Round(baseAttack * (1f + Math.Max(0, floorIndex) * AttackScalePerFloor));

        public static EnemyStats CreateWolf(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Lobo Colmillo {suffix}", MaxHP = 40, HP = 40,
                Attack = ScaledAttack(12, floorIndex), Defense = 3, Speed = 6,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Ice,
                MaxPoise = 30, Poise = 30,
            };
        }

        // Excepcion deliberada al patron fuego+corte (ver CommonWeaknesses): el caparazon lo
        // protege justo de esas 2 (nada de fuego ni de filo lo atraviesa), pero un golpe
        // contundente bien dado lo raja. Enseña que no toda debilidad es fuego/corte.
        public static EnemyStats CreateBeetle(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Escarabajo Coraza {suffix}", MaxHP = 55, HP = 55,
                Attack = ScaledAttack(10, floorIndex), Defense = 6, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Strike }, Resistance = Element.Pierce,
                MaxPoise = 45, Poise = 45,
            };
        }

        // Slime: al morir por primera vez se divide en 2 "crias" mas debiles (OnDeathSplit), que
        // ya no vuelven a dividirse. Debil a fuego+corte (se seca/se corta facil), resiste Strike
        // (los golpes contundentes solo lo aplastan sin hacerle mucho).
        public static EnemyStats CreateSlime(int suffix, int floorIndex = 0)
        {
            var slime = new EnemyStats
            {
                Name = $"Slime {suffix}", MaxHP = 50, HP = 50,
                Attack = ScaledAttack(11, floorIndex), Defense = 2, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 35, Poise = 35,
            };
            slime.OnDeathSplit = () => new List<EnemyStats>
            {
                CreateSlimeling($"{suffix}a", floorIndex),
                CreateSlimeling($"{suffix}b", floorIndex),
            };
            return slime;
        }

        public static EnemyStats CreateSlimeling(string suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Cria de Slime {suffix}", MaxHP = 18, HP = 18,
                Attack = ScaledAttack(7, floorIndex), Defense = 1, Speed = 5,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 16, Poise = 16,
            };
        }

        // El jefe de estos pisos tambien sigue el patron general (fuego+corte) a proposito: para
        // esta primera tanda de pisos, el objetivo es que el jugador confirme lo que ya aprendio
        // contra los enemigos normales, no que tenga que descubrir una debilidad nueva justo en el
        // jefe.
        public static EnemyStats CreateBoss(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Guardian de Piedra", MaxHP = 220, HP = 220,
                Attack = ScaledAttack(19, floorIndex), Defense = 8, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = 90, Poise = 90,
            };
        }

        // Cuantos enemigos trae un encuentro comun (1 a 3), con 2 siempre mas probable que 3 --
        // pero a medida que se baja de piso, un encuentro de 3 se vuelve bastante mas comun (y uno
        // de 1 solo, mas raro), sin que 3 llegue a superar a 2. floorIndex=0 en el primer piso.
        private static int RollEncounterSize(Random rng, int floorIndex)
        {
            int f = Math.Max(0, floorIndex);
            float w1 = Math.Max(0.1f, 0.35f - 0.04f * f);
            float w3 = Math.Min(0.4f, 0.15f + 0.05f * f);
            float w2 = Math.Max(0.1f, 1f - w1 - w3);

            float roll = (float)rng.NextDouble() * (w1 + w2 + w3);
            if (roll < w1) return 1;
            if (roll < w1 + w2) return 2;
            return 3;
        }

        // Grupo aleatorio de enemigos normales (con repeticion) para un encuentro comun; el tamano
        // y la fuerza de cada uno escalan con floorIndex (ver RollEncounterSize/ScaledAttack).
        public static List<EnemyStats> CreateRandomEncounter(Random rng, int floorIndex = 0)
        {
            int count = RollEncounterSize(rng, floorIndex);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                int roll = rng.Next(3);
                if (roll == 0) list.Add(CreateWolf(i + 1, floorIndex));
                else if (roll == 1) list.Add(CreateBeetle(i + 1, floorIndex));
                else list.Add(CreateSlime(i + 1, floorIndex));
            }
            return list;
        }
    }
}
