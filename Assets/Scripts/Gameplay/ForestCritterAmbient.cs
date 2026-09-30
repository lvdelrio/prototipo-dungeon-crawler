using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DungeonGen;

namespace Gameplay
{
    /// <summary>
    /// Pequeña fauna ambiental, sin físicas ni navegación: un único Update mueve un puñado de
    /// mallas simples como un enjambre de boids (separacion/alineacion/cohesion entre ellas, ver
    /// ComputeFlockSteer) en vez de individuos que se ignoran entre si. De día las mariposas
    /// revolotean entre arbustos y salen disparadas del camino cuando el jugador se acerca; de
    /// noche se vuelven polillas atraídas por luces locales.
    /// </summary>
    public sealed class ForestCritterAmbient : MonoBehaviour
    {
        private sealed class Critter
        {
            public Transform root;
            public Transform leftWing;
            public Transform rightWing;
            public Renderer leftRenderer;
            public Renderer rightRenderer;
            // Posicion/velocidad "logicas" del boid, separadas de root.position: root.position
            // ademas lleva sumado el bamboleo vertical (bob) de cada frame, que NO tiene que
            // retroalimentar la integracion de velocidad (si no, cada bob se leeria como un
            // "empujon" real y el vuelo se ve tembloroso en vez de suave).
            public Vector3 position;
            public Vector3 velocity;
            // target = punto de interes fijo (centro de arbusto/agua/luz, ver ChooseDestination),
            // NO el destino final -- el destino real que persigue Update es target + un offset que
            // gira solo (orbitAngle), asi el vuelo es un revoloteo continuo alrededor del punto en
            // vez de "volar hasta ahi y quedarse parado esperando el proximo timer" (eso era lo que
            // se leia como "quieto" pese a que target si cambiaba, ver Update).
            public Vector3 target;
            public float orbitAngle;
            public float orbitRadius;
            public float orbitSpeed;
            // Viaje deliberado a la entrada de la cueva (ver ComputePathTo/ChooseDestination
            // Prioridad 2): secuencia de centros de celda ya validada contra paredes reales (BFS,
            // igual criterio que CastleMouseAmbient), no la linea recta que usaria target solo --
            // sin esto, una migracion de piso entero se trabaria en la primera esquina.
            public Vector3[] migrationPath;
            public int migrationIndex;
            // true si migrationPath es una persecucion del foco dinamico (jugador/cueva, ver
            // Update) en vez de un viaje "de verdad" elegido por ChooseDestination -- el avance de
            // waypoints (ver el bloque traveling en Update) NO debe pisar critter.target mientras
            // esto es true, si no se perderia el destino de fondo (el arbusto al que iba antes de
            // que el jugador se cruzara) y no habria a que volver cuando el jugador se aleje.
            public bool migrationIsDynamic;
            // true si el ultimo punto de interes elegido (ver ChooseDestination) es una laguna --
            // cambia el ESTILO de revoloteo en Update (vaiven vertical marcado "jugando sobre el
            // agua" en vez del circulo chato de un arbusto/luz). Se ignora mientras el foco del
            // frame es el jugador o la cueva por prioridad (ver focusIsDynamic en Update).
            public bool atWater;
            // Cuando se toma prestado el foco del jugador o la cueva (ver focusIsDynamic en
            // Update), este camino se recalcula cada cierto tiempo (nextDynamicPathRefresh) en vez
            // de una sola vez: el destino se mueve (el jugador camina), asi que el BFS de
            // ComputePathTo tiene que refrescarse en vez de perseguir una ruta vieja para siempre.
            public float nextDynamicPathRefresh;
            // Altura propia de vuelo, sorteada por bicho (ver CreateCritter/ChooseDestination) --
            // pedido puntual: "que no esten a la altura de la camara" (se probo fijo a 1.2 antes),
            // ahora cada uno vuela a una altura propia entre el piso y el techo.
            public float preferredHeight;
            public float thinkAt;
            public float phase;
            public float speed;
            public bool fleeing;
        }

        private static readonly Color[] ButterflyColors =
        {
            new Color(0.48f, 0.77f, 0.94f),
            new Color(0.88f, 0.55f, 0.78f),
            new Color(0.94f, 0.78f, 0.38f),
        };
        private static Material[] _butterflyMaterials;
        private static Material _mothMaterial;
        private static Material _bodyMaterial;

