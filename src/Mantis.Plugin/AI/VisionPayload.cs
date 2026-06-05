using System.Collections.Generic;
using System.Linq;

namespace Mantis.Plugin.AI;

/// <summary>
/// Maps a provider-agnostic <see cref="ChatMessage"/> (which may carry reference images)
/// into each LLM provider's specific wire format for its <c>content</c>/<c>parts</c> field.
///
/// Centralised here so (a) every client is one line, and (b) the per-provider shape is
/// directly unit-testable headless — no HTTP, no live model. This is the Phase 1
/// image-to-script acceptance gate: each method's output shape is asserted in tests.
///
/// All methods are pure. Text-only messages pass through unchanged (a plain string for
/// the chat providers), so non-vision behaviour is byte-identical to before.
/// </summary>
public static class VisionPayload
{
    /// <summary>
    /// OpenAI / OpenRouter chat-completions content. Text-only → the raw string.
    /// With images → an array of parts: a text part followed by one
    /// <c>{ type:"image_url", image_url:{ url:"data:&lt;mime&gt;;base64,..." } }</c> per image.
    /// </summary>
    public static object OpenAiContent(ChatMessage m)
    {
        if (!m.HasImages)
            return m.Content;

        var parts = new List<object>();
        if (!string.IsNullOrEmpty(m.Content))
            parts.Add(new { type = "text", text = m.Content });
        foreach (var img in m.Images!)
            parts.Add(new { type = "image_url", image_url = new { url = img.ToDataUrl() } });
        return parts;
    }

    /// <summary>
    /// Anthropic Messages content. Text-only → the raw string. With images → an array of
    /// blocks: each image as <c>{ type:"image", source:{ type:"base64", media_type, data } }</c>
    /// FIRST (Anthropic guidance: images before the text that refers to them), then the text.
    /// </summary>
    public static object ClaudeContent(ChatMessage m)
    {
        if (!m.HasImages)
            return m.Content;

        var blocks = new List<object>();
        foreach (var img in m.Images!)
            blocks.Add(new
            {
                type = "image",
                source = new { type = "base64", media_type = img.MimeType, data = img.Base64 }
            });
        if (!string.IsNullOrEmpty(m.Content))
            blocks.Add(new { type = "text", text = m.Content });
        return blocks;
    }

    /// <summary>
    /// Gemini <c>parts</c> array. Always returns at least one part. Text part first (kept
    /// even when empty so a part always exists), then one
    /// <c>{ inlineData:{ mimeType, data } }</c> per image.
    /// </summary>
    public static object[] GeminiParts(ChatMessage m)
    {
        var parts = new List<object> { new { text = m.Content } };
        if (m.HasImages)
            foreach (var img in m.Images!)
                parts.Add(new { inlineData = new { mimeType = img.MimeType, data = img.Base64 } });
        return parts.ToArray();
    }

    /// <summary>
    /// Ollama puts images in a top-level <c>images</c> array on the message (raw base64,
    /// no data-URL prefix) while <c>content</c> stays the text. Returns null when there are
    /// no images so the field can be omitted entirely.
    /// </summary>
    public static string[]? OllamaImages(ChatMessage m) =>
        m.HasImages ? m.Images!.Select(i => i.Base64).ToArray() : null;
}
