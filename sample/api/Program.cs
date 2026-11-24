var app = WebApplication.Create(args);

app.MapGet("/", () => "Sample API is running");

app.Run();