        private readonly List<Critter> _critters = new List<Critter>();
        // Punto de interes + si es agua (arbustos/puntos de reposo genericos = false, lagunas
        // reales = true, ver Initialize) -- el tag es lo que deja a Update elegir el estilo de
        // revoloteo correcto por tipo de lugar (pedido puntual: "en el agua que jueguen arriba,
        // en el arbusto que jueguen alrededor").
        private readonly List<(Vector3 pos, bool isWater)> _habitats = new List<(Vector3, bool)>();
        private Light[] _lights = new Light[0];
        private GridPlayerController _player;
        private DayNightCycle _dayNight;
        private float _cellSize = 4f;
        private float _nextLightScan;
        private bool _caveOnly;
        private bool _nightMode;
        private bool _modeInitialized;
        // Caja que contiene a todos los habitats (arbustos + agua, ver Initialize) con un margen --
        // sin esto, separacion/alineacion/cohesion (ComputeFlockSteer) no tienen nada que las
        // devuelva si por casualidad empujan al enjambre lejos de sus destinos por un rato largo
        // (pedido puntual, reportado: "las mariposas estan fuera del mapa"). Position.y no se
        // clampea, el bob vertical es chico y separado (ver Critter.position).
        private Vector3 _boundsMin;
        private Vector3 _boundsMax;
        private bool _hasBounds;
        // Piso real (para ResolveWallCollision, ver mas abajo). Rango de altura de vuelo: pedido
        // puntual "su unica limitacion sea no salir del alto de la pared, pero que no esten a la
        // altura de la camara" -- se probo fijarlas todas a la altura de camara (1.2, ver
        // m_LocalPosition de "Main Camera" en DungeonTest.unity) antes; ahora cada bicho vuela a
        // SU PROPIA altura (ver Critter.preferredHeight/RandomFlightHeight), sorteada dentro de
        // este rango, no una constante compartida.
        private DungeonFloor _floor;
        private float _wallHeight = 3f;
        private float _minFlightHeight = 0.5f;
        private float _maxFlightHeight = 2.5f;
        // Destino de migracion nocturna (pedido puntual: "de noche... deben dirigirse a la cueva
        // donde en la entrada hay antorchas") -- null en cualquier piso que no tenga mini-cueva
        // propia (ver DungeonLevelBuilder._caveEntrancePoint), o en la instancia _caveOnly (esa ya
        // vive DENTRO de una cueva real, no necesita migrar a ningun lado).
        private Vector3? _caveEntrancePoint;

        public void Initialize(List<Vector3> bushHabitats, List<Vector3> waterHabitats, GridPlayerController player, DayNightCycle dayNight, bool caveOnly, float cellSize, DungeonFloor floor, float wallHeight, Vector3? caveEntrancePoint = null)
        {
            _habitats.Clear();
            if (bushHabitats != null) foreach (var p in bushHabitats) _habitats.Add((p, false));
            if (waterHabitats != null) foreach (var p in waterHabitats) _habitats.Add((p, true));
            _player = player;
            _dayNight = dayNight;
            _caveOnly = caveOnly;
            _cellSize = Mathf.Max(1f, cellSize);
            _caveEntrancePoint = caveEntrancePoint;
            _floor = floor;
            _wallHeight = Mathf.Max(1f, wallHeight);
            _minFlightHeight = Mathf.Min(0.5f, _wallHeight * 0.3f);
            _maxFlightHeight = Mathf.Max(_minFlightHeight + 0.3f, _wallHeight - 0.4f);

            _hasBounds = _habitats.Count > 0;
            if (_hasBounds)
            {
                _boundsMin = _habitats[0].pos;
                _boundsMax = _habitats[0].pos;
                for (int i = 1; i < _habitats.Count; i++)
                {
                    _boundsMin = Vector3.Min(_boundsMin, _habitats[i].pos);
                    _boundsMax = Vector3.Max(_boundsMax, _habitats[i].pos);
                }
                float margin = _cellSize * 1.5f;
                _boundsMin -= new Vector3(margin, 0f, margin);
                _boundsMax += new Vector3(margin, 0f, margin);
            }

            EnsureMaterials();
            // Pedido puntual: "un par mas de mariposas en todos los pisos" -- piso minimo y
            // maximo subidos ambos en 2 (antes 2-6 bosque / 2-4 cueva).
            int count = Mathf.Clamp(Mathf.CeilToInt(_habitats.Count * 0.35f), 4, caveOnly ? 6 : 8);
            for (int i = 0; i < count; i++)
            {
                Vector3 home = _habitats[Random.Range(0, _habitats.Count)].pos;
                var critter = CreateCritter(i, home);
                _critters.Add(critter);
            }

            _nightMode = _dayNight != null && _dayNight.NightAmount >= 0.55f;
            _modeInitialized = true;
            SetMode(_nightMode);
            if (_nightMode)
            {
                _lights = FindObjectsOfType<Light>();
                _nextLightScan = Time.time + 2f;
            }
            for (int i = 0; i < _critters.Count; i++) ChooseDestination(_critters[i]);
        }

        // Para MinimapUI (pedido puntual: "que los boids se vean en el mapa de debug") -- solo las
        // que estan realmente visibles en el mundo (SetMode desactiva las polillas de cueva de dia,
        // ver _caveOnly), si no el mapa mostraria bichos que en el piso 3D ni siquiera se ven.
        public void CollectPositions(List<Vector3> results)
        {
            for (int i = 0; i < _critters.Count; i++)
            {
                var critter = _critters[i];
                if (critter.root != null && critter.root.gameObject.activeInHierarchy)
                    results.Add(critter.position);
            }
        }

