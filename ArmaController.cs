using UnityEngine;
using UnityEngine.Audio;

public class ArmaController : MonoBehaviour
{
    [SerializeField]WeaponData weaponData;     
    
    [Header("Referencias")]
    [SerializeField] private Transform armaHolder; // Transform donde se coloca el arma
    public GameObject armaActual; // Prefab del arma visual
    [SerializeField] private GameObject impactoBalaPrefab; // Prefab del impacto de bala
    [SerializeField] private Camera playerCamera; // Solo para player
    [SerializeField] private LayerMask shootableLayers; // Layers que se pueden disparar

    [SerializeField] private AudioSource audioSource; 

    // Estado interno
    private float lastFireTime = 0f;
    private bool isPlayer = false;
    private Transform target; // Para guardias, el target a disparar
    
    private void Awake()
    {
        isPlayer = CompareTag("Player");
        audioSource = GetComponent<AudioSource>();
    }
    
    
    private void Start()
    {
    }
    
    private void Update()
    {
        // Para player: disparar con click del mouse
        if (isPlayer)
        {
            HandlePlayerShooting();
        }
        // Para guardias: el disparo se maneja externamente desde GuardController
    }
    
    private void HandlePlayerShooting()
    {
        if (armaActual == null) return;
        
        bool shouldShoot = false;
        
        if (weaponData.isAutomatic)
        {
            // Disparo automático: mantener botón presionado
            shouldShoot = Input.GetMouseButton(0);
        }
        else
        {
            // Disparo semiautomático: click único
            shouldShoot = Input.GetMouseButtonDown(0);
        }
        
        if (shouldShoot)
        {
            TryShoot();
        }
    }
    
    public bool TryShoot()
    {
        // Verificar cooldown
        if (Time.time - lastFireTime < weaponData.fireRate)
        {
            return false;
        }
        
        if (armaActual == null)
        {
            return false;
        }
        
        lastFireTime = Time.time;
        
        // Calcular dirección de disparo
        Vector3 shootDirection;
        Vector3 shootOrigin;
        
        if (isPlayer)
        {
            // Player: disparar hacia donde apunta el mouse
            shootOrigin = armaHolder.position;
            shootDirection = GetMouseDirection();
        }
        else
        {
            // Guardia: disparar hacia el target
            if (target == null)
            {
                return false;
            }
            
            shootOrigin = armaHolder.position;
            shootDirection = (target.position - shootOrigin).normalized;
        }
        
        // Aplicar precisión/dispersión
        shootDirection = ApplyAccuracy(shootDirection);
        
        // Realizar raycast
        PerformRaycast(shootOrigin, shootDirection);
        
        return true;
    }
    
    private Vector3 GetMouseDirection()
    {
        if (playerCamera == null) return transform.forward;
        
        // Obtener posición del mouse en el mundo (plano horizontal)
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        Plane groundPlane = new Plane(Vector3.up, transform.position);
        
        if (groundPlane.Raycast(ray, out float distance))
        {
            Vector3 targetPoint = ray.GetPoint(distance);
            Vector3 direction = (targetPoint - armaHolder.position).normalized;
            direction.y = 0f; // Mantener en plano horizontal
            return direction.normalized;
        }
        
        return transform.forward;
    }
    
    private Vector3 ApplyAccuracy(Vector3 direction)
    {
        // Calcular dispersión basada en la precisión
        float spreadAmount = (1f - weaponData.accuracy) * weaponData.spreadAngle;
        
        // Aplicar dispersión aleatoria
        float randomAngle = Random.Range(-spreadAmount, spreadAmount);
        Quaternion spreadRotation = Quaternion.Euler(0, randomAngle, 0);
        
        return spreadRotation * direction;
    }
    
