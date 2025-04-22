using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class AgentUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject uiContainer;
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro statusText;
    [SerializeField] private GameObject speechBubble;
    [SerializeField] private TextMeshPro speechText;
    
    [Header("UI Settings")]
    [SerializeField] public string agentId = "Agent_Default";
    [SerializeField] private Color nameColor = Color.white;
    [SerializeField] private Color statusColor = Color.cyan;
    [SerializeField] private Color speechColor = Color.white;
    [SerializeField] private float speechDuration = 5.0f;
    [SerializeField] private float maxSpeechLength = 100;
    
    [Header("UI Positioning")]
    [SerializeField] public Vector3 uiOffset = new Vector3(0, 10.0f, 0); // Increased Y height to position name above the model
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private bool fadeWithDistance = true;
    [SerializeField] private float maxVisibleDistance = 50f;
    [SerializeField] private float minAlpha = 0.2f;
    
    [Header("Animation")]
    [SerializeField] private bool animateText = true;
    [SerializeField] private float textAnimationSpeed = 30f; // chars per second
    [SerializeField] private bool useTypewriterEffect = true;
    [SerializeField] private float bobAmplitude = 0.05f;
    [SerializeField] private float bobSpeed = 1.0f;
    
    // Internal state
    private Camera mainCamera;
    private Coroutine currentSpeechCoroutine;
    private Coroutine statusAnimationCoroutine;
    private Vector3 originalUIPosition;
    private float originalUIScanning = 0f;
    private Dictionary<string, Color> statusColors = new Dictionary<string, Color>();
    
    void Awake()
    {
        // Initialize UI elements if not set
        if (uiContainer == null)
        {
            uiContainer = new GameObject("UI_Container");
            uiContainer.transform.SetParent(transform);
            uiContainer.transform.localPosition = uiOffset;
        }
        else
        {
            // Force update position for existing containers (prefab instances)
            uiContainer.transform.localPosition = uiOffset;
        }
        
        if (nameText == null)
        {
            GameObject nameObj = new GameObject("NameText");
            nameObj.transform.SetParent(uiContainer.transform);
            nameObj.transform.localPosition = Vector3.zero;
            nameText = nameObj.AddComponent<TextMeshPro>();
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.fontSize = 4;
        }
        
        if (statusText == null)
        {
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(uiContainer.transform);
            statusObj.transform.localPosition = new Vector3(0, -0.5f, 0);
            statusText = statusObj.AddComponent<TextMeshPro>();
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.fontSize = 3;
        }
        
        if (speechBubble == null)
        {
            speechBubble = new GameObject("SpeechBubble");
            speechBubble.transform.SetParent(uiContainer.transform);
            speechBubble.transform.localPosition = new Vector3(0, 1f, 0);
            
            // Add speech bubble background
            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.transform.SetParent(speechBubble.transform);
            background.transform.localPosition = Vector3.zero;
            background.transform.localScale = new Vector3(2f, 1f, 1f);
            
            Material bubbleMat = new Material(Shader.Find("Standard"));
            bubbleMat.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            background.GetComponent<Renderer>().material = bubbleMat;
            
            // Add speech text
            GameObject speechObj = new GameObject("SpeechText");
            speechObj.transform.SetParent(speechBubble.transform);
            speechObj.transform.localPosition = new Vector3(0, 0, -0.01f);
            speechText = speechObj.AddComponent<TextMeshPro>();
            speechText.alignment = TextAlignmentOptions.Center;
            speechText.fontSize = 3.5f;
            speechText.color = speechColor;
            speechText.margin = new Vector4(0.2f, 0.2f, 0.2f, 0.2f);
            speechText.enableWordWrapping = true;
        }
        
        // Initialize status colors
        statusColors["Idle"] = Color.gray;
        statusColors["Moving"] = Color.green;
        statusColors["Conversing"] = Color.yellow;
        statusColors["Interacting"] = Color.cyan;
        statusColors["Error"] = Color.red;
        
        // Hide speech bubble initially
        if (speechBubble != null)
        {
            speechBubble.SetActive(false);
        }
        
        // Record original position for animation
        originalUIPosition = uiContainer.transform.localPosition;
    }
    
    void Start()
    {
        mainCamera = Camera.main;
        
        // Set initial texts
        SetNameText(agentId);
        UpdateStatus("Idle");
        
        // Apply UI offset (for both new and existing agents)
        UpdateUIPosition();
    }
    
    /// <summary>
    /// Updates the UI container position based on the configured offset
    /// </summary>
    public void UpdateUIPosition()
    {
        if (uiContainer != null)
        {
            uiContainer.transform.localPosition = uiOffset;
            originalUIPosition = uiContainer.transform.localPosition;
            Debug.Log($"Updated UI position for {agentId} to {uiOffset}");
        }
    }
    
    // Allow changing the UI height at runtime
    public void SetUIHeight(float height)
    {
        uiOffset = new Vector3(uiOffset.x, height, uiOffset.z);
        UpdateUIPosition();
    }
    
    void Update()
    {
        if (faceCamera && mainCamera != null)
        {
            // Make UI face the camera
            uiContainer.transform.rotation = Quaternion.LookRotation(
                uiContainer.transform.position - mainCamera.transform.position
            );
        }
        
        if (fadeWithDistance && mainCamera != null)
        {
            // Calculate distance to camera
            float distance = Vector3.Distance(transform.position, mainCamera.transform.position);
            
            // Calculate alpha based on distance
            float alpha = Mathf.Lerp(1f, minAlpha, Mathf.Clamp01(distance / maxVisibleDistance));
            
            // Apply alpha to all text components
            if (nameText != null)
            {
                Color color = nameText.color;
                color.a = alpha;
                nameText.color = color;
            }
            
            if (statusText != null)
            {
                Color color = statusText.color;
                color.a = alpha;
                statusText.color = color;
            }
            
            if (speechText != null && speechBubble.activeSelf)
            {
                Color color = speechText.color;
                color.a = alpha;
                speechText.color = color;
                
                // Also update speech bubble background
                var background = speechBubble.transform.GetChild(0).GetComponent<Renderer>();
                if (background != null)
                {
                    Color bgColor = background.material.color;
                    bgColor.a = alpha * 0.7f;
                    background.material.color = bgColor;
                }
            }
        }
        
        // Animate UI bobbing
        if (animateText && bobAmplitude > 0)
        {
            float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            uiContainer.transform.localPosition = originalUIPosition + new Vector3(0, bobOffset, 0);
        }
    }
    
    public void SetNameText(string name)
    {
        if (nameText != null)
        {
            nameText.text = name;
            nameText.color = nameColor;
        }
        
        agentId = name;
    }
    
    public void UpdateStatus(string status)
    {
        if (statusText != null)
        {
            // Cancel any ongoing status animation
            if (statusAnimationCoroutine != null)
            {
                StopCoroutine(statusAnimationCoroutine);
            }
            
            // Set text immediately
            statusText.text = status;
            
            // Set color based on status type
            if (statusColors.TryGetValue(status.Split(' ')[0], out Color statusTypeColor))
            {
                statusText.color = statusTypeColor;
            }
            else
            {
                statusText.color = statusColor;
            }
            
            // Animate the status change if enabled
            if (animateText)
            {
                statusAnimationCoroutine = StartCoroutine(AnimateStatusChange(status));
            }
        }
    }
    
    public void DisplaySpeech(string message)
    {
        if (speechText == null || speechBubble == null)
            return;
        
        // Cancel any ongoing speech
        if (currentSpeechCoroutine != null)
        {
            StopCoroutine(currentSpeechCoroutine);
        }
        
        // Truncate very long messages
        if (message.Length > maxSpeechLength)
        {
            message = message.Substring(0, (int)maxSpeechLength) + "...";
        }
        
        currentSpeechCoroutine = StartCoroutine(DisplaySpeechCoroutine(message));
    }
    
    private IEnumerator DisplaySpeechCoroutine(string message)
    {
        speechBubble.SetActive(true);
        
        if (useTypewriterEffect)
        {
            // Type out the text character by character
            speechText.text = "";
            for (int i = 0; i < message.Length; i++)
            {
                speechText.text += message[i];
                yield return new WaitForSeconds(1f / textAnimationSpeed);
            }
        }
        else
        {
            // Show the entire text at once
            speechText.text = message;
        }
        
        // Wait for the speech duration
        yield return new WaitForSeconds(speechDuration);
        
        // Hide the speech bubble
        speechBubble.SetActive(false);
        currentSpeechCoroutine = null;
    }
    
    private IEnumerator AnimateStatusChange(string status)
    {
        // Flash status text briefly
        Color originalColor = statusText.color;
        Color flashColor = Color.white;
        
        float duration = 0.3f;
        float elapsedTime = 0f;
        
        while (elapsedTime < duration)
        {
            float t = elapsedTime / duration;
            statusText.color = Color.Lerp(flashColor, originalColor, t);
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        statusText.color = originalColor;
        statusAnimationCoroutine = null;
    }
}