using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Linq;

public class BackendCommunicator : MonoBehaviour
{
    [Header("Backend Configuration")]
    [SerializeField] private string backendUrl = "http://localhost:3000";
    [SerializeField] private float pollingInterval = 1.0f;
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private float retryDelay = 2.0f;
    
    [Header("Communication Settings")]
    [SerializeField] private bool automaticPolling = true;
    [SerializeField] private bool sendEnvironmentUpdates = true;
    [SerializeField] private float environmentUpdateInterval = 5.0f;
    
    [Header("Status")]
    [SerializeField] private bool isConnected = false;
    [SerializeField] private string lastConnectionError = "";
    [SerializeField] private int successfulRequests = 0;
    [SerializeField] private int failedRequests = 0;
    
    private EnvironmentReporter environmentReporter;
    private Coroutine pollingCoroutine;
    private Coroutine environmentUpdateCoroutine;
    private Dictionary<string, AgentController> trackedAgents = new Dictionary<string, AgentController>();
    private WorldManager worldManager;
    
    void Awake()
    {
        environmentReporter = FindObjectOfType<EnvironmentReporter>();
        worldManager = FindObjectOfType<WorldManager>();
        
        if (environmentReporter == null)
        {
            Debug.LogWarning("No EnvironmentReporter found, environment state updates will be disabled");
            sendEnvironmentUpdates = false;
        }
        
        if (worldManager == null)
        {
            Debug.LogWarning("No WorldManager found, agent feedback may be limited");
        }
    }
    
    void Start()
    {
        // Start polling the backend
        if (automaticPolling)
        {
            StartPolling();
        }
        
        // Start environment updates
        if (sendEnvironmentUpdates && environmentReporter != null)
        {
            StartEnvironmentUpdates();
        }
        
        // Initial connection check
        StartCoroutine(CheckConnection());
    }
    
    void OnDestroy()
    {
        StopAllCoroutines();
    }
    
    public void StartPolling()
    {
        if (pollingCoroutine != null)
        {
            StopCoroutine(pollingCoroutine);
        }
        
        pollingCoroutine = StartCoroutine(PollBackend());
    }
    
    public void StopPolling()
    {
        if (pollingCoroutine != null)
        {
            StopCoroutine(pollingCoroutine);
            pollingCoroutine = null;
        }
    }
    
    public void StartEnvironmentUpdates()
    {
        if (environmentUpdateCoroutine != null)
        {
            StopCoroutine(environmentUpdateCoroutine);
        }
        
        environmentUpdateCoroutine = StartCoroutine(SendEnvironmentUpdates());
    }
    
    public void StopEnvironmentUpdates()
    {
        if (environmentUpdateCoroutine != null)
        {
            StopCoroutine(environmentUpdateCoroutine);
            environmentUpdateCoroutine = null;
        }
    }
    
