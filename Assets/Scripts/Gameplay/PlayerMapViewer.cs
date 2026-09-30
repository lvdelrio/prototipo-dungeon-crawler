using UnityEngine;

namespace Gameplay
{
    // Mapa fisico que el personaje "levanta" cerca de camara al apretar M (ver GridPlayerController):
    // un Animator con 4 estados (Closed/Opening/Open/Closing, armados por PlayerMapPropBuilder) mueve
    // el transform del prop de una pose escondida a una pose cerca de camara y de vuelta; este script
    // prende/apaga esos triggers y mantiene actualizado el "dibujo" (drawingRenderer) -- grilla +
    // anotaciones del jugador, EN MODO A MANO (handDrawn: true, ver DungeonMapRenderer.DrawCore):
    // el automapa no revela piso/paredes/marcadores solo, unicamente pinta un celeste (WalkedColor)
    // en las celdas ya pisadas. Todo lo demas (paredes, colores, simbolos) lo dibuja el jugador con
    // las herramientas de PlayerMapEditorHUD -- que hace click/arrastra sobre ESTE mismo sprite
    // (proyecta Drawing.bounds a pantalla), no sobre un dibujo aparte.
    //
    // El juego NO se pausa mientras el mapa esta abierto (a pedido): el jugador puede seguir
    // caminando o ser alcanzado por un FOE con el mapa en pantalla -- por eso Update() sigue
    // refrescando el dibujo en vivo, y se auto-cierra si arranca un combate (la escena/camara de
    // batalla es otra, ver BattleStageController) o si se abre el menu de pausa.
    public class PlayerMapViewer : MonoBehaviour
    {
        public DungeonManager dungeonManager;
        public GridPlayerController player;
        public PauseMenuManager pauseMenu;
        public Animator animator;
        public SpriteRenderer drawingRenderer;

        public int cellPixelSize = 16;
        public float wallPixelThickness = 2f;

        // El papel (Paper, ver PlayerMapPropBuilder) siempre mide 1x1 unidad de mundo sin importar
        // el tamaño del piso. El dibujo tiene que encajar DENTRO de ese mismo cuadrado -- si se
        // dibujara con 1 pixel de textura = 1/cellPixelSize unidades (como hace la version IMGUI en
        // pantalla), un piso de 24x24 celdas terminaria 24 veces mas grande que el papel. En vez de
        // eso, se calcula un factor de escala aparte (ver RefreshDrawing) que appretuja el lado mas
        // largo del piso a "mapMargin" unidades, dejando un margen de papel visible alrededor.
        public float mapMargin = 0.82f;

        // Cada cuanto se re-dibuja la textura mientras el mapa esta abierto -- no hace falta todos
        // los frames (el jugador tarda varios frames en moverse una celda, ver
        // GridPlayerController.moveDuration), y redibujar entero es N*M SetPixels.
        public float refreshInterval = 0.15f;

        private static readonly int OpenTrigger = Animator.StringToHash("Open");
        private static readonly int CloseTrigger = Animator.StringToHash("Close");

        private Texture2D _drawingTex;
        private DungeonGen.DungeonFloor _texFloor;
        private float _nextRefreshTime;
        private bool _dirty;

        public bool IsOpen { get; private set; }

        // El dibujo cambia por 2 motivos: el jugador camina a una celda nueva (WalkedColor) o
        // pinta algo con las herramientas -- PlayerMapEditorHUD llama esto tras cada edicion en
        // vez de esperar hasta el proximo refresco por tiempo, asi el trazo se ve al instante.
        public void MarkDirty() => _dirty = true;

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen || dungeonManager == null || !dungeonManager.IsReady) return;
            if (dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive) return;
            if (pauseMenu != null && pauseMenu.IsOpen) return;

            IsOpen = true;
            RefreshDrawing(force: true);
            if (HasController)
            {
                animator.ResetTrigger(CloseTrigger);
                animator.SetTrigger(OpenTrigger);
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (HasController)
            {
                animator.ResetTrigger(OpenTrigger);
                animator.SetTrigger(CloseTrigger);
            }
        }

        // No solo "animator != null" -- si el Animator quedo sin runtimeAnimatorController (por
        // ejemplo, el .controller se borro/regenero en disco y la escena todavia no se reconstruyo
        // con "Dungeon > Build Test Scene"), SetTrigger/ResetTrigger tiran
        // "Animator is not playing an AnimatorController" a los gritos en la consola cada vez que
        // se abre/cierra el mapa. Mejor no llamarlos en ese caso (el mapa simplemente no anima
        // hasta que se reconstruya la escena, en vez de spamear errores).
        private bool HasController => animator != null && animator.runtimeAnimatorController != null;

        void Update()
        {
            if (!IsOpen) return;

            // Se cierra solo si pasa algo que no tiene sentido mezclar con "estar mirando el mapa"
            // -- entrar en combate (otra camara/escena) o abrir el menu de pausa encima.
            if (dungeonManager == null || dungeonManager.IsCombatActive || dungeonManager.IsGameOverShopActive || (pauseMenu != null && pauseMenu.IsOpen))
            {
                Close();
                return;
            }

            if (_dirty || Time.time >= _nextRefreshTime)
            {
                RefreshDrawing(force: false);
                _dirty = false;
                _nextRefreshTime = Time.time + refreshInterval;
            }
        }

        private void RefreshDrawing(bool force)
        {
            var floor = dungeonManager.CurrentFloor;
            if (floor == null || drawingRenderer == null) return;

            int texW = Mathf.Max(1, floor.Width * cellPixelSize);
            int texH = Mathf.Max(1, floor.Height * cellPixelSize);
            if (_drawingTex == null || floor != _texFloor || _drawingTex.width != texW || _drawingTex.height != texH)
            {
                _drawingTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                _texFloor = floor;
                // ppu tal que el lado MAS LARGO del piso (en pixeles de textura) mida "mapMargin"
                // unidades de mundo -- mismo cuadrado que el papel (1x1) para cualquier tamaño de
                // piso, ver comentario de mapMargin arriba.
                float longSidePx = Mathf.Max(texW, texH);
                float ppu = longSidePx / mapMargin;
                var sprite = Sprite.Create(_drawingTex, new Rect(0, 0, texW, texH), new Vector2(0.5f, 0.5f), ppu);
                drawingRenderer.sprite = sprite;
                force = true;
            }

            DungeonMapRenderer.DrawToTexture(_drawingTex, floor, playerMode: true, cellPixelSize, wallPixelThickness,
                player, showPlayerMarker: true, foe: dungeonManager.ActiveFoe, foeAlwaysVisible: dungeonManager.debugFoeAlwaysVisibleOnMap,
                showGrid: true, showAnnotations: true, handDrawn: true);
        }
    }
}
