using UnityEngine;
using System.Collections.Generic;

public class SimulationController : MonoBehaviour
{
    [Header("All Agents in the Scene")]
    public List<AgentBehavior> agents;

    void Update()
    {
        // Listen for SHIFT + X to trigger a prompt for all agents.
        if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            && Input.GetKeyDown(KeyCode.X))
        {
            foreach (var agent in agents)
            {
                if (agent != null)
                {
                    agent.RequestActionFromLLM();
                }
            }
        }
    }
}
