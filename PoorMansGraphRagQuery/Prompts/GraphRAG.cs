namespace PoorMansGraphRagQuery.Prompts;

public static class GraphRAG
{
    public static string Prompt(IEnumerable<string> graph) => $"""
Given a user's question and some pieces of content, your job is to provide an 
answer as best you can from the information provided.

We have trawled a knowledge graph and discovered the following entities and relationships. 

Entities are in the form of "entity-name|entity-type <summary>":
Relationships are in the form of "entity-name|relationship|entity-name <text>"

Use all the relevant entities and relationships to form your answer.

ENTITIES AND RELATIONSHIPS FOLLOWS
---------------
{string.Join("\n\n", graph)}

Yes, You and I both know the content is about a famous person, but only use the provided above content to form your response. 

FORGET EVERYTHING ELSE YOU KNOW ABOUT THIS TOPIC!!!
""";
}