        // TEMPORAL (diagnostico "siguen quietas" -- sacar apenas se confirme la causa real): texto
        // en pantalla con los numeros en vivo de la primera criatura, para ver la verdad sin
        // depender de interpretar una captura o de navegar el Hierarchy/Inspector a mano. Si
        // "root.position" cambia frame a frame ACA pero el bicho no se mueve en pantalla, el bug
        // esta en el render/sync, no en la logica de movimiento -- si NINGUN numero cambia, Update
        // no esta avanzando de verdad para esta instancia.
        private void OnGUI()
        {
            if (!Application.isEditor || _critters.Count == 0) return;
            var c = _critters[0];
            GUI.Box(new Rect(10, 250, 460, 130), "");
            string text = $"[WildlifeDebug] {(c.root != null ? c.root.name : "???")} (nightMode={_nightMode})\n" +
                $"critter.position = {c.position}\n" +
                $"root.position    = {(c.root != null ? c.root.position.ToString() : "(root null)")}\n" +
                $"velocity = {c.velocity} (mag {c.velocity.magnitude:F3})\n" +
                $"target = {c.target}  orbitAngle = {c.orbitAngle:F2}\n" +
                $"fleeing={c.fleeing} atWater={c.atWater} migrationIsDynamic={c.migrationIsDynamic}\n" +
                $"migrationPath={(c.migrationPath == null ? "null" : c.migrationPath.Length.ToString())} migrationIndex={c.migrationIndex}";
            GUI.Label(new Rect(16, 255, 448, 120), text);
        }

