using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Neo4j.Driver;
using Newtonsoft.Json;
using PoorMansGraphRagQuery.Prompts;

namespace PoorMansGraphRagQuery;

public class GraphRagQuery
{
    private readonly string _basePath = "/Users/graemefoster/code/github/graemefoster/PoorMansGraphRag/cache/";
    private readonly OpenAIClient _client = new(
        new Uri("https://aoai.openai.azure.com/"),
        new DefaultAzureCredential());
    
    private static readonly string SearchEndpoint = "https://grfaisearchtest.search.windows.net";
    private readonly IDriver _neo4JClient = GraphDatabase.Driver("neo4j://localhost:7687", AuthTokens.Basic("neo4j", "password"));

    public async Task<ReadOnlyMemory<float>> Embed(string userQuery)
    {
        var testEmbeddingsOptions = new EmbeddingsOptions("text-embedding-ada-002", [userQuery]);
        var testEmbeddings = await _client.GetEmbeddingsAsync(testEmbeddingsOptions);
        return testEmbeddings.Value.Data[0].Embedding;
    }

    public async Task<Document[]> FindStartingEntitiesFromVectorIndex(string userQuery, ReadOnlyMemory<float> embeddings)
    {
        const string graphRagIndexName = "entities";
        var searchClient = new SearchClient(new Uri(SearchEndpoint), graphRagIndexName , new DefaultAzureCredential());
        var startingEntities = await searchClient.SearchAsync<Document>(
            userQuery, 
            new SearchOptions()
        {
            Size = 6,
            VectorSearch = new VectorSearchOptions()
            {
                Queries =
                {
                    new VectorizedQuery(embeddings)
                    {
                        Fields = { "embeddings" },
                        Exhaustive = true,
                    },
                }
            }
        });

        var entities = new List<Document>();
        await foreach (var doc in startingEntities.Value.GetResultsAsync())
        {
            entities.Add(doc.Document);
        }

        return entities.ToArray();
    }

    private async Task<(string[] entityTypes, string[] relationTypes)> GetEntityAndRelationshipTypes()
    {
        await using var session = _neo4JClient.AsyncSession();

        //Use the LLM to decide what types of entities and relationships to pursue
        //var allEntitiesCursor = await session.RunAsync("MATCH (n) RETURN n.name + \"|\" + labels(n)[0]");
        var allEntitiesCursor = await session.RunAsync("MATCH (n) RETURN DISTINCT labels(n)[0]");
        var allEntities = new List<string>();
        await foreach (var record in allEntitiesCursor) allEntities.Add(record[0].As<string>());

        var allRelationshipsCursor = await session.RunAsync("MATCH ()-[r]-() RETURN distinct (type(r))");
        var allRelationships = new List<string>();
        await foreach (var record in allRelationshipsCursor) allRelationships.Add(record[0].As<string>());

        return (allEntities.ToArray(), allRelationships.ToArray());
    }

    public async Task<LlmEntityRelationships> GetEntityAndRelationshipTypesToPursue(string userQuery)
    {
        var (entityTypes, relationTypes) = await GetEntityAndRelationshipTypes();
        
        var options = new ChatCompletionsOptions()
        {
            DeploymentName = "gpt-4o",
            ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
            Messages =
            {
                new ChatRequestSystemMessage(RelationsToPursue.Prompt(entityTypes, relationTypes)),
                new ChatRequestUserMessage(userQuery)
            }
        };

        var llmResponseEntitiesAndRelationships = await _client.GetChatCompletionsAsync(options);
        var entitiesAndRelationships = JsonConvert.DeserializeObject<LlmEntityRelationships>(llmResponseEntitiesAndRelationships.Value.Choices[0].Message.Content)!;

        return entitiesAndRelationships;

    }

    public IEnumerable<Document> FindGraphStartingPoints(Document[] searchResults, string[] entityTypes)
    {
        return searchResults.Where(x => entityTypes.Contains(x.type, StringComparer.InvariantCultureIgnoreCase));
    }

