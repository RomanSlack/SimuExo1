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
    [SerializeField] private float spawnYPosition = 47.5f; // Exact Y value for agent spawning
    [SerializeField] private Vector3 spawnCenterPosition = new Vector3(336.7f, 47.5f, 428.61f); // Central spawn position
    [SerializeField] private float spawnRandomOffset = 5.0f; // Random offset for X and Z (in units)
    
    [Header("Environment")]
    [SerializeField] private bool usePredefinedLocations = true;
    [SerializeField] private List<LocationDefinition> predefinedLocations = new List<LocationDefinition>();
    
    [Header("Simulation Control")]
    [SerializeField] private bool runAutomatically = false;
    [SerializeField] private float simulationStepInterval = 5.0f;
    [SerializeField] private KeyCode manualStepKey = KeyCode.X;
    [SerializeField] private KeyCode modifierKey = KeyCode.LeftShift;
    [SerializeField] private bool pauseSimulationOnError = true;
    
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
        
        initialized = true;
    }
    
    void Update()
    {
        // Manual step trigger
        if (Input.GetKey(modifierKey) && Input.GetKeyDown(manualStepKey))
        {
            RunSimulationCycle();
        }
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
            // Based on logs, these positions have been verified to work with the NavMesh
            AddLocationIfMissing("park", new Vector3(350.47f, 49.63f, 432.7607f));
            AddLocationIfMissing("library", new Vector3(325.03f, 50.29f, 407.87f));
            AddLocationIfMissing("cantina", new Vector3(324.3666f, 50.33723f, 463.2347f));
            AddLocationIfMissing("gym", new Vector3(300.5f, 50.23723f, 420.8247f));
            AddLocationIfMissing("o2_regulator_room", new Vector3(324.3666f, 50.33723f, 463.2347f));
            
            // Add a central fallback position known to be on the NavMesh (with exact Y coordinate)
            AddLocationIfMissing("center", new Vector3(spawnCenterPosition.x, spawnYPosition, spawnCenterPosition.z));
        }
        
        Debug.Log($"Initialized {locationPositions.Count} locations");
    }
    
    private void AddLocationIfMissing(string name, Vector3 position)
    {
        if (!locationPositions.ContainsKey(name.ToLower()))
        {
            // Verify the position is on the NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(position, out hit, 5f, NavMesh.AllAreas))
            {
                locationPositions.Add(name.ToLower(), hit.position);
            }
            else
            {
                Debug.LogWarning($"Location {name} at {position} is not on NavMesh. Using fallback.");
                
                // Try a wider search
                if (NavMesh.SamplePosition(position, out hit, 20f, NavMesh.AllAreas))
                {
                    locationPositions.Add(name.ToLower(), hit.position);
                }
                else
                {
                    Debug.LogError($"Could not find NavMesh position for location {name}");
                }
            }
        }
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
            labelObj.transform.localPosition = new Vector3(0, 0.3f, 0);
            
            var label = labelObj.AddComponent<TextMeshPro>();
            label.text = location.name;
            label.fontSize = 4;
            label.alignment = TextAlignmentOptions.Center;
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
        Debug.Log($"Initializing {count} default agents around {spawnCenterPosition}");
        
        for (int i = 0; i < count; i++)
        {
            string name = agentNames[i];
            string personality = i < agentPersonalities.Length ? agentPersonalities[i] : agentPersonalities[0];
            
            // Use "center" as the initial location to ensure agents spawn at our custom position
            var agent = CreateNewAgent(name, personality, "center");
            if (agent != null)
            {
                Debug.Log($"Created agent {name} at {agent.transform.position}");
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
        
        // Add UI if missing
        AgentUI ui = agentObject.GetComponent<AgentUI>();
        if (ui == null)
        {
            ui = agentObject.AddComponent<AgentUI>();
        }
        
        // Configure agent
        controller.agentId = agentId;
        ui.agentId = agentId;
        ui.SetNameText(agentId);
        
        // Force UI position update immediately
        ui.SetUIHeight(10.0f); // Use a consistent height
        
        // Set initial position
        Vector3 targetPosition;

        if (initialLocation?.ToLower() == "center" || string.IsNullOrEmpty(initialLocation))
        {
            // Use the spawn center with a random offset in X and Z
            float offsetX = UnityEngine.Random.Range(-spawnRandomOffset, spawnRandomOffset);
            float offsetZ = UnityEngine.Random.Range(-spawnRandomOffset, spawnRandomOffset);
            targetPosition = new Vector3(
                spawnCenterPosition.x + offsetX,
                spawnYPosition,
                spawnCenterPosition.z + offsetZ
            );
        }
        else if (locationPositions.TryGetValue(initialLocation.ToLower(), out Vector3 position))
        {
            targetPosition = position;
        }
        else
        {
            // Random position if location not found
            targetPosition = spawnCenterPosition + new Vector3(
                UnityEngine.Random.Range(-spawnRandomOffset, spawnRandomOffset),
                0,
                UnityEngine.Random.Range(-spawnRandomOffset, spawnRandomOffset)
            );
        }
        
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
        
        // Register with backend communicator
        if (backendCommunicator != null)
        {
            backendCommunicator.RegisterAgent(controller);
        }
        
        // Add to active agents list
        activeAgents.Add(controller);
        
        Debug.Log($"Created new agent: {agentId}");
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
            return $"Agent {agent.agentId} is at {simpleAgentState["location"]} with status: {simpleAgentState["status"]}";
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
        return $"Agent {agent.agentId} is at {currentAgentState["location"]} with status: {currentAgentState["status"]}.\n{nearbyAgents}\n{nearbyObjects}";
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
            // For demo purposes, parsing a simplified response
            // In production, use proper JSON parsing
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
                
                // Display speech
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
            
            // Create the agent at the center spawn point
            CreateNewAgent(name, personality, "center");
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