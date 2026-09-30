using UnityEngine;

namespace Gameplay
{
    // Capas de parallax con arte real pegadas enfrente de una pared de bosque (ver Custom/
    // ForestLayerCutout y DungeonLevelBuilder.BuildWall): 2 quads transparentes (lejos/cerca), cada
    // uno con una de las ilustraciones de Assets/Sprites/Forest/ (paquete de Asset Store
    // reincorporado por pedido puntual: "aplicar esta vez si parallax" -- las versiones anteriores
    // de esto eran una simulacion via shader/ruido procedural porque todavia no habia arte real).
    //
    // Separado de DungeonLevelBuilder (mismo patron que GroundTileFactory) porque es geometria
    // puramente decorativa: no tiene collider, no participa del padding vertical anti-z-fighting de
    // la pared real (ver el comentario largo junto a BuildWall sobre el bug historico de
    // Custom/ForestWallParallax) -- estos quads se paran BIEN AFUERA del volumen solido de la
    // pared (mas alla de wallThickness/2), nunca adentro ni coincidiendo con su superficie, asi que
    // no pueden reabrir ese bug aunque esten hechos de geometria real (a diferencia del shader
    // procedural, que evitaba el problema no usando geometria de mas para nada).
    public static class ForestParallaxWallFactory
    {
        // Cuanto se despega cada capa de la CARA de la pared (mas alla de wallThickness/2, que es
        // donde termina el volumen solido) -- chico a proposito, esto es decoracion pegada a la
        // pared, no una escena real por detras. La capa "cerca" se para mas afuera (hacia el
        // pasillo) para reforzar la ilusion de profundidad junto con su _ParallaxStrength mas alto
        // en el material.
        private const float FarGap = 0.02f;
        private const float NearGap = 0.07f;

        public static void Build(Transform parent, Vector3 wallCenter, Vector3 outwardNormal, float cellSize, float wallHeight, float wallThickness, Material farMaterial, Material nearMaterial)
        {
            if (farMaterial == null && nearMaterial == null) return;

            // Una pared se construye UNA sola vez pero separa DOS celdas (ver el comentario sobre
            // isPrimaryDir en Build()) y se puede ver caminando desde cualquiera de las dos -- un
            // quad de un solo lado queda TAPADO por el volumen opaco del cubo cuando se mira desde
            // el otro lado (el cubo, con su wallThickness real, se interpone entre la camara y el
            // quad). Por eso se construye el mismo par de capas de los DOS lados: +normal y
            // -normal.
            float halfThickness = wallThickness * 0.5f;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 faceNormal = outwardNormal * side;
                if (farMaterial != null)
                    BuildLayer(parent, wallCenter, faceNormal, cellSize, wallHeight, halfThickness + FarGap, farMaterial, "ForestLayerFar");
                if (nearMaterial != null)
                    BuildLayer(parent, wallCenter, faceNormal, cellSize, wallHeight, halfThickness + NearGap, nearMaterial, "ForestLayerNear");
            }
        }

        private static void BuildLayer(Transform parent, Vector3 wallCenter, Vector3 outwardNormal, float cellSize, float wallHeight, float offset, Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            Vector3 pos = wallCenter + outwardNormal * offset;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-outwardNormal, Vector3.up);
            // Tamano COMPLETO (pedido puntual: antes se achicaba 0.96/0.94 "para no asomar por la
            // esquina", pero eso dejaba un hueco de ancho inconsistente entre paredes vecinas en
            // vez de una separacion prolija -- el mismo ancho que ya usa el cubo solido de la pared
            // (BuildWall, sin recortar el largo horizontal a proposito, ver el comentario ahi: el
            // pique minusculo en la esquina es preferible a un agujero real). El borde oscuro
            // parejo ahora lo dibuja el shader por UV (ver Custom/ForestLayerCutout _BorderWidth),
            // no la geometria.
            go.transform.localScale = new Vector3(cellSize, wallHeight, 1f);
            go.isStatic = true;

            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = mat;

            // Variacion entre paredes (pedido puntual: "que no se vean tan repetitivo"): un hash
            // 0-1 por posicion, pintado en el color de vertice de una COPIA del mesh (nunca el mesh
            // compartido de PrimitiveType.Quad -- pintarlo sin clonar cambiaria el color de TODOS
            // los quads del proyecto que usan ese mismo asset). El shader (Custom/
            // ForestLayerCutout) lee ese hash y corre su UV de muestreo -- variacion real sin
            // MaterialPropertyBlock ni un material unico por pared, asi que el static batching
            // sigue agrupando todo por material como antes.
            var meshFilter = go.GetComponent<MeshFilter>();
            var mesh = Object.Instantiate(meshFilter.sharedMesh);
            float hash = HashToUnit(pos);
            var colors = new Color[mesh.vertexCount];
            for (int k = 0; k < colors.Length; k++) colors[k] = new Color(hash, 0f, 0f, 0f);
            mesh.colors = colors;
            meshFilter.sharedMesh = mesh;
        }

        // Hash determinístico de posicion -> [0,1), mismo tipo de formula que ya usan los shaders
        // del proyecto (Custom/StarlitFloor.hash2) pero en C# para bakearlo una vez al construir en
        // vez de recalcularlo en cada fragmento.
        private static float HashToUnit(Vector3 p)
        {
            float n = Mathf.Sin(Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f;
            return n - Mathf.Floor(n);
        }
    }
}
