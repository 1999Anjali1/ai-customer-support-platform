using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json.Nodes;

namespace SmartSupport.Infrastructure.Ai;

/// <summary>
/// Makes Gemini 3 tool calling work through Google's OpenAI-compatible endpoint.
///
/// Gemini 3 attaches a "thought signature" to every tool call (tool_calls[].extra_content.google.thought_signature)
/// and expects it back in the next request. Standard OpenAI clients drop that non-standard field, so the second
/// request of a tool loop fails with HTTP 400. Google documents the sentinel value
/// "skip_thought_signature_validator" for this case: this policy adds it to any tool call that has no signature.
/// Only registered for Gemini endpoints.
/// </summary>
public sealed class GeminiThoughtSignaturePolicy : PipelinePolicy
{
    public const string SkipValidator = "skip_thought_signature_validator";

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Patch(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        Patch(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    public static bool IsGeminiEndpoint(string endpoint) =>
        endpoint.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
        endpoint.Contains("aiplatform.googleapis.com", StringComparison.OrdinalIgnoreCase);

    private static void Patch(PipelineMessage message)
    {
        var content = message.Request.Content;
        if (content is null) return;

        using var buffer = new MemoryStream();
        content.WriteTo(buffer);

        var patched = AddMissingSignatures(buffer.ToArray());
        if (patched is not null)
            message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(patched));
    }

    /// <summary>Returns the modified JSON body, or null when nothing had to change.</summary>
    public static byte[]? AddMissingSignatures(byte[] requestBody)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(requestBody);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (root?["messages"] is not JsonArray messages) return null;

        var changed = false;
        foreach (var message in messages)
        {
            if (message?["tool_calls"] is not JsonArray toolCalls) continue;

            foreach (var call in toolCalls)
            {
                if (call is not JsonObject toolCall || toolCall["extra_content"] is not null) continue;

                toolCall["extra_content"] = new JsonObject
                {
                    ["google"] = new JsonObject { ["thought_signature"] = SkipValidator }
                };
                changed = true;
            }
        }

        return changed ? Encoding.UTF8.GetBytes(root.ToJsonString()) : null;
    }
}
