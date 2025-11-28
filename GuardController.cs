using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public enum GuardState
{
    Vigilando,      // Parado vigilando
    Patrulla,       // Patrullando entre waypoints
    Persiguiendo,   // Persiguiendo al player
    Buscando,       // Buscando al player después de perderlo de vista
    Huyendo,        // Huyendo del player
    RecuperandoArma,// Yendo a recuperar su arma
    Derribado       // Derribado por el player
}

public class GuardController : MonoBehaviour
{
    [Header("Estado Inicial")]
    [SerializeField] private bool startsPatrolling = false;
    
    [Header("Patrulla")]
    [SerializeField] private List<Transform> waypoints = new List<Transform>();
    [SerializeField] private float waypointWaitTime = 2f;
    [SerializeField] private float waypointReachDistance = 1f;
    
    [Header("Detección")]
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float detectionAngle = 90f;
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask obstacleLayer;
    
    [Header("Combate")]
    [SerializeField] private float meleeRange = 2f;
    [SerializeField] private float shootingRange = 15f;
    [SerializeField] private float shootingCooldown = 1f;
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private GameObject weaponPrefab;
    
    [Header("Salud")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float lowHealthThreshold = 30f; // Por debajo de esto está malherido
    [SerializeField] private float dropWeaponChance = 0.3f; // 30% de probabilidad de soltar arma al recibir daño
    
    [Header("Comunicación")]
    [SerializeField] private float alertRange = 20f; // Rango para avisar a otros guardias
    [SerializeField] private LayerMask guardLayer;
    
    [Header("Velocidades")]
    [SerializeField] private float patrolSpeed = 3f;
    [SerializeField] private float chaseSpeed = 6f;
    [SerializeField] private float fleeSpeed = 7f;
    
    [Header("Búsqueda")]
    [SerializeField] private float searchRotationSpeed = 90f; // Grados por segundo al girar buscando
    [SerializeField] private float searchRotationDuration = 3f; // Tiempo girando antes de moverse
    [SerializeField] private float randomSearchRange = 10f; // Rango para búsqueda aleatoria
    [SerializeField] private float randomSearchDuration = 10f; // Tiempo máximo buscando aleatoriamente
    [SerializeField] private float lastKnownPositionReachDistance = 2f; // Distancia para considerar que llegó
    
    // Componentes
    private NavMeshAgent navAgent;
    private GuardState currentState;
    private Transform player;
    private GameObject currentWeapon;
    private bool hasWeapon = true;
    private bool isArmed => hasWeapon && currentWeapon != null;

    public GameObject impactoBalaPrefab;
    
    // Estado interno
    private float currentHealth;
    private int currentWaypointIndex = 0;
    private float waypointWaitTimer = 0f;
    private float shootingTimer = 0f;
    private Vector3 lastKnownPlayerPosition;
    private bool playerDetected = false;
    
    // Variables de búsqueda
    private bool hasReachedLastKnownPosition = false;
    private float searchRotationTimer = 0f;
    private float randomSearchTimer = 0f;
    private Vector3 searchStartPosition; // Posición desde donde empezó a buscar
    private Vector3 randomSearchDestination;
    private bool isRotating = false;
    private float initialRotationY;
    private float rotationProgress = 0f;
    private Vector3 guardPostPosition; // Posición de guardia o waypoint al que volver
    
    // Referencias
    private List<GuardController> nearbyGuards = new List<GuardController>();
    
    private void Awake()
    {
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent == null)
        {
            navAgent = gameObject.AddComponent<NavMeshAgent>();
        }
        
        // Buscar al player
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
        }
        
