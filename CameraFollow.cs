using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Transform target; // El transform del jugador
    
    [Header("Seguimiento")]
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 10, 0); // Offset desde arriba
    
    [Header("Zoom")]
    [SerializeField] private float minZoom = 5f;
    [SerializeField] private float maxZoom = 20f;
    [SerializeField] private float zoomSpeed = 10f;
    [SerializeField] private float scrollSensitivity = 2f;
    [SerializeField] private bool useSmoothZoom = true;
    
    [Header("Suavizado")]
    [SerializeField] private bool useSmoothDamping = true;
    [SerializeField] private float smoothDampingTime = 0.3f;
    
    private float currentZoom;
    private float targetZoom;
    private Vector3 velocity; // Para SmoothDamp del seguimiento horizontal
    
    private void Start()
    {
        // Si no hay target asignado, buscar el jugador automáticamente
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
            else
            {
                Debug.LogWarning("CameraFollow: No se encontró un objeto con tag 'Player'. Asigna el target manualmente.");
            }
        }
        
        // Inicializar zoom con el offset Y actual
        currentZoom = offset.y;
        targetZoom = currentZoom;
        
        // Asegurar que la cámara mire hacia abajo
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
    
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }
        
        HandleZoom();
        FollowTarget();
    }
    
    private void HandleZoom()
    {
        // Obtener input de la rueda del ratón
        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        
        if (Mathf.Abs(scrollInput) > 0.01f)
        {
            // Actualizar el zoom objetivo según el scroll
            targetZoom -= scrollInput * scrollSensitivity;
            targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
        }
        
        // Aplicar zoom de forma suave hacia el objetivo
        if (useSmoothZoom)
        {
            // Usar MoveTowards para un zoom más controlado y que funcione bien con valores altos
            float zoomDelta = zoomSpeed * Time.deltaTime;
            currentZoom = Mathf.MoveTowards(currentZoom, targetZoom, zoomDelta);
        }
        else
        {
            // Zoom instantáneo
            currentZoom = targetZoom;
        }
    }
    
    private void FollowTarget()
    {
        // Calcular la posición objetivo horizontal (solo X y Z del target)
        Vector3 horizontalTarget = new Vector3(
            target.position.x + offset.x,
            transform.position.y, // Mantener Y actual (será actualizado por el zoom)
            target.position.z + offset.z
        );
        
        // Mover la cámara horizontalmente con suavizado (solo X y Z)
        Vector3 currentPos = transform.position;
        Vector3 smoothPosition;
        
        if (useSmoothDamping)
        {
            // Usar SmoothDamp solo para X y Z (seguimiento horizontal)
            Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);
            
            smoothPosition = Vector3.SmoothDamp(
                currentPos,
                horizontalTarget,
                ref horizontalVelocity,
                smoothDampingTime
            );
            
            // Actualizar velocity solo para X y Z
            velocity.x = horizontalVelocity.x;
            velocity.z = horizontalVelocity.z;
        }
        else
        {
            // Usar Lerp solo para X y Z
            smoothPosition = Vector3.Lerp(
                currentPos,
                horizontalTarget,
                followSpeed * Time.deltaTime
            );
        }
        
        // Aplicar el zoom directamente a la posición Y (completamente independiente del seguimiento suave)
        // El zoom ya se calculó en HandleZoom() y está en currentZoom
        smoothPosition.y = target.position.y + currentZoom;
        
        transform.position = smoothPosition;
        
        // Asegurar que la cámara siempre mire hacia abajo (vista top-down)
        // Mantener la rotación fija en 90 grados en X para vista desde arriba
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
    
    // Método público para cambiar el target en tiempo de ejecución
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // Método público para ajustar el zoom programáticamente
    public void SetZoom(float zoom)
    {
        targetZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
    }
    
    // Método público para obtener el zoom actual
    public float GetZoom()
    {
        return currentZoom;
    }
}

