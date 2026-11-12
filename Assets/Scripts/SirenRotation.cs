using UnityEngine;

/// <summary>
/// Hace girar el pivote de las sirenas de la patrulla.
///
/// Se asigna UNICAMENTE al objeto Siren_Pivot, que es el padre de Red_Light y
/// Blue_Light: al rotar el pivote, ambas luces describen un circulo sobre el
/// techo del coche y barren la nieve con su color.
///
/// La velocidad se expone en el Inspector y se multiplica por Time.deltaTime
/// para que el giro sea independiente de los fotogramas por segundo.
/// </summary>
public class SirenRotation : MonoBehaviour
{
    [Header("Velocidad de giro")]
    [Tooltip("Grados por segundo que gira el pivote de la sirena.")]
    public float rotationSpeed = 180f;

    [Header("Eje de giro")]
    [Tooltip("Eje local sobre el que gira el pivote. Y por defecto (giro horizontal).")]
    public Vector3 rotationAxis = Vector3.up;

    void Update()
    {
        // Time.deltaTime convierte "grados por segundo" en "grados de este frame":
        // asi el giro se ve igual a 30, 60 o 144 FPS.
        transform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime);
    }
}
