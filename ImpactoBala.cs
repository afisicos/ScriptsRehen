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
        if (layer == layerSangre)
        {
            meshRenderer.material.color = Color.red;
        }
        else if (layer == layerPiedra)
        {
            meshRenderer.material.color = Color.gray;
        }
    }
}
