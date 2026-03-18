using Symphony.Host.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.AddSymphonyHost();

var app = builder.Build();
app.MapSymphonyHost();

app.Run();
