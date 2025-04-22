// ------------------------------------------ 
// GodModeFlyCamera.cs
// A god-like flying camera controller for Unity
// Based on BasicFPCC.cs by Alucard Jay Kay 
// ------------------------------------------ 

// This is a modified version of the original BasicFPCC first person controller
// that has been transformed into a flying camera with no physical constraints
// like gravity, jumping, or collisions.

// ** SETUP **
// Assign the GodModeFlyCamera object to its own Layer
// Assign the Layer Mask to ignore the GodModeFlyCamera object Layer
// Keep the CharacterController for simplicity
// Main Camera (as child) : Transform : Position => X 0, Y 0, Z 0
// No need for GFX as this is just a camera

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR // only required if using the Menu Item function at the end of this script
using UnityEditor; 
#endif

[RequireComponent(typeof(CharacterController))]
public class GodModeFlyCamera : MonoBehaviour
{
    [Header("Layer Mask")]
    [Tooltip("Layer Mask for sphere/raycasts. Assign the Player object to a Layer, then Ignore that layer here.")]
    public LayerMask castingMask;                              // Layer mask for casts. You'll want to ignore the player.

    // - Components -
    private CharacterController controller;                    // CharacterController component
    private Transform playerTx;                                // this player object

    [Header("Main Camera")]
    [Tooltip("Drag the FPC Camera here")]
    public Transform cameraTx;                                 // Main Camera, as child of BasicFPCC object

    [Header("Optional Player Graphic")]
    [Tooltip("optional capsule to visualize player in scene view")]
    public Transform playerGFX;                                // optional capsule graphic object
    
    [Header("Inputs")]
    [Tooltip("Disable if sending inputs from an external script")]
    public bool useLocalInputs = true;
    [Space(5)]
    public string axisLookHorzizontal = "Mouse X";             // Mouse to Look
    public string axisLookVertical    = "Mouse Y";             // 
    public string axisMoveHorzizontal = "Horizontal";          // WASD to Move
    public string axisMoveVertical    = "Vertical";            // 
    public KeyCode keyFlyUp           = KeyCode.Space;         // Space to fly up
    public KeyCode keyFlyDown         = KeyCode.LeftControl;   // Left Control to fly down
    public KeyCode keyFast            = KeyCode.LeftShift;     // Left Shift for fast movement
    public KeyCode keySlow            = KeyCode.LeftAlt;       // Left Alt for slow/precise movement
    public KeyCode keyToggleCursor    = KeyCode.BackQuote;     // ` to toggle lock cursor (aka [~] console key)

    // Input Variables that can be assigned externally
    // the cursor can also be manually locked or freed by calling the public void SetLockCursor( bool doLock )
    [HideInInspector] public float inputLookX        = 0;      //
    [HideInInspector] public float inputLookY        = 0;      //
    [HideInInspector] public float inputMoveX        = 0;      // range -1f to +1f
    [HideInInspector] public float inputMoveY        = 0;      // range -1f to +1f
    [HideInInspector] public bool inputKeyFlyUp      = false;  // is key Held
    [HideInInspector] public bool inputKeyFlyDown    = false;  // is key Held
    [HideInInspector] public bool inputKeyFast       = false;  // is key Held
    [HideInInspector] public bool inputKeySlow       = false;  // is key Held
    [HideInInspector] public bool inputKeyDownCursor = false;  // is key Pressed
    
    [Header("Look Settings")]
    public float mouseSensitivityX = 2f;             // speed factor of look X
    public float mouseSensitivityY = 2f;             // speed factor of look Y
    [Tooltip("larger values for less filtering, more responsiveness")]
    public float mouseSnappiness = 20f;              // default was 10f; larger values of this cause less filtering, more responsiveness
    public bool invertLookY = false;                 // toggle invert look Y
    public float clampLookY = 90f;                   // maximum look up/down angle
    
    [Header("Move Settings")]
    public float normalSpeed = 10f;                  // normal movement speed
    public float fastSpeed = 30f;                    // fast movement speed
    public float slowSpeed = 3f;                     // slow/precise movement speed
    public float verticalSpeed = 10f;                // vertical movement speed
    public float smoothFactor = 5f;                  // smoothing factor for movement

