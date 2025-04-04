using UnityEngine;

public class WorldManager : MonoBehaviour
{
    private AgentBrain[] allAgents;

    void Start()
    {
        // Find all agents in the scene.
        allAgents = FindObjectsOfType<AgentBrain>();
        Debug.Log($"WorldManager: Found {allAgents.Length} agents.");
    }

    void Update()
    {
        // Press Shift+X to trigger a simulation cycle.
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
            string feedback = agent.GetFeedbackMessage();
            agent.RequestDecision(feedback);
        }
    }
}