    private IEnumerator CheckConnection()
    {
        Debug.Log($"Checking connection to backend at {backendUrl}/health");
        
        UnityWebRequest request = null;
        
        // Create the request outside the try block
        request = UnityWebRequest.Get($"{backendUrl}/health");
        request.timeout = 5; // Set timeout to 5 seconds
        
        // This operation can throw exceptions
        var operation = request.SendWebRequest();
        
        // Wait for the operation to complete
        while (!operation.isDone)
        {
            yield return null;
        }
        
        try
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                isConnected = true;
                lastConnectionError = "";
                Debug.Log("Successfully connected to backend");
                
                // Parse the response to get more information
                try 
                {
                    string responseText = request.downloadHandler.text;
                    Debug.Log($"Backend health response: {responseText}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Error parsing health response: {e.Message}");
                }
            }
            else
            {
                isConnected = false;
                lastConnectionError = request.error;
                Debug.LogWarning($"Failed to connect to backend: {request.error}");
            }
        }
        catch (System.Exception e)
        {
            isConnected = false;
            lastConnectionError = e.Message;
            Debug.LogError($"Exception checking connection: {e.Message}");
        }
        finally
        {
            if (request != null)
            {
                request.Dispose();
            }
        }
    }
    
    private IEnumerator PollBackend()
    {
        while (true)
        {
            // Wait for the polling interval
            yield return new WaitForSeconds(pollingInterval);
            
            // Check connection
            if (!isConnected)
            {
                yield return CheckConnection();
                if (!isConnected)
                {
                    continue;
                }
            }
            
            // Process any pending commands for agents
            ProcessPendingCommands();
        }
    }
    
    private IEnumerator SendEnvironmentUpdates()
    {
        while (true)
        {
            // Wait for the update interval
            yield return new WaitForSeconds(environmentUpdateInterval);
            
            // Check if we should send updates
            if (!isConnected || environmentReporter == null)
            {
                continue;
            }
            
            // Get the current environment state
            var environmentState = environmentReporter.GetEnvironmentState();
            
            // Send it to the backend
            yield return SendRequest(
                "POST", 
                "/env/update", 
                environmentState,
                (success, response) => {
                    if (success)
                    {
                        Debug.Log("Environment state updated successfully");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to update environment state: {response}");
                    }
                }
            );
        }
    }
    
    private void ProcessPendingCommands()
    {
        // In a real implementation, we would check for pending commands for each agent
        // For now, this is a stub
    }
    
    public void RegisterAgent(AgentController agent)
    {
        if (agent == null || string.IsNullOrEmpty(agent.agentId))
        {
            Debug.LogError("Cannot register agent: Invalid agent or missing ID");
            return;
        }
        
        if (trackedAgents.ContainsKey(agent.agentId))
        {
            Debug.LogWarning($"Agent with ID {agent.agentId} is already registered");
            return;
        }
        
        trackedAgents.Add(agent.agentId, agent);
        
        // Notify the backend
        if (isConnected)
        {
            // Get the agent's state
            var agentState = agent.GetAgentState();
            
            // Extract the current location
            string initialLocation = "home"; // Default to home if not set
            if (agentState.ContainsKey("location") && agentState["location"] != null && !string.IsNullOrEmpty(agentState["location"].ToString()))
            {
                initialLocation = agentState["location"].ToString();
            }
            
            // Log the location being sent
            Debug.Log($"Registering agent {agent.agentId} with initial location: {initialLocation}");
            
            var registerData = new Dictionary<string, object>
            {
                { "agent_id", agent.agentId }, // Updated to match backend API
                { "initial_location", initialLocation }
            };
            
            StartCoroutine(SendRequest(
                "POST", 
                "/agent/register", 
                registerData,
                (success, response) => {
                    if (success)
                    {
                        Debug.Log($"Agent {agent.agentId} registered with backend");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to register agent with backend: {response}");
                    }
                }
            ));
        }
    }
    
    public void UnregisterAgent(string agentId)
    {
        if (string.IsNullOrEmpty(agentId) || !trackedAgents.ContainsKey(agentId))
        {
            Debug.LogWarning($"Cannot unregister agent: Agent {agentId} not found");
            return;
        }
        
        // Notify the backend
        if (isConnected)
        {
            StartCoroutine(SendRequest(
                "DELETE", 
                $"/agent/{agentId}", 
                null,
                (success, response) => {
                    if (success)
                    {
                        Debug.Log($"Agent {agentId} unregistered from backend");
                        trackedAgents.Remove(agentId);
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to unregister agent from backend: {response}");
                    }
                }
            ));
        }
    }
    
    public void NotifyAgentStateChange(AgentController agent)
    {
        if (agent == null || string.IsNullOrEmpty(agent.agentId))
        {
            Debug.LogError("Cannot notify state change: Invalid agent or missing ID");
            return;
        }
        
        // Register the agent if not tracked
        if (!trackedAgents.ContainsKey(agent.agentId))
        {
            RegisterAgent(agent);
        }
        
        // In a real implementation, we might queue these updates or send them immediately
        // For now, we're just updating the tracked agent state
    }
    
    public async Task<string> RequestAgentDecision(string agentId, string systemPrompt = null, string task = null)
    {
        if (string.IsNullOrEmpty(agentId))
        {
            Debug.LogError("Cannot request decision: Missing agent ID");
            return null;
        }
        
        if (!isConnected)
        {
            Debug.LogWarning("Cannot request decision: Not connected to backend");
            return null;
        }
        
        var decisionRequest = new Dictionary<string, object>
        {
            { "agent_id", agentId },
            { "user_input", GetAgentFeedback(agentId) } // Add a user_input for compatibility with old API
        };
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            decisionRequest["system_prompt"] = systemPrompt;
        }
        
        if (!string.IsNullOrEmpty(task))
        {
            decisionRequest["task"] = task;
        }
        
        var taskCompletionSource = new TaskCompletionSource<string>();
        
        StartCoroutine(SendRequest(
            "POST", 
            "/generate", 
            decisionRequest,
            (success, response) => {
                if (success)
                {
                    Debug.Log($"Received decision for agent {agentId}");
                    taskCompletionSource.SetResult(response);
                }
                else
                {
                    Debug.LogWarning($"Failed to get decision for agent {agentId}: {response}");
                    taskCompletionSource.SetResult(null);
                }
            }
        ));
        
        return await taskCompletionSource.Task;
    }
    
    public IEnumerator SendRequest(string method, string endpoint, object data, Action<bool, string> callback, int retryCount = 0)
    {
        string url = $"{backendUrl}{endpoint}";
        UnityWebRequest request = null;
        
        if (method.ToUpper() == "GET")
        {
            request = UnityWebRequest.Get(url);
        }
        else if (method.ToUpper() == "POST")
        {
            string jsonData;
            
            // Use Newtonsoft.Json or other method to properly serialize dictionaries/complex objects
            if (data != null)
            {
                if (data is Dictionary<string, object>)
                {
                    // Manual serialization for Dictionary<string, object>
                    jsonData = SerializeDictionary(data as Dictionary<string, object>);
                }
                else
                {
                    // Default serialization for other types
                    jsonData = JsonUtility.ToJson(data);
                }
            }
            else
            {
                jsonData = "{}";
            }
            
            request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            
            // Log the actual JSON being sent for debugging
            if (data != null)
            {
                Debug.Log($"Sending JSON data to {endpoint}: {jsonData}");
            }
        }
        else if (method.ToUpper() == "DELETE")
        {
            request = UnityWebRequest.Delete(url);
            request.downloadHandler = new DownloadHandlerBuffer();
        }
        else
        {
            Debug.LogError($"Unsupported HTTP method: {method}");
            callback?.Invoke(false, $"Unsupported HTTP method: {method}");
            yield break;
        }
        
        // Set common headers
        request.SetRequestHeader("Accept", "application/json");
        
        yield return request.SendWebRequest();
        
        if (request.result == UnityWebRequest.Result.Success)
        {
            // Success
            successfulRequests++;
            callback?.Invoke(true, request.downloadHandler.text);
        }
        else
        {
            // Request failed
            failedRequests++;
            
            if (retryCount < maxRetries)
            {
                Debug.LogWarning($"Request failed, retrying ({retryCount + 1}/{maxRetries}): {request.error}");
                
                // Wait before retrying
                yield return new WaitForSeconds(retryDelay);
                
                // Retry the request
                yield return SendRequest(method, endpoint, data, callback, retryCount + 1);
            }
            else
            {
                Debug.LogError($"Request failed after {maxRetries} retries: {request.error}");
                
                // If all retries failed, update connection status
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    isConnected = false;
                    lastConnectionError = request.error;
                }
                
                callback?.Invoke(false, request.error);
            }
        }
        
        request.Dispose();
    }
    
    // Helper method to manually serialize Dictionary<string,object> to JSON
    private string SerializeDictionary(Dictionary<string, object> dict)
    {
        if (dict == null) return "{}";
        
        List<string> items = new List<string>();
        
        foreach (var kvp in dict)
        {
            string key = kvp.Key;
            object value = kvp.Value;
            string valueStr = SerializeValue(value);
            
            items.Add($"\"{key}\":{valueStr}");
        }
        
        return "{" + string.Join(",", items) + "}";
    }
    
    // Helper method to serialize various value types to JSON
    private string SerializeValue(object value)
    {
        if (value == null) return "null";
        
        // Handle common types
        if (value is string)
        {
            // Escape special characters in the string
            string str = (string)value;
            str = str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
            return $"\"{str}\"";
        }
        else if (value is bool)
        {
            return ((bool)value) ? "true" : "false";
        }
        else if (value is int || value is float || value is double)
        {
            return value.ToString();
        }
        else if (value is Dictionary<string, object>)
        {
            return SerializeDictionary(value as Dictionary<string, object>);
        }
        else if (value is List<object>)
        {
            return SerializeList((List<object>)value);
        }
        else if (value is object[])
        {
            return SerializeList(((object[])value).Cast<object>().ToList());
        }
        else if (value is List<Dictionary<string, object>>)
        {
            return SerializeListOfDicts(value as List<Dictionary<string, object>>);
        }
        else
        {
            // For other types, fall back to Unity's serializer
            try
            {
                return JsonUtility.ToJson(value);
            }
            catch
            {
                // If that fails, just return the string representation
                return $"\"{value}\"";
            }
        }
    }
    
    // Helper method to serialize a list to JSON
    private string SerializeList(List<object> list)
    {
        if (list == null) return "[]";
        
        List<string> items = new List<string>();
        foreach (var item in list)
        {
            items.Add(SerializeValue(item));
        }
        
        return "[" + string.Join(",", items) + "]";
    }
    
    // Helper method to serialize a list of dictionaries
    private string SerializeListOfDicts(List<Dictionary<string, object>> list)
    {
        if (list == null) return "[]";
        
        List<string> items = new List<string>();
        foreach (var dict in list)
        {
            items.Add(SerializeDictionary(dict));
        }
        
        return "[" + string.Join(",", items) + "]";
    }
    
    // Get feedback about the agent's current state and environment
    private string GetAgentFeedback(string agentId)
    {
        // Get the agent
        AgentController agent = null;
        if (trackedAgents.ContainsKey(agentId))
        {
            agent = trackedAgents[agentId];
        }
        else if (worldManager != null)
        {
            agent = worldManager.GetAgentById(agentId);
        }
        
        if (agent == null)
        {
            return $"Agent {agentId} is in an unknown location.";
        }
        
        // Get agent state
        var agentState = agent.GetAgentState();
        
        // Make sure location is present and valid
        string location = "home"; // Default fallback
        if (agentState.ContainsKey("location") && agentState["location"] != null && !string.IsNullOrEmpty(agentState["location"].ToString()))
        {
            location = agentState["location"].ToString();
        }
        
        string status = agentState.ContainsKey("status") ? agentState["status"].ToString() : "Idle";
        
        // Log the location for debugging
        Debug.Log($"Backend GetAgentFeedback using location: {location} for agent {agentId}");
        
        // Get nearby entities if environment reporter is available
        string nearbyInfo = "";
        if (environmentReporter != null)
        {
            var envState = environmentReporter.GetEnvironmentState(agentId);
            
            // Check if there are nearby agents
            if (envState.ContainsKey("agents") && envState["agents"] is List<Dictionary<string, object>> nearbyAgents && nearbyAgents.Count > 0)
            {
                nearbyInfo += "\nNearby agents: ";
                foreach (var nearbyAgent in nearbyAgents)
                {
                    if (nearbyAgent["id"].ToString() == agentId) continue; // Skip self
                    
                    // Skip Agent_Default entities
                    if (nearbyAgent["id"].ToString().Contains("Default")) continue;
                    
                    nearbyInfo += $"{nearbyAgent["id"]} ({nearbyAgent["status"]}), ";
                }
                // Remove trailing comma and space
                if (nearbyInfo.EndsWith(", "))
                {
                    nearbyInfo = nearbyInfo.Substring(0, nearbyInfo.Length - 2);
                }
            }
            
            // Check if there are nearby objects
            if (envState.ContainsKey("objects") && envState["objects"] is List<Dictionary<string, object>> nearbyObjects && nearbyObjects.Count > 0)
            {
                nearbyInfo += "\nNearby objects: ";
                foreach (var nearbyObj in nearbyObjects)
                {
                    nearbyInfo += $"{nearbyObj["name"]} ({nearbyObj["tag"]}), ";
                }
                // Remove trailing comma and space
                if (nearbyInfo.EndsWith(", "))
                {
                    nearbyInfo = nearbyInfo.Substring(0, nearbyInfo.Length - 2);
                }
            }
        }
        
        return $"Agent {agentId} is at {location} with status: {status}.{nearbyInfo}";
    }
}