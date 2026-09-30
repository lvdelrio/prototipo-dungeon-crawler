using UnityEngine;

namespace Gameplay
{
    // Particulas de ambiente para la escena de BATALLA (mismo criterio de 2 capas que
    // AmbientParticles en la mazmorra: una capa cercana mas visible y una capa lejana, grande y
    // tenue, para dar sensacion de profundidad detras de los enemigos -- el fondo de un escenario
    // tipo Tekken). Se crea al entrar en combate (BattleStageController) y se destruye al salir.
    public class BattleAmbientParticles : MonoBehaviour
    {
        private ParticleSystem _near;
        private ParticleSystem _far;
        private ParticleSystem _leaves;
        private Transform _followTarget;

        public static BattleAmbientParticles Spawn(Transform followTarget, bool boss, BattleStageController.CombatZoneTheme zone = BattleStageController.CombatZoneTheme.Forest)
        {
            var go = new GameObject("BattleAmbientParticles");
            var comp = go.AddComponent<BattleAmbientParticles>();
            comp._followTarget = followTarget;
            if (followTarget != null) go.transform.position = followTarget.position;
            comp.Setup(boss, zone);
            return comp;
        }

        private void Setup(bool boss, BattleStageController.CombatZoneTheme zone)
        {
            _near = ParticleLayerFactory.CreateLayer(transform, "BattleAmbientNear");
            _far = ParticleLayerFactory.CreateLayer(transform, "BattleAmbientFar");
            if (zone == BattleStageController.CombatZoneTheme.Forest)
            {
                _leaves = ParticleLayerFactory.CreateLayer(transform, "BattleForestLeaves");
                _leaves.GetComponent<ParticleSystemRenderer>().material = ParticleTextureFactory.LeafMaterial;
                ConfigureLeaves(_leaves);
            }

            ConfigureCommon(_near, maxParticles: 150, boxScale: new Vector3(8f, 4f, 8f));
            ConfigureCommon(_far, maxParticles: 100, boxScale: new Vector3(22f, 9f, 22f));

            var nearMain = _near.main;
            var nearEmission = _near.emission;
            var farMain = _far.main;
            var farEmission = _far.emission;

            if (boss)
            {
                // Brasas mas densas e intensas para las peleas de jefe.
                nearMain.startColor = new Color(1f, 0.45f, 0.12f, 0.95f);
                nearMain.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
                nearMain.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                nearMain.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
                nearMain.gravityModifier = -0.05f;
                nearEmission.rateOverTime = 22f;

                farMain.startColor = new Color(0.55f, 0.22f, 0.08f, 0.14f);
                farMain.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                farMain.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
                farMain.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f);
                farMain.gravityModifier = -0.01f;
                farEmission.rateOverTime = 6f;
            }
            else
            {
                // Polvo suave flotando en el aire de la arena para un combate comun -- tenido
                // segun la zona (pedido puntual: "el fondo de combate tiene que hacer sentido con
                // la zona"), no siempre el mismo gris/beige neutro de antes.
                var (nearTint, farTint) = zone switch
                {
                    BattleStageController.CombatZoneTheme.Cave => (new Color(0.55f, 0.5f, 0.42f, 0.5f), new Color(0.35f, 0.32f, 0.28f, 0.09f)),
                    BattleStageController.CombatZoneTheme.SpaceCave => (new Color(0.55f, 0.65f, 0.9f, 0.5f), new Color(0.3f, 0.35f, 0.55f, 0.09f)),
                    // Bioma de Cuevas: polvo de piedra mas claro/tierra que la cueva chica del
                    // bosque (Cave arriba), sin el tinte azulado del espacio.
                    BattleStageController.CombatZoneTheme.RockCave => (new Color(0.6f, 0.55f, 0.48f, 0.5f), new Color(0.4f, 0.37f, 0.32f, 0.09f)),
                    _ => (new Color(0.78f, 0.85f, 0.7f, 0.5f), new Color(0.5f, 0.58f, 0.45f, 0.09f)), // Forest: polvo verdoso
                };

                nearMain.startColor = nearTint;
                nearMain.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                nearMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                nearMain.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                nearMain.gravityModifier = 0f;
                nearEmission.rateOverTime = 12f;

                farMain.startColor = farTint;
                farMain.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                farMain.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                farMain.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
                farMain.gravityModifier = 0f;
                farEmission.rateOverTime = 4f;
            }

            ParticleLayerFactory.Activate(_near);
            ParticleLayerFactory.Activate(_far);
            if (_leaves != null) ParticleLayerFactory.Activate(_leaves);
        }

        private void ConfigureLeaves(ParticleSystem ps)
        {
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 22;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.73f, 0.83f, 0.52f, 0.58f), new Color(0.88f, 0.58f, 0.32f, 0.68f));
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.gravityModifier = 0.015f;
            var emission = ps.emission;
            emission.rateOverTime = 1.6f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(14f, 7f, 1.5f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(-0.08f, -0.02f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingFudge = 1f;
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
        }

        void LateUpdate()
        {
            if (_followTarget != null) transform.position = _followTarget.position;
        }
    }
}
