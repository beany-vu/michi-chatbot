using System.ClientModel;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace MichiChatbot.Infrastructure.Llm;

/// <summary>
/// Builds Microsoft.Extensions.AI chat clients for the real chat path (the hand-rolled loop stays
/// behind /debug/chat as the raw-wire playground). The pipeline per client:
///   FunctionInvokingChatClient  → runs the tool loop (what HandRolledToolLoop.RunAsync did)
///   LoggingChatClient           → logs each round's request/response
///   OpenAI SDK adapter          → speaks the chat-completions wire format (ChatWireModels.cs)
/// The OpenAI SDK pins the model id at client creation, so clients are cached per model — sites
/// can use different models and each gets its own reused pipeline.
/// </summary>
public sealed class ChatClientFactory(IOptions<LlmOptions> options, ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<string, IChatClient> _byModel = new();

    public IChatClient GetForModel(string model) => _byModel.GetOrAdd(model, m =>
    {
        var llm = options.Value;
        var openAi = new OpenAIClient(
            new ApiKeyCredential(llm.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(llm.BaseUrl) });

        return openAi.GetChatClient(m)
            .AsIChatClient()
            .AsBuilder()
            // Same guardrail the hand-rolled loop had: a model that keeps calling tools forever
            // burns money — cap the rounds.
            .UseFunctionInvocation(loggerFactory, c =>
                c.MaximumIterationsPerRequest = HandRolledToolLoop.MaxRounds)
            .UseLogging(loggerFactory)
            .Build();
    });

    /// <summary>The model actually used: env/appsettings override (dev: LM Studio) wins over the site's configured model.</summary>
    public string ResolveModel(string siteModel) =>
        string.IsNullOrWhiteSpace(options.Value.Model) ? siteModel : options.Value.Model;
}
