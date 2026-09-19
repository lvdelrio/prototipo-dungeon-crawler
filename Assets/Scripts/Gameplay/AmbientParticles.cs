using UnityEngine;

namespace Gameplay
{
    // Particulas de ambiente en la mazmorra, para dar dinamismo y sensacion de profundidad
    // mientras se explora, en 2 capas (como el fondo de un escenario tipo Tekken):
    //  - Cerca: motas nitidas y relativamente rapidas, en un volumen que sigue la posicion Y LA
    //    DIRECCION en la que mira la camara (no solo la posicion) -- asi siempre hay algo
    //    flotando mas o menos adelante, sin importar hacia donde se gire, en vez de quedar
    //    esparcidas al azar y "perderse" cuando el jugador da vuelta. Si esa direccion apunta
    //    contra una pared, las particulas quedan naturalmente detras de ella (tapadas por el
    //    z-buffer, igual que cualquier objeto transparente) -- no hace falta logica extra para eso.
    //  - Lejos: un volumen mucho mas grande de particulas grandes, tenues y lentas (neblina/calina)
    //    que solo sigue la posicion, dando la sensacion de que el fondo se pierde en la distancia.
    // El tipo cambia ciclando segun el indice de piso, y se reemplaza por brasas intensas apenas
    // el jugador entra a la sala de un jefe.
    public class AmbientParticles : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public Transform followTarget;

        // Cuanto se adelanta el volumen "cerca" en la direccion en la que mira la camara.
        private const float NearForwardOffset = 1.1f;

        private enum Biome { Dust, Leaves, Embers }

        private ParticleSystem _near;
        private ParticleSystem _far;
        private int _lastFloorIndex = int.MinValue;
        private bool _lastInBossRoom;

        void Awake()
        {
            _near = ParticleLayerFactory.CreateLayer(transform, "AmbientNear");
            _far = ParticleLayerFactory.CreateLayer(transform, "AmbientFar");

            ConfigureCommon(_near, maxParticles: 220, boxScale: new Vector3(2.6f, 2.2f, 2.6f));
            ConfigureCommon(_far, maxParticles: 120, boxScale: new Vector3(16f, 7f, 16f));

            ApplyBiome(Biome.Dust, false);

            ParticleLayerFactory.Activate(_near);
            ParticleLayerFactory.Activate(_far);
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

        void LateUpdate()
        {
            if (followTarget != null)
            {
                _far.transform.position = followTarget.position;
                _near.transform.position = followTarget.position + followTarget.forward * NearForwardOffset;
            }
            if (dungeonManager == null || player == null) return;

            int floorIndex = dungeonManager.CurrentFloorIndex;
            bool inBossRoom = false;
            var floor = dungeonManager.CurrentFloor;
            if (floor != null && floor.InBounds(player.CellX, player.CellY))
                inBossRoom = floor.Cells[player.CellX, player.CellY].IsBossRoom;

            if (floorIndex == _lastFloorIndex && inBossRoom == _lastInBossRoom) return;
            _lastFloorIndex = floorIndex;
            _lastInBossRoom = inBossRoom;

            var biome = inBossRoom ? Biome.Embers : (Biome)(((floorIndex % 3) + 3) % 3);
            ApplyBiome(biome, inBossRoom);
        }

        private void ApplyBiome(Biome biome, bool intense)
        {
            var nearMain = _near.main;
            var nearEmission = _near.emission;
            var nearVel = _near.velocityOverLifetime;
            var farMain = _far.main;
            var farEmission = _far.emission;
            var farVel = _far.velocityOverLifetime;

            switch (biome)
            {
                case Biome.Leaves:
                    // Vida mas corta que antes: como el volumen "cerca" ahora sigue hacia donde se
                    // mira (no solo la posicion), conviene que se renueve rapido al girar en vez de
                    // dejar hojas viejas colgando de la vez anterior.
                    SetLayer(nearMain, nearEmission, nearVel,
                        color: new Color(0.5f, 0.68f, 0.24f, 0.95f), speed: (0.15f, 0.4f), size: (0.16f, 0.3f),
                        life: (3f, 4.5f), gravity: 0.06f, rate: 14f, drift: (-0.15f, 0.15f));
                    SetLayer(farMain, farEmission, farVel,
                        color: new Color(0.35f, 0.48f, 0.22f, 0.1f), speed: (0.02f, 0.06f), size: (0.6f, 1.1f),
                        life: (14f, 20f), gravity: 0.01f, rate: 3f, drift: (-0.06f, 0.06f));
                    break;

                case Biome.Embers:
                    SetLayer(nearMain, nearEmission, nearVel,
                        color: new Color(1f, 0.48f, 0.14f, 1f), speed: (0.3f, 0.7f), size: (0.07f, 0.15f),
                        life: (1.8f, 2.8f), gravity: -0.04f, rate: intense ? 40f : 22f, drift: (-0.1f, 0.1f));
                    SetLayer(farMain, farEmission, farVel,
                        color: new Color(0.5f, 0.24f, 0.1f, 0.12f), speed: (0.03f, 0.08f), size: (0.7f, 1.3f),
                        life: (10f, 15f), gravity: -0.015f, rate: intense ? 6f : 4f, drift: (-0.05f, 0.05f));
                    break;

                default: // Dust
                    SetLayer(nearMain, nearEmission, nearVel,
                        color: new Color(0.88f, 0.85f, 0.75f, 0.5f), speed: (0.05f, 0.15f), size: (0.06f, 0.13f),
                        life: (3f, 4.5f), gravity: 0f, rate: 18f, drift: (-0.05f, 0.05f));
                    SetLayer(farMain, farEmission, farVel,
                        color: new Color(0.6f, 0.58f, 0.52f, 0.1f), speed: (0.02f, 0.05f), size: (0.5f, 0.9f),
                        life: (12f, 18f), gravity: 0f, rate: 3f, drift: (-0.04f, 0.04f));
                    break;
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
