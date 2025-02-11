using UnityEngine;
using UnityEngine.AI;

public class NavMeshDebug : MonoBehaviour
{
    public Vector3 testPosition = new Vector3(6.31f, -2.26f, 27.01f);
    public float testRadius = 5f; // Try increasing if needed

    void Start()
    {
        NavMeshHit hit;
        if (NavMesh.SamplePosition(testPosition, out hit, testRadius, NavMesh.AllAreas))
        {
            Debug.Log($"Valid NavMesh point found at: {hit.position}");
        }
        else
        {
            Debug.LogError($"No NavMesh point found near {testPosition}. Increase testRadius or check your NavMesh.");
        }
    }
}
