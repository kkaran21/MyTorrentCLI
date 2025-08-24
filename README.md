# MyTorrentCLI

A lightweight, multi-threaded command-line BitTorrent client implementation written in C# that demonstrates advanced peer-to-peer networking concepts and asynchronous programming patterns.

## 🎯 Overview

MyTorrentCLI is a fully functional BitTorrent client that implements the complete BitTorrent protocol from scratch, supporting both HTTP and UDP tracker protocols. The project showcases sophisticated networking architecture, concurrent programming with channels, and efficient file I/O operations.

## 🚀 Features

### Core BitTorrent Protocol Implementation
- **Complete BitTorrent Protocol Support** - Full implementation of BEP-0003 (BitTorrent Protocol Specification)
- **Bencode Parser** - Custom Bencode encoder/decoder for .torrent file parsing
- **Multi-protocol Tracker Support** - Both HTTP and UDP tracker announcements
- **Peer Wire Protocol** - Complete peer communication including handshake, bitfield exchange, and piece requests
- **Piece Verification** - SHA-1 hash validation for downloaded pieces

### Advanced Networking Features
- **Multi-threaded Architecture** - Concurrent connections to multiple peers using async/await patterns
- **Channel-based Work Distribution** - Producer-consumer pattern using .NET Channels for efficient piece distribution
- **Connection Management** - Automatic retry logic, timeout handling, and connection pooling
- **BitArray for Piece Tracking** - Efficient memory usage for tracking available pieces across peers

### File Management
- **Single and Multi-file Torrents** - Support for both single-file and multi-file torrent structures  
- **Direct Disk I/O** - Efficient streaming writes directly to disk
- **Progress Tracking** - Real-time download progress with piece-level granularity
- **Atomic File Operations** - Safe file writing with proper directory structure creation

### Performance Optimizations
- **Pipelined Requests** - Multiple outstanding piece requests per peer connection
- **Configurable Block Size** - 16KB block size for optimal network utilization
- **Memory Efficient** - Streaming operations to minimize memory footprint
- **Connection Reuse** - Persistent connections with connection pooling

## 🛠 Technical Architecture

### Project Structure
```
MyTorrentCLI/
├── Dtos/                    # Data Transfer Objects
│   ├── TorrentFile.cs      # Torrent metadata representation
│   ├── TrackerResponse.cs  # Tracker response models
│   └── PieceProgress.cs    # Piece download state tracking
├── Services/
│   └── Downloader.cs       # Core download orchestration
├── Utils/                   # Utility classes
│   ├── BencodeParser.cs    # Bencode encoding/decoding
│   ├── MessageTypes.cs     # BitTorrent message type definitions
│   └── PeerReqUtil.cs      # Peer protocol utilities
├── Extensions/
│   └── StreamExtension.cs  # Stream helper methods
├── Profiles/
│   └── TorrentProfile.cs   # AutoMapper configuration
└── Program.cs              # Application entry point
```

### Key Components

#### BencodeParser
- Recursive descent parser for Bencode format
- Handles integers, strings, lists, and dictionaries
- Automatic info hash calculation during parsing
- Support for binary data (pieces and peer lists)

#### Downloader Service  
- Producer-consumer architecture using .NET Channels
- Concurrent peer connections with configurable limits
- Automatic piece verification and retry logic
- Progress tracking and completion detection

#### Peer Protocol Implementation
- Complete BitTorrent handshake protocol
- Message handling (choke/unchoke, interested/not interested, have, bitfield, request, piece)
- Pipelined request management with backlog control
- Robust error handling and connection recovery

## 📋 Prerequisites

- **.NET 8.0** or higher
- **Windows/Linux/macOS** - Cross-platform compatibility
- **Network connectivity** - For tracker and peer communications

## 💾 Dependencies

- `AutoMapper` - Object-to-object mapping
- `Microsoft.Extensions.DependencyInjection` - Dependency injection container

## ⚡ Quick Start

### Installation
1. Clone the repository:
```bash
git clone https://github.com/kkaran21/MyTorrentCLI.git
cd MyTorrentCLI
```

2. Build the project:
```bash
dotnet build
```

3. Run the application:
```bash
dotnet run
```

