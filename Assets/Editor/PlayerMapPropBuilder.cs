using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Gameplay;

// Arma el "mapa fisico" que el personaje levanta cerca de camara (tecla M, ver
// GridPlayerController/PlayerMapViewer): un GameObject colgado de la camara con 2 capas de
// SpriteRenderer (Paper fijo + Drawing que PlayerMapViewer repinta en vivo) y un Animator de 4
// estados (Closed/Opening/Open/Closing) que mueve ese GameObject de una pose escondida a una pose
// "sostenida" cerca de camara y de vuelta. Las herramientas (Pared/Pintar piso/Simbolos) siguen
// en 2D (PlayerMapEditorHUD), pero el mapa en si -- grilla, dibujo, todo -- se ve en este sprite
// 3D; PlayerMapEditorHUD calcula donde cae en pantalla proyectando los bordes de Drawing.bounds,
// asi el click SIEMPRE cae exacto sobre lo que se esta viendo, sin duplicar el dibujo en 2D.
//
// Sigue el mismo patron que el resto de Assets/Editor (BattleSceneBuilder, DungeonSceneBuilder):
// arma todo por codigo en vez de guardar el prefab/controller a mano, para que sea reproducible
// y facil de tocar. DungeonSceneBuilder.Build() llama a Attach() al armar la escena; tambien se
// puede correr solo (por si alguna vez hace falta regenerar solo el mapa fisico sin rehacer toda
// la mazmorra) desde el menu.
public static class PlayerMapPropBuilder
{
    private const string DataFolder = "Assets/Data/PlayerMap";

    // Poses en espacio local de la camara (el prop es hijo de la camara, ve GridPlayerController:
    // cameraBobTarget/Camera.main): Z positivo = adelante de la camara (nearClipPlane = 0.05, ver
    // DungeonSceneBuilder, asi que 0.5-0.65 queda comodo sin clippear). Closed lo deja abajo y
    // chico (fuera del cuadro, "guardado").
    //
    // Open: a z=0.62 el alto visible del frustum (FOV vertical default 60°) es ~0.72 unidades
    // (mitad = 0.358), y el papel mide OpenScale.y de alto (mitad = 0.23) -- para que quepa
    // ENTERO sin importar donde lo centres, el centro Y tiene que quedar dentro de
    // [-(0.358-0.23), +(0.358-0.23)] = [-0.128, +0.128]. y=-0.10 lo baja para que se sienta
    // "agarrado" por el personaje (mas cerca del borde de abajo de la pantalla) dejando ~0.03 de
    // margen antes de que el frustum lo corte -- si se lo baja mas, hay que compensar agrandando
    // z o achicando OpenScale para no reabrir el bug de "el mapa no esta entero en pantalla". Son
    // valores de arranque -- quedan como AnimationClips de verdad, editables a mano en la ventana
    // Animation si hace falta ajustarlos.
    private static readonly Vector3 ClosedPos = new Vector3(0.22f, -0.85f, 0.6f);
    private static readonly Vector3 ClosedScale = new Vector3(0.45f, 0.45f, 0.45f);
    private static readonly Vector3 OpenPos = new Vector3(0f, -0.10f, 0.62f);
    private static readonly Vector3 OpenScale = new Vector3(0.46f, 0.46f, 0.46f);
    private const float OpeningDuration = 0.45f;
    private const float ClosingDuration = 0.32f;
    private const float IdleLoopDuration = 3f;
    // Antes tenia un vaiven propio (0.012) que se sumaba SIEMPRE mientras el mapa estaba abierto,
    // incluso parado -- eso dificultaba clickear una arista con precision (a pedido: "el mapa no
    // debe moverse mucho"). Bajado a 0: el UNICO movimiento del mapa ahora es el bob real de
    // caminata (GridPlayerController.ApplyBob, que mueve la camara -- y el mapa la sigue por ser
    // su hijo), que solo pasa mientras el personaje esta efectivamente caminando.
    private const float IdleBobAmount = 0f;

