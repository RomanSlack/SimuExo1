using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

public class WorldManager : MonoBehaviour
{
    [Header("Agent Management")]
    [SerializeField] private GameObject agentPrefab;
    [SerializeField] public Transform agentsContainer; // Make public so SceneSetupManager can access it
    [SerializeField] private int maxAgents = 20;
    [SerializeField] private bool autoInitializeAgents = true;
    [SerializeField] private float spawnYPosition = 48.48f; // Exact Y value for agent spawning
    [SerializeField] private Vector3 spawnCenterPosition = new Vector3(369.37f, 48.48f, 437.23f); // Central spawn position
    [SerializeField] private float spawnRandomOffset = 5.0f; // Random offset for X and Z (in units)
    
    [Header("Environment")]
    [SerializeField] private bool usePredefinedLocations = true;
    [SerializeField] private List<LocationDefinition> predefinedLocations = new List<LocationDefinition>();
    
    // Default locations with their coordinates
    [Header("Default Locations")]
    [SerializeField] private Vector3 homePosition = new Vector3(369.37f, 48.48f, 437.23f);
    [SerializeField] private Vector3 plantfarmPosition = new Vector3(346.88f, 48.48f, 446.15f);
    [SerializeField] private Vector3 cantinaPosition = new Vector3(361.01f, 48.48f, 428.35f);
    [SerializeField] private Vector3 solarfarmPosition = new Vector3(388.15f, 48.48f, 453.3f);
    [SerializeField] private Vector3 electricalroomPosition = new Vector3(386.55f, 48.48f, 424.57f);
    [SerializeField] private Vector3 livingquartersPosition = new Vector3(368.69f, 48.48f, 466.66f);
    
    [Header("Simulation Control")]
    [SerializeField] private bool runAutomatically = false;
    [SerializeField] private float simulationStepInterval = 5.0f;
    [SerializeField] private KeyCode manualStepKey = KeyCode.X;
    [SerializeField] private KeyCode modifierKey = KeyCode.LeftShift;
    [SerializeField] private bool pauseSimulationOnError = true;
    
    [Header("UI Controls")]
    [SerializeField] private bool useEmojiMode = false;
    [SerializeField] private KeyCode emojiToggleKey = KeyCode.E;
    
    [Header("Agent Appearance")]
    [SerializeField] private Material[] agentMaterials;  // Array of materials to assign to agents
    [SerializeField] private Material redMaterial;       // Default red material to replace
    [SerializeField] private bool assignMaterialsDirectly = true;  // Use direct assignment instead of random
    
    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugInfo = true;
    
    // Component references
    private HttpServer httpServer;
    private BackendCommunicator backendCommunicator;
    [SerializeField] private EnvironmentReporter environmentReporter;
    
    // Internal state
    private List<AgentController> activeAgents = new List<AgentController>();
    private Dictionary<string, Vector3> locationPositions = new Dictionary<string, Vector3>();
    private Coroutine simulationRoutine;
    private bool simulationRunning = false;
    private bool initialized = false;
    
    void Awake()
    {
        // Find or create containers
        if (agentsContainer == null)
        {
            GameObject container = new GameObject("Agents");
            agentsContainer = container.transform;
        }
        
        // Get component references
        httpServer = FindObjectOfType<HttpServer>();
        backendCommunicator = FindObjectOfType<BackendCommunicator>();
        
        // Find the EnvironmentReporter if not assigned in the inspector
        if (environmentReporter == null)
        {
            environmentReporter = FindObjectOfType<EnvironmentReporter>();
            if (environmentReporter == null)
            {
                Debug.LogWarning("No EnvironmentReporter found. Agent environment detection will be limited.");
            }
        }
        
        // Initialize locations
        InitializeLocations();
    }
    
    void Start()
    {
        // Ensure HTTP server is running
        if (httpServer != null && !httpServer.IsRunning)
        {
            httpServer.StartServer();
        }
        
        // Auto-initialize agents if needed
        if (autoInitializeAgents)
        {
            InitializeDefaultAgents();
        }
        
        // Start simulation if needed
        if (runAutomatically)
        {
            StartSimulation();
        }
        
        // Prime agents through the backend (does nothing if they're already primed)
        if (backendCommunicator != null)
        {
            StartCoroutine(PrimeAgents());
        }
        
        // Set initial emoji mode for all agents
        SetAllAgentsEmojiMode(useEmojiMode);
        
        initialized = true;
    }
    
    void Update()
    {
        // Manual step trigger
        if (Input.GetKey(modifierKey) && Input.GetKeyDown(manualStepKey))
        {
            RunSimulationCycle();
        }
        
        // Toggle emoji mode
        if (Input.GetKeyDown(emojiToggleKey))
        {
            useEmojiMode = !useEmojiMode;
            SetAllAgentsEmojiMode(useEmojiMode);
            Debug.Log($"Emoji mode set to: {useEmojiMode}");
        }
    }
    
    // Toggle emoji mode for all agents
    public void SetAllAgentsEmojiMode(bool useEmojis)
    {
        foreach (var agent in activeAgents)
        {
            AgentUI ui = agent.GetComponent<AgentUI>();
            if (ui != null)
            {
                ui.ToggleEmojiMode(useEmojis);
            }
        }
    }
    
    // Coroutine to assign material after a delay
    private IEnumerator AssignMaterialWithDelay(GameObject agentObject, int agentIndex)
    {
        // Wait a bit longer to ensure the agent is fully initialized
        yield return new WaitForSeconds(0.1f);
        
        // Apply the specific material based on index
        AssignAgentMaterial(agentObject, agentIndex);
    }
    
