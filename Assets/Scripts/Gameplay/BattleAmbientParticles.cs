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
        private Transform _followTarget;

        public static BattleAmbientParticles Spawn(Transform followTarget, bool boss)
        {
            var go = new GameObject("BattleAmbientParticles");
            var comp = go.AddComponent<BattleAmbientParticles>();
            comp._followTarget = followTarget;
            if (followTarget != null) go.transform.position = followTarget.position;
            comp.Setup(boss);
            return comp;
        }

        private void Setup(bool boss)
        {
            _near = ParticleLayerFactory.CreateLayer(transform, "BattleAmbientNear");
            _far = ParticleLayerFactory.CreateLayer(transform, "BattleAmbientFar");

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
                // Polvo suave flotando en el aire de la arena para un combate comun.
                nearMain.startColor = new Color(0.85f, 0.82f, 0.72f, 0.5f);
                nearMain.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                nearMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
                nearMain.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                nearMain.gravityModifier = 0f;
                nearEmission.rateOverTime = 12f;

                farMain.startColor = new Color(0.55f, 0.53f, 0.5f, 0.09f);
                farMain.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                farMain.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                farMain.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
                farMain.gravityModifier = 0f;
                farEmission.rateOverTime = 4f;
            }

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
        }

        void LateUpdate()
        {
            if (_followTarget != null) transform.position = _followTarget.position;
        }
    }
}