    [MenuItem("Dungeon/Build Player Map Prop")]
    public static void BuildStandalone()
    {
        var cam = Camera.main;
        var manager = Object.FindObjectOfType<DungeonManager>();
        var playerController = Object.FindObjectOfType<GridPlayerController>();
        var pauseMenu = Object.FindObjectOfType<PauseMenuManager>();
        if (cam == null || manager == null || playerController == null)
        {
            Debug.LogError("PlayerMapPropBuilder: hace falta una camara principal, un DungeonManager y un GridPlayerController en la escena abierta.");
            return;
        }
        Attach(cam.transform, manager, playerController, pauseMenu);
    }

    public static PlayerMapViewer Attach(Transform cameraTransform, DungeonManager manager, GridPlayerController playerController, PauseMenuManager pauseMenu)
    {
        EnsureFolder();

        var root = new GameObject("PlayerMapProp");
        root.transform.SetParent(cameraTransform, false);
        root.transform.localPosition = ClosedPos;
        root.transform.localScale = ClosedScale;

        var paperGo = new GameObject("Paper");
        paperGo.transform.SetParent(root.transform, false);
        var paperRenderer = paperGo.AddComponent<SpriteRenderer>();
        paperRenderer.sprite = BuildPaperSprite();
        paperRenderer.sortingOrder = 0;

        var drawingGo = new GameObject("Drawing");
        drawingGo.transform.SetParent(root.transform, false);
        drawingGo.transform.localPosition = new Vector3(0f, 0f, -0.01f); // apenas mas cerca de camara que el papel, para que nunca compita en el sorting
        var drawingRenderer = drawingGo.AddComponent<SpriteRenderer>();
        drawingRenderer.sortingOrder = 1;

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = BuildController();

        var viewer = root.AddComponent<PlayerMapViewer>();
        viewer.dungeonManager = manager;
        viewer.player = playerController;
        viewer.pauseMenu = pauseMenu;
        viewer.animator = animator;
        viewer.drawingRenderer = drawingRenderer;

        playerController.mapViewer = viewer;

        return viewer;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");
        if (!AssetDatabase.IsValidFolder(DataFolder))
            AssetDatabase.CreateFolder("Assets/Data", "PlayerMap");
    }

    // Papel liso con un borde levemente mas oscuro (nada de arte externo, mismo criterio que
    // ParticleTextureFactory: todo generado en runtime/editor por codigo) -- 64x64, FilterMode
    // Point para que quede nitido/pixel-art como el resto de la UI del prototipo.
    private static Sprite BuildPaperSprite()
    {
        const string texPath = DataFolder + "/PlayerMapPaperTexture.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        if (existing != null) return existing;

        const int size = 64;
        const int border = 3;
        var fill = new Color(0.82f, 0.72f, 0.52f);
        var edge = new Color(0.55f, 0.45f, 0.3f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onEdge = x < border || y < border || x >= size - border || y >= size - border;
                pixels[y * size + x] = onEdge ? edge : fill;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.name = "PlayerMapPaper";
        AssetDatabase.CreateAsset(tex, texPath);
        AssetDatabase.AddObjectToAsset(sprite, tex);
        AssetDatabase.SaveAssets();
        return sprite;
    }

    private static AnimatorController BuildController()
    {
        const string path = DataFolder + "/PlayerMapPropController.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing != null) return existing;

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Open", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Close", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        var closedClip = CreatePoseClip("PlayerMapProp_Closed", ClosedPos, ClosedScale);
        var openingClip = CreateMoveClip("PlayerMapProp_Opening", ClosedPos, ClosedScale, OpenPos, OpenScale, OpeningDuration);
        var openClip = CreateIdleClip("PlayerMapProp_OpenIdle", OpenPos, OpenScale);
        var closingClip = CreateMoveClip("PlayerMapProp_Closing", OpenPos, OpenScale, ClosedPos, ClosedScale, ClosingDuration);

        var closedState = sm.AddState("Closed", new Vector3(0, 0, 0));
        closedState.motion = closedClip;
        var openingState = sm.AddState("Opening", new Vector3(250, 0, 0));
        openingState.motion = openingClip;
        var openState = sm.AddState("Open", new Vector3(500, 0, 0));
        openState.motion = openClip;
        var closingState = sm.AddState("Closing", new Vector3(250, 150, 0));
        closingState.motion = closingClip;

        sm.defaultState = closedState;

        AddTrigger(closedState, openingState, "Open");
        AddExitTime(openingState, openState);
        AddTrigger(openState, closingState, "Close");
        AddExitTime(closingState, closedState);
        // Interrumpir a medio camino (apretar M de nuevo mientras esta abriendo/cerrando) en vez
        // de ignorar el input hasta que termine la animacion en curso.
        AddTrigger(openingState, closingState, "Close");
        AddTrigger(closingState, openingState, "Open");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static void AddTrigger(AnimatorState from, AnimatorState to, string param)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0.08f;
        t.AddCondition(AnimatorConditionMode.If, 0, param);
    }

    private static void AddExitTime(AnimatorState from, AnimatorState to)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = 1f;
        t.duration = 0f;
    }

