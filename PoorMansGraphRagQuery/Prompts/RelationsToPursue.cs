namespace PoorMansGraphRagQuery.Prompts;

public static class RelationsToPursue
{
    public static string Prompt(string[] allEntities, string[] allRelationships) => $$"""
    You are an expert in traversing Knowledge Graphs. Given the following user question, and a list of provided entity and relationship types, respond with at most 5 types of entities, and 10 types of relationships to follow.
    
    ENTITIES:
    ------------
    {{string.Join("\n", allEntities)}}
    ------------
    
    RELATIONSHIPS:
    ------------
    {{string.Join("\n", allRelationships)}}
    ------------
    
    Return a JSON object formatted like this:
    {
        "entities": ["entity1", "entity2"],
        "relationships": ["relationship1", "relationship2"],
    }
    
    """;
}