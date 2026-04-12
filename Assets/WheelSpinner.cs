using UnityEngine;

/// <summary>
/// Attach to a wheel sprite GameObject. Tracks the vehicle's movement each frame
/// and rotates this transform to match the rolling motion.
/// </summary>
public class WheelSpinner : MonoBehaviour
{
    [Tooltip("The vehicle transform to track. Defaults to the direct parent if unset.")]
    public Transform vehicleTransform;

    [Tooltip("Wheel radius in world units. Controls how fast the sprite spins.")]
    public float wheelRadius = 0.3f;

    private Vector2 previousVehiclePosition;
    private float angle = 0f;

    void Start()
    {
        if (vehicleTransform == null)
            vehicleTransform = transform.parent;

        previousVehiclePosition = vehicleTransform.position;
    }

    void Update()
    {
        Spin();
    }

    void Spin()
    {
        Vector2 currentPosition = vehicleTransform.position;
        Vector2 moved = currentPosition - previousVehiclePosition;

        // Project movement onto the vehicle's local X axis so wall/ceiling/ground
        // crawling all produce the correct spin direction automatically.
        float rollDist = Vector2.Dot(moved, (Vector2)vehicleTransform.right);
        angle -= rollDist / wheelRadius * Mathf.Rad2Deg;

        transform.localEulerAngles = new Vector3(0f, 0f, angle);
        previousVehiclePosition = currentPosition;
    }
}
