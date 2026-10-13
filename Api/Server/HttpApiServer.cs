using System;
using System.Net;
using System.Threading;

namespace JoksterCube.PlayerDetailSync.Api;

internal sealed class HttpApiServer : IDisposable
{
    private const string LocalBindAddress = "local";
    private const string LoopbackAddress = "127.0.0.1";
    private const string AllInterfacesAddress = "0.0.0.0";

    private readonly HttpListener _listener = new();
    private readonly HttpApiRequestHandler _requestHandler;
    private readonly Thread _worker;
    private volatile PlayerSnapshot _snapshot = new(DateTime.UtcNow.ToString("O"), Array.Empty<PlayerRecord>());

    internal HttpApiServer(string bindAddress, int port, string password)
    {
        _requestHandler = new HttpApiRequestHandler(password);
        string host = string.IsNullOrWhiteSpace(bindAddress) || bindAddress == AllInterfacesAddress
            ? "*"
            : string.Equals(bindAddress, LocalBindAddress, StringComparison.OrdinalIgnoreCase)
                ? LoopbackAddress
                : bindAddress;
        _listener.Prefixes.Add($"http://{host}:{port}/");
        _worker = new Thread(Listen) { IsBackground = true, Name = "PlayerDetailSync HTTP API" };
    }

    internal bool Start()
    {
        try
        {
            _listener.Start();
            _worker.Start();
            return true;
        }
        catch (Exception exception)
        {
            Plugin.ModLogger.LogError($"Could not start the player data API: {exception.Message}");
            _listener.Close();
            return false;
        }
    }

    internal void Publish(PlayerSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    private void Listen()
    {
        while (_listener.IsListening)
        {
            try
            {
                _requestHandler.HandleRequest(_listener.GetContext(), _snapshot);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                Plugin.ModLogger.LogError($"Player data API request failed: {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
    }
}
