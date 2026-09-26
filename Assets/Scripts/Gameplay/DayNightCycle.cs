using UnityEngine;

namespace Gameplay
{
    // Ciclo de dia y noche automatico (pedido puntual: "que el tiempo pase automatico, 2 minutos
    // para pasar de dia a noche"): tiñe la luz ambiente de la escena (RenderSettings.ambient*, lo
    // que ilumina todo lo que no tiene su propia luz directa -- practicamente toda la mazmorra,
    // que no tiene ninguna luz puntual salvo la tormenta de la sala de jefe) de un tono "de dia"
    // a uno "de noche" y de vuelta, en bucle infinito. Se agrega solo (ver
    // DungeonManager.Awake) -- no hace falta cablearlo a mano en la escena.
    //
    // Mathf.PingPong(t, 1) da EXACTAMENTE el vaiven pedido sin necesidad de llevar una fase a
    // mano: sube de 0 a 1 en transitionSeconds, despues baja de 1 a 0 en los mismos
    // transitionSeconds, y repite -- "2 minutos para pasar de dia a noche" (y otros 2 de vuelta).
    public class DayNightCycle : MonoBehaviour
    {
        [Tooltip("Segundos que tarda en pasar de dia a noche (y los mismos de vuelta, de noche a dia).")]
        public float transitionSeconds = 120f;

        [Header("Dia (valores por defecto = los que ya tenia la escena)")]
        public Color daySkyColor = new Color(0.5f, 0.5f, 0.55f);
        public Color dayEquatorColor = new Color(0.114f, 0.125f, 0.133f);
        public Color dayGroundColor = new Color(0.047f, 0.043f, 0.035f);
        public float dayAmbientIntensity = 1f;

        [Header("Noche")]
        public Color nightSkyColor = new Color(0.05f, 0.06f, 0.12f);
        public Color nightEquatorColor = new Color(0.03f, 0.03f, 0.05f);
        public Color nightGroundColor = new Color(0.01f, 0.01f, 0.02f);
        public float nightAmbientIntensity = 0.35f;

        [Header("Sol/luna opcional")]
        [Tooltip("Si se asigna, tambien se atenua/tiñe con el ciclo y rota como un sol cruzando el cielo. Opcional: sin luz asignada, el ciclo sigue funcionando solo con la luz ambiente.")]
        public Light sunLight;
        public float sunDayIntensity = 1f;
        public float sunNightIntensity = 0.05f;

        // 0 = dia pleno, 1 = noche plena -- publico por si otro sistema (particulas, shaders)
        // quiere reaccionar al momento del dia mas adelante.
        public float NightAmount { get; private set; }

        void Update()
        {
            NightAmount = Mathf.PingPong(Time.time / Mathf.Max(1f, transitionSeconds), 1f);

            RenderSettings.ambientSkyColor = Color.Lerp(daySkyColor, nightSkyColor, NightAmount);
            RenderSettings.ambientEquatorColor = Color.Lerp(dayEquatorColor, nightEquatorColor, NightAmount);
            RenderSettings.ambientGroundColor = Color.Lerp(dayGroundColor, nightGroundColor, NightAmount);
            RenderSettings.ambientIntensity = Mathf.Lerp(dayAmbientIntensity, nightAmbientIntensity, NightAmount);

            if (sunLight != null)
            {
                sunLight.intensity = Mathf.Lerp(sunDayIntensity, sunNightIntensity, NightAmount);
                // Cruza el cielo de este a oeste durante el dia y sigue de largo por debajo del
                // horizonte durante la noche -- un ciclo completo (dia+noche) de 360 grados cada
                // 2 * transitionSeconds, sincronizado con el mismo PingPong de arriba.
                float fullCycle = Time.time / (2f * transitionSeconds);
                float angle = (fullCycle % 1f) * 360f;
                sunLight.transform.rotation = Quaternion.Euler(angle - 90f, 170f, 0f);
            }
        }
    }
}
