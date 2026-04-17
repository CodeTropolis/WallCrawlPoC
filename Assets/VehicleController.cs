using UnityEngine;

public class VehicleController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float acceleration = 25f;   // units/s² — ramp up
    public float deceleration = 50f;   // units/s² — ramp down (snappier than accel)
    public float jumpVelocity = 8f;
    public float wallClimbSpeed = 5f;
    public float rotationSpeed = 180f;

    [Header("Ground Detection")]
    public float groundCheckDistance = 1.35f;
    public LayerMask groundMask;

    [Header("Wall Detection")]
    public float wallDetectorDistance = 2.5f;
    public LayerMask wallMask;

    [Header("Wall Hop")]
    public float wallHopDistance = 10f; // how far to scan for an opposing wall while crawling

    [Header("Wheel Positions")]
    public float frontWheelOffsetX = 0.8f; // horizontal dist from center to front wheel
    public float rearWheelOffsetX = 0.8f; // horizontal dist from center to rear wheel
    public float wheelOffsetY = 0.9f; // vertical dist down from center to wheel axles

    private Rigidbody2D rb;
    private SpriteRenderer chassisRenderer;
    private bool isGrounded;
    private bool isOnTopOfWall;
    private bool isFacingRight = true;
    private bool climbingRightWall; // which wall face we're on, independent of visual facing
    private float currentSpeed = 0f;
    private float wallJumpEntrySpeed = 0f; // pre-jump climb speed, restored on landing
    private bool wallHopAvailable = false; // true when an opposing wall is within wallHopDistance
    private bool wallUpIsRight = true;     // which key means "up": set on ground entry, preserved through hops
    private int wallEntryGrace = 0;        // frames to ignore !onWall after entering WallCrawl

    private enum State { Ground, RotatingToWall, WallCrawl, RotatingToGround, RotatingToTop, TopCrawl, WallJump }
    private State state = State.Ground;
    private float targetAngle = 0f;
    private float lockedSurfaceX = 0f;
    private float lockedSurfaceY = 0f;
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
            case State.Ground: UpdateGround(); break;
            case State.RotatingToWall: UpdateRotatingToWall(); break;
            case State.WallCrawl: UpdateWallCrawl(); break;
            case State.RotatingToGround: UpdateRotatingToGround(); break;
            case State.RotatingToTop: UpdateRotatingToTop(); break;
            case State.TopCrawl: UpdateTopCrawl(); break;
            case State.WallJump: UpdateWallJump(); break;
        }
        DrawRays();
    }

    // ── Ground ────────────────────────────────────────────────────────────────

    void UpdateGround()
    {
        // Hard-reset all wall-crawl state every frame while grounded.
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        currentSpeed = 0f;
        wallJumpEntrySpeed = 0f;
        wallHopAvailable = false;

        // Center rays for reliable grounded/wall-top detection.
        isGrounded = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, groundMask);
        isOnTopOfWall = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        // Per-wheel raycasts to compute the angle the vehicle should sit at.
        Vector2 fwd = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
        Vector2 back = -fwd;
        Vector2 frontWheelOrigin = rb.position + fwd * frontWheelOffsetX + (Vector2)(-transform.up) * wheelOffsetY;
        Vector2 rearWheelOrigin = rb.position + back * rearWheelOffsetX + (Vector2)(-transform.up) * wheelOffsetY;
        float castLen = groundCheckDistance * 2f;
        LayerMask combinedMask = groundMask | wallMask;
        RaycastHit2D frontHit = Physics2D.Raycast(frontWheelOrigin, Vector2.down, castLen, combinedMask);
        RaycastHit2D rearHit = Physics2D.Raycast(rearWheelOrigin, Vector2.down, castLen, combinedMask);

        if (frontHit.collider != null && rearHit.collider != null)
        {
            // Angle = slope between the two contact points.
            Vector2 diff = frontHit.point - rearHit.point;
            float targetZAngle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
            if (!isFacingRight) targetZAngle += 180f;
            RotateAroundRearWheel(targetZAngle);
        }
        else
        {
            // One or both wheels airborne — level out.
            RotateAroundRearWheel(0f);
        }

        float h = Input.GetAxisRaw("Horizontal");
        float targetVx = h * moveSpeed;
        float rate = Mathf.Abs(h) > 0.01f ? acceleration : deceleration;
        float newVx = Mathf.MoveTowards(rb.linearVelocity.x, targetVx, rate * Time.deltaTime);
        rb.linearVelocity = new Vector2(newVx, rb.linearVelocity.y);

        if (h > 0.01f && !isFacingRight) { isFacingRight = true; chassisRenderer.flipX = false; }
        else if (h < -0.01f && isFacingRight) { isFacingRight = false; chassisRenderer.flipX = true; }

        if (Input.GetButtonDown("Jump") && (isGrounded || isOnTopOfWall))
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);

        Vector2 wallCheckDir = isFacingRight ? Vector2.right : Vector2.left;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, wallCheckDir, wallDetectorDistance, wallMask);
        if (hit.collider != null)
        {
            wallUpIsRight = isFacingRight; // set once on ground entry; preserved through hops
            EnterWallRotation(hit);
        }
    }

    void EnterWallRotation(RaycastHit2D hit)
    {
        state = State.RotatingToWall;
        climbingRightWall = isFacingRight;
        targetAngle = climbingRightWall ? 90f : -90f;

        float wallStandoff = groundCheckDistance - 0.2f;
        if (climbingRightWall)
            lockedSurfaceX = hit.point.x - wallStandoff;
        else
            lockedSurfaceX = hit.point.x + wallStandoff;

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        currentSpeed = 0f;
    }

    // ── Rotating to wall ──────────────────────────────────────────────────────

    void UpdateRotatingToWall()
    {
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);

        transform.eulerAngles = new Vector3(0f, 0f, next);
        transform.position = new Vector3(lockedSurfaceX, transform.position.y, 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = new Vector3(0f, 0f, targetAngle);
            currentSpeed = wallJumpEntrySpeed;
            wallJumpEntrySpeed = 0f;
            wallEntryGrace = 4;
            state = State.WallCrawl;
        }
    }

    // ── Wall crawl ────────────────────────────────────────────────────────────

    void UpdateWallCrawl()
    {
        // Ensure the physics world reflects any transform changes made in the previous
        // Update (e.g. position/rotation set by UpdateRotatingToWall), otherwise the
        // very first onWall raycast sees stale physics state and drops back to Ground.
        Physics2D.SyncTransforms();

        bool onWall = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        // transform.up points into the corridor interior (world-left on right wall, world-right
        // on left wall), so this ray detects an opposing wall without any extra direction logic.
        wallHopAvailable = Physics2D.Raycast(transform.position, transform.up, wallHopDistance, wallMask).collider != null;

        float h = Input.GetAxisRaw("Horizontal");
        float climbDir = wallUpIsRight ? h : -h; // direction set at ground entry, preserved through hops

        // Facing: front faces up when climbing, down when descending.
        bool goingDown = climbDir < -0.01f;
        if (goingDown)
        {
            isFacingRight = !climbingRightWall;
            chassisRenderer.flipX = climbingRightWall;
        }
        else
        {
            isFacingRight = climbingRightWall;
            chassisRenderer.flipX = !climbingRightWall;
        }

        float targetSpeed = climbDir * wallClimbSpeed;
        float rate = Mathf.Abs(climbDir) > 0.01f ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

        rb.linearVelocity = Vector2.zero;
        transform.position = new Vector3(
            lockedSurfaceX,
            transform.position.y + currentSpeed * Time.deltaTime,
            0f
        );

        // When descending, check if the forward ray (now pointing down) hits the ground.
        if (goingDown)
        {
            Vector2 fwd = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;
            RaycastHit2D groundHit = Physics2D.Raycast(transform.position, fwd, wallDetectorDistance, groundMask);
            if (groundHit.collider != null)
            {
                EnterGroundRotation(groundHit);
                return;
            }
        }

        if (Input.GetButtonDown("Jump") && onWall)
        {
            if (wallHopAvailable)
                EnterWallJump();
            else
                EnterWallKickoff();
            return;
        }

        if (wallEntryGrace > 0)
        {
            wallEntryGrace--;
        }
        else if (!onWall)
        {
            if (climbDir > 0f)
                EnterTopRotation();
            else
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.gravityScale = 1f;
                state = State.Ground;
            }
        }
    }

    // ── Wall hop (flip to opposing wall) ─────────────────────────────────────

    void EnterWallJump()
    {
        bool wasOnRightWall = climbingRightWall;

        // Negate the wall rotation so wheels instantly face the opposing wall.
        float currentZ = NormalizeAngle(transform.eulerAngles.z);
        transform.eulerAngles = new Vector3(0f, 0f, -currentZ);

        climbingRightWall = !wasOnRightWall;
        isFacingRight = climbingRightWall;
        chassisRenderer.flipX = !climbingRightWall;

        float vx = wasOnRightWall ? -jumpVelocity : jumpVelocity;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f; // clean horizontal shot; UpdateWallJump lands it
        rb.linearVelocity = new Vector2(vx, 0f);

        wallJumpEntrySpeed = currentSpeed;
        currentSpeed = 0f;
        state = State.WallJump;
    }

    // ── Wall kickoff (no opposing wall — level out and fall) ──────────────────

    void EnterWallKickoff()
    {
        // Snap level immediately so Ground-state ground checks work correctly.
        transform.eulerAngles = Vector3.zero;

        // Face and launch away from the wall.
        isFacingRight = !climbingRightWall;
        chassisRenderer.flipX = climbingRightWall;

        float vx = climbingRightWall ? -jumpVelocity : jumpVelocity;
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 1f;
        rb.linearVelocity = new Vector2(vx, jumpVelocity * 0.5f);

        wallJumpEntrySpeed = 0f;
        currentSpeed = 0f;
        state = State.Ground;
    }

    void UpdateWallJump()
    {
        // Land on the opposite wall when close enough.
        Vector2 wallCheckDir = isFacingRight ? Vector2.right : Vector2.left;
        RaycastHit2D wallHit = Physics2D.Raycast(transform.position, wallCheckDir, wallDetectorDistance, wallMask);
        if (wallHit.collider != null)
        {
            EnterWallRotation(wallHit);
            return;
        }

        // Safety fallback: restore physics if somehow grounded without hitting a wall.
        bool onGround = Physics2D.Raycast(transform.position, Vector2.down, groundCheckDistance, groundMask);
        if (onGround)
        {
            rb.gravityScale = 1f;
            transform.eulerAngles = Vector3.zero;
            wallJumpEntrySpeed = 0f;
            state = State.Ground;
        }
    }

    // ── Rotating to ground ────────────────────────────────────────────────────

    void EnterGroundRotation(RaycastHit2D groundHit)
    {
        state = State.RotatingToGround;
        targetAngle = 0f;
        currentSpeed = 0f;

        // Lock Y to the ground surface top so we orbit the bottom corner cleanly.
        float standoff = groundCheckDistance - 0.2f;
        float groundY = groundHit.collider.bounds.max.y + standoff;
        lockedSurfaceY = groundY;

        // Pivot sits on the opposite side of the vehicle from the wall so the arc
        // sweeps away from the wall face, not into it.
        float cornerX = climbingRightWall
            ? lockedSurfaceX - standoff   // right wall: pivot is to the vehicle's left
            : lockedSurfaceX + standoff;  // left wall:  pivot is to the vehicle's right
        cornerPivot = new Vector2(cornerX, groundHit.collider.bounds.max.y);
    }

    void UpdateRotatingToGround()
    {
        float standoff = groundCheckDistance - 0.2f;
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, next);

        // Mirror of RotatingToTop: +sin sweeps the vehicle away from the wall face.
        float rad = next * Mathf.Deg2Rad;
        Vector2 offset = standoff * new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        transform.position = new Vector3(cornerPivot.x + offset.x, cornerPivot.y + offset.y, 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = Vector3.zero;
            // Face away from the wall so the ground-state wall-check ray doesn't
            // immediately re-detect the wall we just descended.
            isFacingRight = !climbingRightWall;
            chassisRenderer.flipX = climbingRightWall;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1f;
            state = State.Ground;
        }
    }

    // ── Rotating to top ───────────────────────────────────────────────────────

    void EnterTopRotation()
    {
        state = State.RotatingToTop;
        float standoff = groundCheckDistance - 0.2f;

        // Corner is the wall edge where the side face meets the top face.
        // The vehicle center is at lockedSurfaceX = wall_face ∓ standoff, so:
        //   right wall → corner.x = lockedSurfaceX + standoff = wall left face
        //   left wall  → corner.x = lockedSurfaceX - standoff = wall right face
        float cornerX = isFacingRight
            ? lockedSurfaceX + standoff
            : lockedSurfaceX - standoff;

        cornerPivot = new Vector2(cornerX, transform.position.y);
        targetAngle = 0f;
    }

    void UpdateRotatingToTop()
    {
        float standoff = groundCheckDistance - 0.2f;
        float current = NormalizeAngle(transform.eulerAngles.z);
        float next = Mathf.MoveTowardsAngle(current, targetAngle, rotationSpeed * Time.deltaTime);
        transform.eulerAngles = new Vector3(0f, 0f, next);

        // Orbit the vehicle center around the corner pivot so the chassis bottom
        // stays tangent to the corner. At angle θ, transform.up = (-sinθ, cosθ),
        // so center = corner + standoff * transform.up.
        float rad = next * Mathf.Deg2Rad;
        Vector2 offset = standoff * new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
        transform.position = new Vector3(cornerPivot.x + offset.x, cornerPivot.y + offset.y, 0f);

        if (Mathf.Abs(Mathf.DeltaAngle(next, targetAngle)) < 0.5f)
        {
            transform.eulerAngles = Vector3.zero;
            lockedSurfaceY = cornerPivot.y + standoff; // at θ=0, offset.y = standoff
            currentSpeed = 0f;
            state = State.TopCrawl;
        }
    }

    // ── Top crawl ─────────────────────────────────────────────────────────────

    void UpdateTopCrawl()
    {
        bool onSurface = Physics2D.Raycast(transform.position, -transform.up, groundCheckDistance, wallMask);

        float h = Input.GetAxisRaw("Horizontal");

        if (h > 0.01f && !isFacingRight) { isFacingRight = true; chassisRenderer.flipX = false; }
        else if (h < -0.01f && isFacingRight) { isFacingRight = false; chassisRenderer.flipX = true; }

        float targetSpeed = h * moveSpeed;
        float rate = Mathf.Abs(h) > 0.01f ? acceleration : deceleration;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

        rb.linearVelocity = Vector2.zero;
        transform.position = new Vector3(
            transform.position.x + currentSpeed * Time.deltaTime,
            lockedSurfaceY,
            0f
        );

        if (Input.GetButtonDown("Jump") && onSurface)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpVelocity);
            state = State.Ground;
            return;
        }

        if (!onSurface)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(currentSpeed, 0f);
            state = State.Ground;
        }
    }

    // ── Debug rays ────────────────────────────────────────────────────────────

    void DrawRays()
    {
        Vector2 downDir = -transform.up;
        Vector2 forwardDir = isFacingRight ? (Vector2)transform.right : -(Vector2)transform.right;

        bool onWallState = state == State.WallCrawl || state == State.RotatingToTop || state == State.TopCrawl;
        LayerMask surfaceMask = onWallState ? wallMask : groundMask;

        // Center ray — grounded/wall-top detection
        bool surfaceHit = Physics2D.Raycast(transform.position, downDir, groundCheckDistance, surfaceMask);
        Debug.DrawRay(transform.position, downDir * groundCheckDistance, surfaceHit ? Color.green : Color.red);

        // Per-wheel rays — slope angle detection
        Vector2 backDir = -forwardDir;
        Vector2 frontWheelOrigin = rb.position + forwardDir * frontWheelOffsetX + downDir * wheelOffsetY;
        Vector2 rearWheelOrigin = rb.position + backDir * rearWheelOffsetX + downDir * wheelOffsetY;
        bool frontHit = Physics2D.Raycast(frontWheelOrigin, downDir, groundCheckDistance, groundMask | wallMask);
        bool rearHit = Physics2D.Raycast(rearWheelOrigin, downDir, groundCheckDistance, groundMask | wallMask);
        Debug.DrawRay(frontWheelOrigin, downDir * groundCheckDistance, frontHit ? Color.cyan : Color.white);
        Debug.DrawRay(rearWheelOrigin, downDir * groundCheckDistance, rearHit ? Color.cyan : Color.white);

        bool wallDetected = Physics2D.Raycast(transform.position, forwardDir, wallDetectorDistance, wallMask);
        Debug.DrawRay(transform.position, forwardDir * wallDetectorDistance, wallDetected ? Color.magenta : Color.yellow);

        // Wall-hop sensor — only meaningful while crawling
        if (state == State.WallCrawl)
            Debug.DrawRay(transform.position, (Vector2)transform.up * wallHopDistance,
                wallHopAvailable ? Color.green : Color.grey);
    }

    void RotateAroundRearWheel(float targetZAngle)
    {
        float nextAngle = Mathf.MoveTowardsAngle(rb.rotation, targetZAngle, rotationSpeed * Time.deltaTime);
        if (Mathf.Approximately(nextAngle, rb.rotation)) return;

        // Pivot = rear wheel ground contact point (bottom of rear wheel) in local space.
        float rearSign = isFacingRight ? -1f : 1f;
        Vector2 localPivot = new Vector2(rearSign * rearWheelOffsetX, -wheelOffsetY);

        // World position of pivot before rotation — use rb.position, not transform.position.
        float oldRad = rb.rotation * Mathf.Deg2Rad;
        Vector2 pivotBefore = rb.position + RotateVector(localPivot, oldRad);

        // Apply new rotation.
        rb.rotation = nextAngle;

        // World position of pivot after rotation.
        float newRad = nextAngle * Mathf.Deg2Rad;
        Vector2 pivotAfter = rb.position + RotateVector(localPivot, newRad);

        // Shift vehicle so pivot stays planted.
        rb.position += pivotBefore - pivotAfter;
    }

    static Vector2 RotateVector(Vector2 v, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        return new Vector2(cos * v.x - sin * v.y, sin * v.x + cos * v.y);
    }

    static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}
