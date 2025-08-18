using UnityEngine;

/// <summary>
/// Changes the color of the material on the supplied GameObject's Renderer.
/// </summary>
public class DetectionHighlighter : MonoBehaviour
{
    [Tooltip("The GameObject whose material color will be changed.")]
    [SerializeField]
    private GameObject targetObject;

    private Material _material;

    private void Awake()
    {
        if (targetObject == null)
        {
            Debug.LogError("Target GameObject is not assigned.");
            return;
        }

        Renderer renderer = targetObject.GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("No Renderer found on the target GameObject.");
            return;
        }

        // Use a unique instance of the material
        _material = renderer.material;
    }

    /// <summary>
    /// Changes the color of the target GameObject's material.
    /// </summary>
    /// <param name="color">The new color to apply.</param>
    public void ChangeColor(Color color)
    {
        if (_material != null)
        {
            _material.SetColor("_Color", color);
        }
    }

    private void OnDestroy()
    {
        if (_material != null)
        {
            Destroy(_material);
        }
    }
}