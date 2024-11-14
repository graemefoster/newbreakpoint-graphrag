using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Neo4j.Driver;
using Newtonsoft.Json;
using PoorMansGraphRag;
using PoorMansGraphRag.Prompts;

class GraphRag
{
    private readonly string _searchEndpoint = "https://grfaisearchtest.search.windows.net";
    private readonly List<string> _chunks = new();

    private readonly OpenAIClient _client = new(
        new Uri("https://aoai.openai.azure.com/"),
        new DefaultAzureCredential());

    private readonly Dictionary<string, Entity> _allEntities = new();
    private readonly Dictionary<(Entity, Entity, string), Relation> _allRelationships = new();

    private Entity LoadEntity(LlmEntity llmEntity, int i)
    {
        var nameLower = llmEntity.Name.ToLowerInvariant();

        if (_allEntities.TryGetValue(nameLower, out var discovered))
        {
            if (!string.IsNullOrWhiteSpace(llmEntity.Reason))
            {
                discovered.Reasons.Add(new Reasoning() { Chunk = i, Reason = llmEntity.Reason });
            }
        }
        else
        {
            _allEntities.Add(nameLower, new Entity()
            {
                Name = nameLower,
                Reasons = string.IsNullOrWhiteSpace(llmEntity.Reason)
                    ? []
                    : [new Reasoning() { Chunk = i, Reason = llmEntity.Reason }],
                Type = llmEntity.Type.ToLowerInvariant(),
            });
        }

        return _allEntities[nameLower];
    }

    public async Task CrackDocument(string cachePath, string path)
    {
        using var sampleText = File.OpenText(path);

        var chunk = new StringBuilder();
        var wordsInChunk = 0;
        var useCache = true;

        var chunkIndex = 0;
        while (!sampleText.EndOfStream)
        {
            var line = await sampleText.ReadLineAsync();
            chunk.AppendLine(line);
            var wordCount = line!.Split(' ').Length;
            wordsInChunk += wordCount;
            if (wordsInChunk <= 300) continue;
            
            _chunks.Add(chunk.ToString());
            Console.WriteLine($"Processing chunk: {chunkIndex}");

            var nextChunk = chunk.ToString();

            chunk.Clear();
            chunkIndex++;

            var entitiesAndRelationshipsPathJson = $"{cachePath}entities-and-relationships-{chunkIndex}.json";
            var chunkPath = $"{cachePath}chunk-{chunkIndex}.txt";

            await File.WriteAllTextAsync(chunkPath, nextChunk);

            wordsInChunk = 0;

            var distinctRelationships = _allRelationships.Select(x => x.Value.Relationship).Distinct().ToArray();

            var initialRelationshipDetectionQuery = FindEntitiesAndRelationships.Prompt;
            var secondaryRelationshipDetectionQuery = FindMissingRelationships.Prompt(distinctRelationships);

            var options = new ChatCompletionsOptions()
            {
                DeploymentName = "gpt-4o",
                ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
                Messages =
                {
                    new ChatRequestSystemMessage(
                        initialRelationshipDetectionQuery
                    ),
                    new ChatRequestUserMessage(nextChunk)
                }
            };

            LlmOutput llmOutput = default!;
            if (useCache && File.Exists(entitiesAndRelationshipsPathJson))
            {
                var json = await File.ReadAllTextAsync(entitiesAndRelationshipsPathJson);
                llmOutput = JsonConvert.DeserializeObject<LlmOutput>(json)!;
            }
            else
            {
                var response = await _client.GetChatCompletionsAsync(options);
                var json = response.Value.Choices[0].Message.Content;
                llmOutput = JsonConvert.DeserializeObject<LlmOutput>(json)!;

                //not done yet. Fire another query to get missing relationships:
                var options2 = new ChatCompletionsOptions()
                {
                    DeploymentName = "gpt-4o",
                    ResponseFormat = ChatCompletionsResponseFormat.JsonObject,
                    Messages =
                    {
                        new ChatRequestSystemMessage(
                            secondaryRelationshipDetectionQuery
                        ),
                        new ChatRequestUserMessage(
                            $"""
                             ---------
                             DETECTED ENTITIES
                             ---------
                             {string.Join('\n', llmOutput.Entities.Select(x => $"{x.Name}|{x.Type})"))}
                             ---------

                             ---------
                             DETECTED RELATIONSHIPS
                             ---------
                             {string.Join('\n', llmOutput.Relationships.Select(x => $"{x.From.Name}|{x.To.Name}|{x.Relationship})"))}
                             ---------

                             INPUT TEXT
                             ---------
                             {nextChunk}
                             ---------
                             """
                        )
                    }
                };

                var response2 = await _client.GetChatCompletionsAsync(options2);
                var json2 = response2.Value.Choices[0].Message.Content;
                var llmOutput2 = Newtonsoft.Json.JsonConvert.DeserializeObject<LlmOutput>(json2)!;
                llmOutput.Relationships = llmOutput.Relationships.Union(llmOutput2.Relationships).ToArray();

                await File.WriteAllTextAsync(
                    entitiesAndRelationshipsPathJson,
                    JsonConvert.SerializeObject(llmOutput, Formatting.Indented));
            }

            foreach (var entity in llmOutput!.Entities)
            {
                LoadEntity(entity, chunkIndex);
            }

            foreach (var llmRelationship in llmOutput.Relationships)
            {
                var from = LoadEntity(llmRelationship.From, chunkIndex);
                var to = LoadEntity(llmRelationship.To, chunkIndex);

                if (_allRelationships.TryGetValue((from, to, llmRelationship.Relationship.ToLowerInvariant()),
                        out var discoveredRelationship))
                {
                    discoveredRelationship.Reasons.Add(new Reasoning()
                        { Chunk = chunkIndex, Reason = llmRelationship.Reason });
                }
                else
                {
                    _allRelationships.Add((from, to, llmRelationship.Relationship.ToLowerInvariant()),
                        new Relation()
                        {
                            From = from,
                            To = to,
                            Relationship = llmRelationship.Relationship.ToLowerInvariant(),
                            Reasons = [new Reasoning() { Chunk = chunkIndex, Reason = llmRelationship.Reason }]
                        });
                }
            }
        }
    }

