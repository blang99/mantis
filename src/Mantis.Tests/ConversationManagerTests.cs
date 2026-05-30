using Mantis.Plugin.AI;
using Xunit;

namespace Mantis.Tests;

public class ConversationManagerTests
{
    [Fact]
    public void AddMessages_AppearsInHistory()
    {
        var mgr = new ConversationManager();
        mgr.AddUserMessage("Hello");
        mgr.AddAssistantMessage("Hi there");

        Assert.Equal(2, mgr.History.Count);
        Assert.Equal("user", mgr.History[0].Role);
        Assert.Equal("Hello", mgr.History[0].Content);
        Assert.Equal("assistant", mgr.History[1].Role);
    }

    [Fact]
    public void Trim_ExceedsMaxMessages_RemovesOldest()
    {
        var mgr = new ConversationManager();

        for (int i = 0; i < 25; i++)
            mgr.AddUserMessage($"Message {i}");

        Assert.True(mgr.History.Count <= 20);
        Assert.Contains("Message 24", mgr.History.Last().Content);
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        var mgr = new ConversationManager();
        mgr.AddUserMessage("Hello");
        mgr.AddAssistantMessage("Hi");
        mgr.Clear();

        Assert.Empty(mgr.History);
    }

    [Fact]
    public void GetMessagesForApi_ReturnsCopy()
    {
        var mgr = new ConversationManager();
        mgr.AddUserMessage("Test");

        var messages = mgr.GetMessagesForApi();
        messages.Clear();

        Assert.Single(mgr.History);
    }
}
