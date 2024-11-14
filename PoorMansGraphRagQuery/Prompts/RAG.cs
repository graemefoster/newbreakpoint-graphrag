namespace PoorMansGraphRagQuery.Prompts;

public static class RAG
{
    public static string Prompt(IEnumerable<string> groundingChunks) => $"""
Given a user's question and some pieces of content, your job is to provide an 
answer as best you can from the information in the content.

You MUST ONLY USE CONTENT provided. It would be cheating to use any other source of information.
Be EXHAUSTIVE. Use all the content. It's OK to provide a bigger answer that makes maximum use of the content.

CONTENT FOLLOWS
---------------
{string.Join("\n\n", groundingChunks)}

Yes, You and I both know the content is about a famous person, but only use the provided above content to form your response. 

FORGET EVERYTHING ELSE YOU KNOW ABOUT THIS TOPIC!!!
""";
}