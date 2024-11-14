

//run through the loop taking 15 lines at a time
var basePath = "/Users/graemefoster/code/github/graemefoster/PoorMansGraphRag/";
var cachePath = $"{basePath}cache/";

var graphRag = new GraphRag();

await graphRag.CrackDocument(cachePath, $"{basePath}sample-text.txt");

await graphRag.DetectDuplicateEntities(cachePath);

await graphRag.SummariseAllEntities(cachePath);

await graphRag.BuildGraph();

await graphRag.BuildEntitySummaryVectorIndex();

await graphRag.BuildBaseRagForComparison();


Console.WriteLine("DONE!");