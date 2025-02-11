using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public float walkSpeed = 5f;
    public float sprintSpeed = 10f;
    public float mouseSensitivity = 3f;
    public float jumpSpeed = 10f;
    public float gravity = 20f;

    private CharacterController controller;
    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Mouse look
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(0, mouseX, 0);
        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.localEulerAngles = new Vector3(rotationX, 0, 0);
        }

        // Movement
        if (controller.isGrounded)
        {
            float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;

            float moveForward = Input.GetAxisRaw("Vertical") * speed;
            float moveSide = Input.GetAxisRaw("Horizontal") * speed;

            moveDirection = new Vector3(moveSide, 0, moveForward);
            moveDirection = transform.TransformDirection(moveDirection);

            if (Input.GetButtonDown("Jump"))
            {
                moveDirection.y = jumpSpeed;
            }
        }

        // Apply gravity
        moveDirection.y -= gravity * Time.deltaTime;
        
        // Move the character
        controller.Move(moveDirection * Time.deltaTime);
    }
}