        private void Update()
        {
            if (_critters.Count == 0) return;
            bool isNight = _dayNight != null && _dayNight.NightAmount >= 0.55f;
            if (!_modeInitialized || isNight != _nightMode)
            {
                _modeInitialized = true;
                _nightMode = isNight;
                SetMode(_nightMode);
                _nextLightScan = 0f;
                for (int i = 0; i < _critters.Count; i++) ChooseDestination(_critters[i]);
            }

            if (_nightMode && Time.time >= _nextLightScan)
            {
                _lights = FindObjectsOfType<Light>();
                _nextLightScan = Time.time + 2f;
            }

            float dt = Time.deltaTime;
            bool hasPlayer = _player != null;
            Vector3 playerPosition = hasPlayer ? _player.transform.position : Vector3.one * 100000f;

            // Radios del enjambre (pedido puntual: "cuando se acerquen que empiecen a trabajar como
            // una" -- antes 1.3, muy chico para que dos mariposas con habitats distintos llegaran a
            // cruzarse dentro del radio con frecuencia) percepcion amplia (se ven "de a grupo" desde
            // varios pasos) pero separacion chica (personal space chico, si no se ven todas
            // apiladas en el centro).
            float neighborRadius = _cellSize * 2.2f;
            float neighborRadiusSqr = neighborRadius * neighborRadius;
            float separationRadiusSqr = (_cellSize * 0.32f) * (_cellSize * 0.32f);

            // Pedido puntual: "si el jugador anda cerca que revoloten alrededor del jugador... pero
            // si camina cerca de la cueva que la cueva tenga prioridad". Se chequea UNA vez por
            // frame (no por thinkAt) para que el foco siga al jugador en tiempo real mientras
            // camina. Solo aplica de noche (moment en que existe la migracion a la cueva con la que
            // compite) y nunca en la instancia _caveOnly (esa ya esta DENTRO de una cueva real).
            float curiosityRadiusSqr = (_cellSize * 1.8f) * (_cellSize * 1.8f);
            bool playerNearCave = false;
            if (hasPlayer && _nightMode && !_caveOnly && _caveEntrancePoint.HasValue)
            {
                Vector3 toEntrance = playerPosition - _caveEntrancePoint.Value;
                toEntrance.y = 0f;
                float caveProximityRadius = _cellSize * 3f;
                playerNearCave = toEntrance.sqrMagnitude < caveProximityRadius * caveProximityRadius;
            }

            for (int i = 0; i < _critters.Count; i++)
            {
                var critter = _critters[i];
                Vector3 flatToPlayer = playerPosition - critter.position;
                flatToPlayer.y = 0f;
                float playerDistance = flatToPlayer.magnitude;

                if (!_nightMode && playerDistance < _cellSize * 0.72f)
                {
                    if (!critter.fleeing || Time.time >= critter.thinkAt)
                    {
                        critter.fleeing = true;
                        ChooseFleeDestination(critter, playerPosition);
                        critter.thinkAt = Time.time + Random.Range(0.35f, 0.7f);
                    }
                }
                else if (critter.fleeing && playerDistance > _cellSize * 1.2f)
                {
                    critter.fleeing = false;
                    ChooseDestination(critter);
                }
                else if (Time.time >= critter.thinkAt)
                {
                    ChooseDestination(critter);
                }

                Vector3 steer = ComputeFlockSteer(i, neighborRadiusSqr, separationRadiusSqr, out int neighborCount);

                // Migracion nocturna en curso (ver ChooseDestination Prioridad 2/ComputePathTo):
                // avanza al siguiente waypoint del BFS apenas llega cerca del actual, en vez de
                // esperar al proximo thinkAt -- si no, se quedaria "pegada" varios segundos en cada
                // esquina del camino a la cueva.
                bool traveling = critter.migrationPath != null && critter.migrationIndex < critter.migrationPath.Length;
                if (traveling)
                {
                    Vector3 waypoint = critter.migrationPath[critter.migrationIndex];
                    if ((critter.position - waypoint).sqrMagnitude < 0.09f)
                    {
                        critter.migrationIndex++;
                        if (critter.migrationIndex < critter.migrationPath.Length)
                        {
                            // Mientras persigue al jugador/cueva por prioridad (migrationIsDynamic)
                            // NO toca target: ese sigue guardando el destino de fondo (arbusto/agua)
                            // al que va a volver apenas el jugador se aleje (ver bloque de foco mas
                            // abajo). Para un viaje "de verdad" (ChooseDestination) si se actualiza,
                            // como antes.
                            if (!critter.migrationIsDynamic) critter.target = critter.migrationPath[critter.migrationIndex];
                        }
                        else
                        {
                            critter.migrationPath = null;
                            traveling = false;
                        }
                    }
                }

                // Foco del frame: por defecto el que ya eligio ChooseDestination (arbusto/agua/luz/
                // punto de la migracion en curso), salvo que el jugador se meta en el medio -- ver
                // playerNearCave/curiosityRadiusSqr mas arriba. Nunca pisa una migracion en curso ya
                // activa (traveling ya era true ANTES de este bloque): una vez que arranca el viaje
                // BFS a la cueva, lo termina antes de distraerse con nada mas.
                Vector3 focus = critter.target;
                bool focusIsDynamic = false;
                if (!critter.fleeing && !traveling)
                {
                    Vector3? dynamicDestination = null;
                    if (playerNearCave)
                        dynamicDestination = new Vector3(_caveEntrancePoint.Value.x, critter.preferredHeight, _caveEntrancePoint.Value.z);
                    else if (hasPlayer && flatToPlayer.sqrMagnitude < curiosityRadiusSqr)
                        dynamicDestination = new Vector3(playerPosition.x, critter.preferredHeight, playerPosition.z);

                    if (dynamicDestination.HasValue)
                    {
                        focusIsDynamic = true;
                        // Mismo bug que el wander general (ver comentario grande en ChooseDestination):
                        // perseguir al jugador o la cueva en linea recta las dejaba empujando contra
                        // una pared para siempre -- y encima esta es la situacion en la que MAS
                        // facil pasa, porque para poder VER si se mueven hay que estar cerca, que es
                        // justo lo que activa este foco. Se refresca cada 0.6s (el jugador camina,
                        // no tiene sentido perseguir un camino viejo para siempre) o apenas se
                        // termina el camino anterior -- recalcular 256 celdas cada 0.6s es gratis.
                        if (!critter.migrationIsDynamic || critter.migrationPath == null || Time.time >= critter.nextDynamicPathRefresh)
                        {
                            critter.nextDynamicPathRefresh = Time.time + 0.6f;
                            critter.migrationIsDynamic = true;
                            critter.migrationPath = ComputePathTo(critter.position, dynamicDestination.Value, critter.preferredHeight);
                            critter.migrationIndex = 0;
                        }
                        if (critter.migrationPath != null && critter.migrationPath.Length > 0)
                        {
                            traveling = true;
                            focus = critter.migrationPath[Mathf.Min(critter.migrationIndex, critter.migrationPath.Length - 1)];
                        }
                        else
                        {
                            focus = dynamicDestination.Value; // sin camino real: al menos linea recta.
                        }
                    }
                }

                // Revoloteo (pedido puntual: "borra lo de estar quietas" -- el offset gira SIEMPRE,
                // sin depender de thinkAt ni de nada del jugador, asi el punto que en verdad
                // persigue Update nunca deja de moverse) con estilo distinto segun el tipo de foco:
                // agua "juega arriba" con un vaiven vertical marcado y un circulo mas ancho (vuelo
                // rasante), arbusto/luz/jugador/cueva un circulo chato normal. Suprimido solo
                // mientras viaja (traveling) o huye (fleeing): ahi el objetivo es llegar derecho.
                critter.orbitAngle += critter.orbitSpeed * dt;
                bool useWaterStyle = critter.atWater && !focusIsDynamic && !critter.fleeing && !traveling;
                Vector3 orbitOffset;
                if (critter.fleeing || traveling)
                {
                    orbitOffset = Vector3.zero;
                }
                else if (useWaterStyle)
                {
                    orbitOffset = new Vector3(
                        Mathf.Cos(critter.orbitAngle) * critter.orbitRadius * 1.6f,
                        Mathf.Sin(critter.orbitAngle * 1.7f) * 0.3f,
                        Mathf.Sin(critter.orbitAngle) * critter.orbitRadius * 1.6f);
                }
                else
                {
                    orbitOffset = new Vector3(Mathf.Cos(critter.orbitAngle), 0f, Mathf.Sin(critter.orbitAngle)) * critter.orbitRadius;
                }
                Vector3 chaseTarget = focus + orbitOffset;

                // Huyendo del jugador, ignora al resto del enjambre -- no dejarse pisar importa mas
                // que mantener la formacion. Sin huir, cuantos mas vecinos cerca, MENOS pesa el
                // objetivo propio frente al steer del grupo (pedido puntual: "que empiecen a
                // trabajar como una") -- con 2 vecinos el objetivo individual ya pesa la mitad, asi
                // que de cerca el grupo domina en vez de que cada una tire para su lado como si las
                // otras no existieran.
                float maxSpeed = critter.speed;
                Vector3 seek = chaseTarget - critter.position;
                Vector3 desiredVelocity = seek.sqrMagnitude > 0.0001f ? seek.normalized * maxSpeed : Vector3.zero;
                float seekGain = 1.6f / (1f + neighborCount * 0.5f);
                Vector3 acceleration = critter.fleeing
                    ? (desiredVelocity - critter.velocity) * 3f
                    : (desiredVelocity - critter.velocity) * seekGain + steer;

                critter.velocity += acceleration * dt;
                if (critter.velocity.sqrMagnitude > maxSpeed * maxSpeed)
                    critter.velocity = critter.velocity.normalized * maxSpeed;

                Vector3 proposed = critter.position + critter.velocity * dt;
                proposed = ResolveWallCollision(critter.position, proposed);
                critter.position = proposed;
                if (_hasBounds)
                {
                    critter.position.x = Mathf.Clamp(critter.position.x, _boundsMin.x, _boundsMax.x);
                    critter.position.z = Mathf.Clamp(critter.position.z, _boundsMin.z, _boundsMax.z);
                }
                // Seguro de altura (pedido puntual: "su unica limitacion sea no salir del alto de
                // la pared"): el seek/steer normalmente ya converge solo a preferredHeight, esto es
                // el limite duro por si un empujon de separacion los manda momentaneamente fuera
                // de rango.
                critter.position.y = Mathf.Clamp(critter.position.y, 0.3f, _wallHeight - 0.3f);

                float bob = Mathf.Sin(Time.time * 4.2f + critter.phase) * (_nightMode ? 0.09f : 0.16f);
                critter.root.position = critter.position + Vector3.up * bob;
                if (critter.velocity.sqrMagnitude > 0.0001f)
                    critter.root.rotation = Quaternion.Slerp(critter.root.rotation, Quaternion.LookRotation(critter.velocity), dt * 5f);

                // Aleteo (pedido puntual: "por lo menos las alas deben agitarse, constante
                // movimiento"). BUG encontrado reportado como "siguen quietas" incluso con todo lo
                // demas andando: el ala es un disco achatado (escala 0.17 x 0.22 x 0.025, la cara
                // ancha mira al eje Z) y esto rotaba en Z -- el eje NORMAL a la propia cara del
                // disco. Girar un disco casi circular sobre su propio eje normal casi no cambia la
                // silueta vista desde afuera, para NINGUN angulo de camara: matematicamente el
                // angulo cambiaba cada frame, pero visualmente no se notaba, lo cual se leia como
                // "estatico". Ahora rota en X (un eje que SI esta contenido en el plano del ala):
                // la altura del ala (0.22) se escorza hacia su grosor (0.025) al girar, un cambio de
                // silueta imposible de no ver. Mismo angulo en las dos alas (no espejado): suben y
                // bajan juntas, como un libro abriendose y cerrandose sobre el lomo del cuerpo.
                float flapPhase = Time.time * (_nightMode ? 12f : 17f) + critter.phase;
                float flap = Mathf.Sin(flapPhase) * (_nightMode ? 42f : 55f);
                critter.leftWing.localRotation = Quaternion.Euler(flap, 0f, 0f);
                critter.rightWing.localRotation = Quaternion.Euler(flap, 0f, 0f);

                float wingPulse = 1f + Mathf.Abs(Mathf.Sin(flapPhase)) * 0.35f;
                Vector3 baseWingScale = _nightMode ? new Vector3(0.12f, 0.16f, 0.025f) : new Vector3(0.17f, 0.22f, 0.025f);
                critter.leftWing.localScale = baseWingScale * wingPulse;
                critter.rightWing.localScale = baseWingScale * wingPulse;
            }
        }

