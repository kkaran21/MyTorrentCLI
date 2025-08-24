using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using AutoMapper;
using MyTorrentCLI.Dtos;
using MyTorrentCLI.Extensions;
using MyTorrentCLI.Utils;

namespace MyTorrentCLI.Services
{
    public class Downloader
    {
        Channel<PieceWork> _workQueue;
        TorrentFile _torrentFile;
        IMapper _mapper;
        string _outputPath;
        byte[] _resultBytes;
        private readonly int BLOCK_SIZE = 16384;
        int _maxBacklog;
        private int _completedPieces = 0;
        CancellationTokenSource _cts = new CancellationTokenSource();


        public Downloader(TorrentFile torrentFile, IMapper mapper, string outputPath)
        {
            _mapper = mapper;
            _torrentFile = torrentFile;
            _workQueue = Channel.CreateBounded<PieceWork>(new BoundedChannelOptions(torrentFile.Pieces.Count)
            {
                SingleReader = false,
                SingleWriter = false
            });
            _resultBytes = new byte[_torrentFile.Length];
            _maxBacklog = (int)_torrentFile.PieceLength / (BLOCK_SIZE * 2);
            _outputPath = outputPath;
        }

        private async Task ProduceWork(ChannelWriter<PieceWork> writer)
        {
            for (int i = 0; i < _torrentFile.Pieces.Count; i++)
            {
                var work = new PieceWork()
                {
                    index = i,
                    length = GetPieceLength(i, _torrentFile.PieceLength, _torrentFile.Length),
                    hash = _torrentFile.Pieces[i]
                };

                await writer.WriteAsync(work);
            }
        }

        private long GetPieceLength(int index, long pieceLength, long fileLength)
        {
            long remainderLength = fileLength - (index * pieceLength);
            long length = Math.Min(remainderLength, pieceLength);
            return length;
        }

        public async Task Download()
        {
            await ProduceWork(_workQueue.Writer);
            TrackerResponse response = await GetPeers(_torrentFile.Announce);
            List<Task> peerTasks = new List<Task>();
            if (response.Peers.Any())
            {
                foreach (var peer in response.Peers) //compact peers
                {
                    peerTasks.Add(Task.Run(() => StartConsumerWorker(peer, _cts.Token)));
                }
            }
            else
            {
                foreach (var peer in response.PeerList) //compact peers
                {
                    peerTasks.Add(Task.Run(() => StartConsumerWorker($"{peer.Ip}:{peer.Port}", _cts.Token)));
                }
            }

            await Task.WhenAll(peerTasks);

        }

