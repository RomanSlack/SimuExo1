using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

[Serializable]
public class RequestData
{
    public string agent_id;
    public string user_input;
    public string system_prompt;
}

public class AgentBrain : MonoBehaviour
{
    [SerializeField] public string agentId = "AgentA";
    [SerializeField] private string serverUrl = "http://127.0.0.1:3000/generate";
    [SerializeField] private NavMeshAgent navMeshAgent;

    [SerializeField, Tooltip("Set the agent's personality. This will be injected into its system prompt.")]
    public string personality = "You are assertive, friendly, and eager to collaborate to find the missing O2 regulator.";

    // The system prompt now references the missing O2 regulator more explicitly
    // This portion will be appended to the final system_prompt on the Python side if desired,
    // or used stand-alone here if you prefer. 
    private readonly string centralSystemPrompt = @"
You are an autonomous game agent with the following personality:
[PERSONALITY_HERE]

PRIMARY GOAL: Collaborate with any other agents to locate the missing O2 regulator on this Mars base.
You can:
1) MOVE to 'park', 'library', 'o2_Regulator_Room', 'gym', or move toward another agent by naming them.
2) NOTHING: do nothing.
3) CONVERSE: have a multi-round chat with a specific agent by naming them.

IMPORTANT RULES:
- Provide at least one sentence of reasoning in your response.
- The final line MUST begin with one of: MOVE:, NOTHING:, CONVERSE:
- If you fail to provide reasoning or break the final-line rule, your response is invalid.
";

    // Variables to track last action feedback, movement, conversation state, etc.
    private string lastActionFeedback = "No action taken yet.";
    private string lastMoveLocation = "";

    private bool isMoving = false;
    private bool inConversation = false;
    private string converseTarget = "";
    private int converseRounds = 0;

    private static readonly HttpClient httpClient = new HttpClient();

    void Start()
    {
        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        Debug.Log($"Agent {agentId} started. Waiting for simulation cycle trigger...");
    }

    // Build final system prompt by combining the central prompt with the agent's personality.
    private string BuildSystemPrompt()
    {
        // Insert the personality text where [PERSONALITY_HERE] is.
        return centralSystemPrompt.Replace("[PERSONALITY_HERE]", personality);
    }

    public void RequestDecision(string input)
    {
        Debug.Log($"Agent {agentId} sending decision request with input:\n{input}");
        StartCoroutine(SendToAI(input));
    }

    private IEnumerator SendToAI(string input)
    {
        RequestData requestData = new RequestData
        {
            agent_id = agentId,
            user_input = input,
            system_prompt = BuildSystemPrompt()
        };

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

        // Extract reasoning lines (all but final line)
        string[] lines = resp.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 1)
        {
            string reasoning = string.Join("\n", lines.Take(lines.Length - 1));
            Debug.Log($"Agent {agentId} Reasoning: {reasoning}");
        }
        else
        {
            Debug.Log($"Agent {agentId} Reasoning: (none provided)");
        }

        // Process the final action
        switch (resp.action.ToLower())
        {
            case "move":
                // If not a predefined location, check if it's an agent
                if (!IsPredefinedLocation(resp.location))
                {
                    AgentBrain targetAgent = GetAgentInProximityByName(resp.location);
                    if (targetAgent != null)
                    {
                        // Move to agent
                        lastMoveLocation = $"agent {resp.location}";
                        isMoving = true;
                        navMeshAgent.SetDestination(targetAgent.transform.position);
                        Debug.Log($"Agent {agentId} moving toward agent {resp.location} at {targetAgent.transform.position}.");
                    }
                    else
                    {
                        lastActionFeedback = $"Move failed: no agent named {resp.location} nearby.";
                    }
                }
                else
                {
                    // Move to a predefined location
                    lastMoveLocation = resp.location;
                    isMoving = true;
                    AgentTools.MoveToLocation(navMeshAgent, resp.location);
                }
                break;

            case "nothing":
                lastActionFeedback = "Chose to do nothing.";
                break;

            case "converse":
                // If already in conversation, skip re-initialization
                if (inConversation && converseTarget.Equals(resp.location, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"Agent {agentId} is already conversing with {converseTarget}. Ignoring re-init.");
                }
                else
                {
                    AgentBrain converseAgent = GetAgentInProximityByName(resp.location);
                    if (converseAgent != null)
                    {
                        inConversation = true;
                        converseTarget = resp.location;
                        converseRounds = 4; // 4-step conversation
                        lastActionFeedback = $"Initiated conversation with {resp.location}.";
                        Debug.Log($"Agent {agentId} entering conversation mode with {resp.location} for {converseRounds} rounds.");
                    }
                    else
                    {
                        lastActionFeedback = $"Converse failed: no agent named {resp.location} nearby.";
                    }
                }
                break;

            default:
                lastActionFeedback = "Unknown action returned.";
                break;
        }

        // If we're in conversation mode, decrement round count each time
        if (inConversation)
        {
            converseRounds--;
            if (converseRounds <= 0)
            {
                inConversation = false;
                lastActionFeedback = $"Conversation ended with {converseTarget}.";
                converseTarget = "";
            }
        }
    }

    void Update()
    {
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
    /// Builds feedback about the last action plus a scan of nearby agents.
    /// </summary>
    public string GetFeedbackMessage()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 30f);
        string nearbyInfo = "";
        foreach (var hitCollider in hitColliders)
        {
            AgentBrain otherAgent = hitCollider.GetComponent<AgentBrain>();
            if (otherAgent != null && otherAgent.agentId != this.agentId)
            {
                Vector3 pos = otherAgent.transform.position;
                if (!string.IsNullOrEmpty(nearbyInfo))
                    nearbyInfo += "; ";
                nearbyInfo += $"{otherAgent.agentId} ({pos.x:F1},{pos.y:F1},{pos.z:F1})";
            }
        }
        if (string.IsNullOrEmpty(nearbyInfo))
            nearbyInfo = "none";

        string feedback;
        if (inConversation)
            feedback = $"[CONVERSE mode with {converseTarget}, rounds remaining: {converseRounds}]";
        else
            feedback = $"Last action: {lastActionFeedback}. Nearby agents: {nearbyInfo}.";

        Debug.Log($"Agent {agentId} Feedback: {feedback}");
        return feedback;
    }

    private bool IsPredefinedLocation(string location)
    {
        string[] predefined = { "park", "library", "02_Regulator_Room", "gym" };
        return predefined.Any(loc => loc.Equals(location, StringComparison.OrdinalIgnoreCase));
    }

    private AgentBrain GetAgentInProximityByName(string targetName)
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 30f);
        foreach (var hitCollider in hitColliders)
        {
            AgentBrain otherAgent = hitCollider.GetComponent<AgentBrain>();
            if (otherAgent != null && otherAgent.agentId.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                return otherAgent;
            }
        }
        return null;
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
