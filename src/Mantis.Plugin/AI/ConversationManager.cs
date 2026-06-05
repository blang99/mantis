namespace Mantis.Plugin.AI;

public class ConversationManager
{
    private readonly List<ChatMessage> _history = new();
    private const int MaxHistoryMessages = 20;
    private const int MaxTotalChars = 50_000;

    public IReadOnlyList<ChatMessage> History => _history;

    public void AddUserMessage(string content)
    {
        _history.Add(ChatMessage.User(content));
        Trim();
    }

    /// <summary>
    /// Add a user turn that may carry reference images (Phase 1: image-to-script).
    /// Falls back to a plain text turn when no images are attached, so the common
    /// path is unchanged.
    /// </summary>
    public void AddUserMessage(string content, List<ImageData>? images)
    {
        _history.Add(images is { Count: > 0 }
            ? ChatMessage.User(content, images)
            : ChatMessage.User(content));
        Trim();
    }

    public void AddAssistantMessage(string content)
    {
        _history.Add(ChatMessage.Assistant(content));
        Trim();
    }

    public List<ChatMessage> GetMessagesForApi() => new(_history);

    public void Clear() => _history.Clear();

    private void Trim()
    {
        while (_history.Count > MaxHistoryMessages)
            _history.RemoveAt(0);

        var totalChars = _history.Sum(m => m.Content.Length);
        while (totalChars > MaxTotalChars && _history.Count > 2)
        {
            totalChars -= _history[0].Content.Length;
            _history.RemoveAt(0);
        }
    }
}