        private async Task StartConsumerWorker(string peerAddr, CancellationToken cancellationToken)
        {
            int maxRetries = 10;
            int retryCount = 0;

            while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {

                Client client = null;
                try
                {
                    log($"Attempting to connect to peer: {peerAddr}");
                    client = await ConnectToPeer(peerAddr);

                    if (client?.isHandShakeSuccess != true)
                    {
                        log($"Handshake failed with peer: {peerAddr}");
                        retryCount++;
                        continue;
                    }

                    log($"Successfully connected to peer: {peerAddr}, has {client.bitArray.Cast<bool>().Count(b => b)} pieces");

                    await sendMsgToPeer(client, [(byte)MessageTypes.MsgUnchoke]);
                    await sendMsgToPeer(client, [(byte)MessageTypes.MsgInterested]);

                    await foreach (var piece in _workQueue.Reader.ReadAllAsync())
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!client.bitArray[piece.index])
                        {
                            await _workQueue.Writer.WriteAsync(piece);
                            continue;
                        }

                        try
                        {
                            byte[] downloadedData = await AttemptPieceDownload(piece, client);

                            if (!piece.hash.SequenceEqual(SHA1.HashData(downloadedData)))
                            {
                                log($"Hash mismatch for piece {piece.index}, requeueing");
                                await _workQueue.Writer.WriteAsync(piece);
                                continue;
                            }

                            float progress = ((float)_completedPieces / (float)_torrentFile.Pieces.Count) * 100.0f;
                            log($"({progress:F2}%) Successfully downloaded piece #{piece.index} from {peerAddr}");
                            Array.Copy(downloadedData, 0, _resultBytes, (long)piece.index * _torrentFile.PieceLength, downloadedData.Length);

                            if (Interlocked.Increment(ref _completedPieces) == _torrentFile.Pieces.Count)
                            {
                                log($"Saving Downloaded file to {_outputPath}...");
                                if (!_torrentFile.Files.Any())
                                {
                                    using (var fileStream = new FileStream($"{_outputPath}/{_torrentFile.Name}", FileMode.OpenOrCreate, FileAccess.Write))
                                    {
                                        await fileStream.WriteAsync(_resultBytes, 0, _resultBytes.Length);
                                    }
                                }
                                else
                                {
                                    int subfileOffset = 0;
                                    for (int i = 0; i < _torrentFile.Files.Count; i++)
                                    {
                                        var relativePath = Path.Combine(_torrentFile.Files[i].path.ToArray());
                                        var outPath = Path.Combine($"{_outputPath}", _torrentFile.Name, relativePath);

                                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                                        using var fileStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                                        await fileStream.WriteAsync(_resultBytes, subfileOffset, (int)_torrentFile.Files[i].length);

                                        subfileOffset += (int)_torrentFile.Files[i].length;
                                    }
                                }

                                _workQueue.Writer.Complete();
                                _cts.Cancel();
                                return;
                            }

                        }
                        catch (Exception ex)
                        {
                            log($"Failed to download piece {piece.index} from {peerAddr}: {ex.Message}");
                            await _workQueue.Writer.WriteAsync(piece);
                            break;
                        }
                    }
                }
                catch (SocketException ex)
                {
                    retryCount++;
                    log($"Socket error with peer {peerAddr}: {ex.Message}");
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * retryCount));
                    }
                }
                catch (IOException ex)
                {
                    retryCount++;
                    log($"IO error with peer {peerAddr}: {ex.Message}");
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * retryCount));
                    }
                }
                catch (Exception ex)
                {
                    retryCount++;
                    log($"Unexpected error with peer {peerAddr}: {ex.Message}");
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5 * retryCount));
                    }
                }
                finally
                {
                    client?.conn?.Close();
                }
            }
            if (retryCount >= maxRetries)
            {
                log($"Failed to establish stable connection with peer {peerAddr} after {maxRetries} attempts");
            }

        }


        private async Task<byte[]> AttemptPieceDownload(PieceWork piece, Client client)
        {

            PieceProgress state = new PieceProgress();
            state.client = client;
            state.index = piece.index;
            state.buff = new byte[piece.length];

            while (state.downloaded < piece.length)
            {
                if (!state.client.isChoked)
                {
                    while (state.backlog < _maxBacklog && state.requested < piece.length)
                    {
                        int blockSize = (int)Math.Min(piece.length - state.requested, Convert.ToInt64(BLOCK_SIZE));
                        await SendPieceRequest(state, blockSize);
                        state.backlog++;
                        state.requested += blockSize;
                    }
                }

                await ReadIncomingMsg(state);

            }

            return state.buff;
        }

        private async Task ReadIncomingMsg(PieceProgress state)
        {
            byte[] lengthBuff = new byte[4];
            await state.client.stream.ReadExactlyAsync(lengthBuff, 0, lengthBuff.Length);
            Array.Reverse(lengthBuff);
            int msgSize = BitConverter.ToInt32(lengthBuff, 0);

            if (msgSize > 0)
            {
                byte[] msgBuff = new byte[msgSize];
                await state.client.stream.ReadExactlyAsync(msgBuff, 0, msgBuff.Length);

                switch (msgBuff.FirstOrDefault())
                {
                    case (byte)MessageTypes.MsgUnchoke:
                        state.client.isChoked = false;
                        break;
                    case (byte)MessageTypes.MsgChoke:
                        state.client.isChoked = true;
                        break;
                    case (byte)MessageTypes.MsgHave:
                        //update initial bitfield here 
                        //peer sends have message when it has a new piece
                        state.client.bitArray[ParseHave(msgBuff)] = true;
                        break;
                    case (byte)MessageTypes.MsgPiece:
                        state.backlog--;
                        state.downloaded += ParsePiece(state, msgBuff);
                        // log($"downloaded {((float)state.downloaded / (float)state.buff.Length) * 100.0} % of piece {state.index}");
                        break;
                    default:
                        log($"no message {msgBuff.Length}");
                        break;
                }
            }
        }

        private int ParsePiece(PieceProgress state, byte[] msgBuff)
        {
            int index = BitConverter.ToInt32(msgBuff.Skip(1).Take(4).Reverse().ToArray());
            int offset = BitConverter.ToInt32(msgBuff.Skip(5).Take(4).Reverse().ToArray());

            byte[] data = msgBuff.Skip(9).ToArray();

            if (index < 0 || index >= state.buff.Length)
                throw new InvalidOperationException($"PARSE ERROR: Unexpected piece index {index}, expecting value in 0..{state.buff.Length - 1}");

            if (index != state.index)
                throw new InvalidOperationException($"Unexpected piece index: expected {state.index}, got {index}");

            if (offset < 0 || offset + data.Length > state.buff.Length)
                throw new InvalidOperationException("Invalid block offset or length");

            Array.Copy(data, 0, state.buff, offset, data.Length);

            return data.Length;
        }

        private int ParseHave(byte[] msgBuff)
        {
            int index = BitConverter.ToInt32(msgBuff.Skip(1).Take(4).Reverse().ToArray());
            return index;
        }

        private async Task SendPieceRequest(PieceProgress state, long blocksize)
        {
            byte[] InterestedMsg = [(byte)MessageTypes.MsgRequest];
            byte[] payload = InterestedMsg
                                    .Concat(BitConverter.GetBytes(state.index).Reverse()) //to BigEndian Network Order
                                    .Concat(BitConverter.GetBytes(state.requested).Reverse())
                                    .Concat(BitConverter.GetBytes(Convert.ToInt32(blocksize)).Reverse())
                                    .ToArray();

            byte[] RequestBytes = BitConverter.GetBytes(payload.Length).Reverse().Concat(payload).ToArray();
            await state.client.stream.WriteAsync(RequestBytes, 0, RequestBytes.Length);

        }

        private async Task<Client> ConnectToPeer(string peerIp)
        {
            TcpClient client = new TcpClient();

            string ip = peerIp.Split(":")[0];
            int port = Convert.ToInt32(peerIp.Split(":")[1]);

            await client.ConnectAsync(
                ip,
                port,
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token
            );


            if (!client.Connected)
            {
                throw new Exception("Unable to connect");
            }

            Stream stream = client.GetStream();
            bool isHandshakeSuccess = await PerformHandshake(stream);
            BitArray bitArray = await RecieveBitfield(stream);

            return new Client
            {
                conn = client,
                stream = stream,
                isChoked = true,
                isHandShakeSuccess = isHandshakeSuccess,
                bitArray = bitArray
            };


        }
        private async Task<BitArray> RecieveBitfield(Stream stream)
        {
            BitArray bitArray = new BitArray(_torrentFile.Pieces.Count);

            try
            {
                byte[] lengthBuff = new byte[4];
                await stream.ReadExactlyAsync(lengthBuff, 0, lengthBuff.Length);
                Array.Reverse(lengthBuff);
                int msgSize = BitConverter.ToInt32(lengthBuff, 0);

                if (msgSize == 0)
                {
                    // Keep-alive message, return empty bitfield
                    return bitArray;
                }

                if (msgSize > 0)
                {
                    byte[] msgBuff = new byte[msgSize];
                    await stream.ReadExactlyAsync(msgBuff, 0, msgBuff.Length, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

                    byte messageType = msgBuff.FirstOrDefault();

                    switch (messageType)
                    {
                        case (byte)MessageTypes.MsgBitfield:
                            // Standard bitfield message
                            byte[] bitfieldData = msgBuff.Skip(1).ToArray();
                            return new BitArray(bitfieldData);

                        case (byte)MessageTypes.MsgHave:
                            // Single have message
                            int pieceIndex = ParseHave(msgBuff);
                            bitArray[pieceIndex] = true;
                            return bitArray;

                        case 0x0E: // Have All (Fast Extension)
                                   // Peer has all pieces
                            bitArray.SetAll(true);
                            return bitArray;

                        case 0x0F: // Have None (Fast Extension)
                                   // Peer has no pieces (already initialized to false)
                            return bitArray;

                        case (byte)MessageTypes.MsgPiece:
                            break;
                        default:
                            // No bitfield sent, assume peer has no pieces
                            log($"No bitfield received, assuming peer has no pieces. Message type: {messageType}");
                            return bitArray;
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Error receiving bitfield: {ex.Message}");
            }

            // Return empty bitfield if no message received
            return bitArray;
        }

        private async Task<bool> PerformHandshake(Stream stream)
        {
            bool isHandShakeSuccess;
            try
            {
                byte[] data = PeerReqUtil.getHandshakeReq(_torrentFile.InfoHash);
                await stream.WriteAsync(data, 0, data.Length);

                byte[] ResponseBuff = new byte[data.Length];

                await stream.ReadExactlyAsync(ResponseBuff, 0, ResponseBuff.Length);
                isHandShakeSuccess = _torrentFile.InfoHash.SequenceEqual(PeerReqUtil.getInfoHashFromHandshake(ResponseBuff));
            }
            catch (System.Exception e)
            {
                isHandShakeSuccess = false;
            }

            return isHandShakeSuccess;
        }


        private async Task<TrackerResponse> GetPeers(string AnnounnceUrl)
        {
            TrackerResponse trackerResponse = new TrackerResponse();

            if (AnnounnceUrl.StartsWith("http"))
            {
                using (var client = new HttpClient())
                {
                    Dictionary<string, string> Params = new Dictionary<string, string>
                {
                    {"info_hash", string.Concat(_torrentFile.InfoHash.Select(b => $"%{b:X2}"))},
                    {"peer_id", "00112233445566778869"},
                    {"port", "6881"},
                    {"uploaded", "0"},
                    {"downloaded", "0"},
                    {"compact", "1"},
                    {"left", _torrentFile.Length.ToString()}
                };

                    string queryParams = string.Empty;

                    foreach (var item in Params)
                    {
                        queryParams += $"{item.Key}={item.Value}&";
                    }

                    var response = await client.GetByteArrayAsync($"{AnnounnceUrl}?{queryParams}");

                    BencodeParser parser = new BencodeParser(response);
                    trackerResponse = _mapper.Map<TrackerResponse>(parser.parse());
                }
            }
            else if (AnnounnceUrl.StartsWith("udp"))
            {
                trackerResponse.Peers = new List<string>();
                _torrentFile.AnnounceList.Add(new List<string> { _torrentFile.Announce });
                foreach (var item in _torrentFile.AnnounceList)
                {
                    try
                    {
                        var uri = new Uri(item[0]);
                        IPEndPoint endPoint = new IPEndPoint(Dns.GetHostAddresses(uri.DnsSafeHost)[0], uri.Port);
                        UdpClient client = new UdpClient();
                        var connectReq = new byte[16];
                        var transactionId = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(0x41727101980)), 0, connectReq, 0, 8);
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(0)), 0, connectReq, 8, 4);
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(transactionId)), 0, connectReq, 12, 4);
                        await client.SendAsync(connectReq, connectReq.Length, endPoint);
                        var response = await client.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
                        Int32 action = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response.Buffer, 0));
                        Int32 responseTransactionId = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(response.Buffer, 4));
                        Int64 connectionId = IPAddress.NetworkToHostOrder(BitConverter.ToInt64(response.Buffer, 8));
                        if (responseTransactionId != transactionId || action != 0)
                            throw new Exception();

                        transactionId = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                        var announceRequest = new byte[98];
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(connectionId)), 0, announceRequest, 0, 8);
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(1)), 0, announceRequest, 8, 4); // announce
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(transactionId)), 0, announceRequest, 12, 4);
                        Array.Copy(_torrentFile.InfoHash, 0, announceRequest, 16, 20);
                        Array.Copy(System.Text.Encoding.ASCII.GetBytes("00112033445566778869"), 0, announceRequest, 36, 20);

                        // downloaded, left, uploaded
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(0L)), 0, announceRequest, 56, 8);
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(1000L)), 0, announceRequest, 64, 8); // example left
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(0L)), 0, announceRequest, 72, 8);

                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(2)), 0, announceRequest, 80, 4); // started
                        Array.Copy(BitConverter.GetBytes(0), 0, announceRequest, 84, 4); // IP
                        Array.Copy(BitConverter.GetBytes(RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue)), 0, announceRequest, 88, 4); // key
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(-1)), 0, announceRequest, 92, 4); // num_want
                        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)6881)), 0, announceRequest, 96, 2); // port

                        await client.SendAsync(announceRequest, announceRequest.Length, endPoint);

                        var newresponse = await client.ReceiveAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
                        action = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(newresponse.Buffer, 0));
                        responseTransactionId = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(newresponse.Buffer, 4));

                        if (action != 1 || responseTransactionId != transactionId)
                            throw new Exception("Announce failed.");

                        // Parse tracker response header
                        trackerResponse.Interval = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(newresponse.Buffer, 8));
                        trackerResponse.Leechers = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(newresponse.Buffer, 12));
                        trackerResponse.Seeders = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(newresponse.Buffer, 16));

                        Console.WriteLine($"Response: {newresponse.Buffer.Length} bytes, Seeders: {trackerResponse.Seeders}, Leechers: {trackerResponse.Leechers}");

                        // Parse peers starting at offset 20 (each peer = 6 bytes: 4 IP + 2 port)
                        for (int i = 20; i < newresponse.Buffer.Length; i += 6)
                        {
                            if (i + 5 >= newresponse.Buffer.Length) break; // Safety check

                            // Extract IP address (4 bytes)
                            var ipBytes = new byte[4];
                            Array.Copy(newresponse.Buffer, i, ipBytes, 0, 4);
                            var ip = new IPAddress(ipBytes);

                            // Extract and convert port (2 bytes from network byte order)
                            var portBytes = new byte[2];
                            Array.Copy(newresponse.Buffer, i + 4, portBytes, 0, 2);
                            var port = (ushort)((portBytes[0] << 8) | portBytes[1]);

                            trackerResponse.Peers.Add($"{ip}:{port}");
                        }


                    }
                    catch (System.Exception ee)
                    {

                    }
                }



            }

            return trackerResponse;



        }
        private async Task sendMsgToPeer(Client client, byte[] msg)
        {
            byte[] RequestBytes = BitConverter.GetBytes(msg.Length)
                                    .Reverse() //to BigEndian Network Order
                                    .Concat(msg)
                                    .ToArray();

            await client.stream.WriteAsync(RequestBytes, 0, RequestBytes.Length, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);
        }
        private void log(string msg)
        {
            Console.WriteLine(msg);
        }

    }
}