        // Separacion + alineacion + cohesion contra el resto del enjambre (ver campo velocity de
        // Critter): O(n^2) contra _critters, pero son a lo sumo 8 bichos por piso (ver Initialize),
        // nada que valga la pena optimizar con una grilla espacial. neighborCount sale por out para
        // que Update tambien pueda bajar el peso del objetivo individual cuando hay vecinos cerca
        // (ver seekGain) -- ahi es donde de verdad se nota "trabajar como una", no solo en estos
        // pesos de separacion/alineacion/cohesion.
        private Vector3 ComputeFlockSteer(int index, float neighborRadiusSqr, float separationRadiusSqr, out int neighborCount)
        {
            var critter = _critters[index];
            Vector3 separation = Vector3.zero;
            Vector3 avgVelocity = Vector3.zero;
            Vector3 avgPosition = Vector3.zero;
            neighborCount = 0;
            for (int j = 0; j < _critters.Count; j++)
            {
                if (j == index) continue;
                var other = _critters[j];
                Vector3 offset = critter.position - other.position;
                float distSqr = offset.sqrMagnitude;
                if (distSqr > neighborRadiusSqr || distSqr < 0.0001f) continue;
                neighborCount++;
                avgVelocity += other.velocity;
                avgPosition += other.position;
                if (distSqr < separationRadiusSqr)
                    separation += offset / distSqr;
            }
            if (neighborCount == 0) return separation * 2.2f;

            Vector3 alignment = (avgVelocity / neighborCount) - critter.velocity;
            Vector3 cohesion = (avgPosition / neighborCount) - critter.position;
            return separation * 2.2f + alignment * 1.1f + cohesion * 0.9f;
        }

