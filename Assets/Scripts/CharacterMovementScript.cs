using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMovementScript : MonoBehaviour
{
    // physics
    [Range(0.0f, 50.0f)]
    public float GravityAcceleration = 9.8f;
    public float YTerminalVelocity = 54.0f;
    public float XTerminalVelocity = 54.0f;
    public float MaxMovement = 10.0f;
    public float MaxAirMovement = 5.0f;
    public float JumpVelocity = 2.5f;
    [Range(0.0f, 50.0f)]
    public float Friction = 30.0f;
    public float AirFriction = 15.0f;
    public float Acceleration = 30.0f;
    public float AirAcceleration = 15.0f;
    public float SlopeAcceleration = 10.0f;
    public float GroundCheckDistance = 1.0f;
    public float MaxWalkableSlopeAngle = 45.0f;
    public float SlowSlope = 5.0f;

    // controls
    public KeyCode Jump = KeyCode.Space;
    public KeyCode Left = KeyCode.A;
    public KeyCode Right = KeyCode.D;
    private (bool Jump, bool Left, bool Right) _keyPressTuple = (false, false, false);
    private (bool Jump, bool Left, bool Right) _keyActionTuple = (false, false, false);
    public int JumpFrameForgiveness = 3;
    [Range(0.0f, 1.0f)]
    public float JumpReleaseMultiplyer = 0.5f;
    private bool _hasJumped = false;
    private int _elapsedFallingFrames = 0;
    private int _framesSinceJumpPressed = 0;
    

    // internal physics variables
    Vector2 _velocityVector = new Vector2(5.0f, 10.0f);
    private string _movementState = "Falling";
    private Vector2 _groundSlope;
    private Vector2 _oldGroundSlope = Vector2.down;
    private bool _hadCollision;

    // masks
    private int _platformMask;

    // Components
    private CapsuleCollider2D _collider;
    private Rigidbody2D _rigidBody;
    
    // Start is called before the first frame update
    void Start()
    {
        _collider = GetComponent<CapsuleCollider2D>();
        _rigidBody = GetComponent<Rigidbody2D>();
        _platformMask = LayerMask.GetMask("Platforms");
    }

    // Update is called once per frame
    void Update()
    {
        // these values are updated every frame update to figure out whether or not a key is being pressed
        _keyPressTuple.Jump = (_keyPressTuple.Jump && !Input.GetKeyUp(Jump)) || Input.GetKeyDown(Jump);
        _keyPressTuple.Left = (_keyPressTuple.Left && !Input.GetKeyUp(Left)) || Input.GetKeyDown(Left);
        _keyPressTuple.Right = (_keyPressTuple.Right && !Input.GetKeyUp(Right)) || Input.GetKeyDown(Right);
        
        // _keyActionTuple holds the key inputs that need to be resolved. letting go of a key isn't enough to set its "pressed value" to false, it must be resolved in a fixedUpdate frame.
        _keyActionTuple.Jump = _keyPressTuple.Jump || _keyPressTuple.Jump;
        _keyActionTuple.Left = _keyActionTuple.Left || _keyPressTuple.Left;
        _keyActionTuple.Right = _keyActionTuple.Right || _keyPressTuple.Right;
    }

    // States so far:
    // Idle: the character is standing still, not pressing any movement keys. If the character has velocity, the character will slide to a halt.
    // Walking: the character is travelling along walkable ground from influence of the movement keys.
    // Falling: the character is in freefall, at the whims of gravity.
    // Sliding: the character is sliding down a slope too steep to gain stable footing on.

    void FixedUpdate()
    {
        // decide what movement state the character needs to be in
        StateTransition();

        _hadCollision = false;
        // Debug.Log(_movementState);
        // check movement state and resolve accordingly
        // in general, each case will apply specific alterations to the x and y velocities depending on what movement state the character is in
        Vector2 velocityDirection = _velocityVector.normalized;
        Vector2 leftPointingGroundSlope = _groundSlope.x >= 0 ? _groundSlope : -_groundSlope;
        float xDirectionSign = Mathf.Sign(_velocityVector.x);
        switch (_movementState)
        {
            case "Idle":
                // find the reference angle from a vector that points down to the vector of the ground slope
                // remember, the _groundSlope vector always points downwards, so finding the angle between that and a vector that points straight down
                // will always return an angle that is less than 90 degrees
                float angle = Mathf.Deg2Rad * Vector2.Angle(Vector2.down, _groundSlope);
                // calculation that resembles kenetic friction; deceleration should be less if the slope is steeper
                float decelMagnitude = Mathf.Sin(angle) * Friction;
                // makes sure the magnitude doesn't cross over into the negatives when applying to x and y velocities
                if (_velocityVector.magnitude - (decelMagnitude * Time.deltaTime) > 0.0f)
                {
                    Vector2 decelVector = -decelMagnitude * velocityDirection;
                    _velocityVector.x += decelVector.x * Time.deltaTime;
                    _velocityVector.y += decelVector.y * Time.deltaTime;
                }
                else
                {
                    _velocityVector.x = 0.0f;
                    _velocityVector.y = 0.0f;
                }

                JumpControl();
                Move();
                break;
            case "Walking":
                // use the version of the slope that always points left to make movement calculations easier
                // the character should keep momentum gained outside of pressing left/right. only when he stops will the character lose that momentum
                
                // if going left, and isnt going left faster than speed limit
                if (_keyActionTuple.Left && !(xDirectionSign * _velocityVector.magnitude < -MaxMovement))
                {
                    Vector2 accelVector = -Acceleration * leftPointingGroundSlope;
                    _velocityVector.x += accelVector.x * Time.deltaTime;
                    _velocityVector.y += accelVector.y * Time.deltaTime;
                }
                // if going right, and isnt going right faster than speed limit
                else if (_keyActionTuple.Right && !(xDirectionSign * _velocityVector.magnitude > MaxMovement))
                {
                    Vector2 accelVector = Acceleration * leftPointingGroundSlope;
                    _velocityVector.x += accelVector.x * Time.deltaTime;
                    _velocityVector.y += accelVector.y * Time.deltaTime;
                }

                JumpControl();
                Move();
                break;
            case "Falling":
                // Apply gravity
                _velocityVector.y -= GravityAcceleration * Time.deltaTime;
                
                // for jumping, make this so that there is a period of time where the character is falling after being on a platform (saw this in a video once)
                _elapsedFallingFrames++;

                AirXControl();
                JumpControl(); 
                Move();
                break;
            case "Sliding":
                float slopeDirectionSign = Mathf.Sign(_groundSlope.x);
                float slowSlopeTargetVel = Mathf.Abs(SlowSlope * velocityDirection.y);

                // if the correct keys to slow the character arent being pressed or the character is traveling up the slope, gravity should continue pulling them down the slope
                if (!((slopeDirectionSign > 0 && _keyActionTuple.Left && _velocityVector.magnitude * xDirectionSign >= slowSlopeTargetVel) || 
                    (slopeDirectionSign < 0 && _keyActionTuple.Right && _velocityVector.magnitude * xDirectionSign <= -slowSlopeTargetVel)))
                {
                    // find the magnitude of sliding based on the steepness of the slope; steeper slopes mean faster sliding
                    float slidingAccelerationMagnitude = Mathf.Abs(GravityAcceleration * Mathf.Cos((Mathf.PI / 2) - Mathf.Asin(_groundSlope.y)));
                    // multiply the slope vector by the magnitude, and apply each component to x and y velocities
                    Vector2 slidingAccelerationVector = slidingAccelerationMagnitude * _groundSlope;
                    _velocityVector.x += slidingAccelerationVector.x * Time.deltaTime;
                    _velocityVector.y += slidingAccelerationVector.y * Time.deltaTime;
                }
                // else, slow the character if the correct keys are being pressed
                else if (_keyActionTuple.Left && !(slopeDirectionSign * _velocityVector.magnitude < slowSlopeTargetVel))
                {
                    Vector2 accelVector = -SlopeAcceleration * leftPointingGroundSlope;
                    _velocityVector.x += accelVector.x * Time.deltaTime;
                    _velocityVector.y += accelVector.y * Time.deltaTime;
                }
                else if (_keyActionTuple.Right && !(slopeDirectionSign * _velocityVector.magnitude > -slowSlopeTargetVel))
                {
                    Vector2 accelVector = SlopeAcceleration * leftPointingGroundSlope;
                    _velocityVector.x += accelVector.x * Time.deltaTime;
                    _velocityVector.y += accelVector.y * Time.deltaTime;
                }

                JumpControl();
                Move();
                break;
            default:
                Debug.LogError(string.Format("Invalid state {0} in {1}.CharacterMovementScript", _movementState, this.name));
                _movementState = "Idle";
                break;
        }

        // resolve action tuple
        _keyActionTuple = (false, false, false);
    }

    ///<summary>This is to control x velocity while the character is in the air. Jumping and Falling use this.</summary>
    private void AirXControl()
    {
        // Make it so that the character slows x vel in the air for better control
        // slow down if no sideways movement keys are being pressed
        if (!(_keyActionTuple.Left ^ _keyActionTuple.Right))
        {
            // this is stupid
            // _velocityVector.x = Mathf.Abs(_velocityVector.x) - AirFriction > 0.0f ? _velocityVector.x - Mathf.Sign(_velocityVector.x) * Friction * Time.deltaTime : 0.0f;
            if (Mathf.Abs(_velocityVector.x) - (AirFriction * Time.deltaTime) > 0.0f)
            {
                _velocityVector.x -= Mathf.Sign(_velocityVector.x) * AirFriction * Time.deltaTime;
            }
            else
            {
                _velocityVector.x = 0.0f;
            }
        }
        // else, character can move, albiet at a different rate while in mid-air
        else
        {
            // simpler in the air than on the ground, cos no need to worry about the y, that'll just be sorted out by gravity
            if (_keyActionTuple.Left && !(_velocityVector.x < -MaxAirMovement))
            {
                _velocityVector.x -= AirAcceleration * Time.deltaTime;
            }
            else if (_keyActionTuple.Right && !(_velocityVector.x > MaxAirMovement))
            {
                _velocityVector.x += AirAcceleration * Time.deltaTime;
            }
        }
    }

    private void JumpControl()
    {
        if (_keyActionTuple.Jump || _framesSinceJumpPressed <= JumpFrameForgiveness)
        {
            // first frame in jump
            if ((_movementState == "Falling" && _elapsedFallingFrames <= JumpFrameForgiveness && _velocityVector.y < 0) ||
                (_movementState == "Walking" || _movementState == "Idle"))
            {
                _velocityVector.y = JumpVelocity;
                _hasJumped = true;
            }
        }
        if (!_keyActionTuple.Jump)
        {
            Debug.Log("rekaea");
            if (_movementState == "Falling" && _hasJumped && _velocityVector.y > 0)
            {
                _velocityVector.y *= JumpReleaseMultiplyer;
            }
            _framesSinceJumpPressed++;
            _hasJumped = false;
        }
        
    }

    ///<summary>Transitions the character's movement state. Only changes <paramref name="_movementState"/></summary>
    private void StateTransition()
    {
        string groundCheck = CheckGround();
        Debug.Log(groundCheck);
        // Debug.Log(_movementState);
        switch (_movementState)
        {
            case "Idle":
                if (groundCheck == "Air")
                {
                    _movementState = "Falling";
                }
                else if (groundCheck == "SteepSlope")
                {
                    _movementState = "Sliding";
                }
                else if (_keyActionTuple.Left ^ _keyActionTuple.Right)
                {
                    _movementState = "Walking";
                }
                break;
            case "Walking":
                if (groundCheck == "Air")
                {
                    _movementState = "Falling";
                }
                else if (groundCheck == "SteepSlope")
                {
                    _movementState = "Sliding";
                }
                else if (!(_keyActionTuple.Left ^ _keyActionTuple.Right))
                {
                    _movementState = "Idle";
                }
                break;
            case "Falling":
                if (groundCheck == "Flat" || groundCheck == "ShallowSlope")
                {
                    _movementState = "Idle";
                }
                else if (groundCheck == "SteepSlope")
                {
                    _movementState = "Sliding";
                }
                break;
            case "Sliding":
                if (groundCheck == "Flat" || groundCheck == "ShallowSlope")
                {
                    _movementState = "Idle";
                }
                else if (groundCheck == "Air")
                {
                    _movementState = "Falling";
                }
                break;
            default:
                Debug.LogError(string.Format("Invalid state {0} in {1}.CharacterMovementScript", _movementState, this.name));
                break;
        }
    }

    /// <summary>Check the ground underneath the character and set _groundSlope</summary>
    /// <returns>String representing the type of ground the character is standing on/not standing on</returns>
    private string CheckGround()
    {
        string ground = "Air";
        // configuring the circlecast
        Vector2 capsulePosition = new Vector2(_rigidBody.position.x + _collider.offset.x, _rigidBody.position.y + _collider.offset.y);
        float castingDistance = _collider.bounds.extents.y - _collider.bounds.extents.x + 0.05f;
        RaycastHit2D floorHit = Physics2D.CircleCast(
            capsulePosition,
            _collider.bounds.extents.x,
            Vector2.down,
            castingDistance,
            _platformMask
        );

        // we want to see what type of ground this is if 1, we actually detect ground, and 2, if we aren't detecting ground before we actually collide with it
        if ((floorHit && _hadCollision) || (!(_movementState == "Falling") && floorHit))
        {
            // this is for other functions to do some calculations based on the slope of the ground
            _groundSlope = Vector2.Perpendicular(floorHit.normal);
            // Always have _groundSlope pointing down any given slope
            if (_groundSlope.y > 0)
            {
                _groundSlope *= -1;
            }
            // flat ground
            if (_groundSlope.y == 0)
            {
                ground = "Flat";
            }
            // walkable slope (BANDAID: added 5 degree buffer for when slope reading decides to act stupid)
            else if (Mathf.Abs(_groundSlope.y) <= (Mathf.Cos((MaxWalkableSlopeAngle - 5) * Mathf.Deg2Rad)))
            {
                ground = "ShallowSlope";
            }
            // slope too steep to walk
            else
            {
                ground = "SteepSlope";
            }
        }
        else
        {
            // removing might break something, keep an eye out!
            // _groundSlope = Vector2.down;
            ground = "Air";
        }
        return ground;
    }

    /// <summary>
    /// Moves the character and handles potential collisions, as well as updating the groundSlope
    /// </summary>
    private void Move()
    {
        // velocity on both axes cannot surpass terminal velocity
        _velocityVector.y = _velocityVector.y > YTerminalVelocity ? YTerminalVelocity : _velocityVector.y;
        _velocityVector.x = _velocityVector.x > XTerminalVelocity ? XTerminalVelocity : _velocityVector.x;
        Debug.Log(string.Format("x: {0}, y: {1}", _velocityVector.x, _velocityVector.y));

        // if there were no collisions and no need to stick to the ground, move as normal, dictated by the velocity vector
        if (! HandleCollisions())
        {
            if (! StickToGround(_rigidBody.position + (_velocityVector * Time.deltaTime)))
            {
                _rigidBody.position += (_velocityVector * Time.deltaTime);
                Debug.Log("Moved via velocities");
            }
        }
    }

    /// <summary>
    /// Detects any would-be collisions and moves the character as to not have collider overlap. 
    /// Also changes <paramref name="_velocityVector.x"/> and <paramref name="_velocityVector.y"/> based on the normal of the surface the character is colliding with
    /// </summary>
    /// <returns>
    /// True if there was a collision.
    /// </returns>
    /// Also
    private bool HandleCollisions()
    {
        bool temp = false;
        // configuring the capsule cast
        Vector2 colliderSize = _collider.bounds.extents * 2;
        Vector2 castingOrigin = new Vector2(
            transform.position.x + _collider.offset.x, 
            transform.position.y + _collider.offset.y
        );
        Vector2 castingDirection = _velocityVector.normalized;
        float castingDistance = _velocityVector.magnitude * Time.deltaTime;
        // cast a capsule in the direction of the velocity vector, and as far as the magnatude of the velocity vector times delta time
        // this will check to see if the character is going to hit something in the next fixedupdate, and output information about said collision, if it happened
        // we can use this information to determine how the character should react realistically
        RaycastHit2D hit = Physics2D.CapsuleCast(
            castingOrigin,
            colliderSize,
            _collider.direction,
            0.0f,
            castingDirection,
            castingDistance,
            _platformMask
        );

        // if the character would hit something in the next fixedupdate
        if (hit)
        {
            // this is for check ground, used later
            _hadCollision = true;

            Debug.DrawRay(hit.point, hit.normal, Color.yellow, 10.0f);
            // move character to be in contact with surface, but not to overlap with surface
            _rigidBody.position = new Vector2(
                hit.centroid.x, 
                hit.centroid.y
            );
            Debug.Log("Moved via collisions");

            // if character is flying through the air or sliding, he should behave as a normal physics object should.
            // otherwise, there are special rules we want to apply to the characters velocity.
            if (_movementState == "Falling" || _movementState == "Sliding")
            {
                NormalCollision(hit);
            }
            // this is for if the character is Idle, or if he is
            else
            {
                MaintainMomentum(hit);
            }

            temp = true;
        }
        return temp;
    }

    /// <summary>
    /// Handles a collision closer to how a regular physics engine would handle a collision 
    /// </summary>
    private void NormalCollision(RaycastHit2D hit)
    {
        float normalAngle = Vector2.SignedAngle(Vector2.right, hit.normal);
        // convert negative angle to positive reflex angle. this is to find what quadrant the normal is in.
        if (normalAngle < 0)
        {
            normalAngle += 360.0f;
        }
        // find quadrant and convert normalAngle to a refrence angle
        int quadrant = 1;
        while (normalAngle > 90.0f)
        {
            normalAngle -= 90.0f;
            quadrant++;
        }
        // depending on the quadrant, find the signed magnitude of the sum of the components of _velocityVector.x and _velocityVector.y that are parallel to the ground
        // this magnitude will be how fast the character is traveling along the surface after the collision 
        // the sign will determine whether the character moves along or opposite the normal vector turned 90 degrees counterclockwise
        float velocityMagnitude;
        normalAngle *= Mathf.Deg2Rad;
        switch (quadrant)
        {
            case 1:
                velocityMagnitude = -(_velocityVector.x * Mathf.Sin(normalAngle)) + (_velocityVector.y * Mathf.Sin((Mathf.PI / 2) - normalAngle));
                break;
            case 2:
                velocityMagnitude = -(_velocityVector.y * Mathf.Sin(normalAngle)) - (_velocityVector.x * Mathf.Sin((Mathf.PI / 2) - normalAngle));
                break;
            case 3:
                velocityMagnitude = (_velocityVector.x * Mathf.Sin(normalAngle)) - (_velocityVector.y * Mathf.Sin((Mathf.PI / 2) - normalAngle));
                break;
            case 4:
                velocityMagnitude = (_velocityVector.y * Mathf.Sin(normalAngle)) + (_velocityVector.x * Mathf.Sin((Mathf.PI / 2) - normalAngle));
                break;
            default:
                Debug.LogError(string.Format("Invalid quadrant {0}", quadrant));
                velocityMagnitude = 0.0f;
                break;
        }
        // we take this magnitude and we multiply it with the direction vector perpendicular to the normal vector to find the x and y velocities after the collision
        Vector2 newVelocityVector = velocityMagnitude * Vector2.Perpendicular(hit.normal);
        Debug.DrawLine(hit.point, hit.point + newVelocityVector, Color.green, 10.0f);
        _velocityVector.x = newVelocityVector.x;
        _velocityVector.y = newVelocityVector.y;
    }

    /// <summary>
    /// Handles a collision while walking, doesnt calculate loss in momentum due to change in slope
    /// </summary>
    // NOTE: this WILL work wonky if the character is going where the characters head hits the ceiling while he is walking
    private void MaintainMomentum(RaycastHit2D hit)
    {
        Vector2 surfaceSlope = Vector2.Perpendicular(hit.normal);
        // use the version of the slope that always points left to make movement calculations easier
        Vector2 leftPointingSurfaceDirection = surfaceSlope.x >= 0 ? surfaceSlope : -surfaceSlope;
        // take the sign of the x direction of the velocity
        float velDirectionFloat = Mathf.Sign(_velocityVector.x);
        // use this sign to determine the direction the character should be moving in
        float velocityMagnitude = _velocityVector.magnitude;
        Vector2 newVelocityVector = velocityMagnitude * leftPointingSurfaceDirection * velDirectionFloat;
        Debug.DrawLine(hit.point, hit.point + newVelocityVector, Color.green, 10.0f);
        _velocityVector.x = newVelocityVector.x;
        _velocityVector.y = newVelocityVector.y;
    }

    // NOTE: Strange bug when going up 45 degree slope, slope can read as steep slope (>45 degrees) for a single fixed update ???
    // BANDAID: im going to just, add a couple degrees to the max since im only working with a set number of different slope types (tilemap)
    /// <summary>
    /// Makes the character stick to the ground he is traveling along, adjusting velocity as needed, as well as moving the character to stick to the ground
    /// </summary>
    /// <returns>
    /// RaycastHit2D info related to the ground check with the raycast
    /// </returns>
    private bool StickToGround(Vector2 origin)
    {
        bool temp = false;
        
        // stick to the ground if the character is walking or idle (slowing down to stop)
        if (_movementState == "Walking" || _movementState == "Idle")
        {
            Vector2 newGroundSlope;
            // capsule cast to find where character should be when sticking to ground and what direction the character should be going in to avoid collisions
            // cast a capsule to find out where the character should be
            Vector2 castingOrigin = new Vector2(
                origin.x + _collider.offset.x, 
                origin.y + _collider.offset.y
            );
            Vector2 colliderSize = _collider.bounds.extents * 2;
            RaycastHit2D capsuleHit = Physics2D.CapsuleCast(
                castingOrigin,
                colliderSize,
                _collider.direction,
                0.0f,
                Vector2.down,
                GroundCheckDistance,
                _platformMask
            );
            Debug.DrawLine(castingOrigin, castingOrigin + ((GroundCheckDistance + _collider.bounds.extents.y) * Vector2.down), Color.red, 10.0f);

            // once checking for ground, we must check if its valid ground to be sticking to, or if we should continue with the previous velocity
            bool shouldStick = false;
            if (capsuleHit)
            {
                newGroundSlope = Vector2.Perpendicular(capsuleHit.normal);
                // Always have newGroundSlope pointing down any given slope
                if (newGroundSlope.y > 0)
                {
                    newGroundSlope *= -1;
                }

                // raycast downwards to find out if there is a slope directly below the character (or if its a ledge), and if that slope is one the character should be sticking to
                float castingDistance = GroundCheckDistance + _collider.bounds.extents.y;
                RaycastHit2D rayHit = Physics2D.Raycast(
                    castingOrigin,
                    Vector2.down,
                    castingDistance,
                    _platformMask
                );
                
                if (rayHit)
                {
                    // this checks if the slope underneath the character
                    Vector2 ledgeCheckSlope = Vector2.Perpendicular(rayHit.normal);
                    if (Mathf.Abs(ledgeCheckSlope.y) <= (Mathf.Cos(MaxWalkableSlopeAngle * Mathf.Deg2Rad)))
                    {
                        shouldStick = true;
                    }
                }
            }
            else
            {
                newGroundSlope = Vector2.down;
            }
            

            // we should only be doing this if we actually find some ground. if suddenly there is no ground, the character should just keep going
            if (shouldStick)
            {
                // check if the character is traveling along a slope that requires a ground stick
                float directionFloat = Mathf.Sign(_velocityVector.x);
                Vector2 leftPointingOldGroundSlope = _oldGroundSlope.x >= 0 ? _oldGroundSlope : -_oldGroundSlope;
                Vector2 leftPointingNewGroundSlope = newGroundSlope.x >= 0 ? newGroundSlope : -newGroundSlope;
                Debug.Log(string.Format("Old: {0}, New: {1}", leftPointingOldGroundSlope.y, leftPointingNewGroundSlope.y));
                if
                    (_velocityVector.x != 0 && 
                    ((directionFloat < 0 && leftPointingNewGroundSlope.y > leftPointingOldGroundSlope.y) || 
                    (directionFloat > 0 && leftPointingNewGroundSlope.y < leftPointingOldGroundSlope.y))
                )
                {
                    // this part is to teleport the player such that the player is now still in contact with the ground, rather than leaving it
                    
                    // then, use the info from the cast to teleport the player to the ground beneath them
                    if (capsuleHit)
                    {
                        _rigidBody.position = capsuleHit.centroid;
                        Debug.Log("Moved via ground stick");
                    }
                    else
                    {
                        Debug.LogError("Error: sticking to ground failed. After finding ground with raycast, could not find ground with capsule cast!");
                    }

                    // this part updates the velocity so that the character is traveling along the new ground
                    Debug.Log(string.Format("{0} * {1} * {2} = {3}", _velocityVector.magnitude, leftPointingNewGroundSlope, directionFloat, _velocityVector.magnitude * leftPointingNewGroundSlope * directionFloat));
                    _velocityVector = _velocityVector.magnitude * leftPointingNewGroundSlope * directionFloat;

                    temp = true;
                }
            }
            // update oldGroundSlope for possible next iteration
            _oldGroundSlope = newGroundSlope;
        }
        
        return temp;
    }
}