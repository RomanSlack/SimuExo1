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
    [SerializeField] private Transform agentsContainer;
    [SerializeField] private int maxAgents = 20;
    [SerializeField] private bool autoInitializeAgents = true;
    
    [Header("Environment")]
    [SerializeField] private bool usePredefinedLocations = true;
    [SerializeField] private List<LocationDefinition> predefinedLocations = new List<LocationDefinition>();
    
    [Header("Simulation Control")]
    [SerializeField] private bool runAutomatically = false;
    [SerializeField] private float simulationStepInterval = 5.0f;
    [SerializeField] private KeyCode manualStepKey = KeyCode.X;
    [SerializeField] private KeyCode modifierKey = KeyCode.LeftShift;
    [SerializeField] private bool pauseSimulationOnError = true;
    
    // Component references
    private HttpServer httpServer;
    private BackendCommunicator backendCommunicator;
    private EnvironmentReporter environmentReporter;
    
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
        environmentReporter = FindObjectOfType<EnvironmentReporter>();
        
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
            
            // Add a central fallback position known to be on the NavMesh
            AddLocationIfMissing("center", new Vector3(325.0f, 50.0f, 425.0f));
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
    
    private void InitializeDefaultAgents()
    {
        // Create some default agents for testing
        CreateNewAgent("Agent_A", "Friendly and helpful. Expert in Mars environmental systems.", "library");
        CreateNewAgent("Agent_B", "Analytical and logical. Specializes in electronics and maintenance.", "cantina");
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
        }
        else
        {
            agentObject = new GameObject($"Agent_{agentId}");
            agentObject.transform.SetParent(agentsContainer);
            
            // Add visual representation
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.transform.SetParent(agentObject.transform);
            visual.transform.localPosition = Vector3.zero;
            
            // Add required components
            agentObject.AddComponent<NavMeshAgent>();
        }
        
        // Set name and initialize
        agentObject.name = $"Agent_{agentId}";
        
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
        
        // Set initial position
        Vector3 targetPosition;

        if (!string.IsNullOrEmpty(initialLocation) && locationPositions.TryGetValue(initialLocation.ToLower(), out Vector3 position))
        {
            targetPosition = position;
        }
        else
        {
            // Random position if location not found
            targetPosition = agentsContainer.position + UnityEngine.Random.insideUnitSphere * 10f;
        }
        
        // Ensure position is on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(targetPosition, out hit, 20f, NavMesh.AllAreas))
        {
            agentObject.transform.position = hit.position;
        }
        else
        {
            // If can't find NavMesh nearby, try a fallback approach
            Debug.LogWarning($"Failed to find NavMesh near {targetPosition}. Using fallback position.");
            
            // Try to find any valid position on NavMesh
            Vector3 fallbackOrigin = new Vector3(325f, 50f, 425f); // Center of the map or a known good position
            if (NavMesh.SamplePosition(fallbackOrigin, out hit, 50f, NavMesh.AllAreas))
            {
                agentObject.transform.position = hit.position;
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
    
    public void RunSimulationCycle()
    {
        Debug.Log("Running simulation cycle...");
        
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
    
    private string GetAgentFeedback(AgentController agent)
    {
        // Simple feedback for now - in a real implementation this would use EnvironmentReporter
        var agentState = agent.GetAgentState();
        return $"Agent {agent.agentId} is at {agentState["location"]} with status: {agentState["status"]}";
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