    public async Task<(IEnumerable<int> chunks, IEnumerable<string> graphSummaries)> TraverseGraph(IEnumerable<Document> graphStartingPoints, string[] relationships, string[] entities)
    {
        await using var session = _neo4JClient.AsyncSession();

        var allChunks = new Dictionary<int, int>();
        var allContext = new HashSet<string>();

        foreach (var x in graphStartingPoints)
        {
            var entity = x.entity;
            var type = x.type;
            var parameters = new
            {
                relationships = relationships,
                entities = entities
            };

            var neo4JQuery = $$"""MATCH (e:{{type}} {name:'{{entity.Replace("'", "\\'")}}'})-[r]-(e2) where type(r) in $relationships and labels(e2)[0] in $entities return r.chunks + e2.chunks, coalesce(e.summary, ""), coalesce(r.summary, ""), coalesce(e2.summary, ""), e.name, labels(e)[0], e2.name, labels(e2)[0]""";

            var results = await session.RunAsync(new Query(neo4JQuery, parameters));
            await foreach (var record in results)
            {
                foreach (int chunk in record[0].As<List<object>>().Cast<long>())
                {
                    if (!allChunks.TryAdd(chunk, 1))
                    {
                        allChunks[chunk] += 1;
                    }
                }

                var entityFrom = $"{record[4].As<string>()}:{record[5].As<string>()}";
                var entityTo = $"{record[6].As<string>()}:{record[7].As<string>()}";

                allContext.Add($"{entityFrom}: {record[1].As<string>()}");

                if (!string.IsNullOrEmpty(record[2].As<string>()))
                {
                    allContext.Add($"{entityFrom}<->{entityTo}: {record[2].As<string>()}");
                }

                allContext.Add($"{entityTo}: {record[3].As<string>()}");
            }
            
            var neo4JQuery2 = $$"""MATCH (e:{{type}} {name:'{{entity.Replace("'", "\\'")}}'})-[]-()-[r]-(e2) where type(r) in $relationships return r.chunks + e2.chunks, coalesce(e.summary, ""), coalesce(r.summary, ""), coalesce(e2.summary, ""), e.name, labels(e)[0], e2.name, labels(e2)[0]""";

            var results2 = await session.RunAsync(new Query(neo4JQuery2, parameters));
            await foreach (var record in results2)
            {
                //not sure if the chunks are as relevant as we move further out. But grab the summaries and relationships of nodes we find.
                var entityFrom = $"{record[4].As<string>()}:{record[5].As<string>()}";
                var entityTo = $"{record[6].As<string>()}:{record[7].As<string>()}";

                allContext.Add($"{entityFrom}: {record[1].As<string>()}");
                if (!string.IsNullOrEmpty(record[2].As<string>()))
                {
                    allContext.Add($"{entityFrom}<->{entityTo}: {record[2].As<string>()}");
                }
                allContext.Add($"{entityTo}: {record[3].As<string>()}");
            }
        }

        return (
            allChunks.OrderByDescending(x => x.Value).Take(5).Select(x => x.Key),
            allContext
        );
    }

    public IEnumerable<string> GetChunks(IEnumerable<int> groundingChunks)
    {
        var allChunks = groundingChunks.Select(x => File.ReadAllText($"{_basePath}chunk-{x}.txt"));
        return allChunks;
    }

    public async Task<(string response, int promptTokens, int completionTokens)> Rag(IEnumerable<string> context, string userQuery)
    {
        var chatCompletionsRequest = new ChatCompletionsOptions("gpt-4o", [
            new ChatRequestSystemMessage(RAG.Prompt(context)),
            new ChatRequestUserMessage(userQuery)
        ]);

        var llmResponse = await _client.GetChatCompletionsAsync(chatCompletionsRequest);
        return (llmResponse.Value.Choices[0].Message.Content, llmResponse.Value.Usage.PromptTokens, llmResponse.Value.Usage.CompletionTokens);    
    }

    public async Task<(string response, int promptTokens, int completionTokens)> GraphRag(IEnumerable<string> entitiesAndRelationships, string userQuery)
    {
        var chatCompletionsRequest = new ChatCompletionsOptions("gpt-4o", [
            new ChatRequestSystemMessage(GraphRAG.Prompt(entitiesAndRelationships)),
            new ChatRequestUserMessage(userQuery)
        ]);

        var llmResponse = await _client.GetChatCompletionsAsync(chatCompletionsRequest);
        return (llmResponse.Value.Choices[0].Message.Content, llmResponse.Value.Usage.PromptTokens, llmResponse.Value.Usage.CompletionTokens);    
    }

    public async Task<(string response, int promptTokens, int completionTokens)> GraphRagWithChunks(IEnumerable<string> entitiesAndRelationships, IEnumerable<string> context, string userQuery)
    {
        var chatCompletionsRequest = new ChatCompletionsOptions("gpt-4o", [
            new ChatRequestSystemMessage(GraphRAGPlusChunks.Prompt(entitiesAndRelationships, context)),
            new ChatRequestUserMessage(userQuery)
        ]);

        var llmResponse = await _client.GetChatCompletionsAsync(chatCompletionsRequest);
        return (llmResponse.Value.Choices[0].Message.Content, llmResponse.Value.Usage.PromptTokens, llmResponse.Value.Usage.CompletionTokens);    
    }

    public async Task<IEnumerable<int>> BaseRagChunks(string userQuery, ReadOnlyMemory<float> embeddings)
    {
        var baseRagIndexName = "baserag";
        var baseRagSearchClient = new SearchClient(new Uri(SearchEndpoint), baseRagIndexName , new DefaultAzureCredential());
        var baseRagChunksResponse = await baseRagSearchClient.SearchAsync<BaseRagDocument>(userQuery, new SearchOptions()
        {
            Size = 5,
            VectorSearch = new VectorSearchOptions()
            {
                Queries =
                {
                    new VectorizedQuery(embeddings)
                    {
                        Fields = { "embeddings" },
                        Exhaustive = true,
                    },
                }
            }
        });
        var baseRagChunks = new HashSet<int>();
        await foreach (var doc in baseRagChunksResponse.Value.GetResultsAsync())
        {
            baseRagChunks.Add(Convert.ToInt32(doc.Document.id));
        }

        return baseRagChunks.AsEnumerable();
    }
}