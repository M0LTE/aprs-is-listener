using AprsSharp.AprsParser;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace aprs_is_listener;

internal partial class AprsIsListener(ILogger<AprsIsListener> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ClientLoop();
        return Task.CompletedTask;
    }

    private readonly CancellationTokenSource clientLoopTokenSource = new();

    private async Task ClientLoop()
    {
        do
        {
            try
            {
                using var tcpClient = new TcpClient();
                logger.LogInformation("Connecting...");
                await tcpClient.ConnectAsync("euro.aprs2.net", 14580, clientLoopTokenSource.Token);
                logger.LogInformation("Connected");
                await HandleConnection(tcpClient.GetStream());
            }
            catch (Exception ex)
            {
                logger.LogError("Error: {error}", ex);
            }
            finally
            {
                logger.LogInformation("Sleeping...");
                await Task.Delay(10000);
            }
        } while (!clientLoopTokenSource.Token.IsCancellationRequested);
    }

    private async Task HandleConnection(NetworkStream networkStream)
    {
        using var reader = new StreamReader(networkStream);
        using var writer = new StreamWriter(networkStream);
        writer.AutoFlush = true;

        await reader.ReadLineAsync();

        // https://www.aprs-is.net/javAPRSFilter.aspx
        // poimqstunw
        /*
            p = Position packets
            o = Objects
            i = Items
            m = Message
            q = Query
            s = Status
            t = Telemetry
            u = User-defined
            n = NWS format messages and objects
            w = Weather
         */

        const string filter = "ps";
        //const string filter = "poimqstunw";
        await writer.WriteAsync($"user N0CALL pass -1 vers m0ltetestclient 0.0.1 filter t/{filter}\n");

        do
        {
            using var readToken = new CancellationTokenSource(10000);
            using var combined = CancellationTokenSource.CreateLinkedTokenSource(clientLoopTokenSource.Token, readToken.Token);
            string? line;
            try
            {
                line = await reader.ReadLineAsync(combined.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Read timeout");
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("#"))
            {
                continue;
            }

            Packet packet;
            try
            {
                packet = new Packet(line);
            }
            catch (Exception)
            {
                //logger.LogWarning("Parse fail: {error}; frame: {frame}", ex.Message, line);
                continue;
            }

            if (packet.InfoField is PositionInfo position)
            {
                await ProcessPosition(packet.Sender, position.Position);
            }
            else if (packet.InfoField is StatusInfo statusInfo)
            {
                if (statusInfo.Position != null)
                {
                    await ProcessPosition(packet.Sender, statusInfo.Position);
                }
            }
            else
            {
                if (packet.InfoField is UnsupportedInfo || packet.InfoField is MessageInfo)
                {
                    continue;
                }

                logger.LogInformation(packet.InfoField.GetType().Name);
            }
        } while (!clientLoopTokenSource.IsCancellationRequested);
    }

    private async Task ProcessPosition(string senderWithSsid, Position position)
    {
        var lat = position.Coordinates.Latitude;
        var lon = position.Coordinates.Longitude;
        var alt = position.Coordinates.Altitude;
        var horizontalAccuracy = position.Coordinates.HorizontalAccuracy;
        var verticalAccuracy = position.Coordinates.VerticalAccuracy;
        var speed = position.Coordinates.Speed;
        var course = position.Coordinates.Course;
        var positionUnknown = position.Coordinates.IsUnknown;
        var symbolCode = position.SymbolCode;
        var symbolTableIdentifier = position.SymbolTableIdentifier;

        if (lat == (int)lat && lon == (int)lon)
        {
            return;
        }

        if (positionUnknown)
        {
            return;
        }

        if (lat == 0 && lon == 0)
        {
            return;
        }

        if (!IsUkCallsign(senderWithSsid))
        {
            return;
        }

        if (!IsCallsign().IsMatch(senderWithSsid))
        {
            return;
        }

        var sender = senderWithSsid.Split('-')[0];

        if (double.IsNaN(alt))
        {
            logger.LogInformation("{call} {withSsid} {lat} {lon}", sender, senderWithSsid, lat, lon);
        }
        else
        {
            logger.LogInformation("{call} {withSsid} {lat} {lon} {alt}", sender, senderWithSsid, lat, lon, alt);
        }
    }

    private static bool IsUkCallsign(string sender) => sender.StartsWith("M") || sender.StartsWith("G") || sender.StartsWith("2");

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await clientLoopTokenSource.CancelAsync();
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,2}[0-9][A-Za-z0-9]{1,4}")]
    private static partial Regex IsCallsign();
}
