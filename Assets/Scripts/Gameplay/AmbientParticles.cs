using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DungeonGen;

namespace Gameplay
{
    // Particulas de ambiente en la mazmorra, para dar dinamismo y sensacion de profundidad
    // mientras se explora. Antes ciclaba entre 3 "biomas" de color segun el piso (Polvo/Hojas/
    // Brasas, piso % 3): en la practica varios de esos tonos se leian amarillentos y NO daban la
    // sensacion de misterio buscada. Ahora el motivo es SIEMPRE el mismo polvo blanco/celeste palido
    // en TODOS los pisos normales (consistente, no cambia con el piso), reforzado por una tercera
    // capa de motas que titilan (el "algo mas" aparte del blanco liso) para que se sienta con vida
    // sin necesitar variar el color. Lo UNICO que cambia de verdad es la sala de JEFE: ahi el motivo
    // se reemplaza por una tormenta electrica (rayos + chispas), bien distinta del resto, para que
    // el jugador sienta que algo mas peligroso esta cerca apenas entra.
    //
    // 4 elementos (como el fondo de un escenario tipo Tekken, mas la tormenta del jefe):
    //  - Cerca: motas nitidas y relativamente rapidas, en un volumen que sigue la posicion Y LA
    //    DIRECCION en la que mira la camara (no solo la posicion) -- asi siempre hay algo
    //    flotando mas o menos adelante, sin importar hacia donde se gire, en vez de quedar
    //    esparcidas al azar y "perderse" cuando el jugador da vuelta. Si esa direccion apunta
    //    contra una pared, las particulas quedan naturalmente detras de ella (tapadas por el
    //    z-buffer, igual que cualquier objeto transparente) -- no hace falta logica extra para eso.
    //  - Lejos: un volumen mucho mas grande de particulas grandes, tenues y lentas (neblina/calina)
    //    que solo sigue la posicion, dando la sensacion de que el fondo se pierde en la distancia.
    //  - Destellos: motas dispersas y MUY lentas cuyo brillo titila varias veces durante su vida
    //    (colorOverLifetime con varios picos de alpha, no solo un fade in/out) -- como luciernagas o
    //    polvo magico en la oscuridad, el detalle que le da presencia al blanco sin volverlo un
    //    color distinto por piso.
    //  - Tormenta (solo sala de jefe): relampagos reales (flash de Light + rafaga de chispas
    //    electricas) a intervalos random, ver StormRoutine.
    public class AmbientParticles : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public Transform followTarget;

        // Cuanto se adelanta el volumen "cerca" en la direccion en la que mira la camara.
        private const float NearForwardOffset = 1.1f;

        // Las hojas nacen 2 CASILLAS adelante del jugador (cellSize=4, ver DungeonSettings) en vez
        // de esparcidas en una caja grande centrada arriba suyo -- asi se ven aparecer en un punto
        // mas o menos fijo adelante, en vez de perderse como ambiente difuso por todos lados.
        private const float LeafForwardOffset = 8f;

        private static readonly Color MysteryNearColor = new Color(0.85f, 0.9f, 0.95f, 0.4f);
        private static readonly Color MysteryFarColor = new Color(0.55f, 0.62f, 0.7f, 0.09f);
        private static readonly Color StormNearColor = new Color(0.6f, 0.68f, 0.85f, 0.4f);
        private static readonly Color StormFarColor = new Color(0.18f, 0.2f, 0.3f, 0.16f);
        private static readonly Color LightShaftColor = new Color(1f, 0.93f, 0.72f);
        private static readonly Color LightningColor = new Color(0.75f, 0.85f, 1f);
        private static readonly Color ShootingStarColor = new Color(0.9f, 0.93f, 1f);

        // Radio (en celdas, distancia Chebyshev -- permite diagonal) dentro del cual la tormenta ya
        // se activa aunque el jugador todavia no haya PISADO la sala de jefe: asi las luces/rayos
        // se ven desde un pasillo cercano, de afuera, como aviso de que hay algo peligroso ahi.
        private const int BossProximityRadius = 4;

