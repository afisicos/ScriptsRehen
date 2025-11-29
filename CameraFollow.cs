using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private Transform target; // El transform del jugador
    [SerializeField] private Camera playerCamera; // La cámara del player
    
    [Header("Seguimiento")]
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 10, 0); // Offset desde arriba
    
    [Header("Suavizado")]
    [SerializeField] private bool useSmoothDamping = true;
    [SerializeField] private float smoothDampingTime = 0.3f;
    
    private Vector3 velocity; // Para SmoothDamp del seguimiento
    
    private void Start()
    {
        // Si no hay target asignado, buscar el jugador automáticamente
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
                playerCamera = player.GetComponent<Camera>();
                if (playerCamera == null)
                {
                    playerCamera = player.GetComponentInChildren<Camera>();
                }
            }
            else
            {
                Debug.LogWarning("CameraFollow: No se encontró un objeto con tag 'Player'. Asigna el target manualmente.");
            }
        }
        
        // Si no hay cámara asignada, buscarla
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        
        // Asegurar que la cámara mire hacia abajo
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
    
    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }
        
        FollowTarget();
    }
    
    private void FollowTarget()
    {
        // Obtener la posición del cursor en el mundo
        Vector3 mouseWorldPosition = GetMouseWorldPosition();
        
        // Calcular el punto medio entre el player y el cursor
        Vector3 midPoint = (target.position + mouseWorldPosition) / 2f;
        
        // Calcular la posición objetivo (punto medio + offset)
        Vector3 targetPosition = new Vector3(
            midPoint.x + offset.x,
            target.position.y + offset.y, // Mantener la altura relativa al player
            midPoint.z + offset.z
        );
        
        // Mover la cámara con suavizado
        Vector3 smoothPosition;
        
        if (useSmoothDamping)
        {
            smoothPosition = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref velocity,
                smoothDampingTime
            );
        }
        else
        {
            // Usar Lerp sin suavizado
            smoothPosition = Vector3.Lerp(
                transform.position,
                targetPosition,
                followSpeed * Time.deltaTime
            );
        }
        
        transform.position = smoothPosition;
        
        // Asegurar que la cámara siempre mire hacia abajo (vista top-down)
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
    
    private Vector3 GetMouseWorldPosition()
    {
        if (playerCamera == null)
        {
            // Si no hay cámara, devolver la posición del player
            return target.position;
        }
        
        // Obtener la posición del mouse en pantalla
        Vector3 mouseScreenPos = Input.mousePosition;
        
        // Convertir a posición mundial en el plano horizontal (Y del player)
        Ray ray = playerCamera.ScreenPointToRay(mouseScreenPos);
        Plane groundPlane = new Plane(Vector3.up, target.position);
        
        if (groundPlane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }
        
        // Fallback si el raycast no funciona
        return target.position;
    }
    
    // Método público para cambiar el target en tiempo de ejecución
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}