        // Frena el movimiento propuesto en el eje que cruzaria un muro real (DungeonCell.HasWall,
        // mismo dato que usa el resto del juego para colisiones -- ver GridPlayerController/
        // CastleMouseAmbient) -- pedido puntual: "tampoco pueden atravesarlas". Los ejes se resuelven
        // por separado (X primero, Z con la celda X ya resuelta) para poder "deslizar" contra la
        // pared en vez de trabarse en seco si el movimiento viene en diagonal. A proposito NO trata
        // las celdas Void (lagunas) como bloqueadas: son destino de vuelo valido (ver
        // _wildlifeWaterPoints en DungeonLevelBuilder), no paredes.
        private Vector3 ResolveWallCollision(Vector3 oldPos, Vector3 newPos)
        {
            if (_floor == null) return newPos;
            int cxOld = Mathf.RoundToInt(oldPos.x / _cellSize);
            int cyOld = Mathf.RoundToInt(oldPos.z / _cellSize);
            if (!_floor.InBounds(cxOld, cyOld)) return newPos;
            var cell = _floor.Cells[cxOld, cyOld];

            int cxNew = Mathf.RoundToInt(newPos.x / _cellSize);
            if (cxNew != cxOld)
            {
                Direction dir = cxNew > cxOld ? Direction.East : Direction.West;
                var (dx, dy) = dir.Offset();
                bool blocked = cell.HasWall(dir) || !_floor.InBounds(cxOld + dx, cyOld + dy);
                if (blocked)
                {
                    float edge = (cxOld + (dir == Direction.East ? 0.5f : -0.5f)) * _cellSize;
                    newPos.x = dir == Direction.East ? edge - 0.3f : edge + 0.3f;
                }
            }

            int cxResolved = Mathf.RoundToInt(newPos.x / _cellSize);
            if (_floor.InBounds(cxResolved, cyOld))
            {
                var cellForZ = _floor.Cells[cxResolved, cyOld];
                int cyNew = Mathf.RoundToInt(newPos.z / _cellSize);
                if (cyNew != cyOld)
                {
                    Direction dir = cyNew > cyOld ? Direction.North : Direction.South;
                    var (dx, dy) = dir.Offset();
                    bool blocked = cellForZ.HasWall(dir) || !_floor.InBounds(cxResolved + dx, cyOld + dy);
                    if (blocked)
                    {
                        float edge = (cyOld + (dir == Direction.North ? 0.5f : -0.5f)) * _cellSize;
                        newPos.z = dir == Direction.North ? edge - 0.3f : edge + 0.3f;
                    }
                }
            }

            return newPos;
        }

        // BFS de celda a celda contra las paredes reales (mismo dato/criterio que
        // CastleMouseAmbient.Initialize: from.HasWall(dir) || to.HasWall(dir.Opposite())) --
        // NO excluye celdas Void a proposito (a diferencia de CastleMouseAmbient, que si las evita:
        // un raton no puede nadar, pero una polilla vuela por encima de la laguna igual, ver
        // _wildlifeWaterPoints). Devuelve los centros de celda (Y = height, la altura propia del
        // bicho que pregunta) del camino, o null si no hay uno (piso desconectado / fuera de rango).
        private Vector3[] ComputePathTo(Vector3 fromWorld, Vector3 toWorld, float height)
        {
            if (_floor == null) return null;
            int sx = Mathf.RoundToInt(fromWorld.x / _cellSize);
            int sy = Mathf.RoundToInt(fromWorld.z / _cellSize);
            int gx = Mathf.RoundToInt(toWorld.x / _cellSize);
            int gy = Mathf.RoundToInt(toWorld.z / _cellSize);
            if (!_floor.InBounds(sx, sy) || !_floor.InBounds(gx, gy)) return null;
            if (sx == gx && sy == gy) return new[] { new Vector3(gx * _cellSize, height, gy * _cellSize) };

            int w = _floor.Width, h = _floor.Height;
            var visited = new bool[w, h];
            var cameFromX = new int[w, h];
            var cameFromY = new int[w, h];
            var queue = new Queue<(int x, int y)>();
            visited[sx, sy] = true;
            queue.Enqueue((sx, sy));
            bool found = false;
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                if (cx == gx && cy == gy) { found = true; break; }
                var cell = _floor.Cells[cx, cy];
                foreach (var dir in DirectionExtensions.All)
                {
                    var (dx, dy) = dir.Offset();
                    int nx = cx + dx, ny = cy + dy;
                    if (!_floor.InBounds(nx, ny) || visited[nx, ny]) continue;
                    if (cell.HasWall(dir) || _floor.Cells[nx, ny].HasWall(dir.Opposite())) continue;
                    visited[nx, ny] = true;
                    cameFromX[nx, ny] = cx;
                    cameFromY[nx, ny] = cy;
                    queue.Enqueue((nx, ny));
                }
            }
            if (!found) return null;

            var reversed = new List<Vector3>();
            int px = gx, py = gy;
            int guard = w * h + 4;
            while ((px != sx || py != sy) && guard-- > 0)
            {
                reversed.Add(new Vector3(px * _cellSize, height, py * _cellSize));
                int tx = cameFromX[px, py], ty = cameFromY[px, py];
                px = tx;
                py = ty;
            }
            reversed.Reverse();
            return reversed.Count > 0 ? reversed.ToArray() : null;
        }

        private float RandomFlightHeight() => Random.Range(_minFlightHeight, _maxFlightHeight);

