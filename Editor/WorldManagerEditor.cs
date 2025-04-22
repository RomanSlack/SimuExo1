using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(WorldManager))]
public class WorldManagerEditor : Editor
{
    private WorldManager worldManager;
    private int agentCountToAdd = 1;
    private int agentCountToRemove = 1;
    private int targetAgentCount = 2;

    private void OnEnable()
    {
        worldManager = (WorldManager)target;
    }

    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

        // Only enable these controls in play mode
        GUI.enabled = Application.isPlaying;

        EditorGUILayout.BeginHorizontal();
        agentCountToAdd = EditorGUILayout.IntField("Add Agents", agentCountToAdd);
        if (GUILayout.Button("Add", GUILayout.Width(60)))
        {
            worldManager.AddAgents(agentCountToAdd);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        agentCountToRemove = EditorGUILayout.IntField("Remove Agents", agentCountToRemove);
        if (GUILayout.Button("Remove", GUILayout.Width(60)))
        {
            worldManager.RemoveAgents(agentCountToRemove);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        targetAgentCount = EditorGUILayout.IntField("Set Agent Count", targetAgentCount);
        if (GUILayout.Button("Set", GUILayout.Width(60)))
        {
            worldManager.SetAgentCount(targetAgentCount);
        }
        EditorGUILayout.EndHorizontal();

        GUI.enabled = true;
    }
}