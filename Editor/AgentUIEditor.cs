using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AgentUI))]
public class AgentUIEditor : Editor
{
    private float newHeight = 10.0f;

    public override void OnInspectorGUI()
    {
        AgentUI agentUI = (AgentUI)target;
        
        // Draw the default inspector
        DrawDefaultInspector();
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Height Adjustment", EditorStyles.boldLabel);
        
        newHeight = EditorGUILayout.Slider("UI Height", newHeight, 0f, 20f);
        
        if (GUILayout.Button("Apply Height"))
        {
            // Apply to this UI
            agentUI.SetUIHeight(newHeight);
            
            // Mark scene as dirty
            EditorUtility.SetDirty(agentUI);
        }
        
        if (GUILayout.Button("Apply To All Agents"))
        {
            // Find all agent UIs in the scene
            AgentUI[] allAgentUIs = GameObject.FindObjectsOfType<AgentUI>();
            foreach (AgentUI ui in allAgentUIs)
            {
                ui.SetUIHeight(newHeight);
                EditorUtility.SetDirty(ui);
            }
            
            Debug.Log($"Updated {allAgentUIs.Length} agent UIs to height {newHeight}");
        }
    }
}

// Runtime Agent UI Manager - can be added to any GameObject
public class AgentUIManager : MonoBehaviour
{
    [SerializeField] private float globalUIHeight = 10.0f;
    
    [ContextMenu("Update All Agent UI Heights")]
    public void UpdateAllAgentUIHeights()
    {
        AgentUI[] allAgentUIs = GameObject.FindObjectsOfType<AgentUI>();
        foreach (AgentUI ui in allAgentUIs)
        {
            ui.SetUIHeight(globalUIHeight);
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