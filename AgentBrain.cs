using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

[Serializable]
public class RequestData
{
    public string agent_id;
    public string user_input;
    public string system_prompt;
}

public class AgentBrain : MonoBehaviour
{
    [SerializeField] private string agentId = "AgentA";
    [SerializeField] private string serverUrl = "http://127.0.0.1:3000/generate";
    [SerializeField] private NavMeshAgent navMeshAgent;

    // Editable mood field in the Inspector.
    [SerializeField, Tooltip("Set the agent's mood here.")]
    private string agentMood = "I feel like doing something interesting today.";

    // Fixed system prompt (non-editable) with instructions for MOVE or NOTHING.
    [SerializeField, Tooltip("System prompt for the AI. DO NOT MODIFY.")]
    private string systemPrompt = "You are a game agent. You have a mood (provided by the user).\n" +
        "You can choose to move to exactly one of these four locations: park, library, home, gym, or choose to do NOTHING.\n" +
        "Your response should contain some brief explanation in natural language, but MUST end with a line in one of the following forms:\n" +
        "MOVE: <location>\n" +
        "NOTHING: do nothing\n" +
        "Example:\n\"I feel like reading, so the library is best.\"\nMOVE: library";

    private static readonly HttpClient httpClient = new HttpClient();
    private bool isMoving = false;
    
    // Stores details of the last action.
    private string lastActionFeedback = "No action taken yet.";
    private string lastMoveLocation = "";

    void Start()
    {
        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        // On start, send an initial decision request.
        // (This is now manual: if you want to start automatically, you could call RequestDecision here.
        // For manual control, you can let WorldManager trigger the cycle.)
        Debug.Log($"Agent {agentId} started. Waiting for simulation cycle trigger...");
    }

    // RequestDecision now accepts a single input string.
    // This input should contain feedback (last action details + scan info).
    public void RequestDecision(string input)
    {
        string combinedInput = $"{input}\nMy current mood is: {agentMood}";
        Debug.Log($"Agent {agentId} sending decision request with input:\n{combinedInput}");
        StartCoroutine(SendToAI(combinedInput));
    }

    private IEnumerator SendToAI(string input)
    {
        RequestData requestData = new RequestData();
        requestData.agent_id = agentId;
        requestData.user_input = input;
        requestData.system_prompt = systemPrompt;

        string jsonString = JsonUtility.ToJson(requestData);
        Debug.Log($"Agent {agentId} Request JSON: {jsonString}");

        var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

        Task<HttpResponseMessage> postTask = httpClient.PostAsync(serverUrl, content);
        yield return new WaitUntil(() => postTask.IsCompleted);

        HttpResponseMessage response = postTask.Result;
        if (!response.IsSuccessStatusCode)
        {
            Debug.LogError($"Agent {agentId} Server error: {response.StatusCode}");
            yield break;
        }

        string responseJson = response.Content.ReadAsStringAsync().Result;
        GenerateResponse resp = JsonUtility.FromJson<GenerateResponse>(responseJson);

        Debug.Log($"Agent {agentId} | AI Output: {resp.text}");
        Debug.Log($"Agent {agentId} | Action: {resp.action}, Location: {resp.location}");

        // Process LLM response:
        switch (resp.action.ToLower())
        {
            case "move":
                lastMoveLocation = resp.location;
                isMoving = true;
                AgentTools.MoveToLocation(navMeshAgent, resp.location);
                break;
            case "nothing":
                lastActionFeedback = "Chose to do nothing.";
                break;
            default:
                lastActionFeedback = "Unknown action returned.";
                break;
        }
    }

    void Update()
    {
        // If currently moving, check if we've reached the destination.
        if (isMoving && !navMeshAgent.pathPending)
        {
            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude == 0f)
                {
                    isMoving = false;
                    lastActionFeedback = $"Used move tool to successfully move to {lastMoveLocation}.";
                    Debug.Log($"Agent {agentId} reached destination: {lastMoveLocation}");
                }
            }
        }
    }

    /// <summary>
    /// Gathers the last action feedback along with a scan of nearby agents.
    /// </summary>
    /// <returns>A string with the details of the last action and nearby agent IDs.</returns>
    public string GetFeedbackMessage()
    {
        // Find nearby agents (within 10 units radius).
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 30f);
        string nearbyAgents = "";
        foreach (var hitCollider in hitColliders)
        {
            AgentBrain otherAgent = hitCollider.GetComponent<AgentBrain>();
            if (otherAgent != null && otherAgent.agentId != this.agentId)
            {
                if (!string.IsNullOrEmpty(nearbyAgents))
                    nearbyAgents += ", ";
                nearbyAgents += otherAgent.agentId;
            }
        }
        if (string.IsNullOrEmpty(nearbyAgents))
            nearbyAgents = "none";

        string feedback = $"Last action: {lastActionFeedback}. Nearby agents: {nearbyAgents}.";
        Debug.Log($"Agent {agentId} Feedback: {feedback}");
        return feedback;
    }
}

[Serializable]
public class GenerateResponse
{
    public string agent_id;
    public string text;
    public string action;
    public string location;
}
