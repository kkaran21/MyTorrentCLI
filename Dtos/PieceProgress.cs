using System.Collections;
using System.Net.Sockets;

public class PieceWork
{
    public int index { get; set; }
    public byte[]? hash { get; set; }
    public long length { get; set; }
}

public class PieceResult
{
    public int index { get; set; }
    public Byte[]? buff { get; set; }
}

public class PieceProgress
{
    public int index { get; set; }
    public byte[]? buff { get; set; }
    public int downloaded { get; set; }
    public int requested { get; set; }
    public int backlog { get; set; }
    public Client? client { get; set; }
}

public class Client
{
    public TcpClient conn { get; set; }
    public bool isChoked { get; set; }
    public BitArray bitArray { get; set; }
    public bool isHandShakeSuccess { get; set; }
    public Stream stream { get; set; }
}