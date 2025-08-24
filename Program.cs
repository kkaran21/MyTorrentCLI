using Microsoft.Extensions.DependencyInjection;
using MyTorrentCLI.Dtos;
using MyTorrentCLI.Profiles;
using MyTorrentCLI.Services;
using MyTorrentCLI.Utils;

namespace MyTorrentCLI
{
    public class Program
    {
        static async Task Main(string[] args)
        {

            var services = new ServiceCollection();
            services.AddAutoMapper(typeof(TorrentProfile).Assembly);

            var serviceProvider = services.BuildServiceProvider();

            var _mapper = serviceProvider.GetRequiredService<AutoMapper.IMapper>();
            Console.WriteLine("Enter Path For .torrent File");
            string torrentFilePath = Console.ReadLine().Trim();
            Console.WriteLine("Enter Path For directory to save your download");
            string OutputPath = Console.ReadLine().Trim();
            Byte[] allBytes = File.ReadAllBytes(torrentFilePath);
            BencodeParser parser = new BencodeParser(allBytes);
            var parsedTrnt = parser.parse();

            TorrentFile file = _mapper.Map<TorrentFile>((Dictionary<string, object>)parsedTrnt);
            // IDownloader downloader = downloaderFactory.GetDownloader(file, );
            Downloader downloader = new Downloader(file, _mapper, OutputPath);
            await downloader.Download();
            Console.WriteLine($"File Saved to {OutputPath}");
        }
    }
}