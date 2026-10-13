param(
    # Use "+" to accept requests from other machines (needs URL reservation and firewall rule).
    [string]$HostName = "localhost",
    [int]$Port = 8790,
    [string]$Path = "/",
    [int]$StatusCode = 204
)

$ErrorActionPreference = "Stop"
if (-not $Path.StartsWith("/")) { $Path = "/$Path" }
if (-not $Path.EndsWith("/")) { $Path = "$Path/" }
$prefix = "http://${HostName}:$Port$Path"

$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)
$listener.Start()
Write-Host "Listening on $prefix (Ctrl+C to stop)" -ForegroundColor Green

try {
    while ($listener.IsListening) {
        $pending = $listener.GetContextAsync()
        # Poll so Ctrl+C can interrupt the wait.
        while (-not $pending.AsyncWaitHandle.WaitOne(250)) { }
        $context = $pending.GetAwaiter().GetResult()
        $request = $context.Request

        $reader = [System.IO.StreamReader]::new($request.InputStream, [System.Text.Encoding]::UTF8)
        $body = $reader.ReadToEnd()
        $reader.Dispose()

        Write-Host ""
        Write-Host "[$((Get-Date).ToString('HH:mm:ss'))] $($request.HttpMethod) $($request.Url.PathAndQuery) from $($request.RemoteEndPoint) ($($request.ContentType))" -ForegroundColor Cyan
        try {
            $body | ConvertFrom-Json | ConvertTo-Json -Depth 20 | Write-Host
        }
        catch {
            Write-Host $body
        }

        $context.Response.StatusCode = $StatusCode
        $context.Response.Close()
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-Host "Stopped." -ForegroundColor Yellow
}
