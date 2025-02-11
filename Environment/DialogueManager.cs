using UnityEngine;
using TMPro;

public class DialogueManager : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text globalDialogueText;
    public GameObject dialoguePanel; 

    private static DialogueManager instance;

    void Awake()
    {
        if (instance == null) 
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public static DialogueManager Instance
    {
        get { return instance; }
    }

    public void AppendGlobalDialogue(string message)
    {
        if (dialoguePanel != null) 
            dialoguePanel.SetActive(true);

        if (globalDialogueText != null)
        {
            globalDialogueText.text += (string.IsNullOrEmpty(globalDialogueText.text) ? "" : "\n\n") + message;
        }
    }
}
