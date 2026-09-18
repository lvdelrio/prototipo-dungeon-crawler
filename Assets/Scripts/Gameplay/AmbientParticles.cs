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
            // El sistema de particulas real vive en un HIJO que arranca INACTIVO: si se agregara
            // el componente ya activo, "Play On Awake" (true por defecto) lo haria empezar a
            // reproducirse YA MISMO con la config de fabrica (particulas blancas gigantes en cono)
            // antes de terminar de aplicar el bioma -- quedaba visualmente mal o de plano invisible
            // segun el timing. Configurar todo con el hijo inactivo y recien ahi activarlo evita esto.
            var psGo = new GameObject("AmbientParticleSystem");
            psGo.transform.SetParent(transform, false);
            psGo.SetActive(false);

            _ps = psGo.AddComponent<ParticleSystem>();
            var renderer = psGo.GetComponent<ParticleSystemRenderer>();
            renderer.material = ParticleTextureFactory.SharedMaterial;

            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 200;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Ancho/profundidad cerca del tamano real de un pasillo (DungeonSettings.cellSize=4),
            // para que la mayoria del volumen quede en el aire caminable y no adentro de paredes.
            shape.scale = new Vector3(3.5f, 2.6f, 3.5f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;

            ApplyBiome(Biome.Dust, false);

            psGo.SetActive(true);
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
                    main.startColor = new Color(0.5f, 0.68f, 0.24f, 0.95f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                    main.gravityModifier = 0.06f;
                    emission.rateOverTime = 8f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                    break;

                case Biome.Embers:
                    main.startColor = new Color(1f, 0.48f, 0.14f, 1f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                    main.gravityModifier = -0.04f; // suben, como brasas
                    emission.rateOverTime = intense ? 30f : 16f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
                    break;

                default: // Dust
                    main.startColor = new Color(0.88f, 0.85f, 0.75f, 0.45f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.gravityModifier = 0f;
                    emission.rateOverTime = 10f;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    break;
            }
        }
    }
}
