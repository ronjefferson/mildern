using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Orbit Settings")]
    public float orbitSpeed = 0.3f;
    public float minVerticalAngle = 10f;
    public float maxVerticalAngle = 85f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 20f;
    public float minZoom = 50f;
    public float maxZoom = 2000f;

    private Transform target;
    private float currentX = 0f;
    private float currentY = 45f;
    private float currentZoom = 500f;
    private Vector2 lastMousePosition;

    void Start()
    {
        GameObject city = GameObject.Find("tokyo_cropped2");
        if (city != null)
        {
            // Get the actual center of the model using its bounds
            Renderer[] renderers = city.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            // Create an empty target at the center of the model
            GameObject centerObj = new GameObject("CameraTarget");
            centerObj.transform.position = bounds.center;
            target = centerObj.transform;
        }

        UpdateCameraPosition();
    }

    void Update()
    {
        if (target == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            lastMousePosition = Mouse.current.position.ReadValue();
        }
        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 delta = Mouse.current.position.ReadValue() - lastMousePosition;
            currentX += delta.x * orbitSpeed;
            currentY -= delta.y * orbitSpeed;
            currentY = Mathf.Clamp(currentY, minVerticalAngle, maxVerticalAngle);
            lastMousePosition = Mouse.current.position.ReadValue();
        }

        float scroll = Mouse.current.scroll.ReadValue().y;
        currentZoom -= scroll * zoomSpeed;
        currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);

        UpdateCameraPosition();
    }

    void UpdateCameraPosition()
    {
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -currentZoom);
        transform.position = target.position + offset;
        transform.LookAt(target.position);
    }
}