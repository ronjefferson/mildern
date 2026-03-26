using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
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
        cam.orthographic = true; 
        
        // Ensure the camera can see the whole city without clipping
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

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        // ORBIT: Right-Click and Drag
        if (Mouse.current.rightButton.isPressed)
        {
            currentX += mouseDelta.x * orbitSpeed;
            currentY -= mouseDelta.y * orbitSpeed;
            currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);
        }
        // PAN: Middle-Click and Drag
        else if (Mouse.current.middleButton.isPressed)
        {
            float adjustedPanSpeed = panSpeed * (cam.orthographicSize / 50f);
            
            Vector3 move = -transform.right * mouseDelta.x * adjustedPanSpeed;
            move += -transform.up * mouseDelta.y * adjustedPanSpeed;
            move.y = 0f; // Lock Y-axis so panning doesn't sink the camera
            
            target.position += move;
        }

        // ZOOM: Scroll Wheel
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