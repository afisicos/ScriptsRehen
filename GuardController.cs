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
    private const float WEAPON_DROP_COOLDOWN_TIME = 1.2f; // 2 segundos antes de poder recoger
    private Vector3 lastPlayerPosition; // Para calcular velocidad del player
    private float playerSpeedLastFrame = 0f; // Velocidad del player en el frame anterior
    private float stunDuration = 0f; // Duración del aturdimiento por disparo
    private const float STUN_TIME_MIN = 0.4f; // Mínimo tiempo de aturdimiento
    private const float STUN_TIME_MAX = 1.2f; // Máximo tiempo de aturdimiento
    private const float KNOCKBACK_FORCE = 25f; // Fuerza del retroceso
    
    // Variables de búsqueda
    private bool hasReachedLastKnownPosition = false;
    private float searchRotationTimer = 0f;
    private float randomSearchTimer = 0f;
    private Vector3 searchStartPosition; // Posición desde donde empezó a buscar
    private Vector3 randomSearchDestination;
    private bool isRotating = false;
    private float initialRotationY;
    private Vector3 guardPostPosition; // Posición de guardia o waypoint al que volver
    
    // Variables de vigilancia
    private float vigilanceRotationTimer = 0f;
    private float vigilanceLookDuration = 1f; // Tiempo mirando en cada dirección (más rápido)
    private int[] vigilanceLookDirections = new int[4] { 0, 1, 2, 3 }; // Direcciones disponibles
    private int vigilanceLookDirectionIndex = 0; // Índice en el array aleatorio
    private const float VIGILANCE_ROTATION_SMOOTH = 8f; // Más rápido
    private float initialVigilanceRotationY = 0f;
    private bool isVigilanceRotating = false;
    private Vector3 vigilanceMovementDirection = Vector3.zero;
    
    
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
        
        // Si está desarmado, ir a buscar arma en lugar de perseguir
        if (!isArmed)
        {
            // Buscar si hay armas cercanas
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
            
            if (hasWeaponNearby)
            {
                SetState(GuardState.RecuperandoArma);
            }
            else
            {
                // Sin arma cerca, huir
                SetState(GuardState.Huyendo);
            }
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
        // Si detecta al player, cambiar a perseguir
        if (playerDetected && player != null)
        {
            SetState(GuardState.Persiguiendo);
            return;
        }
        
        // Solo escanear sin moverse
        if (!isVigilanceRotating)
        {
            isVigilanceRotating = true;
            initialVigilanceRotationY = transform.eulerAngles.y;
            vigilanceRotationTimer = 0f;
            ShuffleVigilanceDirections();
            vigilanceLookDirectionIndex = 0;
        }
        
        vigilanceRotationTimer += Time.deltaTime;
        
        // Calcular ángulo objetivo según la dirección actual
        int currentDirection = vigilanceLookDirections[vigilanceLookDirectionIndex];
        float targetRotationY = initialVigilanceRotationY;
        switch (currentDirection)
        {
            case 0:
                targetRotationY = initialVigilanceRotationY;
                break;
            case 1:
                targetRotationY = initialVigilanceRotationY + 90f;
                break;
            case 2:
                targetRotationY = initialVigilanceRotationY + 180f;
                break;
            case 3:
                targetRotationY = initialVigilanceRotationY - 90f;
                break;
        }
        
        // Rotar suavemente
        Quaternion targetRotation = Quaternion.Euler(0, targetRotationY, 0);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, VIGILANCE_ROTATION_SMOOTH * Time.deltaTime);
        
        // Cambiar dirección cada 1 segundo
        if (vigilanceRotationTimer >= vigilanceLookDuration)
        {
            vigilanceLookDirectionIndex++;
            if (vigilanceLookDirectionIndex >= 4)
            {
                ShuffleVigilanceDirections();
                vigilanceLookDirectionIndex = 0;
            }
            vigilanceRotationTimer = 0f;
        }
    }
    
    private void ShuffleVigilanceDirections()
    {
        // Fisher-Yates shuffle para barajar las direcciones
        for (int i = vigilanceLookDirections.Length - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            
            // Intercambiar
            int temp = vigilanceLookDirections[i];
            vigilanceLookDirections[i] = vigilanceLookDirections[randomIndex];
            vigilanceLookDirections[randomIndex] = temp;
        }
    }
    
    private void GenerateRandomMovementDirection()
    {
        // Generar una dirección de movimiento aleatoria
        float randomAngle = Random.Range(0f, 360f);
        vigilanceMovementDirection = new Vector3(Mathf.Cos(randomAngle * Mathf.Deg2Rad), 0, Mathf.Sin(randomAngle * Mathf.Deg2Rad)).normalized;
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
        
        // Si está desarmado, ir a buscar arma en lugar de atacar
        if (!isArmed)
        {
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
            
            if (hasWeaponNearby)
            {
                SetState(GuardState.RecuperandoArma);
                return;
            }
            else
            {
                // Sin arma y sin armas cercanas, huir
                SetState(GuardState.Huyendo);
                return;
            }
        }
        
        // Si detecta al player, perseguir
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
                float accuracy = CalculateAccuracy();
                if (Random.value < accuracy)
                {
                    if (armaController != null && isArmed)
                        armaController.Shoot();
                }
                else
                {
                    if (armaController != null && isArmed)
                        armaController.ShootWithMiss();
                }
            }
            // Acercarse si no está en rango
            else
            {
                navAgent.isStopped = false;
                navAgent.SetDestination(player.position);
            }
        }
        else
        {
            // Perdió de vista, ir a buscar
            StartSearching();
        }
    }
    
    private void StartSearching()
    {
        SetState(GuardState.Buscando);
    }
    
    private void UpdateBuscando()
    {
        // Si detecta al player, perseguir
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
                searchRotationTimer = 0f;
                ShuffleVigilanceDirections();
                vigilanceLookDirectionIndex = 0;
            }
        }
        // Si ya llegó, escanear la zona
        else if (isRotating)
        {
            searchRotationTimer += Time.deltaTime;
            
            // Escaneo similar al de vigilancia
            int currentDirection = vigilanceLookDirections[vigilanceLookDirectionIndex];
            float targetRotationY = initialRotationY;
            switch (currentDirection)
            {
                case 0:
                    targetRotationY = initialRotationY;
                    break;
                case 1:
                    targetRotationY = initialRotationY + 90f;
                    break;
                case 2:
                    targetRotationY = initialRotationY + 180f;
                    break;
                case 3:
                    targetRotationY = initialRotationY - 90f;
                    break;
            }
            
            Quaternion targetRotation = Quaternion.Euler(0, targetRotationY, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, VIGILANCE_ROTATION_SMOOTH * Time.deltaTime);
            
            // Moverse lentamente en dirección aleatoria mientras escanea
            Vector3 forwardDirection = transform.forward;
            forwardDirection.y = 0f;
            forwardDirection = forwardDirection.normalized;
            
            Vector3 newPosition = transform.position + forwardDirection * 0.5f * Time.deltaTime;
            if (Vector3.Distance(newPosition, searchStartPosition) < 5f)
            {
                transform.position = newPosition;
            }
            
            // Cambiar dirección cada 1 segundo
            if (searchRotationTimer >= 1f)
            {
                vigilanceLookDirectionIndex++;
                
                if (vigilanceLookDirectionIndex >= 4)
                {
                    ShuffleVigilanceDirections();
                    vigilanceLookDirectionIndex = 0;
                    randomSearchTimer += 1f;
                    
                    // Después de 10 segundos sin encontrar, volver al puesto de guardia
                    if (randomSearchTimer >= randomSearchDuration)
                    {
                        ReturnToGuardPost();
                        return;
                    }
                }
                
                searchRotationTimer = 0f;
            }
        }
    }
    
    private void ReturnToGuardPost()
    {
        SetState(GuardState.Vigilando);
        // Mover el guardia a su puesto de guardia
        transform.position = guardPostPosition;
        navAgent.SetDestination(guardPostPosition);
        Debug.Log($"{gameObject.name} vuelve a su puesto de guardia");
    }
    
    private void UpdateHuyendo()
    {
        if (player == null) return;
        
        // Si recupera arma y está sano, dejar de huir
        if (isArmed && currentHealth >= lowHealthThreshold)
        {
            SetState(GuardState.Vigilando);
            return;
        }
        
        // Si está desarmado, ir a buscar arma
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
        
        // Huir en dirección opuesta al player
        Vector3 fleeDirection = (transform.position - player.position).normalized;
        Vector3 fleePosition = transform.position + fleeDirection * 15f;
        
        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePosition, out hit, 15f, NavMesh.AllAreas))
        {
            navAgent.SetDestination(hit.position);
        }
        
        // Si está muy lejos, volver a vigilar
        if (Vector3.Distance(transform.position, player.position) > detectionRange * 2f)
        {
            SetState(GuardState.Vigilando);
        }
    }
    
    private void UpdateRecuperandoArma()
    {
        // Si estamos armados y sanos, volver a vigilar
        if (isArmed && currentHealth >= lowHealthThreshold)
        {
            SetState(GuardState.Vigilando);
            return;
        }
        
        // Si el cooldown no ha pasado, no hacer nada
        if (weaponDropCooldown > 0f)
            return;
        
        // Buscar arma cercana (primero en rango corto)
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
        
        // Si no hay arma cerca, buscar en rango mayor
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
        
        // Si no hay arma, ir a vigilar
        if (nearbyWeapon == null)
        {
            SetState(GuardState.Vigilando);
            return;
        }
        
        // Si está muy cerca del arma, recogerla
        float distanceToWeapon = Vector3.Distance(transform.position, nearbyWeapon.transform.position);
        if (distanceToWeapon < 1f)
        {
            PickupWeapon();
            return;
        }
        
        // Moverse hacia el arma
        navAgent.SetDestination(nearbyWeapon.transform.position);
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
        
        switch (newState)
        {
            case GuardState.Vigilando:
                navAgent.isStopped = true;
                isVigilanceRotating = false;
                break;
            case GuardState.Persiguiendo:
                navAgent.isStopped = false;
                navAgent.speed = chaseSpeed;
                break;
            case GuardState.Buscando:
                navAgent.isStopped = false;
                navAgent.speed = patrolSpeed;
                hasReachedLastKnownPosition = false;
                searchRotationTimer = 0f;
                randomSearchTimer = 0f;
                isRotating = false;
                searchStartPosition = transform.position;
                navAgent.SetDestination(lastKnownPlayerPosition);
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
        
        // Si es ataque a melee
        if (isMeleeAttack && currentHealth > 0)
        {
            SetState(GuardState.Derribado);
            return;
        }
        
        // Si es disparo
        if (!isMeleeAttack && currentHealth > 0 && player != null)
        {
            // Actualizar posición conocida
            lastKnownPlayerPosition = player.position;
            
            // Si está en vigilancia o patrulla, reaccionar al disparo
            if (currentState == GuardState.Vigilando)
            {
                if (!isArmed)
                {
                    // Desarmado: buscar arma
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
                    
                    if (hasWeaponNearby)
                        SetState(GuardState.RecuperandoArma);
                    else
                        SetState(GuardState.Huyendo);
                }
                else
                {
                    // Armado: persecutar
                    SetState(GuardState.Persiguiendo);
                    AlertNearbyGuards();
                }
            }
            
            // Knockback y aturdimiento
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }
            
            Vector3 knockbackDirection = (transform.position - player.position).normalized;
            knockbackDirection.y = 0f;
            
            if (!rb.isKinematic)
                rb.AddForce(knockbackDirection * KNOCKBACK_FORCE, ForceMode.Impulse);
            else
                transform.Translate(knockbackDirection * KNOCKBACK_FORCE * Time.deltaTime, Space.World);
            
            stunDuration = Random.Range(STUN_TIME_MIN, STUN_TIME_MAX);
            
            if (navAgent != null)
                navAgent.isStopped = true;
            
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
            DropWeapon();
    }
    
    private void DropWeapon()
    {

        if (!armaController.HasWeapon()) return;

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
        
        // Después de soltar el arma, intentar recuperarla
        // Buscar si hay armas cercanas
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
        
        // Si hay un arma cerca, ir a recuperarla
        if (hasWeaponNearby && currentState == GuardState.Persiguiendo)
        {
            SetState(GuardState.RecuperandoArma);
        }
    }
    
    private void PickupWeapon()
    {
        Collider[] nearbyObjects = Physics.OverlapSphere(transform.position, 5f);
        GameObject weaponToPickup = null;
        float closestDistance = float.MaxValue;
        
        foreach (Collider col in nearbyObjects)
        {
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

        // Si es arma del player, desarmar al player
        if (weaponToPickup.GetComponent<WeaponData>().ownerTag == "Player")
            player.GetComponentInChildren<ArmaController>().DropCurrentWeapon();

        armaController.EquipWeapon(weaponToPickup);
        Destroy(weaponToPickup.GetComponent<Rigidbody>());
        weaponToPickup.GetComponent<WeaponData>().ownerTag = "Guard";

        hasWeapon = true;
        Debug.Log($"{gameObject.name} recoge arma: {weaponToPickup.name}");
        
        weaponDropCooldown = 0f;
        
        // Volver a vigilar
        SetState(GuardState.Vigilando);
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
        // Si el guardia está en estado normal y está armed y sano, ir a investigar
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

        // Desactivar el NavMeshAgent PRIMERO antes de añadir Rigidbody
        if (navAgent != null && navAgent.enabled)
        {
            navAgent.enabled = false;
        }

        // Agregar o obtener Rigidbody
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        
        // Configurar Rigidbody para físicas realistas
        rb.mass = 80f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.None;
        
        // Aplicar una pequeña fuerza para que caiga de forma natural
        Vector3 fallForce = transform.forward * 10f + (Random.insideUnitSphere * 2f);
        rb.AddForce(fallForce, ForceMode.Impulse);

        // Soltar el arma si tiene
        DropWeapon();

        // Cambiar al estado Muerto (DESPUÉS de desactivar NavMeshAgent)
        currentState = GuardState.Muerto;

        // Desactivar componentes
        if (armaController != null)
        {
            armaController.enabled = false;
        }

        // Destruir este gameObject después de 3 segundos
        Destroy(gameObject, 3);
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

