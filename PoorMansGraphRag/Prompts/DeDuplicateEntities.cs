namespace PoorMansGraphRag.Prompts;

public static class DeDuplicateEntities
{
    public static readonly string Prompt = 
        """
Look through all the discovered entities and relationships. If you find any entities that look like the same thing, report them. Use the context of the entities to help make decisions.

For example:

ENTITIES
----------------
The Last Supper|painting
Last Supper|object
Leonardo|person
Leonardo Da Vinci|person
st. maria delle grazie, milan|place

Report back in JSON... For this list you would report back:
{
   "duplicates": [
       {
           "primary": "The Last Supper|painting",
           "duplicates": [
             "Last Supper|object"
           ]
       },
       {
           "primary": "Leonardo Da Vinci|person",
           "duplicates": [
             "Leonardo|person"
           ]
       }
   ]
}

NOW IT'S YOUR TURN. DO NOT CHANGE THE NAMES. THEY MUST BE THE SAME AS THE INPUT.
""";
}