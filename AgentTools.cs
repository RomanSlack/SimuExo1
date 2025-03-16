using UnityEngine;
using UnityEngine.AI;

public static class AgentTools
{
    /// <summary>
    /// Moves the given NavMeshAgent to a predefined destination based on the location string.
    /// </summary>
    public static void MoveToLocation(NavMeshAgent navMeshAgent, string location)
    {
        Vector3 destination;
        switch (location.ToLower())
        {
            case "park":
                destination = new Vector3(350.47f, 49.63f, 432.7607f);
                break;
            case "library":
                destination = new Vector3(325.03f, 50.29f, 407.87f);
                break;
            case "home":
                destination = new Vector3(324.3666f, 50.33723f, 463.2347f);
                break;
            case "gym":
                destination = new Vector3(300.5f, 50.23723f, 420.8247f);
                break;
            default:
                destination = navMeshAgent.transform.position;
                break;
        }
        navMeshAgent.SetDestination(destination);
    }
}