### Usage
1. **Enter torrent file path** when prompted:
   ```
   Enter Path For .torrent File: /path/to/your/file.torrent
   ```

2. **Enter download directory** when prompted:
   ```
   Enter Path For directory to save your download: /path/to/download/directory
   ```

3. **Monitor progress** as the client:
   - Connects to trackers (HTTP/UDP)
   - Discovers and connects to peers
   - Downloads pieces with real-time progress
   - Verifies piece integrity using SHA-1 hashes
   - Assembles the complete file(s)

## 🎥 Demo

The client has been successfully tested with real torrents, including downloading the latest **Debian ISO** demonstrating its capability to handle large files efficiently. 

**Video demonstration:** [Download Demo](https://drive.google.com/file/d/1jkN_FNL_NnQ2Xb_GkJ6oHeMjjMHiTiJU/view?usp=sharing)

## 🔧 Technical Implementation Details

### Async/Await Patterns
```csharp
// Example of concurrent peer handling
List<Task> peerTasks = new List<Task>();
foreach (var peer in response.Peers)
{
    peerTasks.Add(Task.Run(() => StartConsumerWorker(peer, _cts.Token)));
}
await Task.WhenAll(peerTasks);
```

### Channel-based Work Distribution
```csharp
// Producer-consumer pattern for piece distribution
Channel<PieceWork> _workQueue = Channel.CreateBounded<PieceWork>(
    new BoundedChannelOptions(torrentFile.Pieces.Count)
    {
        SingleReader = false,
        SingleWriter = false
    });
```

### Piece Verification
```csharp
// SHA-1 hash verification for downloaded pieces
if (!piece.hash.SequenceEqual(SHA1.HashData(downloadedData)))
{
    log($"Hash mismatch for piece {piece.index}, requeueing");
    await _workQueue.Writer.WriteAsync(piece);
    continue;
}
```

## 🎓 Learning Outcomes

This project demonstrates proficiency in:

- **Network Programming** - TCP connections, binary protocol implementation
- **Concurrent Programming** - Async/await, channels, thread-safe operations  
- **System Design** - Producer-consumer patterns, connection pooling
- **Protocol Implementation** - BitTorrent protocol, Bencode parsing
- **Performance Optimization** - Memory management, I/O efficiency
- **Error Handling** - Robust retry logic, connection recovery

## 🔍 Key Features Demonstrated

### Protocol Compliance
- Full BitTorrent BEP-0003 implementation
- Support for both compact and non-compact peer lists
- Proper handling of multi-file torrents
- Complete message protocol implementation

### Advanced C# Concepts
- Channels for producer-consumer patterns
- AutoMapper for object mapping
- Extension methods for enhanced functionality
- Dependency injection container usage
- Asynchronous streams with `IAsyncEnumerable`

### Network Resilience
- Connection retry logic with exponential backoff
- Timeout handling for network operations
- Graceful handling of peer disconnections
- Support for both IPv4 and IPv6 (where applicable)

## 📊 Performance Characteristics

- **Concurrent Connections**: Up to 30+ simultaneous peer connections
- **Block Size**: 16KB blocks for optimal throughput
- **Memory Usage**: Streaming operations minimize memory footprint
- **Download Speed**: Limited by network bandwidth and peer availability
- **Scalability**: Efficient handling of large torrents (tested with GB+ files)

## 🐛 Error Handling

The client includes comprehensive error handling for:
- Network timeouts and connection failures
- Malformed torrent files
- Tracker communication errors  
- Piece hash mismatches
- File system I/O errors
- Peer protocol violations

## 🤝 Contributing

This is a learning project demonstrating BitTorrent protocol implementation. Feel free to:
- Report issues or bugs
- Suggest improvements
- Fork for educational purposes
- Submit pull requests

## 📄 License

This project is provided as-is for educational and demonstration purposes. Please respect copyright laws when using BitTorrent technology.

## 🙏 Acknowledgments

- BitTorrent Protocol Specification (BEP-0003)
- .NET documentation and community
- Various open-source BitTorrent implementations for reference

---

**Built with ❤️ using C# and .NET**

*This project showcases advanced networking concepts, concurrent programming patterns, and protocol implementation skills suitable for distributed systems and network programming roles.*
