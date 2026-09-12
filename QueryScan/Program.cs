using System.Net;
using System.Net.Sockets;

var ip = args.Length > 0 ? args[0] : "80.242.59.221";
byte[] A2S = { 0xFF,0xFF,0xFF,0xFF,0x54,0x53,0x6F,0x75,0x72,0x63,0x65,0x20,
               0x45,0x6E,0x67,0x69,0x6E,0x65,0x20,0x51,0x75,0x65,0x72,0x79,0x00 };

Console.WriteLine($"Scanning {ip} (1024-65535), please wait ~3-5 min...");
var found = new System.Collections.Concurrent.ConcurrentBag<int>();

await Parallel.ForEachAsync(
    Enumerable.Range(1024, 65535 - 1024),
    new ParallelOptions { MaxDegreeOfParallelism = 250 },
    async (port, ct) =>
    {
        try
        {
            using var udp = new UdpClient();
            var ep = new IPEndPoint(IPAddress.Parse(ip), port);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(900);
            await udp.SendAsync(A2S, A2S.Length, ep);
            var res = await udp.ReceiveAsync(cts.Token);
            var b = res.Buffer;
            var hex = BitConverter.ToString(b, 0, Math.Min(8, b.Length));
            found.Add(port);
            Console.WriteLine($"RESPONSE port={port} len={b.Length} hex={hex}");
        }
        catch { }
    });

Console.WriteLine("SCAN DONE. Responded ports: " + string.Join(", ", found.OrderBy(x => x)));