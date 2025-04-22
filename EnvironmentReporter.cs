using UnityEngine;
using UnityEditor;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using System;

public class EnvironmentReporter : MonoBehaviour
{
    [Header("Detection Settings")]
    [SerializeField] private float defaultDetectionRadius = 20f;
    [SerializeField] private float agentDetectionRadius = 30f;
    [SerializeField] private float objectDetectionRadius = 15f;
    [SerializeField] private float fieldOfViewAngle = 120f;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private LayerMask detectionLayerMask;
    [SerializeField] private LayerMask obstructionLayerMask;
    
    [Header("Object Tags")]
    [SerializeField] private List<string> interactableObjectTags = new List<string> { 
        "Untagged", "Respawn", "Finish", "EditorOnly", "Player"
    }; // Using standard Unity tags as defaults
    
    [Header("Debug")]
    [SerializeField] private bool showDebugVisualization = true;
    [SerializeField] private bool logDetectionEvents = false;
    
    private Dictionary<string, AgentController> cachedAgents = new Dictionary<string, AgentController>();
    private Dictionary<string, Transform> cachedObjects = new Dictionary<string, Transform>();
    private float lastCacheRefreshTime;
    private float cacheRefreshInterval = 5f;
    
    void Awake()
    {
        if (detectionLayerMask == 0)
        {
            detectionLayerMask = ~0; // All layers if not specified
        }
        
        if (obstructionLayerMask == 0)
        {
            // By default, use everything except agents and ignored layers
            obstructionLayerMask = ~(LayerMask.GetMask("Agent") | LayerMask.GetMask("Ignore Raycast"));
        }
    }
    
    void Update()
    {
        // Refresh cache periodically
        if (Time.time - lastCacheRefreshTime > cacheRefreshInterval)
        {
            RefreshCache();
            lastCacheRefreshTime = Time.time;
        }
    }
    
    private void RefreshCache()
    {
        // Cache all agents
        cachedAgents.Clear();
        foreach (var agent in FindObjectsOfType<AgentController>())
        {
            if (!cachedAgents.ContainsKey(agent.agentId))
            {
                cachedAgents.Add(agent.agentId, agent);
            }
        }
        
        // Cache interactable objects
        cachedObjects.Clear();
        
        try {
            // Create a copy of the tags list to avoid modification during iteration
            List<string> validTags = new List<string>();
            List<string> invalidTags = new List<string>();
            
            // First pass: identify valid and invalid tags
            foreach (var tag in interactableObjectTags)
            {
                // Skip empty or null tags
                if (string.IsNullOrEmpty(tag))
                    continue;
                    
                try {
                    GameObject.FindGameObjectsWithTag(tag);
                    validTags.Add(tag);
                }
                catch (UnityException) {
                    Debug.LogWarning($"Tag '{tag}' is not defined in project settings. Skipping.");
                    invalidTags.Add(tag);
                }
            }
            
            // Remove invalid tags (outside of iteration)
            if (Application.isPlaying && invalidTags.Count > 0)
            {
                foreach (var invalidTag in invalidTags)
                {
                    interactableObjectTags.Remove(invalidTag);
                }
            }
            
            // Now use only valid tags to find objects
            foreach (var tag in validTags)
            {
                var objects = GameObject.FindGameObjectsWithTag(tag);
                foreach (var obj in objects)
                {
                    if (!cachedObjects.ContainsKey(obj.name))
                    {
                        cachedObjects.Add(obj.name, obj.transform);
                    }
                }
            }
        }
        catch (System.Exception e) {
            Debug.LogError($"Error refreshing environment cache: {e.Message}");
        }
    }
    
    public Dictionary<string, object> GetEnvironmentState(string agentId = null)
    {
        var result = new Dictionary<string, object>();
        List<Dictionary<string, object>> agentsList = new List<Dictionary<string, object>>();
        List<Dictionary<string, object>> locationsList = new List<Dictionary<string, object>>();
        List<Dictionary<string, object>> objectsList = new List<Dictionary<string, object>>();
        
        // Get all agents
        foreach (var agent in cachedAgents.Values)
        {
            var agentState = agent.GetAgentState();
            
            // If we're getting the state for a specific agent, add nearby objects and agents
            if (agent.agentId == agentId)
            {
                // Add nearby objects and agents to this agent's state
                agentState["nearby_objects"] = GetNearbyObjects(agent.transform);
                agentState["nearby_agents"] = GetNearbyAgents(agent.transform);
            }
            
            agentsList.Add(agentState);
        }
        
        // Aggregate all data
        result["agents"] = agentsList;
        result["locations"] = locationsList;
        result["objects"] = objectsList;
        
        return result;
    }
    
