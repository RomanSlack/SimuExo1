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
            string feedback = agent.GetFeedbackMessage();
            agent.RequestDecision(feedback);
        }
    }
}