    private static AnimationClip CreatePoseClip(string name, Vector3 pos, Vector3 scale)
    {
        var clip = NewClip(name);
        SetConstantVector3Curve(clip, "m_LocalPosition", pos);
        SetConstantVector3Curve(clip, "m_LocalScale", scale);
        SaveClip(clip, name);
        return clip;
    }

    private static AnimationClip CreateMoveClip(string name, Vector3 fromPos, Vector3 fromScale, Vector3 toPos, Vector3 toScale, float duration)
    {
        var clip = NewClip(name);
        SetEasedVector3Curve(clip, "m_LocalPosition", fromPos, toPos, duration);
        SetEasedVector3Curve(clip, "m_LocalScale", fromScale, toScale, duration);
        SaveClip(clip, name);
        return clip;
    }

    // Vaiven sutil sosteniendo el mapa abierto: sube y baja una vez por ciclo (loop) sin mover el
    // ancho/alto, solo para que no se sienta un cartel estatico pegado a la pantalla.
    private static AnimationClip CreateIdleClip(string name, Vector3 pos, Vector3 scale)
    {
        var clip = NewClip(name);
        var yKeys = new[]
        {
            new Keyframe(0f, pos.y),
            new Keyframe(IdleLoopDuration * 0.5f, pos.y + IdleBobAmount),
            new Keyframe(IdleLoopDuration, pos.y),
        };
        var yCurve = new AnimationCurve(yKeys);
        for (int i = 0; i < yCurve.length; i++) yCurve.SmoothTangents(i, 0f);

        clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", ConstantCurve(pos.x, IdleLoopDuration));
        clip.SetCurve("", typeof(Transform), "m_LocalPosition.y", yCurve);
        clip.SetCurve("", typeof(Transform), "m_LocalPosition.z", ConstantCurve(pos.z, IdleLoopDuration));
        SetConstantVector3Curve(clip, "m_LocalScale", scale);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        SaveClip(clip, name);
        return clip;
    }

    private static AnimationClip NewClip(string name)
    {
        var clip = new AnimationClip { name = name };
        clip.frameRate = 30f;
        return clip;
    }

    private static void SaveClip(AnimationClip clip, string name)
    {
        string path = $"{DataFolder}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing == null) AssetDatabase.CreateAsset(clip, path);
    }

    private static void SetConstantVector3Curve(AnimationClip clip, string property, Vector3 v)
    {
        clip.SetCurve("", typeof(Transform), property + ".x", ConstantCurve(v.x, 0.01f));
        clip.SetCurve("", typeof(Transform), property + ".y", ConstantCurve(v.y, 0.01f));
        clip.SetCurve("", typeof(Transform), property + ".z", ConstantCurve(v.z, 0.01f));
    }

    private static void SetEasedVector3Curve(AnimationClip clip, string property, Vector3 from, Vector3 to, float duration)
    {
        clip.SetCurve("", typeof(Transform), property + ".x", AnimationCurve.EaseInOut(0f, from.x, duration, to.x));
        clip.SetCurve("", typeof(Transform), property + ".y", AnimationCurve.EaseInOut(0f, from.y, duration, to.y));
        clip.SetCurve("", typeof(Transform), property + ".z", AnimationCurve.EaseInOut(0f, from.z, duration, to.z));
    }

    private static AnimationCurve ConstantCurve(float value, float duration)
    {
        return new AnimationCurve(new Keyframe(0f, value), new Keyframe(Mathf.Max(duration, 0.01f), value));
    }
}
