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

    [SerializeField, Tooltip("Agent's personality inserted into the system prompt on first request.")]
    private string personality = "You are friendly, logical, and collaborative.";

    [TextArea(8, 15)]
    [SerializeField, Tooltip("System prompt template (use [PERSONALITY_HERE] placeholder).")]
    private string systemPromptTemplate = @"
You are an autonomous game agent.
PRIMARY GOAL: Collaborate with other agents to find and fix the broken O2 regulator on this Mars base.

ACTIONS:
1) MOVE: <location_or_agent>
   Valid locations: park, library, gym, cantina.
2) NOTHING: do nothing
3) CONVERSE: <agent_name>
   Engage in conversation with the specified agent.

REQUIREMENTS:
- Provide at least one short paragraph of reasoning.
- The last line must start with MOVE:, NOTHING:, or CONVERSE: with no other text.

EXAMPLES:
Example MOVE:
I think the library has info on the O2 regulator specs.
MOVE: library

Example NOTHING:
No new info, so I'll wait here.
NOTHING: do nothing

Example CONVERSE:
Agent_2 might have more details, I'd like to chat with them.
CONVERSE: Agent_2

Personality: [PERSONALITY_HERE]
";

    private bool firstRequest = true;
    private string lastActionFeedback = "No action taken yet.";
    private string lastMoveLocation = "";
    private bool isMoving = false;
    private bool inConversation = false;
    private string converseTarget = "";
    private int converseRounds = 0;

    private static readonly HttpClient httpClient = new HttpClient();

    void Start()
    {
        if (navMeshAgent == null) navMeshAgent = GetComponent<NavMeshAgent>();
        Debug.Log($"{agentId} started. Ready for simulation steps...");
    }

    private string BuildSystemPrompt()
    {
        return systemPromptTemplate.Replace("[PERSONALITY_HERE]", personality);
    }

    public void RequestDecision(string feedbackInput)
    {
        // Send system prompt only first time
        string sp = firstRequest ? BuildSystemPrompt() : "";
        firstRequest = false;

        var reqData = new RequestData
        {
            agent_id = agentId,
            user_input = feedbackInput,
            system_prompt = sp
        };
        StartCoroutine(SendToAI(reqData));
    }

    private IEnumerator SendToAI(RequestData reqData)
    {
        string jsonStr = JsonUtility.ToJson(reqData);
        Debug.Log($"{agentId} POSTing: {jsonStr}");

        var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        var postTask = httpClient.PostAsync(serverUrl, content);
        yield return new WaitUntil(() => postTask.IsCompleted);

        if (!postTask.Result.IsSuccessStatusCode)
        {
            Debug.LogError($"{agentId} server error: {postTask.Result.StatusCode}");
            yield break;
        }

        string respJson = postTask.Result.Content.ReadAsStringAsync().Result;
        var resp = JsonUtility.FromJson<GenerateResponse>(respJson);

        Debug.Log($"{agentId} AI Output: {resp.text}");
        Debug.Log($"{agentId} Action: {resp.action}, Location: {resp.location}");

        // Extract reasoning from all but last line
        var lines = resp.text.Split('\n');
        if (lines.Length > 1)
        {
            string reasoning = string.Join("\n", lines.Take(lines.Length - 1));
            Debug.Log($"{agentId} Reasoning: {reasoning}");
        }

        switch (resp.action.ToLower())
        {
            case "move":
                HandleMove(resp.location);
                break;
            case "nothing":
                lastActionFeedback = "Chose to do nothing.";
                break;
            case "converse":
                HandleConverse(resp.location);
                break;
            default:
                lastActionFeedback = "Unknown action.";
                break;
        }

        // Decrement conversation rounds if in conversation
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

    private void HandleMove(string location)
    {
        if (!AgentTools.IsPredefinedLocation(location))
        {
            // Possibly moving to an agent
            var target = FindAgent(location);
            if (target != null)
            {
                lastMoveLocation = $"agent {location}";
                isMoving = true;
                navMeshAgent.SetDestination(target.transform.position);
                Debug.Log($"{agentId} moving toward agent {location} at {target.transform.position}.");
            }
            else
            {
                lastActionFeedback = $"Move failed: no agent named {location}.";
            }
        }
        else
        {
            lastMoveLocation = location;
            isMoving = true;
            AgentTools.MoveToLocation(navMeshAgent, location);
        }
    }

    private void HandleConverse(string location)
    {
        if (inConversation && converseTarget.Equals(location, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"{agentId} already conversing with {location}.");
            return;
        }
        var target = FindAgent(location);
        if (target != null)
        {
            inConversation = true;
            converseTarget = location;
            converseRounds = 3; // or 4, your choice
            lastActionFeedback = $"Initiated conversation with {location}.";
            Debug.Log($"{agentId} in conversation mode with {location} for {converseRounds} rounds.");
        }
        else
        {
            lastActionFeedback = $"Converse failed: no agent named {location}.";
        }
    }

    private AgentBrain FindAgent(string targetName)
    {
        var allAgents = FindObjectsOfType<AgentBrain>();
        return allAgents.FirstOrDefault(a => a.agentId.Equals(targetName, StringComparison.OrdinalIgnoreCase));
    }

    void Update()
    {
        if (isMoving && !navMeshAgent.pathPending)
        {
            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude < 0.01f)
                {
                    isMoving = false;
                    lastActionFeedback = $"Used move tool to arrive at {lastMoveLocation}.";
                    Debug.Log($"{agentId} arrived at {lastMoveLocation}.");
                }
            }
        }
    }

    public string GetFeedbackMessage()
    {
        // Find nearby agents
        var hits = Physics.OverlapSphere(transform.position, 50f);
        var neighborInfo = "";
        foreach (var h in hits)
        {
            var other = h.GetComponent<AgentBrain>();
            if (other && other.agentId != agentId)
            {
                Vector3 pos = other.transform.position;
                if (!string.IsNullOrEmpty(neighborInfo)) neighborInfo += "; ";
                neighborInfo += $"{other.agentId} ({pos.x:F1},{pos.y:F1},{pos.z:F1})";
            }
        }
        if (string.IsNullOrEmpty(neighborInfo)) neighborInfo = "none";

        string feedback = inConversation
            ? $"[CONVERSE mode with {converseTarget}, rounds left: {converseRounds}]"
            : $"Last action: {lastActionFeedback}. Nearby: {neighborInfo}.";
        Debug.Log($"{agentId} feedback: {feedback}");
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