    public async Task DetectDuplicateEntities(string cachePath)
    {
        //make sure we don't have duplicate entities that are effectively the same. Needs another prompt:
        var duplicatesFile = $"{cachePath}duplicate-entities.json";

        Duplicates? duplicates = null;
        if (File.Exists(duplicatesFile))
        {
            duplicates = JsonConvert.DeserializeObject<Duplicates>(await File.ReadAllTextAsync(duplicatesFile));
        }
        else
        {
            var deduplicationPrompt = DeDuplicateEntities.Prompt;

            var duplicateDetection = new ChatCompletionsOptions("gpt-4o",
            [
                new ChatRequestSystemMessage(deduplicationPrompt),
                new ChatRequestUserMessage(
                    string.Join('\n', _allEntities.Select(x => $"{x.Value.Name}|{x.Value.Type}"))
                ),
            ])
            {
                ResponseFormat = ChatCompletionsResponseFormat.JsonObject
            };

            var messageContent = (await _client.GetChatCompletionsAsync(duplicateDetection)).Value.Choices[0].Message
                .Content;
            duplicates = JsonConvert.DeserializeObject<Duplicates>(messageContent);

            await File.WriteAllTextAsync(duplicatesFile, messageContent);
        }

//collapse the duplicates
        foreach (var duplicate in duplicates!.duplicates)
        {
            var primaryEntity = _allEntities[duplicate.primary.Split('|')[0].ToLowerInvariant()];
            foreach (var duplicateEntity in duplicate.duplicates)
            {
                var duplicateEntityName = duplicateEntity.Split('|')[0].ToLowerInvariant();
                if (_allEntities.TryGetValue(duplicateEntityName, out var entity))
                {
                    primaryEntity.Reasons.AddRange(entity.Reasons);
                    _allEntities.Remove(duplicateEntityName);
                }

                var relationshipsPointingToDuplicate =
                    _allRelationships.Where(x =>
                        x.Value.From.Name == duplicateEntityName || x.Value.To.Name == duplicateEntityName).ToArray();

                foreach (var relationship in relationshipsPointingToDuplicate)
                {
                    _allRelationships.Remove(relationship.Key);
                    if (relationship.Value.From.Name == duplicateEntityName)
                    {
                        relationship.Value.From = primaryEntity;
                    }

                    if (relationship.Value.To.Name == duplicateEntityName)
                    {
                        relationship.Value.To = primaryEntity;
                    }

                    var key = (relationship.Value.From, relationship.Value.To, relationship.Value.Relationship);
                    if (_allRelationships.TryGetValue(key, out var existingRelationship))
                    {
                        existingRelationship.Reasons.AddRange(relationship.Value.Reasons);
                    }
                    else
                    {
                        _allRelationships.Add(key, relationship.Value);
                    }
                }
            }
        }
    }

