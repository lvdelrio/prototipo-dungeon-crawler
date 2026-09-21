using System.IO;
using UnityEngine;
using Meta;

namespace Gameplay
{
    // Guarda/carga el progreso permanente (MetaProgress) en un archivo JSON simple dentro de
    // Application.persistentDataPath, para que sobreviva entre sesiones (cerrar y abrir Unity).
    public static class MetaSaveService
    {
        private const string FileName = "meta_save.json";
        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        public static MetaProgress Load()
        {
            if (!File.Exists(SavePath)) return new MetaProgress();
            try
            {
                string json = File.ReadAllText(SavePath);
                var loaded = JsonUtility.FromJson<MetaProgress>(json);
                return loaded ?? new MetaProgress();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"No se pudo leer el guardado de progreso ({SavePath}), se empieza de cero: {ex.Message}");
                return new MetaProgress();
            }
        }

        public static void Save(MetaProgress progress)
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(progress));
        }
    }
}
