using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(Camera))]
public class UIViewportFitter : MonoBehaviour
{
    public UIDocument uiDocument;
    public string viewAreaName = "GameViewArea";
    
    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;

        VisualElement viewArea = uiDocument.rootVisualElement.Q<VisualElement>(viewAreaName);
        if (viewArea == null) return;

        // Get the absolute screen boundaries to map exactly to the monitor
        Rect uiRect = viewArea.worldBound;
        Rect rootRect = uiDocument.rootVisualElement.worldBound;

        // Prevent math errors on frame 1
        if (uiRect.width == 0 || rootRect.width == 0 || rootRect.height == 0) return;

        float normalizedX = uiRect.x / rootRect.width;
        float normalizedY = (rootRect.height - uiRect.y - uiRect.height) / rootRect.height;
        float normalizedWidth = uiRect.width / rootRect.width;
        float normalizedHeight = uiRect.height / rootRect.height;

        cam.rect = new Rect(normalizedX, normalizedY, normalizedWidth, normalizedHeight);
    }
}