using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Construye la escena "WinterRescue" (Taller de Laboratorio: Escena Invernal de Rescate)
/// en etapas que coinciden 1:1 con los commits atomicos del repositorio.
///
/// Uso (linea de comandos, modo batch):
///   Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildTerrainAndCar -quit
///   Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildLightsAndSirens -quit
///   Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.AttachSirenScript -quit
///   Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildParticles -quit
///   Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.RenderPreview -quit
/// </summary>
public static class WinterSceneBuilder
{
    const string ScenePath = "Assets/Scenes/WinterRescue.unity";
    const string CarModelPath = "Assets/Models/PoliceCar/police-car.fbx";
    const string MaterialDir = "Assets/Materials";

    // El coche queda enterrado: Police_Car vive por debajo de Y = 0.
    const float BuriedY = -0.5f;
    const float CarTargetLength = 4.7f;   // longitud real aproximada de una patrulla

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    static Scene OpenScene()
    {
        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static void SaveScene(Scene scene)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[WINTER] Escena guardada: " + ScenePath);
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }

    static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent)
            if (t.name == name) return t;
        return null;
    }

    static Bounds GetBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static Material GetOrCreateMaterial(string name, Color color, float smoothness, float metallic)
    {
        if (!Directory.Exists(MaterialDir)) Directory.CreateDirectory(MaterialDir);
        string path = MaterialDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material GetOrCreateParticleMaterial(string name, Texture2D tex)
    {
        if (!Directory.Exists(MaterialDir)) Directory.CreateDirectory(MaterialDir);
        string path = MaterialDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (tex != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D DefaultParticleTexture()
    {
        var tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (tex == null) tex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.png");
        return tex;
    }

    // ------------------------------------------------------------------
    // Etapa 2 - Composicion 3D base: terreno nevado + coche enterrado
    // ------------------------------------------------------------------

    public static void BuildTerrainAndCar()
    {
        var scene = OpenScene();

        // La plantilla URP trae un Global Volume que rompe el conteo exacto de
        // 10 objetos de la jerarquia: se elimina.
        var volume = FindRoot("Global Volume");
        if (volume != null) UnityEngine.Object.DestroyImmediate(volume);

        BuildSnowTerrain();
        var policeCar = BuildBuriedCar();
        FrameMainCamera(policeCar);

        SaveScene(scene);
    }

    /// <summary>Terreno nevado con dunas suaves y un monticulo de nieve alrededor del coche.</summary>
    static void BuildSnowTerrain()
    {
        const float size = 110f;         // 110 x 110 metros (mas alla del fog, para que no se vea el borde)
        const int res = 180;             // ~0.6 m por quad

        // Re-ejecutable: elimina una version previa del terreno.
        var previous = FindRoot("Snow_Terrain");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

        string meshPath = "Assets/Models/SnowTerrainMesh.asset";
        if (!Directory.Exists("Assets/Models")) Directory.CreateDirectory("Assets/Models");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = new Mesh();
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        mesh.Clear();
        mesh.name = "SnowTerrainMesh";
        mesh.indexFormat = IndexFormat.UInt32;

        var vertices = new Vector3[(res + 1) * (res + 1)];
        var uvs = new Vector2[vertices.Length];
        var normalsSrc = new Vector3[vertices.Length];

        for (int z = 0; z <= res; z++)
        {
            for (int x = 0; x <= res; x++)
            {
                int i = z * (res + 1) + x;
                float wx = (x / (float)res - 0.5f) * size;
                float wz = (z / (float)res - 0.5f) * size;

                // Dunas: Perlin compuesto, amplitud moderada.
                float dunes = Mathf.PerlinNoise(wx * 0.08f + 11.3f, wz * 0.08f + 4.7f) - 0.5f;
                float detail = Mathf.PerlinNoise(wx * 0.35f + 3.1f, wz * 0.35f + 9.9f) - 0.5f;

                // Cerca de la patrulla el terreno se aplana: la nieve forma una
                // plataforma continua alrededor del coche en vez de ondular.
                float r = Mathf.Sqrt(wx * wx + wz * wz);
                float flat01 = Mathf.InverseLerp(3.2f, 9.0f, r);
                float noiseAmp = flat01 * flat01 * (3f - 2f * flat01);   // smoothstep
                float h = (dunes * 0.85f + detail * 0.12f) * noiseAmp;

                // Capa de nieve acumulada sobre el coche (0.30 m) y cordon que se
                // levanta alrededor, como si el viento hubiera amontonado la nieve
                // contra la carroceria.
                h += 0.18f * Mathf.Exp(-Mathf.Pow(r / 3.6f, 2f));
                h += 0.26f * Mathf.Exp(-Mathf.Pow((r - 5.2f) / 2.1f, 2f));

                vertices[i] = new Vector3(wx, h, wz);
                uvs[i] = new Vector2(wx / 8f, wz / 8f);
            }
        }

        var triangles = new int[res * res * 6];
        int t = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                int i = z * (res + 1) + x;
                triangles[t++] = i;
                triangles[t++] = i + res + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + res + 1;
                triangles[t++] = i + res + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssets();

        var terrain = new GameObject("Snow_Terrain");
        terrain.transform.position = Vector3.zero;
        var mf = terrain.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = terrain.AddComponent<MeshRenderer>();
        mr.sharedMaterial = GetOrCreateMaterial("Snow_Material", new Color(0.90f, 0.94f, 1.00f), 0.10f, 0f);
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = true;
        var mc = terrain.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
    }

    /// <summary>Crea Police_Car (vacio) y su hijo Car_Mesh con el modelo CC0 importado.</summary>
    static GameObject BuildBuriedCar()
    {
        var previous = FindRoot("Police_Car");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

        var policeCar = new GameObject("Police_Car");
        policeCar.transform.position = Vector3.zero;   // se hunde al final

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CarModelPath);
        if (modelAsset == null)
            throw new Exception("No se encontro el modelo del coche en " + CarModelPath);

        var carMesh = (GameObject)UnityEngine.Object.Instantiate(modelAsset, policeCar.transform);
        carMesh.name = "Car_Mesh";
        carMesh.transform.localPosition = Vector3.zero;
        carMesh.transform.localScale = Vector3.one;

        // El FBX de Grab3D viene en espacio Z-up (Blender): el largo del coche esta
        // sobre +Y, la altura sobre +Z y el frente mira a -Y. Lo llevamos a Y-up con
        // el frente hacia +Z girando -90 grados sobre X.
        carMesh.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        // Verificacion automatica: si el coche quedo boca abajo (las ruedas por
        // encima de la carroceria) lo volteamos 180 grados sobre su eje longitudinal.
        if (IsUpsideDown(carMesh))
        {
            carMesh.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 180f);
            Debug.Log("[WINTER] El modelo venia invertido: se volteo 180 grados en Z.");
        }

        // Escala uniforme hasta la longitud real de una patrulla.
        var b = GetBounds(carMesh);
        if (b.size.x > b.size.z)
        {
            // Por si el export viniera con el largo sobre X.
            carMesh.transform.localRotation *= Quaternion.Euler(0f, 90f, 0f);
            b = GetBounds(carMesh);
        }
        float s = CarTargetLength / Mathf.Max(b.size.z, 0.001f);
        carMesh.transform.localScale = Vector3.one * s;

        // Apoyar las ruedas exactamente en local Y = 0 y centrar en XZ.
        b = GetBounds(carMesh);
        carMesh.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

        foreach (var r in carMesh.GetComponentsInChildren<Renderer>())
            r.shadowCastingMode = ShadowCastingMode.On;

        // Aseguramos materiales URP en el modelo importado (FBX + texturas .fbm).
        EnsureUrpMaterials(carMesh);

        b = GetBounds(carMesh);
        Debug.Log($"[WINTER] Car_Mesh bounds tras ajuste: size={b.size} min={b.min} max={b.max}");

        // Hundir la patrulla: la nieve cubre hasta las puertas.
        policeCar.transform.position = new Vector3(0f, BuriedY, 0f);
        policeCar.transform.rotation = Quaternion.Euler(0f, -18f, 0f);

        return policeCar;
    }

    /// <summary>Detecta si la carroceria quedo por debajo de las ruedas (modelo boca abajo).</summary>
    static bool IsUpsideDown(GameObject car)
    {
        float wheelY = 0f, bodyY = 0f;
        int nWheels = 0, nBody = 0;
        foreach (var t in car.GetComponentsInChildren<Transform>())
        {
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            string n = t.name.ToLowerInvariant();
            if (n.Contains("wheel"))
            {
                wheelY += r.bounds.center.y;
                nWheels++;
            }
            else if (n.Contains("body"))
            {
                bodyY += r.bounds.center.y;
                nBody++;
            }
        }
        if (nWheels == 0 || nBody == 0) return false;
        return (wheelY / nWheels) > (bodyY / nBody);
    }

    /// <summary>Metricas del coche medidas sobre el modelo real (en espacio local de Police_Car).</summary>
    static void GetCarMetrics(GameObject policeCar, out float roofY, out float carLength)
    {
        roofY = 1.35f;
        carLength = CarTargetLength;
        var t = policeCar.transform;
        foreach (Transform child in t)
        {
            if (child.name != "Car_Mesh") continue;
            var renderers = child.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var wb = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) wb.Encapsulate(renderers[i].bounds);
            // La rotacion de Police_Car es solo en Y, asi que la altura del techo
            // se puede leer directo del AABB del modelo.
            roofY = t.InverseTransformPoint(wb.max).y;
            // BuildBuriedCar normaliza el modelo a esta longitud exacta.
            carLength = CarTargetLength;
            return;
        }
    }

    /// <summary>Garantiza que cada material del modelo use URP/Lit conservando sus texturas.</summary>
    static void EnsureUrpMaterials(GameObject carMesh)
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        var map = new System.Collections.Generic.Dictionary<Material, Material>();
        foreach (var r in carMesh.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (src.shader != null && src.shader.name.StartsWith("Universal Render Pipeline")) continue;

                if (!map.TryGetValue(src, out var dst))
                {
                    dst = new Material(urpLit) { name = src.name + "_URP" };
                    if (src.HasProperty("_MainTex") && src.mainTexture != null)
                        dst.SetTexture("_BaseMap", src.mainTexture);
                    if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap") != null)
                    {
                        dst.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
                        dst.EnableKeyword("_NORMALMAP");
                    }
                    dst.color = src.HasProperty("_Color") ? src.color : Color.white;
                    if (dst.HasProperty("_BaseColor") && src.HasProperty("_Color"))
                        dst.SetColor("_BaseColor", src.color);
                    string path = MaterialDir + "/" + Sanitize(dst.name) + ".mat";
                    if (!File.Exists(path))
                    {
                        Directory.CreateDirectory(MaterialDir);
                        AssetDatabase.CreateAsset(dst, path);
                    }
                    map[src] = dst;
                }
                mats[i] = map[src];
            }
            r.sharedMaterials = mats;
        }
        AssetDatabase.SaveAssets();
    }

    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    /// <summary>Encuadre lateral/diagonal clasico de tres cuartos.</summary>
    static void FrameMainCamera(GameObject policeCar)
    {
        var camGo = FindRoot("Main Camera");
        if (camGo == null)
        {
            camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.035f, 0.045f, 0.075f);
        cam.fieldOfView = 48f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        cam.allowHDR = true;

        camGo.transform.position = new Vector3(4.8f, 2.00f, -5.6f);
        var lookAt = new Vector3(0f, 0.45f, 0f);
        camGo.transform.rotation = Quaternion.LookRotation(lookAt - camGo.transform.position, Vector3.up);

        // Ambiente de noche de tormenta: fog + luz ambiental azulada muy baja.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.055f, 0.075f, 0.125f);
        RenderSettings.fogStartDistance = 11f;
        RenderSettings.fogEndDistance = 52f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.15f, 0.22f);
        RenderSettings.skybox = null;
        RenderSettings.reflectionIntensity = 0.25f;
    }

    // ------------------------------------------------------------------
    // Etapa 3 - Jerarquia de luces y sirenas (sin codigo todavia)
    // ------------------------------------------------------------------

    public static void BuildLightsAndSirens()
    {
        var scene = OpenScene();

        // 2. Moon_Light (Directional): azul muy palido, intensidad baja.
        var dirGo = FindRoot("Moon_Light") ?? FindRoot("Directional Light");
        if (dirGo == null)
        {
            dirGo = new GameObject("Moon_Light");
            dirGo.AddComponent<Light>();
        }
        dirGo.name = "Moon_Light";
        var moon = dirGo.GetComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.66f, 0.78f, 1.00f);
        moon.intensity = 0.60f;
        moon.shadows = LightShadows.Soft;
        moon.shadowStrength = 0.85f;
        dirGo.transform.position = new Vector3(0f, 18f, 0f);
        dirGo.transform.rotation = Quaternion.Euler(28f, -142f, 0f);

        var policeCar = FindRoot("Police_Car");
        if (policeCar == null) throw new Exception("Police_Car no existe: corre primero BuildTerrainAndCar");

        GetCarMetrics(policeCar, out float roofY, out float carLength);

        // 6. Siren_Pivot en el techo del coche (aqui se montara SirenRotation.cs).
        var pivot = FindChild(policeCar.transform, "Siren_Pivot");
        if (pivot == null)
        {
            var pivotGo = new GameObject("Siren_Pivot");
            pivot = pivotGo.transform;
            pivot.SetParent(policeCar.transform, false);
        }
        pivot.localPosition = new Vector3(0f, roofY + 0.03f, -0.15f);
        pivot.localRotation = Quaternion.identity;

        // 7. Red_Light y 8. Blue_Light como hijos del pivote: al girar el pivote
        //    las dos luces barren la nieve en circulos opuestos.
        CreateSirenLight(pivot, "Red_Light", new Vector3(0.42f, 0f, 0f), new Color(1f, 0.12f, 0.10f), 26f, 0f);
        CreateSirenLight(pivot, "Blue_Light", new Vector3(-0.42f, 0f, 0f), new Color(0.12f, 0.32f, 1f), 26f, 180f);

        SaveScene(scene);
    }

    static void CreateSirenLight(Transform pivot, string name, Vector3 localPos, Color color, float range, float yaw)
    {
        var existing = FindChild(pivot, name);
        var go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(pivot, false);

        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        var light = go.GetComponent<Light>();
        if (light == null) light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = 22f;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;

        // Datos extra de URP (si el paquete los expone).
        var urpType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
        if (urpType != null && go.GetComponent(urpType) == null) go.AddComponent(urpType);
    }

    // ------------------------------------------------------------------
    // Etapa 4 - Script SirenRotation.cs asignado al pivote
    // ------------------------------------------------------------------

    public static void AttachSirenScript()
    {
        var scene = OpenScene();

        var policeCar = FindRoot("Police_Car");
        var pivot = FindChild(policeCar.transform, "Siren_Pivot");
        if (pivot == null) throw new Exception("Siren_Pivot no existe");

        var siren = pivot.GetComponent<SirenRotation>();
        if (siren == null) siren = pivot.gameObject.AddComponent<SirenRotation>();
        siren.rotationSpeed = 180f;

        // Solo el pivote debe tener el script.
        foreach (var other in UnityEngine.Object.FindObjectsByType<SirenRotation>(FindObjectsSortMode.None))
            if (other.transform != pivot) UnityEngine.Object.DestroyImmediate(other);

        EditorUtility.SetDirty(pivot.gameObject);
        SaveScene(scene);
        Debug.Log("[WINTER] SirenRotation asignado a Siren_Pivot con rotationSpeed=" + siren.rotationSpeed);
    }

    // ------------------------------------------------------------------
    // Etapa 5 - Sistemas de particulas: tormenta global + humo de escape
    // ------------------------------------------------------------------

    public static void BuildParticles()
    {
        var scene = OpenScene();

        var policeCar = FindRoot("Police_Car");
        GetCarMetrics(policeCar, out float roofY, out float carLength);

        BuildExhaustSmoke(policeCar.transform, carLength);
        BuildGlobalBlizzard();

        SaveScene(scene);
    }

    /// <summary>Humo gris del escape: cono estrecho, crece y se desvanece.</summary>
    static void BuildExhaustSmoke(Transform policeCar, float carLength)
    {
        var existing = FindChild(policeCar, "Exhaust_Smoke");
        var go = existing != null ? existing.gameObject : new GameObject("Exhaust_Smoke");
        if (existing == null) go.transform.SetParent(policeCar, false);

        // Parte trasera del coche (-Z), justo por encima de la capa de nieve.
        go.transform.localPosition = new Vector3(0.55f, 0.72f, -carLength * 0.5f + 0.28f);
        go.transform.localRotation = Quaternion.Euler(-105f, 0f, 0f);

        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.62f, 0.62f, 0.66f, 0.55f);
        main.gravityModifier = -0.015f;          // el humo caliente sube
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 260;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(26f, 34f);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;                        // cono muy estrecho
        shape.radius = 0.05f;
        shape.radiusThickness = 0.3f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(ExhaustGradient());

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        var grow = new AnimationCurve();
        grow.AddKey(0f, 0.55f);
        grow.AddKey(0.35f, 1.35f);
        grow.AddKey(1f, 3.10f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, grow);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.22f;
        noise.frequency = 0.55f;
        noise.scrollSpeed = 0.35f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetOrCreateParticleMaterial("Exhaust_Smoke_Material", DefaultParticleTexture());
        renderer.sortingFudge = 0f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    static Gradient ExhaustGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.72f, 0.73f, 0.78f), 0.00f),
                new GradientColorKey(new Color(0.55f, 0.57f, 0.62f), 0.35f),
                new GradientColorKey(new Color(0.40f, 0.42f, 0.48f), 0.75f),
                new GradientColorKey(new Color(0.35f, 0.37f, 0.43f), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.00f, 0.00f),
                new GradientAlphaKey(0.55f, 0.10f),
                new GradientAlphaKey(0.30f, 0.55f),
                new GradientAlphaKey(0.00f, 1.00f)
            });
        return g;
    }

    /// <summary>Tormenta de nieve global: caja sobre la escena, gravedad moderada y viento.</summary>
    static void BuildGlobalBlizzard()
    {
        var go = FindRoot("Global_Blizzard");
        if (go == null) go = new GameObject("Global_Blizzard");

        go.transform.position = new Vector3(0f, 12.5f, 0f);
        // El +Z local del emisor apunta hacia abajo: la caja emite desde el cielo.
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = Vector3.one;

        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.135f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.86f, 0.92f, 1.00f, 0.90f),
            new Color(0.74f, 0.83f, 1.00f, 0.55f));
        main.gravityModifier = 0.30f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 6000;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(800f, 1100f);

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(46f, 0.5f, 46f);   // plano de emision amplio
        shape.position = Vector3.zero;

        // Viento lateral de la tormenta.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.6f, -0.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f);

        // Rotacion sobre si mismas para que no se vean como puntos estaticos.
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2.2f, 2.2f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetOrCreateParticleMaterial("Blizzard_Snow_Material", DefaultParticleTexture());
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
    }

    // ------------------------------------------------------------------
    // Construccion completa (todas las etapas + snapshots por commit)
    // ------------------------------------------------------------------

    /// <summary>
    /// Ejecuta las cuatro etapas de construccion y guarda una copia de la escena
    /// despues de cada una, para poder revisar/entregar el avance por etapas.
    /// </summary>
    public static void BuildAllStages()
    {
        BuildTerrainAndCar();
        Snapshot(2);
        BuildLightsAndSirens();
        Snapshot(3);
        AttachSirenScript();
        Snapshot(4);
        BuildParticles();
        Snapshot(5);
    }

    static void Snapshot(int stage)
    {
        var scene = EditorSceneManager.GetActiveScene();
        string path = "Assets/Scenes/_snap" + stage + ".unity";
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log("[WINTER] Snapshot etapa " + stage + " -> " + path);
        // Volvemos a la escena de trabajo para que la siguiente etapa escriba en ella.
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }


    // ------------------------------------------------------------------
    // Diagnostico del modelo importado
    // ------------------------------------------------------------------

    public static void ProbeCarModel()
    {
        OpenScene();
        var policeCar = FindRoot("Police_Car");
        var carMesh = FindChild(policeCar.transform, "Car_Mesh");
        Debug.Log("[PROBE] Car_Mesh localPos=" + carMesh.localPosition + " localScale=" + carMesh.localScale + " localRot=" + carMesh.localEulerAngles);
        DumpRec(carMesh, 0);
    }

    static void DumpRec(Transform t, int depth)
    {
        string pad = new string(' ', depth * 2);
        var mf = t.GetComponent<MeshFilter>();
        string meshInfo = "";
        if (mf != null && mf.sharedMesh != null)
        {
            var mb = mf.sharedMesh.bounds;
            meshInfo = $" mesh=#{mf.sharedMesh.vertexCount} meshBounds center={mb.center} size={mb.size}";
        }
        string rendInfo = "";
        foreach (var r in t.GetComponents<Renderer>())
            rendInfo += $" [{r.GetType().Name} worldBounds center={r.bounds.center} size={r.bounds.size}]";
        Debug.Log($"[PROBE] {pad}{t.name} pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale}{meshInfo}{rendInfo}");
        foreach (Transform c in t) DumpRec(c, depth + 1);
    }


    // ------------------------------------------------------------------
    // Verificacion automatica de la entrega
    // ------------------------------------------------------------------

    public static void VerifyScene()
    {
        var scene = OpenScene();
        var roots = scene.GetRootGameObjects();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[VERIFY] Objetos raiz (" + roots.Length + "):");
        foreach (var r in roots)
        {
            string tag = r.tag;
            string comps = string.Join(",", System.Array.ConvertAll(r.GetComponents<Component>(), c => c.GetType().Name));
            sb.AppendLine($"  - {r.name} tag={tag} pos={r.transform.position} rot={r.transform.eulerAngles} comps=[{comps}]");
            foreach (Transform c in r.transform)
            {
                var anyLight = c.GetComponent<Light>();
                var anyPs = c.GetComponent<ParticleSystem>();
                var siren = c.GetComponent<SirenRotation>();
                string extra = anyLight != null
                    ? $" Light({anyLight.type} color={anyLight.color} intensity={anyLight.intensity} range={anyLight.range})"
                    : (anyPs != null ? " ParticleSystem" : "");
                if (siren != null) extra += $" SirenRotation(speed={siren.rotationSpeed})";
                sb.AppendLine($"      > {c.name} localPos={c.localPosition} localRot={c.localEulerAngles}{extra}");
                foreach (Transform g in c)
                {
                    var gl = g.GetComponent<Light>();
                    var gs = g.GetComponent<SirenRotation>();
                    string ge = gl != null ? $" Light({gl.type} color={gl.color} intensity={gl.intensity} range={gl.range})" : "";
                    if (gs != null) ge += $" SirenRotation(speed={gs.rotationSpeed})";
                    sb.AppendLine($"          >> {g.name} localPos={g.localPosition} localRot={g.localEulerAngles}{ge}");
                    foreach (Transform gg in g)
                        sb.AppendLine($"              >>> {gg.name} localPos={gg.localPosition}");
                }
            }
        }

        var car = FindRoot("Police_Car");
        var meshT = FindChild(car.transform, "Car_Mesh");
        var meshBounds = GetBounds(meshT.gameObject);
        sb.AppendLine($"[VERIFY] Police_Car.y={car.transform.position.y} Car_Mesh worldMinY={meshBounds.min.y} roofY={meshBounds.max.y} size={meshBounds.size}");

        var terrain = FindRoot("Snow_Terrain");
        var mf = terrain.GetComponent<MeshFilter>();
        var mc = terrain.GetComponent<MeshCollider>();
        sb.AppendLine($"[VERIFY] Snow_Terrain mesh verts={mf.sharedMesh.vertexCount} bounds={mf.sharedMesh.bounds} collider={(mc != null)} material={terrain.GetComponent<Renderer>().sharedMaterial.name} shader={terrain.GetComponent<Renderer>().sharedMaterial.shader.name}");

        // Altura de la nieve alrededor del coche (para comprobar el entierro).
        var heights = new System.Text.StringBuilder();
        foreach (var p in new[] { new Vector3(0,0,0), new Vector3(0.9f,0,2.3f), new Vector3(-0.9f,0,-2.3f), new Vector3(0,0,3.2f), new Vector3(0,0,-3.2f), new Vector3(2.2f,0,0) })
        {
            var origin = new Vector3(p.x, 12f, p.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 40f))
                heights.Append($"({p.x},{p.z})->{hit.point.y:F2} ");
        }
        sb.AppendLine("[VERIFY] Altura de nieve alrededor: " + heights);

        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            var main = ps.main;
            var shape = ps.shape;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            sb.AppendLine($"[VERIFY] PS {ps.name}: shape={shape.shapeType} shapeScale={shape.scale} rate={ps.emission.rateOverTime.constant} life={main.startLifetime.constantMin}-{main.startLifetime.constantMax} speed={main.startSpeed.constantMin}-{main.startSpeed.constantMax} size={main.startSize.constantMin}-{main.startSize.constantMax} gravity={main.gravityModifier.constant} maxParticles={main.maxParticles} mat={(r.sharedMaterial != null ? r.sharedMaterial.name : "NULL")} shadowCasters={r.shadowCastingMode}");
        }

        var cam = FindRoot("Main Camera");
        sb.AppendLine($"[VERIFY] Camera pos={cam.transform.position} rot={cam.transform.eulerAngles} fov={cam.GetComponent<Camera>().fieldOfView} tag={cam.tag}");
        var moon = FindRoot("Moon_Light");
        sb.AppendLine($"[VERIFY] Moon pos={moon.transform.position} rot={moon.transform.eulerAngles}");

        Debug.Log(sb.ToString());
    }

    // ------------------------------------------------------------------
    // Render de verificacion (QA visual)
    // ------------------------------------------------------------------

    public static void RenderPreview()
    {
        OpenScene();

        string outDir = Path.Combine(Application.dataPath, "../Screenshots");
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "WinterRescue_preview.png");

        var cam = Camera.main;
        if (cam == null) throw new Exception("No hay Main Camera");

        // Adelantar las particulas para que la escena se vea "llena".
        foreach (var ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play();
            ps.Simulate(4.0f, true, true);
        }

        // Rotacion de la sirena (para ver el efecto en el render).
        var pivot = FindChild(FindRoot("Police_Car").transform, "Siren_Pivot");
        if (pivot != null) pivot.localRotation = Quaternion.Euler(0f, 55f, 0f);

        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(outPath, tex.EncodeToPNG());

        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);

        Debug.Log("[WINTER] Preview renderizado en " + outPath);
    }
}