        private ParticleSystem _near;
        private ParticleSystem _far;
        private ParticleSystem _wisps;
        // Hojas cayendo (ver ConfigureLeafLayer): solo en el Bioma 1 (el "normal", con plantas --
        // el Bioma 2 es la cueva intergalactica con cielo estrellado, no pega tematicamente ahi),
        // toggleada junto con la estrella fugaz en el mismo chequeo de inBiome2 en LateUpdate.
        private ParticleSystem _leaves;
        private static readonly Color LeafColorA = new Color(0.62f, 0.42f, 0.14f); // ocre/naranja seco
        private static readonly Color LeafColorB = new Color(0.45f, 0.5f, 0.16f); // verde oliva
        // Rayos de luz de bosque (ver ConfigureLightShaftLayer): pocos, grandes y casi quietos --
        // MISMA condicion de Bioma 1 que las hojas (se togglean juntos en LateUpdate).
        private ParticleSystem _lightShafts;
        // Sistema dedicado para el rayo: renderMode Stretch (particulas "estiradas" segun su
        // velocidad, se ven como rayas/lineas) en vez del Billboard redondo de _wisps -- antes el
        // rayo se armaba con puntitos redondos de _wisps, que no se leian como un relampago de
        // verdad por mas que se los encadenara en zigzag.
        private ParticleSystem _lightningStreaks;
        private Light _stormLight;
        private Coroutine _stormRoutine;
        private bool _lastNearBossRoom;
        private bool _biomeInitialized;
        private Vector3 _bossRoomAnchor;

        // Estrella fugaz (techo de Bioma 2, ver Custom/StarrySky): mismo mecanismo Stretch+Light que
        // el rayo de la sala de jefe arriba, pero cruzando el techo cerca del jugador de vez en
        // cuando en vez de quedarse fija en un punto -- ver ShootingStarRoutine.
        private ParticleSystem _starStreaks;
        private Light _starLight;
        private Coroutine _shootingStarRoutine;
        private bool _lastInBiome2;
        private const float ShootingStarPeakIntensity = 2.2f;

        void Awake()
        {
            _near = ParticleLayerFactory.CreateLayer(transform, "AmbientNear");
            _far = ParticleLayerFactory.CreateLayer(transform, "AmbientFar");
            _wisps = ParticleLayerFactory.CreateLayer(transform, "AmbientWisps");
            _lightningStreaks = ParticleLayerFactory.CreateLayer(transform, "LightningStreaks");

            ConfigureCommon(_near, maxParticles: 220, boxScale: new Vector3(2.6f, 2.2f, 2.6f));
            ConfigureCommon(_far, maxParticles: 120, boxScale: new Vector3(16f, 7f, 16f));
            ConfigureCommon(_wisps, maxParticles: 40, boxScale: new Vector3(5f, 2.6f, 5f));
            ConfigureWispFlicker(_wisps);
            ConfigureStreakLayer(_lightningStreaks);

            _leaves = ParticleLayerFactory.CreateLayer(transform, "AmbientLeaves");
            _leaves.GetComponent<ParticleSystemRenderer>().material = ParticleTextureFactory.LeafMaterial;
            ConfigureLeafLayer(_leaves);

            _lightShafts = ParticleLayerFactory.CreateLayer(transform, "AmbientLightShafts");
            _lightShafts.GetComponent<ParticleSystemRenderer>().material = ParticleTextureFactory.BeamMaterial;
            ConfigureLightShaftLayer(_lightShafts);

            var stormGo = new GameObject("StormLight");
            stormGo.transform.SetParent(transform, false);
            _stormLight = stormGo.AddComponent<Light>();
            _stormLight.type = LightType.Point;
            _stormLight.color = LightningColor;
            _stormLight.range = 20f;
            _stormLight.intensity = 0f;
            _stormLight.shadows = LightShadows.None;

            _starStreaks = ParticleLayerFactory.CreateLayer(transform, "ShootingStarStreaks");
            ConfigureStreakLayer(_starStreaks);

            var starGo = new GameObject("ShootingStarLight");
            starGo.transform.SetParent(transform, false);
            _starLight = starGo.AddComponent<Light>();
            _starLight.type = LightType.Point;
            _starLight.color = ShootingStarColor;
            _starLight.range = 10f; // ilumina un poco alrededor del jugador, no toda la sala
            _starLight.intensity = 0f;
            _starLight.shadows = LightShadows.None;

            ApplyBiome(nearBossRoom: false);

            ParticleLayerFactory.Activate(_near);
            ParticleLayerFactory.Activate(_far);
            ParticleLayerFactory.Activate(_wisps);
            ParticleLayerFactory.Activate(_lightningStreaks);
            ParticleLayerFactory.Activate(_starStreaks);
            ParticleLayerFactory.Activate(_leaves);
            ParticleLayerFactory.Activate(_lightShafts);
        }

