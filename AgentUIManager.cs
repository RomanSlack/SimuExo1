using UnityEngine;

/// <summary>
/// Runtime manager for updating all AgentUI components at once.
/// Add this to the SimulationManager GameObject.
/// </summary>
public class AgentUIManager : MonoBehaviour
{
    [SerializeField] private float globalUIHeight = 10.0f;
    [SerializeField] private bool updateOnStart = true;
    
    void Start()
    {
        if (updateOnStart)
        {
            UpdateAllAgentUIHeights();
        }
    }
    
    [ContextMenu("Update All Agent UI Heights")]
    public void UpdateAllAgentUIHeights()
    {
        AgentUI[] allAgentUIs = GameObject.FindObjectsOfType<AgentUI>();
        foreach (AgentUI ui in allAgentUIs)
        {
            if (ui != null)
            {
                ui.SetUIHeight(globalUIHeight);
            }
        }
        
        Debug.Log($"Updated {allAgentUIs.Length} agent UIs to height {globalUIHeight}");
    }
    
    // This can be called at runtime to adjust all UIs
    public void SetGlobalUIHeight(float height)
    {
        globalUIHeight = height;
        UpdateAllAgentUIHeights();
    }
}