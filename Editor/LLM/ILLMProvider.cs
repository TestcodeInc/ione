using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ione.LLM
{
    public interface ILLMProvider
    {
        string DisplayName { get; }

        Task<LLMResponse> SendAsync(
            string systemPrompt,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken ct);
    }
}
