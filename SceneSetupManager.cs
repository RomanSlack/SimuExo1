using UnityEngine;
using System.Collections.Generic;
using UnityEngine.AI;

/// <summary>
/// This script ensures the proper setup of the simulation scene.
/// It checks for required components and objects, and creates them if needed.
/// </summary>
public class SceneSetupManager : MonoBehaviour
{
    [SerializeField] private Transform defaultAgentsParent;
    [SerializeField] private Transform defaultLocationsParent;
    [SerializeField] private bool initializeOnStart = true;
    [SerializeField] private bool forceReinitialize = false;
    
    private bool isInitialized = false;
    
    private void Start()
    {
        if (initializeOnStart)
        {
            InitializeScene();
        }
    }
    
    public void InitializeScene()
    {
        if (isInitialized && !forceReinitialize)
            return;
        
        Debug.Log("Initializing scene setup...");
        
        // Setup the simulation manager if it doesn't exist
        SetupSimulationManager();
        
        // Setup the containers if needed
        Transform agentsContainer = SetupContainer("Agents", defaultAgentsParent);
        Transform locationsContainer = SetupContainer("Locations", defaultLocationsParent);
        
        // Find or create the WorldManager component and configure it
        WorldManager worldManager = FindObjectOfType<WorldManager>();
        if (worldManager != null)
        {
            // Configure the WorldManager
            worldManager.agentsContainer = agentsContainer;
            Debug.Log("Configured existing WorldManager");
        }
        else
        {
            Debug.LogError("WorldManager component not found. Please add it to the SimulationManager GameObject.");
        }
        
        // Ensure NavMesh exists
        CheckNavMesh();
        
        isInitialized = true;
        Debug.Log("Scene setup complete");
    }
    
    private GameObject SetupSimulationManager()
    {
        // Find existing SimulationManager or create it
        GameObject simulationManager = GameObject.Find("SimulationManager");
        if (simulationManager == null)
        {
            simulationManager = new GameObject("SimulationManager");
            Debug.Log("Created SimulationManager GameObject");
        }
        
        // Ensure it has all required components
        EnsureComponent<HttpServer>(simulationManager);
        EnsureComponent<WorldManager>(simulationManager);
        EnsureComponent<BackendCommunicator>(simulationManager);
        EnsureComponent<EnvironmentReporter>(simulationManager);
        
        return simulationManager;
    }
    
    private Transform SetupContainer(string containerName, Transform defaultParent)
    {
        // Find existing container or create it
        GameObject container = GameObject.Find(containerName);
        if (container == null)
        {
            if (defaultParent != null)
            {
                container = new GameObject(containerName);
                container.transform.SetParent(defaultParent);
            }
            else
            {
                container = new GameObject(containerName);
            }
            Debug.Log($"Created {containerName} container");
        }
        
        return container.transform;
    }
    
    private void CheckNavMesh()
    {
        // Test for NavMesh existence by sampling at origin
        NavMeshHit hit;
        Vector3 testPoint = new Vector3(325f, 50f, 425f); // Example center point
        bool hasNavMesh = NavMesh.SamplePosition(testPoint, out hit, 100f, NavMesh.AllAreas);
        
        if (!hasNavMesh)
        {
            Debug.LogWarning("NavMesh might not exist in the scene! This will cause agent navigation to fail. Please bake a NavMesh.");
        }
        else
        {
            Debug.Log("NavMesh detected and is valid");
        }
    }
    
    private T EnsureComponent<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null)
        {
            component = obj.AddComponent<T>();
            Debug.Log($"Added missing {typeof(T).Name} component to {obj.name}");
        }
        return component;
    }
}