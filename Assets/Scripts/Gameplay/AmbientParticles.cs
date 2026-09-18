using UnityEngine;

namespace Gameplay
{
    // Particulas de ambiente en la mazmorra, para dar dinamismo mientras se explora: motas de
    // polvo por defecto, hojas cayendo en pisos "verdes" y brasas subiendo en pisos de fuego -- el
    // tipo cambia ciclando segun el indice de piso, y se reemplaza por brasas intensas apenas el
    // jugador entra a la sala de un jefe. Sigue la POSICION de la camara (nunca su rotacion, para
    // que el volumen de aparicion no gire con cada vuelta del jugador) sin ser su hijo.
    public class AmbientParticles : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public Transform followTarget;

        private enum Biome { Dust, Leaves, Embers }

        private ParticleSystem _ps;
        private int _lastFloorIndex = int.MinValue;
        private bool _lastInBossRoom;

        void Awake()
        {
            _ps = gameObject.AddComponent<ParticleSystem>();
            var renderer = gameObject.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleTextureFactory.SharedMaterial;

            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 200;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(10f, 4f, 10f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;

            ApplyBiome(Biome.Dust, false);
            _ps.Play();
        }

        void LateUpdate()
        {
            if (followTarget != null) transform.position = followTarget.position;
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
            var main = _ps.main;
            var emission = _ps.emission;
            var vel = _ps.velocityOverLifetime;

            switch (biome)
            {
                case Biome.Leaves:
                    main.startColor = new Color(0.45f, 0.62f, 0.22f, 0.85f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                    main.gravityModifier = 0.06f;
                    emission.rateOverTime = 5f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                    break;

                case Biome.Embers:
                    main.startColor = new Color(1f, 0.45f, 0.12f, 0.9f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                    main.gravityModifier = -0.04f; // suben, como brasas
                    emission.rateOverTime = intense ? 26f : 12f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
                    break;

                default: // Dust
                    main.startColor = new Color(0.82f, 0.8f, 0.72f, 0.22f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.gravityModifier = 0f;
                    emission.rateOverTime = 6f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    break;
            }
        }
    }
}
