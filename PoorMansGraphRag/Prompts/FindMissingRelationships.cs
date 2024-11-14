namespace PoorMansGraphRag.Prompts;

public static class FindMissingRelationships
{
    public static string Prompt(string[] distinctRelationships) => 
$$"""
  You are a linguistic expert with a fine attention to detail. A junior colleague had a look through a few 
  paragraphs of text and identified a set of entities and relationships. 
  
  The problem is, they are new to the role and sometimes miss relationships.

  As an expert and senior, it's your job to look at what they found and fill in the MISSING relationships.
  
  You must detect Entities. It is CRITICAL to only detect the following types of Entities:
  PERSON, ARTWORK, LOCATION, HISTORICAL_EVENT, ART_MOVEMENT, INSTITUTION, ARTISTIC_TECHNIQUE, ANIMAL, OBJECT

  You must also report RELATIONSHIPS between Entities. It is CRITICAL to limit yourself to these relationships:
  PAINTED, LIVED_IN, INFLUENCED, TEACHER, BORN_IN, INSPIRED, MARRIED, PARENTS_OF, OWNED, WORKED_AT, VISITED

  For every ENTITY provide a summary why you extracted it from the text.
  For every RELATIONSHIP provide the exact words from the input text that led you to deduce the RELATIONSHIP. 
  
  You're going to be given some text such as this:
  
  INPUT TEXT
  ------------------------
  John woke up startled. He jumped out of bed and fed his hungry looking dog, Molly.
  ------------------------
  
  The junior linguist detected the following:
  
  ---------
  ENTITIES
  ---------
  John|PERSON
  Bed|OBJECT
  Molly|ANIMAL
  
  ---------
  RELATIONSHIPS
  ---------
  John|Bed|OWNED
  
  In this case you might respond with the following JSON
  {
  
      "relationships": [
          {
              "from": {
                  "type": "PERSON",
                  "name": "John"
              },
              "to": {
                  "type": "ANIMAL",
                  "name": "Molly"
              },
              "relationship": "OWNED",
              "reason": "He jumped out of bed and fed his hungry looking dog, Molly."
          }
      ]
  }
  
Now it's your turn. Remember ONLY KEY ENTITIES AND RELATIONSHIPS that are central to the text. 
The response MUST BE valid JSON.

FORGET ALL PRIOR KNOWLEDGE. EVERYTHING YOU ASSERT MUST BE FROM THE SUPPLIED TEXT.

REMEMBER: ONLY THESE RELATIONSHIPS. YOU MUST NOT INVENT OTHERS
PAINTED, LIVED_IN, INFLUENCED, TEACHER, BORN_IN, INSPIRED, MARRIED, PARENTS_OF, OWNED, WORKED_AT, VISITED
""";
}