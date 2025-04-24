using UnityEngine;

/// <summary>
/// Runtime manager for updating all AgentUI components at once.
/// Add this to the SimulationManager GameObject.
/// </summary>
public class AgentUIManager : MonoBehaviour
{
    [SerializeField] private float globalUIHeight = 10.0f;
    [SerializeField] private bool updateOnStart = false; // Disabled by default to respect prefab settings
    [SerializeField] private bool respectPrefabSettings = true; // Added option to respect prefab settings
    
    void Start()
    {
        if (updateOnStart)
        {
            if (respectPrefabSettings)
            {
                RefreshAllAgentUIs();
            }
            else
            {
                UpdateAllAgentUIHeights();
            }
        }
        else
        {
            Debug.Log("AgentUIManager is present but set to not update on start (respecting prefab settings)");
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
                // Log current height before change
                Debug.Log($"Agent {ui.agentId} UI height before: {ui.uiOffset.y}");
                
                // Update the height
                ui.SetUIHeight(globalUIHeight);
            }
        }
        
        Debug.Log($"Updated {allAgentUIs.Length} agent UIs to height {globalUIHeight}");
    }
    
    [ContextMenu("Refresh Agent UIs Without Changing Height")]
    public void RefreshAllAgentUIs()
    {
        AgentUI[] allAgentUIs = GameObject.FindObjectsOfType<AgentUI>();
        foreach (AgentUI ui in allAgentUIs)
        {
            if (ui != null)
            {
                // Just call UpdateUIPosition to ensure proper positioning without changing height
                ui.UpdateUIPosition();
            }
        }
        
        Debug.Log($"Refreshed {allAgentUIs.Length} agent UIs while respecting original heights");
    }
    
    // This can be called at runtime to adjust all UIs
    public void SetGlobalUIHeight(float height)
    {
        globalUIHeight = height;
        UpdateAllAgentUIHeights();
    }
}