using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class AgentUI : MonoBehaviour
{


    [Header("Dev Options")]
    [SerializeField] private bool autoCreateMissingUI = false;   // leave OFF in your prefab
    [SerializeField] private bool overrideFontSizes   = false;   // leave OFF in your prefab


    [Header("UI References")]
    [SerializeField] public GameObject uiContainer; // Made public for debugging
    [SerializeField] public TextMeshPro nameText; // Made public for debugging
    [SerializeField] public TextMeshPro statusText; // Made public for debugging
    [SerializeField] public GameObject speechBubble; // Made public for debugging
    [SerializeField] public TextMeshPro speechText; // Made public for debugging
    
    [Header("UI Settings")]
    [SerializeField] public string agentId = "Agent_Default";
    [SerializeField] private Color nameColor = Color.white;
    [SerializeField] private Color statusColor = Color.cyan;
    [SerializeField] private Color speechColor = Color.white;
    [SerializeField] private float speechDuration = 5.0f;
    [SerializeField] private float maxSpeechLength = 100;
    
    [Header("UI Positioning")]
    [SerializeField] public Vector3 uiOffset = new Vector3(0, 5.0f, 0); // Increased Y height to position name above the model
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
    
    [Header("Emoji Display")]
    [SerializeField] private bool useEmojis = false; // Defaults to OFF - use speech bubbles by default
    [SerializeField] private GameObject emojiContainer;
    [SerializeField] private EmojiDisplay emojiDisplay;
    
    // Internal state
    private Camera mainCamera;
    private Coroutine currentSpeechCoroutine;
    private Coroutine statusAnimationCoroutine;
    private Vector3 originalUIPosition;
    private float originalUIScanning = 0f;
    private Dictionary<string, Color> statusColors = new Dictionary<string, Color>();
    
    void Awake()
{
    // ---------- 1. FIND ALREADY-WIRED OBJECTS ----------
    if (uiContainer == null)
        uiContainer = transform.Find("UI_Container")?.gameObject;

    nameText    ??= uiContainer?.transform.Find("NameText")    ?.GetComponent<TextMeshPro>();
    statusText  ??= uiContainer?.transform.Find("StatusText")  ?.GetComponent<TextMeshPro>();
    speechBubble??= uiContainer?.transform.Find("SpeechBubble")?.gameObject;
    speechText  ??= speechBubble?.transform.Find("SpeechText") ?.GetComponent<TextMeshPro>();
    
    // Find emoji container and display component
    emojiContainer ??= uiContainer?.transform.Find("EmojiContainer")?.gameObject;
    emojiDisplay ??= emojiContainer?.GetComponent<EmojiDisplay>();
    
    // Initialize emoji display state
    if (emojiContainer != null)
    {
        emojiContainer.SetActive(useEmojis);
    }

    // ---------- 2. OPTIONALLY CREATE MISSING PARTS ----------
    if (autoCreateMissingUI)
    {
        if (uiContainer == null)
        {
            uiContainer = new GameObject("UI_Container");
            uiContainer.transform.SetParent(transform);
            uiContainer.transform.localPosition = uiOffset;
        }

        if (nameText == null)
        {
            var go = new GameObject("NameText");
            go.transform.SetParent(uiContainer.transform);
            nameText = go.AddComponent<TextMeshPro>();
            nameText.alignment = TextAlignmentOptions.Center;
        }

        if (statusText == null)
        {
            var go = new GameObject("StatusText");
            go.transform.SetParent(uiContainer.transform);
            statusText = go.AddComponent<TextMeshPro>();
            statusText.alignment = TextAlignmentOptions.Center;
        }

        if (speechBubble == null)
        {
            speechBubble = new GameObject("SpeechBubble");
            speechBubble.transform.SetParent(uiContainer.transform);

            // background quad
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.transform.SetParent(speechBubble.transform);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            bg.GetComponent<Renderer>().material = mat;

            // speech text
            var textGO = new GameObject("SpeechText");
            textGO.transform.SetParent(speechBubble.transform);
            speechText = textGO.AddComponent<TextMeshPro>();
            speechText.alignment = TextAlignmentOptions.Center;
        }

        // if we created the bubble above, it starts hidden
        speechBubble?.SetActive(false);
    }

    // ---------- 3. OPTIONAL FONT OVERRIDES ----------
    if (overrideFontSizes)
    {
        if (nameText   != null) nameText.fontSize   = 4f;
        if (statusText != null) statusText.fontSize = 3f;
        if (speechText != null) speechText.fontSize = 3.5f;
    }

    // record base position for bobbing
    if (uiContainer != null) originalUIPosition = uiContainer.transform.localPosition;
}

    
    void Start()
    {
        mainCamera = Camera.main;
        
        // Debug output to help diagnose UI issues
        Debug.Log($"[{agentId}] Start() - UI References:");
        Debug.Log($"  uiContainer: {(uiContainer != null ? "FOUND" : "NULL")}");
        Debug.Log($"  nameText: {(nameText != null ? "FOUND" : "NULL")}");
        Debug.Log($"  statusText: {(statusText != null ? "FOUND" : "NULL")}");
        Debug.Log($"  speechBubble: {(speechBubble != null ? "FOUND" : "NULL")}");
        Debug.Log($"  speechText: {(speechText != null ? "FOUND" : "NULL")}");
        
        // Set initial texts
        SetNameText(agentId);
        UpdateStatus("Idle");
        
        // Apply UI offset (for both new and existing agents)
        UpdateUIPosition();
        
        // Check for font sizes
        if (nameText != null) {
            Debug.Log($"  nameText fontSize: {nameText.fontSize}");
        }
        if (statusText != null) {
            Debug.Log($"  statusText fontSize: {statusText.fontSize}");
        }
        if (speechText != null) {
            Debug.Log($"  speechText fontSize: {speechText.fontSize}");
        }
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
    
    // Toggle between emoji mode and speech bubble mode
    public void ToggleEmojiMode(bool useEmojisMode)
    {
        useEmojis = useEmojisMode;
        
        // Update the visibility of containers
        if (emojiContainer != null)
            emojiContainer.SetActive(useEmojis);
            
        if (speechBubble != null)
            speechBubble.SetActive(!useEmojis && speechBubble.activeSelf);
            
        // Update the emoji to match current status if switching to emoji mode
        if (useEmojis && emojiDisplay != null)
        {
            emojiDisplay.SetEmojiForStatus(statusText?.text ?? "Idle");
        }
        
        Debug.Log($"[{agentId}] Emoji mode set to: {useEmojis}");
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
            // Make UI face the camera, but rotate 180 degrees to fix backwards text
            uiContainer.transform.rotation = Quaternion.LookRotation(
                uiContainer.transform.position - mainCamera.transform.position
            ) * Quaternion.Euler(0, 180, 0); // Add 180 degree Y rotation to fix flipped text
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
            
            if (speechText != null && speechBubble != null && speechBubble.activeSelf)
            {
                Color color = speechText.color;
                color.a = alpha;
                speechText.color = color;
                
                // Also update speech bubble background if it exists
                if (speechBubble.transform.childCount > 0)
                {
                    var background = speechBubble.transform.GetChild(0).GetComponent<Renderer>();
                    if (background != null)
                    {
                        Color bgColor = background.material.color;
                        bgColor.a = alpha * 0.7f;
                        background.material.color = bgColor;
                    }
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
            
            // Update emoji if emoji mode is enabled
            if (useEmojis && emojiDisplay != null)
            {
                emojiDisplay.SetEmojiForStatus(status);
                
                // Make sure emoji container is active and speech bubble is inactive
                if (emojiContainer != null)
                    emojiContainer.SetActive(true);
                    
                if (speechBubble != null && speechBubble.activeSelf)
                    speechBubble.SetActive(false);
            }
        }
    }
    
    public void DisplaySpeech(string message)
    {
        if ((speechText == null || speechBubble == null) && (emojiDisplay == null || !useEmojis))
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
        
        // Handle emoji mode
        if (useEmojis && emojiDisplay != null)
        {
            // Update emoji to speaking
            emojiDisplay.SetEmojiForSpeech();
            
            // Make sure emoji container is visible
            if (emojiContainer != null)
                emojiContainer.SetActive(true);
                
            // Hide speech bubble in emoji mode
            if (speechBubble != null)
                speechBubble.SetActive(false);
                
            // Still start the coroutine to handle timing
            currentSpeechCoroutine = StartCoroutine(DisplaySpeechCoroutine(message, true));
        }
        else
        {
            // Normal speech bubble mode
            currentSpeechCoroutine = StartCoroutine(DisplaySpeechCoroutine(message, false));
        }
    }
    
    private IEnumerator DisplaySpeechCoroutine(string message, bool emojiModeActive = false)
    {
        // Debug message
        Debug.Log($"[{agentId}] Displaying speech: {message.Substring(0, Mathf.Min(30, message.Length))}...");
        
        // Force-find the speech bubble and speech text if they're missing
        if (speechBubble == null)
        {
            Debug.LogWarning($"[{agentId}] Speech bubble is NULL before display attempt! Trying to find it...");
            Transform bubbleTransform = uiContainer?.transform.Find("SpeechBubble");
            if (bubbleTransform != null)
            {
                speechBubble = bubbleTransform.gameObject;
                Debug.Log($"[{agentId}] Found speech bubble in hierarchy!");
            }
            else
            {
                Debug.LogError($"[{agentId}] Cannot find speech bubble. Speech will not display!");
                yield break;
            }
        }
        
        if (speechText == null && speechBubble != null)
        {
            Debug.LogWarning($"[{agentId}] Speech text is NULL! Trying to find it...");
            Transform textTransform = speechBubble.transform.Find("SpeechText");
            if (textTransform != null)
            {
                speechText = textTransform.GetComponent<TextMeshPro>();
                if (speechText == null)
                {
                    speechText = textTransform.gameObject.AddComponent<TextMeshPro>();
                }
                Debug.Log($"[{agentId}] Found speech text in hierarchy!");
            }
            else
            {
                Debug.LogError($"[{agentId}] Cannot find or create speech text. Speech will not display properly!");
            }
        }
        
        // Make sure speech bubble is visible
        if (speechBubble != null && !speechBubble.activeSelf)
        {
            speechBubble.SetActive(true);
        }
        
        // Set text content
        if (speechText != null)
{
    // Only override font size and color if explicitly allowed
    if (overrideFontSizes)
    {
        speechText.fontSize = 3.5f;
        speechText.color    = speechColor;
    }

    // Don't adjust any of the text settings to preserve the prefab configuration
    // Only set properties needed for text display
    speechText.enableWordWrapping = true;
    
    // Store original transform values to preserve position and size
    Vector3 originalLocalPosition = speechText.transform.localPosition;
    Vector2 originalSizeDelta = speechText.rectTransform.sizeDelta;
    TextAlignmentOptions originalAlignment = speechText.alignment;

    // Set the message
    speechText.text = message;
    
    // Restore original position and properties to ensure text stays centered correctly
    speechText.transform.localPosition = originalLocalPosition;
    speechText.rectTransform.sizeDelta = originalSizeDelta;
    speechText.alignment = originalAlignment;
    
    Debug.Log($"[{agentId}] Set speech text with preserved positioning.");

    // Keep speech bubble at original prefab size - no size changes
}

        
        // Make speech bubble visible
        if (speechBubble != null)
        {
            speechBubble.SetActive(true);
            Debug.Log($"[{agentId}] Activated speech bubble.");
            
            // Keep speech bubble at original prefab size
            Debug.Log($"[{agentId}] Using original prefab speech bubble size");
        }
        
        // Reset text content if using typewriter effect
        if (useTypewriterEffect && speechText != null)
        {
            string fullMessage = message;
            
            // Store all original positioning data
            Vector3 originalLocalPosition = speechText.transform.localPosition;
            Vector2 originalSizeDelta = speechText.rectTransform.sizeDelta;
            TextAlignmentOptions originalAlignment = speechText.alignment;
            
            // Start with empty text
            speechText.text = "";
            
            // Type out the text character by character
            for (int i = 0; i < fullMessage.Length; i++)
            {
                // Add the next character
                speechText.text += fullMessage[i];
                
                // Restore original values after each character addition to maintain positioning
                speechText.transform.localPosition = originalLocalPosition;
                speechText.rectTransform.sizeDelta = originalSizeDelta;
                speechText.alignment = originalAlignment;
                
                yield return new WaitForSeconds(1f / textAnimationSpeed);
            }
            
            // Final position correction
            speechText.transform.localPosition = originalLocalPosition;
            speechText.rectTransform.sizeDelta = originalSizeDelta;
            speechText.alignment = originalAlignment;
            
            Debug.Log($"[{agentId}] Completed typewriter effect.");
        }
        
        // Wait for the speech duration but don't hide the bubble
        Debug.Log($"[{agentId}] Speech displayed. Waiting {speechDuration} seconds before accepting new speech.");
        yield return new WaitForSeconds(speechDuration);
        
        // For emoji mode, revert to the current status emoji after speaking
        if (emojiModeActive && emojiDisplay != null)
        {
            emojiDisplay.SetEmojiForStatus(statusText?.text ?? "Idle");
        }
        else
        {
            // Speech bubble stays visible - no deactivation
            Debug.Log($"[{agentId}] Speech display complete, but bubble remains visible.");
        }
        
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