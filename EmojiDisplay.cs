using UnityEngine;
using System.Collections.Generic;

public class EmojiDisplay : MonoBehaviour
{
    [Header("Emoji References")]
    [SerializeField] public SpriteRenderer emojiRenderer;
    
    [Header("Emoji Sprites")]
    [SerializeField] public Sprite idleEmoji;
    [SerializeField] public Sprite movingEmoji;
    [SerializeField] public Sprite speakingEmoji;
    [SerializeField] public Sprite thinkingEmoji;
    [SerializeField] public Sprite conversationEmoji;
    
    [Header("Location Specific Emojis")]
    [SerializeField] public LocationEmojiPair[] locationEmojis;
    
    private Dictionary<string, Sprite> locationEmojiDictionary = new Dictionary<string, Sprite>();
    
    private void Awake()
    {
        // Initialize the location-emoji dictionary
        foreach (LocationEmojiPair pair in locationEmojis)
        {
            if (pair.sprite != null && !string.IsNullOrEmpty(pair.locationName))
            {
                locationEmojiDictionary[pair.locationName.ToLower()] = pair.sprite;
            }
        }
    }
    
    public void SetEmojiForStatus(string status)
    {
        if (emojiRenderer == null) return;
        
        string statusLower = status.ToLower();
        
        if (statusLower.Contains("moving") || statusLower.Contains("walking"))
        {
            emojiRenderer.sprite = movingEmoji;
        }
        else if (statusLower.Contains("conversing") || statusLower.Contains("talking"))
        {
            emojiRenderer.sprite = conversationEmoji;
        }
        else if (statusLower.Contains("at "))
        {
            // Extract location name
            string location = statusLower.Substring(statusLower.IndexOf("at ") + 3).Trim();
            
            // Check if we have a specific emoji for this location
            if (locationEmojiDictionary.ContainsKey(location))
                emojiRenderer.sprite = locationEmojiDictionary[location];
            else if (location == "home")
                emojiRenderer.sprite = idleEmoji;
            else
                emojiRenderer.sprite = idleEmoji;
        }
        else if (statusLower.Contains("idle"))
        {
            emojiRenderer.sprite = idleEmoji;
        }
        else
        {
            emojiRenderer.sprite = thinkingEmoji;
        }
    }
    
    public void SetEmojiForSpeech()
    {
        if (emojiRenderer != null)
        {
            emojiRenderer.sprite = speakingEmoji;
        }
    }
}

[System.Serializable]
public class LocationEmojiPair
{
    public string locationName;
    public Sprite sprite;
}