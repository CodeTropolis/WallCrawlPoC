using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float jumpVelocity = 12f;
    public float wallClimbSpeed = 5f;
    public float rotationSpeed = 270f;

    [Header("Wall Detection")]
    public Transform frontWheel;
    public float wallDetectDistance = 1.9f;
    public LayerMask wallMask;

    [Header("Ground Detection")]
    public float groundCheckDistance = 0.6f;
    public LayerMask groundMask;

    private Rigidbody2D rb;
    private float wheelRadius = 0.5f;
    private bool isGrounded;
    private bool isWallCrawling;
    private bool isExitingWall;
    private bool isFacingRight = true;
    private float targetAngle = 0f;
    private float lockedX;
    private float wallTopY;             // world-Y of the wall's top edge
    private float wallSurfaceX;         // world-X of the wall's face (used as fold pivot)

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;

        if (frontWheel != null)
        {
            var col = frontWheel.GetComponent<CircleCollider2D>();
            if (col != null) wheelRadius = col.radius;
        }
    }

    void Update()
    {
        CheckGround();

        if      (isWallCrawling) CheckWallTop();
        else if (!isExitingWall) DetectWall();

        HandleMovement();
        SmoothRotate();
        CheckExitRotationDone();
    }

    // ── Ground ────────────────────────────────────────────────────────────────

    void CheckGround()
    {
        Vector2 origin = frontWheel != null ? (Vector2)frontWheel.position : (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundCheckDistance, groundMask);
        isGrounded = hit.collider != null;
        Debug.DrawRay(origin, Vector2.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
    }

    // ── Wall detection (ground → wall) ────────────────────────────────────────

    void DetectWall()
    {
        if (!isGrounded || frontWheel == null) return;

        Vector2 origin = frontWheel.position;
        Vector2 dir    = isFacingRight ? Vector2.right : Vector2.left;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, wallDetectDistance, wallMask);
        Debug.DrawRay(origin, dir * wallDetectDistance, hit ? Color.yellow : Color.cyan);

        if (hit.collider != null)
            EnterWallCrawl(hit);
    }

    void EnterWallCrawl(RaycastHit2D wallHit)
    {
        Debug.Log("Wall detected! Entering wall crawl.");
        isWallCrawling = true;
        targetAngle    = isFacingRight ? 90f : -90f;

        // Store wall geometry once — used for top detection and fold pivot
        wallTopY     = wallHit.collider.bounds.max.y;
        wallSurfaceX = isFacingRight
            ? wallHit.collider.bounds.min.x   // left face of wall
            : wallHit.collider.bounds.max.x;  // right face of wall

        float wheelReach = Mathf.Abs(frontWheel.localPosition.y) + wheelRadius;
        lockedX = isFacingRight
            ? wallHit.point.x - wheelReach
            : wallHit.point.x + wheelReach;

        transform.position = new Vector3(lockedX, transform.position.y, 0f);
        rb.gravityScale    = 0f;
        rb.linearVelocity  = Vector2.zero;
    }

    // ── Wall-top detection (wall → top) ───────────────────────────────────────

    void CheckWallTop()
    {
        if (frontWheel == null) return;

        // Wait until the mount rotation has settled before checking
        float angleError = Mathf.Abs(Mathf.DeltaAngle(NormalizeAngle(transform.eulerAngles.z), targetAngle));
        if (angleError > 2f) return;

        // The front wheel is the topmost point while climbing.
        // Once its centre clears the wall's top edge the vehicle has crested.
        Debug.DrawRay(frontWheel.position, Vector2.up * 0.3f, Color.magenta);
        if (frontWheel.position.y >= wallTopY)
            ExitWallCrawl();
    }

    void ExitWallCrawl()
    {
        Debug.Log("Cleared wall top — rotating flat.");
        isWallCrawling = false;
        isExitingWall  = true;
        targetAngle    = 0f;
    }

    void CheckExitRotationDone()
    {
        if (!isExitingWall) return;

        float angleError = Mathf.Abs(Mathf.DeltaAngle(NormalizeAngle(transform.eulerAngles.z), 0f));
        if (angleError < 2f)
        {
            isExitingWall   = false;
            rb.gravityScale = 1f;
        }
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    void HandleMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");

        if (isWallCrawling)
        {
            float angleError = Mathf.Abs(Mathf.DeltaAngle(NormalizeAngle(transform.eulerAngles.z), targetAngle));
            if (angleError < 2f)
            {
                float climbDir    = isFacingRight ? h : -h;
                rb.linearVelocity = new Vector2(0f, climbDir * wallClimbSpeed);
                transform.position = new Vector3(lockedX, transform.position.y, 0f);
            }
        }
        else if (!isExitingWall)
        {
            rb.linearVelocity = new Vector2(h * moveSpeed, rb.linearVelocity.y);

            if      (h >  0.01f) isFacingRight = true;
            else if (h < -0.01f) isFacingRight = false;

            if (Input.GetButtonDown("Jump") && isGrounded)
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);
        }
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    void SmoothRotate()
    {
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next    = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);

        if (isExitingWall)
        {
            // Rotate around the wall's top corner (where the face meets the top surface).
            // This makes the chassis fold over the edge rather than spin about its centre,
            // and the maths work out so both wheels land flush on the platform top.
            float delta = Mathf.DeltaAngle(current, next);
            var   pivot = new Vector3(wallSurfaceX, wallTopY, 0f);
            transform.RotateAround(pivot, Vector3.forward, delta);
        }
        else
        {
            transform.eulerAngles = new Vector3(0f, 0f, next);
        }
    }

    static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }

    // ── Editor gizmo ──────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (frontWheel == null) return;
        Gizmos.color = Color.yellow;
        Vector2 dir = Application.isPlaying
            ? (isFacingRight ? Vector2.right : Vector2.left)
            : Vector2.right;
        Gizmos.DrawRay(frontWheel.position, dir * wallDetectDistance);
    }
}
