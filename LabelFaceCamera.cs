using UnityEngine;

public class LabelFaceCamera : MonoBehaviour
{
    private Camera mainCamera;
    
    void Start()
    {
        // Find the main camera
        mainCamera = Camera.main;
        
        if (mainCamera == null)
        {
            Debug.LogWarning("Main camera not found for LabelFaceCamera component!");
        }
    }
    
    void Update()
    {
        if (mainCamera != null)
        {
            // Make the label face the camera directly
            transform.rotation = Quaternion.LookRotation(
                transform.position - mainCamera.transform.position
            );
        }
    }
}