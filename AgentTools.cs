using UnityEngine;
using UnityEngine.AI;
using System.Linq;

public static class AgentTools
{
    /// <summary>
    /// Moves the given NavMeshAgent to a predefined destination based on the location string.
    /// If the location is not predefined, attempts to interpret it as an agent's name.
    /// </summary>
    public static void MoveToLocation(NavMeshAgent navMeshAgent, string location)
    {
        Vector3 destination;
        if (IsPredefinedLocation(location))
        {
            switch (location.ToLower())
            {
                case "park":
                    destination = new Vector3(350.47f, 49.63f, 432.7607f);
                    break;
                case "library":
                    destination = new Vector3(325.03f, 50.29f, 407.87f);
                    break;
                case "cantina":
                    destination = new Vector3(324.3666f, 50.33723f, 463.2347f);
                    break;
                case "gym":
                    destination = new Vector3(300.5f, 50.23723f, 420.8247f);
                    break;
                default:
                    destination = navMeshAgent.transform.position;
                    break;
            }
        }
        else
        {
            AgentBrain targetAgent = GetAgentInProximityByName(navMeshAgent.transform.position, location, 30f);
            if (targetAgent != null)
                destination = targetAgent.transform.position;
            else
                destination = navMeshAgent.transform.position;
        }
        navMeshAgent.SetDestination(destination);
    }

    /// <summary>
    /// Public method to check if the location string is one of the predefined spots.
    /// </summary>
    public static bool IsPredefinedLocation(string location)
    {
        string[] predefined = { "park", "library", "cantina", "gym" };
        return predefined.Contains(location.ToLower());
    }

    private static AgentBrain GetAgentInProximityByName(Vector3 currentPos, string agentName, float radius)
    {
        AgentBrain[] agents = UnityEngine.Object.FindObjectsOfType<AgentBrain>();
        foreach (var agent in agents)
        {
            if (agent.agentId.Equals(agentName, System.StringComparison.OrdinalIgnoreCase))
            {
                if (Vector3.Distance(currentPos, agent.transform.position) <= radius)
                    return agent;
            }
        }
        return null;
    }
}
