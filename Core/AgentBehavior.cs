using UnityEngine;
using System.Collections;
using System.Diagnostics;
using System.Text;
using TMPro;

public class AgentBehavior : MonoBehaviour
{
    [Header("Agent Config")]
    public string agentName = "Agent_1";
    public string mood = "neutral";
    public string ollamaPath = @"C:\Path\To\ollama.exe";  // Update this path as needed
    public string modelName = "qwen:14b";

    [Header("UI References")]
    public TMP_Text dialogueText;
    public GameObject dialoguePanel;
    public TMP_InputField moodInputField;

    [Header("Tools References")]
    public Tool_Move moveTool;
    public Tool_Reset resetTool;  // Optional tool for handling errors

    // PUBLIC so that external tools (like conversation) can access it.
    public AgentBehavior conversationPartner;

    // Internal fields
    private UnityEngine.AI.NavMeshAgent navAgent;
    private Animator animator;
    private bool isPrompting = false;

    void Start()
    {
        // Set up the NavMeshAgent so the agent does not sink through the floor.
        navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null)
        {
            // Adjust this offset as needed to keep the agent above the floor.
            navAgent.baseOffset = 0f;
        }

        // Ensure the animator starts in idle.
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            animator.SetBool("isWalking", false);
        }

        // Activate the dialogue panel.
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(true);
        }

        // Initialize the mood input field if present.
        if (moodInputField != null)
        {
            moodInputField.text = mood;
            moodInputField.onValueChanged.AddListener((newMood) => mood = newMood);
        }
    }

    /// <summary>
    /// Called (by SimulationController) when SHIFT+X is pressed.
    /// </summary>
    public void RequestActionFromLLM()
    {
        if (!isPrompting)
        {
            isPrompting = true;

            // Build a prompt similar to your original system prompt.
            string prompt =
                $"You are an agent in a multi-agent environment. Your task is to choose an action based on your mood. " +
                $"The possible actions (tools) are:\n" +
                $"1. MOVE: To move to a location. The location context options are PARK, HOME, GYM, and LIBRARY.\n" +
                $"2. ERROR: If the response is unclear.\n\n" +
                $"You will always output your decision in this format:\n[\"TOOL\", \"CONTEXT\"]\n\n" +
                $"Your current mood is {mood}. Where do you want to move?";

            StartCoroutine(AskOllama(prompt));
        }
    }




    void Update()
{
    if (Input.GetKeyDown(KeyCode.Escape))
    {
        ToggleCursor();
    }
}

/// <summary>
/// Toggles the mouse cursor to allow UI interaction.
/// </summary>
private void ToggleCursor()
{
    if (Cursor.lockState == CursorLockMode.Locked)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        // Set focus to the mood input field.
        if (moodInputField != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(moodInputField.gameObject);
        }
    }
    else
    {
        // Optionally, clear UI focus before locking the cursor again.
        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}





    /// <summary>
    /// Public so that external tools (like Tool_Conversation) can call it.
    /// </summary>
    /// 

    


    public IEnumerator AskOllama(string prompt)
    {
        // Prepare the command-line arguments for the local LLM (Ollama).
        string arguments = $"run {modelName} \"{prompt}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = ollamaPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using (Process process = new Process { StartInfo = startInfo })
        {
            StringBuilder output = new StringBuilder();
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            yield return new WaitUntil(() => process.HasExited);

            string result = output.ToString().Trim();
            AppendToDialogue($"LLM Raw Response: {result}");
            ProcessResponse(result);
            isPrompting = false;
        }
    }

    private void ProcessResponse(string response)
    {
        // Clean the response by removing brackets and quotes.
        response = response.Replace("[", "").Replace("]", "").Replace("\"", "");
        string[] parsed = response.Split(',');

        if (parsed.Length != 2)
        {
            AppendToDialogue("Error: Invalid response format.");
            resetTool?.ExecuteReset("Invalid response format.");
            return;
        }

        // Example expected response: ["MOVE", "PARK"]
        string tool = parsed[0].Trim().ToUpper();
        string context = parsed[1].Trim();

        AppendToDialogue($"Tool: {tool} | Context: {context}");

        switch (tool)
        {
            case "MOVE":
                if (moveTool != null)
                {
                    moveTool.ExecuteMove(context);
                }
                break;

            case "ERROR":
                resetTool?.ExecuteReset("LLM returned an ERROR tool.");
                break;

            // (Optional) Add a CONVERSE case if you wish to test conversation later.
            default:
                resetTool?.ExecuteReset($"Unknown tool: {tool}");
                break;
        }
    }

    /// <summary>
    /// Appends text to the agent's dialogue UI.
    /// </summary>
    public void AppendToDialogue(string message)
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        if (dialogueText != null)
        {
            if (!string.IsNullOrEmpty(dialogueText.text))
                dialogueText.text += "\n\n";

            dialogueText.text += message;
        }
    }

    /// <summary>
    /// Finds a nearby agent (within 5 units) that is available for conversation.
    /// </summary>
    public AgentBehavior FindConversationPartner()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 5f);
        foreach (var hit in hits)
        {
            AgentBehavior other = hit.GetComponent<AgentBehavior>();
            if (other != null && other != this && other.conversationPartner == null)
            {
                return other;
            }
        }
        return null;
    }
}
