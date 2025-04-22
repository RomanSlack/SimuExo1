using UnityEngine;

public class WorldManager : MonoBehaviour
{
    private AgentBrain[] allAgents;

    void Start()
    {
        allAgents = FindObjectsOfType<AgentBrain>();
        Debug.Log($"WorldManager: Found {allAgents.Length} agents.");
    }

    void Update()
    {
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
