using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed      = 5f;
    public float jumpVelocity   = 12f;
    public float wallClimbSpeed = 5f;
    public float rotationSpeed  = 180f;

    [Header("Ground Detection")]
    public float groundCheckDistance = 1.35f;
    public LayerMask groundMask;

    [Header("Wall Detection")]
    public float wallDetectorDistance = 2f;
    public LayerMask wallMask;

    private Rigidbody2D rb;
    private SpriteRenderer chassisRenderer;
    private bool isGrounded;
    private bool isFacingRight = true;

    private enum State { Ground, RotatingToWall, WallCrawl }
    private State state = State.Ground;
    private float targetAngle    = 0f;
    private float lockedSurfaceX = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        chassisRenderer = GetComponent<SpriteRenderer>();
    }

    void Update()
    {
        switch (state)
        {
            case State.Ground:         UpdateGround();    break;
            case State.RotatingToWall: UpdateRotating();  break;
            case State.WallCrawl:      UpdateWallCrawl(); break;
        }
        DrawRays();
    }

    // ── Ground ────────────────────────────────────────────────────────────────

    void UpdateGround()
    {
        isGrounded = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, groundMask);

        float h = Input.GetAxisRaw("Horizontal");
        rb.linearVelocity = new Vector2(h * moveSpeed, rb.linearVelocity.y);

        if      (h >  0.01f && !isFacingRight) { isFacingRight = true;  chassisRenderer.flipX = false; }
        else if (h < -0.01f &&  isFacingRight) { isFacingRight = false; chassisRenderer.flipX = true;  }

        if (Input.GetButtonDown("Jump") && isGrounded)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);

        if (isGrounded)
        {
            Vector2 fwd = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
            RaycastHit2D hit = Physics2D.Raycast(transform.position, fwd, wallDetectorDistance, wallMask);
            if (hit.collider != null)
                EnterWallRotation(hit);
        }
    }

    void EnterWallRotation(RaycastHit2D hit)
    {
        state = State.RotatingToWall;

        // Rotate so the chassis bottom (-transform.up) faces the wall.
        // Right wall → +90°; left wall → -90°.
        targetAngle = isFacingRight ? 90f : -90f;

        // After rotation the chassis-down ray length determines standoff distance.
        if (isFacingRight)
            lockedSurfaceX = hit.collider.bounds.min.x - groundCheckDistance;
        else
            lockedSurfaceX = hit.collider.bounds.max.x + groundCheckDistance;

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale   = 0f;
    }

    // ── Rotating to wall ──────────────────────────────────────────────────────

    void UpdateRotating()
    {
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next    = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);

        transform.eulerAngles = new Vector3(0f, 0f, next);
        transform.position    = new Vector3(lockedSurfaceX, transform.position.y, 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = new Vector3(0f, 0f, targetAngle);
            state = State.WallCrawl;
        }
    }

    // ── Wall crawl ────────────────────────────────────────────────────────────

    void UpdateWallCrawl()
    {
        // Chassis-down ray now points into the wall — it is the surface detector.
        bool onWall = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        float h        = Input.GetAxisRaw("Horizontal");
        float climbDir = isFacingRight ? h : -h;
        rb.linearVelocity = new Vector2(0f, climbDir * wallClimbSpeed);
        transform.position = new Vector3(lockedSurfaceX, transform.position.y, 0f);

        if (!onWall)
        {
            rb.gravityScale = 1f;
            state = State.Ground;
        }
    }

    // ── Debug rays ────────────────────────────────────────────────────────────

    void DrawRays()
    {
        Vector2 downDir    = -transform.up;
        Vector2 forwardDir = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;

        // Chassis-perpendicular ray: ground detector on ground, wall detector on wall
        LayerMask surfaceMask = state == State.WallCrawl ? wallMask : groundMask;
        bool surfaceHit = Physics2D.Raycast(transform.position, downDir, groundCheckDistance, surfaceMask);
        Debug.DrawRay(transform.position, downDir * groundCheckDistance, surfaceHit ? Color.green : Color.red);

        // Chassis-parallel ray: wall detector
        bool wallDetected = Physics2D.Raycast(transform.position, forwardDir, wallDetectorDistance, wallMask);
        Debug.DrawRay(transform.position, forwardDir * wallDetectorDistance, wallDetected ? Color.magenta : Color.yellow);
    }

    static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}
