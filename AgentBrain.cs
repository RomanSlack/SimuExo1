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

    // Modular personality: set per agent in the Inspector.
    [SerializeField, Tooltip("Set the agent's personality. It will be injected into the system prompt.")]
    private string personality = "You are friendly, logical, and collaborative.";

    // System prompt template with a placeholder for personality.
    // This contains all instructions, available actions, and concise examples.
    [TextArea(8, 15)]
    [SerializeField, Tooltip("Base system prompt template. Use [PERSONALITY_HERE] as a placeholder for the agent's personality.")]
    private string systemPromptTemplate = @"
You are an autonomous game agent.
PRIMARY GOAL: Collaborate with other agents to locate the missing O2 regulator on this Mars base.

ACTIONS:
1) MOVE: <location_or_agent>
   Valid locations: park, library, gym, o2_regulator_room.
   You can also move toward another agent by naming them.
2) NOTHING: do nothing.
3) CONVERSE: <agent_name>
   Engage in conversation with another agent.

REQUIREMENTS:
- Provide at least one short paragraph of reasoning.
- The VERY LAST line of your response must start exactly with MOVE:, NOTHING:, or CONVERSE: 
  and contain no additional text.

EXAMPLES:
Example MOVE:
I'm heading to the library because there might be documents on the O2 regulator.
MOVE: library

Example NOTHING:
I see no clues at the moment, so I'll stay put.
NOTHING: do nothing

Example CONVERSE:
I notice Agent_1 nearby and think they could have useful insights. Let's chat.
CONVERSE: Agent_1

Personality: [PERSONALITY_HERE]
";

    // Agent state variables.
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

    // Build final system prompt by replacing the personality placeholder.
    private string BuildSystemPrompt()
    {
        return systemPromptTemplate.Replace("[PERSONALITY_HERE]", personality);
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

        // Extract and log reasoning (all lines except the final line)
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

        // Process the final action.
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
                lastActionFeedback = "Unknown action returned.";
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
            AgentBrain targetAgent = GetAgentInProximityByName(location);
            if (targetAgent != null)
            {
                lastMoveLocation = $"agent {location}";
                isMoving = true;
                navMeshAgent.SetDestination(targetAgent.transform.position);
                Debug.Log($"Agent {agentId} moving toward agent {location} at {targetAgent.transform.position}.");
            }
            else
            {
                lastActionFeedback = $"Move failed: no agent named {location} nearby.";
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
            Debug.Log($"Agent {agentId} is already conversing with {converseTarget}. Ignoring re-init.");
            return;
        }
        AgentBrain target = GetAgentInProximityByName(location);
        if (target != null)
        {
            inConversation = true;
            converseTarget = location;
            converseRounds = 4;
            lastActionFeedback = $"Initiated conversation with {location}.";
            Debug.Log($"Agent {agentId} entering conversation mode with {location} for {converseRounds} rounds.");
        }
        else
        {
            lastActionFeedback = $"Converse failed: no agent named {location} nearby.";
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

    public string GetFeedbackMessage()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 1000f);
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

        string feedback = inConversation ?
            $"[CONVERSE mode with {converseTarget}, rounds remaining: {converseRounds}]" :
            $"Last action: {lastActionFeedback}. Nearby agents: {nearbyInfo}.";

        Debug.Log($"Agent {agentId} Feedback: {feedback}");
        return feedback;
    }

    private AgentBrain GetAgentInProximityByName(string targetName)
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, 1000f);
        foreach (var hitCollider in hitColliders)
        {
            AgentBrain other = hitCollider.GetComponent<AgentBrain>();
            if (other != null && other.agentId.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                return other;
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
