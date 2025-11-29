using UnityEngine;

public class WeaponData : MonoBehaviour
{
    public float damage = 10f;
    public float fireRate = 0.5f; // Tiempo entre disparos en segundos
    public float accuracy = 0.95f; // Precisión (0-1, donde 1 es perfecto)
    public bool isAutomatic = false; // Si es automática o semiautomática
    public float range = 50f; // Alcance máximo del arma
    public AudioClip shootSound; // Sonido de disparo
    public string weaponName = "Arma";
    public string ownerTag;
}

