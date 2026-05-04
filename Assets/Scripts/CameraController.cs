using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Visual Settings")]
    [Tooltip("Choose the background color for your simulation void!")]
    public Color backgroundColor = new Color(0.1f, 0.1f, 0.12f, 1f); 

    [Header("Orbit Settings")]
    public float orbitSpeed = 0.3f;
    public float minVerticalAngle = 10f;
    public float maxVerticalAngle = 85f;

    [Header("Pan Settings")]
    public float panSpeed = 0.05f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 0.1f;
    public float minZoom = 10f;
    public float maxZoom = 1000f;

    private Transform target;
    private float currentX = 45f; 
    private float currentY = 30f; 
    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
        
        cam.orthographic = true; 
        cam.nearClipPlane = -5000f; 
        cam.farClipPlane = 5000f;

        GameObject city = GameObject.Find("tokyo_cropped2");
        if (city != null)
        {
            Renderer[] renderers = city.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

                GameObject centerObj = new GameObject("CameraTarget");
                centerObj.transform.position = bounds.center;
                target = centerObj.transform;
            }
        }

        if (target == null) target = new GameObject("CameraTarget").transform;

        cam.orthographicSize = 250f; 
        UpdateCameraPosition();
    }

    void LateUpdate() 
    {
        if (target == null || Mouse.current == null) return;
        
        UIDocument uiDoc = FindObjectOfType<UIDocument>();
        if (uiDoc != null && uiDoc.rootVisualElement != null && uiDoc.rootVisualElement.panel != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 uiPos = new Vector2(mousePos.x, Screen.height - mousePos.y); 
            VisualElement picked = uiDoc.rootVisualElement.panel.Pick(uiPos);
            
            bool overSolidUI = false;
            VisualElement current = picked;
            
            while (current != null)
            {
                if (current.resolvedStyle.backgroundColor.a > 0.01f)
                {
                    overSolidUI = true;
                    break;
                }
                current = current.parent;
            }
            
            if (overSolidUI) return; 
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        if (Mouse.current.rightButton.isPressed)
        {
            currentX += mouseDelta.x * orbitSpeed;
            currentY -= mouseDelta.y * orbitSpeed;
            currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);
        }
        else if (Mouse.current.middleButton.isPressed)
        {
            float adjustedPanSpeed = panSpeed * (cam.orthographicSize / 50f);
            
            Vector3 move = -transform.right * mouseDelta.x * adjustedPanSpeed;
            move += -transform.up * mouseDelta.y * adjustedPanSpeed;
            move.y = 0f; 
            
            target.position += move;
        }

        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }

        UpdateCameraPosition();
    }

    void UpdateCameraPosition()
    {
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -2000f);
        
        transform.position = target.position + offset;
        transform.LookAt(target.position);
    }
}