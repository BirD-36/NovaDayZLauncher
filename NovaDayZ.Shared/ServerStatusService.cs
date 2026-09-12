using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace NovaDayZ.Shared;

public class ServerStatusService
{
    private static readonly byte[] A2S_INFO =
    {
        0xFF,0xFF,0xFF,0xFF,0x54,0x53,0x6F,0x75,0x72,0x63,0x65,0x20,
        0x45,0x6E,0x67,0x69,0x6E,0x65,0x20,0x51,0x75,0x65,0x72,0x79,0x00
    };

    public async Task<(int Players, int Max, int PingMs)> QueryAsync(string ip, int port)
    {
        var sw = Stopwatch.StartNew();
        using var udp = new UdpClient();
        var ep = new IPEndPoint(IPAddress.Parse(ip), port);
        using var cts = new CancellationTokenSource(2500);   // жёсткий таймаут

        var request = (byte[])A2S_INFO.Clone();
        await udp.SendAsync(request, request.Length, ep);
        var res = await udp.ReceiveAsync(cts.Token);
        var b = res.Buffer;

        if (b.Length >= 9 && b[4] == 0x41)   // challenge — повторяем с ним
        {
            var withChallenge = new byte[request.Length + 4];
            request.CopyTo(withChallenge, 0);
            Array.Copy(b, 5, withChallenge, request.Length, 4);
            await udp.SendAsync(withChallenge, withChallenge.Length, ep);
            res = await udp.ReceiveAsync(cts.Token);
            b = res.Buffer;
        }
        sw.Stop();

        if (b.Length < 10 || b[4] != 0x49)
            throw new Exception("Bad A2S response");

        int i = 6;
        for (int s = 0; s < 4; s++) while (b[i++] != 0) { }
        i += 2;
        return (b[i], b[i + 1], (int)sw.ElapsedMilliseconds);
    }
}