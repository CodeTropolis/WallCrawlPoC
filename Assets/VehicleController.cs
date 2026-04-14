using UnityEngine;

// Physics-based vehicle controller.
// The Rigidbody2D stays Dynamic at all times.
// Wall attachment uses gravityScale = 0 + position constraint rather than Kinematic.
// Corner transitions (RotatingToWall, RotatingToTop, RotatingToGround) drive position
// via rb.MovePosition so the physics body stays authoritative.

public class VehicleController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed     = 10f;
    public float acceleration  = 25f;
    public float deceleration  = 20f;
    public float jumpVelocity  = 7f;
    public float wallClimbSpeed = 10f;
    public float rotationSpeed = 300f;

    [Header("Ground Detection")]
    public float groundCheckDistance = 1.35f;
    public LayerMask groundMask;

    [Header("Wall Detection")]
    public float wallDetectorDistance = 2.5f;
    public LayerMask wallMask;

    [Header("Wheel Positions")]
    public float frontWheelOffsetX = 0.8f;
    public float rearWheelOffsetX  = 0.8f;
    public float wheelOffsetY      = 0.9f;

    private Rigidbody2D rb;
    private SpriteRenderer chassisRenderer;
    private bool isFacingRight = true;
    private bool climbingRightWall;
    private float currentSpeed = 0f;

    private enum State { Ground, RotatingToWall, WallCrawl, RotatingToGround, RotatingToTop, TopCrawl }
    private State state = State.Ground;
    private float targetAngle;
    private float lockedSurfaceX;
    private Vector2 cornerPivot;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        chassisRenderer = GetComponent<SpriteRenderer>();
        rb.freezeRotation = true;
    }

    void Update()
    {
        switch (state)
        {
            case State.Ground:           UpdateGround();          break;
            case State.RotatingToWall:   UpdateRotatingToWall();  break;
            case State.WallCrawl:        UpdateWallCrawl();       break;
            case State.RotatingToGround: UpdateRotatingToGround();break;
            case State.RotatingToTop:    UpdateRotatingToTop();   break;
            case State.TopCrawl:         UpdateTopCrawl();        break;
        }
        DrawDebugRays();
    }

    void FixedUpdate()
    {
        // During wall states, own velocity entirely in FixedUpdate so it runs
        // in sync with the physics solver each step.
        // X: correct any drift using velocity rather than teleporting the body.
        // Y: wall climb speed (zero during rotation).
        if (state == State.WallCrawl || state == State.RotatingToWall)
        {
            float xCorrection = (lockedSurfaceX - rb.position.x) / Time.fixedDeltaTime;
            float ySpeed      = state == State.WallCrawl ? currentSpeed : 0f;
            rb.linearVelocity = new Vector2(xCorrection, ySpeed);
        }
    }

    // ── Ground ─────────────────────────────────────────────────────────────────

    void UpdateGround()
    {
        bool isGrounded  = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, groundMask);
        bool isOnWallTop = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        // Per-wheel raycasts for slope-following rotation.
        Vector2 fwd         = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
        Vector2 frontOrigin = rb.position + fwd    * frontWheelOffsetX - (Vector2)transform.up * wheelOffsetY;
        Vector2 rearOrigin  = rb.position + (-fwd) * rearWheelOffsetX  - (Vector2)transform.up * wheelOffsetY;
        LayerMask combined  = groundMask | wallMask;
        float castLen       = groundCheckDistance * 2f;
        RaycastHit2D frontHit = Physics2D.Raycast(frontOrigin, Vector2.down, castLen, combined);
        RaycastHit2D rearHit  = Physics2D.Raycast(rearOrigin,  Vector2.down, castLen, combined);

        if (frontHit.collider != null && rearHit.collider != null)
        {
            Vector2 diff  = frontHit.point - rearHit.point;
            float angle   = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            if (!isFacingRight) angle += 180f;
            RotateAroundRearWheel(angle);
        }
        else
        {
            RotateAroundRearWheel(0f);
        }

        // Horizontal movement — physics carries vertical velocity naturally.
        float h       = Input.GetAxisRaw("Horizontal");
        float rate    = Mathf.Abs(h) > 0.01f ? acceleration : deceleration;
        float targetVx = h * moveSpeed;
        rb.linearVelocity = new Vector2(
            Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.deltaTime),
            rb.linearVelocity.y
        );

        if (h > 0.01f  && !isFacingRight) { isFacingRight = true;  chassisRenderer.flipX = false; }
        if (h < -0.01f &&  isFacingRight) { isFacingRight = false; chassisRenderer.flipX = true;  }

        if (Input.GetButtonDown("Jump") && (isGrounded || isOnWallTop))
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);

        // Wall detection — trigger rotation when wall is close ahead.
        Vector2 wallDir      = isFacingRight ? Vector2.right : Vector2.left;
        RaycastHit2D wallHit = Physics2D.Raycast(transform.position, wallDir, wallDetectorDistance, wallMask);
        if (wallHit.collider != null)
            EnterWallRotation(wallHit);
    }

    void EnterWallRotation(RaycastHit2D hit)
    {
        state             = State.RotatingToWall;
        climbingRightWall = isFacingRight;
        targetAngle       = climbingRightWall ? 90f : -90f;

        float standoff = groundCheckDistance - 0.2f;
        lockedSurfaceX = climbingRightWall
            ? hit.point.x - standoff
            : hit.point.x + standoff;

        rb.gravityScale   = 0f;
        rb.linearVelocity = Vector2.zero;
        currentSpeed      = 0f;
    }

    // ── Rotating to wall ───────────────────────────────────────────────────────

    void UpdateRotatingToWall()
    {
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next    = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, next);
        // X and Y velocity owned by FixedUpdate; nothing to set here.

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = new Vector3(0f, 0f, targetAngle);
            state = State.WallCrawl;
        }
    }

    // ── Wall crawl ─────────────────────────────────────────────────────────────

    void UpdateWallCrawl()
    {
        bool onWall = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        float h        = Input.GetAxisRaw("Horizontal");
        float climbDir = climbingRightWall ? h : -h;
        bool goingDown = climbDir < -0.01f;

        isFacingRight         = goingDown ? !climbingRightWall :  climbingRightWall;
        chassisRenderer.flipX = goingDown ?  climbingRightWall : !climbingRightWall;

        float rate   = Mathf.Abs(climbDir) > 0.01f ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, climbDir * wallClimbSpeed, rate * Time.deltaTime);
        // Velocity is applied in FixedUpdate to stay in sync with the physics solver.

        // While descending, watch for the ground coming up.
        if (goingDown)
        {
            Vector2 fwd          = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
            RaycastHit2D groundHit = Physics2D.Raycast(transform.position, fwd, wallDetectorDistance, groundMask);
            if (groundHit.collider != null)
            {
                EnterGroundRotation(groundHit);
                return;
            }
        }

        if (!onWall)
        {
            if (climbDir > 0f)
                EnterTopRotation();
            else
            {
                rb.gravityScale = 1f;
                state = State.Ground;
            }
        }
    }

    // ── Rotating to ground ─────────────────────────────────────────────────────

    void EnterGroundRotation(RaycastHit2D groundHit)
    {
        state        = State.RotatingToGround;
        targetAngle  = 0f;
        currentSpeed = 0f;
        rb.linearVelocity = Vector2.zero;

        float standoff = groundCheckDistance - 0.2f;
        float cornerX  = climbingRightWall
            ? lockedSurfaceX + standoff
            : lockedSurfaceX - standoff;
        cornerPivot = new Vector2(cornerX, groundHit.collider.bounds.max.y);
    }

    void UpdateRotatingToGround()
    {
        float standoff = groundCheckDistance - 0.2f;
        float current  = NormalizeAngle(transform.eulerAngles.z);
        float next     = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, next);

        float rad = next * Mathf.Deg2Rad;
        rb.MovePosition(cornerPivot + standoff * new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)));

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = Vector3.zero;
            isFacingRight         = climbingRightWall;
            chassisRenderer.flipX = !climbingRightWall;
            rb.gravityScale       = 1f;
            rb.linearVelocity     = Vector2.zero;
            state = State.Ground;
        }
    }

    // ── Rotating to top ────────────────────────────────────────────────────────

    void EnterTopRotation()
    {
        state = State.RotatingToTop;
        float standoff = groundCheckDistance - 0.2f;
        float cornerX  = isFacingRight
            ? lockedSurfaceX + standoff
            : lockedSurfaceX - standoff;
        cornerPivot       = new Vector2(cornerX, rb.position.y);
        targetAngle       = 0f;
        rb.linearVelocity = Vector2.zero;
    }

    void UpdateRotatingToTop()
    {
        float standoff = groundCheckDistance - 0.2f;
        float current  = NormalizeAngle(transform.eulerAngles.z);
        float next     = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, next);

        float rad = next * Mathf.Deg2Rad;
        rb.MovePosition(cornerPivot + standoff * new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad)));

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = Vector3.zero;
            rb.gravityScale       = 1f;
            rb.linearVelocity     = Vector2.zero;
            currentSpeed          = 0f;
            state = State.TopCrawl;
        }
    }

    // ── Top crawl ──────────────────────────────────────────────────────────────
    // Gravity is re-enabled here — physics keeps the vehicle on the wall top
    // without needing to lock the Y position.

    void UpdateTopCrawl()
    {
        bool onSurface = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        float h = Input.GetAxisRaw("Horizontal");
        if (h > 0.01f  && !isFacingRight) { isFacingRight = true;  chassisRenderer.flipX = false; }
        if (h < -0.01f &&  isFacingRight) { isFacingRight = false; chassisRenderer.flipX = true;  }

        float rate     = Mathf.Abs(h) > 0.01f ? acceleration : deceleration;
        float targetVx = h * moveSpeed;
        rb.linearVelocity = new Vector2(
            Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.deltaTime),
            rb.linearVelocity.y
        );

        if (Input.GetButtonDown("Jump") && onSurface)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);
            state = State.Ground;
            return;
        }

        if (!onSurface)
            state = State.Ground;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    void RotateAroundRearWheel(float targetZAngle)
    {
        float next = Mathf.MoveTowardsAngle(rb.rotation, targetZAngle, rotationSpeed * Time.deltaTime);
        if (Mathf.Approximately(next, rb.rotation)) return;

        float rearSign     = isFacingRight ? -1f : 1f;
        Vector2 localPivot = new Vector2(rearSign * rearWheelOffsetX, -wheelOffsetY);

        float oldRad        = rb.rotation * Mathf.Deg2Rad;
        Vector2 pivotBefore = rb.position + RotateVector(localPivot, oldRad);
        rb.rotation         = next;
        float newRad        = next * Mathf.Deg2Rad;
        Vector2 pivotAfter  = rb.position + RotateVector(localPivot, newRad);
        rb.position        += pivotBefore - pivotAfter;
    }

    void DrawDebugRays()
    {
        Vector2 down = -transform.up;
        Vector2 fwd  = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
        bool wallState = state == State.WallCrawl || state == State.RotatingToTop || state == State.TopCrawl;

        bool ch = Physics2D.Raycast(transform.position, down, groundCheckDistance, wallState ? wallMask : groundMask);
        Debug.DrawRay(transform.position, down * groundCheckDistance, ch ? Color.green : Color.red);

        Vector2 fo = rb.position + fwd    * frontWheelOffsetX + down * wheelOffsetY;
        Vector2 ro = rb.position + (-fwd) * rearWheelOffsetX  + down * wheelOffsetY;
        bool fh = Physics2D.Raycast(fo, down, groundCheckDistance, groundMask | wallMask);
        bool rh = Physics2D.Raycast(ro, down, groundCheckDistance, groundMask | wallMask);
        Debug.DrawRay(fo, down * groundCheckDistance, fh ? Color.cyan  : Color.white);
        Debug.DrawRay(ro, down * groundCheckDistance, rh ? Color.cyan  : Color.white);

        bool wh = Physics2D.Raycast(transform.position, fwd, wallDetectorDistance, wallMask);
        Debug.DrawRay(transform.position, fwd * wallDetectorDistance, wh ? Color.magenta : Color.yellow);
    }

    static Vector2 RotateVector(Vector2 v, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}