    [Header("Grounded Settings")]
    [Tooltip("The starting position of the isGrounded spherecast. Set to the sphereCastRadius plus the CC Skin Width. Enable showGizmos to visualize.")]
    // this should be just above the base of the cc, in the amount of the skin width (in case the cc sinks in)
    //public float startDistanceFromBottom = 0.2f;  
    public float groundCheckY = 0.33f;               // 0.25 + 0.08 (sphereCastRadius + CC skin width)
    [Tooltip("The position of the ceiling checksphere. Set to the height minus sphereCastRadius plus the CC Skin Width. Enable showGizmos to visualize.")]
    // this should extend above the cc (by approx skin width) so player can still move when not at full height (not crouching, trying to stand up), 
    // otherwise if it's below the top then the cc gets stuck
    public float ceilingCheckY = 1.83f;              // 2.00 - 0.25 + 0.08 (height - sphereCastRadius + CC skin width) 
    [Space(5)]
    public float sphereCastRadius = 0.25f;           // radius of area to detect for ground
    public float sphereCastDistance = 0.75f;         // How far spherecast moves down from origin point
    [Space(5)]
    public float raycastLength = 0.75f;              // secondary raycasts (match to sphereCastDistance)
    public Vector3 rayOriginOffset1 = new Vector3(-0.2f, 0f, 0.16f);
    public Vector3 rayOriginOffset2 = new Vector3(0.2f, 0f, -0.16f);

    [Header("Debug Gizmos")]
    [Tooltip("Show debug gizmos and lines")]
    public bool showGizmos = false;                  // Show debug gizmos and lines

    // - private reference variables -
    private float cameraStartY = 0;                  // reference camera position

    [Header("- reference variables -")]
    public float xRotation = 0f;                     // the up/down angle the player is looking
    private float currentSpeed = 0;                  // current movement speed
    private float accMouseX = 0;                     // reference for mouse look smoothing
    private float accMouseY = 0;                     // reference for mouse look smoothing
    private Vector3 lastPos = Vector3.zero;          // reference for velocity calculation
    private Vector3 moveDirection = Vector3.zero;    // current movement direction
    [Space(5)]
    public bool cursorActive = false;                // cursor state

    
    void Start()
    {
        Initialize();
    }

    void Update()
    {
        ProcessInputs();
        ProcessLook();
        ProcessMovement();
    }
    
    void Initialize()
    {
        if ( !cameraTx ) { Debug.LogError( "* " + gameObject.name + ": GodModeFlyCamera has NO CAMERA ASSIGNED in the Inspector *" ); }
        
        controller = GetComponent< CharacterController >();
        
        playerTx = transform;
        currentSpeed = normalSpeed;
        lastPos = playerTx.position;
        cameraStartY = cameraTx.localPosition.y;
        
        // Set a reasonable step offset (no more than the height of the character controller)
        controller.stepOffset = Mathf.Min(controller.height * 0.5f, 0.5f);
        
        // Make the collision detection minimal 
        controller.skinWidth = 0.01f;
        controller.minMoveDistance = 0;

        RefreshCursor();
    }

    void ProcessInputs()
    {
        if ( useLocalInputs )
        {
            inputLookX = Input.GetAxis( axisLookHorzizontal );
            inputLookY = Input.GetAxis( axisLookVertical );

            inputMoveX = Input.GetAxis( axisMoveHorzizontal );
            inputMoveY = Input.GetAxis( axisMoveVertical );

            inputKeyFlyUp     = Input.GetKey( keyFlyUp );
            inputKeyFlyDown   = Input.GetKey( keyFlyDown );
            inputKeyFast      = Input.GetKey( keyFast );
            inputKeySlow      = Input.GetKey( keySlow );
            
            inputKeyDownCursor = Input.GetKeyDown( keyToggleCursor );
        }

        if ( inputKeyDownCursor )
        {
            ToggleLockCursor();
        }
    }

    void ProcessLook()
    {
        accMouseX = Mathf.Lerp( accMouseX, inputLookX, mouseSnappiness * Time.deltaTime );
        accMouseY = Mathf.Lerp( accMouseY, inputLookY, mouseSnappiness * Time.deltaTime );

        float mouseX = accMouseX * mouseSensitivityX * 100f * Time.deltaTime;
        float mouseY = accMouseY * mouseSensitivityY * 100f * Time.deltaTime;

        // rotate camera X
        xRotation += ( invertLookY == true ? mouseY : -mouseY );
        xRotation = Mathf.Clamp( xRotation, -clampLookY, clampLookY );

        cameraTx.localRotation = Quaternion.Euler( xRotation, 0f, 0f );
        
        // rotate player Y
        playerTx.Rotate( Vector3.up * mouseX );
    }

    void ProcessMovement()
    {
        // Update reference for position history
        lastPos = playerTx.position;
        
        // Calculate target speed based on input
        float targetSpeed = normalSpeed;
        
        if (inputKeyFast)
            targetSpeed = fastSpeed;
        else if (inputKeySlow)
            targetSpeed = slowSpeed;
            
        // Smoothly transition to target speed
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, smoothFactor * Time.deltaTime);
        
