
using CrdtServer;
using CrdtServer.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// gRPC needs HTTP/2. Kestrel only allows HTTP/2 on a plain "http://" (non-TLS)
// endpoint if explicitly told to - otherwise it silently stays on HTTP/1.1 and
// every gRPC call fails.
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddGrpc();
builder.Services.AddSingleton<CrdtDocumentStore>();
builder.Services.AddSingleton<PeerSyncClient>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
    {
        policy.WithOrigins("http://localhost:5500", "http://127.0.0.1:5500", "null")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});


var app = builder.Build();


app.UseCors("AllowClient");

app.MapGrpcService<CrdtServer.Services.CrdtService>();
app.MapHub<CrdtHub>("/editorHub");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
