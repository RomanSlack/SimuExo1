using UnityEngine;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;

[Serializable]
public class ApiResponse
{
    public string status = "success";
    public string message = "";
    public object data = null;
    
    public ApiResponse() { }
    
    public ApiResponse(string status, string message, object data = null)
    {
        this.status = status;
        this.message = message;
        this.data = data;
    }
    
    public static ApiResponse Success(string message = "", object data = null)
    {
        return new ApiResponse("success", message, data);
    }
    
    public static ApiResponse Error(string message, object data = null)
    {
        return new ApiResponse("error", message, data);
    }
}

public class HttpServer : MonoBehaviour
{
    [SerializeField] private string ipAddress = "127.0.0.1";
    [SerializeField] private int port = 8080;
    [SerializeField] private bool startOnAwake = true;
    [SerializeField] private bool logRequests = true;
    
    private HttpListener listener;
    private Thread listenerThread;
    private CancellationTokenSource cancellationTokenSource;
    private ConcurrentDictionary<string, Func<HttpListenerRequest, Task<ApiResponse>>> endpointHandlers = 
        new ConcurrentDictionary<string, Func<HttpListenerRequest, Task<ApiResponse>>>();
    
    public bool IsRunning { get; private set; }
    
    private void Awake()
    {
        if (startOnAwake)
        {
            StartServer();
        }
    }
    
    private void OnDestroy()
    {
        StopServer();
    }
    
    public void StartServer()
    {
        if (IsRunning) return;
        
        try
        {
            RegisterEndpoints();
            
            cancellationTokenSource = new CancellationTokenSource();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://{ipAddress}:{port}/");
            listener.Start();
            
            listenerThread = new Thread(ListenerThreadProc);
            listenerThread.IsBackground = true;
            listenerThread.Start();
            
            IsRunning = true;
            Debug.Log($"HTTP server started on http://{ipAddress}:{port}/");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start HTTP server: {e.Message}");
            StopServer();
        }
    }
    
    public void StopServer()
    {
        if (!IsRunning) return;
        
        try
        {
            cancellationTokenSource?.Cancel();
            
            // Give the listener thread time to complete
            if (listenerThread != null && listenerThread.IsAlive)
            {
                listenerThread.Join(1000);
                if (listenerThread.IsAlive)
                {
                    Debug.LogWarning("Listener thread did not exit cleanly");
                }
            }
            
            listener?.Close();
            
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            
            IsRunning = false;
            Debug.Log("HTTP server stopped");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error stopping HTTP server: {e.Message}");
        }
    }
    
    private void RegisterEndpoints()
    {
        // Register health check endpoint
        RegisterEndpoint("GET", "/health", async (request) => 
        {
            return ApiResponse.Success("Server is running");
        });
        
        // Agent movement endpoint
        RegisterEndpoint("POST", "/agent/{id}/move", HandleAgentMove);
        
        // Agent speech endpoint
        RegisterEndpoint("POST", "/agent/{id}/speak", HandleAgentSpeak);
        
        // Agent conversation endpoint 
        RegisterEndpoint("POST", "/agent/{id}/converse", HandleAgentConverse);
        
        // Agent registration endpoint
        RegisterEndpoint("POST", "/agent/register", HandleAgentRegister);
        
        // Agent deregistration endpoint
        RegisterEndpoint("POST", "/agent/{id}/deregister", HandleAgentDeregister);
        
        // Environment query endpoint
        RegisterEndpoint("GET", "/env/{agent_id}", HandleEnvironmentQuery);
    }
    
    public void RegisterEndpoint(string method, string path, Func<HttpListenerRequest, Task<ApiResponse>> handler)
    {
        string key = $"{method.ToUpper()}:{path}";
        endpointHandlers[key] = handler;
        Debug.Log($"Registered endpoint {method} {path}");
    }
    
