namespace Lore
{
    // Que restos, si los hay, se ven ALREDEDOR del marcador de lore en la celda (ver
    // Gameplay.DungeonLevelBuilder.BuildLoreSceneDressing) -- la idea es que la sala respalde lo
    // que el texto cuenta en vez de que el fragmento sea la unica fuente de la escena (environmental
    // storytelling: el espacio se lee, no solo se explica). None es una eleccion valida, no un
    // olvido: un fragmento que describe un mecanismo o una reflexion (no un evento con marca fisica)
    // no deberia forzar escombros que el texto no sostiene.
    public enum SceneDressing
    {
        None,
        Scorched,  // quemado / ceniza -- algo se incendio aca
        Collapsed, // ruina vieja / derrumbe -- esto lleva mucho mas tiempo abandonado que el resto
        Ambush,    // rastro de un combate viejo, ya terminado
        Camp,      // restos de un campamento -- alguien se quedo aca un tiempo
    }

    // Fragmento de lore descubrible (metroidvania): pura descripcion + texto, sin dependencia de
    // Unity. DungeonGen.CellType.Lore asigna un Id de este catalogo a una celda del mapa; al
    // pisarla se desbloquea (Meta.MetaProgress.UnlockLore) y con eso se habilita el atajo que la
    // acompaña (DungeonGen.ShortcutGate.RequiredLoreId).
    public class LoreEntry
    {
        public string Id = "";
        public string Title = "";
        public string Text = "";
        public SceneDressing Dressing = SceneDressing.None;
    }

    public static class LoreCatalog
    {
        public static readonly LoreEntry[] All =
        {
            // Los 3 de abajo (runas/diario/sello) describen un MECANISMO, no un evento -- son la
            // explicacion de como funciona el atajo del piso, ya representada en el mundo por los
            // propios marcadores de ShortcutSwitch/ShortcutLanding. Dressing = None a proposito: no
            // hay nada fisico que el texto sostenga, forzarlo seria decorar por decorar.
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
            // Cuenta un combate (los Guardianes golpeando una formacion) -- Ambush deja el rastro de
            // esa pelea alrededor del punto en vez de que quede solo en el texto.
            new LoreEntry
            {
                Id = "cronicas_guardianes", Title = "Cronicas de los Guardianes",
                Text = "Los Guardianes de Piedra no atacan al azar: golpean donde la formacion esta "
                     + "mas expuesta. Los relatos coinciden en que el frente atrae toda la furia.",
                Dressing = SceneDressing.Ambush,
            },
            // "Quemado en los bordes" es literal: Scorched es la unica opcion honesta aca.
            new LoreEntry
            {
                Id = "mapa_fragmentado", Title = "Mapa Fragmentado",
                Text = "Un trozo de mapa, quemado en los bordes, marca una ruta alternativa que no "
                     + "aparece en ningun otro registro de la expedicion.",
                Dressing = SceneDressing.Scorched,
            },
            // "Mucho antes de que llegaramos", sin firma, un eco en la piedra -- esto es mas viejo
            // que el resto del lore del piso. Collapsed marca esa diferencia de antiguedad.
            new LoreEntry
            {
                Id = "voz_en_la_piedra", Title = "Una Voz en la Piedra",
                Text = "\"El que entienda esto podra abrir el paso.\" No hay firma. Solo el eco de "
                     + "quien lo escribio, mucho antes de que llegaramos.",
                Dressing = SceneDressing.Collapsed,
            },

            // La Puerta Fria (ver DungeonGenerator.PlaceBiomeGate): 3 pistas de colocacion
            // garantizada en el piso 0, cada una con un trabajo distinto (plantar la pregunta,
            // decir DONDE, decir CON QUE) -- juntas le dicen al jugador como encontrar y abrir el
            // paso al Bioma 2. Ninguna es obligatoria: la pared es real y perforable igual sin
            // haber leido nada, esto solo ahorra tiempo. Las 3 comparten Dressing = Camp: son el
            // mismo diario de LA MISMA expedicion (marcaron el camino, acamparon, no llegaron a
            // tiempo), asi que las 3 salas de pistas deberian leerse como puntos del mismo
            // recorrido abandonado, no como tres escenas sueltas sin relacion entre si.
            new LoreEntry
            {
                Id = "puerta_fria_1", Title = "Diario de Expedición (I)",
                Text = "\"No vinimos por el tesoro de este piso. Vinimos por lo que sella la Puerta "
                     + "Fría. Los otros no la encontraron a tiempo.\"",
                Dressing = SceneDressing.Camp,
            },
            new LoreEntry
            {
                Id = "puerta_fria_2", Title = "Diario de Expedición (II)",
                Text = "\"Marcamos el camino de vuelta al punto de partida con ceniza, para no "
                     + "perder el rumbo. La Puerta está a un lado de ese mismo punto, donde la "
                     + "piedra no hizo eco cuando golpeamos.\"",
                Dressing = SceneDressing.Camp,
            },
            new LoreEntry
            {
                Id = "puerta_fria_3", Title = "Diario de Expedición (III)",
                Text = "\"Ningún golpe de espada abrió esa piedra. Hizo falta la misma herramienta "
                     + "que abre paso entre las salas — apuntada con paciencia, no con fuerza.\"",
                Dressing = SceneDressing.Camp,
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