    private List<Dictionary<string, object>> GetNearbyAgents(Transform observer)
    {
        List<Dictionary<string, object>> nearbyAgents = new List<Dictionary<string, object>>();
        
        foreach (var agent in cachedAgents.Values)
        {
            // Skip self
            if (agent.transform == observer)
                continue;
            
            // Check if agent is within detection radius
            float distance = Vector3.Distance(observer.position, agent.transform.position);
            if (distance <= agentDetectionRadius)
            {
                // Check if within field of view
                if (IsWithinFieldOfView(observer, agent.transform.position))
                {
                    // Check line of sight if required
                    if (!requireLineOfSight || HasLineOfSight(observer, agent.transform.position))
                    {
                        var agentInfo = new Dictionary<string, object>
                        {
                            { "id", agent.agentId },
                            { "distance", distance },
                            { "position", new Dictionary<string, float>
                                {
                                    { "x", agent.transform.position.x },
                                    { "y", agent.transform.position.y },
                                    { "z", agent.transform.position.z }
                                }
                            },
                            { "status", agent.GetAgentState()["status"] }
                        };
                        
                        nearbyAgents.Add(agentInfo);
                        
                        if (logDetectionEvents)
                        {
                            Debug.Log($"Agent {observer.GetComponent<AgentController>().agentId} detected nearby agent: {agent.agentId} at {distance}m");
                        }
                    }
                }
            }
        }
        
        // Sort by distance
        nearbyAgents.Sort((a, b) => 
            ((float)a["distance"]).CompareTo((float)b["distance"]));
        
        return nearbyAgents;
    }
    
    private List<Dictionary<string, object>> GetNearbyObjects(Transform observer)
    {
        List<Dictionary<string, object>> nearbyObjects = new List<Dictionary<string, object>>();
        
        // Use spherecast to find objects
        Collider[] colliders = Physics.OverlapSphere(
            observer.position, 
            objectDetectionRadius, 
            detectionLayerMask
        );
        
        foreach (var collider in colliders)
        {
            // Skip if it's an agent 
            if (collider.GetComponent<AgentController>() != null)
                continue;
            
            // Check if the object is interesting (has an interesting tag)
            bool isInterestingObject = false;
            foreach (var tag in interactableObjectTags)
            {
                if (collider.CompareTag(tag))
                {
                    isInterestingObject = true;
                    break;
                }
            }
            
            if (!isInterestingObject)
                continue;
            
            // Check if within field of view
            if (IsWithinFieldOfView(observer, collider.transform.position))
            {
                // Check line of sight if required
                if (!requireLineOfSight || HasLineOfSight(observer, collider.transform.position))
                {
                    float distance = Vector3.Distance(observer.position, collider.transform.position);
                    
                    var objectInfo = new Dictionary<string, object>
                    {
                        { "id", collider.name },
                        { "name", collider.name },
                        { "distance", distance },
                        { "position", new Dictionary<string, float>
                            {
                                { "x", collider.transform.position.x },
                                { "y", collider.transform.position.y },
                                { "z", collider.transform.position.z }
                            }
                        },
                        { "tag", collider.tag }
                    };
                    
                    // Add description if available from InteractableObject component
                    var interactable = collider.GetComponent<InteractableObject>();
                    if (interactable != null)
                    {
                        objectInfo["description"] = interactable.description;
                    }
                    
                    nearbyObjects.Add(objectInfo);
                    
                    if (logDetectionEvents)
                    {
                        Debug.Log($"Agent {observer.GetComponent<AgentController>().agentId} detected object: {collider.name} at {distance}m");
                    }
                }
            }
        }
        
        // Sort by distance
        nearbyObjects.Sort((a, b) => 
            ((float)a["distance"]).CompareTo((float)b["distance"]));
        
        return nearbyObjects;
    }
    
    private bool IsWithinFieldOfView(Transform observer, Vector3 targetPosition)
    {
        if (fieldOfViewAngle >= 360f)
            return true;
        
        Vector3 directionToTarget = (targetPosition - observer.position).normalized;
        float angle = Vector3.Angle(observer.forward, directionToTarget);
        
        return angle <= fieldOfViewAngle * 0.5f;
    }
    
    private bool HasLineOfSight(Transform observer, Vector3 targetPosition)
    {
        Vector3 directionToTarget = targetPosition - observer.position;
        float distance = directionToTarget.magnitude;
        
        if (Physics.Raycast(observer.position, directionToTarget.normalized, out RaycastHit hit, distance, obstructionLayerMask))
        {
            // The ray hit something before reaching the target
            return false;
        }
        
        return true;
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugVisualization || !Application.isPlaying)
            return;
        
#if UNITY_EDITOR
        // Draw detection radius for currently selected agent
        AgentController selectedAgent = Selection.activeGameObject?.GetComponent<AgentController>();
        if (selectedAgent != null)
        {
            // Draw agent detection radius
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawSphere(selectedAgent.transform.position, agentDetectionRadius);
            
            // Draw object detection radius
            Gizmos.color = new Color(0, 0.5f, 1, 0.15f);
            Gizmos.DrawSphere(selectedAgent.transform.position, objectDetectionRadius);
            
            // Draw field of view
            if (fieldOfViewAngle < 360f)
            {
                Gizmos.color = new Color(1, 1, 0, 0.2f);
                float halfFOV = fieldOfViewAngle * 0.5f * Mathf.Deg2Rad;
                Vector3 rightDir = selectedAgent.transform.position + 
                    (Quaternion.Euler(0, halfFOV * Mathf.Rad2Deg, 0) * selectedAgent.transform.forward) * agentDetectionRadius;
                Vector3 leftDir = selectedAgent.transform.position + 
                    (Quaternion.Euler(0, -halfFOV * Mathf.Rad2Deg, 0) * selectedAgent.transform.forward) * agentDetectionRadius;
                
                Gizmos.DrawLine(selectedAgent.transform.position, rightDir);
                Gizmos.DrawLine(selectedAgent.transform.position, leftDir);
            }
        }
#endif
    }
}

// Helper component for interactable objects
public class InteractableObject : MonoBehaviour
{
    public string description;
    public Dictionary<string, object> properties = new Dictionary<string, object>();
}