    private async void ListenerThreadProc()
    {
        try
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync();
                
                // Handle request in a separate task to not block the listener
                Task.Run(async () => 
                {
                    try
                    {
                        await HandleRequest(context);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error handling request: {e.Message}");
                        try
                        {
                            // Try to send error response
                            await SendJsonResponse(context.Response, 500, 
                                JsonUtility.ToJson(ApiResponse.Error($"Internal server error: {e.Message}")));
                        }
                        catch
                        {
                            // Ignore if sending error fails
                        }
                    }
                });
            }
        }
        catch (HttpListenerException e) when (cancellationTokenSource.Token.IsCancellationRequested)
        {
            // Expected exception when canceling - ignore
        }
        catch (ObjectDisposedException e) when (cancellationTokenSource.Token.IsCancellationRequested)
        {
            // Expected exception when disposing - ignore
        }
        catch (OperationCanceledException)
        {
            // Expected exception when canceling - ignore
        }
        catch (Exception e)
        {
            Debug.LogError($"HTTP server error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        string method = request.HttpMethod;
        string rawUrl = request.RawUrl;
        
        if (logRequests)
        {
            Debug.Log($"Received {method} request for {rawUrl}");
        }
        
        try
        {
            // Find matching endpoint
            Func<HttpListenerRequest, Task<ApiResponse>> handler = FindMatchingEndpointHandler(method, rawUrl);
            
            if (handler != null)
            {
                try
                {
                    ApiResponse result = await handler(request);
                    string jsonResponse = JsonUtility.ToJson(result);
                    await SendJsonResponse(response, 200, jsonResponse);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in endpoint handler: {e.Message}\n{e.StackTrace}");
                    await SendJsonResponse(response, 500, 
                        JsonUtility.ToJson(ApiResponse.Error($"Handler error: {e.Message}")));
                }
            }
            else
            {
                // No matching endpoint
                await SendJsonResponse(response, 404, 
                    JsonUtility.ToJson(ApiResponse.Error($"Endpoint not found: {method} {rawUrl}")));
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error processing request: {e.Message}\n{e.StackTrace}");
            await SendJsonResponse(response, 500, 
                JsonUtility.ToJson(ApiResponse.Error($"Server error: {e.Message}")));
        }
    }
    
    private Func<HttpListenerRequest, Task<ApiResponse>> FindMatchingEndpointHandler(string method, string rawUrl)
    {
        // Parse URL parts
        Uri uri = new Uri("http://localhost" + rawUrl);
        string path = uri.AbsolutePath;
        
        // First try direct match
        string exactKey = $"{method.ToUpper()}:{path}";
        if (endpointHandlers.TryGetValue(exactKey, out var exactHandler))
        {
            return exactHandler;
        }
        
        // Then try pattern matching
        foreach (var handler in endpointHandlers)
        {
            string[] keyParts = handler.Key.Split(':');
            string handlerMethod = keyParts[0];
            string handlerPattern = keyParts[1];
            
            if (handlerMethod != method.ToUpper())
                continue;
            
            // Check if pattern matches path with parameter extraction
            if (IsPatternMatch(handlerPattern, path))
            {
                return handler.Value;
            }
        }
        
        return null;
    }
    
    private bool IsPatternMatch(string pattern, string path)
    {
        string[] patternParts = pattern.Split('/');
        string[] pathParts = path.Split('/');
        
        if (patternParts.Length != pathParts.Length)
            return false;
        
        for (int i = 0; i < patternParts.Length; i++)
        {
            // Skip empty segments (multiple slashes)
            if (string.IsNullOrEmpty(patternParts[i]) && string.IsNullOrEmpty(pathParts[i]))
                continue;
            
            // Match parameter placeholders {id}
            if (patternParts[i].StartsWith("{") && patternParts[i].EndsWith("}"))
                continue;
            
            // Direct segment comparison
            if (patternParts[i] != pathParts[i])
                return false;
        }
        
        return true;
    }
    
    private Dictionary<string, string> ExtractParameters(string pattern, string path)
    {
        Dictionary<string, string> parameters = new Dictionary<string, string>();
        
        string[] patternParts = pattern.Split('/');
        string[] pathParts = path.Split('/');
        
        for (int i = 0; i < patternParts.Length; i++)
        {
            if (i < pathParts.Length && 
                patternParts[i].StartsWith("{") && 
                patternParts[i].EndsWith("}"))
            {
                string paramName = patternParts[i].Substring(1, patternParts[i].Length - 2);
                parameters[paramName] = pathParts[i];
            }
        }
        
        return parameters;
    }
    
    private async Task SendJsonResponse(HttpListenerResponse response, int statusCode, string jsonContent)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        
        byte[] buffer = Encoding.UTF8.GetBytes(jsonContent);
        response.ContentLength64 = buffer.Length;
        
        var output = response.OutputStream;
        await output.WriteAsync(buffer, 0, buffer.Length);
        output.Close();
    }
    
    private string ReadRequestBody(HttpListenerRequest request)
    {
        if (request.HasEntityBody)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }
        
        return null;
    }
    
    // Endpoint handlers
    private async Task<ApiResponse> HandleAgentMove(HttpListenerRequest request)
    {
        try
        {
            string path = request.RawUrl;
            Uri uri = new Uri("http://localhost" + path);
            string pattern = "/agent/{id}/move";
            
            var parameters = ExtractParameters(pattern, uri.AbsolutePath);
            string agentId = parameters["id"];
            
            string requestBody = ReadRequestBody(request);
            MoveRequest moveData = JsonUtility.FromJson<MoveRequest>(requestBody);
            
            // Find the agent
            var agentController = FindAgentById(agentId);
            if (agentController == null)
            {
                return ApiResponse.Error($"Agent with ID {agentId} not found");
            }
            
            // Request the move
            bool moveRequested = agentController.RequestMove(moveData.location);
            
            if (moveRequested)
            {
                return ApiResponse.Success($"Move to {moveData.location} initiated", 
                    new { agentId = agentId, location = moveData.location });
            }
            else
            {
                return ApiResponse.Error("Move request failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling agent move: {e.Message}");
            return ApiResponse.Error($"Error handling agent move: {e.Message}");
        }
    }
    
    private async Task<ApiResponse> HandleAgentSpeak(HttpListenerRequest request)
    {
        try
        {
            string path = request.RawUrl;
            Uri uri = new Uri("http://localhost" + path);
            string pattern = "/agent/{id}/speak";
            
            var parameters = ExtractParameters(pattern, uri.AbsolutePath);
            string agentId = parameters["id"];
            
            string requestBody = ReadRequestBody(request);
            SpeakRequest speakData = JsonUtility.FromJson<SpeakRequest>(requestBody);
            
            // Find the agent
            var agentUI = FindAgentUIById(agentId);
            if (agentUI == null)
            {
                return ApiResponse.Error($"Agent with ID {agentId} not found");
            }
            
            // Request the speech
            agentUI.DisplaySpeech(speakData.message);
            
            return ApiResponse.Success("Speech displayed", 
                new { agentId = agentId, message = speakData.message });
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling agent speak: {e.Message}");
            return ApiResponse.Error($"Error handling agent speak: {e.Message}");
        }
    }
    
    private async Task<ApiResponse> HandleAgentConverse(HttpListenerRequest request)
    {
        try
        {
            string path = request.RawUrl;
            Uri uri = new Uri("http://localhost" + path);
            string pattern = "/agent/{id}/converse";
            
            var parameters = ExtractParameters(pattern, uri.AbsolutePath);
            string agentId = parameters["id"];
            
            string requestBody = ReadRequestBody(request);
            ConverseRequest converseData = JsonUtility.FromJson<ConverseRequest>(requestBody);
            
            // Find both agents
            var agentController = FindAgentById(agentId);
            var targetAgentController = FindAgentById(converseData.targetAgent);
            
            if (agentController == null)
            {
                return ApiResponse.Error($"Agent with ID {agentId} not found");
            }
            
            if (targetAgentController == null)
            {
                return ApiResponse.Error($"Target agent with ID {converseData.targetAgent} not found");
            }
            
            // Request the conversation
            bool conversationRequested = agentController.InitiateConversation(targetAgentController);
            
            if (conversationRequested)
            {
                return ApiResponse.Success($"Conversation with {converseData.targetAgent} initiated", 
                    new { initiator = agentId, target = converseData.targetAgent });
            }
            else
            {
                return ApiResponse.Error("Conversation request failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling agent converse: {e.Message}");
            return ApiResponse.Error($"Error handling agent converse: {e.Message}");
        }
    }
    
    private async Task<ApiResponse> HandleAgentRegister(HttpListenerRequest request)
    {
        try
        {
            string requestBody = ReadRequestBody(request);
            RegisterAgentRequest registerData = JsonUtility.FromJson<RegisterAgentRequest>(requestBody);
            
            // Check if agent already exists
            var existingAgent = FindAgentById(registerData.agentId);
            if (existingAgent != null)
            {
                return ApiResponse.Error($"Agent with ID {registerData.agentId} already exists");
            }
            
            // Find the world manager to create a new agent
            var worldManager = FindObjectOfType<WorldManager>();
            if (worldManager == null)
            {
                return ApiResponse.Error("World manager not found");
            }
            
            // Create the new agent
            var newAgent = worldManager.CreateNewAgent(
                registerData.agentId, 
                registerData.personality, 
                registerData.initialLocation
            );
            
            if (newAgent != null)
            {
                return ApiResponse.Success($"Agent {registerData.agentId} registered", 
                    new { agentId = registerData.agentId });
            }
            else
            {
                return ApiResponse.Error("Agent registration failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling agent registration: {e.Message}");
            return ApiResponse.Error($"Error handling agent registration: {e.Message}");
        }
    }
    
    private async Task<ApiResponse> HandleAgentDeregister(HttpListenerRequest request)
    {
        try
        {
            string path = request.RawUrl;
            Uri uri = new Uri("http://localhost" + path);
            string pattern = "/agent/{id}/deregister";
            
            var parameters = ExtractParameters(pattern, uri.AbsolutePath);
            string agentId = parameters["id"];
            
            // Check if agent exists
            var existingAgent = FindAgentById(agentId);
            if (existingAgent == null)
            {
                return ApiResponse.Error($"Agent with ID {agentId} not found");
            }
            
            // Find the world manager to remove the agent
            var worldManager = FindObjectOfType<WorldManager>();
            if (worldManager == null)
            {
                return ApiResponse.Error("World manager not found");
            }
            
            // Remove the agent
            bool removed = worldManager.RemoveAgent(agentId);
            
            if (removed)
            {
                return ApiResponse.Success($"Agent {agentId} deregistered");
            }
            else
            {
                return ApiResponse.Error("Agent deregistration failed");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling agent deregistration: {e.Message}");
            return ApiResponse.Error($"Error handling agent deregistration: {e.Message}");
        }
    }
    
    private async Task<ApiResponse> HandleEnvironmentQuery(HttpListenerRequest request)
    {
        try
        {
            string path = request.RawUrl;
            Uri uri = new Uri("http://localhost" + path);
            string pattern = "/env/{agent_id}";
            
            var parameters = ExtractParameters(pattern, uri.AbsolutePath);
            string agentId = parameters["agent_id"];
            
            // Find the environment reporter
            var environmentReporter = FindObjectOfType<EnvironmentReporter>();
            if (environmentReporter == null)
            {
                return ApiResponse.Error("Environment reporter not found");
            }
            
            // Get the environment state
            var environmentState = environmentReporter.GetEnvironmentState(agentId);
            
            return ApiResponse.Success("Environment state retrieved", environmentState);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error handling environment query: {e.Message}");
            return ApiResponse.Error($"Error handling environment query: {e.Message}");
        }
    }
    
    // Helper methods to find game objects
    private AgentController FindAgentById(string agentId)
    {
        return FindObjectsOfType<AgentController>()
            .FirstOrDefault(a => a.agentId == agentId);
    }
    
    private AgentUI FindAgentUIById(string agentId)
    {
        return FindObjectsOfType<AgentUI>()
            .FirstOrDefault(a => a.agentId == agentId);
    }
}

// Request/Response models
[Serializable]
public class MoveRequest
{
    public string location;
}

[Serializable]
public class SpeakRequest
{
    public string message;
}

[Serializable]
public class ConverseRequest
{
    public string targetAgent;
}

[Serializable]
public class RegisterAgentRequest
{
    public string agentId;
    public string personality;
    public string initialLocation;
}