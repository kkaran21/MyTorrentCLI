namespace MyTorrentCLI.Dtos;

public class TrackerResponse
{
    public string FailureReason { get; set; }
    public string WarningMessage { get; set; }
    public int Interval { get; set; } //in seconds
    public string TrackerId { get; set; }
    public string Complete { get; set; } //Seeders
    public string Incomplete { get; set; } //Leechers
    public int Seeders { get; set; } 
    public int Leechers { get; set; } 
    public List<string> Peers { get; set; }
        // Non-compact peer list (structured dictionaries)
    public List<PeerList> PeerList { get; set; }

}

    public class PeerList
    {
        public string PeerId { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
    }