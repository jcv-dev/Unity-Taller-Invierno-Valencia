# Taller 2 — Escena Invernal de Rescate

**Módulo:** Composición de Escenas y Control de Transformaciones
**Escena:** `Assets/Scenes/WinterRescue.unity`
**Motor:** Unity **6000.6.6f1** con **URP** (Universal Render Pipeline 17.6)

Una patrulla de policía quedó atascada y semienterrada en la nieve. El motor sigue
encendido (humo en el escape), las luces de emergencia giran sobre el techo y una
tormenta de nieve arrecia sobre el entorno.

---

## 1. Cómo abrir el proyecto

1. Abrir el proyecto con Unity Hub usando Unity **6000.6.6f1** (o cualquier 6000.6.x).
2. Abrir la escena `Assets/Scenes/WinterRescue.unity`.
3. Pulsar **Play**.

No se requiere ningún asset de la Asset Store: el modelo del coche está incluido
y es de dominio público (ver [Créditos](#7-créditos-y-licencias)).

---

## 2. Jerarquía de la escena (10 objetos)

| # | Objeto | Tipo / componentes | Posición | Función |
|---|--------|--------------------|----------|---------|
| 1 | `Main Camera` | Camera + `UniversalAdditionalCameraData` | `(4.8, 2.00, -5.6)` | Encuadre diagonal de tres cuartos sobre la patrulla. FOV 48, fondo azul noche sólido. |
| 2 | `Moon_Light` | Directional Light | `(0, 18, 0)` rot `(28, 218, 0)` | Luz general azul muy pálida `(0.66, 0.78, 1.00)`, intensidad **0.60**, sombras suaves. |
| 3 | `Snow_Terrain` | MeshFilter + MeshRenderer + MeshCollider | `(0, 0, 0)` | Terreno nevado de 110 × 110 m con dunas suaves y un cordón de nieve alrededor del coche. Material `Snow_Material` (URP/Lit). |
| 4 | `Police_Car` | Empty (Transform) | **`(0, -0.5, 0)`** rot `(0, 342, 0)` | Objeto padre de la patrulla. Hundido en Y para simular que está enterrado. |
| 5 | `↳ Car_Mesh` | Modelo 3D (FBX) | local `(0, 0, 0)` | Carrocería visible. Ruedas apoyadas en local Y = 0 y nieve cubriéndolas. |
| 6 | `↳ Siren_Pivot` | Empty (Transform) + **`SirenRotation.cs`** | local `(0, 1.60, -0.15)` | Pivote en el techo; aquí gira la sirena. |
| 7 | `↳ Red_Light` | Point Light | local `(0.42, 0, 0)` | Roja `(1.00, 0.12, 0.10)`, intensidad 22, rango 26. |
| 8 | `↳ Blue_Light` | Point Light | local `(-0.42, 0, 0)` rot `(0, 180, 0)` | Azul `(0.12, 0.32, 1.00)`, intensidad 22, rango 26, orientada al lado opuesto. |
| 9 | `↳ Exhaust_Smoke` | Particle System | local `(0.55, 0.72, -2.07)` | Humo gris del escape, apenas saliendo de la capa de nieve. |
| 10 | `Global_Blizzard` | Particle System | `(0, 12.5, 0)` rot `(90, 0, 0)` | Nieve cayendo sobre toda la escena. |

```
Main Camera            (raíz)
Moon_Light             (raíz)
Snow_Terrain           (raíz)
Global_Blizzard        (raíz)
Police_Car             (raíz, Y = -0.5)
├── Car_Mesh
├── Siren_Pivot        ← SirenRotation.cs
│   ├── Red_Light
│   └── Blue_Light
└── Exhaust_Smoke
```

> El `Global Volume` que trae la plantilla URP se eliminó a propósito para que la
> jerarquía quede **exactamente** en los 10 objetos fundamentales pedidos.

---

## 3. El coche enterrado

El truco visual es puramente de transformaciones relativas:

* El FBX original (CC0, de Grab3D) viene en espacio **Z-up** (Blender): largo sobre
  `+Y`, altura sobre `+Z` y frente hacia `-Y`. Al importarlo se endereza con una
  rotación de **-90° en X** y se comprueba por código que no quede boca abajo.
* `Car_Mesh` se escala uniformemente a **4.7 m** de largo y se apoya con las ruedas
  en `localY = 0` (`min.y = 0`, centrado en X/Z).
* `Police_Car` se coloca en **Y = -0.5**, así que el coche queda 0.5 m por debajo de
  la superficie. Como la nieve alrededor del vehículo se genera a **≈ 0.18 m** de
  altura, la capa de nieve tapa las ruedas y llega hasta media puerta, dejando
  0.89 m de carrocería visible (techo a 1.07 m; las ventanillas empiezan a 0.35 m).

Medidas reales verificadas en el Editor: `Car_Mesh` mide `1.79 × 1.57 × 4.70 m`;
altura de nieve muestreada alrededor del coche: `0.16 – 0.19 m`.

---

## 4. Script `SirenRotation.cs`

```csharp
public class SirenRotation : MonoBehaviour
{
    public float rotationSpeed = 180f;      // grados por segundo (Inspector)
    public Vector3 rotationAxis = Vector3.up;

    void Update()
    {
        transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
    }
}
```

* Está asignado **únicamente** a `Siren_Pivot`.
* `Time.deltaTime` hace que el giro sea independiente de los fotogramas por segundo.
* `rotationSpeed` es pública y se ajusta desde el Inspector (valor actual: 180).
* Como `Red_Light` y `Blue_Light` son hijos del pivote, las dos luces describen un
  círculo sobre el techo y barren la nieve con su color.

---

## 5. Sistemas de partículas

### `Global_Blizzard` (independiente, sobre toda la escena)

| Parámetro | Valor |
|-----------|-------|
| Shape | **Box** 46 × 0.5 × 46 m, rotada 90° en X para emitir desde el cielo |
| Posición | Y = 12.5 |
| Gravity Modifier | 0.30 |
| Start Speed | 3.5 – 6.5 |
| Start Size | 0.035 – 0.135 |
| Start Lifetime | 2.4 – 4.2 s |
| Rate over Time | 800 – 1100 |
| Max Particles | 6000 |
| Color | azul muy claro con alfa variable |
| Velocity over Lifetime | viento lateral 2.2 – 3.6 en X |
| Rotation over Lifetime | ±2.2 rad/s |
| Simulation Space | World, **Prewarm** activo |
| Renderer | Billboard, `Blizzard_Snow_Material` (URP/Particles/Unlit), sin sombras |

### `Exhaust_Smoke` (hijo de `Police_Car`, parte trasera)

| Parámetro | Valor |
|-----------|-------|
| Shape | **Cone** de 9° de apertura, radio 0.05 (cono muy estrecho) |
| Orientación | Emite hacia arriba y hacia atrás (rotación local -105° en X) |
| Gravity Modifier | -0.015 (el humo caliente sube) |
| Start Speed / Size | 0.45 – 0.85 / 0.18 – 0.34 |
| Start Lifetime | 2.0 – 3.4 s |
| Rate over Time | 26 – 34 |
| **Color over Lifetime** | gris `(0.72, 0.73, 0.78)` → transparente (alfa 0 → 0.55 → 0) |
| **Size over Lifetime** | creciente: 0.55 → 1.35 → 3.10 |
| Noise | strength 0.22 para que el humo se deshilache |
| Renderer | Billboard, `Exhaust_Smoke_Material` (URP/Particles/Unlit) |

---

## 6. Historial de commits

| Commit | Mensaje | Contenido |
|--------|---------|-----------|
| 1 | `feat: configuracion inicial del proyecto y escena vacia` | Proyecto URP, `.gitignore` de Unity, escena base renombrada a `WinterRescue.unity`. |
| 2 | `feat: agregar terreno nevado y coche enterrado` | Terreno generado, `Police_Car` en Y = -0.5, modelo CC0, cámara y ambiente. |
| 3 | `feat: jerarquia de luces y sirenas de emergencia` | `Moon_Light`, `Siren_Pivot`, `Red_Light`, `Blue_Light` (sin código). |
| 4 | `feat: implementar script SirenRotation.cs` | Script de rotación y su asignación al pivote. |
| 5 | `feat: integrar particulas de nieve y humo de escape` | `Global_Blizzard` y `Exhaust_Smoke`. |
| 6 | `fix: encuadre de camara y horizonte de la tormenta` | Cámara más cerrada (FOV 48), terreno de 110 m y niebla a 11–52 m para ocultar el borde. |

Cada commit representa una única característica funcional y deja la escena en un
estado válido.

---

## 7. Reproducibilidad (herramienta de construcción)

`Assets/Editor/WinterSceneBuilder.cs` reconstruye la escena por etapas (una por
commit) usando la API del Editor, con métodos públicos que se pueden ejecutar en
modo batch:

```bash
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildTerrainAndCar -quit
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildLightsAndSirens -quit
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.AttachSirenScript -quit
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildParticles -quit
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.BuildAllStages -quit
Unity -batchmode -projectPath . -executeMethod WinterSceneBuilder.VerifyScene -quit
```

`BuildAllStages` además guarda una copia de la escena tras cada etapa
(`_snap2 … _snap5`) y `VerifyScene` imprime un informe de comprobación (jerarquía,
alturas de nieve, módulos de partículas y cámara).

`Assets/Editor/SceneBuildCommandRunner.cs` permite lanzar esos mismos métodos
desde fuera del Editor dejando un archivo de texto en `.scratch/cmd/` mientras el
Editor está abierto; el resultado queda en `.scratch/results/`.

---

## 8. Créditos y licencias

* **Modelo del coche:** [Police Car — Grab3D](https://grab3d.com/models/vehicles/police-car/),
  licencia **CC0 1.0** (dominio público). Copia de la licencia en
  `Assets/Models/PoliceCar/LICENSE-Grab3D-CC0.txt`.
* **Texturas del coche:** incluidas en el mismo paquete CC0
  (`Assets/Models/PoliceCar/police-car.fbm/`).
* **Terreno, materiales y partículas:** generados para este taller.
