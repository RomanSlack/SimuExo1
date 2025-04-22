using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(NavMeshAgent))]
public class AgentController : MonoBehaviour
{
    [Header("Agent Configuration")]
    [SerializeField] public string agentId = "Agent_Default";
    [SerializeField] private float movementSpeed = 3.5f;
    [SerializeField] private float turnSpeed = 120f;
    [SerializeField] private float stoppingDistance = 0.5f;
    [SerializeField] private bool dynamicPathfinding = true;
    
    [Header("Movement Parameters")]
    [SerializeField] private float positionTolerance = 0.1f;
    [SerializeField] private float rotationTolerance = 5f;
    [SerializeField] private float accelerationTime = 0.5f;
    [SerializeField] private float decelerationDistance = 1.5f;
    
    [Header("Status")]
    [SerializeField] private string currentLocation = "";
    [SerializeField] private string desiredLocation = "";
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private bool isMoving = false;
    [SerializeField] private bool isInConversation = false;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private bool logStateChanges = true;
    
    // References
    private NavMeshAgent navMeshAgent;
    private Animator animator;
    private AgentUI agentUI;
    private BackendCommunicator backendCommunicator;
    
    // Internal state
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float currentAcceleration = 0f;
    private AgentController conversationPartner;
    private Coroutine moveRoutine;
    private bool initialized = false;
    private Dictionary<string, Vector3> knownLocations = new Dictionary<string, Vector3>();
    
    void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        agentUI = GetComponent<AgentUI>();
        backendCommunicator = FindObjectOfType<BackendCommunicator>();
        
        // Configure NavMeshAgent
        navMeshAgent.speed = movementSpeed;
        navMeshAgent.angularSpeed = turnSpeed;
        navMeshAgent.stoppingDistance = stoppingDistance;
        navMeshAgent.acceleration = movementSpeed * 2;
        
