# How the backend works

A guide to the .NET code for people who know Angular/Node.js and are new to ASP.NET Core.
Each section maps a .NET idea to something you already know and points to the file where it's used.

---

## 1. The four projects (Clean Architecture)

```
Api  ──►  Infrastructure  ──►  Application  ──►  Domain
```
An arrow means "references". **Domain** has no dependencies at all.

| Project | What lives here | Node.js equivalent |
|---|---|---|
| **Domain** | Entities (`Conversation`, `CustomerAccount`...) and their **rules** | your models + validation, without any DB code |
| **Application** | Use cases (`ConversationService`), the AI agent, DTOs, **interfaces** like `IAppDbContext` | services layer |
| **Infrastructure** | EF Core `AppDbContext`, PostgreSQL/pgvector, LLM clients, background workers, seed data | repositories, DB config, external API clients |
| **Api** | Controllers, SignalR hub, JWT setup, `Program.cs` | Express routes + `app.js` |

**Why split it?** The rules don't depend on the database or the AI provider, so they can be unit-tested without them.
For example, `CustomerAccount.ChangePlan()` throws if a bill is overdue, and this is tested in `DomainTests.cs`.
To switch from Gemini to OpenAI or Azure OpenAI you only change configuration.

## 2. Dependency Injection (DI)

ASP.NET Core has DI built in. Services are registered once and requested through constructors:

```csharp
// Application/DependencyInjection.cs
services.AddScoped<ConversationService>();   // one instance per HTTP request / hub call

// Api/Controllers/AgentController.cs  (C# 12 "primary constructor")
public sealed class AgentController(ConversationService conversations, InsightsService insights) : ControllerBase
```

- `AddScoped`: one per request (DbContext and most services)
- `AddSingleton`: one for the whole app (the SignalR notifier, the indexing queue, the AI client)
- Background workers (`AddHostedService`) are singletons, so they create a **scope** to get a DbContext (see `KnowledgeIndexingWorker`).

This is the same idea as Angular's `inject()` / `providedIn: 'root'`.

## 3. EF Core (the ORM)

- `AppDbContext` = TypeORM DataSource. `DbSet<Conversation>` = a repository.
- LINQ is turned into SQL: `db.Conversations.Where(c => c.Status == ConversationStatus.WaitingForAgent)`.
- `ConversationService.SummaryProjection` is an `Expression` that EF turns into **one SQL query**
  (including the "last message" sub-query). This avoids the N+1 problem.
- **Change tracking:** load an entity, call a domain method, then call `SaveChangesAsync()`. EF works out the UPDATE/INSERT statements.
- Enums are stored as text, and Guid keys are generated in C#. Both are set up in `AppDbContext.ApplyConventions`.
- On start-up `DbInitializer` creates the schema and seeds demo data.

## 4. The AI agent (Microsoft.Extensions.AI)

`Microsoft.Extensions.AI` is Microsoft's standard abstraction for LLMs (`IChatClient`). Semantic Kernel is built on it too.

```csharp
// Infrastructure/Ai/AiSetup.cs
IChatClient inner = new OpenAIClient(key, new() { Endpoint = endpoint }).GetChatClient("gemini-3.1-flash-lite").AsIChatClient();
services.AddChatClient(inner).UseFunctionInvocation();   // ← middleware that runs the tool-calling loop
```

`SupportTools` turns C# methods into tools:

```csharp
AIFunctionFactory.Create(GetRecentBillsAsync, name: "get_recent_bills", description: "...")
```

The library generates a JSON schema from the method parameters and their `[Description]` attributes.
When the model replies "call get_recent_bills with count=3", the middleware calls the C# method and sends the result back to the model.
It repeats this until the model writes a final answer.

**Safety design (a common interview topic):**
- A `SupportTools` instance is created **per reply** and holds the customer's account id. The model never sends an account id, so it can't read another customer's data.
- Actions that change data need `customerConfirmed=true`. The prompt tells the model to ask first, and the tool still refuses without it.
- Business rules run in the domain (`ChangePlan`). If one fails, the tool returns `NOT ALLOWED: <reason>` and the model explains it politely.

## 5. RAG: how the AI knows the policies

1. An admin saves an article. `KnowledgeService` stores it with status **Pending** and pushes its id onto a `Channel` (an in-memory queue).
2. `KnowledgeIndexingWorker` (a `BackgroundService`) reads the queue. `TextChunker` splits the article into ~800-character overlapping chunks, `gemini-embedding-001` turns each chunk into a 768-number vector (the model can return a requested vector size), and the chunks are saved to `KnowledgeChunks` (a `vector(768)` column with an HNSW index).
3. At question time `KnowledgeSearch` runs two searches:
   - **vector**: `ORDER BY embedding <=> query_vector`, which finds similar meaning
   - **keyword**: PostgreSQL full-text search (`tsvector @@ tsquery`), which finds exact words like "eSIM"
4. **Reciprocal Rank Fusion** merges the two ranked lists. The top chunks go back to the model, and the article titles are shown as sources.

If the embedding service is down, the vector search is skipped and keyword search still works.

## 6. Real-time with SignalR

SignalR is ASP.NET Core's WebSocket library, similar to Socket.IO.

- `SupportHub` holds the methods the browser calls: `SendCustomerMessage`, `SendAgentMessage`, `JoinConversation`.
- Clients join **groups**: `conversation:{id}` and `agents`.
- Application code doesn't depend on SignalR. It calls `ISupportNotifier`, and `SignalRSupportNotifier` (in Api) implements it with `IHubContext`.
- Browsers can't set headers on WebSockets, so the JWT goes in `?access_token=`. `Program.cs` reads it in `OnMessageReceived`.

## 7. Auth

- `POST /api/auth/login` checks the password with ASP.NET Core's `PasswordHasher` (PBKDF2) and returns a JWT.
- `[Authorize(Roles = "Agent,Admin")]` on controllers and hub methods handles role-based access.
- `MapInboundClaims = false` keeps claim names short (`sub`, `role`, `name`).

## 8. Errors

Business errors are exceptions (`DomainException`, `NotFoundException`, `ForbiddenException`).
`ApiExceptionHandler` turns them into **RFC 7807 ProblemDetails** with status 400/404/403.
In the hub they become `HubException`, so the Angular app can show the message.

## 9. Useful commands

```bash
dotnet build                                   # compile
dotnet test                                    # run unit tests
dotnet run --project src/SmartSupport.Api      # start API on :5080
dotnet ef migrations add Initial -p src/SmartSupport.Infrastructure -s src/SmartSupport.Api   # create a migration
```

## 10. Interview cheat sheet

- **Why Clean Architecture?** Testable business rules, and providers (DB, LLM) can be swapped.
- **Scoped vs singleton?** DbContext is scoped because it isn't thread-safe. A singleton that needs it creates a scope.
- **How do you stop the AI doing something wrong?** Tools are bound to the current customer, actions need confirmation, rules are enforced in code, and the model only sees tool results.
- **Why hybrid search?** Embeddings miss exact codes and product names, and keywords miss paraphrases. RRF combines both without tuning weights.
- **How does streaming work?** `GetStreamingResponseAsync` returns an `IAsyncEnumerable`, and each text piece is pushed with SignalR to the conversation group.
- **Why background workers?** Embedding is slow and can fail. The API returns immediately and the article status shows the progress.
