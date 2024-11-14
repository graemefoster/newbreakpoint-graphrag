namespace PoorMansGraphRag;

class Relation {
    public Entity From { get; set; }
    public Entity To { get; set; }
    public string Relationship { get; set; }
    public List<Reasoning> Reasons { get; set; }= new List<Reasoning>();
}