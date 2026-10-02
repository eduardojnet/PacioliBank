var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// RNF-013: vivo e pronto sao verificacoes distintas.
// Vivo nao depende de nenhuma dependencia externa.
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

// Pronto passara a refletir o armazenamento primario quando a persistencia entrar.
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.Run();
