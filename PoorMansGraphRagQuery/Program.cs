using PoorMansGraphRagQuery;

var queryEngine = new GraphRagQuery();

//test query
var userQuery = "Who did Leonardo Da Vinci interact with? Provide each person as a separate line of your response along with a short description.";

var embeddings = await queryEngine.Embed(userQuery);

//GRAPH RAG: 
var graphStartingEntities = await queryEngine.FindStartingEntitiesFromVectorIndex(userQuery, embeddings);
var entityAndRelationshipTypesToPursue = await queryEngine.GetEntityAndRelationshipTypesToPursue(userQuery);
var graphRagInfo = await queryEngine.TraverseGraph(graphStartingEntities, entityAndRelationshipTypesToPursue.Relationships, entityAndRelationshipTypesToPursue.Entities);

var graphRagChunks = queryEngine.GetChunks(graphRagInfo.chunks);
var graphRagWithChunksResult = await queryEngine.GraphRagWithChunks(graphRagInfo.graphSummaries, graphRagChunks, userQuery);
var graphRagResult = await queryEngine.GraphRag(graphRagInfo.graphSummaries, userQuery);

//Console.WriteLine(string.Join(", ", graphRagInfo.chunks.Order()));
Console.WriteLine($"--------- GRAPH RAG LLM RESPONSE. Prompt Tokens: {graphRagResult.promptTokens}. Completion Tokens: {graphRagResult.completionTokens}");
Console.WriteLine(graphRagResult.response);
Console.WriteLine("---------");

//Console.WriteLine(string.Join(", ", graphRagInfo.chunks.Order()));
Console.WriteLine($"--------- GRAPH RAG + CHUNKS RESPONSE. Prompt Tokens: {graphRagWithChunksResult.promptTokens}. Completion Tokens: {graphRagWithChunksResult.completionTokens}");
Console.WriteLine(graphRagWithChunksResult);
Console.WriteLine("---------");

//find the entities. Traverse the graph, find the chunks, and let's summarise :)
var baseRagChunksIndexes = await queryEngine.BaseRagChunks(userQuery, embeddings);
var baseRagChunks = queryEngine.GetChunks(baseRagChunksIndexes);
var baseRagResult = await queryEngine.Rag(baseRagChunks, userQuery);

//Console.WriteLine(string.Join(", ", baseRagChunks.Order()));
Console.WriteLine($"--------- BASE RAG LLM RESPONSE. Prompt Tokens: {baseRagResult.promptTokens}. Completion Tokens: {baseRagResult.completionTokens}");
Console.WriteLine(baseRagResult);
Console.WriteLine("---------");
Console.WriteLine("DONE!");