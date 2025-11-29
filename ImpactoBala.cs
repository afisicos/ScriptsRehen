using UnityEngine;

public class ImpactoBala : MonoBehaviour
{
    [SerializeField] private LayerMask layerSangre;
    [SerializeField] private LayerMask layerPiedra;

    public MeshRenderer meshRenderer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Destroy(gameObject, 1f);
    }

    public void Impact(LayerMask layer)
    {
        // Obtener el número de layer del LayerMask recibido
        int layerNumber = 0;
        for (int i = 0; i < 32; i++)
        {
            if ((layer & (1 << i)) != 0)
            {
                layerNumber = i;
                break;
            }
        }
        
        // Verificar si la layer está en layerSangre
        if ((layerSangre & (1 << layerNumber)) != 0)
        {
            meshRenderer.material.color = Color.red;
        }
        // Verificar si la layer está en layerPiedra
        else if ((layerPiedra & (1 << layerNumber)) != 0)
        {
            meshRenderer.material.color = Color.gray;
        }
    }
}