        // Add standard locations
        InitializeKnownLocations();
    }
    
    void Start()
    {
        initialized = true;
        
        // Set status indicator in UI
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        // Notify backend of initial state
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Initialized at position {transform.position}");
        }
    }
    
    void Update()
    {
        // Update animation parameters if animator exists
        if (animator != null)
        {
            // Check if the animator has the required parameters before setting them
            if (HasAnimatorParameter("IsMoving", AnimatorControllerParameterType.Bool))
            {
                animator.SetBool("IsMoving", isMoving);
            }
            
            if (HasAnimatorParameter("MoveSpeed", AnimatorControllerParameterType.Float))
            {
                if (isMoving && navMeshAgent.velocity.magnitude > 0.1f)
                {
                    animator.SetFloat("MoveSpeed", navMeshAgent.velocity.magnitude / movementSpeed);
                }
                else
                {
                    animator.SetFloat("MoveSpeed", 0f);
                }
            }
        }
        
        // Check if reached destination
        if (isMoving && !navMeshAgent.pathPending)
        {
            if (navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
            {
                if (!navMeshAgent.hasPath || navMeshAgent.velocity.sqrMagnitude < 0.1f)
                {
                    OnReachedDestination();
                }
            }
        }
    }
    
    public bool RequestMove(string locationName)
    {
        // Don't interrupt existing movements unless forced
        if (isMoving && !string.IsNullOrEmpty(desiredLocation))
        {
            Debug.LogWarning($"[{agentId}] Already moving to {desiredLocation}, ignoring move to {locationName}");
            return false;
        }
        
        // Check if it's a known location
        if (knownLocations.TryGetValue(locationName.ToLower(), out Vector3 position))
        {
            desiredLocation = locationName;
            return MoveTo(position);
        }
        
        // Check if it's another agent
        var targetAgent = GameObject.FindObjectsOfType<AgentController>()
            .FirstOrDefault(a => a.agentId.Equals(locationName, StringComparison.OrdinalIgnoreCase));
            
        if (targetAgent != null)
        {
            desiredLocation = locationName;
            return MoveTo(targetAgent.transform.position);
        }
        
        Debug.LogWarning($"[{agentId}] Unknown location: {locationName}");
        return false;
    }
    
    public bool MoveTo(Vector3 position)
    {
        if (!initialized || !navMeshAgent.isOnNavMesh)
        {
            Debug.LogError($"[{agentId}] Cannot move: agent not initialized or not on NavMesh");
            return false;
        }
        
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
        }
        
        // Set target position
        targetPosition = position;
        
        // Update status
        isMoving = true;
        currentStatus = $"Moving to {desiredLocation}";
        
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        // Start movement
        navMeshAgent.SetDestination(targetPosition);
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Moving to {desiredLocation} at {targetPosition}");
        }
        
        // Notify backend of state change
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
        
        return true;
    }
    
    public void StopMovement()
    {
        if (isMoving)
        {
            navMeshAgent.ResetPath();
            isMoving = false;
            currentStatus = "Idle";
            
            if (agentUI != null)
            {
                agentUI.UpdateStatus(currentStatus);
            }
            
            if (logStateChanges)
            {
                Debug.Log($"[{agentId}] Stopped movement");
            }
            
            // Notify backend of state change
            if (backendCommunicator != null)
            {
                backendCommunicator.NotifyAgentStateChange(this);
            }
        }
    }
    
    private void OnReachedDestination()
    {
        isMoving = false;
        
        // Update current location if we moved to a named location
        if (!string.IsNullOrEmpty(desiredLocation))
        {
            currentLocation = desiredLocation;
        }
        
        currentStatus = $"At {currentLocation}";
        
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Reached destination: {currentLocation}");
        }
        
        // Reset desired location
        desiredLocation = "";
        
        // Notify backend of state change
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
    }
    
    public bool InitiateConversation(AgentController target)
    {
        if (target == null || target == this)
        {
            Debug.LogWarning($"[{agentId}] Cannot converse with invalid target");
            return false;
        }
        
        // Calculate distance to target
        float distance = Vector3.Distance(transform.position, target.transform.position);
        if (distance > 5f)  // Maximum conversation distance
        {
            Debug.LogWarning($"[{agentId}] Target too far for conversation ({distance}m)");
            return false;
        }
        
        // Set both agents in conversation
        isInConversation = true;
        target.EnterConversation(this);
        
        // Update status
        currentStatus = $"Conversing with {target.agentId}";
        conversationPartner = target;
        
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        // Face each other
        StartCoroutine(TurnToFace(target.transform));
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Started conversation with {target.agentId}");
        }
        
        // Notify backend of state change
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
        
        return true;
    }
    
    public void EnterConversation(AgentController initiator)
    {
        isInConversation = true;
        conversationPartner = initiator;
        currentStatus = $"Conversing with {initiator.agentId}";
        
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        // Face the initiator
        StartCoroutine(TurnToFace(initiator.transform));
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Entered conversation with {initiator.agentId}");
        }
        
        // Notify backend of state change
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
    }
    
    public void EndConversation()
    {
        if (isInConversation && conversationPartner != null)
        {
            // End conversation on other side too
            if (conversationPartner.IsInConversationWith(this))
            {
                conversationPartner.ExitConversation();
            }
            
            ExitConversation();
        }
    }
    
    private void ExitConversation()
    {
        isInConversation = false;
        conversationPartner = null;
        currentStatus = "Idle";
        
        if (agentUI != null)
        {
            agentUI.UpdateStatus(currentStatus);
        }
        
        if (logStateChanges)
        {
            Debug.Log($"[{agentId}] Exited conversation");
        }
        
        // Notify backend of state change
        if (backendCommunicator != null)
        {
            backendCommunicator.NotifyAgentStateChange(this);
        }
    }
    
    public bool IsInConversationWith(AgentController other)
    {
        return isInConversation && conversationPartner == other;
    }
    
    private IEnumerator TurnToFace(Transform target)
    {
        Vector3 directionToTarget = (target.position - transform.position).normalized;
        directionToTarget.y = 0; // Ensure we only rotate on the y-axis
        
        if (directionToTarget != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            
            while (Quaternion.Angle(transform.rotation, targetRotation) > rotationTolerance)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, 
                    targetRotation, 
                    turnSpeed * Time.deltaTime
                );
                yield return null;
            }
        }
    }
    
    private void InitializeKnownLocations()
    {
        // Initialize with the same locations from the reference implementation
        knownLocations.Add("park", new Vector3(350.47f, 49.63f, 432.7607f));
        knownLocations.Add("library", new Vector3(325.03f, 50.29f, 407.87f));
        knownLocations.Add("cantina", new Vector3(324.3666f, 50.33723f, 463.2347f));
        knownLocations.Add("gym", new Vector3(300.5f, 50.23723f, 420.8247f));
        knownLocations.Add("o2_regulator_room", new Vector3(324.3666f, 50.33723f, 463.2347f));
    }
    
    public void AddKnownLocation(string name, Vector3 position)
    {
        if (!knownLocations.ContainsKey(name.ToLower()))
        {
            knownLocations.Add(name.ToLower(), position);
            
            if (logStateChanges)
            {
                Debug.Log($"[{agentId}] Added known location: {name} at {position}");
            }
        }
        else
        {
            knownLocations[name.ToLower()] = position;
            
            if (logStateChanges)
            {
                Debug.Log($"[{agentId}] Updated known location: {name} to {position}");
            }
        }
    }
    
    // Get agent state for backend communication
    public Dictionary<string, object> GetAgentState()
    {
        return new Dictionary<string, object>
        {
            { "id", agentId },
            { "position", new Dictionary<string, float>
                {
                    { "x", transform.position.x },
                    { "y", transform.position.y },
                    { "z", transform.position.z }
                }
            },
            { "rotation", new Dictionary<string, float>
                {
                    { "y", transform.eulerAngles.y }
                }
            },
            { "status", currentStatus },
            { "location", currentLocation },
            { "is_moving", isMoving },
            { "is_in_conversation", isInConversation },
            { "conversation_partner", conversationPartner?.agentId },
            { "desired_location", desiredLocation }
        };
    }
    
    private bool HasAnimatorParameter(string paramName, AnimatorControllerParameterType paramType)
    {
        if (animator == null)
            return false;
            
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.name == paramName && param.type == paramType)
                return true;
        }
        
        return false;
    }
    
    void OnDrawGizmos()
    {
        if (!showDebugInfo || !initialized)
            return;
        
        // Draw agent direction
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, transform.forward * 2f);
        
        // Draw path if moving
        if (isMoving && navMeshAgent != null && navMeshAgent.hasPath)
        {
            Gizmos.color = Color.yellow;
            Vector3[] corners = navMeshAgent.path.corners;
            for (int i = 0; i < corners.Length - 1; i++)
            {
                Gizmos.DrawLine(corners[i], corners[i + 1]);
            }
        }
        
        // Draw conversation link
        if (isInConversation && conversationPartner != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, conversationPartner.transform.position);
        }
    }
}