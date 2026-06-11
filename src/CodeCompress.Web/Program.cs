using CodeCompress.Web;

var port = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_PORT"), out var p) ? p : 7070;
var bind = Environment.GetEnvironmentVariable("ASPNETCORE_BIND") ?? "localhost";

var app = WebHostFactory.Build(port, bind);
await app.RunAsync().ConfigureAwait(false);
