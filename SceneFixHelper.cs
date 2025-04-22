using UnityEngine;
using System.Collections;

// This script helps fix common scene setup issues
public class SceneFixHelper : MonoBehaviour
{
    [SerializeField] private GameObject simulationManagerPrefab;
    [SerializeField] private bool autoFixMissingScripts = true;
    
    private void Start()
    {
        if (autoFixMissingScripts)
        {
            StartCoroutine(FixMissingScripts());
        }
    }
    
    private IEnumerator FixMissingScripts()
    {
        // Wait a frame to ensure all other components have initialized
        yield return null;
        
        // Check for WorldManager object with missing scripts
        GameObject worldManagerObj = GameObject.Find("WorldManager");
        if (worldManagerObj != null && worldManagerObj.GetComponent<WorldManager>() == null)
        {
            Debug.LogWarning("Found WorldManager object with missing script - fixing");
            
            // Get the script from another object or add a new one
            GameObject simulationManager = GameObject.Find("SimulationManager");
            if (simulationManager == null)
            {
                Debug.Log("Creating SimulationManager as WorldManager script was missing");
                
                if (simulationManagerPrefab != null)
                {
                    simulationManager = Instantiate(simulationManagerPrefab);
                    simulationManager.name = "SimulationManager";
                }
                else
                {
                    simulationManager = new GameObject("SimulationManager");
                    simulationManager.AddComponent<WorldManager>();
                    simulationManager.AddComponent<HttpServer>();
                    simulationManager.AddComponent<BackendCommunicator>();
                    simulationManager.AddComponent<EnvironmentReporter>();
                    simulationManager.AddComponent<SceneSetupManager>();
                }
            }
            
            // Add empty child to ensure references work
            Transform agentsContainer = worldManagerObj.transform.Find("Agents");
            if (agentsContainer == null)
            {
                GameObject agentsObj = new GameObject("Agents");
                agentsObj.transform.SetParent(worldManagerObj.transform);
            }
            
            // Set up to replace the broken object or reroute functionality
            WorldManager worldManager = simulationManager.GetComponent<WorldManager>();
            if (worldManager != null)
            {
                // Ensure the world manager is properly configured
                worldManager.enabled = true;
                Debug.Log("WorldManager script is now active on SimulationManager");
            }
            
            Debug.Log("Scene has been fixed to use SimulationManager instead of broken WorldManager");
        }
    }
}