        currentHealth = maxHealth;
    }
    
    private void Start()
    {
        // Inicializar arma si existe
        if (weaponPrefab != null && weaponHolder != null)
        {
            SpawnWeapon();
        }
        else
        {
            hasWeapon = false;
        }
        
        // Establecer estado inicial
        if (startsPatrolling && waypoints.Count > 0)
        {
            SetState(GuardState.Patrulla);
            SetDestinationToWaypoint(0);
            guardPostPosition = waypoints[0].position;
        }
        else
        {
            SetState(GuardState.Vigilando);
            guardPostPosition = transform.position;
        }
        
        // Configurar NavMeshAgent
        navAgent.speed = patrolSpeed;
    }
    
    private void Update()
    {
        // Actualizar timers
        shootingTimer -= Time.deltaTime;
        
        // Detectar al player
        CheckForPlayer();
        
        // Ejecutar lógica según el estado actual
        switch (currentState)
        {
            case GuardState.Vigilando:
                UpdateVigilando();
                break;
            case GuardState.Patrulla:
                UpdatePatrulla();
                break;
            case GuardState.Persiguiendo:
                UpdatePersiguiendo();
                break;
            case GuardState.Buscando:
                UpdateBuscando();
                break;
            case GuardState.Huyendo:
                UpdateHuyendo();
                break;
            case GuardState.RecuperandoArma:
                UpdateRecuperandoArma();
                break;
            case GuardState.Derribado:
                UpdateDerribado();
                break;
        }
    }
    
    private void CheckForPlayer()
    {
        if (player == null) return;
        
        Vector3 directionToPlayer = player.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;
        
        // Si está dentro del rango de detección
        if (distanceToPlayer <= detectionRange)
        {
            // Verificar ángulo de visión
            float angleToPlayer = Vector3.Angle(transform.forward, directionToPlayer);
            if (angleToPlayer <= detectionAngle / 2f)
            {
                // Verificar si hay obstáculos
                RaycastHit hit;
                if (Physics.Raycast(transform.position + Vector3.up, directionToPlayer.normalized, out hit, distanceToPlayer, obstacleLayer))
                {
                    // Hay un obstáculo, no detecta
                    playerDetected = false;
                    return;
                }
                
                // Player detectado
                if (!playerDetected)
                {
                    OnPlayerDetected();
                }
                playerDetected = true;
                lastKnownPlayerPosition = player.position;
            }
            else
            {
                playerDetected = false;
            }
        }
        else
        {
            playerDetected = false;
        }
    }
    
    private void OnPlayerDetected()
    {
        // Decidir qué hacer según el estado del guardia
        if (currentState == GuardState.Derribado) return;
        
        // Si está malherido o desarmado, huir
        if (currentHealth < lowHealthThreshold || !isArmed)
        {
            SetState(GuardState.Huyendo);
        }
        // Si está sano y armado, perseguir
        else if (isArmed && currentHealth >= lowHealthThreshold)
        {
            SetState(GuardState.Persiguiendo);
            AlertNearbyGuards();
        }
    }
    
    private void UpdateVigilando()
    {
        // Solo rotar y vigilar
        if (playerDetected && player != null)
        {
            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            directionToPlayer.y = 0f;
            if (directionToPlayer.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 5f * Time.deltaTime);
            }
        }
    }
    
    private void UpdatePatrulla()
    {
        // Si detecta al player, cambiar de estado
        if (playerDetected)
        {
            return; // OnPlayerDetected ya manejará el cambio de estado
        }
        
        // Verificar si llegó al waypoint
        if (!navAgent.pathPending && navAgent.remainingDistance < waypointReachDistance)
        {
            waypointWaitTimer += Time.deltaTime;
            
            if (waypointWaitTimer >= waypointWaitTime)
            {
                // Ir al siguiente waypoint
                currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
                SetDestinationToWaypoint(currentWaypointIndex);
                waypointWaitTimer = 0f;
            }
        }
    }
    
    private void UpdatePersiguiendo()
    {
        if (player == null)
        {
            // Si no hay player, empezar a buscar
            StartSearching();
            return;
        }
        
        // Si detecta al player de nuevo, seguir persiguiendo
        if (playerDetected)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);
            
            // Si está muy cerca, ataque a melee
            if (distanceToPlayer <= meleeRange)
            {
                navAgent.isStopped = true;
                PerformMeleeAttack();
            }
            // Si está en rango de disparo, disparar
            else if (distanceToPlayer <= shootingRange && isArmed)
            {
                navAgent.isStopped = true;
                // Mirar al player
                Vector3 directionToPlayer = (player.position - transform.position).normalized;
                directionToPlayer.y = 0f;
                if (directionToPlayer.magnitude > 0.1f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
                }
                
                // Disparar
                if (shootingTimer <= 0f)
                {
                    ShootAtPlayer();
                    shootingTimer = shootingCooldown;
                }
            }
            // Acercarse para disparar si no está en rango
            else
            {
                navAgent.isStopped = false;
                navAgent.SetDestination(player.position);
            }
        }
        else
        {
            // Perdió de vista al player, empezar a buscar
            StartSearching();
        }
    }
    
    private void StartSearching()
    {
        // Guardar posición de guardia/waypoint para volver después
        if (currentState == GuardState.Persiguiendo)
        {
            if (startsPatrolling && waypoints.Count > 0)
            {
                guardPostPosition = waypoints[currentWaypointIndex].position;
            }
            else
            {
                guardPostPosition = transform.position; // Guardar posición actual como punto de guardia
            }
        }
        
        SetState(GuardState.Buscando);
        hasReachedLastKnownPosition = false;
        searchRotationTimer = 0f;
        randomSearchTimer = 0f;
        isRotating = false;
        searchStartPosition = transform.position;
        
        // Ir al último lugar conocido
        navAgent.SetDestination(lastKnownPlayerPosition);
    }
    
    private void UpdateBuscando()
    {
        // Si detecta al player de nuevo, perseguir
        if (playerDetected && player != null)
        {
            SetState(GuardState.Persiguiendo);
            return;
        }
        
        // Si aún no ha llegado al último lugar conocido
        if (!hasReachedLastKnownPosition)
        {
            float distanceToLastKnown = Vector3.Distance(transform.position, lastKnownPlayerPosition);
            
            if (distanceToLastKnown <= lastKnownPositionReachDistance)
            {
                hasReachedLastKnownPosition = true;
                navAgent.isStopped = true;
                isRotating = true;
                initialRotationY = transform.eulerAngles.y;
                rotationProgress = 0f;
                searchRotationTimer = 0f;
            }
        }
        // Si ya llegó, empezar a girar buscando
        else if (isRotating)
        {
            searchRotationTimer += Time.deltaTime;
            
            // Girar 360 grados durante searchRotationDuration segundos
            rotationProgress = searchRotationTimer / searchRotationDuration;
            
            if (rotationProgress >= 1f)
            {
                // Terminó de girar, empezar búsqueda aleatoria
                isRotating = false;
                randomSearchTimer = 0f;
                SetRandomSearchDestination();
            }
            else
            {
                // Rotar suavemente
                float currentRotationY = initialRotationY + (rotationProgress * 360f);
                transform.rotation = Quaternion.Euler(0, currentRotationY, 0);
            }
        }
        // Búsqueda aleatoria
        else
        {
            randomSearchTimer += Time.deltaTime;
            
            // Verificar si llegó al destino aleatorio o si pasó el tiempo
            if (randomSearchTimer >= randomSearchDuration)
            {
                // Tiempo agotado, volver a punto de guardia
                ReturnToGuardPost();
                return;
            }
            
            // Verificar si llegó al destino aleatorio
            if (!navAgent.pathPending && navAgent.remainingDistance < waypointReachDistance)
            {
                // Ir a otro punto aleatorio
                SetRandomSearchDestination();
            }
        }
    }
    
    private void SetRandomSearchDestination()
    {
        // Generar un punto aleatorio dentro del rango de búsqueda
        Vector2 randomCircle = Random.insideUnitCircle * randomSearchRange;
        Vector3 randomPosition = searchStartPosition + new Vector3(randomCircle.x, 0, randomCircle.y);
        
        // Buscar un punto válido en el NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPosition, out hit, randomSearchRange, NavMesh.AllAreas))
        {
            randomSearchDestination = hit.position;
            navAgent.SetDestination(randomSearchDestination);
            navAgent.isStopped = false;
        }
        else
        {
            // Si no encuentra un punto válido, intentar de nuevo
            SetRandomSearchDestination();
        }
    }
    
    private void ReturnToGuardPost()
    {
        // Volver al punto de guardia o waypoint correspondiente
        if (startsPatrolling && waypoints.Count > 0)
        {
            SetState(GuardState.Patrulla);
            SetDestinationToWaypoint(currentWaypointIndex);
        }
        else
        {
            SetState(GuardState.Vigilando);
            navAgent.SetDestination(guardPostPosition);
        }
    }
    
    private void UpdateHuyendo()
    {
        if (player == null) return;
        
        // Calcular dirección opuesta al player
        Vector3 fleeDirection = (transform.position - player.position).normalized;
        Vector3 fleePosition = transform.position + fleeDirection * 10f;
        
        // Buscar un punto válido en el NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePosition, out hit, 10f, NavMesh.AllAreas))
        {
            navAgent.SetDestination(hit.position);
        }
        
        // Si está muy lejos del player, volver a patrulla/vigilancia
        if (Vector3.Distance(transform.position, player.position) > detectionRange * 2f)
        {
            if (startsPatrolling && waypoints.Count > 0)
            {
                SetState(GuardState.Patrulla);
            }
            else
            {
                SetState(GuardState.Vigilando);
            }
        }
    }
    
    private void UpdateRecuperandoArma()
    {
        // Si no hay arma que recuperar, volver a estado normal
        if (currentWeapon == null)
        {
            if (startsPatrolling && waypoints.Count > 0)
            {
                SetState(GuardState.Patrulla);
            }
            else
            {
                SetState(GuardState.Vigilando);
            }
            return;
        }
        
        // Ir hacia el arma
        float distanceToWeapon = Vector3.Distance(transform.position, currentWeapon.transform.position);
        
        if (distanceToWeapon < 1f)
        {
            // Recuperar el arma
            PickupWeapon();
        }
        else
        {
            navAgent.SetDestination(currentWeapon.transform.position);
        }
    }
    
    private void UpdateDerribado()
    {
        // El guardia está derribado, no puede moverse
        navAgent.isStopped = true;
        // Aquí se puede añadir lógica de interrogación más adelante
    }
    
    private void SetState(GuardState newState)
    {
        if (currentState == newState) return;
        
        currentState = newState;
        
        // Configurar NavMeshAgent según el estado
        switch (newState)
        {
            case GuardState.Vigilando:
                navAgent.isStopped = true;
                navAgent.speed = patrolSpeed;
                break;
            case GuardState.Patrulla:
                navAgent.isStopped = false;
                navAgent.speed = patrolSpeed;
                break;
            case GuardState.Persiguiendo:
                navAgent.isStopped = false;
                navAgent.speed = chaseSpeed;
                break;
            case GuardState.Buscando:
                navAgent.isStopped = false;
                navAgent.speed = patrolSpeed;
                break;
            case GuardState.Huyendo:
                navAgent.isStopped = false;
                navAgent.speed = fleeSpeed;
                break;
            case GuardState.RecuperandoArma:
                navAgent.isStopped = false;
                navAgent.speed = patrolSpeed;
                break;
            case GuardState.Derribado:
                navAgent.isStopped = true;
                break;
        }
    }
    
    private void SetDestinationToWaypoint(int waypointIndex)
    {
        if (waypoints.Count == 0 || waypointIndex >= waypoints.Count) return;
        
        navAgent.SetDestination(waypoints[waypointIndex].position);
    }
    
    private void ShootAtPlayer()
    {
        if (player == null || currentWeapon == null) return;
        
        // Calcular dirección hacia el player
        Vector3 shootOrigin = weaponHolder != null ? weaponHolder.position : transform.position + Vector3.up;
        Vector3 directionToPlayer = (player.position - shootOrigin).normalized;
        
        // Hacer raycast para detectar impacto
        RaycastHit hit;
        if (Physics.Raycast(shootOrigin, directionToPlayer, out hit, shootingRange))
        {
            // Verificar si impactó en el player
            if (hit.collider.CompareTag("Player"))
            {
                // Generar prefab de impacto en el punto de impacto
                if (impactoBalaPrefab != null)
                {
                    GameObject impacto = Instantiate(impactoBalaPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                    
                    // Obtener el script ImpactoBala y llamar a Impact con la layer del player
                    ImpactoBala impactoScript = impacto.GetComponent<ImpactoBala>();
                    if (impactoScript != null)
                    {
                        // Obtener la layer del objeto impactado
                        LayerMask playerLayerMask = 1 << hit.collider.gameObject.layer;
                        impactoScript.Impact(playerLayerMask);
                    }
                }
            }
        }
    }
    
    private void PerformMeleeAttack()
    {
        if (player == null) return;
        
        // Mirar al player
        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        directionToPlayer.y = 0f;
        if (directionToPlayer.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
        }
        
        // Aquí se puede añadir lógica de ataque a melee
        Debug.Log($"{gameObject.name} ataca a melee al player");
    }
    
    public void TakeDamage(float damage, bool isMeleeAttack = false)
    {
        currentHealth -= damage;
        
        // Si es ataque a melee, puede derribar al guardia
        if (isMeleeAttack && currentHealth > 0)
        {
            SetState(GuardState.Derribado);
            return;
        }
        
        // Si muere
        if (currentHealth <= 0)
        {
            Die();
            return;
        }
        
        // Posibilidad de soltar el arma
        if (isArmed && Random.value < dropWeaponChance)
        {
            DropWeapon();
        }
        
        // Si está malherido y desarmado, huir
        if (currentHealth < lowHealthThreshold && !isArmed)
        {
            if (currentState != GuardState.Derribado)
            {
                SetState(GuardState.Huyendo);
            }
        }
        // Si está poco herido y perdió el arma, intentar recuperarla
        else if (currentHealth >= lowHealthThreshold && !isArmed && currentWeapon != null)
        {
            if (currentState != GuardState.Derribado && currentState != GuardState.Persiguiendo)
            {
                SetState(GuardState.RecuperandoArma);
            }
        }
    }
    
    private void DropWeapon()
    {
        if (currentWeapon == null) return;
        
        // Desparentar el arma
        currentWeapon.transform.SetParent(null);
        
        // Añadir física al arma (opcional)
        Rigidbody weaponRb = currentWeapon.GetComponent<Rigidbody>();
        if (weaponRb == null)
        {
            weaponRb = currentWeapon.AddComponent<Rigidbody>();
        }
        weaponRb.isKinematic = false;
        
        // Lanzar el arma un poco
        weaponRb.AddForce((transform.forward + Vector3.up) * 3f, ForceMode.Impulse);
        
        hasWeapon = false;
        Debug.Log($"{gameObject.name} suelta su arma");
    }
    
    private void PickupWeapon()
    {
        if (currentWeapon == null) return;
        
        // Remover física
        Rigidbody weaponRb = currentWeapon.GetComponent<Rigidbody>();
        if (weaponRb != null)
        {
            Destroy(weaponRb);
        }
        
        // Reparentar al weapon holder
        if (weaponHolder != null)
        {
            currentWeapon.transform.SetParent(weaponHolder);
            currentWeapon.transform.localPosition = Vector3.zero;
            currentWeapon.transform.localRotation = Quaternion.identity;
        }
        
        hasWeapon = true;
        Debug.Log($"{gameObject.name} recupera su arma");
        
        // Volver a estado normal
        if (startsPatrolling && waypoints.Count > 0)
        {
            SetState(GuardState.Patrulla);
        }
        else
        {
            SetState(GuardState.Vigilando);
        }
    }
    
    private void SpawnWeapon()
    {
        if (weaponPrefab == null || weaponHolder == null) return;
        
        currentWeapon = Instantiate(weaponPrefab, weaponHolder);
        currentWeapon.transform.localPosition = Vector3.zero;
        currentWeapon.transform.localRotation = Quaternion.identity;
        hasWeapon = true;
    }
    
    private void AlertNearbyGuards()
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, alertRange, guardLayer);
        
        foreach (Collider col in nearbyColliders)
        {
            GuardController guard = col.GetComponent<GuardController>();
            if (guard != null && guard != this && player != null)
            {
                // Alertar al guardia cercano
                guard.OnAlerted(player.position);
            }
        }
    }
    
    public void OnAlerted(Vector3 alertPosition)
    {
        // Si el guardia está en estado normal y está armado y sano, ir a investigar
        if ((currentState == GuardState.Vigilando || currentState == GuardState.Patrulla || currentState == GuardState.Buscando) 
            && isArmed && currentHealth >= lowHealthThreshold)
        {
            lastKnownPlayerPosition = alertPosition;
            SetState(GuardState.Persiguiendo);
            navAgent.SetDestination(alertPosition);
        }
    }
    
    private void Die()
    {
        Debug.Log($"{gameObject.name} muere");
        // Aquí se puede añadir lógica de muerte
        navAgent.isStopped = true;
        // Deshabilitar el script o destruir el objeto
        // Destroy(gameObject);
    }
    
    // Métodos públicos para obtener información del estado
    public GuardState GetState() => currentState;
    public bool IsArmed() => isArmed;
    public float GetHealth() => currentHealth;
    public float GetHealthPercentage() => currentHealth / maxHealth;
    
    private void OnDrawGizmosSelected()
    {
        // Dibujar rango de detección
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        
        // Dibujar rango de alerta
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, alertRange);
        
        // Dibujar rango de melee
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, meleeRange);
        
        // Dibujar rango de disparo
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, shootingRange);
        
        // Dibujar waypoints de patrulla
        if (waypoints.Count > 0)
        {
            Gizmos.color = Color.green;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i] != null)
                {
                    Gizmos.DrawWireSphere(waypoints[i].position, 0.5f);
                    if (i < waypoints.Count - 1 && waypoints[i + 1] != null)
                    {
                        Gizmos.DrawLine(waypoints[i].position, waypoints[i + 1].position);
                    }
                    else if (waypoints.Count > 1 && waypoints[0] != null)
                    {
                        Gizmos.DrawLine(waypoints[i].position, waypoints[0].position);
                    }
                }
            }
        }
    }
}

