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

        // Rebalance (pedido puntual): la party ahora es de 6 integrantes, no 4 -- el dano total
        // por ronda es mucho mas alto que con lo que estaba pensado el bestiario original, y los
        // encuentros comunes se sentian de un solo golpe. +45% de HP y +30% de aguante en TODO el
        // bestiario (jefes incluidos), como multiplicadores aparte de ScaledAttack (que ya escala
        // el ATQ por piso) para poder tocar cada eje por separado -- mas vida Y mas dificil de
        // aturdir, sin tocar cuanto pega cada uno.
        private const float HpScaleForSixPartyMembers = 1.45f;
        private const float PoiseScaleForSixPartyMembers = 1.3f;
        private static int ScaledHp(int baseHp) => (int)Math.Round(baseHp * HpScaleForSixPartyMembers);
        private static int ScaledPoise(int basePoise) => (int)Math.Round(basePoise * PoiseScaleForSixPartyMembers);

        public static EnemyStats CreateWolf(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Lobo Colmillo {suffix}", MaxHP = ScaledHp(40), HP = ScaledHp(40),
                Attack = ScaledAttack(12, floorIndex), Defense = 3, Speed = 6,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(30), Poise = ScaledPoise(30),
            };
        }

        // Excepcion deliberada al patron fuego+corte (ver CommonWeaknesses): el caparazon lo
        // protege justo de esas 2 (nada de fuego ni de filo lo atraviesa), pero un golpe
        // contundente bien dado lo raja. Enseña que no toda debilidad es fuego/corte.
        public static EnemyStats CreateBeetle(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Escarabajo Coraza {suffix}", MaxHP = ScaledHp(55), HP = ScaledHp(55),
                Attack = ScaledAttack(10, floorIndex), Defense = 6, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Strike }, Resistance = Element.Pierce,
                MaxPoise = ScaledPoise(45), Poise = ScaledPoise(45),
            };
        }

        // Slime: al morir por primera vez se divide en 2 "crias" mas debiles (OnDeathSplit), que
        // ya no vuelven a dividirse. Debil a fuego+corte (se seca/se corta facil), resiste Strike
        // (los golpes contundentes solo lo aplastan sin hacerle mucho).
        public static EnemyStats CreateSlime(int suffix, int floorIndex = 0)
        {
            var slime = new EnemyStats
            {
                Name = $"Slime {suffix}", MaxHP = ScaledHp(50), HP = ScaledHp(50),
                Attack = ScaledAttack(11, floorIndex), Defense = 2, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(35), Poise = ScaledPoise(35),
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
                Name = $"Cria de Slime {suffix}", MaxHP = ScaledHp(18), HP = ScaledHp(18),
                Attack = ScaledAttack(7, floorIndex), Defense = 1, Speed = 5,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(16), Poise = ScaledPoise(16),
            };
        }

        // Gordo Baboso (pedido puntual): mucha Defensa (los golpes normales rebotan), pero el
        // aguante es MUY fragil (MaxPoise bajo, sin ScaledPoise -- a proposito, romperlo tiene que
        // sentirse facil) -- una vez roto, BrokenDamageMultiplierOverride castiga mucho mas fuerte
        // que el generico (ver CombatEngine.ComputeDamageVsEnemy), asi que "romperlo" es la forma
        // REAL de bajarle la vida, no solo pegar y pegar contra su Defensa alta. Al morir suelta 2
        // Babosas (OnDeathSplit) que atacan tirando veneno (ver CreateSlug).
        public static EnemyStats CreateSludge(int suffix, int floorIndex = 0)
        {
            var sludge = new EnemyStats
            {
                Name = $"Gordo Baboso {suffix}", MaxHP = ScaledHp(70), HP = ScaledHp(70),
                Attack = ScaledAttack(10, floorIndex), Defense = 9, Speed = 2,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Pierce,
                // Aguante fragil A PROPOSITO (no usa ScaledPoise): mitad del de un Slime comun.
                MaxPoise = 18, Poise = 18,
                BrokenDamageMultiplierOverride = 2f,
            };
            sludge.OnDeathSplit = () => new List<EnemyStats>
            {
                CreateSlug($"{suffix}a", floorIndex),
                CreateSlug($"{suffix}b", floorIndex),
            };
            return sludge;
        }

        // Babosa: cria del Gordo Baboso. Fragil (poca HP/Defensa), pero cada golpe que conecta
        // tiene OnHitStatusChancePercent de tirar veneno -- chance real, NO garantizada, mismo
        // criterio que cualquier otro proc del juego (ver CombatEngine.ExecuteEnemyAction).
        public static EnemyStats CreateSlug(string suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Babosa {suffix}", MaxHP = ScaledHp(20), HP = ScaledHp(20),
                Attack = ScaledAttack(7, floorIndex), Defense = 1, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(14), Poise = ScaledPoise(14),
                OnHitStatusName = "Veneno", OnHitStatusChancePercent = 20, OnHitStatusDamagePercent = 5, OnHitStatusRounds = 3,
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
                Name = "Guardian de Piedra", MaxHP = ScaledHp(220), HP = ScaledHp(220),
                Attack = ScaledAttack(19, floorIndex), Defense = 8, Speed = 4,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(90), Poise = ScaledPoise(90),
                // Combinado con que un golpe de debilidad ya pega el doble de dano de HP, el
                // multiplicador de aguante de debilidad (1.8x) lo rompia demasiado rapido para un
                // jefe -- 1.3 lo deja bastante mas lento que un enemigo comun, sin ser imposible.
                PoiseWeaknessResistance = 1.3f,
            };
        }

        // FOE (ver Gameplay/FoeController): enemigo fuerte que patrulla el piso a la vista, evitable
        // -- a mitad de camino entre un encuentro comun y el jefe (HP/Ataque ~60% del jefe), para
        // que colisionar con el se sienta arriesgado de verdad sin ser un segundo jefe.
        public static EnemyStats CreateFoe(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Centinela Errante", MaxHP = ScaledHp(130), HP = ScaledHp(130),
                Attack = ScaledAttack(15, floorIndex), Defense = 6, Speed = 5,
                AttackElement = Element.Strike,
                Weaknesses = CommonWeaknesses, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(60), Poise = ScaledPoise(60),
                PoiseWeaknessResistance = 1.15f,
            };
        }

        // Guardian de una boveda de cascada (ver Dungeon/DungeonGenerator.AddWaterfallVault y
        // Gameplay/DungeonManager.OnPlayerEnterCell): un enemigo fuerte de proposito unico por
        // piso, a mitad de camino entre un comun y un jefe -- protege el tesoro garantizado de la
        // boveda. Mismo criterio que CreateFoe (un solo statline fijo, sin reskin por bioma):
        // aparece igual en cualquier piso con agua de verdad (bosque, cueva intergalactica y Bioma
        // de Cuevas -- ver AddWaterfallVault para por que no en el patio ni en el castillo).
        public static EnemyStats CreateWaterfallGuardian(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Guardián de la Cascada", MaxHP = ScaledHp(150), HP = ScaledHp(150),
                Attack = ScaledAttack(17, floorIndex), Defense = 7, Speed = 4,
                AttackElement = Element.Ice,
                Weaknesses = CommonWeaknesses, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(70), Poise = ScaledPoise(70),
                PoiseWeaknessResistance = 1.2f,
            };
        }

        // Cuantos enemigos trae un encuentro comun. Pedido puntual: el piso 0 ("de respiro") sigue
        // sin pasar de 2 (1 el mas probable), pero de ahi en adelante el tope real es SIEMPRE 6 sin
        // importar la profundidad (antes escalaba 1 cada 2 pisos) y la distribucion es una campana
        // de Gauss discreta centrada en 4 -- 4 es el tamano MAS comun, 1 y 6 los mas raros (las 2
        // colas de la campana), en vez del reparto monotonicamente decreciente desde 1 de antes
        // (que hacia que un grupo grande fuera casi anecdotico incluso con el tope alto). Un solo
        // punto de verdad para las 4 zonas que tienen encuentros comunes (bosque/cueva/cueva de
        // roca/castillo+patio, ver Create*Encounter mas abajo).
        private const float EncounterSizeMean = 4f;
        private const float EncounterSizeStdDev = 1.35f;

        private static int RollWeighted(Random rng, float[] weights)
        {
            float total = 0f;
            foreach (var w in weights) total += w;

            float roll = (float)rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (roll < acc) return i + 1;
            }
            return weights.Length;
        }

        private static int RollEncounterSize(Random rng, int floorIndex)
        {
            if (floorIndex <= 0) return RollWeighted(rng, new[] { 1f, 0.5f }); // piso de respiro: tope 2

            var weights = new float[6];
            for (int i = 0; i < weights.Length; i++)
            {
                float z = (i + 1 - EncounterSizeMean) / EncounterSizeStdDev;
                weights[i] = (float)Math.Exp(-0.5 * z * z);
            }
            return RollWeighted(rng, weights);
        }

        // Grupo aleatorio de enemigos normales (con repeticion) para un encuentro comun; el tamano
        // y la fuerza de cada uno escalan con floorIndex (ver RollEncounterSize/ScaledAttack). El
        // Gordo Baboso entra en el mismo pool que el resto (1 en 4 en vez de 1 en 3) -- no es un
        // "mini-jefe" aparte, es un enemigo comun mas, solo que con un enfoque de combate distinto.
        public static List<EnemyStats> CreateRandomEncounter(Random rng, int floorIndex = 0)
        {
            int count = RollEncounterSize(rng, floorIndex);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                int roll = rng.Next(4);
                if (roll == 0) list.Add(CreateWolf(i + 1, floorIndex));
                else if (roll == 1) list.Add(CreateBeetle(i + 1, floorIndex));
                else if (roll == 2) list.Add(CreateSlime(i + 1, floorIndex));
                else list.Add(CreateSludge(i + 1, floorIndex));
            }
            return list;
        }

        // ---------- Bioma 2: Cueva Intergalactica ----------
        // Segunda zona, escondida detras de la Puerta Fria del piso 0 (ver
        // Dungeon/DungeonGenerator.PlaceBiomeGate y Gameplay/DungeonManager.EnterBiomeGateFloor):
        // una cueva donde algo de mas alla de las estrellas quedo atrapado. La debilidad
        // dominante de la zona es Volt (todo lo que vive aca es humedo/organico o esta cargado de
        // estatica comica), salvo la excepcion deliberada de CreateQuartzCrab -- mismo patron
        // pedagogico que CommonWeaknesses arriba, pero renovado: el jugador tiene que aprender que
        // ESTA zona premia un elemento distinto al de siempre.
        private static readonly Element[] CaveWeaknesses = { Element.Volt, Element.Pierce };

        public static EnemyStats CreateStarLarva(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Larva Estelar {suffix}", MaxHP = ScaledHp(42), HP = ScaledHp(42),
                Attack = ScaledAttack(13, floorIndex), Defense = 2, Speed = 7,
                AttackElement = Element.Pierce,
                Weaknesses = CaveWeaknesses, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(28), Poise = ScaledPoise(28),
            };
        }

        public static EnemyStats CreateVoidJelly(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Medusa del Vacío {suffix}", MaxHP = ScaledHp(48), HP = ScaledHp(48),
                Attack = ScaledAttack(12, floorIndex), Defense = 3, Speed = 5,
                AttackElement = Element.Strike,
                Weaknesses = CaveWeaknesses, Resistance = Element.Fire,
                MaxPoise = ScaledPoise(32), Poise = ScaledPoise(32),
            };
        }

        // Excepcion deliberada (mismo rol que CreateBeetle en la zona original): el caparazon de
        // cuarzo no conduce electricidad y resiste bien un pinchazo, pero un golpe contundente lo raja.
        public static EnemyStats CreateQuartzCrab(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Cangrejo de Cuarzo {suffix}", MaxHP = ScaledHp(60), HP = ScaledHp(60),
                Attack = ScaledAttack(11, floorIndex), Defense = 7, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Strike }, Resistance = Element.Volt,
                MaxPoise = ScaledPoise(48), Poise = ScaledPoise(48),
            };
        }

        public static EnemyStats CreateAbyssStalker(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Acechador de las Simas {suffix}", MaxHP = ScaledHp(58), HP = ScaledHp(58),
                Attack = ScaledAttack(15, floorIndex), Defense = 4, Speed = 6,
                AttackElement = Element.Pierce,
                Weaknesses = CaveWeaknesses, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(40), Poise = ScaledPoise(40),
            };
        }

        // Kadulu: quedo atrapado en esta cueva cuando lo que sea que la resquebrajo hacia el vacio
        // se cerro de nuevo detras suyo. Sigue el mismo patron que CreateBoss (confirma la
        // debilidad dominante de SU zona -- Volt -- en vez de una sorpresa nueva) pero con mas HP
        // y ataque que el Guardian de Piedra, para que se sienta como una escalada real.
        public static EnemyStats CreateKadulu(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Kadulu, el Hambriento del Vacío", MaxHP = ScaledHp(260), HP = ScaledHp(260),
                Attack = ScaledAttack(21, floorIndex), Defense = 9, Speed = 5,
                AttackElement = Element.Pierce,
                Weaknesses = new[] { Element.Volt }, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(100), Poise = ScaledPoise(100),
                PoiseWeaknessResistance = 1.3f,
            };
        }

        public static List<EnemyStats> CreateCaveEncounter(Random rng, int floorIndex = 0)
        {
            int count = RollEncounterSize(rng, floorIndex);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                int roll = rng.Next(4);
                if (roll == 0) list.Add(CreateStarLarva(i + 1, floorIndex));
                else if (roll == 1) list.Add(CreateVoidJelly(i + 1, floorIndex));
                else if (roll == 2) list.Add(CreateQuartzCrab(i + 1, floorIndex));
                else list.Add(CreateAbyssStalker(i + 1, floorIndex));
            }
            return list;
        }

        // ---------- Bioma de Cuevas: nido de goblins ----------
        // Tercera zona, alcanzable por la escalera al fondo de la zona aislada de CUALQUIER piso
        // del bioma raiz (ver Dungeon/DungeonGenerator.PlaceCaveBiomeExit y Gameplay/
        // DungeonManager.EnterCaveBiomeExit): pedido puntual, catacumbas tomadas por una horda de
        // goblins (inspiracion Warhammer/orcos) en vez de fauna generica de cueva -- la debilidad
        // dominante sigue siendo Hielo+Contundente (huesos y cuero curtido, un golpe seco o el frio
        // calan mejor que el filo o el rayo), salvo la excepcion deliberada de CreateCryptOrc
        // (mismo patron pedagogico que CommonWeaknesses/CaveWeaknesses arriba). Las 3 zonas cubren
        // entre las 3 los 6 elementos sin pisarse: Bosque Fuego+Corte, Bioma 2 Rayo+Perforante,
        // Cuevas Hielo+Contundente.
        private static readonly Element[] RockCaveWeaknesses = { Element.Ice, Element.Strike };

        public static EnemyStats CreateGoblinScout(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Goblin Explorador {suffix}", MaxHP = ScaledHp(38), HP = ScaledHp(38),
                Attack = ScaledAttack(13, floorIndex), Defense = 2, Speed = 8,
                AttackElement = Element.Slash,
                Weaknesses = RockCaveWeaknesses, Resistance = Element.Volt,
                MaxPoise = ScaledPoise(26), Poise = ScaledPoise(26),
            };
        }

        public static EnemyStats CreateGoblinSpearman(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Goblin Lancero {suffix}", MaxHP = ScaledHp(46), HP = ScaledHp(46),
                Attack = ScaledAttack(12, floorIndex), Defense = 3, Speed = 6,
                AttackElement = Element.Pierce,
                Weaknesses = RockCaveWeaknesses, Resistance = Element.Fire,
                MaxPoise = ScaledPoise(30), Poise = ScaledPoise(30),
            };
        }

        // Excepcion deliberada (mismo rol que CreateBeetle/CreateQuartzCrab): el orco bruto de la
        // cripta ni el frio ni un golpe contundente lo frenan (esta curtido a los dos), pero una
        // lanza bien clavada en una juntura de su armadura oxidada lo raja.
        public static EnemyStats CreateCryptOrc(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Orco de la Cripta {suffix}", MaxHP = ScaledHp(65), HP = ScaledHp(65),
                Attack = ScaledAttack(11, floorIndex), Defense = 8, Speed = 2,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Pierce }, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(55), Poise = ScaledPoise(55),
            };
        }

        public static EnemyStats CreateGoblinDigger(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Goblin Cavador {suffix}", MaxHP = ScaledHp(50), HP = ScaledHp(50),
                Attack = ScaledAttack(14, floorIndex), Defense = 4, Speed = 5,
                AttackElement = Element.Strike,
                Weaknesses = RockCaveWeaknesses, Resistance = Element.Pierce,
                MaxPoise = ScaledPoise(34), Poise = ScaledPoise(34),
            };
        }

        // Gorlok sigue el mismo patron que CreateBoss/CreateKadulu (confirma la debilidad
        // dominante de SU zona -- Hielo -- en vez de una sorpresa nueva) con HP/ataque en la misma
        // escala que los otros 2 jefes. Titulo cambiado a jefe de horda goblin, mismo nombre propio.
        public static EnemyStats CreateCaveBoss(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Gorlok, Jefe de la Horda", MaxHP = ScaledHp(240), HP = ScaledHp(240),
                Attack = ScaledAttack(20, floorIndex), Defense = 9, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Ice }, Resistance = Element.Strike,
                MaxPoise = ScaledPoise(95), Poise = ScaledPoise(95),
                PoiseWeaknessResistance = 1.3f,
            };
        }

        public static List<EnemyStats> CreateRockCaveEncounter(Random rng, int floorIndex = 0)
        {
            int count = RollEncounterSize(rng, floorIndex);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                int roll = rng.Next(4);
                if (roll == 0) list.Add(CreateGoblinScout(i + 1, floorIndex));
                else if (roll == 1) list.Add(CreateGoblinSpearman(i + 1, floorIndex));
                else if (roll == 2) list.Add(CreateCryptOrc(i + 1, floorIndex));
                else list.Add(CreateGoblinDigger(i + 1, floorIndex));
            }
            return list;
        }

        public static EnemyStats CreateCastleSentinel(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Centinela de Ónice {suffix}", MaxHP = ScaledHp(78), HP = ScaledHp(78),
                Attack = ScaledAttack(15, floorIndex), Defense = 8, Speed = 3,
                AttackElement = Element.Strike,
                Weaknesses = new[] { Element.Fire, Element.Strike }, Resistance = Element.Pierce,
                MaxPoise = ScaledPoise(56), Poise = ScaledPoise(56),
            };
        }

        public static EnemyStats CreateCastleWraith(int suffix, int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = $"Espectro del Salón {suffix}", MaxHP = ScaledHp(52), HP = ScaledHp(52),
                Attack = ScaledAttack(18, floorIndex), Defense = 3, Speed = 7,
                AttackElement = Element.Ice,
                Weaknesses = new[] { Element.Fire, Element.Slash }, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(36), Poise = ScaledPoise(36),
            };
        }

        public static EnemyStats CreateCastleBoss(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Castellano de la Corona Umbría", MaxHP = ScaledHp(285), HP = ScaledHp(285),
                Attack = ScaledAttack(22, floorIndex), Defense = 10, Speed = 5,
                AttackElement = Element.Slash,
                Weaknesses = new[] { Element.Fire, Element.Strike }, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(110), Poise = ScaledPoise(110), PoiseWeaknessResistance = 1.35f,
            };
        }

        public static EnemyStats CreatePatioBoss(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Guardián del Patio Marchito", MaxHP = ScaledHp(205), HP = ScaledHp(205),
                Attack = ScaledAttack(19, floorIndex), Defense = 8, Speed = 6,
                AttackElement = Element.Pierce,
                Weaknesses = new[] { Element.Fire, Element.Strike }, Resistance = Element.Pierce,
                MaxPoise = ScaledPoise(82), Poise = ScaledPoise(82),
            };
        }

        public static EnemyStats CreateCastleCryptBoss(int floorIndex = 0)
        {
            return new EnemyStats
            {
                Name = "Custodio Sepultado", MaxHP = ScaledHp(325), HP = ScaledHp(325),
                Attack = ScaledAttack(25, floorIndex), Defense = 13, Speed = 4,
                AttackElement = Element.Ice,
                Weaknesses = new[] { Element.Fire, Element.Strike }, Resistance = Element.Ice,
                MaxPoise = ScaledPoise(138), Poise = ScaledPoise(138), PoiseWeaknessResistance = 1.5f,
            };
        }

        public static List<EnemyStats> CreateCastleEncounter(Random rng, int floorIndex = 0)
        {
            // Antes SIEMPRE 1 o 2 (rng.Next(1,3)), sin importar el piso ni la nueva regla de grupo
            // de RollEncounterSize -- por eso el castillo se sentia con encuentros casi siempre de
            // un solo enemigo. Ahora comparte la misma campana de Gauss (tope 6, pico en 4) que el
            // resto de las zonas.
            int count = RollEncounterSize(rng, floorIndex);
            var list = new List<EnemyStats>();
            for (int i = 0; i < count; i++)
            {
                if (rng.Next(2) == 0) list.Add(CreateCastleSentinel(i + 1, floorIndex));
                else list.Add(CreateCastleWraith(i + 1, floorIndex));
            }
            return list;
        }
    }
}
