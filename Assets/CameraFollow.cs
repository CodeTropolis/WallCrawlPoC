using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector2 offset = new Vector2(10f, 0f);
    public float smoothTime = 0.2f;

    private Vector2 velocity;

    void Start()
    {
        // Interpolate the rigidbody so its visual position is smooth between
        // physics steps, preventing jitter when the camera reads it in LateUpdate.
        if (target != null)
        {
            var rb = target.GetComponent<Rigidbody2D>();
            if (rb != null) rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector2 desired = (Vector2)target.position + offset;
        Vector2 smoothed = Vector2.SmoothDamp((Vector2)transform.position, desired, ref velocity, smoothTime);
        transform.position = new Vector3(smoothed.x, smoothed.y, transform.position.z);
    }
}