    public async Task SummariseAllEntities(string cachePath)
    {
        //simplify all the summaries. We will use these for context to the LLM when we perform queries
        foreach (var entity in _allEntities)
        {
            if (entity.Value.Reasons.Count > 1)
            {
                var summaryPath =
                    $"{cachePath}{entity.Value.Name.Replace(' ', '_').Replace('/', '_').Replace('\'', '_').Replace('"', '_')}.summary";
                if (File.Exists(summaryPath))
                {
                    entity.Value.SummarisedReasons = await File.ReadAllTextAsync(summaryPath);
                }
                else
                {
                    var summaryCleanup = new ChatCompletionsOptions("gpt-4o",
                    [
                        new ChatRequestSystemMessage(
                            "Take the given text, and provide a summary of the content. Keep all important points in there, but remove duplicate information! ONLY USE THE PROVIDED TEXT."),
                        new ChatRequestUserMessage(string.Join("\n", entity.Value.Reasons.Select(r => r.Reason)))
                    ]);
                    var response = (await _client.GetChatCompletionsAsync(summaryCleanup)).Value.Choices[0].Message
                        .Content;
                    await File.WriteAllTextAsync(summaryPath, response);
                    entity.Value.SummarisedReasons = response;
                }
            }
            else
            {
                entity.Value.SummarisedReasons =
                    entity.Value.Reasons.Count == 1 ? entity.Value.Reasons.Single().Reason : "";
            }

            Console.WriteLine($"Summarised entity {entity.Value.Name}");
        }
    }

    public async Task BuildGraph()
    {
        //load all this into the graph
        var neo4JClient = GraphDatabase.Driver("neo4j://localhost:7687", AuthTokens.Basic("neo4j", "password"));
        await using var session = neo4JClient.AsyncSession();

        foreach (var entity in _allEntities)
        {
            var parameters = new
            {
                name = entity.Value.Name.ToLowerInvariant(),
                summarisedReasons = entity.Value.SummarisedReasons,
                chunks = entity.Value.Reasons.Select(x => x.Chunk).Distinct().ToArray()
            };

            var output = await session.ExecuteWriteAsync(
                async tx =>
                {
                    var result = await tx.RunAsync(
                        $$"""
                          MERGE (e:{{entity.Value.Type.ToLowerInvariant()}} { name: $name })
                          ON CREATE
                              SET e.name = $name, e.summary = $summarisedReasons, e.chunks = $chunks
                          RETURN e.name + '::' + id(e)
                          """, parameters
                    );

                    var record = await result.SingleAsync();
                    return record[0].As<string>();
                });
            Console.WriteLine(output);
        }

        foreach (var relation in
                 _allRelationships.Values
                     .GroupBy(x =>
                     (
                         FromName: x.From.Name.ToLowerInvariant(),
                         FromType: x.From.Type.ToLowerInvariant(),
                         ToName: x.To.Name.ToLowerInvariant(),
                         ToType: x.To.Type.ToLowerInvariant(),
                         Relationship: x.Relationship.ToLowerInvariant()
                     )))
        {
            var output = await session.ExecuteWriteAsync(
                async tx =>
                {
                    var parameters = new
                    {
                        fromName = relation.Key.FromName,
                        toName = relation.Key.ToName,
                        reasons = relation.SelectMany(x => x.Reasons.Select(r => r.Reason)).Distinct().ToArray(),
                        chunks = relation.SelectMany(x => x.Reasons.Select(r => r.Chunk)).Distinct().ToArray()
                    };

                    var query = $@"
MATCH (f:{relation.Key.FromType.ToLowerInvariant()} {{name:$fromName}})
MATCH (t:{relation.Key.ToType.ToLowerInvariant()} {{name:$toName}})
MERGE (f)-[rel:{relation.Key.Relationship.ToLowerInvariant()}]->(t)
ON CREATE
    SET rel.reasons = $reasons, rel.chunks = $chunks
RETURN f.name + '-[:{relation.Key.Relationship.ToLowerInvariant()}]->' + t.name
";
                    var result = await tx.RunAsync(query, parameters);
                    var record = await result.SingleAsync();
                    return record[0].As<string>();
                });

            Console.WriteLine(output);
        }
    }

