namespace Lore
{
    // Fragmento de lore descubrible (metroidvania): pura descripcion + texto, sin dependencia de
    // Unity. DungeonGen.CellType.Lore asigna un Id de este catalogo a una celda del mapa; al
    // pisarla se desbloquea (Meta.MetaProgress.UnlockLore) y con eso se habilita el atajo que la
    // acompaña (DungeonGen.ShortcutGate.RequiredLoreId).
    public class LoreEntry
    {
        public string Id = "";
        public string Title = "";
        public string Text = "";
    }

    public static class LoreCatalog
    {
        public static readonly LoreEntry[] All =
        {
            new LoreEntry
            {
                Id = "runas_antiguas", Title = "Runas Antiguas",
                Text = "Grabados en la piedra describen un metodo para \"despertar\" los mecanismos "
                     + "de atajo dejados por quienes cavaron estos pasillos antes que nosotros.",
            },
            new LoreEntry
            {
                Id = "diario_explorador", Title = "Diario de un Explorador",
                Text = "\"Encontre un segundo camino, mas corto, pero la palanca no respondia hasta "
                     + "que entendi la inscripcion en la entrada. El conocimiento abre tanto como la fuerza.\"",
            },
            new LoreEntry
            {
                Id = "sello_olvidado", Title = "Sello Olvidado",
                Text = "Un sello circular, mitad roca mitad metal. Se activa al reconocer el patron: "
                     + "no hace falta romperlo, solo saber donde presionar.",
            },
            new LoreEntry
            {
                Id = "cronicas_guardianes", Title = "Cronicas de los Guardianes",
                Text = "Los Guardianes de Piedra no atacan al azar: golpean donde la formacion esta "
                     + "mas expuesta. Los relatos coinciden en que el frente atrae toda la furia.",
            },
            new LoreEntry
            {
                Id = "mapa_fragmentado", Title = "Mapa Fragmentado",
                Text = "Un trozo de mapa, quemado en los bordes, marca una ruta alternativa que no "
                     + "aparece en ningun otro registro de la expedicion.",
            },
            new LoreEntry
            {
                Id = "voz_en_la_piedra", Title = "Una Voz en la Piedra",
                Text = "\"El que entienda esto podra abrir el paso.\" No hay firma. Solo el eco de "
                     + "quien lo escribio, mucho antes de que llegaramos.",
            },
        };

        public static LoreEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var entry in All)
                if (entry.Id == id) return entry;
            return null;
        }
    }
}
