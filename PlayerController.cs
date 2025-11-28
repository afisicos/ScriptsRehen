using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 15f;
    
    [Header("Dash")]
    [SerializeField] private float dashDistance = 5f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1f;
    
    [Header("Rotación")]
    [SerializeField] private float rotationSpeed = 15f;
    
    [Header("Referencias")]
    [SerializeField] private Camera playerCamera;
    
    private CharacterController characterController;
    private Vector3 currentVelocity;
    private Vector3 moveDirection;
    private bool isSprinting;
    private bool isDashing;
    private float dashTimer;
    private float dashCooldownTimer;
    private Vector3 dashDirection;
    private float initialYPosition;
    
    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        
        // Si no hay CharacterController, lo añadimos automáticamente
        if (characterController == null)
        {
            characterController = gameObject.AddComponent<CharacterController>();
            characterController.height = 2f;
            characterController.radius = 0.5f;
            characterController.center = new Vector3(0, 1, 0);
        }
        
        // Si no hay cámara asignada, buscamos la cámara principal
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }
    
    private void Start()
    {
        // Guardar la posición Y inicial para mantenerla constante
        initialYPosition = transform.position.y;
    }
    
    private void Update()
    {
        HandleDash();
        HandleMovement();
        HandleRotation();
    }
    
    private void HandleMovement()
    {
        // Si estamos en dash, no procesamos el movimiento normal
        if (isDashing)
        {
            return;
        }
        
        // Input de movimiento
        float horizontal = Input.GetAxisRaw("Horizontal"); // A/D
        float vertical = Input.GetAxisRaw("Vertical");     // W/S
        
        // Detectar sprint
        isSprinting = Input.GetKey(KeyCode.LeftShift);
        
        // Calcular dirección de movimiento en el plano horizontal
        moveDirection = new Vector3(horizontal, 0f, vertical).normalized;
        
        // Velocidad objetivo según si está sprinting o no
        float targetSpeed = isSprinting ? sprintSpeed : walkSpeed;
        
        // Si hay input, acelerar hacia la velocidad objetivo
        if (moveDirection.magnitude > 0.1f)
        {
            currentVelocity = Vector3.Lerp(
                currentVelocity, 
                moveDirection * targetSpeed, 
                acceleration * Time.deltaTime
            );
        }
        // Si no hay input, desacelerar suavemente
        else
        {
            currentVelocity = Vector3.Lerp(
                currentVelocity, 
                Vector3.zero, 
                deceleration * Time.deltaTime
            );
        }
        
        // Forzar velocidad Y a 0 para mantener posición vertical constante
        currentVelocity.y = 0f;
        
        // Mover el CharacterController solo en X y Z
        Vector3 movement = new Vector3(currentVelocity.x, 0f, currentVelocity.z) * Time.deltaTime;
        characterController.Move(movement);
        
        // Restaurar posición Y original después del movimiento
        Vector3 pos = transform.position;
        pos.y = initialYPosition;
        transform.position = pos;
    }
    
    private void HandleRotation()
    {
        // Obtener posición del cursor en el mundo
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        
        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 targetPoint = ray.GetPoint(distance);
            Vector3 directionToTarget = targetPoint - transform.position;
            directionToTarget.y = 0f; // Mantener rotación solo en el plano horizontal
            
            if (directionToTarget.magnitude > 0.1f)
            {
                // Rotar suavemente hacia la dirección del cursor
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, 
                    targetRotation, 
                    rotationSpeed * Time.deltaTime
                );
            }
        }
    }
    
    private void HandleDash()
    {
        // Actualizar cooldown del dash
        if (dashCooldownTimer > 0f)
        {
            dashCooldownTimer -= Time.deltaTime;
        }
        
        // Si estamos en dash, ejecutar el movimiento del dash
        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            
            if (dashTimer <= 0f)
            {
                isDashing = false;
                dashCooldownTimer = dashCooldown;
            }
            else
            {
                // Mover en la dirección del dash (solo X y Z)
                float dashSpeed = dashDistance / dashDuration;
                Vector3 dashMovement = new Vector3(dashDirection.x, 0f, dashDirection.z) * dashSpeed * Time.deltaTime;
                characterController.Move(dashMovement);
                
                // Restaurar posición Y original después del movimiento del dash
                Vector3 pos = transform.position;
                pos.y = initialYPosition;
                transform.position = pos;
            }
            
            return;
        }
        
        // Detectar input de dash (barra espaciadora)
        if (Input.GetKeyDown(KeyCode.Space) && dashCooldownTimer <= 0f)
        {
            PerformDash();
        }
    }
    
    private void PerformDash()
    {
        isDashing = true;
        dashTimer = dashDuration;
        
        // La dirección del dash es hacia donde está mirando el jugador
        dashDirection = transform.forward;
        dashDirection.y = 0f;
        dashDirection.Normalize();
        
        // Resetear velocidad actual para que el dash sea limpio (mantener Y en 0)
        currentVelocity = Vector3.zero;
        currentVelocity.y = 0f;
    }
    
    // Método público para obtener la velocidad actual (útil para animaciones, efectos, etc.)
    public Vector3 GetVelocity()
    {
        return currentVelocity;
    }
    
    // Método público para saber si está sprinting
    public bool IsSprinting()
    {
        return isSprinting && moveDirection.magnitude > 0.1f;
    }
    
    // Método público para saber si está haciendo dash
    public bool IsDashing()
    {
        return isDashing;
    }
}

