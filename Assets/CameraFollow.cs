using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector2 offset = new Vector2(10f, 0f);
    public float smoothTime       = 0.2f;
    public float facingTransition = 0.6f;  // how long the offset slides when facing flips

    private Vector2 velocity;
    private SpriteRenderer targetSprite;
    private float currentOffsetX;
    private float offsetVelocity;

    void Start()
    {
        if (target != null)
        {
            var rb = target.GetComponent<Rigidbody2D>();
            if (rb != null) rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            targetSprite = target.GetComponent<SpriteRenderer>();
        }
        currentOffsetX = offset.x;
    }

    void LateUpdate()
    {
        if (target == null) return;

        bool facingLeft  = targetSprite != null && targetSprite.flipX;
        float targetOffsetX = facingLeft ? -offset.x : offset.x;
        currentOffsetX = Mathf.SmoothDamp(currentOffsetX, targetOffsetX, ref offsetVelocity, facingTransition);

        Vector2 desired = (Vector2)target.position + new Vector2(currentOffsetX, offset.y);
        Vector2 smoothed = Vector2.SmoothDamp((Vector2)transform.position, desired, ref velocity, smoothTime);
        transform.position = new Vector3(smoothed.x, smoothed.y, transform.position.z);
    }
}