    // Assign a specific material to an agent based on its index
    private void AssignAgentMaterial(GameObject agentObject, int agentIndex)
    {
        if (agentObject == null)
        {
            Debug.LogError("Cannot assign material to null agent object");
            return;
        }

        Debug.Log($"Attempting to assign material to agent {agentObject.name} with index {agentIndex}");

        // Check if we have materials to work with
        if (agentMaterials == null || agentMaterials.Length == 0)
        {
            Debug.LogError("No agent materials assigned in the inspector");
            return;
        }

        // Select material based on agent index (cycle through available materials)
        int materialIndex = agentIndex % agentMaterials.Length;
        Material materialToAssign = agentMaterials[materialIndex];

        if (materialToAssign == null)
        {
            Debug.LogError($"Material at index {materialIndex} is null");
            return;
        }

        Debug.Log($"Selected material {materialToAssign.name} for agent {agentObject.name}");

        // Try multiple methods to change the material
        bool success = false;

        // METHOD 1: Direct replacement of materials by name
        success = TryReplaceAgentMaterial(agentObject, materialToAssign);
        
        // METHOD 2: If METHOD 1 fails, try to find a SkinnedMeshRenderer
        if (!success)
        {
            SkinnedMeshRenderer[] skinnedMeshRenderers = agentObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedMeshRenderers.Length > 0)
            {
                Debug.Log($"Found {skinnedMeshRenderers.Length} SkinnedMeshRenderers on agent");
                foreach (SkinnedMeshRenderer smr in skinnedMeshRenderers)
                {
                    // Print all materials on this renderer
                    Debug.Log($"Renderer {smr.gameObject.name} has {smr.materials.Length} materials:");
                    for (int i = 0; i < smr.materials.Length; i++)
                    {
                        Debug.Log($"  Material {i}: {smr.materials[i]?.name ?? "null"}");
                    }

                    // Try to replace materials
                    Material[] newMaterials = new Material[smr.materials.Length];
                    for (int i = 0; i < smr.materials.Length; i++)
                    {
                        // Replace material (agent's body material is usually first)
                        if (i == 0 || (smr.materials[i] != null && smr.materials[i].name.Contains("Red")))
                        {
                            newMaterials[i] = materialToAssign;
                            Debug.Log($"Replacing material at index {i} with {materialToAssign.name}");
                        }
                        else
                        {
                            newMaterials[i] = smr.materials[i];
                        }
                    }
                    smr.materials = newMaterials;
                    success = true;
                }
            }
        }

