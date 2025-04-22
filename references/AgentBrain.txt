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
    public string task; // New Task field
}

public class AgentBrain : MonoBehaviour
{
    [SerializeField] public string agentId = "AgentA";
    [SerializeField] private string serverUrl = "http://127.0.0.1:3000/generate";
    [SerializeField] private NavMeshAgent navMeshAgent;

    // Modular personality set per agent via Inspector.
    [SerializeField, Tooltip("Set the agent's personality. It will be injected into the system prompt on first request.")]
    private string personality = "You are friendly, logical, and collaborative.";

    // Current Task editable via Inspector.
    [SerializeField, Tooltip("Set the agent's current task.")]
    private string task = "Investigate the O2 regulator issue.";

    // System prompt template with placeholders for personality and task.
    [TextArea(8, 15)]
    [SerializeField, Tooltip("System prompt template. Use [PERSONALITY_HERE] and [TASK_HERE] placeholders.")]
    private string systemPromptTemplate = @"
You are an autonomous game agent.
PRIMARY GOAL: Collaborate with other agents to find and fix the broken O2 regulator on this Mars base.
Current Task: [TASK_HERE]

ACTIONS:
1) MOVE: <location_or_agent>
   Valid locations: park, library, gym, cantina.
   You may also move toward another agent by naming them.
2) NOTHING: do nothing.
3) CONVERSE: <agent_name>
   Engage in conversation with the specified agent.

REQUIREMENTS:
- Provide at least one short paragraph of reasoning.
- The very last line of your response must begin exactly with MOVE:, NOTHING:, or CONVERSE: (with no extra text).

EXAMPLES:
Example MOVE:
I think the library might have documents on the O2 regulator.
MOVE: library

Example NOTHING:
No new clues; I will stay put.
NOTHING: do nothing

Example CONVERSE:
I see Agent_2 nearby and believe they could have valuable insights.
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
        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();
        Debug.Log($"{agentId} started. Ready for simulation steps...");
    }

    // Build the final system prompt by injecting personality and task.
    private string BuildSystemPrompt()
    {
        return systemPromptTemplate.Replace("[PERSONALITY_HERE]", personality)
                                   .Replace("[TASK_HERE]", task);
    }

    // Request a decision; send the full system prompt only on the first request.
    public void RequestDecision(string feedbackInput)
    {
        string sp = firstRequest ? BuildSystemPrompt() : "";
        firstRequest = false;
        Debug.Log($"{agentId} sending decision request with input:\n{feedbackInput}");
        var reqData = new RequestData
        {
            agent_id = agentId,
            user_input = feedbackInput,
            system_prompt = sp,
            task = task
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

        // Log reasoning (all lines except final one).
        var lines = resp.text.Split('\n');
        if (lines.Length > 1)
        {
            string reasoning = string.Join("\n", lines.Take(lines.Length - 1));
            Debug.Log($"{agentId} Reasoning: {reasoning}");
        }
        else
        {
            Debug.Log($"{agentId} Reasoning: (none provided)");
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
            Debug.Log($"{agentId} is already conversing with {converseTarget}. Ignoring re-init.");
            return;
        }
        var target = FindAgent(location);
        if (target != null)
        {
            inConversation = true;
            converseTarget = location;
            converseRounds = 3; // Set conversation rounds (can be adjusted)
            lastActionFeedback = $"Initiated conversation with {location}.";
            Debug.Log($"{agentId} entering conversation mode with {location} for {converseRounds} rounds.");
            // Forward a conversation message to the target agent.
            string fwdMsg = $"[Conversation from {agentId}]: {lastActionFeedback} CONVERSE: {agentId}";
            target.ReceiveConversationMessage(fwdMsg);
        }
        else
        {
            lastActionFeedback = $"Converse failed: no agent named {location} nearby.";
        }
    }

    // Called when a conversation message is received from another agent.
    public void ReceiveConversationMessage(string message)
    {
        Debug.Log($"{agentId} received conversation message: {message}");
        // In production, you might auto-trigger a decision or update a chat UI here.
    }

    private AgentBrain FindAgent(string targetName)
    {
        var all = FindObjectsOfType<AgentBrain>();
        return all.FirstOrDefault(a => a.agentId.Equals(targetName, StringComparison.OrdinalIgnoreCase));
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
        var hits = Physics.OverlapSphere(transform.position, 50f);
        var info = "";
        foreach (var h in hits)
        {
            var other = h.GetComponent<AgentBrain>();
            if (other != null && other.agentId != agentId)
            {
                Vector3 pos = other.transform.position;
                if (!string.IsNullOrEmpty(info)) info += "; ";
                info += $"{other.agentId} ({pos.x:F1},{pos.y:F1},{pos.z:F1})";
            }
        }
        if (string.IsNullOrEmpty(info)) info = "none";
        string feedback = inConversation 
            ? $"[CONVERSE mode with {converseTarget}, rounds left: {converseRounds}]" 
            : $"Last action: {lastActionFeedback}. Nearby: {info}.";
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
