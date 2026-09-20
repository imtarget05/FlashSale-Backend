// Minimal health probe for the container HEALTHCHECK (no wget/curl in the
// aspnet runtime image). Usage: dotnet HealthProbe.dll <url> — exits 0 on 2xx.
var url = args.Length > 0 ? args[0] : "http://127.0.0.1:8080/health/ready";
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
try
{
    using var res = await client.GetAsync(url);
    return res.IsSuccessStatusCode ? 0 : 1;
}
catch
{
    return 1;
}