        private Critter CreateCritter(int index, Vector3 position)
        {
            float preferredHeight = RandomFlightHeight();
            position.y = preferredHeight;
            var root = new GameObject(_caveOnly ? $"Moth_{index}" : $"Butterfly_{index}");
            root.transform.SetParent(transform, false);
            root.transform.position = position;

            CreatePart(root.transform, "Body", new Vector3(0f, 0f, 0f), new Vector3(0.045f, 0.18f, 0.045f), _bodyMaterial);
            var left = CreatePart(root.transform, "LeftWing", new Vector3(-0.095f, 0.035f, 0f), new Vector3(0.17f, 0.22f, 0.025f), _butterflyMaterials[index % _butterflyMaterials.Length]);
            var right = CreatePart(root.transform, "RightWing", new Vector3(0.095f, 0.035f, 0f), new Vector3(0.17f, 0.22f, 0.025f), _butterflyMaterials[index % _butterflyMaterials.Length]);

            return new Critter
            {
                root = root.transform,
                leftWing = left.transform,
                rightWing = right.transform,
                leftRenderer = left.GetComponent<Renderer>(),
                rightRenderer = right.GetComponent<Renderer>(),
                position = position,
                velocity = Vector3.zero,
                preferredHeight = preferredHeight,
                orbitAngle = Random.value * Mathf.PI * 2f,
                orbitRadius = Random.Range(0.25f, 0.5f),
                orbitSpeed = Random.Range(1.4f, 2.4f),
                phase = Random.value * Mathf.PI * 2f,
                speed = Random.Range(0.55f, 0.9f),
                thinkAt = Time.time + Random.Range(0.5f, 1.5f),
            };
        }

        private static GameObject CreatePart(Transform parent, string partName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            return part;
        }

        private void SetMode(bool night)
        {
            for (int i = 0; i < _critters.Count; i++)
            {
                var critter = _critters[i];
                Material wingMaterial = night ? _mothMaterial : _butterflyMaterials[i % _butterflyMaterials.Length];
                critter.leftRenderer.sharedMaterial = wingMaterial;
                critter.rightRenderer.sharedMaterial = wingMaterial;
                Vector3 wingScale = night ? new Vector3(0.12f, 0.16f, 0.025f) : new Vector3(0.17f, 0.22f, 0.025f);
                critter.leftWing.localScale = wingScale;
                critter.rightWing.localScale = wingScale;
                critter.root.gameObject.SetActive(!_caveOnly || night);
                critter.fleeing = false;
            }
        }