        // METHOD 3: Last resort - brute force replacement of all materials
        if (!success)
        {
            Debug.Log("Attempting brute force material replacement");
            Renderer[] allRenderers = agentObject.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in allRenderers)
            {
                // Skip UI elements and text meshes
                if (renderer.GetComponent<TextMeshPro>() != null ||
                    renderer.gameObject.GetComponentInParent<AgentUI>() != null)
                {
                    continue;
                }

                Material[] newMaterials = new Material[renderer.materials.Length];
                for (int i = 0; i < renderer.materials.Length; i++)
                {
                    newMaterials[i] = materialToAssign;
                }
                renderer.materials = newMaterials;
                success = true;
            }
        }

        if (success)
        {
            Debug.Log($"Successfully assigned material to agent {agentObject.name}");
        }
        else
        {
            Debug.LogError($"Failed to assign material to agent {agentObject.name}");
        }
    }

    // Try to replace the agent material by looking for specific renderers and materials
    private bool TryReplaceAgentMaterial(GameObject agentObject, Material newMaterial)
    {
        // Try to find all renderers that might have the red material
        Renderer[] renderers = agentObject.GetComponentsInChildren<Renderer>();
        bool replacedAny = false;

        foreach (Renderer renderer in renderers)
        {
            // Skip UI-related renderers
            if (renderer.GetComponent<TextMeshPro>() != null || 
                renderer.gameObject.name.Contains("UI") ||
                renderer.gameObject.name.Contains("Text") ||
                renderer.gameObject.name.Contains("Speech") ||
                renderer.gameObject.GetComponentInParent<AgentUI>() != null)
            {
                continue;
            }

            Debug.Log($"Checking renderer: {renderer.gameObject.name} with {renderer.materials.Length} materials");

            // Check all materials on this renderer
            Material[] materials = renderer.sharedMaterials;
            Material[] newMaterials = new Material[materials.Length];
            bool replacedOnThisRenderer = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Material currentMat = materials[i];
                if (currentMat == null)
                {
                    newMaterials[i] = null;
                    continue;
                }

                // Check if this is a material we should replace (Red_Agent or similar)
                if (currentMat.name.Contains("Red") || 
                    (redMaterial != null && currentMat.name == redMaterial.name))
                {
                    Debug.Log($"Found material to replace: {currentMat.name} at index {i}");
                    newMaterials[i] = newMaterial;
                    replacedOnThisRenderer = true;
                }
                else
                {
                    // Keep any non-red materials
                    newMaterials[i] = materials[i];
                }
            }

            // Apply the changes if we found something to replace
            if (replacedOnThisRenderer)
            {
                renderer.sharedMaterials = newMaterials;
                replacedAny = true;
                Debug.Log($"Replaced materials on {renderer.gameObject.name}");
            }
        }

        return replacedAny;
    }
    
    // Old random appearance method - kept for reference
    private void ApplyRandomAppearance(GameObject agentObject)
    {
        if (agentObject == null) return;
        
        // Debug to check if the function is being called
        Debug.Log($"ApplyRandomAppearance called for {agentObject.name}");
        
        // Find all renderers on the agent model (excluding UI elements)
        List<Renderer> agentRenderers = new List<Renderer>();
        foreach (Renderer renderer in agentObject.GetComponentsInChildren<Renderer>(true))
        {
            // Skip any renderer that might be part of the UI
            if (renderer.gameObject.GetComponentInParent<TMPro.TextMeshPro>() != null || 
                renderer.gameObject.GetComponentInParent<AgentUI>() != null ||
                renderer.gameObject.name.Contains("UI_") ||
                renderer.gameObject.name.Contains("Text") ||
                renderer.gameObject.name.Contains("Speech") ||
                renderer.gameObject.name.Contains("Bubble") ||
                renderer.gameObject.name.Contains("Emoji"))
            {
                Debug.Log($"Skipping UI element: {renderer.gameObject.name}");
                continue;
            }
            
            agentRenderers.Add(renderer);
        }
        
        if (agentRenderers.Count == 0)
        {
            Debug.LogWarning($"No suitable renderers found on agent {agentObject.name}");
            return;
        }
        
        Debug.Log($"Found {agentRenderers.Count} renderers to modify on {agentObject.name}");
        
        // Only proceed if we have materials to use
        if (agentMaterials == null || agentMaterials.Length == 0)
        {
            Debug.LogWarning("No materials defined for random agent appearance");
            return;
        }
        
        // Select a random material from the array
        Material randomMaterial = agentMaterials[UnityEngine.Random.Range(0, agentMaterials.Length)];
        
        if (randomMaterial == null)
        {
            Debug.LogWarning("Selected random material is null");
            return;
        }
        
        Debug.Log($"Applying random material {randomMaterial.name} to agent {agentObject.name}");
        
        // Apply the material only to renderers with the Red_Agent material
        foreach (Renderer renderer in agentRenderers)
        {
            // Log the current materials on this renderer
            Material[] currentMaterials = renderer.materials;
            for (int i = 0; i < currentMaterials.Length; i++)
            {
                if (currentMaterials[i] != null)
                {
                    Debug.Log($"Material {i} on {renderer.gameObject.name}: {currentMaterials[i].name}");
                }
            }
            
            // Create a copy of the current materials array
            Material[] newMaterials = new Material[renderer.materials.Length];
            bool materialChanged = false;
            
            // Replace only the Red_Agent material with our random material
            for (int i = 0; i < renderer.materials.Length; i++)
            {
                Material currentMat = renderer.materials[i];
                
                // Check if this is the Red_Agent material
                if (currentMat != null && 
                   (currentMat.name.Contains("Red_Agent") || currentMat.name.StartsWith("Red_Agent")))
                {
                    Debug.Log($"Found Red_Agent material on {renderer.gameObject.name}, replacing with {randomMaterial.name}");
                    
                    // Replace with random material
                    newMaterials[i] = new Material(randomMaterial);
                    materialChanged = true;
                }
                else
                {
                    // Keep the original material
                    newMaterials[i] = new Material(currentMat);
                }
            }
            
            // Only update materials if we actually changed something
            if (materialChanged)
            {
                renderer.materials = newMaterials;
                Debug.Log($"Updated materials on {renderer.gameObject.name}");
            }
        }
        
        Debug.Log($"Finished applying appearance to {agentObject.name}");
    }
    
    private void InitializeLocations()
    {
        // Clear existing locations to avoid duplicates
        locationPositions.Clear();
        
        // Add predefined locations from the Inspector
        foreach (var location in predefinedLocations)
        {
            if (!string.IsNullOrEmpty(location.name) && !locationPositions.ContainsKey(location.name.ToLower()))
            {
                locationPositions.Add(location.name.ToLower(), location.position);
                
                // Create location marker if needed
                if (location.createMarker)
                {
                    CreateLocationMarker(location);
                }
            }
        }
        
        // Add standard locations from reference implementation if using predefined locations
        if (usePredefinedLocations)
        {
            // First, add any predefined locations from inspector if they don't exist already
            // This creates marker prefabs for all default locations
            if (!locationPositions.ContainsKey("home"))
            {
                LocationDefinition homeLocation = new LocationDefinition
                {
                    name = "home",
                    position = homePosition,
                    description = "The home base for all agents",
                    isInteractable = true,
                    createMarker = true
                };
                
                if (!predefinedLocations.Any(l => l.name.ToLower() == "home"))
                {
                    predefinedLocations.Add(homeLocation);
                }
                
                locationPositions.Add("home", homePosition);
                CreateLocationMarker(homeLocation);
            }
            
            // Create a dictionary of default locations
            Dictionary<string, Vector3> defaultLocations = new Dictionary<string, Vector3>
            {
                { "plantfarm", plantfarmPosition },
                { "cantina", cantinaPosition },
                { "solarfarm", solarfarmPosition },
                { "electricalroom", electricalroomPosition },
                { "livingquarters", livingquartersPosition }
                // Removed "center" as it's redundant with "home"
            };
            
            // Add each default location if not already in predefinedLocations
            foreach (var location in defaultLocations)
            {
                if (!locationPositions.ContainsKey(location.Key))
                {
                    LocationDefinition locDef = new LocationDefinition
                    {
                        name = location.Key,
                        position = location.Value,
                        description = $"This is the {location.Key} location",
                        isInteractable = true,
                        createMarker = true
                    };
                    
                    if (!predefinedLocations.Any(l => l.name.ToLower() == location.Key))
                    {
                        predefinedLocations.Add(locDef);
                    }
                    
                    locationPositions.Add(location.Key, location.Value);
                    CreateLocationMarker(locDef);
                }
            }
        }
        
        Debug.Log($"Initialized {locationPositions.Count} locations");
    }
    
    private void AddLocationIfMissing(string name, Vector3 position)
    {
        if (!locationPositions.ContainsKey(name.ToLower()))
        {
            // Verify the position is on the NavMesh
            NavMeshHit hit;
            Vector3 finalPosition;
            
            if (NavMesh.SamplePosition(position, out hit, 5f, NavMesh.AllAreas))
            {
                finalPosition = hit.position;
                locationPositions.Add(name.ToLower(), finalPosition);
            }
            else
            {
                Debug.LogWarning($"Location {name} at {position} is not on NavMesh. Using fallback.");
                
                // Try a wider search
                if (NavMesh.SamplePosition(position, out hit, 20f, NavMesh.AllAreas))
                {
                    finalPosition = hit.position;
                    locationPositions.Add(name.ToLower(), finalPosition);
                }
                else
                {
                    Debug.LogError($"Could not find NavMesh position for location {name}");
                    return; // Skip marker creation if we can't find a valid position
                }
            }
            
            // Create a marker for this location
            CreateStandardLocationMarker(name, finalPosition);
        }
    }
    
    private void CreateStandardLocationMarker(string name, Vector3 position)
    {
        // Create a LocationDefinition for this standard location
        LocationDefinition locationDef = new LocationDefinition
        {
            name = name,
            position = position,
            description = $"This is the {name} location",
            isInteractable = true,
            createMarker = true
        };
        
        // Create the marker
        CreateLocationMarker(locationDef);
        
        // Find the marker we just created and make it more visible
        GameObject marker = GameObject.Find($"Location_{locationDef.name}");
        if (marker != null)
        {
            // Find the visual cylinder component
            Transform visual = marker.transform.GetChild(0);
            if (visual != null)
            {
                // Make it more visible with a distinct material
                Renderer renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // Create a new material with a bright color based on the location name
                    Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    
                    // Pick a color based on location name to make them distinct
                    switch (name.ToLower())
                    {
                        case "home":
                            material.color = new Color(0.2f, 0.6f, 1.0f, 1.0f); // Blue
                            break;
                        case "park":
                            material.color = new Color(0.2f, 0.8f, 0.2f, 1.0f); // Green
                            break;
                        case "library":
                            material.color = new Color(0.8f, 0.3f, 0.8f, 1.0f); // Purple
                            break;
                        case "cantina":
                            material.color = new Color(1.0f, 0.6f, 0.2f, 1.0f); // Orange
                            break;
                        case "gym":
                            material.color = new Color(1.0f, 0.2f, 0.2f, 1.0f); // Red
                            break;
                        case "o2_regulator_room":
                            material.color = new Color(0.2f, 0.9f, 0.9f, 1.0f); // Cyan
                            break;
                        default:
                            material.color = new Color(0.9f, 0.9f, 0.2f, 1.0f); // Yellow
                            break;
                    }
                    
                    // Apply the material
                    renderer.material = material;
                    
                    // Make the marker larger
                    visual.localScale = new Vector3(2.0f, 0.2f, 2.0f);
                }
            }
        }
        
        Debug.Log($"Created marker for location: {name} at {position}");
    }
    
    private void CreateLocationMarker(LocationDefinition location)
    {
        GameObject marker = new GameObject($"Location_{location.name}");
        marker.transform.position = location.position;
        
        // Add visual indicator
        if (location.markerPrefab != null)
        {
            Instantiate(location.markerPrefab, marker.transform);
        }
        else
        {
            // Create a simple marker
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.transform.SetParent(marker.transform);
            visual.transform.localPosition = new Vector3(0, 0.1f, 0);
            visual.transform.localScale = new Vector3(1f, 0.1f, 1f);
            
            // Create label
            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(marker.transform);
            labelObj.transform.localPosition = new Vector3(0, 10f, 0); // Raised by 10 units
            
            var label = labelObj.AddComponent<TextMeshPro>();
            label.text = location.name;
            label.fontSize = 12; // Make text bigger
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white; // Make sure text is visible
            
            // Add script to make the label face the camera
            labelObj.AddComponent<LabelFaceCamera>();
            
            // Set initial scale
            label.transform.localScale = new Vector3(1, 1, 1);
        }
        
        // Make it interactable if needed
        if (location.isInteractable)
        {
            var interactable = marker.AddComponent<InteractableObject>();
            interactable.description = location.description;
        }
    }
    
    [Header("Agent Initialization")]
    [SerializeField] private int initialAgentCount = 2; // Number of agents to spawn on startup
    [SerializeField] private string[] agentNames = new string[] { "Agent_A", "Agent_B", "Agent_C", "Agent_D", "Agent_E" };
    [SerializeField] private string[] agentPersonalities = new string[] {
        "Friendly and helpful. Expert in Mars environmental systems.",
        "Analytical and logical. Specializes in electronics and maintenance.",
        "Creative and curious. Interested in exploring and discovering new things.",
        "Cautious and detail-oriented. Focuses on safety protocols and risk assessment.",
        "Optimistic and adaptable. Excels at finding solutions to unexpected problems."
    };

    private void InitializeDefaultAgents()
    {
        // Cap initial count to available names and max agents
        int count = Mathf.Min(initialAgentCount, agentNames.Length, maxAgents);
        Debug.Log($"Initializing {count} default agents");
        
        for (int i = 0; i < count; i++)
        {
            string name = agentNames[i];
            string personality = i < agentPersonalities.Length ? agentPersonalities[i] : agentPersonalities[0];
            string initialLocation = "home"; // Default to home
            
            // Try to get the default location from the backend if connected
            // If not available, will use "home" as default
            if (backendCommunicator != null)
            {
                Dictionary<string, string> profileLocations = new Dictionary<string, string>
                {
                    { "Agent_A", "home" },
                    { "Agent_B", "home" },
                    { "Agent_C", "cantina" },
                    { "Agent_D", "electricalroom" },
                    { "Agent_E", "solarfarm" }
                };
                
                // Use known default locations from profiles if available
                if (profileLocations.ContainsKey(name))
                {
                    initialLocation = profileLocations[name];
                }
            }
            
            // Create the agent at their default location or home if not specified
            var agent = CreateNewAgent(name, personality, initialLocation);
            if (agent != null)
            {
                Debug.Log($"Created agent {name} at location: {initialLocation}");
            }
        }
    }
    
    public AgentController CreateNewAgent(string agentId, string personality, string initialLocation)
    {
        if (activeAgents.Count >= maxAgents)
        {
            Debug.LogWarning($"Cannot create agent {agentId}: Maximum number of agents reached");
            return null;
        }
        
        if (activeAgents.Any(a => a.agentId == agentId))
        {
            Debug.LogWarning($"Agent with ID {agentId} already exists");
            return null;
        }
        
        // Create the agent from prefab or a default cube
        GameObject agentObject;
        if (agentPrefab != null)
        {
            agentObject = Instantiate(agentPrefab, agentsContainer);
            // Don't override prefab scale to preserve the size from the prefab
            
            // Make sure it's on the Agent layer for detection
            agentObject.layer = LayerMask.NameToLayer("Agent");
            
            // Add "Agent" tag if it exists
            try {
                agentObject.tag = "Agent";
            } catch (UnityException) {
                Debug.LogWarning("'Agent' tag not defined in project. Consider adding it for better agent detection.");
            }
        }
        else
        {
            agentObject = new GameObject($"Agent_{agentId}");
            agentObject.transform.SetParent(agentsContainer);
            
            // Make sure it's on the Agent layer for detection
            agentObject.layer = LayerMask.NameToLayer("Agent");
            
            // Add "Agent" tag if it exists
            try {
                agentObject.tag = "Agent";
            } catch (UnityException) {
                Debug.LogWarning("'Agent' tag not defined in project. Consider adding it for better agent detection.");
            }
            
            // Add visual representation
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.transform.SetParent(agentObject.transform);
            visual.transform.localPosition = Vector3.zero;
            
            // Add required components
            agentObject.AddComponent<NavMeshAgent>();
        }
        
        // Set name and initialize - ensure it doesn't keep the default name from the prefab
        agentObject.name = $"Agent_{agentId.Replace("Agent_", "")}";
        
        // Add controller if missing
        AgentController controller = agentObject.GetComponent<AgentController>();
        if (controller == null)
        {
            controller = agentObject.AddComponent<AgentController>();
        }
        
        // Add UI if missing, but be careful with the prefab
        AgentUI ui = agentObject.GetComponent<AgentUI>();
        if (ui == null)
        {
            // Log this issue
            Debug.LogWarning($"AgentUI component was not found on the instantiated prefab. This is unexpected and may cause UI issues.");
            ui = agentObject.AddComponent<AgentUI>();
        }
        else
        {
            Debug.Log($"Using existing AgentUI component from prefab for agent {agentId}");
        }
        
        // Configure agent
        controller.agentId = agentId;
        ui.agentId = agentId;
        ui.SetNameText(agentId);
        
        // Apply materials (with delay to ensure model is fully loaded)
        if (assignMaterialsDirectly)
        {
            // Get the agent index to determine which material to use
            int agentIndex = activeAgents.Count;
            
            // Use coroutine to apply the appearance after a short delay
            StartCoroutine(AssignMaterialWithDelay(agentObject, agentIndex));
        }
        
        // Don't force UI height - use the values from prefab instead

        // Set initial position and agent's current location
        Vector3 targetPosition;
        string actualLocation; // Track which location we actually use

        // Default to "home" if not specified or if "center" is specified
        if (string.IsNullOrEmpty(initialLocation) || initialLocation?.ToLower() == "center")
        {
            initialLocation = "home";
        }
        
        // If initialLocation is "home" or another known location, use it
        if (locationPositions.TryGetValue(initialLocation.ToLower(), out Vector3 position))
        {
            targetPosition = position;
            actualLocation = initialLocation.ToLower();
        }
        else
        {
            // Use "home" as fallback if the specified location doesn't exist
            Debug.LogWarning($"Location '{initialLocation}' not found for agent {agentId}, using 'home' instead");
            
            if (locationPositions.TryGetValue("home", out Vector3 homePosition))
            {
                targetPosition = homePosition;
                actualLocation = "home";
            }
            else
            {
                // This shouldn't happen since we always add "home", but just in case
                targetPosition = spawnCenterPosition;
                actualLocation = "home";
                
                // Add home location if it's somehow missing
                AddLocationIfMissing("home", spawnCenterPosition);
            }
        }
        
        // Add small random offset to prevent agents from stacking exactly
        float offsetX = UnityEngine.Random.Range(-spawnRandomOffset * 0.5f, spawnRandomOffset * 0.5f);
        float offsetZ = UnityEngine.Random.Range(-spawnRandomOffset * 0.5f, spawnRandomOffset * 0.5f);
        targetPosition = new Vector3(
            targetPosition.x + offsetX,
            spawnYPosition,
            targetPosition.z + offsetZ
        );
        
        // Force Y position to exact value while ensuring XZ is on NavMesh
        NavMeshHit hit;
        // First try the exact position with our fixed Y coordinate
        Vector3 positionWithFixedY = new Vector3(targetPosition.x, spawnYPosition, targetPosition.z);
        
        if (NavMesh.SamplePosition(positionWithFixedY, out hit, 20f, NavMesh.AllAreas))
        {
            // Use the hit position but override Y to our exact value
            Vector3 finalPosition = hit.position;
            finalPosition.y = spawnYPosition;
            agentObject.transform.position = finalPosition;

            NavMeshAgent nav = agentObject.GetComponent<NavMeshAgent>();
            if (nav != null)
            {
                nav.Warp(finalPosition);   // formally place agent on the NavMesh
                nav.updateRotation = true; // keep the usual rotation updates
            }
        }
        else
        {
            // If can't find NavMesh nearby, try a fallback approach
            Debug.LogWarning($"Failed to find NavMesh near {positionWithFixedY}. Using fallback position.");
            
            // Try to find any valid position near the center with our fixed Y
            Vector3 fallbackOrigin = new Vector3(spawnCenterPosition.x, spawnYPosition, spawnCenterPosition.z); // Center of the map
            if (NavMesh.SamplePosition(fallbackOrigin, out hit, 50f, NavMesh.AllAreas))
            {
                Vector3 finalPosition = hit.position;
                finalPosition.y = spawnYPosition; // Force exact Y coordinate
                agentObject.transform.position = finalPosition;
            }
            else
            {
                // If still failed, destroy the agent and return null
                Debug.LogError("Failed to create agent because it is not close enough to the NavMesh");
                Destroy(agentObject);
                return null;
            }
        }
        
        // Set the agent's current location in the controller
        controller.SetLocation(actualLocation);
        
        // Add all known locations to the agent's knowledge
        foreach (var locationEntry in locationPositions)
        {
            controller.AddKnownLocation(locationEntry.Key, locationEntry.Value);
        }
        
        // Register with backend communicator
        if (backendCommunicator != null)
        {
            backendCommunicator.RegisterAgent(controller);
        }
        
        // Add to active agents list
        activeAgents.Add(controller);
        
        Debug.Log($"Created new agent: {agentId} at location: {actualLocation}");
        return controller;
    }
    
    public bool RemoveAgent(string agentId)
    {
        AgentController agent = activeAgents.FirstOrDefault(a => a.agentId == agentId);
        if (agent == null)
        {
            Debug.LogWarning($"Cannot remove agent {agentId}: Agent not found");
            return false;
        }
        
        // Unregister with backend
        if (backendCommunicator != null)
        {
            backendCommunicator.UnregisterAgent(agentId);
        }
        
        // Remove from active agents
        activeAgents.Remove(agent);
        
        // Destroy game object
        Destroy(agent.gameObject);
        
        Debug.Log($"Removed agent: {agentId}");
        return true;
    }
    
    public void StartSimulation()
    {
        if (simulationRunning)
            return;
        
        simulationRunning = true;
        
        if (simulationRoutine != null)
        {
            StopCoroutine(simulationRoutine);
        }
        
        simulationRoutine = StartCoroutine(SimulationCoroutine());
        Debug.Log("Simulation started");
    }
    
    public void StopSimulation()
    {
        if (!simulationRunning)
            return;
        
        simulationRunning = false;
        
        if (simulationRoutine != null)
        {
            StopCoroutine(simulationRoutine);
            simulationRoutine = null;
        }
        
        Debug.Log("Simulation stopped");
    }
    
    private IEnumerator SimulationCoroutine()
    {
        while (simulationRunning)
        {
            yield return new WaitForSeconds(simulationStepInterval);
            
            try
            {
                RunSimulationCycle();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during simulation cycle: {e.Message}");
                
                if (pauseSimulationOnError)
                {
                    simulationRunning = false;
                    Debug.LogWarning("Simulation paused due to error");
                    yield break;
                }
            }
        }
    }
    
    [Header("Simulation Status")]
    [SerializeField] private bool agentsPrimed = false;
    
    public void RunSimulationCycle()
    {
        Debug.Log("Running simulation cycle...");
        
        // If agents haven't been primed yet, prime them first
        if (!agentsPrimed && backendCommunicator != null)
        {
            Debug.Log("Priming agents before first simulation cycle...");
            StartCoroutine(PrimeAndRunCycle());
            return;
        }
        
        foreach (var agent in activeAgents)
        {
            try
            {
                // In a real implementation, we would get the environment state for each agent
                // and send a decision request to the backend
                
                // Get environment state (simplified for demo)
                string environmentContext = GetAgentFeedback(agent);
                
                // Request decision from backend (async)
                StartCoroutine(RequestAgentDecisionAsync(agent, environmentContext));
            }
            catch (Exception e)
            {
                Debug.LogError($"Error processing agent {agent.agentId}: {e.Message}");
                
                if (pauseSimulationOnError)
                {
                    StopSimulation();
                    break;
                }
            }
        }
    }
    
    private IEnumerator PrimeAndRunCycle()
    {
        // Create the request for priming all agents
        Dictionary<string, object> requestData = new Dictionary<string, object>
        {
            { "force", false } // Don't force re-priming of already primed agents
        };
        
        bool primeSuccess = false;
        
        // Send the request to the backend
        yield return backendCommunicator.SendRequest(
            "POST", 
            "/agents/prime", 
            requestData,
            (success, response) => {
                if (success)
                {
                    Debug.Log("Successfully primed agents for simulation");
                    primeSuccess = true;
                    agentsPrimed = true;
                }
                else
                {
                    Debug.LogWarning($"Failed to prime agents: {response}");
                }
            }
        );
        
        // If successful, continue with the simulation cycle
        if (primeSuccess)
        {
            // Small delay to let the priming "sink in"
            yield return new WaitForSeconds(0.5f);
            
            // Now run the actual simulation
            RunSimulationCycle();
        }
    }
    
    private string GetAgentFeedback(AgentController agent)
    {
        if (environmentReporter == null)
        {
            // Simple fallback feedback if no EnvironmentReporter is available
            var simpleAgentState = agent.GetAgentState();
            string simpleLoc = simpleAgentState.ContainsKey("location") ? 
                              simpleAgentState["location"]?.ToString() ?? "home" : 
                              "home";
                              
            return $"Agent {agent.agentId} is at {simpleLoc} with status: {simpleAgentState["status"]}";
        }
        
        // Get detailed environment state from the EnvironmentReporter
        var envState = environmentReporter.GetEnvironmentState(agent.agentId);
        
        // Check if there are nearby agents
        string nearbyAgents = "No other agents nearby.";
        if (envState.ContainsKey("agents") && envState["agents"] is List<Dictionary<string, object>> agents && agents.Count > 1)
        {
            List<string> agentDescriptions = new List<string>();
            foreach (var otherAgent in agents)
            {
                string otherAgentId = otherAgent["id"].ToString();
                // Skip the current agent and any Agent_Default entities
                if (otherAgentId != agent.agentId && !otherAgentId.Contains("Default")) 
                {
                    string status = otherAgent.ContainsKey("status") ? otherAgent["status"].ToString() : "Unknown";
                    agentDescriptions.Add($"{otherAgentId} ({status})");
                }
            }
            
            if (agentDescriptions.Count > 0)
            {
                nearbyAgents = $"Nearby agents: {string.Join(", ", agentDescriptions)}";
            }
        }
        
        // Check if there are nearby objects
        string nearbyObjects = "No notable objects nearby.";
        if (envState.ContainsKey("objects") && envState["objects"] is List<Dictionary<string, object>> objects && objects.Count > 0)
        {
            List<string> objectDescriptions = new List<string>();
            foreach (var obj in objects)
            {
                string name = obj.ContainsKey("name") ? obj["name"].ToString() : "Unknown object";
                string description = obj.ContainsKey("description") && obj["description"] != null 
                    ? $" - {obj["description"]}" : "";
                objectDescriptions.Add($"{name}{description}");
            }
            
            if (objectDescriptions.Count > 0)
            {
                nearbyObjects = $"Nearby objects: {string.Join(", ", objectDescriptions)}";
            }
        }
        
        // Construct the feedback
        var currentAgentState = agent.GetAgentState();
        
        // Make sure the location is properly extracted
        string agentLocation = currentAgentState.ContainsKey("location") ? 
                              currentAgentState["location"]?.ToString() ?? "home" : 
                              "home";
        
        // Log the location for debugging
        Debug.Log($"Agent feedback using location: {agentLocation} for agent {agent.agentId}");
                          
        return $"Agent {agent.agentId} is at {agentLocation} with status: {currentAgentState["status"]}.\n{nearbyAgents}\n{nearbyObjects}";
    }
    
    private IEnumerator RequestAgentDecisionAsync(AgentController agent, string environmentContext)
    {
        if (backendCommunicator == null)
        {
            Debug.LogError("Backend communicator not found");
            yield break;
        }
        
        var task = backendCommunicator.RequestAgentDecision(agent.agentId, null, null);
        
        // Wait for the task to complete
        while (!task.IsCompleted)
        {
            yield return null;
        }
        
        // Process the result
        if (task.IsFaulted)
        {
            Debug.LogError($"Error requesting decision for agent {agent.agentId}: {task.Exception}");
        }
        else if (task.IsCompleted)
        {
            string response = task.Result;
            if (!string.IsNullOrEmpty(response))
            {
                // Parse the response and take action
                ProcessAgentResponse(agent, response);
            }
        }
    }
    
    private void ProcessAgentResponse(AgentController agent, string response)
    {
        if (string.IsNullOrEmpty(response))
            return;
        
        try
        {
            // Get full text from response to display
            string fullText = "";
            if (response.Contains("\"text\":\""))
            {
                string textStart = "\"text\":\"";
                int startIdx = response.IndexOf(textStart) + textStart.Length;
                int endIdx = response.IndexOf("\"", startIdx);
                if (endIdx > startIdx)
                {
                    fullText = response.Substring(startIdx, endIdx - startIdx);
                }
                
                // Display the full response text as speech
                var ui = agent.GetComponent<AgentUI>();
                if (ui != null && !string.IsNullOrEmpty(fullText))
                {
                    ui.DisplaySpeech(fullText);
                    Debug.Log($"Agent {agent.agentId} says: {fullText}");
                }
            }
            
            // Process action
            if (response.Contains("\"action_type\":\"move\""))
            {
                // Extract location
                string locationStart = "\"action_param\":\"";
                int startIdx = response.IndexOf(locationStart) + locationStart.Length;
                int endIdx = response.IndexOf("\"", startIdx);
                string location = response.Substring(startIdx, endIdx - startIdx);
                
                // Request move
                agent.RequestMove(location);
            }
            else if (response.Contains("\"action_type\":\"speak\""))
            {
                // Extract message
                string messageStart = "\"action_param\":\"";
                int startIdx = response.IndexOf(messageStart) + messageStart.Length;
                int endIdx = response.IndexOf("\"", startIdx);
                string message = response.Substring(startIdx, endIdx - startIdx);
                
                // Display speech (already handled above with full text, but adding specific action)
                var ui = agent.GetComponent<AgentUI>();
                if (ui != null)
                {
                    ui.DisplaySpeech(message);
                }
            }
            else if (response.Contains("\"action_type\":\"converse\""))
            {
                // Extract target agent
                string targetStart = "\"action_param\":\"";
                int startIdx = response.IndexOf(targetStart) + targetStart.Length;
                int endIdx = response.IndexOf("\"", startIdx);
                string targetAgent = response.Substring(startIdx, endIdx - startIdx);
                
                // Find target
                var target = activeAgents.FirstOrDefault(a => a.agentId == targetAgent);
                if (target != null)
                {
                    agent.InitiateConversation(target);
                }
            }
            else if (response.Contains("\"action_type\":\"nothing\""))
            {
                // Just display the thinking/reasoning as speech
                var ui = agent.GetComponent<AgentUI>();
                if (ui != null && !string.IsNullOrEmpty(fullText))
                {
                    ui.DisplaySpeech(fullText);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing response for agent {agent.agentId}: {e.Message}");
        }
    }
    
    // Utility method to get agent by ID
    public AgentController GetAgentById(string agentId)
    {
        return activeAgents.FirstOrDefault(a => a.agentId == agentId);
    }
    
    // Utility method to get all location positions
    public Dictionary<string, Vector3> GetLocationPositions()
    {
        return new Dictionary<string, Vector3>(locationPositions);
    }
    
    // Public methods for controlling agents
    
    /// <summary>
    /// Add a specified number of new agents to the simulation
    /// </summary>
    public void AddAgents(int count)
    {
        // Don't exceed max agents
        count = Mathf.Min(count, maxAgents - activeAgents.Count);
        
        if (count <= 0)
        {
            Debug.LogWarning($"Cannot add more agents: already at max capacity ({activeAgents.Count}/{maxAgents})");
            return;
        }
        
        for (int i = 0; i < count; i++)
        {
            // Generate a name that's not already in use
            string name = $"Agent_{(char)('A' + UnityEngine.Random.Range(0, 26))}_{activeAgents.Count}";
            
            // Pick a random personality
            string personality = agentPersonalities[UnityEngine.Random.Range(0, agentPersonalities.Length)];
            
            // Create the agent at the home location
            CreateNewAgent(name, personality, "home");
        }
        
        Debug.Log($"Added {count} new agents. Total agents: {activeAgents.Count}");
    }
    
    /// <summary>
    /// Remove a specified number of agents from the simulation
    /// </summary>
    public void RemoveAgents(int count)
    {
        count = Mathf.Min(count, activeAgents.Count);
        
        for (int i = 0; i < count; i++)
        {
            if (activeAgents.Count > 0)
            {
                // Remove the last agent in the list
                var agent = activeAgents[activeAgents.Count - 1];
                RemoveAgent(agent.agentId);
            }
        }
        
        Debug.Log($"Removed {count} agents. Remaining agents: {activeAgents.Count}");
    }
    
    /// <summary>
    /// Set the exact number of agents in the simulation
    /// </summary>
    public void SetAgentCount(int targetCount)
    {
        targetCount = Mathf.Clamp(targetCount, 0, maxAgents);
        int currentCount = activeAgents.Count;
        
        if (targetCount > currentCount)
        {
            AddAgents(targetCount - currentCount);
        }
        else if (targetCount < currentCount)
        {
            RemoveAgents(currentCount - targetCount);
        }
    }
    
    // Prime agents with initial context before simulation
    private IEnumerator PrimeAgents()
    {
        // Allow some time for everything to initialize
        yield return new WaitForSeconds(2.0f);
        
        Debug.Log("Priming active agents with initial context...");
        
        // Get the IDs of active agents only
        List<string> activeAgentIds = activeAgents.Select(a => a.agentId).ToList();
        
        if (activeAgentIds.Count == 0) {
            Debug.LogWarning("No active agents to prime");
            yield break;
        }
        
        Debug.Log($"Active agents to prime: {string.Join(", ", activeAgentIds)}");
        
        // Prime each active agent individually instead of all profiles
        foreach (string agentId in activeAgentIds)
        {
            Dictionary<string, object> requestData = new Dictionary<string, object>
            {
                { "agent_id", agentId },
                { "force", false } // Don't force re-priming of already primed agents
            };
            
            yield return backendCommunicator.SendRequest(
                "POST", 
                $"/profiles/{agentId}", 
                requestData,
                (success, response) => {
                    if (success)
                    {
                        Debug.Log($"Updated profile for agent {agentId}");
                    }
                }
            );
            
            yield return new WaitForSeconds(0.5f);
        }
        
        // Now prime just those active agents - need to wrap in an object for JSON
        var agentIdsList = new List<object>();
        foreach (var id in activeAgentIds) {
            agentIdsList.Add(id);
        }
        
        Dictionary<string, object> agentsToProcess = new Dictionary<string, object>
        {
            { "agent_ids", agentIdsList },
            { "force", false } // Don't force re-priming of already primed agents
        };
        
        // Send the request to the backend
        yield return backendCommunicator.SendRequest(
            "POST", 
            "/agents/prime", 
            agentsToProcess,
            (success, response) => {
                if (success)
                {
                    Debug.Log($"Successfully primed {activeAgentIds.Count} active agents for simulation");
                }
                else
                {
                    Debug.LogWarning($"Failed to prime agents: {response}");
                }
            }
        );
    }
    
    // Debug visualization
    void OnDrawGizmos()
    {
        if (!showDebugInfo)
            return;
        
        // Visualize spawn area
        Gizmos.color = new Color(0, 1, 0, 0.2f); // Semi-transparent green
        Gizmos.DrawSphere(spawnCenterPosition, 0.5f);
        Gizmos.DrawWireSphere(spawnCenterPosition, spawnRandomOffset);
    }
}

[System.Serializable]
public class LocationDefinition
{
    public string name;
    public Vector3 position;
    public string description;
    public bool isInteractable = true;
    public bool createMarker = true;
    public GameObject markerPrefab;
}