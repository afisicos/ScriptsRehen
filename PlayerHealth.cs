using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    [Header("Salud")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [SerializeField] private bool invulnerable = false;
    [SerializeField] private float invulnerabilityDuration = 0f; // Duración de invulnerabilidad después de recibir daño
    
    [Header("Regeneración")]
    [SerializeField] private bool canRegenerate = false;
    [SerializeField] private float regenerationDelay = 5f; // Tiempo antes de empezar a regenerar
    [SerializeField] private float regenerationRate = 1f; // Salud por segundo
    [SerializeField] private float regenerationTimer = 0f;
    
    [Header("Efectos")]
    [SerializeField] private GameObject deathEffectPrefab; // Efecto visual al morir
    [SerializeField] private AudioClip hurtSound; // Sonido al recibir daño
    [SerializeField] private AudioClip deathSound; // Sonido al morir
    
    [Header("Eventos")]
    public UnityEvent<float> OnHealthChanged; // Pasa el porcentaje de salud (0-1)
    public UnityEvent<float> OnDamageTaken; // Pasa la cantidad de daño recibido
    public UnityEvent OnDeath;
    public UnityEvent OnHeal;
    
    // Componentes
    private AudioSource audioSource;
    private float invulnerabilityTimer = 0f;
    private bool isDead = false;
    
    private void Awake()
    {
        // Inicializar salud
        currentHealth = maxHealth;
        
        // Obtener AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }
    
    private void Start()
    {
        // Notificar salud inicial
        OnHealthChanged?.Invoke(GetHealthPercentage());
    }
    
    private void Update()
    {
        // Actualizar timer de invulnerabilidad
        if (invulnerabilityTimer > 0f)
        {
            invulnerabilityTimer -= Time.deltaTime;
            if (invulnerabilityTimer <= 0f)
            {
                invulnerabilityTimer = 0f;
            }
        }
        
        // Regeneración de salud
        if (canRegenerate && !isDead && currentHealth < maxHealth)
        {
            regenerationTimer += Time.deltaTime;
            
            if (regenerationTimer >= regenerationDelay)
            {
                Heal(regenerationRate * Time.deltaTime);
            }
        }
        else
        {
            regenerationTimer = 0f;
        }
    }
    
    public void TakeDamage(float damage)
    {
        // Si está muerto o invulnerable, no recibir daño
        if (isDead || IsInvulnerable())
        {
            return;
        }
        
        // Aplicar daño
        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        
        // Activar invulnerabilidad temporal si está configurada
        if (invulnerabilityDuration > 0f)
        {
            invulnerabilityTimer = invulnerabilityDuration;
        }
        
        // Resetear timer de regeneración
        regenerationTimer = 0f;
        
        // Reproducir sonido de daño
        if (hurtSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(hurtSound);
        }
        
        // Notificar eventos
        OnDamageTaken?.Invoke(damage);
        OnHealthChanged?.Invoke(GetHealthPercentage());
        
        // Verificar si murió
        if (currentHealth <= 0f)
        {
            Die();
        }
    }
    
    public void Heal(float amount)
    {
        // Si está muerto, no puede curarse
        if (isDead)
        {
            return;
        }
        
        // Aplicar curación
        float previousHealth = currentHealth;
        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        
        // Solo notificar si realmente se curó
        if (currentHealth > previousHealth)
        {
            OnHeal?.Invoke();
            OnHealthChanged?.Invoke(GetHealthPercentage());
        }
    }
    
    public void HealToFull()
    {
        Heal(maxHealth);
    }
    
    public void SetMaxHealth(float newMaxHealth)
    {
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        OnHealthChanged?.Invoke(GetHealthPercentage());
    }
    
    public void SetInvulnerable(bool invulnerableState)
    {
        invulnerable = invulnerableState;
    }
    
    private void Die()
    {
        if (isDead)
        {
            return;
        }
        
        isDead = true;
        
        // Reproducir sonido de muerte
        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }
        
        // Generar efecto de muerte
        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
        }
        
        // Notificar evento de muerte
        OnDeath?.Invoke();
        
        // Deshabilitar componentes del player
        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = false;
        }
        
        ArmaController armaController = GetComponent<ArmaController>();
        if (armaController != null)
        {
            armaController.enabled = false;
        }
        
        // Aquí se puede añadir lógica adicional de muerte
        // Por ejemplo: mostrar pantalla de game over, respawn, etc.
        Debug.Log("Player ha muerto");
    }
    
    public void Respawn()
    {
        // Resetear salud
        currentHealth = maxHealth;
        isDead = false;
        invulnerabilityTimer = 0f;
        regenerationTimer = 0f;
        
        // Rehabilitar componentes
        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = true;
        }
        
        ArmaController armaController = GetComponent<ArmaController>();
        if (armaController != null)
        {
            armaController.enabled = true;
        }
        
        // Notificar cambio de salud
        OnHealthChanged?.Invoke(GetHealthPercentage());
    }
    
    public bool IsInvulnerable()
    {
        return invulnerable || invulnerabilityTimer > 0f;
    }
    
    public bool IsDead()
    {
        return isDead;
    }
    
    // Getters
    public float GetHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetHealthPercentage() => maxHealth > 0 ? currentHealth / maxHealth : 0f;
    
    // Método para obtener información de salud como string (útil para UI)
    public string GetHealthText()
    {
        return $"{Mathf.Ceil(currentHealth)} / {maxHealth}";
    }
}

