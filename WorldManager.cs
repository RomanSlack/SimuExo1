using UnityEngine;

public class WorldManager : MonoBehaviour
{
    private AgentBrain[] allAgents;

    void Start()
    {
        // Collect all AgentBrain scripts in the scene.
        allAgents = FindObjectsOfType<AgentBrain>();
        Debug.Log($"WorldManager: Found {allAgents.Length} agents.");
    }

    void Update()
    {
        // Check if Shift + X is pressed to manually trigger a simulation cycle.
        if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) 
            && Input.GetKeyDown(KeyCode.X))
        {
            RunSimulationCycle();
        }
    }

    private void RunSimulationCycle()
    {
        Debug.Log("WorldManager: Running one simulation cycle...");

        foreach (var agent in allAgents)
        {
            // For each agent, get its feedback message (last action and scan of nearby agents)
            // and use that as the input for the next decision.
            string feedback = agent.GetFeedbackMessage();
            agent.RequestDecision(feedback);
        }
    }
}
