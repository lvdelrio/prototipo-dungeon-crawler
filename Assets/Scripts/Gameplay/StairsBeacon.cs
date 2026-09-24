using UnityEngine;

namespace Gameplay
{
    // Baliza de particulas fija en cada escalera (subida o bajada): una columna continua que usa
    // el mismo material de particulas compartido que el resto del juego (ver
    // ParticleTextureFactory) -- ese shader no se ve afectado por la niebla de distancia, asi que
    // la columna sigue siendo visible mucho mas alla de donde ya se pierden las paredes y el piso
    // (ver DungeonSceneBuilder: niebla completa a las 4 celdas). Subir = celeste, ascendiendo;
    // bajar = naranja, descendiendo -- mismo color que el marcador de esa escalera, pero el
    // sentido del movimiento tambien delata cual es cual sin tener que acercarse a leerlo.
    public class StairsBeacon : MonoBehaviour
    {
        private ParticleSystem _column;

        public void Configure(bool goingUp, float cellSize, float wallHeight)
        {
            _column = ParticleLayerFactory.CreateLayer(transform, "StairsBeacon");

            var main = _column.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 40;
            main.startColor = goingUp ? new Color(0.35f, 1f, 1f, 0.85f) : new Color(1f, 0.55f, 0.1f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0f); // el movimiento lo da velocityOverLifetime
            float travelTime = Mathf.Max(1.5f, wallHeight / 0.5f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(travelTime * 0.8f, travelTime * 1.1f);
            main.gravityModifier = 0f;

            var shape = _column.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = cellSize * 0.22f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // disco horizontal: las particulas nacen "acostadas" y suben/bajan en columna

            var emission = _column.emission;
            emission.rateOverTime = 9f;

            var vel = _column.velocityOverLifetime;
            vel.enabled = true;
            float dir = goingUp ? 1f : -1f;
            vel.y = new ParticleSystem.MinMaxCurve(dir * 0.35f, dir * 0.55f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);

            var sizeOverLifetime = _column.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 1.2f));

            var colorOverLifetime = _column.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.75f),
                new GradientAlphaKey(0f, 1f),
            };
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphaKeys);
            colorOverLifetime.color = gradient;

            // Nace pegada al piso (subida) o al techo (bajada), asi el flujo entero atraviesa la
            // altura del pasillo en la direccion correcta. OJO: solo se ajusta la altura (Y) --
            // pisar todo transform.localPosition con un Vector3 nuevo tiraba a la basura el X/Z que
            // el creador (DungeonLevelBuilder.BuildMarker) ya habia fijado bien via
            // transform.position = center ANTES de llamar a Configure(); el bug real era que TODAS
            // las balizas de escalera de un piso quedaban apiladas en el origen local del piso
            // (0, y, 0) en vez de en su propia celda -- por eso no se veian en las escaleras.
            var localPos = transform.localPosition;
            localPos.y = goingUp ? 0.15f : wallHeight - 0.15f;
            transform.localPosition = localPos;

            ParticleLayerFactory.Activate(_column);
        }
    }
}
