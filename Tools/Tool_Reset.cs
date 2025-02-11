using UnityEngine;

public class Tool_Reset : MonoBehaviour
{
    public void ExecuteReset(string reason)
    {
        Debug.LogWarning($"[{name}] Reset called: {reason}");
        // Potentially stop movement, revert to idle, clear partial states, etc.
        GetComponent<AgentBehavior>()?.AppendToDialogue("Reset: " + reason);
    }
}