        // Rayos de luz filtrandose desde el techo, tipo luz de bosque entre las copas: pocas
        // franjas (ConfigureLightShaftLayer/Beam en ParticleTextureFactory) grandes, casi quietas
        // (sin gravedad, con un vaiven organico chico via Noise en vez de caer), que aparecen y se
        // desvanecen despacio (colorOverLifetime) en vez de aparecer/desaparecer de golpe.
        // startSize3D permite estirarlas mucho en Y (altas, como una columna de luz) sin ensanchar
        // en X -- la textura Beam en si ya es angosta al centro, esto la hace ademas bien alta.
        private void ConfigureLightShaftLayer(ParticleSystem ps)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 10;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startColor = new Color(LightShaftColor.r, LightShaftColor.g, LightShaftColor.b, 0.1f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startRotation = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(2.6f, 3.4f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(10f, 0.2f, 10f);

            var emission = ps.emission;
            emission.rateOverTime = 0.12f; // muy poco a poco: son columnas grandes, no hace falta que sean muchas

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.06f;
            noise.frequency = 0.08f;
            noise.scrollSpeed = 0.05f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
        }

        // Caja fina cerca del techo (altura de pared ~3, ver DungeonSettings.wallHeight) de la que
        // "nacen" las hojas, cayendo con velocidad vertical fija (no gravityModifier -- asi cae
        // siempre a un ritmo parejo y lento, sin importar el peso/tamaño de la particula) mas un
        // vaiven horizontal con Noise (turbulencia con textura Perlin) para que floten de costado
        // en vez de caer en linea recta, y un giro continuo (rotationOverLifetime) para que
        // tambaleen como una hoja de verdad. El color sale de 2 tonos otoño (ver LeafColorA/B) via
        // startColor en modo "random entre 2 colores", nunca de la textura (Leaf es blanca).
        private void ConfigureLeafLayer(ParticleSystem ps)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 60;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            main.startColor = new ParticleSystem.MinMaxGradient(LeafColorA, LeafColorB);

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(4f, 0.3f, 4f);

            var emission = ps.emission;
            emission.rateOverTime = 3.5f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.35f, -0.22f); // caida lenta y pareja
            vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.2f;

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-60f * Mathf.Deg2Rad, 60f * Mathf.Deg2Rad);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = gradient;
        }

        // Sistema SOLO para rafagas por Emit() (el rayo y sus chispas): sin emision ambiente propia
        // (rateOverTime en 0) y en modo Stretch, que dibuja cada particula como una raya orientada
        // segun SU velocidad -- eso es lo que hace que se vea como un relampago/chispazo en vez de
        // puntos sueltos.
        private void ConfigureStreakLayer(ParticleSystem ps)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 200;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4.5f;
            renderer.velocityScale = 0.16f;
        }

        private void ConfigureCommon(ParticleSystem ps, int maxParticles, Vector3 boxScale)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = maxParticles;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxScale;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
        }

        // Pulso de brillo real (no un simple fade in/out): el alpha sube y baja varias veces
        // durante la vida de cada particula, como si titilara -- eso es lo que las distingue del
        // polvo comun y les da el "aire de misterio" pedido.
        private void ConfigureWispFlicker(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.1f, 0.32f),
                    new GradientAlphaKey(1f, 0.52f),
                    new GradientAlphaKey(0.1f, 0.72f),
                    new GradientAlphaKey(0.8f, 0.88f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = gradient;
        }

        void LateUpdate()
        {
            if (followTarget != null)
            {
                _far.transform.position = followTarget.position;
                _near.transform.position = followTarget.position + followTarget.forward * NearForwardOffset;
                _wisps.transform.position = followTarget.position;
                // 2 casillas adelante (LeafForwardOffset) Y cerca del techo (wallHeight ~3, ver
                // DungeonSettings) para que las hojas tengan recorrido de sobra antes de
                // "tocar piso" (en realidad nunca chocan de verdad, solo se desvanecen por
                // colorOverLifetime -- ver ConfigureLeafLayer).
                _leaves.transform.position = followTarget.position + followTarget.forward * LeafForwardOffset + Vector3.up * 2.6f;
                // A media altura de pared (~1.5, wallHeight=3) para que la columna estirada
                // (startSizeY 2.6-3.4) llegue comoda de casi el techo a casi el piso.
                _lightShafts.transform.position = followTarget.position + Vector3.up * 1.5f;
            }
            if (dungeonManager == null || player == null || !dungeonManager.IsReady) return;

            var floor = dungeonManager.CurrentFloor;
            bool nearBossRoom = false;
            if (floor != null && floor.HasBossRoom)
            {
                // El ancla de la tormenta (luz + rayos) es la sala del jefe, NO el jugador: asi el
                // relampago se ve desde afuera (un pasillo cercano) tal cual es, en vez de
                // "seguirte" a donde vayas -- ademas la luz no tiene sombras (LightShadows.None),
                // asi que se filtra por las paredes cercanas como un buen indicio de peligro.
                _bossRoomAnchor = dungeonManager.CellToWorld(floor.BossPos.x, floor.BossPos.y) + Vector3.up * 1.6f;
                _stormLight.transform.position = _bossRoomAnchor;

                if (floor.InBounds(player.CellX, player.CellY))
                    nearBossRoom = IsNearBossRoom(floor, player.CellX, player.CellY);
            }

            if (!_biomeInitialized || nearBossRoom != _lastNearBossRoom)
            {
                _biomeInitialized = true;
                _lastNearBossRoom = nearBossRoom;
                ApplyBiome(nearBossRoom);

                if (nearBossRoom)
                {
                    if (_stormRoutine == null) _stormRoutine = StartCoroutine(StormRoutine());
                }
                else if (_stormRoutine != null)
                {
                    StopCoroutine(_stormRoutine);
                    _stormRoutine = null;
                    _stormLight.intensity = 0f;
                }
            }

            // Estrella fugaz: activa en CUALQUIER piso del Bioma 2 (todo el bioma comparte el techo
            // Custom/StarrySky), no solo cerca de algo puntual como la tormenta de la sala de jefe.
            bool inBiome2 = floor != null && floor.Biome != 0;
            if (inBiome2 != _lastInBiome2)
            {
                _lastInBiome2 = inBiome2;
                if (inBiome2)
                {
                    if (_shootingStarRoutine == null) _shootingStarRoutine = StartCoroutine(ShootingStarRoutine());
                }
                else if (_shootingStarRoutine != null)
                {
                    StopCoroutine(_shootingStarRoutine);
                    _shootingStarRoutine = null;
                    _starLight.intensity = 0f;
                }

                // Hojas y rayos de luz SOLO en Bioma 1 (con plantas) -- en la cueva intergalactica
                // (Bioma 2, cielo estrellado) no pegan tematicamente, se apagan del todo en vez de
                // seguir cayendo/brillando.
                var leafEmission = _leaves.emission;
                leafEmission.enabled = !inBiome2;
                var shaftEmission = _lightShafts.emission;
                shaftEmission.enabled = !inBiome2;
            }
        }

        // Distancia Chebyshev (la que importa en una grilla con movimiento en 8 direcciones/vision)
        // del jugador a la celda MAS CERCANA de la sala de jefe; true si esta a BossProximityRadius
        // celdas o menos (incluye estar parado adentro, distancia 0).
        private static bool IsNearBossRoom(DungeonFloor floor, int px, int py)
        {
            foreach (var (bx, by) in floor.BossRoomCells)
            {
                int dist = Mathf.Max(Mathf.Abs(px - bx), Mathf.Abs(py - by));
                if (dist <= BossProximityRadius) return true;
            }
            return false;
        }

        private void ApplyBiome(bool nearBossRoom)
        {
            var nearMain = _near.main;
            var nearEmission = _near.emission;
            var nearVel = _near.velocityOverLifetime;
            var farMain = _far.main;
            var farEmission = _far.emission;
            var farVel = _far.velocityOverLifetime;

            if (nearBossRoom)
            {
                SetLayer(nearMain, nearEmission, nearVel,
                    color: StormNearColor, speed: (0.25f, 0.6f), size: (0.05f, 0.1f),
                    life: (1.2f, 2f), gravity: 0.02f, rate: 20f, drift: (-0.25f, 0.25f));
                SetLayer(farMain, farEmission, farVel,
                    color: StormFarColor, speed: (0.04f, 0.1f), size: (0.7f, 1.4f),
                    life: (8f, 12f), gravity: 0f, rate: 5f, drift: (-0.08f, 0.08f));
            }
            else
            {
                // Rates bajados de nuevo (12->8, 2->1.3, wisps 1.6->1.1 mas abajo) a pedido -- se
                // seguia sintiendo cargado el polvo blanco "de siempre" (fuera de la tormenta de
                // jefe, que no se toco: ese es un momento especial, no la ambientacion diaria).
                SetLayer(nearMain, nearEmission, nearVel,
                    color: MysteryNearColor, speed: (0.05f, 0.15f), size: (0.06f, 0.13f),
                    life: (3f, 4.5f), gravity: 0f, rate: 8f, drift: (-0.05f, 0.05f));
                SetLayer(farMain, farEmission, farVel,
                    color: MysteryFarColor, speed: (0.02f, 0.05f), size: (0.5f, 0.9f),
                    life: (12f, 18f), gravity: 0f, rate: 1.3f, drift: (-0.04f, 0.04f));
            }

            var wispMain = _wisps.main;
            var wispEmission = _wisps.emission;
            var wispVel = _wisps.velocityOverLifetime;
            SetLayer(wispMain, wispEmission, wispVel,
                color: nearBossRoom ? LightningColor : Color.white,
                speed: (0.02f, 0.06f), size: (0.09f, 0.16f),
                life: (4f, 6f), gravity: 0f, rate: nearBossRoom ? 4f : 1.1f, drift: (-0.03f, 0.03f));
        }

        // Relampago real de sala de jefe: espera un intervalo random y despues dispara un flash de
        // luz (sube a full brillo casi de inmediato y cae rapido, como FlashLight en
        // ElementalParticleEffect) mas 2-3 rayos en puntos DISTINTOS de la sala (ver
        // PickBoltOrigins) -- se repite mientras el jugador siga cerca, para que la tormenta no
        // pare nunca del todo.
        private IEnumerator StormRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(3f, 7f));
                yield return StartCoroutine(LightningStrike());
            }
        }

        private IEnumerator LightningStrike()
        {
            const float peakIntensity = 9f;
            const float riseTime = 0.03f;
            const float fallTime = 0.35f;

            // Varios rayos "aca y alla" (no siempre el mismo punto) ADEMAS del flash de luz y las
            // chispas: antes esto era solo un Light parpadeando -- se sentia mas a un fogonazo que
            // a un rayo de verdad. Ahora hay particulas Stretch (rayas, ver ConfigureStreakLayer)
            // que dibujan trazos de verdad, apareciendo en distintos rincones de la sala.
            foreach (var origin in PickBoltOrigins(Random.Range(2, 4))) EmitLightningBolt(origin);
            EmitSparkBurst(_bossRoomAnchor);

            float t = 0f;
            while (t < riseTime)
            {
                t += Time.deltaTime;
                _stormLight.intensity = Mathf.Lerp(0f, peakIntensity, t / riseTime);
                yield return null;
            }
            // Segundo destello mas corto, como el rebote de un trueno real, antes de apagarse del
            // todo -- con SUS PROPIOS rayos, en otros puntos de la sala.
            yield return new WaitForSeconds(0.05f);
            foreach (var origin in PickBoltOrigins(Random.Range(1, 3))) EmitLightningBolt(origin);
            EmitSparkBurst(_bossRoomAnchor);
            _stormLight.intensity = peakIntensity * 0.7f;

            t = 0f;
            while (t < fallTime)
            {
                t += Time.deltaTime;
                _stormLight.intensity = Mathf.Lerp(peakIntensity * 0.7f, 0f, t / fallTime);
                yield return null;
            }
            _stormLight.intensity = 0f;
        }

        // `count` posiciones en el mundo dentro de la sala de jefe ACTUAL (celdas al azar de
        // floor.BossRoomCells, convertidas via DungeonManager.CellToWorld) para que cada rayo caiga
        // en un rincon distinto de la sala en vez de siempre el mismo punto.
        private List<Vector3> PickBoltOrigins(int count)
        {
            var origins = new List<Vector3>();
            var floor = dungeonManager.CurrentFloor;
            if (floor == null || !floor.HasBossRoom)
            {
                origins.Add(_bossRoomAnchor);
                return origins;
            }

            var cells = floor.BossRoomCells;
            for (int i = 0; i < count; i++)
            {
                var (cx, cy) = cells[Random.Range(0, cells.Count)];
                origins.Add(dungeonManager.CellToWorld(cx, cy) + Vector3.up * Random.Range(1.2f, 2.4f));
            }
            return origins;
        }

        // Trazo en zigzag hecho de RAYAS (particulas Stretch de _lightningStreaks) desde un techo
        // imaginario hasta el piso, cada segmento orientado segun SU PROPIA direccion (no siempre
        // derecho hacia abajo) -- asi se lee como un relampago quebrado de verdad, no una fila de
        // puntos redondos.
        private void EmitLightningBolt(Vector3 origin)
        {
            Vector3 top = origin + Vector3.up * 1.6f;
            Vector3 bottom = origin - Vector3.up * 1.4f;

            var emitParams = new ParticleSystem.EmitParams { startColor = LightningColor };

            const int segments = 9;
            Vector3 prev = top;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 basePos = Vector3.Lerp(top, bottom, t);
                Vector3 jitter = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * (0.4f * (1f - t) + 0.05f);
                Vector3 next = basePos + jitter;

                Vector3 dir = (next - prev).sqrMagnitude > 0.0001f ? (next - prev).normalized : Vector3.down;
                emitParams.position = prev;
                emitParams.velocity = dir * Random.Range(9f, 14f); // el modulo Stretch dibuja la raya en esta direccion
                emitParams.startLifetime = Random.Range(0.08f, 0.13f);
                emitParams.startSize = Random.Range(0.05f, 0.09f);
                _lightningStreaks.Emit(emitParams, 1);

                prev = next;
            }
        }

        // Cada tanto (de vez en cuando, no un ritmo fijo) una estrella fugaz cruza el techo cerca
        // del jugador -- se repite mientras siga en el Bioma 2, igual que la tormenta se repite
        // mientras siga cerca de la sala de jefe.
        private IEnumerator ShootingStarRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(20f, 45f));
                yield return StartCoroutine(ShootingStar());
            }
        }

        // Recorre una cuerda recta a la altura del techo, pasando CERCA del jugador (no
        // necesariamente justo encima) en una direccion horizontal al azar. La luz sube y baja en
        // forma de campana a medida que avanza (pico a mitad de camino, mas fuerte cuanto mas cerca
        // pasa del jugador) -- eso es lo que "ilumina un poco la zona alrededor del jugador" sin
        // ser un flash parejo de principio a fin. El trazo visible es el mismo truco Stretch que el
        // rayo de la tormenta: se emite una particula por frame en la posicion actual, con velocidad
        // = direccion de avance, asi el renderer la dibuja como una raya en vez de un punto.
        private IEnumerator ShootingStar()
        {
            if (player == null) yield break;

            const float height = 5f; // cerca del techo, por encima de la cabeza del jugador
            const float halfLength = 7f;
            const float duration = 1.0f;

            Vector3 playerPos = player.transform.position;
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            Vector3 dir = new Vector3(dir2D.x, 0f, dir2D.y);
            Vector3 center = playerPos + Vector3.up * height + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            Vector3 start = center - dir * halfLength;
            Vector3 end = center + dir * halfLength;

            var emitParams = new ParticleSystem.EmitParams { startColor = ShootingStarColor, startSize = 0.1f, startLifetime = 0.35f };

            float t = 0f;
            Vector3 prevPos = start;
            while (t < duration)
            {
                t += Time.deltaTime;
                float frac = Mathf.Clamp01(t / duration);
                Vector3 pos = Vector3.Lerp(start, end, frac);
                _starLight.transform.position = pos;
                _starLight.intensity = Mathf.Sin(frac * Mathf.PI) * ShootingStarPeakIntensity;

                emitParams.position = pos;
                emitParams.velocity = (pos - prevPos) / Mathf.Max(Time.deltaTime, 0.001f);
                _starStreaks.Emit(emitParams, 1);

                prevPos = pos;
                yield return null;
            }
            _starLight.intensity = 0f;
        }

        // Chispas radiando hacia afuera desde el punto de impacto: con velocidad (no quietas como
        // antes) para que el modulo Stretch las dibuje como rayitas cortas, no puntos.
        private void EmitSparkBurst(Vector3 center)
        {
            var emitParams = new ParticleSystem.EmitParams
            {
                startColor = LightningColor,
                startSize = 0.07f,
                startLifetime = 0.18f,
            };
            int count = Random.Range(8, 14);
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Random.insideUnitSphere.normalized;
                emitParams.position = center + dir * 0.3f;
                emitParams.velocity = dir * Random.Range(4f, 7f);
                _lightningStreaks.Emit(emitParams, 1);
            }
        }

        private static void SetLayer(ParticleSystem.MainModule main, ParticleSystem.EmissionModule emission,
            ParticleSystem.VelocityOverLifetimeModule vel, Color color, (float, float) speed, (float, float) size,
            (float, float) life, float gravity, float rate, (float, float) drift)
        {
            main.startColor = color;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Item1, speed.Item2);
            main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.Item1, life.Item2);
            main.gravityModifier = gravity;
            emission.rateOverTime = rate;
            vel.x = new ParticleSystem.MinMaxCurve(drift.Item1, drift.Item2);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(drift.Item1, drift.Item2);
        }
    }
}
