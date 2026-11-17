using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SmartSupport.UnitTests;

/// <summary>
/// Runs the real Microsoft.Extensions.AI function-calling pipeline against a fake model.
/// Catches package version mismatches (they only fail at runtime) and proves the tool loop works.
/// </summary>
public class FunctionCallingPipelineTests
{
    /// <summary>First call: asks for a tool. Second call: answers using the tool result.</summary>
    private sealed class FakeModel : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var toolResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().FirstOrDefault();
            return Task.FromResult(toolResult is null
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_bill", new Dictionary<string, object?>())]))
                : new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Your bill is {toolResult.Result}.")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Tool_is_invoked_and_its_result_reaches_the_final_answer()
    {
        var model = new FakeModel();
        var toolCalled = false;
        var client = new ChatClientBuilder(model).UseFunctionInvocation().Build();
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() => { toolCalled = true; return "QAR 100"; }, name: "get_bill", description: "Get the bill")]
        };

        var text = "";
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "What is my bill?")], options))
            text += update.Text;

        Assert.True(toolCalled);
        Assert.Equal(2, model.Calls);
        Assert.Contains("QAR 100", text);
    }
}
