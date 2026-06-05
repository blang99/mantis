namespace Mantis.Plugin.AI;

/// <summary>
/// A single turn in the conversation sent to an <see cref="ILlmClient"/>. Text-only in
/// the common case; may carry reference images for the image-to-script path (Phase 1).
/// This is a provider-agnostic DTO — each client maps it into its own wire format via
/// <see cref="VisionPayload"/>.
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";

    /// <summary>
    /// Optional reference images attached to this message (Phase 1: image-to-script).
    /// Null or empty for text-only messages — the common case.
    /// </summary>
    public List<ImageData>? Images { get; set; }

    /// <summary>True when this message carries at least one reference image.</summary>
    public bool HasImages => Images is { Count: > 0 };

    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };

    /// <summary>A user message with one or more attached reference images.</summary>
    public static ChatMessage User(string content, List<ImageData> images) =>
        new() { Role = "user", Content = content, Images = images };
}

/// <summary>
/// A single reference image attached to a <see cref="ChatMessage"/>, stored as raw
/// base64 (no <c>data:</c> prefix) plus its MIME type. Vision-capable providers consume
/// these; the offline path uses Ollama vision models (llava / qwen2.5vl).
/// </summary>
public class ImageData
{
    /// <summary>Raw base64 of the image bytes — NOT a data URL (no "data:...," prefix).</summary>
    public string Base64 { get; set; } = "";

    /// <summary>MIME type, e.g. "image/png" or "image/jpeg".</summary>
    public string MimeType { get; set; } = "image/png";

    /// <summary>Convenience: a full data URL (<c>data:&lt;mime&gt;;base64,&lt;data&gt;</c>) for OpenAI-style image_url fields.</summary>
    public string ToDataUrl() => $"data:{MimeType};base64,{Base64}";

    /// <summary>Build from raw image bytes.</summary>
    public static ImageData FromBytes(byte[] bytes, string mimeType = "image/png") =>
        new() { Base64 = System.Convert.ToBase64String(bytes), MimeType = mimeType };
}