    public async Task BuildEntitySummaryVectorIndex()
    {

        //Build the AI Search index. We'll create 2 indexes. One representing entities, and another representing relationships.
        var searchIndexClient = new SearchIndexClient(new Uri(_searchEndpoint), new DefaultAzureCredential());
        var expectedIndexName = "entities";
        var searchClient = new SearchClient(new Uri(_searchEndpoint), expectedIndexName, new DefaultAzureCredential());

        var indexMetadata = new SearchIndex(expectedIndexName)
        {
            VectorSearch = new VectorSearch()
            {
                Profiles =
                {
                    new VectorSearchProfile("vector-profile", "vector-search-config")
                    {
                        CompressionName = "grf-scalar-quantizer",
                    }
                },
                Algorithms = { new HnswAlgorithmConfiguration("vector-search-config") },
                Compressions = { new ScalarQuantizationCompression("grf-scalar-quantizer") }
            },
            Fields = new List<SearchField>()
            {
                new("id", SearchFieldDataType.String)
                    { IsKey = true, IsFilterable = false, IsSearchable = false },
                new("entity", SearchFieldDataType.String)
                    { IsKey = false, IsFilterable = true, IsSearchable = true, IsSortable = true },
                new("type", SearchFieldDataType.String)
                    { IsKey = false, IsFilterable = true, IsSearchable = true, IsSortable = true, IsFacetable = true },
                new("summary", SearchFieldDataType.String)
                    { IsKey = false, IsFilterable = false, IsSearchable = true, IsSortable = false },
                new("embeddings", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsFilterable = false,
                    IsSearchable = true,
                    IsSortable = false, IsFacetable = false,
                    IsHidden = true,
                    VectorSearchProfileName = "vector-profile",
                    VectorSearchDimensions = 1536, //text-embedding-ada-002
                }
            }
        };

        await searchIndexClient.CreateOrUpdateIndexAsync(indexMetadata);

        //now generate embeddings for the entities, then index them
        foreach (var entity in _allEntities)
        {
            Console.WriteLine($"Embedding {entity.Value.Name.ToLowerInvariant()}");
            var embeddingText = $"{entity.Value.Name.ToLowerInvariant()} {entity.Value.Type.ToLowerInvariant()} {entity.Value.SummarisedReasons}";

            var embeddingsOptions = new EmbeddingsOptions("text-embedding-ada-002", [embeddingText]);
            var embeddings = await _client.GetEmbeddingsAsync(embeddingsOptions);
            var newItem = new
            {
                id = Convert.ToBase64String(Encoding.UTF8.GetBytes(entity.Value.Name.ToLowerInvariant())).Replace("/", "_"),
                entity = entity.Value.Name.ToLowerInvariant(),
                type = entity.Value.Type.ToLowerInvariant(),
                embeddings = embeddings.Value.Data[0].Embedding,
                summary = embeddingText
            };

            await searchClient.MergeOrUploadDocumentsAsync([newItem]);
        }
    }

    public async Task BuildBaseRagForComparison()
    {
        var searchIndexClient = new SearchIndexClient(new Uri(_searchEndpoint), new DefaultAzureCredential());

        //BASE RAG
        var baseRagIndexName = "baserag";
        var baseRagSearchClient = new SearchClient(new Uri(_searchEndpoint), baseRagIndexName, new DefaultAzureCredential());
        var baseRagIndexMetadata = new SearchIndex(baseRagIndexName)
        {
            VectorSearch = new VectorSearch()
            {
                Profiles =
                {
                    new VectorSearchProfile("vector-profile", "vector-search-config")
                    {
                        CompressionName = "grf-scalar-quantizer",
                    }
                },
                Algorithms = { new HnswAlgorithmConfiguration("vector-search-config") },
                Compressions = { new ScalarQuantizationCompression("grf-scalar-quantizer") }
            },
            Fields = new List<SearchField>()
            {
                new("id", SearchFieldDataType.String)
                    { IsKey = true, IsFilterable = false, IsSearchable = false },
                new("chunk", SearchFieldDataType.String)
                    { IsKey = false, IsFilterable = false, IsSearchable = true, IsSortable = false},
                new("embeddings", SearchFieldDataType.Collection(SearchFieldDataType.Single))
                {
                    IsFilterable = false, 
                    IsSearchable = true, 
                    IsSortable = false, IsFacetable = false,
                    IsHidden = true,
                    VectorSearchProfileName = "vector-profile",
                    VectorSearchDimensions = 1536, //text-embedding-ada-002
                }
            }
        };

//now generate embeddings for the entities, then index them
        await searchIndexClient.CreateOrUpdateIndexAsync(baseRagIndexMetadata);
        var indx = 1;
        foreach (var baseRagChunk in _chunks)
        {
            var embeddingsOptions = new EmbeddingsOptions("text-embedding-ada-002", [baseRagChunk]);
            var embeddings = await _client.GetEmbeddingsAsync(embeddingsOptions);

            var newItem = new
            {
                id = indx.ToString(),
                chunk = baseRagChunk,
                embeddings = embeddings.Value.Data[0].Embedding
            };

            indx++;
            Console.WriteLine($"Embedded {indx} out of {_chunks.Count}");
            await baseRagSearchClient.MergeOrUploadDocumentsAsync([newItem]);
        }
    }
}