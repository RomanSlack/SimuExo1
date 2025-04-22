using UnityEngine;

/// <summary>
/// Utility script to fix UI issues in the scene at runtime.
/// Add this to any GameObject and it will automatically fix UI positions on Start.
/// </summary>
public class UIFixer : MonoBehaviour
{
    [SerializeField] public float uiHeight = 5.0f;
    [SerializeField] public bool fixOnStart = true;
    [SerializeField] public KeyCode fixHotkey = KeyCode.F1;
    
    void Start()
    {
        if (fixOnStart)
        {
            FixAllAgentUIs();
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
                // Force position update using the direct reference
                agent.uiOffset = new Vector3(0, uiHeight, 0);
                
                // Use reflection to force immediate update
                var container = agent.transform.Find("UI_Container");
                if (container != null)
                {
                    container.localPosition = new Vector3(0, uiHeight, 0);
                    fixed_count++;
                }
                
                // Call the public update method too
                agent.UpdateUIPosition();
            }
        }
        
        Debug.Log($"Fixed {fixed_count}/{agents.Length} agent UIs by setting height to {uiHeight}");
    }
}