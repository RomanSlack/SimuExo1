using UnityEngine;
using UnityEngine.AI;

public class Tool_Move : MonoBehaviour
{
    private NavMeshAgent agent;
    private Animator animator;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        // Ensure the agent is placed correctly on the NavMesh
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"Agent {gameObject.name} is NOT on the NavMesh! Attempting to Warp...");
            agent.Warp(transform.position);
        }

        agent.baseOffset = 0.5f; // Keep agent above ground

        animator.applyRootMotion = false;
        animator.SetBool("isWalking", false);
    }

    void Update()
    {
        if (!agent.enabled) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            agent.isStopped = true;
            animator.SetBool("isWalking", false);
        }
        else if (agent.velocity.sqrMagnitude > 0.01f)
        {
            agent.isStopped = false;
            animator.SetBool("isWalking", true);
        }
    }

    public void ExecuteMove(string destination)
    {
        Vector3 target = ConvertDestinationToCoordinates(destination);
        if (target != Vector3.zero)
        {
            if (!agent.isOnNavMesh)
            {
                Debug.LogError($"Agent {gameObject.name} is NOT on the NavMesh! Cannot move.");
                return;
            }

            // Force movement
            agent.isStopped = false;
            agent.Warp(agent.transform.position); // Ensures agent is correctly placed before move
            agent.SetDestination(target);

            Debug.Log($"Moving to {destination} => {target}");
        }
        else
        {
            Debug.LogWarning($"Unknown destination: {destination}");
        }
    }

    private Vector3 ConvertDestinationToCoordinates(string dest)
    {
        float offsetX = Random.Range(-8f, 8f);
        float offsetZ = Random.Range(-8f, 8f);

        switch (dest.ToUpper())
        {
            case "PARK":
                return new Vector3(350.47f + offsetX,  49.63f, 432.7607f + offsetZ);
            case "HOME":
                return new Vector3(324.3666f + offsetX , 50.33723f, 463.2347f + offsetZ);
            case "LIBRARY":
                return new Vector3(325.03f + offsetX , 50.29f, 407.87f + offsetZ);
            case "GYM":
                return new Vector3(300.5f + offsetX, 50.23723f, 420.8247f + offsetZ);
            default:
                return Vector3.zero;
        }
    }
}

