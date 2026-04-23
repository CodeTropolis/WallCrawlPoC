using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float speed = 15f;

    void Update()
    {
        transform.position += transform.right * speed * Time.deltaTime;

        Vector3 vp = Camera.main.WorldToViewportPoint(transform.position);
        if (vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f)
            Destroy(gameObject);
    }
}
