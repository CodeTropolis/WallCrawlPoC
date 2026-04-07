using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed    = 5f;
    public float jumpVelocity = 12f;

    [Header("Ground Detection")]
    public float groundCheckDistance   = 1.35f;
    public LayerMask groundMask;

    [Header("Wall Detection")]
    public float wallDetectorDistance = 2f;
    public LayerMask wallMask;

    private Rigidbody2D rb;
    private SpriteRenderer chassisRenderer;
    private bool isGrounded;
    private bool isFacingRight = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        chassisRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        CheckGround();
        HandleMovement();
    }

    void CheckGround()
    {
        Vector2 downDir    = -transform.up;
        Vector2 forwardDir = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;

        isGrounded = Physics2D.Raycast(transform.position, downDir, groundCheckDistance, groundMask);
        Debug.DrawRay(transform.position, downDir * groundCheckDistance, isGrounded ? Color.green : Color.red);

        bool wallDetected = Physics2D.Raycast(transform.position, forwardDir, wallDetectorDistance, wallMask);
        Debug.DrawRay(transform.position, forwardDir * wallDetectorDistance, wallDetected ? Color.magenta : Color.yellow);
    }

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");

        rb.linearVelocity = new Vector2(h * moveSpeed, rb.linearVelocity.y);

        if (h > 0.01f && !isFacingRight)
        {
            isFacingRight = true;
            chassisRenderer.flipX = false;
        }
        else if (h < -0.01f && isFacingRight)
        {
            isFacingRight = false;
            chassisRenderer.flipX = true;
        }

        if (Input.GetButtonDown("Jump") && isGrounded)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);
    }
}