        // Calculate horizontal movement direction
        Vector3 horizontalMove = (playerTx.right * inputMoveX) + (playerTx.forward * inputMoveY);
        
        // Normalize if exceeding magnitude of 1
        if (horizontalMove.magnitude > 1f)
            horizontalMove = horizontalMove.normalized;
            
        // Calculate vertical movement based on fly up/down inputs
        float verticalMove = 0f;
        if (inputKeyFlyUp)
            verticalMove += 1f;
        if (inputKeyFlyDown)
            verticalMove -= 1f;
            
        // Combine horizontal and vertical movement
        moveDirection = horizontalMove;
        moveDirection.y = verticalMove;
        
        // Apply movement
        Vector3 motion = moveDirection * currentSpeed * Time.deltaTime;
        controller.Move(motion);
        
        // Debug visualization
        #if UNITY_EDITOR
        if (showGizmos)
        {
            Debug.DrawRay(playerTx.position, moveDirection * 2f, Color.green);
        }
        #endif
    }

    // lock/hide or show/unlock cursor
    public void SetLockCursor( bool doLock )
    {
        cursorActive = doLock;
        RefreshCursor();
    }

    void ToggleLockCursor()
    {
        cursorActive = !cursorActive;
        RefreshCursor();
    }

    void RefreshCursor()
    {
        if ( !cursorActive && Cursor.lockState != CursorLockMode.Locked )	{ Cursor.lockState = CursorLockMode.Locked;	}
        if (  cursorActive && Cursor.lockState != CursorLockMode.None   )	{ Cursor.lockState = CursorLockMode.None;	}
    }
    
    // this script pushes all rigidbodies that the character touches
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;

        // no rigidbody
        if (body == null || body.isKinematic)
        {
            return;
        }

        // If you know how fast your character is trying to move,
        // then you can also multiply the push velocity by that.
        body.AddForce(hit.moveDirection * currentSpeed * 10f, ForceMode.Impulse);
    }

    // Debug Gizmos
    #if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (showGizmos)
        {
            // Draw direction of movement
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, moveDirection * 2f);
            
            // Draw forward direction
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, transform.forward * 2f);
        }
    }
    #endif
}


// =======================================================================================================================================

// ** DELETE from here down, if menu item and auto configuration is NOT Required **

// this section adds create BasicFPCC object to the menu : New -> GameObject -> 3D Object
// then configures the gameobject
// demo layer used : Ignore Raycast
// also finds the main camera, attaches and sets position
// and creates capsule gfx object (for visual while editing)

// A using clause must precede all other elements defined in the namespace except extern alias declarations
//#if UNITY_EDITOR
//using UnityEditor;
//#endif

public class GodModeFlyCamera_Setup : MonoBehaviour
{
    #if UNITY_EDITOR

    private static int cameraLayer = 2; // default to the Ignore Raycast Layer (to demonstrate configuration)

    [MenuItem("GameObject/3D Object/GodModeFlyCamera", false, 0)]
    public static void CreateGodModeFlyCamera() 
    {
        GameObject go = new GameObject("FlyCamera");

        CharacterController controller = go.AddComponent<CharacterController>();
        controller.center = new Vector3(0, 0, 0);
        controller.height = 0.1f; // Make it small so it doesn't interfere with anything
        controller.radius = 0.1f;
        controller.stepOffset = 999f; // Disable gravity
        controller.skinWidth = 0.01f;
        controller.minMoveDistance = 0;

        GodModeFlyCamera flyCamera = go.AddComponent<GodModeFlyCamera>();

        // Layer Mask
        go.layer = cameraLayer;
        flyCamera.castingMask = ~(1 << cameraLayer);
        Debug.LogWarning("GodModeFlyCamera set to layer: " + LayerMask.LayerToName(cameraLayer));

        // Main Camera
        GameObject mainCamObject = GameObject.Find("Main Camera");
        if (mainCamObject)
        {
            mainCamObject.transform.parent = go.transform;
            mainCamObject.transform.localPosition = Vector3.zero;
            mainCamObject.transform.localRotation = Quaternion.identity;

            flyCamera.cameraTx = mainCamObject.transform;
        }
        else // create example camera
        {
            GameObject camGo = new GameObject("FlyCamera View");
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            
            camGo.transform.parent = go.transform;
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;

            flyCamera.cameraTx = camGo.transform;
        }
    }
    #endif
}

// =======================================================================================================================================