        private void ChooseDestination(Critter critter)
        {
            critter.thinkAt = Time.time + Random.Range(_nightMode ? 1.2f : 0.8f, _nightMode ? 2.6f : 1.8f);
            // Radio/velocidad de la orbita (ver Update) se re-sortean en cada punto de interes
            // nuevo, no solo en CreateCritter -- variedad entre paradas sin volver a un offset
            // estatico grande. "Puntos de interes mas pequeños" (pedido puntual): 0.25-0.5, mismo
            // rango chico que CreateCritter, contra el 0.6 suelto de antes.
            critter.orbitRadius = Random.Range(0.25f, 0.5f);
            critter.orbitSpeed = Random.Range(1.4f, 2.4f);

            if (_nightMode)
            {
                // Prioridad 1: una luz de verdad ya cerca (torch real, ver FindClosestLocalLight) --
                // una vez que llegan a la cueva, esto las hace revolotear alrededor de la antorcha
                // puntual en vez de quedarse clavadas en el centro de la mini-cueva.
                Light closest = FindClosestLocalLight(critter.root.position);
                if (closest != null)
                {
                    critter.migrationPath = null; // ya llegamos -- no hace falta seguir el viaje.
                    critter.migrationIsDynamic = false;
                    critter.atWater = false;
                    float lightY = Mathf.Clamp(closest.transform.position.y, 0.3f, _wallHeight - 0.3f);
                    critter.target = new Vector3(closest.transform.position.x, lightY, closest.transform.position.z);
                    critter.speed = Random.Range(0.45f, 0.75f);
                    return;
                }
                // Prioridad 2 (pedido puntual: "de noche... deben dirigirse a la cueva donde en la
                // entrada hay antorchas"): sin luz real cerca todavia, migran a la entrada de la
                // mini-cueva por un camino real (ComputePathTo, BFS contra las paredes) -- a
                // diferencia de FindClosestLocalLight (radio corto a proposito, ver su comentario)
                // esto SI cruza el piso entero, y a diferencia de apuntar directo en linea recta
                // (lo que se probo primero) no se traba en la primera esquina del laberinto.
                if (!_caveOnly && _caveEntrancePoint.HasValue)
                {
                    // migrationIsDynamic en la condicion: si el camino actual era una persecucion
                    // del jugador (ver Update), no sirve para esto aunque todavia tenga waypoints
                    // sin consumir -- hay que recalcular uno de verdad hacia la cueva.
                    bool needsPath = critter.migrationIsDynamic || critter.migrationPath == null || critter.migrationIndex >= critter.migrationPath.Length;
                    if (needsPath)
                    {
                        critter.migrationIsDynamic = false;
                        critter.migrationPath = ComputePathTo(critter.position, _caveEntrancePoint.Value, critter.preferredHeight);
                        critter.migrationIndex = 0;
                    }
                    if (critter.migrationPath != null && critter.migrationPath.Length > 0)
                    {
                        critter.atWater = false;
                        critter.target = critter.migrationPath[critter.migrationIndex];
                        critter.speed = Random.Range(0.5f, 0.8f);
                        return;
                    }
                    // BFS sin camino (piso desconectado / entrada inalcanzable desde aca): sigue
                    // abajo al wander normal en vez de trabarse esperando un camino que no existe.
                }
            }
            else
            {
                critter.migrationPath = null; // de dia no se migra -- no dejar un camino viejo activo.
            }

            Vector3 here = critter.root.position;
            int selected = Random.Range(0, _habitats.Count);
            float bestDistance = float.MaxValue;
            for (int attempt = 0; attempt < Mathf.Min(_habitats.Count, 5); attempt++)
            {
                int candidate = Random.Range(0, _habitats.Count);
                float distance = (here - _habitats[candidate].pos).sqrMagnitude;
                if (distance < 0.25f * _cellSize * _cellSize) continue;
                if (distance < bestDistance)
                {
                    selected = candidate;
                    bestDistance = distance;
                }
            }
            // Re-sorteada en cada punto de interes nuevo (pedido puntual: "que no esten a la altura
            // de la camara", ver RandomFlightHeight) -- asi ademas de cambiar de arbusto tambien
            // cambian de altura de vuelo cada tanto, en vez de quedar en una sola altura toda la vida.
            critter.preferredHeight = RandomFlightHeight();
            var chosenHabitat = _habitats[selected];
            critter.atWater = chosenHabitat.isWater;
            Vector3 destination = new Vector3(chosenHabitat.pos.x, critter.preferredHeight, chosenHabitat.pos.z);

            // BUG real detras de "siguen quietas" (confirmado por el reporte "se hizo tp al abrir
            // una puerta"): el arbusto/agua elegido arriba se escogia solo por distancia en linea
            // recta, sin chequear si hay un camino de verdad hasta ahi. Si quedaba del OTRO LADO de
            // una pared, el seek empujaba contra esa pared cuadro tras cuadro y ResolveWallCollision
            // la clavaba justo en el borde -- desplazamiento neto CERO, se leia como "estatica"
            // aunque las fuerzas se siguieran calculando. Al abrir una puerta esa pared desaparecia
            // y todo el empuje acumulado se soltaba de golpe en un solo frame: el "teletransporte"
            // reportado. Mismo BFS que la migracion a la cueva (ComputePathTo), reusando
            // migrationPath/migrationIndex como camino generico, no exclusivo de la cueva: primero
            // caminan la ruta real, y una vez que llegan recien ahi arranca el revoloteo/orbita.
            critter.migrationIsDynamic = false;
            critter.migrationPath = ComputePathTo(critter.position, destination, critter.preferredHeight);
            critter.migrationIndex = 0;
            critter.target = (critter.migrationPath != null && critter.migrationPath.Length > 0)
                ? critter.migrationPath[0]
                : destination; // sin camino real (fuera de rango/piso desconectado): al menos apunta directo.
            critter.speed = _nightMode ? Random.Range(0.45f, 0.75f) : Random.Range(0.55f, 0.9f);
        }

        private void ChooseFleeDestination(Critter critter, Vector3 playerPosition)
        {
            int selected = -1;
            float bestDistance = -1f;
            for (int i = 0; i < _habitats.Count; i++)
            {
                float fromPlayer = (_habitats[i].pos - playerPosition).sqrMagnitude;
                float fromCritter = (_habitats[i].pos - critter.root.position).sqrMagnitude;
                if (fromCritter > _cellSize * _cellSize * 9f) continue;
                if (fromPlayer > bestDistance)
                {
                    selected = i;
                    bestDistance = fromPlayer;
                }
            }
            if (selected >= 0)
            {
                Vector3 chosen = _habitats[selected].pos;
                critter.target = new Vector3(chosen.x, critter.preferredHeight, chosen.z);
            }
            else
            {
                Vector3 away = critter.root.position - playerPosition;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                Vector3 fleeTarget = critter.root.position + away.normalized * _cellSize * 1.5f;
                critter.target = new Vector3(fleeTarget.x, critter.preferredHeight, fleeTarget.z);
            }
            critter.speed = Random.Range(2.2f, 3.2f);
        }

        private Light FindClosestLocalLight(Vector3 position)
        {
            Light best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _lights.Length; i++)
            {
                Light light = _lights[i];
                if (light == null || !light.isActiveAndEnabled || light.type == LightType.Directional || light.intensity < 0.05f)
                    continue;
                float distance = (light.transform.position - position).sqrMagnitude;
                // Busca luz local; no cruza el mapa ni atraviesa paredes persiguiendo una antorcha lejana.
                if (distance > _cellSize * _cellSize * 16f) continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = light;
                }
            }
            return best;
        }

        private static void EnsureMaterials()
        {
            if (_butterflyMaterials != null) return;
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            _butterflyMaterials = new Material[ButterflyColors.Length];
            for (int i = 0; i < ButterflyColors.Length; i++)
                _butterflyMaterials[i] = CreateMaterial(shader, ButterflyColors[i]);
            _mothMaterial = CreateMaterial(shader, new Color(0.72f, 0.78f, 0.86f));
            _bodyMaterial = CreateMaterial(shader, new Color(0.14f, 0.12f, 0.1f));
        }

        private static Material CreateMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.12f);
            return material;
        }
    }
}
