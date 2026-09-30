using UnityEngine;
using System.Collections.Generic;

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

        [Header("Respuesta visible del bosque")]
        public Color dayFogColor = new Color(0.13f, 0.18f, 0.12f);
        public Color nightFogColor = new Color(0.012f, 0.022f, 0.065f);

        [Header("Iluminacion de cuevas")]
        public Color caveDayAmbient = new Color(0.12f, 0.13f, 0.14f);
        public Color caveNightAmbient = new Color(0.025f, 0.035f, 0.06f);
        public Color caveDayFog = new Color(0.045f, 0.052f, 0.06f);
        public Color caveNightFog = new Color(0.012f, 0.018f, 0.032f);
        public float caveFogEndDistance = 18f;

        private const string NightShaderParameter = "_WorldNightAmount";
        private float _cycleStartTime;
        private Light[] _directionalLights;
        private float[] _dayLightIntensities;
        private Color[] _dayLightColors;
        private Camera _mainCamera;
        private DungeonManager _dungeonManager;
        private float _defaultFogStartDistance;
        private float _defaultFogEndDistance;
        private bool _caveEnvironment;

        // 0 = dia pleno, 1 = noche plena -- publico por si otro sistema (particulas, shaders)
        // quiere reaccionar al momento del dia mas adelante.
        public float NightAmount { get; private set; }

        public void SetCaveEnvironment(bool inCave) => _caveEnvironment = inCave;

        void Awake()
        {
            _cycleStartTime = Time.time;
            _mainCamera = Camera.main;
            _dungeonManager = GetComponent<DungeonManager>();
            _defaultFogStartDistance = RenderSettings.fogStartDistance;
            _defaultFogEndDistance = RenderSettings.fogEndDistance;
            var lights = new List<Light>();
            foreach (var light in FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Directional && light.gameObject.scene == gameObject.scene && light != sunLight)
                    lights.Add(light);
            }
            _directionalLights = lights.ToArray();
            _dayLightIntensities = new float[_directionalLights.Length];
            _dayLightColors = new Color[_directionalLights.Length];
            for (int i = 0; i < _directionalLights.Length; i++)
            {
                _dayLightIntensities[i] = _directionalLights[i].intensity;
                _dayLightColors[i] = _directionalLights[i].color;
            }
        }

        void Update()
        {
            NightAmount = Mathf.PingPong((Time.time - _cycleStartTime) / Mathf.Max(1f, transitionSeconds), 1f);
            Shader.SetGlobalFloat(NightShaderParameter, NightAmount);

            RenderSettings.ambientSkyColor = Color.Lerp(daySkyColor, nightSkyColor, NightAmount);
            RenderSettings.ambientEquatorColor = Color.Lerp(dayEquatorColor, nightEquatorColor, NightAmount);
            RenderSettings.ambientGroundColor = Color.Lerp(dayGroundColor, nightGroundColor, NightAmount);
            RenderSettings.ambientIntensity = Mathf.Lerp(dayAmbientIntensity, nightAmbientIntensity, NightAmount) * (_caveEnvironment ? 0.45f : 1f);
            // La escena usa AmbientMode.Flat: sky/equator/ground no se consultan en ese modo.
            // Cambiar ambientLight y las direccionales hace que Standard y los shaders propios
            // respondan de verdad, en vez de dejar el ciclo guardado en valores que nadie lee.
            Color forestAmbient = Color.Lerp(new Color(0.5f, 0.5f, 0.55f), new Color(0.045f, 0.065f, 0.14f), NightAmount);
            RenderSettings.ambientLight = _caveEnvironment
                ? Color.Lerp(caveDayAmbient, caveNightAmbient, NightAmount)
                : forestAmbient;
            Shader.SetGlobalFloat("_WorldCaveAmount", _caveEnvironment ? 1f : 0f);
            if (_dungeonManager == null || !_dungeonManager.IsCombatActive)
            {
                RenderSettings.fogColor = _caveEnvironment
                    ? Color.Lerp(caveDayFog, caveNightFog, NightAmount)
                    : Color.Lerp(dayFogColor, nightFogColor, NightAmount);
                RenderSettings.fogStartDistance = _caveEnvironment ? 0f : _defaultFogStartDistance;
                RenderSettings.fogEndDistance = _caveEnvironment ? caveFogEndDistance : _defaultFogEndDistance;
                if (_mainCamera != null) _mainCamera.backgroundColor = RenderSettings.fogColor;
            }

            for (int i = 0; i < _directionalLights.Length; i++)
            {
                var light = _directionalLights[i];
                if (light == null) continue;
                float caveLightScale = _caveEnvironment ? 0.18f : 1f;
                light.intensity = _dayLightIntensities[i] * Mathf.Lerp(1f, 0.16f, NightAmount) * caveLightScale;
                // Neutraliza luces de escena demasiado amarillas durante el dia y las lleva
                // gradualmente a azul lunar por la noche.
                Color daylight = Color.Lerp(_dayLightColors[i], new Color(0.82f, 0.9f, 1f), 0.55f);
                light.color = Color.Lerp(daylight, new Color(0.35f, 0.48f, 0.9f), NightAmount * 0.82f);
            }

            if (sunLight != null)
            {
                sunLight.intensity = Mathf.Lerp(sunDayIntensity, sunNightIntensity, NightAmount) * (_caveEnvironment ? 0.18f : 1f);
                // Cruza el cielo de este a oeste durante el dia y sigue de largo por debajo del
                // horizonte durante la noche -- un ciclo completo (dia+noche) de 360 grados cada
                // 2 * transitionSeconds, sincronizado con el mismo PingPong de arriba.
                float fullCycle = (Time.time - _cycleStartTime) / (2f * transitionSeconds);
                float angle = (fullCycle % 1f) * 360f;
                sunLight.transform.rotation = Quaternion.Euler(angle - 90f, 170f, 0f);
            }
        }
    }
}
