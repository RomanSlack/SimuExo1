using UnityEngine;
using System.Collections;

public class Tool_Conversation : MonoBehaviour
{
    public void StartConversation(AgentBehavior agent1, AgentBehavior agent2)
    {
        if (agent1 != null && agent2 != null)
        {
            // Set each other's conversation partner.
            agent1.conversationPartner = agent2;
            agent2.conversationPartner = agent1;

            agent1.AppendToDialogue($"Started talking to {agent2.agentName}");
            agent2.AppendToDialogue($"Started talking to {agent1.agentName}");

            agent1.StartCoroutine(ContinueConversation(agent1, agent2));
        }
    }

    private IEnumerator ContinueConversation(AgentBehavior agent1, AgentBehavior agent2)
    {
        int conversationTurns = Random.Range(3, 6); // 3-6 turns before ending
        for (int i = 0; i < conversationTurns; i++)
        {
            yield return new WaitForSeconds(4f);

            // Have each agent ask the LLM for a conversation response.
            agent1.StartCoroutine(agent1.AskOllama($"You are talking with {agent2.agentName}. Respond briefly."));
            agent2.StartCoroutine(agent2.AskOllama($"You are talking with {agent1.agentName}. Respond briefly."));
        }

        EndConversation(agent1, agent2);
    }

    public void EndConversation(AgentBehavior agent1, AgentBehavior agent2)
    {
        if (agent1 != null) agent1.conversationPartner = null;
        if (agent2 != null) agent2.conversationPartner = null;

        agent1.AppendToDialogue($"Conversation with {agent2.agentName} ended.");
        agent2.AppendToDialogue($"Conversation with {agent1.agentName} ended.");
    }
}
