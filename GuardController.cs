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
    Derribado,      // Derribado por el player
    Muerto          // Muerto
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
    public GuardState currentState;
    private Transform player;
    private ArmaController armaController;
    private PlayerHealth playerHealth;
    private bool hasWeapon = true;
    private bool isArmed => hasWeapon && armaController != null && armaController.HasWeapon();
    
    // Estado interno
    private float currentHealth;
    private int currentWaypointIndex = 0;
    private float waypointWaitTimer = 0f;
    private Vector3 lastKnownPlayerPosition;
    private bool playerDetected = false;
    private float weaponDropCooldown = 0f; // Cooldown después de soltar arma
    private const float WEAPON_DROP_COOLDOWN_TIME = 2f; // 2 segundos antes de poder recoger
    private Vector3 lastPlayerPosition; // Para calcular velocidad del player
    private float playerSpeedLastFrame = 0f; // Velocidad del player en el frame anterior
    private float stunDuration = 0f; // Duración del aturdimiento por disparo
    private const float STUN_TIME_MIN = 1f; // Mínimo tiempo de aturdimiento
    private const float STUN_TIME_MAX = 2f; // Máximo tiempo de aturdimiento
    private const float KNOCKBACK_FORCE = 5f; // Fuerza del retroceso
    
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
            playerHealth = playerObj.GetComponent<PlayerHealth>();
        }
        
        currentHealth = maxHealth;
    }
    
    private void Start()
    {
        // Obtener o crear ArmaController
        armaController = GetComponent<ArmaController>();
        if (armaController == null)
        {
            armaController = gameObject.AddComponent<ArmaController>();
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
        
        // Inicializar posición del player
        if (player != null)
        {
            lastPlayerPosition = player.position;
        }
    }
    
    private void Update()
    {
        // Reducir cooldown de arma
        if (weaponDropCooldown > 0f)
        {
            weaponDropCooldown -= Time.deltaTime;
        }
        
        // Reducir duración del aturdimiento
        if (stunDuration > 0f)
        {
            stunDuration -= Time.deltaTime;
        }
        
        // Verificar si el jugador ha muerto
        if (playerHealth != null && playerHealth.IsDead())
        {
            OnPlayerDead();
            return;
        }
        
        // Si el guardia está muerto, no hacer nada
        if (currentState == GuardState.Muerto)
        {
            return;
        }
        
        // Si está aturdido, no hacer nada
        if (stunDuration > 0f)
        {
            return;
        }
        
        // Detectar al player
        CheckForPlayer();
        
        // Actualizar target del ArmaController si está persiguiendo
        if (currentState == GuardState.Persiguiendo && player != null && armaController != null)
        {
            armaController.SetTarget(player);
        }
        
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
    
    private void OnPlayerDead()
    {
        // Si el guardia está persiguiendo o buscando, volver a su posición
        if (currentState == GuardState.Persiguiendo || currentState == GuardState.Buscando)
        {
            ReturnToGuardPost();
            return;
        }
        
        // Detener cualquier acciones relacionadas con el jugador
        playerDetected = false;
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
        if (currentState == GuardState.Derribado || currentState == GuardState.RecuperandoArma || currentState == GuardState.Muerto) 
            return;
        
        // Si está desarmado, no persegiur (ya debería estar buscando arma)
        if (!isArmed)
        {
            return;
        }
        
        // Si está malherido pero armado, huir
        if (currentHealth < lowHealthThreshold)
        {
            SetState(GuardState.Huyendo);
        }
        // Si está sano y armado, persecución
        else if (currentHealth >= lowHealthThreshold)
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
    
    private float CalculateAccuracy()
    {
        if (player == null) return 0f;
        
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        
        // Calcular la velocidad del player
        float playerSpeed = Vector3.Distance(player.position, lastPlayerPosition) / Time.deltaTime;
        lastPlayerPosition = player.position;
        playerSpeedLastFrame = playerSpeed;
        
        // Precisión base según la distancia
        // A corta distancia (1m): 90% de precisión
        // A distancia media (shootingRange): 50% de precisión
        // A larga distancia (2x shootingRange): 20% de precisión
        float accuracyByDistance = Mathf.Clamp01(1f - (distanceToPlayer / (shootingRange * 2f)));
        accuracyByDistance = Mathf.Lerp(0.2f, 0.9f, accuracyByDistance);
        
        // Penalización por movimiento del player
        // Si no se mueve: sin penalización
        // Si se mueve lentamente: -10% precisión
        // Si se mueve rápido: -40% precisión
        float movementPenalty = 0f;
        if (playerSpeed > 0.5f) // Si se mueve más de 0.5 m/s
        {
            movementPenalty = Mathf.Min(playerSpeed * 0.15f, 0.4f); // Hasta -40%
        }
        
        // Aplicar precisión del arma
        float weaponAccuracy = 0.5f; // Por defecto 50%
        if (armaController != null && armaController.HasWeapon())
        {
            weaponAccuracy = armaController.GetAccuracy(); // Obtener accuracy del arma
        }
        
        // Precisión final: combinar distancia, movimiento y accuracy del arma
        float baseAccuracy = Mathf.Clamp01(accuracyByDistance - movementPenalty);
        float finalAccuracy = Mathf.Clamp01(baseAccuracy * weaponAccuracy);
        
        return finalAccuracy;
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
                
                // Calcular precisión y decidir si acierta o falla
                float accuracy = CalculateAccuracy();
                if (Random.value < accuracy)
                {
                    // Acierto: disparar normalmente
                    if (armaController != null && isArmed)
                    {
                        armaController.Shoot();
                    }
                }
                else
                {
                    // Fallo: disparar pero sin acertar
                    if (armaController != null && isArmed)
                    {
                        // Disparar con desviación importante
                        armaController.ShootWithMiss();
                    }
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
        // Si detecta al player de nuevo, persiguir
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
                // Tiempoagotado, volver a punto de guardia
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
            navAgent.isStopped = false;
            navAgent.SetDestination(guardPostPosition);
            Debug.Log($"{gameObject.name} vuelve a su puesto de guardia en {guardPostPosition}");
        }
    }
    
    private void UpdateHuyendo()
    {
        if (player == null) return;
        
        // Si recupera salud o consigue arma, dejar de huir
        if (isArmed && currentHealth >= lowHealthThreshold)
        {
            // Volver a perseguir si detecta al player
            if (playerDetected)
            {
                SetState(GuardState.Persiguiendo);
                return;
            }
            // Volver a estado normal
            else if (startsPatrolling && waypoints.Count > 0)
            {
                SetState(GuardState.Patrulla);
                return;
            }
            else
            {
                SetState(GuardState.Vigilando);
                return;
            }
        }
        
        // Si está desarmado, ir a buscar arma en lugar de huir
        if (!isArmed)
        {
            Collider[] nearbyWeapons = Physics.OverlapSphere(transform.position, 20f);
            bool hasWeaponNearby = false;
            
            foreach (Collider col in nearbyWeapons)
            {
                if (col.CompareTag("Arma") || col.GetComponent<WeaponData>() != null)
                {
                    hasWeaponNearby = true;
                    break;
                }
            }
            
            if (hasWeaponNearby)
            {
                SetState(GuardState.RecuperandoArma);
                return;
            }
        }
        
        // Calcular dirección opuesta al player
        Vector3 fleeDirection = (transform.position - player.position).normalized;
        Vector3 fleePosition = transform.position + fleeDirection * 15f;
        
        // Buscar un punto válido en el NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePosition, out hit, 15f, NavMesh.AllAreas))
        {
            navAgent.isStopped = false;
            navAgent.SetDestination(hit.position);
        }
        
        // Si está muy lejos del player, volver a estado normal
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
        // Si el player está detectado y estamos armados, no buscar arma
        if (playerDetected && isArmed && currentHealth >= lowHealthThreshold)
        {
            SetState(GuardState.Persiguiendo);
            return;
        }
        
        // Si estamos en cooldown de soltar arma, no buscarla todavía
        if (weaponDropCooldown > 0f)
        {
            // Solo movernos lejos del player mientras esperamos
            if (player != null && playerDetected && currentHealth < lowHealthThreshold)
            {
                Vector3 fleeDirection = (transform.position - player.position).normalized;
                Vector3 fleePosition = transform.position + fleeDirection * 10f;
                
                NavMeshHit hit;
                if (NavMesh.SamplePosition(fleePosition, out hit, 10f, NavMesh.AllAreas))
                {
                    navAgent.isStopped = false;
                    navAgent.SetDestination(hit.position);
                }
            }
            return;
        }
        
        // Buscar armas cercanas (primero rango corto)
        Collider[] nearbyObjects = Physics.OverlapSphere(transform.position, 2f);
        GameObject nearbyWeapon = null;
        
        foreach (Collider col in nearbyObjects)
        {
            if (col.CompareTag("Arma") || col.GetComponent<WeaponData>() != null)
            {
                nearbyWeapon = col.gameObject;
                break;
            }
        }
        
        // Si no hay arma muy cerca, buscar en un rango mayor
        if (nearbyWeapon == null)
        {
            Collider[] widerSearch = Physics.OverlapSphere(transform.position, 20f);
            float closestDistance = float.MaxValue;
            
            foreach (Collider col in widerSearch)
            {
                if (col.CompareTag("Arma") || col.GetComponent<WeaponData>() != null)
                {
                    float distance = Vector3.Distance(transform.position, col.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        nearbyWeapon = col.gameObject;
                    }
                }
            }
        }
        
        // Si no hay arma que recuperar, cambiar de estado
        if (nearbyWeapon == null)
        {
            // Si está malherido, huir
            if (currentHealth < lowHealthThreshold)
            {
                SetState(GuardState.Huyendo);
            }
            // Si no está malherido, volver a estado normal
            else if (startsPatrolling && waypoints.Count > 0)
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
        float distanceToWeapon = Vector3.Distance(transform.position, nearbyWeapon.transform.position);
        
        // Coger el arma cuando está más cerca (hasta 1 metro)
        if (distanceToWeapon < 1f)
        {
            // Recuperar el arma
            PickupWeapon();
            return;
        }
        
        // Moverse hacia el arma con velocidad de patrulla
        navAgent.isStopped = false;
        navAgent.SetDestination(nearbyWeapon.transform.position);
    }
    
    private void UpdateDerribado()
    {
        // El guardia está derribado, no puede moverse
        navAgent.isStopped = true;
        // Aquí se puede añadir lógica de interrogación más adelante
    }
    
    private void UpdateMuerto()
    {
        // El guardia está muerto, no puede moverse ni hacer nada
        navAgent.isStopped = true;
        
        // Desactivar componentes adicionales si es necesario
        // Por ejemplo, desactivar el collider para que no sea golpeable de nuevo
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }
        
        // Aquí se puede añadir lógica adicional para el estado de muerto
    }
    
    private void SetState(GuardState newState)
    {
        if (currentState == newState) return;
        
        currentState = newState;
        
        // Configurar NavMeshAgent según el estado
        switch (newState)
        {
            case GuardState.Vigilando:
                navAgent.speed = patrolSpeed;
                navAgent.isStopped = false;
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
            case GuardState.Muerto:
                navAgent.isStopped = true;
                navAgent.enabled = false;
                break;
        }
    }
    
    private void SetDestinationToWaypoint(int waypointIndex)
    {
        if (waypoints.Count == 0 || waypointIndex >= waypoints.Count) return;
        
        navAgent.SetDestination(waypoints[waypointIndex].position);
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
        
        // Si es ataque a distancia (disparo), aplicar knockback y aturdimiento
        if (!isMeleeAttack && currentHealth > 0 && player != null)
        {
            // Aplicar knockback
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true; // Inicialmente cinemático para no interferi con NavMesh
            }
            
            // Calcular dirección del knockback (desde el jugador hacia el guardia)
            Vector3 knockbackDirection = (transform.position - player.position).normalized;
            knockbackDirection.y = 0f; // No empujar hacia arriba/abajo
            
            // Aplicar impulso
            if (!rb.isKinematic)
            {
                rb.AddForce(knockbackDirection * KNOCKBACK_FORCE, ForceMode.Impulse);
            }
            else
            {
                // Si es cinemático, mover manualmente
                transform.Translate(knockbackDirection * KNOCKBACK_FORCE * Time.deltaTime, Space.World);
            }
            
            // Aplicar aturdimiento (1-2 segundos sin poder actuar)
            stunDuration = Random.Range(STUN_TIME_MIN, STUN_TIME_MAX);
            
            // Detener el NavMeshAgent mientras está aturdido
            if (navAgent != null)
            {
                navAgent.isStopped = true;
            }
            
            Debug.Log($"{gameObject.name} recibe disparo y está aturdido por {stunDuration:F2} segundos");
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
        
        // Lógica de respuesta a daño
        if (!isArmed)
        {
            // Si está desarmado, SIEMPRE buscar arma primero
            if (currentState != GuardState.Derribado && currentState != GuardState.RecuperandoArma && currentState != GuardState.Muerto)
            {
                // Verificar si hay armas cerca
                Collider[] nearbyWeapons = Physics.OverlapSphere(transform.position, 25f);
                bool hasWeaponNearby = false;
                
                foreach (Collider col in nearbyWeapons)
                {
                    if (col.CompareTag("Arma") || col.GetComponent<WeaponData>() != null)
                    {
                        hasWeaponNearby = true;
                        break;
                    }
                }
                
                // Ir a buscar arma si la hay
                if (hasWeaponNearby)
                {
                    SetState(GuardState.RecuperandoArma);
                }
                else if (playerDetected)
                {
                    // Si no hay arma y detecta al player, huir
                    SetState(GuardState.Huyendo);
                }
            }
        }
        else if (currentHealth < lowHealthThreshold)
        {
            // Si está malherido y armado, huir
            if (currentState != GuardState.Derribado && currentState != GuardState.Huyendo)
            {
                SetState(GuardState.Huyendo);
            }
        }
    }
    
    private void DropWeapon()
    {

        armaController.armaActual.transform.SetParent(null);

        Rigidbody weaponRb = armaController.armaActual.GetComponent<Rigidbody>();
        if (weaponRb == null)
        {
            weaponRb = armaController.armaActual.AddComponent<Rigidbody>();
        }
        weaponRb.isKinematic = false;
            
        // Lanzar el arma un poco
        weaponRb.AddForce((transform.forward + Vector3.up) * 3f, ForceMode.Impulse);
        
        hasWeapon = false;
        armaController.armaActual = null;
        
        // Iniciar cooldown para no recoger el arma inmediatamente
        weaponDropCooldown = WEAPON_DROP_COOLDOWN_TIME;

        Debug.Log($"{gameObject.name} suelta su arma");
    }
    
    private void PickupWeapon()
    {
        // Buscar armas cercanas en el suelo (rango aumentado para asegurar que encuentra)
        Collider[] nearbyObjects = Physics.OverlapSphere(transform.position, 5f);
        GameObject weaponToPickup = null;
        float closestDistance = float.MaxValue;
        
        foreach (Collider col in nearbyObjects)
        {
            // Buscar por tag o componente WeaponData
            if (col.CompareTag("Arma") || col.GetComponent<WeaponData>() != null)
            {
                float distance = Vector3.Distance(transform.position, col.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    weaponToPickup = col.gameObject;
                }
            }
        }
        
        if (weaponToPickup == null || armaController == null)
        {
            Debug.Log($"{gameObject.name} no encontró arma para recoger");
            return;
        }

        if(weaponToPickup.transform.parent.transform.parent.CompareTag("Player"))
            player.GetComponentInChildren<ArmaController>().DropCurrentWeapon();
        armaController.EquipWeapon(weaponToPickup);
       
        hasWeapon = true;
        Debug.Log($"{gameObject.name} recupera su arma: {weaponToPickup.name}");
        
        // Resetear cooldown después de recoger el arma
        weaponDropCooldown = 0f;
        
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
        
        // Cambiar al estado Muerto
        SetState(GuardState.Muerto);

        DropWeapon();

        // Agregar Rigidbody si no lo tiene
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = 80f;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.05f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.None;
        }
        
        // Aplicar una pequeña fuerza para que caiga de forma natural
        Vector3 fallForce = transform.forward * 35f + (Random.insideUnitSphere * 2f);
        rb.AddForce(fallForce, ForceMode.Impulse);

        Destroy(gameObject,3); // Destruir este gameObject después de 3 segundos

        // Desactivar componentes de movimiento
        navAgent.enabled = false;
        if (armaController != null)
        {
            armaController.enabled = false;
        }
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

