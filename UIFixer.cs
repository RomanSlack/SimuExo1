using UnityEngine;

/// <summary>
/// Utility script to fix UI issues in the scene at runtime.
/// Add this to any GameObject and it will automatically fix UI positions on Start.
/// </summary>
public class UIFixer : MonoBehaviour
{
    [SerializeField] public float uiHeight = 10.0f; // Increased default height
    [SerializeField] public bool fixOnStart = false; // Disabled by default to respect prefab settings
    [SerializeField] public KeyCode fixHotkey = KeyCode.F1;
    [SerializeField] public bool respectPrefabSettings = true; // New option to respect prefab settings
    
    void Start()
    {
        // Don't automatically fix on start
        if (fixOnStart)
        {
            FixAllAgentUIs();
        }
        else
        {
            Debug.Log("UIFixer is present but set to not fix on start (respecting prefab settings)");
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(fixHotkey))
        {
            FixAllAgentUIs();
        }
    }
    
    [ContextMenu("Fix All Agent UIs")]
    public void FixAllAgentUIs()
    {
        var agents = GameObject.FindObjectsOfType<AgentUI>();
        int fixed_count = 0;
        
        foreach (var agent in agents)
        {
            if (agent != null)
            {
                // Log the current position before changing
                Debug.Log($"Agent {agent.agentId} UI before fix: height = {agent.uiOffset.y}");
                
                if (!respectPrefabSettings)
                {
                    // Force position update using the direct reference
                    agent.uiOffset = new Vector3(0, uiHeight, 0);
                    
                    // Update the container directly
                    var container = agent.transform.Find("UI_Container");
                    if (container != null)
                    {
                        container.localPosition = new Vector3(0, uiHeight, 0);
                        fixed_count++;
                    }
                    
                    // Call the public update method too
                    agent.UpdateUIPosition();
                    
                    Debug.Log($"Forced Agent {agent.agentId} UI height to {uiHeight}");
                }
                else
                {
                    // Just call UpdateUIPosition to make sure everything is properly positioned
                    // but don't change the height - respect what's in the prefab
                    agent.UpdateUIPosition();
                    Debug.Log($"Respected Agent {agent.agentId} UI prefab settings with height {agent.uiOffset.y}");
                    fixed_count++;
                }
            }
        }
        
        if (respectPrefabSettings)
        {
            Debug.Log($"Updated {fixed_count}/{agents.Length} agent UIs while respecting prefab settings");
        }
        else
        {
            Debug.Log($"Fixed {fixed_count}/{agents.Length} agent UIs by setting height to {uiHeight}");
        }
    }
}