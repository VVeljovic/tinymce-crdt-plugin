
using CrdtServer;
using CrdtServer.RabbitMQ.Connection;
using CrdtServer.RabbitMQ.Consumer;
using CrdtServer.RabbitMQ.Producer;
using CrdtServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddSignalR();
builder.Services.AddSingleton<CrdtDocumentStore>();
builder.Services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
builder.Services.AddSingleton<IOperationProducer, OperationProducer>();
builder.Services.AddHostedService<OperationConsumer>();
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

app.MapHub<CrdtHub>("/editorHub");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