    private void PerformRaycast(Vector3 origin, Vector3 direction)
    {
        RaycastHit hit;
        audioSource.PlayOneShot(weaponData.shootSound);

        if (Physics.Raycast(origin, direction, out hit, weaponData.range, shootableLayers))
        {
            // Verificar qué se impactó
            GameObject hitObject = hit.collider.gameObject;
            
            // Generar impacto visual
            if (impactoBalaPrefab != null)
            {
                GameObject impacto = Instantiate(impactoBalaPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                
                // Llamar a Impact con la layer del objeto impactado
                ImpactoBala impactoScript = impacto.GetComponent<ImpactoBala>();
                if (impactoScript != null)
                {
                    LayerMask hitLayerMask = 1 << hitObject.layer;
                    impactoScript.Impact(hitLayerMask);
                }
            }

            // Aplicar daño si el objeto tiene un componente de salud
            
            ApplyDamage(hitObject, hit.point);
        }
    }
    
    private void PerformRaycastWithMiss(Vector3 origin, Vector3 direction)
    {
        // Añadir una desviación importante para los fallos
        float missSpread = 45f; // Ángulo de dispersión para fallos (mucho mayor que el normal)
        float randomAngle = Random.Range(-missSpread, missSpread);
        Quaternion missRotation = Quaternion.Euler(Random.Range(-missSpread * 0.5f, missSpread * 0.5f), randomAngle, 0);
        direction = missRotation * direction;
        
        RaycastHit hit;
        audioSource.PlayOneShot(weaponData.shootSound);

        if (Physics.Raycast(origin, direction, out hit, weaponData.range, shootableLayers))
        {
            // Verificar qué se impactó
            GameObject hitObject = hit.collider.gameObject;
            
            // Generar impacto visual
            if (impactoBalaPrefab != null)
            {
                GameObject impacto = Instantiate(impactoBalaPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                
                // Llamar a Impact con la layer del objeto impactado
                ImpactoBala impactoScript = impacto.GetComponent<ImpactoBala>();
                if (impactoScript != null)
                {
                    LayerMask hitLayerMask = 1 << hitObject.layer;
                    impactoScript.Impact(hitLayerMask);
                }
            }
            
            // NO aplicar daño en los fallos (solo impacto visual)
        }
    }
    
    private void ApplyDamage(GameObject target, Vector3 hitPoint)
    {
        // Intentar obtener componente de salud del player
        if (target.CompareTag("Player"))
        {
            PlayerHealth playerHealth = target.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(weaponData.damage);
            }
        }
        // Intentar obtener componente de salud del guardia
        else if (target.CompareTag("Guard") || target.GetComponent<GuardController>() != null)
        {
            GuardController guard = target.GetComponent<GuardController>();
            if (guard != null)
            {
                guard.TakeDamage(weaponData.damage);
            }
        }
    }
    

    public void DropCurrentWeapon()
    {
        if(armaActual != null)
        {
            armaActual = null;
            weaponData = null;
        }
    }
    
    // Método para equipar un arma nueva
    public void EquipWeapon(GameObject weaponToEquip)
    {
        if(armaActual != null)
        {
            Destroy(armaActual);
        }

        armaActual = weaponToEquip;

        Debug.Log("Armandose con arma: " + weaponToEquip.name);

        weaponToEquip.transform.SetParent(armaHolder);
        weaponToEquip.transform.localPosition = Vector3.zero;
        weaponToEquip.transform.localRotation = Quaternion.identity;

        weaponData = weaponToEquip.GetComponent<WeaponData>();
        
    }
    
    
    // Método para que los guardias establezcan su target
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
    
    // Método público para disparar (usado por guardias)
    public void Shoot()
    {
        TryShoot();
    }
    
    // Método público para disparar fallando (usado por guardias con baja precisión)
    public void ShootWithMiss()
    {
        // Verificar cooldown
        if (Time.time - lastFireTime < weaponData.fireRate)
        {
            return;
        }
        
        if (armaActual == null)
        {
            return;
        }
        
        lastFireTime = Time.time;
        
        // Calcular dirección de disparo
        Vector3 shootDirection;
        Vector3 shootOrigin;
        
        if (isPlayer)
        {
            // El player no debería usar esto, pero por si acaso
            shootOrigin = armaHolder.position;
            shootDirection = GetMouseDirection();
        }
        else
        {
            // Guardia: disparar hacia el target pero con desviación
            if (target == null)
            {
                return;
            }
            
            shootOrigin = armaHolder.position;
            shootDirection = (target.position - shootOrigin).normalized;
        }
        
        // Realizar raycast con dispersión de fallo
        PerformRaycastWithMiss(shootOrigin, shootDirection);
    }
    
    // Getters públicos
    public bool IsAutomatic() => weaponData.isAutomatic;
    public float GetFireRate() => weaponData.fireRate;
    public float GetDamage() => weaponData.damage;
    public float GetRange() => weaponData.range;
    public float GetAccuracy() => weaponData != null ? weaponData.accuracy : 0.5f;
    public bool HasWeapon() => armaActual != null;
    

}

